using System.Globalization;
using Timberborn.BaseComponentSystem;
using Timberborn.CoreUI;
using Timberborn.TooltipSystem;
using Timberborn.EntityPanelSystem;
using Timberborn.EntitySystem;
using Timberborn.TubeSystem;
using Timberborn.ZiplineSystem;
using UnityEngine;
using UnityEngine.UIElements;
namespace BoardingCounter;

public static class StationClassifier
{
 public static string? Mode(BaseComponent entity) {
  if(entity.HasComponent<TubeStationSpec>())return "tubeway";
  if(entity.HasComponent<ZiplineTower>())foreach(var c in entity.GetComponent<EntityComponent>().AllComponents)if(c.GetType().FullName=="Timberborn.ZiplineSystem.ZiplineStationSpec")return "zipline";
  return null;
 }
}

public sealed class RidershipFragment : IEntityPanelFragment
{
 readonly CounterService service;
 readonly ITooltipRegistrar tooltips;
 readonly VisualElementLoader elements;
 VisualElement root=null!,activity=null!;
 Label totalBoardings=null!,totalAlightings=null!,averageBoardings=null!,averageAlightings=null!;
 Label period=null!,since=null!,flavor=null!,hourPeriod=null!;
 readonly VisualElement[] rates=new VisualElement[24],markers=new VisualElement[24];
 HourlyActivity? profile;
 string? stationId;
 float nextRefresh;
 public RidershipFragment(CounterService service,ITooltipRegistrar tooltips,VisualElementLoader elements){this.service=service;this.tooltips=tooltips;this.elements=elements;}
 public VisualElement InitializeFragment() {
  root=new NineSliceVisualElement{name="StraphangersRidership"};
  root.AddToClassList("entity-sub-panel");root.AddToClassList("bg-sub-box--green");root.style.flexShrink=0;
  var header=Row();header.style.justifyContent=Justify.SpaceBetween;header.style.marginBottom=4;
  var heading=Text("Straphangers","game-text-heading");heading.AddToClassList("text--bold");header.Add(heading);
  since=Text("","game-text-small");since.style.marginLeft=8;since.style.flexShrink=1;since.style.unityTextAlign=TextAnchor.MiddleRight;header.Add(since);root.Add(header);
  tooltips.Register(header,()=>"Station ridership: boardings enter the network; alightings return to the path. Repeat journeys and bots count. Totals and recent history are saved with this settlement.");
  var columns=Row();columns.Add(Caption(""));columns.Add(Cell("Boardings","game-text-small",true,"Beavers entering the network at this station."));columns.Add(Cell("Alightings","game-text-small",true,"Beavers leaving the network at this station and returning to the path."));root.Add(columns);
  var averages=MetricRow("Daily average",true,out averageBoardings,out averageAlightings);
  tooltips.Register(averages,()=>"Average per day over up to seven complete tracked days, including zero-traffic days. Excludes the first partial tracking day and the current day.");
  var totals=MetricRow("Total",false,out totalBoardings,out totalAlightings);
  tooltips.Register(totals,()=>"Lifetime totals since this station's tracking began. Loading an older save restores that point in its history.");
  period=Text("","game-text-small");period.style.unityTextAlign=TextAnchor.MiddleRight;root.Add(period);
  activity=new VisualElement{name="HourlyActivity"};activity.style.marginTop=6;root.Add(activity);
  var hourHeader=Row();hourHeader.style.justifyContent=Justify.SpaceBetween;
  hourHeader.Add(Text("Activity by hour","entity-panel__text"));hourPeriod=Text("","game-text-small");hourPeriod.style.unityTextAlign=TextAnchor.MiddleRight;hourHeader.Add(hourPeriod);activity.Add(hourHeader);
  tooltips.Register(hourHeader,()=>"Combined boardings and alightings by game hour, scaled to this station's busiest hour. These bars show activity, not percent capacity. Hover an hour for separate counts. Starts with today, then averages up to seven complete days of hourly history.");
  var hours=Row();hours.style.justifyContent=Justify.Center;activity.Add(hours);
  for(int h=0;h<24;h++) {
   // The actual template used by agora/medical-bed Popularity, including its
   // native bar textures, sizes and current-hour marker. No copied game assets.
   var bar=elements.LoadVisualElement("Game/AttractionLoadRate");hours.Add(bar);
   rates[h]=bar.Q<VisualElement>("Rate");markers[h]=bar.Q<VisualElement>("CurrentHourMarker");
   int hour=h;tooltips.Register(bar,()=>profile?.Tooltip(hour,CultureInfo.CurrentCulture)??"Hourly history starting");
  }
  flavor=Text("","game-text-small");flavor.AddToClassList("text--yellow");flavor.style.marginTop=6;
  flavor.style.unityFontStyleAndWeight=FontStyle.Italic;flavor.style.unityTextAlign=TextAnchor.MiddleRight;root.Add(flavor);
  ClearFragment();return root;
 }
 static VisualElement Row(){var row=new VisualElement();row.AddToClassList("entity-panel__row");return row;}
 static Label Text(string text,string styleClass){var label=new Label(text);label.AddToClassList(styleClass);return label;}
 static Label Caption(string title){var label=Text(title,"entity-panel__text");label.style.flexGrow=1;label.style.flexShrink=1;label.style.minWidth=0;return label;}
 Label Cell(string text,string styleClass,bool bold,string? help=null) {
  var label=Text(text,styleClass);if(bold)label.AddToClassList("text--bold");
  label.style.width=80;label.style.marginLeft=4;label.style.flexShrink=0;label.style.unityTextAlign=TextAnchor.MiddleRight;
  label.style.whiteSpace=WhiteSpace.NoWrap;label.style.overflow=Overflow.Hidden;label.style.textOverflow=TextOverflow.Ellipsis;
  tooltips.Register(label,()=>help??label.text);return label;
 }
 VisualElement MetricRow(string title,bool prominent,out Label boardings,out Label alightings) {
  var row=Row();row.style.marginBottom=2;row.Add(Caption(title));
  boardings=Cell("—",prominent?"game-text-big":"entity-panel__text",prominent);
  alightings=Cell("—",prominent?"game-text-big":"entity-panel__text",prominent);
  row.Add(boardings);row.Add(alightings);root.Add(row);return row;
 }
 public void ShowFragment(BaseComponent entity) {
  ClearFragment();var mode=StationClassifier.Mode(entity);if(mode==null)return;
  flavor.text=mode=="zipline"?"“The pastries are still warm if you take the zipline.”":"“A colony that rides together thrives together.”";
  stationId=entity.GetComponent<EntityComponent>().EntityId.ToString();root.style.display=DisplayStyle.Flex;UpdateFragment();
 }
 public void ClearFragment(){stationId=null;profile=null;nextRefresh=0;if(root!=null)root.style.display=DisplayStyle.None;}
 public void UpdateFragment() {
  if(stationId==null||Time.realtimeSinceStartup<nextRefresh)return;
  nextRefresh=Time.realtimeSinceStartup+.25f;
  var counts=service.Counts;
  if(!service.TrackingAvailable||counts==null){Blank("Tracking unavailable");return;}
  if(!counts.Stations.TryGetValue(stationId,out var station)){Blank("Tracking begins when construction finishes.");totalBoardings.text=totalAlightings.text="0";return;}
  var view=RidershipPresentation.ForStation(counts,stationId,service.Today,CultureInfo.CurrentCulture);
  totalBoardings.text=view.Total;totalAlightings.text=view.TotalAlightings;averageBoardings.text=view.Average;averageAlightings.text=view.AverageAlightings;
  period.text=view.Period;since.text=view.Since;activity.style.display=DisplayStyle.Flex;
  profile=HourlyActivity.ForStation(station,service.Today,service.DayProgress);hourPeriod.text=profile.Period;
  for(int h=0;h<24;h++) {
   rates[h].style.height=new StyleLength(Length.Percent(profile.HeightPercent(h)));
   markers[h].style.display=h==profile.CurrentHour?DisplayStyle.Flex:DisplayStyle.None;
  }
 }
 void Blank(string message){totalBoardings.text=totalAlightings.text=averageBoardings.text=averageAlightings.text="—";period.text=message;since.text="";profile=null;activity.style.display=DisplayStyle.None;}
}
