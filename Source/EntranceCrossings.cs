using System;
using System.Collections.Generic;
namespace BoardingCounter;

public readonly struct Point3
{
 public readonly double X,Y,Z;
 public Point3(double x,double y,double z) { X=x;Y=y;Z=z; }
 public bool IsFinite => !(double.IsNaN(X)||double.IsNaN(Y)||double.IsNaN(Z)||double.IsInfinity(X)||double.IsInfinity(Y)||double.IsInfinity(Z));
 public static Point3 operator +(Point3 a,Point3 b)=>new Point3(a.X+b.X,a.Y+b.Y,a.Z+b.Z);
 public static Point3 operator -(Point3 a,Point3 b)=>new Point3(a.X-b.X,a.Y-b.Y,a.Z-b.Z);
 public static Point3 operator *(Point3 a,double b)=>new Point3(a.X*b,a.Y*b,a.Z*b);
 public double Dot(Point3 b)=>X*b.X+Y*b.Y+Z*b.Z;
 public double LengthSquared=>Dot(this);
}
public sealed class EntranceGate
{
 public readonly string Id,Name,Mode;
 public readonly Point3 Center,Inward;
 public readonly double HalfWidth,HeightTolerance;
 public EntranceGate(string id,string name,string mode,Point3 center,Point3 inward,double halfWidth=.5,double heightTolerance=.15) {
  if(!center.IsFinite||!inward.IsFinite||Math.Abs(inward.Y)>1e-8||inward.LengthSquared<1e-8||halfWidth<=0||heightTolerance<=0)throw new ArgumentException("Invalid entrance geometry");
  Id=id;Name=name;Mode=mode;Center=center;Inward=inward*(1/Math.Sqrt(inward.LengthSquared));HalfWidth=halfWidth;HeightTolerance=heightTolerance;
 }
 public static EntranceGate FromAnchors(string id,string name,string mode,Point3 outside,Point3 inside) {
  var inward=inside-outside;
  if(Math.Abs(inward.Y)>1e-8||Math.Abs(inward.LengthSquared-1)>1e-6)throw new ArgumentException("Station entrance anchors must be adjacent horizontal cells");
  return new EntranceGate(id,name,mode,(outside+inside)*.5,inward);
 }
 public double Distance(Point3 p)=>(p-Center).Dot(Inward);
 public int Side(Point3 p) { var d=Distance(p);return d>1e-6?1:d< -1e-6?-1:0; }
 public bool Contains(Point3 p) {
  var d=p-Center;
  return Math.Abs(d.X*Inward.Z-d.Z*Inward.X)<=HalfWidth+1e-6 && Math.Abs(d.Y)<=HeightTolerance+1e-6;
 }
 public bool SameGeometry(EntranceGate b)=>(Center-b.Center).LengthSquared<1e-12 && (Inward-b.Inward).LengthSquared<1e-12 && HalfWidth==b.HalfWidth && HeightTolerance==b.HeightTolerance;
}
public readonly struct EntranceCrossing
{
 public readonly EntranceGate Gate;
 public readonly bool Boarding;
 public readonly Point3 Position,From,To;
 public EntranceCrossing(EntranceGate gate,bool boarding,Point3 position,Point3 from,Point3 to) { Gate=gate;Boarding=boarding;Position=position;From=from;To=to; }
}
// Tracks a passenger's ACTUAL simulated positions. A navigation group or route
// corner alone is never a measurement input. The caller supplies each movement.
public sealed class EntranceTracker
{
 IReadOnlyList<EntranceGate> gates=Array.Empty<EntranceGate>();
 readonly Dictionary<string,int> sides=new Dictionary<string,int>();
 public Point3 Previous {get;private set;}
 public long SkippedDiscontinuities {get;private set;}
 public readonly double MaximumStep;
 public EntranceTracker(Point3 initial,IReadOnlyList<EntranceGate> gates,double maximumStep=2) {
  if(!initial.IsFinite||maximumStep<=0)throw new ArgumentException("Invalid baseline");
  Previous=initial;MaximumStep=maximumStep;Configure(gates);
 }
 public void Configure(IReadOnlyList<EntranceGate> next) {
  var old=new Dictionary<string,EntranceGate>();foreach(var g in gates)old.Add(g.Id,g);
  var kept=new Dictionary<string,int>();
  foreach(var g in next)kept.Add(g.Id,old.TryGetValue(g.Id,out var prior)&&g.SameGeometry(prior)&&sides.TryGetValue(g.Id,out var side)?side:g.Side(Previous));
  gates=next;sides.Clear();foreach(var p in kept)sides.Add(p.Key,p.Value);
 }
 public void ResetBaseline(Point3 next) {
  if(!next.IsFinite)throw new ArgumentException("Invalid reset position");
  SkippedDiscontinuities++;Previous=next;foreach(var gate in gates)sides[gate.Id]=gate.Side(next);
 }
 public void Observe(Point3 next,List<EntranceCrossing> crossings,bool verifiedContinuous=false) {
  if(!next.IsFinite)throw new ArgumentException("Invalid movement position");
  var from=Previous;var step=next-from;
  if(!verifiedContinuous&&step.LengthSquared>MaximumStep*MaximumStep) {
   ResetBaseline(next);return;
  }
  foreach(var gate in gates) {
   int before=sides[gate.Id],after=gate.Side(next);
   if(after==0)continue; // A point on the plane is contact, not a crossing.
   if(before!=0&&before!=after) {
    double a=gate.Distance(from),b=gate.Distance(next);
    double t=Math.Abs(a)<=1e-6?0:-a/(b-a);
    if(t>=-1e-6&&t<=1+1e-6) {
     var intersection=from+step*Math.Max(0,Math.Min(1,t));
     if(gate.Contains(intersection))crossings.Add(new EntranceCrossing(gate,after>0,intersection,from,next));
    }
   }
   sides[gate.Id]=after;
  }
  Previous=next;
 }
}
