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

// A normal entity-panel fragment, shown only for transport stations.
// No runtime patches, global window, hotkey, or scene-specific UI assets.
public sealed class RidershipFragment : IEntityPanelFragment
{
 readonly CounterService service;
 readonly ITooltipRegistrar tooltips;
 VisualElement root=null!;
 Label total=null!,average=null!,period=null!,since=null!,flavor=null!;
 string? stationId;
 public RidershipFragment(CounterService service,ITooltipRegistrar tooltips){this.service=service;this.tooltips=tooltips;}
 public VisualElement InitializeFragment() {
  // These are the same nine-slice element and USS classes used by vanilla
  // building fragments. Styles are supplied by the parent EntityPanel.
  root=new NineSliceVisualElement{name="BoardingCounterRidership"};
  root.AddToClassList("entity-sub-panel");root.AddToClassList("bg-sub-box--green");root.style.flexShrink=0;
  var header=new VisualElement{name="RidershipHeader"};header.AddToClassList("entity-panel__row");
  header.style.justifyContent=Justify.SpaceBetween;header.style.marginBottom=6;
  var heading=Text("Ridership","game-text-heading");heading.AddToClassList("text--bold");header.Add(heading);
  since=Text("","game-text-small");since.style.marginLeft=8;since.style.flexShrink=1;since.style.unityTextAlign=TextAnchor.MiddleRight;header.Add(since);root.Add(header);
  tooltips.Register(header,()=>"Each entry counts as one boarding, including repeat journeys and bots. Totals and recent daily counts are saved with this settlement.");
  total=ValueRow("Total boardings",out var totalRow);
  average=ValueRow("Daily average",out var averageRow);
  tooltips.Register(totalRow,()=>"Boardings since this station's tracking began. Loading a save restores the total from that point in the colony's history.");
  tooltips.Register(averageRow,()=>"Average boardings per day over up to seven complete tracked game days. Includes zero-traffic days; excludes the first partial tracking day and the current day.");
  period=Text("","game-text-small");period.style.unityTextAlign=TextAnchor.MiddleRight;root.Add(period);
  flavor=Text("","game-text-small");flavor.AddToClassList("text--yellow");flavor.AddToClassList("flavor-text");
  flavor.style.marginTop=6;flavor.style.unityFontStyleAndWeight=FontStyle.Italic;flavor.style.unityTextAlign=TextAnchor.MiddleRight;root.Add(flavor);
  ClearFragment();return root;
 }
 static Label Text(string text,string styleClass) {
  var label=new Label(text);label.AddToClassList(styleClass);return label;
 }
 Label ValueRow(string title,out VisualElement row) {
  row=new VisualElement();row.AddToClassList("entity-panel__row");row.style.justifyContent=Justify.SpaceBetween;row.style.marginBottom=2;
  var caption=Text(title,"entity-panel__text");caption.style.flexShrink=1;caption.style.marginRight=8;row.Add(caption);
  var value=Text("—","game-text-big");value.AddToClassList("text--bold");value.style.flexShrink=0;value.style.unityTextAlign=TextAnchor.MiddleRight;row.Add(value);root.Add(row);return value;
 }
 public void ShowFragment(BaseComponent entity) {
  ClearFragment();var mode=StationClassifier.Mode(entity);if(mode==null)return;
  flavor.text=mode=="zipline"?"“The pastries are still warm if you take the zipline.”":"“A colony that rides together thrives together.”";
  stationId=entity.GetComponent<EntityComponent>().EntityId.ToString();root.style.display=DisplayStyle.Flex;UpdateFragment();
 }
 public void ClearFragment(){stationId=null;if(root!=null)root.style.display=DisplayStyle.None;}
 public void UpdateFragment() {
  if(stationId==null)return;
  var counts=service.Counts;
  if(!service.TrackingAvailable||counts==null){total.text="—";average.text="—";period.text="Tracking unavailable";since.text="";return;}
  if(!counts.Stations.TryGetValue(stationId,out var station)){total.text="0";average.text="—";period.text="Tracking begins when construction finishes.";since.text="";return;}
  var view=RidershipPresentation.ForStation(counts,stationId,service.Today,CultureInfo.CurrentCulture);
  total.text=view.Total;average.text=view.Average;period.text=view.Period;since.text=view.Since;
 }
}
