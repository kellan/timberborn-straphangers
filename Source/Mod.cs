using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections;
using Newtonsoft.Json;
using Bindito.Core;
using Timberborn.BlockSystem;
using Timberborn.Bots;
using Timberborn.Coordinates;
using Timberborn.CharacterMovementSystem;
using Timberborn.WalkingSystem;
using Timberborn.EntitySystem;
using Timberborn.ModManagerScene;
using Timberborn.WorldPersistence;
using Timberborn.EntityPanelSystem;
using Timberborn.SingletonSystem;
using Timberborn.TimeSystem;
using Timberborn.TubeSystem;
using Timberborn.ZiplineSystem;
using UnityEngine;
namespace BoardingCounter;

public sealed class Starter : IModStarter
{
 public const string Version="0.1.11";
 public static string LogDirectory="",RecordingRoot="";
 public static string? ReferenceJson,AuditorSource;
 public static object Provenance=new{};
 static readonly JsonSerializer ReplayJson=JsonSerializer.Create(new JsonSerializerSettings{Culture=CultureInfo.InvariantCulture,Formatting=Formatting.None,TypeNameHandling=TypeNameHandling.None,NullValueHandling=NullValueHandling.Include,DefaultValueHandling=DefaultValueHandling.Include,FloatFormatHandling=FloatFormatHandling.String,ContractResolver=new Newtonsoft.Json.Serialization.DefaultContractResolver()});
 public static string SerializeReplay(object value){using(var writer=new StringWriter(CultureInfo.InvariantCulture)){ReplayJson.Serialize(writer,value);return writer.ToString();}}
 public void StartMod(IModEnvironment environment) {
  LogDirectory=Path.Combine(environment.ModPath,"Logs");RecordingRoot=Path.Combine(environment.ModPath,"Recordings");
  var referencePath=Path.Combine(environment.ModPath,"Replay","station-reference.json");
  ReferenceJson=File.Exists(referencePath)?File.ReadAllText(referencePath):null;
  var auditorPath=Path.Combine(environment.ModPath,"Replay","audit_recording.py");AuditorSource=File.Exists(auditorPath)?File.ReadAllText(auditorPath):null;
  var ownPath=Path.Combine(environment.ModPath,"BoardingCounter.dll");
  var gamePath=typeof(PathFollower).Assembly.Location;
  Provenance=new{mod_version=Version,mod_mvid=typeof(Starter).Assembly.ManifestModule.ModuleVersionId.ToString(),mod_sha256=File.Exists(ownPath)?ReplayRecording.HashFile(ownPath):null,game_version=Application.version,unity_version=Application.unityVersion,movement_assembly_sha256=File.Exists(gamePath)?ReplayRecording.HashFile(gamePath):null,coordinate_convention="world x,y-up,z; grid x,y,z-up",tube_assembly_sha256=ReplayRecording.HashFile(typeof(TubeSpec).Assembly.Location),tube_occupancy_source="Tube.VisitorsChanged membership sets; displayed CharacterModel.Position",metric="directed pedestrian station entrance crossings"};
  try{NativeMovementBuffer.Initialize();}catch(Exception e){Debug.LogError("[Straphangers] Native movement capture unavailable: "+e);return;}
  Debug.Log("[Straphangers] v0.1.11 entrance crossing observer loaded; saved counts and station panel; logs: "+LogDirectory);
 }
}
[Context("Game")]
public sealed class CounterConfigurator : Configurator
{
 protected override void Configure() {
  Bind<CounterService>().AsSingleton();Bind<RidershipFragment>().AsSingleton();
  MultiBind<EntityPanelModule>().ToProvider<RidershipModuleProvider>().AsSingleton();
 }
 sealed class RidershipModuleProvider : IProvider<EntityPanelModule> {
  readonly RidershipFragment fragment;
  public RidershipModuleProvider(RidershipFragment fragment){this.fragment=fragment;}
  public EntityPanelModule Get(){var builder=new EntityPanelModule.Builder();builder.AddMiddleFragment(fragment);return builder.Build();}
 }
}
public sealed class CounterService : IPostLoadableSingleton,IUpdatableSingleton,IUnloadableSingleton,ISaveableSingleton
{
 public static CounterService? Active;
 readonly EntityRegistry entities;
 readonly IDayNightCycle clock;
 readonly RidershipSaveStore savedCounts;
 public bool TrackingAvailable=>counter!=null&&!failed&&Active==this;
 public int Today=>clock.DayNumber;
 public double DayProgress=>clock.DayProgress;
 public Counter? Counts=>counter;
 readonly string sessionId=Guid.NewGuid().ToString("N");
 Counter? counter;
 EntranceGate[] gates=Array.Empty<EntranceGate>();
 readonly Dictionary<Guid,Rider> riders=new Dictionary<Guid,Rider>();
 readonly HashSet<Guid> seen=new HashSet<Guid>();
 readonly HashSet<Guid> sampled=new HashSet<Guid>();
 StreamWriter? events,samples,geometry;
 ReplayRecording? recording;
 TubeOccupancyObserver? tubeObserver;
 ReplayStation[] replayGates=Array.Empty<ReplayStation>();
 float nextRefresh,nextWrite;
 bool failed;
 int revision;
 long observationId;
 public CounterService(EntityRegistry entities,IDayNightCycle clock,ISingletonLoader singletonLoader) { this.entities=entities;this.clock=clock;savedCounts=new RidershipSaveStore(singletonLoader,Starter.SerializeReplay); }
 public void Save(ISingletonSaver saver) {
  savedCounts.Save(saver,counter,entities.Entities.Where(e=>!e.Deleted&&StationClassifier.Mode(e)!=null).Select(e=>e.EntityId.ToString()),clock.DayNumber);
 }
 void LoadCounts() {
  counter=savedCounts.Load(clock.DayNumber);
  Debug.Log(savedCounts.Restored?"[Straphangers] Restored ridership for "+counter.Stations.Count+" stations from this game save.":"[Straphangers] No saved ridership yet; tracking begins on day "+clock.DayNumber+".");
 }
 public void PostLoad() {
  try {
   LoadCounts();
   if(!NativeMovementBuffer.Ready){Debug.LogError("[Straphangers] Native movement API unavailable; entrance observer disabled.");return;}
   Directory.CreateDirectory(Starter.LogDirectory);
   events=Open("events.csv","session_id,day,day_progress,observation_id,revision,mode,event,station_id,station_name,passenger_id,passenger_type,cross_x,cross_y,cross_z,from_x,from_y,from_z,to_x,to_y,to_z,attribution");
   samples=Open("sample-routes.csv","session_id,day,day_progress,observation_id,revision,passenger_id,from_x,from_y,from_z,to_x,to_y,to_z,entries,exits,discontinuity,observation_kind");
   geometry=Open("entrances.csv","session_id,revision,station_id,station_name,mode,center_x,center_y,center_z,inward_x,inward_y,inward_z,half_width,height_tolerance");
   Rotate("summary.csv");RefreshStations();
   try {
    if(Starter.ReferenceJson!=null&&Starter.AuditorSource!=null){recording=new ReplayRecording(Starter.RecordingRoot,sessionId,Starter.Version,clock.DayNumber,Starter.Provenance,Starter.ReferenceJson,Starter.AuditorSource,Starter.SerializeReplay,m=>Debug.LogWarning("[Straphangers] Replay capture: "+m));recording.Geometry(revision,replayGates,clock.DayNumber,clock.DayProgress);}
    else Debug.LogWarning("[Straphangers] Replay reference missing; standalone recording unavailable.");
   }catch(Exception e){Debug.LogWarning("[Straphangers] Replay capture unavailable; live counter continues: "+e.Message);}
   ObserveRiders();
   try{tubeObserver=new TubeOccupancyObserver(entities,(id,grid,action,occupants)=>recording?.Tube(id,grid,action,occupants,clock.DayNumber,clock.DayProgress,Time.frameCount,Time.timeScale),e=>TubeFailure(e));recording?.TubeStatus("ready","Read-only event subscriptions; all tube passenger IDs, route capture bounded separately",clock.DayNumber,clock.DayProgress);tubeObserver.Refresh();}
   catch(Exception e){TubeFailure(e);}
   Application.quitting+=OnApplicationQuit;
   Active=this;WriteSummary();
   Debug.Log("[Straphangers] Tracking entrances on day "+clock.DayNumber+"; "+gates.Length+" station doors; standalone replay captures starting positions and native movement for up to 64 passengers. Tube membership events retain passenger IDs alongside native routes. Legacy rolling CSV samples remain separate. Inward crossings are entries, not proof of completed rides.");
  } catch(Exception e){Fail(e);}
 }
 StreamWriter Open(string name,string header) { Rotate(name);var w=new StreamWriter(Path.Combine(Starter.LogDirectory,name),false);w.WriteLine(header);return w; }
 void Rotate(string name) {
  var a=Path.Combine(Starter.LogDirectory,name);var b=Path.Combine(Starter.LogDirectory,"previous-"+name);
  if(File.Exists(a)){if(File.Exists(b))File.Delete(b);File.Move(a,b);}
 }
 public void UpdateSingleton() {
  if(Active!=this||failed)return;
  try {
   if(Time.realtimeSinceStartup>=nextRefresh){RefreshStations();ObserveRiders();tubeObserver?.Refresh();nextRefresh=Time.realtimeSinceStartup+1;}
   if(Time.realtimeSinceStartup>=nextWrite) {
    events!.Flush();samples!.Flush();geometry!.Flush();recording?.Flush();WriteSummary();counter!.Trim(clock.DayNumber);
    if(events.BaseStream.Length>5*1024*1024){events.Dispose();events=Open("events.csv","session_id,day,day_progress,observation_id,revision,mode,event,station_id,station_name,passenger_id,passenger_type,cross_x,cross_y,cross_z,from_x,from_y,from_z,to_x,to_y,to_z,attribution");}
    if(samples.BaseStream.Length>5*1024*1024){samples.Dispose();samples=Open("sample-routes.csv","session_id,day,day_progress,observation_id,revision,passenger_id,from_x,from_y,from_z,to_x,to_y,to_z,entries,exits,discontinuity,observation_kind");}
    if(geometry.BaseStream.Length>1024*1024){geometry.Dispose();geometry=Open("entrances.csv","session_id,revision,station_id,station_name,mode,center_x,center_y,center_z,inward_x,inward_y,inward_z,half_width,height_tolerance");WriteGeometry();}
    nextWrite=Time.realtimeSinceStartup+5;
   }
  }catch(Exception e){Fail(e);}
 }
 public void Unload(){recording?.Seal("session-ended",clock.DayNumber,clock.DayProgress);try{if(!failed)WriteSummary();}catch(Exception e){Debug.LogWarning("[Straphangers] Summary close: "+e.Message);}Cleanup();}
 void OnApplicationQuit(){recording?.Seal("application-quit",clock.DayNumber,clock.DayProgress);try{WriteSummary();}catch(Exception e){Debug.LogWarning("[Straphangers] Quit status write: "+e.Message);}}
 void TubeFailure(Exception e){tubeObserver?.Dispose();tubeObserver=null;recording?.TubeStatus("failed",e.ToString(),clock.DayNumber,clock.DayProgress);Debug.LogWarning("[Straphangers] Tube occupancy unavailable: "+e);}
 void Cleanup(){tubeObserver?.Dispose();tubeObserver=null;Application.quitting-=OnApplicationQuit;recording?.Dispose();if(Active==this)Active=null;foreach(var rider in riders.Values)rider.Detach();riders.Clear();events?.Dispose();samples?.Dispose();geometry?.Dispose();events=samples=geometry=null;}
 public void Fail(Exception e){if(failed)return;failed=true;recording?.Seal("observer-error",clock.DayNumber,clock.DayProgress);try{Cleanup();}catch{}Debug.LogError("[Straphangers] Observer stopped: "+e);}
 void ObserveRiders() {
  seen.Clear();
  foreach(var entity in entities.Entities) {
   if(entity.Deleted||!entity.HasComponent<Walker>())continue;
   var id=entity.EntityId;seen.Add(id);
   if(!riders.ContainsKey(id))riders.Add(id,new Rider(this,entity.GetComponent<Walker>(),id));
  }
  foreach(var id in riders.Keys.Where(id=>!seen.Contains(id)).ToArray()){riders[id].Detach();riders.Remove(id);sampled.Remove(id);recording?.RemoveActor(id.ToString(),clock.DayNumber,clock.DayProgress);}
 }
 void RefreshStations() {
  var next=new List<EntranceGate>();var definitions=new Dictionary<string,ReplayStation>();var existingStations=new HashSet<string>();
  foreach(var entity in entities.Entities) {
   if(entity.Deleted)continue;
   string? mode=StationClassifier.Mode(entity);
   if(mode!=null)existingStations.Add(entity.EntityId.ToString());
   if(mode==null||!entity.TryGetComponent<BlockObject>(out var block)||!block.IsFinished||block.PositionedEntrance==null)continue;
   // Station blueprints place the entrance cell outside and the adjacent
   // doorstep cell inside. Verified against the authored door edge in all
   // orientations and against the captured v0.1.4 geometry.
   var entrance=CoordinateSystem.GridToWorldCentered(block.PositionedEntrance.Coordinates);
   var doorstep=CoordinateSystem.GridToWorldCentered(block.PositionedEntrance.DoorstepCoordinates);
   var name=(mode=="tubeway"?"Tubeway Station":"Zipline Station")+" @ "+block.Coordinates;
   var gate=EntranceGate.FromAnchors(entity.EntityId.ToString(),name,mode,Point(entrance),Point(doorstep));
   counter!.Register(gate.Id,mode,name,clock.DayNumber);counter.BeginHourlyTracking(gate.Id,clock.DayNumber,clock.DayProgress);next.Add(gate);
   definitions.Add(gate.Id,new ReplayStation{id=gate.Id,name=gate.Name,mode=gate.Mode,template=entity.Name.Replace("(Clone)","").Trim(),placement=new[]{block.Coordinates.x,block.Coordinates.y,block.Coordinates.z},orientation=block.Orientation.ToString(),outside=ReplayRecording.Vector(Point(entrance)),inside=ReplayRecording.Vector(Point(doorstep)),center=ReplayRecording.Vector(gate.Center),inward=ReplayRecording.Vector(gate.Inward),half_width=gate.HalfWidth,height_tolerance=gate.HeightTolerance});
  }
  counter!.RetainStations(existingStations);
  next.Sort((a,b)=>string.CompareOrdinal(a.Id,b.Id));
  bool changed=next.Count!=gates.Length||next.Where((g,i)=>i>=gates.Length||g.Id!=gates[i].Id||!g.SameGeometry(gates[i])).Any();
  if(!changed)return;
  gates=next.ToArray();replayGates=gates.Select(g=>definitions[g.Id]).ToArray();revision++;foreach(var r in riders.Values)r.Configure(gates);WriteGeometry();recording?.Geometry(revision,replayGates,clock.DayNumber,clock.DayProgress);
 }
 void WriteGeometry(){foreach(var g in gates)geometry!.WriteLine(string.Join(",",new[]{sessionId,revision.ToString(),Csv(g.Id),Csv(g.Name),g.Mode,Number(g.Center.X),Number(g.Center.Y),Number(g.Center.Z),Number(g.Inward.X),Number(g.Inward.Y),Number(g.Inward.Z),Number(g.HalfWidth),Number(g.HeightTolerance)}));}
 sealed class Rider {
  readonly CounterService service;
  readonly Walker walker;
  readonly Guid id;
  readonly bool bot;
  readonly EntranceTracker tracker;
  readonly NativeMovementBuffer native;
  readonly List<MovementSpan> spans=new List<MovementSpan>();
  readonly List<ReplayStep> replaySteps=new List<ReplayStep>();
  bool captureFrame;
  public long NativeSteps {get;private set;}
  public long CompletedFrames {get;private set;}
  public long SmoothingExcluded {get;private set;}
  readonly List<EntranceCrossing> found=new List<EntranceCrossing>();
  public long Skipped=>tracker.SkippedDiscontinuities;
  public Rider(CounterService service,Walker walker,Guid id){this.service=service;this.walker=walker;this.id=id;bot=walker.HasComponent<BotSpec>();native=new NativeMovementBuffer(walker.PathFollower);tracker=new EntranceTracker(Point(walker.Transform.position),service.gates);service.recording?.RegisterActor(id.ToString(),bot,tracker.Previous,service.clock.DayNumber,service.clock.DayProgress);walker.PathFollower.MovedAlongPath+=OnMoved;}
  public void Configure(EntranceGate[] gates)=>tracker.Configure(gates);
  void OnMoved(object sender,MovementEventArgs args) {
   if(Active!=service)return;
   ReplayFrame? frame=null;
   try {
    captureFrame=service.recording?.Wants(id.ToString())==true;replaySteps.Clear();
    if(captureFrame)frame=new ReplayFrame{actor=id.ToString(),day=service.clock.DayNumber,progress=service.clock.DayProgress,revision=service.revision,render_frame=Time.frameCount,time_scale=Time.timeScale,actual_end=ReplayRecording.Vector(Point(walker.Transform.position)),native=native.Snapshot()};
    int end=CompletedPath.Reduce(native,Point(walker.Transform.position),spans);
    CompletedFrames++;NativeSteps+=end;SmoothingExcluded+=native.Count-end-1;
    var start=native[0].Position;
    if((tracker.Previous-start).LengthSquared>0)Process(start,"baseline-reset");
    foreach(var span in spans)Process(span.To,span.Instant?"instant-edge":"native-walk");
    if(frame!=null){frame.claimed_completed_end=end;frame.steps=replaySteps.ToArray();service.recording!.Frame(frame);}
   }catch(Exception e){if(frame!=null){frame.error=e.ToString();frame.steps=replaySteps.ToArray();service.recording?.Frame(frame);}service.Fail(e);}
  }
  void Process(Point3 to,string kind) {
   var from=tracker.Previous;
   if((to-from).LengthSquared==0)return;
   found.Clear();
   if(kind=="native-walk")tracker.Observe(to,found,verifiedContinuous:true);
   else tracker.ResetBaseline(to);
   long obs=++service.observationId;
   if(captureFrame)replaySteps.Add(new ReplayStep{observation=obs,kind=kind,from=ReplayRecording.Vector(from),to=ReplayRecording.Vector(to),events=found.Select(c=>new ReplayEvent{station=c.Gate.Id,direction=c.Boarding?"entry":"exit",cross=ReplayRecording.Vector(c.Position)}).ToArray()});
   foreach(var crossing in found)service.Record(crossing,id,bot,obs);
   if(found.Count>0&&service.sampled.Count<4)service.sampled.Add(id);
   if(service.sampled.Contains(id))service.samples!.WriteLine(string.Join(",",new[]{service.sessionId,service.clock.DayNumber.ToString(),Number(service.clock.DayProgress),obs.ToString(),service.revision.ToString(),id.ToString(),Number(from.X),Number(from.Y),Number(from.Z),Number(to.X),Number(to.Y),Number(to.Z),found.Count(c=>c.Boarding).ToString(),found.Count(c=>!c.Boarding).ToString(),kind=="native-walk"?"0":"1",kind}));
  }
  public void Detach()=>walker.PathFollower.MovedAlongPath-=OnMoved;
 }
 void Record(EntranceCrossing crossing,Guid passenger,bool bot,long obs) {
  var g=crossing.Gate;counter!.Record(g.Id,g.Mode,g.Name,clock.DayNumber,crossing.Boarding,bot,clock.DayProgress);
  events!.WriteLine(string.Join(",",new[]{sessionId,clock.DayNumber.ToString(),Number(clock.DayProgress),obs.ToString(),revision.ToString(),g.Mode,crossing.Boarding?"entry":"exit",Csv(g.Id),Csv(g.Name),passenger.ToString(),bot?"bot":"beaver",Number(crossing.Position.X),Number(crossing.Position.Y),Number(crossing.Position.Z),Number(crossing.From.X),Number(crossing.From.Y),Number(crossing.From.Z),Number(crossing.To.X),Number(crossing.To.Y),Number(crossing.To.Z),"entrance-plane"}));
 }
 void WriteSummary() {
  if(counter==null)return;string path=Path.Combine(Starter.LogDirectory,"summary.csv");
  using(var w=new StreamWriter(path+".tmp",false)) {
   w.WriteLine("session_id,station_id,station_name,mode,day,entries_today,exits_today,total_entries,total_exits,total_bot_entries,average_daily_entries,average_daily_alightings,completed_days_in_average,tracking_start_day");
   foreach(var p in counter.Stations.OrderBy(p=>p.Value.Name)) {
    var s=p.Value;s.Days.TryGetValue(clock.DayNumber,out var d);
    w.WriteLine(string.Join(",",new[]{sessionId,Csv(p.Key),Csv(s.Name),s.Mode,clock.DayNumber.ToString(),(d?.Boardings??0).ToString(),(d?.Arrivals??0).ToString(),s.TotalBoardings.ToString(),s.TotalArrivals.ToString(),s.BotBoardings.ToString(),counter.Average(p.Key,clock.DayNumber)?.ToString("F2",CultureInfo.InvariantCulture)??"",counter.AverageAlightings(p.Key,clock.DayNumber)?.ToString("F2",CultureInfo.InvariantCulture)??"",counter.ObservedFullDays(p.Key,clock.DayNumber).ToString(),s.FirstDay.ToString()}));
   }
  }
  if(File.Exists(path))File.Delete(path);File.Move(path+".tmp",path);
  File.WriteAllText(Path.Combine(Starter.LogDirectory,"observer-status.txt"),"Version: 0.1.11\nSession: "+sessionId+"\nMetric: pedestrian station entrance crossings\nActive entrances: "+gates.Length+"\nPortal revision: "+revision+"\nExplicit baseline resets / instant edges: "+riders.Values.Sum(r=>r.Skipped)+"\nCompleted native frames: "+riders.Values.Sum(r=>r.CompletedFrames)+"\nTraversed native steps: "+riders.Values.Sum(r=>r.NativeSteps)+"\nUntraversed smoothing points excluded: "+riders.Values.Sum(r=>r.SmoothingExcluded)+"\nSampled passengers: "+string.Join(", ",sampled)+"\nStandalone recording: "+(recording?.DirectoryPath??"unavailable")+"\nRecording status: "+(recording?.Status??"unavailable")+"\nRecording reason: "+(recording?.Reason??"")+"\nRecording coverage: "+(recording?.Coverage??"unavailable")+"\nRecording actors: "+(recording?.ActorsCaptured??0)+"\nObserved tube segments: "+(tubeObserver?.Count??0)+"\nRecording tube records: "+(recording?.TubeRecordsCaptured??0)+"\nRecording native frames: "+(recording?.FramesCaptured??0)+"\nTotals are stored in this game save; rolling average uses up to seven complete tracked days.\n");
 }
 static Point3 Point(Vector3 p)=>new Point3(p.x,p.y,p.z);
 static string Number(double d)=>d.ToString("R",CultureInfo.InvariantCulture);
 static string Csv(string s)=>"\""+s.Replace("\"","\"\"")+"\"";
}

// Read-only access to the game's own completed-step buffer. No Harmony or code
// patches are needed. Fail closed if a future game version changes its shape.
public sealed class NativeMovementBuffer : IReadOnlyList<MovementPoint>
{
 static FieldInfo? field;
 public static bool Ready=>field!=null;
 readonly List<AnimatedPathCorner> points;
 public static void Initialize() {
  field=typeof(PathFollower).GetField("_animatedPathCorners",BindingFlags.Instance|BindingFlags.NonPublic);
  if(field==null||field.FieldType!=typeof(List<AnimatedPathCorner>)){field=null;throw new MissingFieldException("PathFollower completed-step buffer");}
 }
 public NativeMovementBuffer(PathFollower follower){points=(List<AnimatedPathCorner>)(field?.GetValue(follower)??throw new InvalidOperationException("Native movement buffer unavailable"));}
 public double[][] Snapshot(){var rows=new double[points.Count][];for(int i=0;i<rows.Length;i++){var p=points[i];rows[i]=new[]{(double)p.Position.x,p.Position.y,p.Position.z,p.Time,p.Speed,p.Speed>=float.MaxValue*.5f?1d:0d};}return rows;}
 public int Count=>points.Count;
 public MovementPoint this[int index]{get{var p=points[index];return new MovementPoint(new Point3(p.Position.x,p.Position.y,p.Position.z),p.Speed>=float.MaxValue*.5f);}}
 public IEnumerator<MovementPoint> GetEnumerator(){for(int i=0;i<Count;i++)yield return this[i];}
 IEnumerator IEnumerable.GetEnumerator()=>GetEnumerator();
}
