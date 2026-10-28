using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using BoardingCounter;
public static class PersistenceTests
{
 public static void Run(Action<bool,string> check) {
  Counter Reload(Counter source,int day,string[]? ids=null) {
   string json=JsonSerializer.Serialize(CounterPersistence.Capture(source,ids??source.Stations.Keys.ToArray(),day));
   return CounterPersistence.Restore(JsonSerializer.Deserialize<CounterState>(json)!,day);
  }
  void Invalid(Action<CounterState> alter,string label) {
   var c=new Counter(10);c.Record("a","tubeway","A",11,true,false);
   var state=CounterPersistence.Capture(c,new[]{"a"},12);alter(state);
   try{CounterPersistence.Restore(state,12);check(false,label);}catch(InvalidDataException){check(true,label);}
  }
  var original=new Counter(10);original.Register("a","tubeway","A");original.Register("b","zipline","B");
  original.Record("a","tubeway","A",10,true,false);
  original.Record("a","tubeway","A",11,true,true);original.Record("a","tubeway","A",11,false,true);
  original.Record("a","tubeway","A",13,true,false);original.Record("b","zipline","B",12,true,false);
  var restored=Reload(original,13);
  check(restored.FirstDay==10&&restored.Stations["a"].FirstDay==10,"save/load preserves the original tracking dates");
  check(restored.Stations["a"].TotalBoardings==3&&restored.Stations["a"].TotalArrivals==1&&restored.Stations["a"].BotBoardings==1,"save/load preserves entries, exits and bot totals exactly");
  check(restored.Average("a",13)==.5&&restored.ObservedFullDays("a",13)==2,"average includes zero-traffic days and excludes both partial days after reload");
  check(restored.Stations["b"].Mode=="zipline"&&restored.Stations["b"].TotalBoardings==1,"each station and transport mode retains its own saved count");
  restored.Record("a","tubeway","A",13,true,false);
  var next=Reload(restored,13);
  check(next.Stations["a"].TotalBoardings==4&&next.Stations["a"].Days[13].Boardings==2,"new boardings continue after reload without resetting the current daily bucket");
  var older=Reload(original,13);check(older.Stations["a"].TotalBoardings==3,"loading an earlier snapshot rolls totals back with the game instead of merging a later timeline");
  var other=Reload(new Counter(50),50);check(other.Stations.Count==0&&other.FirstDay==50,"another settlement cannot inherit the previous settlement's stations");
  var clone=CounterPersistence.Capture(original,new[]{"a","b"},13);original.Record("a","tubeway","A",13,true,false);
  check(clone.stations.Single(s=>s.id=="a").entries==3&&clone.stations.Single(s=>s.id=="a").days.Single(d=>d.day==13).entries==1,"save snapshots are detached from later live count changes");
  var pruned=Reload(restored,13,new[]{"b"});check(!pruned.Stations.ContainsKey("a")&&pruned.Stations["b"].TotalBoardings==1,"saving drops demolished station IDs without changing surviving stations");
  restored.Register("new","tubeway","New",13);var withNew=Reload(restored,13);
  check(withNew.Average("new",14)==null&&withNew.Average("new",15)==0,"a newly built station gets its own partial first day and zero-traffic average");
  var longRun=new Counter(1);for(int d=1;d<=30;d++)longRun.Record("a","tubeway","A",d,true,false);
  longRun.Trim(30);var longReload=Reload(longRun,30);
  check(longReload.Stations["a"].TotalBoardings==30&&longReload.Stations["a"].Days.Count<=8&&longReload.Average("a",30)==1,"bounded daily history preserves lifetime totals and the rolling seven-day average");
  var again=Reload(longReload,30);check(again.Stations["a"].TotalBoardings==30,"repeated save/load cycles never accumulate the same snapshot twice");
  var gate=new EntranceGate("a","A","tubeway",new Point3(0,0,0),new Point3(1,0,0));
  var tracker=new EntranceTracker(new Point3(1,0,0),new[]{gate});var hits=new System.Collections.Generic.List<EntranceCrossing>();tracker.Observe(new Point3(-1,0,0),hits,true);
  check(hits.Count==1&&!hits[0].Boarding,"a passenger loaded inside a station exits without a synthetic new boarding");
  var view=RidershipPresentation.ForStation(next,"a",13,CultureInfo.InvariantCulture);
  check(view.Total=="4"&&view.Average=="0.5"&&view.Period.Contains("2 complete days")&&view.Since.Contains("day 10"),"station display uses saved totals and the same daily average as the counter");
  var pending=RidershipPresentation.ForStation(withNew,"new",14,CultureInfo.InvariantCulture);
  check(pending.Average=="—"&&pending.Period.Contains("after one complete day"),"station display explains why a new average is not available yet");
  var quiet=RidershipPresentation.ForStation(withNew,"new",15,CultureInfo.InvariantCulture);
  check(quiet.Average=="0.0"&&quiet.Period.Contains("1 complete day"),"station display distinguishes zero traffic from insufficient history");
  Invalid(s=>s.schema=99,"unsupported future save schemas fail explicitly");
  Invalid(s=>s.schema=0,"a missing save schema cannot silently reset counts");
  Invalid(s=>s.stations[0].entries=-1,"negative saved totals are rejected");
  Invalid(s=>s.stations[0].days[0].day=13,"future daily buckets are rejected");
  Invalid(s=>s.stations=new[]{s.stations[0],s.stations[0]},"duplicate saved station IDs are rejected");
  Invalid(s=>s.stations[0].days=new[]{s.stations[0].days[0],s.stations[0].days[0]},"duplicate daily buckets are rejected");
  Invalid(s=>s.stations[0].entries=0,"daily ridership cannot exceed its saved lifetime total");
  Invalid(s=>s.stations[0].bot_entries=2,"saved bot boardings cannot exceed total boardings");
 }
}
