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
}
public sealed class SavedDailyCount
{
 public int day {get;set;}
 public long entries {get;set;}
 public long exits {get;set;}
}
public static class CounterPersistence
{
 public static CounterState Capture(Counter counter,IEnumerable<string> activeStations,int today) {
  var ids=new HashSet<string>(activeStations);
  return new CounterState{schema=1,first_day=counter.FirstDay,stations=counter.Stations.Where(p=>ids.Contains(p.Key)).OrderBy(p=>p.Key,StringComparer.Ordinal).Select(p=>new SavedStationCount{id=p.Key,mode=p.Value.Mode,name=p.Value.Name,first_day=p.Value.FirstDay,entries=p.Value.TotalBoardings,exits=p.Value.TotalArrivals,bot_entries=p.Value.BotBoardings,days=p.Value.Days.Where(d=>d.Key>=today-7).OrderBy(d=>d.Key).Select(d=>new SavedDailyCount{day=d.Key,entries=d.Value.Boardings,exits=d.Value.Arrivals}).ToArray()}).ToArray()};
 }
 public static Counter Restore(CounterState state,int today) {
  if(state==null||state.schema!=1||state.first_day<0||state.first_day>today||state.stations==null)throw new InvalidDataException("Unsupported ridership save or invalid starting day");
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
   result.Stations.Add(saved.id,station);
  }
  result.Trim(today);return result;
 }
}
