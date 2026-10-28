using System;
using System.Globalization;
namespace BoardingCounter;

// An hour-of-day profile, not a percentage of a station's capacity.
public sealed class HourlyActivity
{
 public readonly double[] Boardings=new double[24];
 public readonly double[] Alightings=new double[24];
 public readonly bool[] Observed=new bool[24];
 public int CurrentHour {get;private set;}
 public int CompleteDays {get;private set;}
 public bool Started {get;private set;}
 public bool IsAverage=>CompleteDays>0;
 public bool PartialToday {get;private set;}
 public int StartHour {get;private set;}
 public string Period=>!Started?"Hourly history starting":IsAverage?(CompleteDays==1?"Avg · 1 complete day":"Avg · "+CompleteDays+" complete days"):PartialToday?"Today (partial)":"Today";
 public double Peak {get;private set;}
 public static int HourOfDay(double progress) {
  if(double.IsNaN(progress)||double.IsInfinity(progress)||progress<0||progress>1)throw new ArgumentOutOfRangeException(nameof(progress));
  // The native day progress is a float. Tolerance keeps exact game-hour
  // boundaries such as (float)(7/24) from falling into the previous bin.
  return Math.Min(23,(int)Math.Floor(progress*24+1e-6));
 }
 public static HourlyActivity ForStation(StationCount station,int today,double progress) {
  var result=new HourlyActivity{CurrentHour=HourOfDay(progress),Started=station.HourlyFirstDay.HasValue};
  if(!result.Started)return result;
  int first=station.HourlyFirstDay!.Value;
  result.CompleteDays=Math.Min(Counter.HistoryDays,Math.Max(0,today-first-1));
  result.PartialToday=first==today;result.StartHour=HourOfDay(station.HourlyFirstProgress);
  for(int hour=0;hour<24;hour++) {
   if(result.IsAverage) {
    result.Observed[hour]=true;
    for(int day=today-result.CompleteDays;day<today;day++)if(station.HourlyDays.TryGetValue(day,out var counts)) {
     result.Boardings[hour]+=counts.Boardings[hour]/(double)result.CompleteDays;
     result.Alightings[hour]+=counts.Alightings[hour]/(double)result.CompleteDays;
    }
   } else {
    result.Observed[hour]=hour<=result.CurrentHour&&(!result.PartialToday||hour>=result.StartHour);
    if(result.Observed[hour]&&station.HourlyDays.TryGetValue(today,out var counts)) {
     result.Boardings[hour]=counts.Boardings[hour];result.Alightings[hour]=counts.Alightings[hour];
    }
   }
   result.Peak=Math.Max(result.Peak,result.Boardings[hour]+result.Alightings[hour]);
  }
  return result;
 }
 public float HeightPercent(int hour)=>Peak>0?(float)((Boardings[hour]+Alightings[hour])/Peak*100):0;
 public string Tooltip(int hour,CultureInfo culture) {
  string time=hour.ToString("00",culture)+":00–"+(hour+1).ToString("00",culture)+":00";
  if(!Started)return time+"\nHourly history has not started.";
  if(!Observed[hour])return time+(hour>CurrentHour?"\nLater today; no observations yet.":"\nBefore hourly tracking began; no observations.");
  string format=IsAverage?"N1":"N0";
  string note=IsAverage?Period:(hour==CurrentHour?"Today · current hour is partial":PartialToday&&hour==StartHour?"Today · tracking began during this hour":"Today");
  return time+"\nBoardings: "+Boardings[hour].ToString(format,culture)+"\nAlightings: "+Alightings[hour].ToString(format,culture)+"\n"+note;
 }
}
