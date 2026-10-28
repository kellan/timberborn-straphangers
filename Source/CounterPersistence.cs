using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
namespace BoardingCounter;

// Save snapshots contain values, not references into the running counter.
public sealed class CounterState
{
 public int schema {get;set;}
 public int first_day {get;set;}
 public SavedStationCount[] stations {get;set;}=Array.Empty<SavedStationCount>();
}
public sealed class SavedStationCount
{
 public string id {get;set;}="";
 public string mode {get;set;}="";
 public string name {get;set;}="";
 public int first_day {get;set;}
 public long entries {get;set;}
 public long exits {get;set;}
 public long bot_entries {get;set;}
 public SavedDailyCount[] days {get;set;}=Array.Empty<SavedDailyCount>();
 public int? hourly_first_day {get;set;}
 public double hourly_first_progress {get;set;}
 public SavedHourlyCount[] hourly_days {get;set;}=Array.Empty<SavedHourlyCount>();
}
public sealed class SavedDailyCount
{
 public int day {get;set;}
 public long entries {get;set;}
 public long exits {get;set;}
}
public sealed class SavedHourlyCount
{
 public int day {get;set;}
 public long[] boardings {get;set;}=Array.Empty<long>();
 public long[] alightings {get;set;}=Array.Empty<long>();
}
public static class CounterPersistence
{
 public static CounterState Capture(Counter counter,IEnumerable<string> activeStations,int today) {
  var ids=new HashSet<string>(activeStations);
  return new CounterState{schema=2,first_day=counter.FirstDay,stations=counter.Stations.Where(p=>ids.Contains(p.Key)).OrderBy(p=>p.Key,StringComparer.Ordinal).Select(p=>new SavedStationCount{id=p.Key,mode=p.Value.Mode,name=p.Value.Name,first_day=p.Value.FirstDay,entries=p.Value.TotalBoardings,exits=p.Value.TotalArrivals,bot_entries=p.Value.BotBoardings,days=p.Value.Days.Where(d=>d.Key>=today-Counter.HistoryDays).OrderBy(d=>d.Key).Select(d=>new SavedDailyCount{day=d.Key,entries=d.Value.Boardings,exits=d.Value.Arrivals}).ToArray(),hourly_first_day=p.Value.HourlyFirstDay,hourly_first_progress=p.Value.HourlyFirstProgress,hourly_days=p.Value.HourlyDays.Where(d=>d.Key>=today-Counter.HistoryDays).OrderBy(d=>d.Key).Select(d=>new SavedHourlyCount{day=d.Key,boardings=(long[])d.Value.Boardings.Clone(),alightings=(long[])d.Value.Alightings.Clone()}).ToArray()}).ToArray()};
 }
 public static Counter Restore(CounterState state,int today) {
  if(state==null||(state.schema!=1&&state.schema!=2)||state.first_day<0||state.first_day>today||state.stations==null)throw new InvalidDataException("Unsupported ridership save or invalid starting day");
  var result=new Counter(state.first_day);
  foreach(var saved in state.stations) {
   if(saved==null||string.IsNullOrWhiteSpace(saved.id)||result.Stations.ContainsKey(saved.id)||!(saved.mode=="tubeway"||saved.mode=="zipline")||saved.name==null||saved.first_day<state.first_day||saved.first_day>today||saved.entries<0||saved.exits<0||saved.bot_entries<0||saved.bot_entries>saved.entries||saved.days==null)throw new InvalidDataException("Invalid saved station ridership");
   var station=new StationCount(saved.mode,saved.name,saved.first_day){TotalBoardings=saved.entries,TotalArrivals=saved.exits,BotBoardings=saved.bot_entries};
   long entries=0,exits=0;
   foreach(var d in saved.days) {
    if(d==null||d.day<saved.first_day||d.day>today||d.entries<0||d.exits<0||station.Days.ContainsKey(d.day))throw new InvalidDataException("Invalid saved daily ridership");
    try{entries=checked(entries+d.entries);exits=checked(exits+d.exits);}catch(OverflowException){throw new InvalidDataException("Saved daily ridership overflow");}
    station.Days.Add(d.day,new DailyCount{Boardings=d.entries,Arrivals=d.exits});
   }
   if(entries>saved.entries||exits>saved.exits)throw new InvalidDataException("Daily ridership exceeds saved totals");
   if(state.schema==2)RestoreHourly(saved,station,today);
   result.Stations.Add(saved.id,station);
  }
  result.Trim(today);return result;
 }
 static void RestoreHourly(SavedStationCount saved,StationCount station,int today) {
  if(saved.hourly_days==null||saved.hourly_days.Length>Counter.HistoryDays+1)throw new InvalidDataException("Invalid hourly history length");
  if(!saved.hourly_first_day.HasValue) {
   if(saved.hourly_days.Length>0)throw new InvalidDataException("Hourly data has no tracking baseline");
   return;
  }
  int first=saved.hourly_first_day.Value;
  if(first<station.FirstDay||first>today)throw new InvalidDataException("Invalid hourly tracking day");
  try{HourlyActivity.HourOfDay(saved.hourly_first_progress);}catch(ArgumentOutOfRangeException){throw new InvalidDataException("Invalid hourly tracking progress");}
  station.HourlyFirstDay=first;station.HourlyFirstProgress=saved.hourly_first_progress;
  foreach(var h in saved.hourly_days) {
   if(h==null||h.day<first||h.day<today-Counter.HistoryDays||h.day>today||h.boardings==null||h.alightings==null||h.boardings.Length!=24||h.alightings.Length!=24||station.HourlyDays.ContainsKey(h.day))throw new InvalidDataException("Invalid saved hourly buckets");
   long entries=0,exits=0;
   for(int i=0;i<24;i++) {
    if(h.boardings[i]<0||h.alightings[i]<0)throw new InvalidDataException("Negative hourly count");
    if(h.day==first&&i<HourlyActivity.HourOfDay(saved.hourly_first_progress)&&(h.boardings[i]!=0||h.alightings[i]!=0))throw new InvalidDataException("Hourly count predates its tracking baseline");
    try{entries=checked(entries+h.boardings[i]);exits=checked(exits+h.alightings[i]);}catch(OverflowException){throw new InvalidDataException("Hourly count overflow");}
   }
   station.Days.TryGetValue(h.day,out var daily);
   if(entries>(daily?.Boardings??0)||exits>(daily?.Arrivals??0))throw new InvalidDataException("Hourly counts exceed daily totals");
   var counts=new HourlyCount();Array.Copy(h.boardings,counts.Boardings,24);Array.Copy(h.alightings,counts.Alightings,24);station.HourlyDays.Add(h.day,counts);
  }
 }

}
