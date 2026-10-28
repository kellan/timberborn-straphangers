using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Globalization;
using BoardingCounter;
var c=new Counter(10); c.Register("a","tubeway","A");
int checks=0;
void Check(bool ok,string message) { if(!ok)throw new Exception(message); checks++; Console.WriteLine("PASS: "+message); }
c.Record("a","tubeway","A",10,true,false);
Check(c.Average("a",11)==null,"partial first day excluded");
c.Record("a","tubeway","A",11,true,true);
c.Record("a","tubeway","A",11,false,true);
Check(c.Stations["a"].TotalBoardings==2 && c.Stations["a"].TotalArrivals==1,"boardings and arrivals remain separate");
Check(c.Stations["a"].BotBoardings==1,"bot split");
Check(c.Average("a",12)==1,"first complete day");
Check(c.Average("a",13)==0.5,"zero-traffic days included");
c.Record("a","tubeway","A",13,true,false);
Check(c.Average("a",13)==0.5,"current incomplete day excluded");
Check(c.ObservedFullDays(30)==7 && c.Average("a",30)==0,"rolling seven-day window");
c.Trim(30); Check(c.Stations["a"].TotalBoardings==3 && c.Stations["a"].Days.Count==0,"pruning retains totals");
var loaded=new Counter(30); loaded.Register("a","tubeway","A"); Check(loaded.Stations["a"].TotalBoardings==0,"new session has independent counts");

c.Register("new","zipline","New station",29); Check(c.Average("new",30)==null,"new stations exclude days before observation");

var validation=Environment.GetEnvironmentVariable("STRAPHANGERS_TEST_OUTPUT")??Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../artifacts/tests"));
Directory.CreateDirectory(validation);
var normals=new[]{new Point3(1,0,0),new Point3(0,0,1),new Point3(-1,0,0),new Point3(0,0,-1)};
var gates=Enumerable.Range(0,10).Select(i=>new EntranceGate("synthetic-"+i,"Synthetic station "+i,"tubeway",new Point3(i*20,0,i*20),normals[i%4])).ToArray();
Check(gates.Length==10,"constructed fixtures cover ten doors in all four orientations");
var hits=new List<EntranceCrossing>();
foreach(var g in gates) {
 var outside=g.Center-g.Inward*.5;var inside=g.Center+g.Inward*.5;
 var t=new EntranceTracker(outside,gates);hits.Clear();t.Observe(inside,hits);
 Check(hits.Count==1&&hits[0].Gate.Id==g.Id&&hits[0].Boarding,"directed entry at "+g.Name);
 Check((hits[0].Position-g.Center).LengthSquared<1e-12,"intersection lies on the exact doorway plane");
 hits.Clear();t.Observe(inside,hits);t.Observe(inside,hits);Check(hits.Count==0,"duplicate callbacks do not count twice");
 t.Observe(outside,hits);Check(hits.Count==1&&!hits[0].Boarding&&hits[0].Gate.Id==g.Id,"directed exit at "+g.Name);
 var tangent=new Point3(g.Inward.Z,0,-g.Inward.X);
 foreach(var offset in new[]{tangent*1.1,new Point3(0,1,0),new Point3(0,-1,0)}) {
  t=new EntranceTracker(outside+offset,gates);hits.Clear();t.Observe(inside+offset,hits);
  Check(hits.Count==0,"side/over/under passage is not a doorway crossing");
 }
}
var gate=gates[0];var out0=gate.Center-gate.Inward*.5;var in0=gate.Center+gate.Inward*.5;
var tracker=new EntranceTracker(out0,new[]{gate});hits.Clear();tracker.Observe(gate.Center,hits);Check(hits.Count==0,"touching the plane is not an entry");tracker.Observe(out0,hits);Check(hits.Count==0,"approach and retreat do not count");
tracker.Observe(gate.Center,hits);tracker.Observe(in0,hits);Check(hits.Count==1&&hits[0].Boarding,"crossing through an exact plane sample counts once");
tracker=new EntranceTracker(in0,new[]{gate});hits.Clear();tracker.Observe(in0,hits);Check(hits.Count==0,"load baseline inside does not create an entry");tracker.Observe(out0,hits);Check(hits.Count==1&&!hits[0].Boarding,"passenger restored inside can later exit");
tracker=new EntranceTracker(gate.Center-gate.Inward*3,new[]{gate});hits.Clear();tracker.Observe(in0,hits);Check(hits.Count==0&&tracker.SkippedDiscontinuities==1,"long position discontinuity resets the baseline without a crossing");tracker.Observe(in0,hits);Check(hits.Count==0,"no deferred phantom crossing after reset");
tracker=new EntranceTracker(in0,new[]{gate});hits.Clear();tracker.Observe(in0+gate.Inward*.4,hits);Check(hits.Count==0,"travel within the station platform does not count");
tracker=new EntranceTracker(out0,new[]{gate});hits.Clear();tracker.Observe(gate.Center,hits);tracker.Configure(new[]{new EntranceGate(gate.Id,gate.Name,gate.Mode,gate.Center,gate.Inward)});tracker.Observe(in0,hits);Check(hits.Count==1,"refreshing identical station geometry preserves plane-contact history");
tracker=new EntranceTracker(out0,Array.Empty<EntranceGate>());tracker.Observe(in0,hits);tracker.Configure(new[]{gate});hits.Clear();tracker.Observe(in0,hits);Check(hits.Count==0,"new station geometry establishes a baseline without retroactive entry");
var movedGate=new EntranceGate(gate.Id,gate.Name,gate.Mode,gate.Center+gate.Inward*10,gate.Inward);tracker.Configure(new[]{movedGate});hits.Clear();tracker.Observe(in0,hits);Check(hits.Count==0,"moving a station does not create a crossing");
var stacked=new EntranceGate("stacked","Stacked station","tubeway",gate.Center+new Point3(0,2,0),gate.Inward);tracker=new EntranceTracker(out0,new[]{gate,stacked});hits.Clear();tracker.Observe(in0,hits);Check(hits.Count==1&&hits[0].Gate.Id==gate.Id,"stacked station is distinguished by doorway height");
tracker=new EntranceTracker(out0,new[]{gate});hits.Clear();tracker.Observe(gate.Center-gate.Inward*5e-7,hits);tracker.Observe(gate.Center+gate.Inward*5e-7,hits);Check(hits.Count==0,"sub-epsilon jitter at the plane is ignored");tracker.Observe(in0,hits);Check(hits.Count==1,"small-step crossing completes after the contact tolerance");

var zipGate=new EntranceGate("zipline-fixture","Zipline station entrance","zipline",new Point3(10,0,10),new Point3(0,0,1));
tracker=new EntranceTracker(new Point3(10,0,9.5),new[]{zipGate});hits.Clear();tracker.Observe(new Point3(10,0,10.5),hits);Check(hits.Count==1&&hits[0].Gate.Mode=="zipline"&&hits[0].Boarding,"zipline pedestrian entrance uses the same gate rule");
tracker=new EntranceTracker(new Point3(10,3,9.5),new[]{zipGate});hits.Clear();tracker.Observe(new Point3(10,3,10.5),hits);Check(hits.Count==0,"elevated zipline pass-through does not count as pedestrian boarding");
tracker=new EntranceTracker(gate.Center-gate.Inward*.9,new[]{gate});hits.Clear();tracker.Observe(gate.Center+gate.Inward*.9,hits);Check(hits.Count==1,"fast contiguous movement crosses the doorway exactly once");

// An inward direction reversal must fail the directed-anchor regressions.
foreach(var g in gates) {
 var outside=g.Center-g.Inward*.5;var inside=g.Center+g.Inward*.5;
 var corrected=EntranceGate.FromAnchors(g.Id,g.Name,g.Mode,outside,inside);
 Check((corrected.Center-g.Center).LengthSquared<1e-12,"anchor midpoint matches the constructed door plane");
 Check((corrected.Inward-g.Inward).LengthSquared<1e-12,"anchor direction is inward");
 var t=new EntranceTracker(outside,new[]{corrected});hits.Clear();t.Observe(inside,hits,true);
 Check(hits.Count==1&&hits[0].Boarding,"outside-to-inside anchors produce entry");
 hits.Clear();t.Observe(outside,hits,true);Check(hits.Count==1&&!hits[0].Boarding,"inside-to-outside anchors produce exit");
}
var originGate=new EntranceGate("origin","Door at origin","tubeway",new Point3(0,0,0),new Point3(1,0,0));
var spans=new List<MovementSpan>();
MovementPoint M(double x,double y=0,double z=0,bool instant=false)=>new MovementPoint(new Point3(x,y,z),instant);
void ReplaySpans(EntranceTracker t) { foreach(var span in spans){if(span.Instant)t.ResetBaseline(span.To);else t.Observe(span.To,hits,verifiedContinuous:true);} }
int endIndex=CompletedPath.Reduce(new[]{M(-1),M(-.5),M(1)},new Point3(-.5,0,0),spans);
tracker=new EntranceTracker(new Point3(-1,0,0),new[]{originGate});hits.Clear();ReplaySpans(tracker);
Check(endIndex==1&&hits.Count==0,"untraversed future smoothing corner cannot create a boarding");
endIndex=CompletedPath.Reduce(new[]{M(-2e-6),M(-1.1e-6),M(-.2e-6)},new Point3(-1.1e-6,0,0),spans);Check(endIndex==1,"even a very close future smoothing point is excluded by exact native endpoint matching");
CompletedPath.Reduce(new[]{M(-.5),M(.5),M(-.5),M(-2)},new Point3(-.5,0,0),spans);
tracker=new EntranceTracker(new Point3(-.5,0,0),new[]{originGate});hits.Clear();ReplaySpans(tracker);
Check(spans.Count==2&&hits.Count==2&&hits[0].Boarding&&!hits[1].Boarding,"same-tick out-and-back motion retains both real crossings even with zero net displacement");
CompletedPath.Reduce(new[]{M(-1,0,-1),M(-1,0,1),M(1,0,1),M(2,0,1)},new Point3(1,0,1),spans);
tracker=new EntranceTracker(new Point3(-1,0,-1),new[]{originGate});hits.Clear();ReplaySpans(tracker);
Check(spans.Count==2&&hits.Count==0,"bent path around the door never becomes a false diagonal crossing");
tracker=new EntranceTracker(new Point3(-1,0,-1),new[]{originGate});hits.Clear();tracker.Observe(new Point3(1,0,1),hits,verifiedContinuous:true);Check(hits.Count==1,"bent-path regression would catch the old straight-chord approximation");
CompletedPath.Reduce(new[]{M(-3),M(-2),M(-1),M(0),M(1),M(2),M(3),M(5)},new Point3(3,0,0),spans);
tracker=new EntranceTracker(new Point3(-3,0,0),new[]{originGate});hits.Clear();ReplaySpans(tracker);
Check(spans.Count==1&&hits.Count==1&&hits[0].Boarding&&tracker.SkippedDiscontinuities==0,"long completed straight movement is counted without the old two-tile discard");
CompletedPath.Reduce(new[]{M(-.5),M(.5,instant:true),M(1),M(2)},new Point3(1,0,0),spans);
tracker=new EntranceTracker(new Point3(-.5,0,0),new[]{originGate});hits.Clear();ReplaySpans(tracker);
Check(hits.Count==0&&tracker.SkippedDiscontinuities==1,"explicit instant path edge establishes a baseline instead of a phantom entry");
bool invalidFrame=false;try{CompletedPath.Reduce(new[]{M(-1),M(1),M(2)},new Point3(99,0,0),spans);}catch(InvalidOperationException){invalidFrame=true;}Check(invalidFrame,"missing native endpoint fails closed instead of guessing a route");
CompletedPath.Reduce(new[]{M(-.5),M(-.5),M(0),M(.5),M(.5),M(2)},new Point3(.5,0,0),spans);
tracker=new EntranceTracker(new Point3(-.5,0,0),new[]{originGate});hits.Clear();ReplaySpans(tracker);Check(hits.Count==1,"duplicate native corners and an exact plane contact still yield one entry");

// Constructed long-distance workloads; these contain no private map recordings.
for(int i=1;i<=1440;i++) {
 var finish=new Point3(2+i*.01,0,0);int n=(int)Math.Ceiling(finish.X/.1);
 var points=Enumerable.Range(0,n+1).Select(k=>new MovementPoint(finish*(k/(double)n))).ToList();
 points.Add(new MovementPoint(finish+new Point3(1,0,0)));
 CompletedPath.Reduce(points,finish,spans);
 tracker=new EntranceTracker(new Point3(0,0,0),Array.Empty<EntranceGate>());hits.Clear();ReplaySpans(tracker);
 if(tracker.SkippedDiscontinuities!=0)throw new Exception("Constructed continuous workload was discarded");
}
Check(true,"1440 constructed long-distance paths avoid false movement resets");
PersistenceTests.Run(Check);
#if TIMBERBORN_INTEGRATION
NativeSaveTests.Run(Check);
#endif
RecordingTests.Run(validation,Check);
Console.WriteLine("RESULT: "+checks+" checks passed. Fixtures are constructed; live-game validation is separate.");
