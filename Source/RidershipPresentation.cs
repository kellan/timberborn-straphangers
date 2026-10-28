using System.Globalization;
namespace BoardingCounter;
public sealed class RidershipPresentation
{
 public string Total {get;private set;}="";
 public string Average {get;private set;}="";
 public string TotalAlightings {get;private set;}="";
 public string AverageAlightings {get;private set;}="";
 public string Period {get;private set;}="";
 public string Since {get;private set;}="";
 public static RidershipPresentation ForStation(Counter counts,string id,int today,CultureInfo culture) {
  int n=counts.ObservedFullDays(id,today);var s=counts.Stations[id];
  return new RidershipPresentation{Total=s.TotalBoardings.ToString("N0",culture),Average=counts.Average(id,today)?.ToString("N1",culture)??"—",TotalAlightings=s.TotalArrivals.ToString("N0",culture),AverageAlightings=counts.AverageAlightings(id,today)?.ToString("N1",culture)??"—",Period=n==0?"Average available after one complete day.":n==1?"1 complete day":"Last "+n.ToString(culture)+" complete days",Since="Since day "+s.FirstDay.ToString(culture)};
 }
}
