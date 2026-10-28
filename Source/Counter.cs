using System;
using System.Collections.Generic;
using System.Linq;
namespace BoardingCounter;
// Pure counting logic: the first (possibly partial) day is excluded from averages.
public sealed class Counter
{
 public int FirstDay { get; }
 public readonly Dictionary<string, StationCount> Stations = new Dictionary<string, StationCount>();
 public Counter(int firstDay) { FirstDay = firstDay; }
 public void Register(string id, string mode, string name, int? firstDay = null) {
  if (!Stations.ContainsKey(id)) Stations[id] = new StationCount(mode, name, firstDay ?? FirstDay);
 }
 public void Record(string id, string mode, string name, int day, bool boarding, bool bot) {
  Register(id, mode, name, day);
  var s = Stations[id];
  if (!s.Days.TryGetValue(day, out var d)) s.Days[day] = d = new DailyCount();
  if (boarding) { d.Boardings++; s.TotalBoardings++; if (bot) s.BotBoardings++; }
  else { d.Arrivals++; s.TotalArrivals++; }
 }
 public int ObservedFullDays(int today) => Math.Min(7, Math.Max(0, today - FirstDay - 1));
 public int ObservedFullDays(string id, int today) => Math.Min(7, Math.Max(0, today - Stations[id].FirstDay - 1));
 public double? Average(string id, int today) {
  int n = ObservedFullDays(id, today);
  if (n == 0) return null;
  var s = Stations[id];
  return Enumerable.Range(today - n, n).Sum(day => s.Days.TryGetValue(day, out var d) ? d.Boardings : 0L) / (double)n;
 }
 public void Trim(int today) { foreach(var s in Stations.Values) foreach(var day in s.Days.Keys.Where(d => d < today - 7).ToArray()) s.Days.Remove(day); }
}
public sealed class StationCount
{
 public readonly string Mode, Name;
 public readonly int FirstDay;
 public long TotalBoardings, TotalArrivals, BotBoardings;
 public readonly Dictionary<int, DailyCount> Days = new Dictionary<int, DailyCount>();
 public StationCount(string mode, string name, int firstDay) { Mode=mode; Name=name; FirstDay=firstDay; }
}
public sealed class DailyCount { public long Boardings, Arrivals; }
