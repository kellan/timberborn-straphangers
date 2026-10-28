using System;
using System.Collections.Generic;
using System.Linq;
namespace BoardingCounter;
// First partial days and the current day are excluded from daily averages.
public sealed class Counter
{
 public const int HistoryDays=7;
 public int FirstDay { get; }
 readonly bool boundHistory;
 public readonly Dictionary<string, StationCount> Stations = new Dictionary<string, StationCount>();
 public Counter(int firstDay,bool boundHistory=true) { FirstDay = firstDay;this.boundHistory=boundHistory; }
 public void Register(string id, string mode, string name, int? firstDay = null) {
  if (!Stations.ContainsKey(id)) Stations[id] = new StationCount(mode, name, firstDay ?? FirstDay);
 }
 public void BeginHourlyTracking(string id,int day,double progress) {
  HourlyActivity.HourOfDay(progress); // Validate the native clock before changing state.
  var s=Stations[id];
  if(s.HourlyFirstDay.HasValue)return;
  s.HourlyFirstDay=day;s.HourlyFirstProgress=progress;
 }
 public void Record(string id, string mode, string name, int day, bool boarding, bool bot,double? progress=null) {
  int hour=progress.HasValue?HourlyActivity.HourOfDay(progress.Value):-1;
  Register(id, mode, name, day);
  var s = Stations[id];if(boundHistory)TrimStation(s,day);
  if (!s.Days.TryGetValue(day, out var d)) s.Days[day] = d = new DailyCount();
  if (boarding) { d.Boardings++; s.TotalBoardings++; if (bot) s.BotBoardings++; }
  else { d.Arrivals++; s.TotalArrivals++; }
  // Replay-only counters need daily totals, but need not allocate hourly history.
  if(hour>=0) {
   BeginHourlyTracking(id,day,progress!.Value);
   if(!s.HourlyDays.TryGetValue(day,out var hours))s.HourlyDays[day]=hours=new HourlyCount();
   if(boarding)hours.Boardings[hour]++;else hours.Alightings[hour]++;
  }
 }
 public int ObservedFullDays(int today) => Math.Min(HistoryDays, Math.Max(0, today - FirstDay - 1));
 public int ObservedFullDays(string id, int today) => Math.Min(HistoryDays, Math.Max(0, today - Stations[id].FirstDay - 1));
 public double? Average(string id, int today)=>AverageDirection(id,today,true);
 public double? AverageAlightings(string id,int today)=>AverageDirection(id,today,false);
 double? AverageDirection(string id,int today,bool boarding) {
  int n = ObservedFullDays(id, today);
  if (n == 0) return null;
  var s = Stations[id];
  return Enumerable.Range(today - n, n).Sum(day => s.Days.TryGetValue(day, out var d) ? (boarding?d.Boardings:d.Arrivals) : 0L) / (double)n;
 }
 public void Trim(int today) {foreach(var s in Stations.Values)TrimStation(s,today);}
 static void TrimStation(StationCount s,int today) {
  if(s.LastTrimDay==today)return;
  foreach(var day in s.Days.Keys.Where(d=>d<today-HistoryDays).ToArray())s.Days.Remove(day);
  foreach(var day in s.HourlyDays.Keys.Where(d=>d<today-HistoryDays).ToArray())s.HourlyDays.Remove(day);
  s.LastTrimDay=today;
 }
 public void RetainStations(IEnumerable<string> activeIds) {
  var keep=new HashSet<string>(activeIds);
  foreach(var id in Stations.Keys.Where(id=>!keep.Contains(id)).ToArray())Stations.Remove(id);
 }
}
public sealed class StationCount
{
 public readonly string Mode, Name;
 public readonly int FirstDay;
 public long TotalBoardings, TotalArrivals, BotBoardings;
 public readonly Dictionary<int, DailyCount> Days = new Dictionary<int, DailyCount>();
 public int? HourlyFirstDay;
 public double HourlyFirstProgress;
 public readonly Dictionary<int,HourlyCount> HourlyDays=new Dictionary<int,HourlyCount>();
 internal int LastTrimDay=int.MinValue;
 public StationCount(string mode, string name, int firstDay) { Mode=mode; Name=name; FirstDay=firstDay; }
}
public sealed class DailyCount { public long Boardings, Arrivals; }
public sealed class HourlyCount
{
 public readonly long[] Boardings=new long[24];
 public readonly long[] Alightings=new long[24];
}
