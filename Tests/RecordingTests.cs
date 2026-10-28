using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using BoardingCounter;

public static class RecordingTests
{
 public static void Run(string validation,Action<bool,string> check) {
  string reference=SyntheticReference.Json();
  string auditor=File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Replay/audit_recording.py"));
  using var rdoc=JsonDocument.Parse(reference);string hash=rdoc.RootElement.GetProperty("movement_assembly_sha256").GetString()!;
  var metadata=new{fixture=true,mod_version="0.1.7",movement_assembly_sha256=hash,game_version="constructed-fixture",mod_sha256=new string('0',64)};
  Func<object,string> json=o=>JsonSerializer.Serialize(o);
  string root=Path.Combine(Path.GetTempPath(),"boarding-replay-tests-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
  try {
   var a=new EntranceGate("A","Station A","tubeway",new Point3(1.5,0,0),new Point3(0,0,1));
   var b=new EntranceGate("B","Station B","tubeway",new Point3(.5,0,11),new Point3(0,0,-1));
   var definitions=new[]{Definition(a,new[]{0,0,0},"Cw0"),Definition(b,new[]{1,10,0},"Cw180")};
   var gates=new[]{a,b};var outsideA=a.Center-a.Inward*.5;var insideA=a.Center+a.Inward*.5;var outsideB=b.Center-b.Inward*.5;var insideB=b.Center+b.Inward*.5;
   string session=new string('1',32);
   using(var rec=new ReplayRecording(root,session,"0.1.7",1,metadata,reference,auditor,json)) {
    rec.Geometry(0,Array.Empty<ReplayStation>(),1,0);rec.Geometry(1,definitions,1,0);
    rec.RegisterActor("traveler",false,outsideA,1,0);rec.RegisterActor("bot",true,outsideA,1,0);rec.RegisterActor("waiting-passenger",false,new Point3(20,0,20),1,0);
    check(rec.Wants("waiting-passenger"),"recording includes non-crossing passengers before their first entry");
    rec.TubeStatus("ready","fixture",1,0);
    var waiting=new ReplayOccupant{id="waiting-passenger",bot=false};var passenger=new ReplayOccupant{id="traveler",bot=false};var botPassenger=new ReplayOccupant{id="bot",bot=true};
    rec.Tube("tube-one",new[]{1,1,0},"baseline",new[]{waiting},1,0,0,3);
    rec.Tube("tube-two",new[]{1,2,0},"baseline",Array.Empty<ReplayOccupant>(),1,0,0,3);
    var traveler=new EntranceTracker(outsideA,gates);var bot=new EntranceTracker(outsideA,gates);long obs=0;
    var outbound=new[]{outsideA,insideA,new Point3(1.5,0,10.5),insideB,outsideB};var inbound=outbound.Reverse().ToArray();
    rec.Frame(Frame("traveler",traveler,outbound,2,ref obs));rec.Frame(Frame("bot",bot,outbound,2,ref obs));
    rec.Frame(Frame("traveler",traveler,inbound,3,ref obs));rec.Frame(Frame("bot",bot,inbound,3,ref obs));
    // Same-station construction excursion, with an interval outside any tube.
    var enter=Frame("traveler",traveler,new[]{outsideA,insideA},3,ref obs);enter.render_frame=100;rec.Frame(enter);
    rec.Tube("tube-one",new[]{1,1,0},"change",new[]{waiting,passenger},3,.25,101,3);
    rec.Tube("tube-one",new[]{1,1,0},"change",new[]{waiting,passenger,botPassenger},3,.25,102,3);
    rec.Tube("tube-one",new[]{1,1,0},"change",new[]{waiting,botPassenger},3,.25,103,3);
    rec.Tube("tube-two",new[]{1,2,0},"change",new[]{passenger},3,.25,103,3);
    rec.Tube("tube-two",new[]{1,2,0},"change",Array.Empty<ReplayOccupant>(),3,.25,104,3);
    rec.Tube("tube-one",new[]{1,1,0},"change",new[]{waiting,botPassenger,passenger},3,.25,105,3);
    rec.Tube("tube-one",new[]{1,1,0},"change",new[]{waiting,botPassenger},3,.25,105,3);
    var exit=Frame("traveler",traveler,new[]{insideA,outsideA},3,ref obs);exit.render_frame=106;rec.Frame(exit);
    rec.Tube("tube-one",new[]{1,1,0},"remove",Array.Empty<ReplayOccupant>(),3,.3,107,3);
    rec.Tube("tube-two",new[]{1,2,0},"remove",Array.Empty<ReplayOccupant>(),3,.3,107,3);
    rec.Geometry(2,Array.Empty<ReplayStation>(),3,.4);rec.Geometry(3,definitions,3,.4);
    rec.Seal("fixture-complete",3,.5);
    check(rec.Status=="sealed","complete-prefix recording seals without live gameplay");
    using var manifest=JsonDocument.Parse(File.ReadAllText(Path.Combine(rec.DirectoryPath,"manifest.json")));
    check(manifest.RootElement.GetProperty("frames").GetInt32()==6&&manifest.RootElement.GetProperty("coverage").GetString()=="all","recording declares complete actor coverage and frame count");
    check(manifest.RootElement.GetProperty("trace_sha256").GetString()==ReplayRecording.HashFile(Path.Combine(rec.DirectoryPath,"trace.jsonl.gz")),"sealed recording has a matching trace checksum");
    check(manifest.RootElement.GetProperty("tube_records").GetInt64()==11,"tube baselines, per-passenger changes, transfers and deletion share the ordered replay");
    using(var f=File.OpenRead(Path.Combine(rec.DirectoryPath,"trace.jsonl.gz")))using(var gz=new GZipStream(f,CompressionMode.Decompress))using(var reader=new StreamReader(gz)) {
     var rows=reader.ReadToEnd().Split('\n',StringSplitOptions.RemoveEmptyEntries).Select(x=>JsonDocument.Parse(x)).ToArray();
     var baseline=rows.First(x=>x.RootElement.GetProperty("type").GetString()=="tube").RootElement.GetProperty("data");
     check(baseline.GetProperty("occupants").GetArrayLength()==1&&baseline.GetProperty("added").GetArrayLength()==0,"already occupied baseline does not fabricate a tube entry");
     var changes=rows.Where(x=>x.RootElement.GetProperty("type").GetString()=="tube").Select(x=>x.RootElement.GetProperty("data")).Where(x=>x.GetProperty("action").GetString()=="change").ToArray();
     check(changes[0].GetProperty("added")[0].GetString()=="traveler"&&changes[1].GetProperty("added")[0].GetString()=="bot","additional passengers in an already lit tube retain distinct IDs and bot types");
     foreach(var row in rows)row.Dispose();
    }
    var dest=Path.Combine(validation,"offline-fixture");if(Directory.Exists(dest))Directory.Delete(dest,true);Directory.CreateDirectory(dest);
    foreach(string name in new[]{"manifest.json","trace.jsonl.gz","station-reference.json","audit_recording.py"})File.Copy(Path.Combine(rec.DirectoryPath,name),Path.Combine(dest,name));
   }
   using(var limited=new ReplayRecording(root,new string('2',32),"0.1.7",1,metadata,reference,auditor,json,actorLimit:1)) {
    limited.Geometry(1,definitions,1,0);limited.RegisterActor("first",false,outsideA,1,0);limited.RegisterActor("not-captured",false,outsideA,1,0);
    check(limited.Wants("first")&&!limited.Wants("not-captured"),"actor limit is bounded and selection does not depend on detected crossings");limited.Seal("fixture-complete",1,.5);
    using var m=JsonDocument.Parse(File.ReadAllText(Path.Combine(limited.DirectoryPath,"manifest.json")));
    check(m.RootElement.GetProperty("coverage").GetString()=="partial"&&m.RootElement.GetProperty("omitted_actors").GetInt32()==1,"partial coverage is explicitly reported");
   }
   using(var capped=new ReplayRecording(root,new string('3',32),"0.1.7",1,metadata,reference,auditor,json,rawLimit:2500)) {
    capped.Geometry(1,definitions,1,0);capped.RegisterActor("traveler",false,outsideA,1,0);long obs=0;
    capped.Frame(Frame("traveler",new EntranceTracker(outsideA,gates),new[]{outsideA,insideA,new Point3(1.5,0,10.5),insideB,outsideB},2,ref obs));
    check(capped.Status=="sealed"&&capped.Reason=="capacity","size limit seals a complete prefix instead of independently rotating inputs");
    using var f=File.OpenRead(Path.Combine(capped.DirectoryPath,"trace.jsonl.gz"));using var gzip=new GZipStream(f,CompressionMode.Decompress);using var reader=new StreamReader(gzip);string text=reader.ReadToEnd();
    check(text.Split('\n',StringSplitOptions.RemoveEmptyEntries).Select(x=>JsonDocument.Parse(x).RootElement.GetProperty("type").GetString()).Last()=="end","capacity-limited trace has a valid gzip trailer and end marker");
    var dest=Path.Combine(validation,"capacity-fixture");if(Directory.Exists(dest))Directory.Delete(dest,true);Directory.CreateDirectory(dest);foreach(string name in new[]{"manifest.json","trace.jsonl.gz","station-reference.json","audit_recording.py"})File.Copy(Path.Combine(capped.DirectoryPath,name),Path.Combine(dest,name));
   }
   using(var longCapture=new ReplayRecording(root,new string('5',32),"0.1.11",1,new{fixture=true,mod_version="0.1.11",movement_assembly_sha256=hash},reference,auditor,json)) {
    longCapture.Geometry(1,definitions,1,0);longCapture.RegisterActor("traveler",false,outsideA,1,0);var tracker=new EntranceTracker(outsideA,gates);long obs=0;
    for(int day=2;day<=13;day++)longCapture.Frame(Frame("traveler",tracker,day%2==0?new[]{outsideA,insideA}:new[]{insideA,outsideA},day,ref obs));
    longCapture.Seal("fixture-complete",13,.5);
    var dest=Path.Combine(validation,"long-capture-fixture");if(Directory.Exists(dest))Directory.Delete(dest,true);Directory.CreateDirectory(dest);
    foreach(string name in new[]{"manifest.json","trace.jsonl.gz","station-reference.json","audit_recording.py"})File.Copy(Path.Combine(longCapture.DirectoryPath,name),Path.Combine(dest,name));
    check(longCapture.Status=="sealed","a replay window longer than seven days seals its complete daily history");
   }
   using(var after=new ReplayRecording(root,new string('4',32),"0.1.7",1,metadata,reference,auditor,json)){after.Seal("fixture-complete",1,0);}
   check(!Directory.Exists(Path.Combine(root,session))&&Directory.GetDirectories(root).Length==3,"recording retention preserves only the last three owned recordings");
  } finally {Directory.Delete(root,true);}
 }
 static ReplayStation Definition(EntranceGate g,int[] placement,string orientation)=>new ReplayStation{id=g.Id,name=g.Name,mode=g.Mode,template="TubewayStation.IronTeeth",placement=placement,orientation=orientation,outside=ReplayRecording.Vector(g.Center-g.Inward*.5),inside=ReplayRecording.Vector(g.Center+g.Inward*.5),center=ReplayRecording.Vector(g.Center),inward=ReplayRecording.Vector(g.Inward),half_width=g.HalfWidth,height_tolerance=g.HeightTolerance};
 static ReplayFrame Frame(string actor,EntranceTracker tracker,Point3[] vertices,int day,ref long obs) {
  var raw=new List<MovementPoint>{new MovementPoint(vertices[0])};
  for(int i=1;i<vertices.Length;i++) {
   var delta=vertices[i]-vertices[i-1];int n=(int)Math.Ceiling(Math.Sqrt(delta.LengthSquared)/.1);
   for(int k=1;k<=n;k++)raw.Add(new MovementPoint(vertices[i-1]+delta*(k/(double)n)));
  }
  var actual=raw[raw.Count-1].Position;raw.Add(new MovementPoint(actual+new Point3(0,0,2)));
  var spans=new List<MovementSpan>();int end=CompletedPath.Reduce(raw,actual,spans);var steps=new List<ReplayStep>();
  foreach(var span in spans) {
   var from=tracker.Previous;var found=new List<EntranceCrossing>();tracker.Observe(span.To,found,verifiedContinuous:true);
   steps.Add(new ReplayStep{observation=++obs,kind="native-walk",from=ReplayRecording.Vector(from),to=ReplayRecording.Vector(span.To),events=found.Select(c=>new ReplayEvent{station=c.Gate.Id,direction=c.Boarding?"entry":"exit",cross=ReplayRecording.Vector(c.Position)}).ToArray()});
  }
  return new ReplayFrame{actor=actor,day=day,progress=.25,revision=1,render_frame=(int)obs,time_scale=3,actual_end=ReplayRecording.Vector(actual),native=raw.Select((p,i)=>new[]{p.Position.X,p.Position.Y,p.Position.Z,i*.01,1d,p.Instant?1d:0d}).ToArray(),claimed_completed_end=end,steps=steps.ToArray()};
 }
}
