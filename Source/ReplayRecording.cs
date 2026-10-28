using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
namespace BoardingCounter;

public sealed class ReplayStation
{
 public string id {get;set;}="";
 public string name {get;set;}="";
 public string mode {get;set;}="";
 public string template {get;set;}="";
 public int[] placement {get;set;}=Array.Empty<int>();
 public string orientation {get;set;}="";
 public double[] outside {get;set;}=Array.Empty<double>();
 public double[] inside {get;set;}=Array.Empty<double>();
 public double[] center {get;set;}=Array.Empty<double>();
 public double[] inward {get;set;}=Array.Empty<double>();
 public double half_width {get;set;}
 public double height_tolerance {get;set;}
}
public sealed class ReplayOccupant
{
 public string id {get;set;}="";
 public bool bot {get;set;}
}
public sealed class ReplayEvent
{
 public string station {get;set;}="";
 public string direction {get;set;}="";
 public double[] cross {get;set;}=Array.Empty<double>();
}
public sealed class ReplayStep
{
 public long observation {get;set;}
 public string kind {get;set;}="";
 public double[] from {get;set;}=Array.Empty<double>();
 public double[] to {get;set;}=Array.Empty<double>();
 public ReplayEvent[] events {get;set;}=Array.Empty<ReplayEvent>();
}
public sealed class ReplayFrame
{
 public string actor {get;set;}="";
 public int day {get;set;}
 public double progress {get;set;}
 public int revision {get;set;}
 public int render_frame {get;set;}
 public double time_scale {get;set;}
 public double[] actual_end {get;set;}=Array.Empty<double>();
 // Each row: x, y, z, animation time, movement speed, explicit instant flag.
 public double[][] native {get;set;}=Array.Empty<double[]>();
 public int claimed_completed_end {get;set;}
 public ReplayStep[] steps {get;set;}=Array.Empty<ReplayStep>();
 public string? error {get;set;}
}

// One ordered, compressed, complete-prefix recording; it never rotates files
// independently. Recording failures do not interrupt the live game counter.
public sealed class ReplayRecording : IDisposable
{
 public const string Owner="BoardingCounter.Replay";
 readonly Func<object,string> serialize;
 readonly string directory,session,version;
 readonly string startedUtc=DateTime.UtcNow.ToString("O",CultureInfo.InvariantCulture);
 readonly long rawLimit,compressedLimit;
 readonly int actorLimit;
 readonly Action<string>? onError;
 readonly HashSet<string> seen=new HashSet<string>();
 readonly HashSet<string> captured=new HashSet<string>();
 readonly HashSet<string> active=new HashSet<string>();
 readonly HashSet<string> omitted=new HashSet<string>();
 readonly Counter counts;
 readonly Dictionary<string,bool> bots=new Dictionary<string,bool>();
 FileStream? trace,recordingLock;
 StreamWriter? writer;
 long sequence,rawBytes,frames,tubeRecords;
 readonly Dictionary<string,ReplayOccupant[]> tubes=new Dictionary<string,ReplayOccupant[]>();
 int endDay;
 double endProgress;
 bool closed;
 object metadata;
 public string DirectoryPath=>directory;
 public int ActorsCaptured=>captured.Count;
 public long FramesCaptured=>frames;
 public long TubeRecordsCaptured=>tubeRecords;
 public string Coverage=>omitted.Count==0?"all":"partial";
 public bool Capturing=>!closed&&writer!=null;
 public string Status {get;private set;}="recording";
 public string Reason {get;private set;}="";
 public string? LastError {get;private set;}
 public bool Wants(string actor)=>Capturing&&active.Contains(actor);
 public ReplayRecording(string root,string session,string version,int firstDay,object metadata,string referenceJson,string auditorSource,Func<object,string> serialize,Action<string>? onError=null,long rawLimit=128L*1024*1024,long compressedLimit=16L*1024*1024,int actorLimit=64,int keep=3) {
  if(!Guid.TryParseExact(session,"N",out _)||actorLimit<1||keep<1||rawLimit<1||compressedLimit<1)throw new ArgumentException("Invalid recording settings");
  this.session=session;this.version=version;this.serialize=serialize;this.onError=onError;this.rawLimit=rawLimit;this.compressedLimit=compressedLimit;this.actorLimit=actorLimit;this.metadata=metadata;endDay=firstDay;counts=new Counter(firstDay);
  System.IO.Directory.CreateDirectory(root);Prune(root,keep);directory=Path.Combine(root,session);
  if(System.IO.Directory.Exists(directory))throw new IOException("A recording with this session ID already exists");
  System.IO.Directory.CreateDirectory(directory);
  try {
   recordingLock=new FileStream(Path.Combine(directory,"recording.lock"),FileMode.CreateNew,FileAccess.ReadWrite,FileShare.None);
   File.WriteAllText(Path.Combine(directory,"station-reference.json"),referenceJson,new UTF8Encoding(false));
   File.WriteAllText(Path.Combine(directory,"audit_recording.py"),auditorSource,new UTF8Encoding(false));
   trace=new FileStream(Path.Combine(directory,"trace.jsonl.gz"),FileMode.CreateNew,FileAccess.Write,FileShare.Read);
   var gzip=new GZipStream(trace,CompressionLevel.Fastest,leaveOpen:true);
   writer=new StreamWriter(gzip,new UTF8Encoding(false),8192,leaveOpen:false);
   Write("header",new{schema=3,session,mod_version=version,first_day=firstDay,metadata,actor_limit=actorLimit,raw_limit_bytes=rawLimit,compressed_limit_bytes=compressedLimit,utc_started=startedUtc,reference_sha256=HashFile(Path.Combine(directory,"station-reference.json")),auditor_sha256=HashFile(Path.Combine(directory,"audit_recording.py"))},force:true);
   Manifest(null,null);
  }catch{writer?.Dispose();trace?.Dispose();recordingLock?.Dispose();throw;}
 }
 public void RegisterActor(string id,bool bot,Point3 position,int day,double progress) {
  if(!Capturing)return;
  Stamp(day,progress);bool selected=active.Count<actorLimit;
  Safe(()=>{if(Write("actor",new{action="add",id,bot,captured=selected,position=Vector(position),day,progress})){seen.Add(id);bots[id]=bot;if(selected){active.Add(id);captured.Add(id);}else omitted.Add(id);}});
 }
 public void RemoveActor(string id,int day,double progress) {
  if(!Capturing)return;Stamp(day,progress);
  bool selected=active.Contains(id);Safe(()=>{if(Write("actor",new{action="remove",id,captured=selected,day,progress}))active.Remove(id);});
 }
 public void Geometry(int revision,IReadOnlyList<ReplayStation> gates,int day,double progress) {
  if(!Capturing)return;Stamp(day,progress);
  Safe(()=>{if(Write("geometry",new{revision,gates,day,progress}))foreach(var g in gates)counts.Register(g.id,g.mode,g.name,day);});
 }
 public void Frame(ReplayFrame frame) {
  if(!Wants(frame.actor))return;Stamp(frame.day,frame.progress);
  Safe(()=>{
   if(!Write("frame",frame))return;
   frames++;
   foreach(var step in frame.steps)foreach(var e in step.events) {
    if(!counts.Stations.TryGetValue(e.station,out var station))throw new InvalidDataException("Crossing references unrecorded station");
    counts.Record(e.station,station.Mode,station.Name,frame.day,e.direction=="entry",bots[frame.actor]);
   }
   if(frame.error!=null){Seal("observer-error",frame.day,frame.progress);return;}
   if(rawBytes>=rawLimit||trace!.Position>=compressedLimit)Seal("capacity",frame.day,frame.progress);
  });
 }
 // Full membership snapshots plus explicit deltas make missed/reordered tube
 // events detectable offline. Baselines never synthesize passenger entries.
 public void TubeStatus(string status,string detail,int day,double progress) {
  if(!Capturing)return;Stamp(day,progress);
  Safe(()=>Write("tube-status",new{status,detail,day,progress,source="Tube.VisitorsChanged; displayed CharacterModel.Position"}));
 }
 public void Tube(string id,int[] grid,string action,ReplayOccupant[] occupants,int day,double progress,int renderFrame,double timeScale) {
  if(!Capturing)return;Stamp(day,progress);
  Safe(()=>{
   occupants=occupants.OrderBy(o=>o.id,StringComparer.Ordinal).ToArray();
   if(occupants.Select(o=>o.id).Distinct().Count()!=occupants.Length)throw new InvalidDataException("Duplicate tube passenger");
   tubes.TryGetValue(id,out var previous);
   if((action=="baseline")!=(previous==null)||!(action=="baseline"||action=="change"||action=="remove"))throw new InvalidDataException("Invalid tube lifecycle");
   if(action=="remove"&&occupants.Length!=0)throw new InvalidDataException("Removed tube has occupants");
   var before=previous??Array.Empty<ReplayOccupant>();
   var added=action=="baseline"?Array.Empty<string>():occupants.Where(o=>!before.Any(b=>b.id==o.id)).Select(o=>o.id).ToArray();
   var removed=before.Where(o=>!occupants.Any(n=>n.id==o.id)).Select(o=>o.id).ToArray();
   if(Write("tube",new{id,grid,action,occupants,added,removed,day,progress,render_frame=renderFrame,time_scale=timeScale})) {
    tubeRecords++;if(action=="remove")tubes.Remove(id);else tubes[id]=occupants;
    if(trace!.Position>=compressedLimit)Seal("capacity",day,progress);
   }
  });
 }
 public void Flush() {
  if(!Capturing)return;Safe(()=>{writer!.Flush();Manifest(null,null);if(trace!.Position>=compressedLimit)Seal("capacity",endDay,endProgress);});
 }
 public void Seal(string reason,int day,double progress) {
  if(!Capturing)return;Stamp(day,progress);
  try {
   Reason=reason;Status=reason=="observer-error"||reason=="recording-error"?"failed":"sealed";
   var totals=counts.Stations.OrderBy(p=>p.Key).Select(p=>new{id=p.Key,mode=p.Value.Mode,name=p.Value.Name,first_day=p.Value.FirstDay,entries=p.Value.TotalBoardings,exits=p.Value.TotalArrivals,bot_entries=p.Value.BotBoardings,daily=p.Value.Days.OrderBy(d=>d.Key).Select(d=>new{day=d.Key,entries=d.Value.Boardings,exits=d.Value.Arrivals}).ToArray(),average=counts.Average(p.Key,endDay),average_days=counts.ObservedFullDays(p.Key,endDay)}).ToArray();
   Write("end",new{reason,status=Status,frames,tube_records=tubeRecords,seen_actors=seen.Count,captured_actors=captured.Count,omitted_actors=omitted.Count,coverage=omitted.Count==0?"all":"partial",day=endDay,progress=endProgress,totals},force:true);
   closed=true;writer!.Dispose();writer=null;trace!.Dispose();trace=null;
   Manifest(HashFile(Path.Combine(directory,"trace.jsonl.gz")),HashFile(Path.Combine(directory,"station-reference.json")));
  }catch(Exception e){closed=true;Status="failed";LastError=e.ToString();onError?.Invoke(LastError);try{writer?.Dispose();trace?.Dispose();}catch{}try{Manifest(null,null);}catch{}}
  finally{recordingLock?.Dispose();recordingLock=null;}
 }
 bool Write(string type,object data,bool force=false) {
  if(writer==null||closed)return false;
  string json=serialize(new{seq=sequence+1,type,data});long bytes=Encoding.UTF8.GetByteCount(json)+1;
  if(!force&&(bytes>2L*1024*1024||rawBytes+bytes>rawLimit)) {Seal(bytes>2L*1024*1024?"record-limit":"capacity",endDay,endProgress);return false;}
  writer.Write(json);writer.Write('\n');sequence++;rawBytes+=bytes;writer.Flush();return true;
 }
 void Safe(Action action){try{action();}catch(Exception e){LastError=e.ToString();onError?.Invoke(LastError);Seal("recording-error",endDay,endProgress);}}
 void Stamp(int day,double progress){endDay=day;endProgress=progress;}
 void Manifest(string? traceHash,string? referenceHash) {
  var value=new{owner=Owner,schema=3,session,mod_version=version,utc_started=startedUtc,status=Status,reason=Reason,records=sequence,frames,tube_records=tubeRecords,raw_bytes=rawBytes,compressed_bytes=File.Exists(Path.Combine(directory,"trace.jsonl.gz"))?new FileInfo(Path.Combine(directory,"trace.jsonl.gz")).Length:0,coverage=omitted.Count==0?"all":"partial",seen_actors=seen.Count,captured_actors=captured.Count,omitted_actors=omitted.Count,trace_sha256=traceHash,reference_sha256=referenceHash,auditor_sha256=HashFile(Path.Combine(directory,"audit_recording.py")),metadata,error=LastError};
  string target=Path.Combine(directory,"manifest.json");File.WriteAllText(target+".tmp",serialize(value),new UTF8Encoding(false));if(File.Exists(target))File.Delete(target);File.Move(target+".tmp",target);
 }
 static void Prune(string root,int keep) {
  var owned=System.IO.Directory.GetDirectories(root).Where(d=>Guid.TryParseExact(Path.GetFileName(d),"N",out _)&&File.Exists(Path.Combine(d,"manifest.json"))&&File.ReadAllText(Path.Combine(d,"manifest.json")).Contains("\"owner\":\""+Owner+"\"")).OrderBy(d=>System.IO.Directory.GetLastWriteTimeUtc(d)).ToList();
  int remove=owned.Count-keep+1;
  foreach(var d in owned) {
   if(remove<=0)break;
   var allowed=new HashSet<string>{"manifest.json","manifest.json.tmp","trace.jsonl.gz","station-reference.json","audit_recording.py","recording.lock"};
   if(System.IO.Directory.GetDirectories(d).Length!=0||System.IO.Directory.GetFiles(d).Any(f=>!allowed.Contains(Path.GetFileName(f))))continue;
   try{using(var guard=new FileStream(Path.Combine(d,"recording.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None)){}System.IO.Directory.Delete(d,true);remove--;}catch(IOException){}
  }
  if(remove>0)throw new IOException("Recording retention budget is full; active or unrecognized files were preserved");
 }
 public static string HashFile(string path){using(var sha=SHA256.Create())using(var f=File.OpenRead(path))return BitConverter.ToString(sha.ComputeHash(f)).Replace("-","").ToLowerInvariant();}
 public static double[] Vector(Point3 p)=>new[]{p.X,p.Y,p.Z};
 public void Dispose(){Seal("session-ended",endDay,endProgress);}
}
