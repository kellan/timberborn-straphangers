using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using BoardingCounter;
public static class HourlyActivityTests
{
 public static void Run(Action<bool,string> check) {
  var culture=CultureInfo.InvariantCulture;
  var c=new Counter(10);c.Register("s","tubeway","Station");c.BeginHourlyTracking("s",10,.25);
  void Record(int day,int hour,bool boarding,int count=1){for(int i=0;i<count;i++)c.Record("s","tubeway","Station",day,boarding,false,hour/24d);}
  Record(10,7,true,2);Record(10,9,false);
  var today=HourlyActivity.ForStation(c.Stations["s"],10,10/24d);
  check(today.Boardings[7]==2&&today.Alightings[9]==1&&today.CurrentHour==10,"hour bins distinguish boardings and alightings by game time");
  check(!today.Observed[5]&&today.Observed[6]&&today.Observed[10]&&!today.Observed[11],"first-day hours before tracking and future hours remain unobserved rather than fake zeroes");
  check(today.PartialToday&&!today.IsAverage&&today.Period=="Today (partial)","hour strip immediately shows today's partial collection window");
  check(today.HeightPercent(7)==100&&today.HeightPercent(9)==50&&today.HeightPercent(0)==0,"native bar heights scale combined activity to the busiest hour");
  check(today.Tooltip(7,culture).Contains("07:00–08:00")&&today.Tooltip(7,culture).Contains("Boardings: 2")&&today.Tooltip(9,culture).Contains("Alightings: 1"),"hour hover text identifies its time interval and separate directions");
  check(today.Tooltip(5,culture).Contains("Before hourly tracking")&&today.Tooltip(11,culture).Contains("Later today"),"hour tooltips distinguish missing observation from future hours");
  check(HourlyActivity.HourOfDay((float)(7/24d))==7&&HourlyActivity.HourOfDay((float)(23/24d))==23&&HourlyActivity.HourOfDay(1)==23,"native float hour boundaries map correctly, including the end-of-day clamp");
  Record(11,7,true,4);Record(11,7,false,2);Record(11,9,false);
  var snapshot=CounterPersistence.Capture(c,new[]{"s"},11);
  Record(13,5,true,10);Record(13,5,false,2); // Day 12 intentionally has no traffic.
  var profile=HourlyActivity.ForStation(c.Stations["s"],13,.5);
  check(profile.IsAverage&&profile.CompleteDays==2&&profile.Boardings[7]==2&&profile.Alightings[7]==1&&profile.Alightings[9]==.5,"hourly average includes a zero-traffic complete day and excludes the first partial day");
  check(profile.Boardings[5]==0&&profile.Observed.All(x=>x),"hourly average excludes today's traffic and treats all hours of complete days as observed");
  check(c.Average("s",13)==2&&c.AverageAlightings("s",13)==1.5,"directional daily averages use the same complete-day window");
  var view=RidershipPresentation.ForStation(c,"s",13,culture);
  check(view.Total=="16"&&view.TotalAlightings=="6"&&view.Average=="2.0"&&view.AverageAlightings=="1.5","compact table presents separate totals and averages without combining trip ends");
  var saved=CounterPersistence.Capture(c,new[]{"s"},13);
  var restored=CounterPersistence.Restore(JsonSerializer.Deserialize<CounterState>(JsonSerializer.Serialize(saved))!,13);
  var restoredProfile=HourlyActivity.ForStation(restored.Stations["s"],13,.5);
  check(saved.schema==2&&restored.Stations["s"].HourlyFirstDay==10&&restoredProfile.Boardings[7]==2&&restoredProfile.Alightings[9]==.5,"new save schema round-trips hourly coverage, both arrays and the activity profile");
  var older=CounterPersistence.Restore(snapshot,11);
  check(older.Stations["s"].TotalBoardings==6&&older.Stations["s"].HourlyDays.Count==2&&!older.Stations["s"].HourlyDays.ContainsKey(13),"reloading an older snapshot restores its hourly timeline without leaking later events");
  c.Record("s","tubeway","Station",13,true,false,.5);
  check(saved.stations[0].hourly_days.Single(h=>h.day==13).boardings[12]==0,"hourly save arrays are detached copies of the running counter");
  restored.Stations["s"].HourlyDays[13].Boardings[12]=8;
  check(saved.stations[0].hourly_days.Single(h=>h.day==13).boardings[12]==0,"restored hour arrays do not alias the loaded snapshot");
  string legacy="{\"schema\":1,\"first_day\":10,\"stations\":[{\"id\":\"old\",\"mode\":\"tubeway\",\"name\":\"Old station\",\"first_day\":10,\"entries\":100,\"exits\":80,\"bot_entries\":5,\"days\":[{\"day\":11,\"entries\":30,\"exits\":10},{\"day\":12,\"entries\":20,\"exits\":30},{\"day\":13,\"entries\":10,\"exits\":10}]}]}";
  var migrated=CounterPersistence.Restore(JsonSerializer.Deserialize<CounterState>(legacy)!,13);
  check(migrated.Stations["old"].TotalBoardings==100&&migrated.Stations["old"].TotalArrivals==80&&migrated.Average("old",13)==25&&migrated.AverageAlightings("old",13)==20,"schema-one saves preserve lifetime totals and recover both daily averages");
  check(!HourlyActivity.ForStation(migrated.Stations["old"],13,.5).Started,"an old save never fabricates an hourly distribution from daily totals");
  migrated.BeginHourlyTracking("old",13,.5);migrated.BeginHourlyTracking("old",13,.7);
  migrated.Record("old","tubeway","Old station",13,true,false,.75);migrated.Record("old","tubeway","Old station",13,false,false,.75);
  var migratedProfile=HourlyActivity.ForStation(migrated.Stations["old"],13,.8);
  check(migrated.Stations["old"].TotalBoardings==101&&migrated.Stations["old"].TotalArrivals==81&&migrated.Stations["old"].HourlyFirstProgress==.5&&migratedProfile.Boardings[18]==1&&migratedProfile.Alightings[18]==1,"migration starts fresh hourly collection once without resetting or reallocating existing counts");
  var quiet=HourlyActivity.ForStation(migrated.Stations["old"],15,.3);
  check(quiet.IsAverage&&quiet.CompleteDays==1&&quiet.Peak==0&&quiet.Observed.All(x=>x)&&quiet.HeightPercent(7)==0,"a complete quiet day yields known zero hourly activity without divide-by-zero");
  var future=HourlyActivity.ForStation(migrated.Stations["old"],14,.3);
  check(!future.IsAverage&&!future.PartialToday&&future.Observed[0],"the next day's fallback observes zero-traffic hours from midnight");
  var bounded=new Counter(1);
  for(int day=1;day<=500;day++){bounded.Record("s","zipline","Station",day,true,false,(day%24)/24d);bounded.Record("s","zipline","Station",day,false,false,(day%24)/24d);}
  var boundedState=CounterPersistence.Capture(bounded,new[]{"s"},500);
  check(bounded.Stations["s"].HourlyDays.Count==8&&bounded.Stations["s"].Days.Count==8&&boundedState.stations[0].hourly_days.Length==8,"500 simulated days retain exactly seven previous days plus today, without relying on a later flush");
  check(bounded.Stations["s"].TotalBoardings==500&&bounded.Stations["s"].TotalArrivals==500&&boundedState.stations[0].hourly_days.All(h=>h.boardings.Length==24&&h.alightings.Length==24),"bounded hourly history preserves lifetime counts and fixed-size directional bins");
  var replay=new Counter(1,boundHistory:false);for(int day=1;day<=12;day++)replay.Record("s","tubeway","S",day,true,false);
  check(replay.Stations["s"].Days.Count==12,"bounded saved history does not truncate the separate replay counter's complete capture window");
  bounded.Register("unfinished","tubeway","Unfinished",500);bounded.Register("demolished","tubeway","Demolished",500);bounded.RetainStations(new[]{"s","unfinished"});
  check(!bounded.Stations.ContainsKey("demolished")&&bounded.Stations.ContainsKey("unfinished"),"station refresh can prune demolished data while retaining unfinished existing stations");
  var invalidClock=new Counter(1);bool rejected=false;try{invalidClock.Record("bad","tubeway","Bad",1,true,false,double.NaN);}catch(ArgumentOutOfRangeException){rejected=true;}
  check(rejected&&invalidClock.Stations.Count==0,"an invalid native time cannot partially mutate ridership counts");
  void Invalid(Action<SavedStationCount> change,string label) {
   var sample=new Counter(1);sample.Record("a","tubeway","A",1,true,false,0);sample.Record("a","tubeway","A",1,false,false,0);
   var state=CounterPersistence.Capture(sample,new[]{"a"},2);change(state.stations[0]);
   try{CounterPersistence.Restore(state,2);check(false,label);}catch(InvalidDataException){check(true,label);}
  }
  Invalid(s=>s.hourly_days[0].boardings=new long[23],"malformed hourly array lengths are rejected");
  Invalid(s=>s.hourly_days[0].alightings[0]=-1,"negative alighting bins are rejected");
  Invalid(s=>s.hourly_days[0].boardings[1]=100,"hourly boardings cannot exceed their daily total");
  Invalid(s=>s.hourly_days[0].alightings[1]=100,"hourly alightings cannot exceed their daily total");
  Invalid(s=>s.hourly_first_day=null,"hourly data without a coverage baseline is rejected");
  Invalid(s=>s.hourly_first_progress=double.NaN,"non-finite hourly coverage time is rejected");
  Invalid(s=>s.hourly_first_day=3,"future hourly coverage dates are rejected");
  Invalid(s=>s.hourly_days[0].day=3,"future hourly buckets are rejected");
  Invalid(s=>s.hourly_days=new[]{s.hourly_days[0],s.hourly_days[0]},"duplicate saved hourly days are rejected");
  Invalid(s=>s.hourly_days=Enumerable.Repeat(s.hourly_days[0],9).ToArray(),"oversized hourly history is rejected");
  Invalid(s=>s.hourly_first_progress=.5,"counts before the recorded hourly start are rejected");
  Invalid(s=>{s.hourly_days[0].boardings[0]=long.MaxValue;s.hourly_days[0].boardings[1]=long.MaxValue;},"overflowing hourly sums are rejected");
 }
}
