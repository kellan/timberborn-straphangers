using System;
using System.Collections.Generic;
namespace BoardingCounter;

public readonly struct MovementPoint
{
 public readonly Point3 Position;
 public readonly bool Instant;
 public MovementPoint(Point3 position,bool instant=false){Position=position;Instant=instant;}
}
public readonly struct MovementSpan
{
 public readonly Point3 From,To;
 public readonly bool Instant;
 public MovementSpan(Point3 from,Point3 to,bool instant=false){From=from;To=to;Instant=instant;}
}
// Input is the game's actual per-tick path buffer. It has an optional last
// smoothing corner that has NOT been traversed. The logical transform is the
// authoritative endpoint: it must equal the last or penultimate buffer point.
public static class CompletedPath
{
 const double SamePointSquared=0;
 public static int Reduce(IReadOnlyList<MovementPoint> points,Point3 actualEnd,List<MovementSpan> spans) {
  spans.Clear();
  if(points.Count==0||!actualEnd.IsFinite)throw new InvalidOperationException("Missing native movement frame");
  int end=-1;
  for(int i=points.Count-1;i>=Math.Max(0,points.Count-2);i--)if((points[i].Position-actualEnd).LengthSquared<=SamePointSquared){end=i;break;}
  if(end<0)throw new InvalidOperationException("Native movement buffer does not end at the actual character position; observer cannot safely infer movement");
  Point3 start=points[0].Position,previous=start;
  if(!start.IsFinite)throw new InvalidOperationException("Invalid native movement start");
  for(int i=1;i<=end;i++) {
   var point=points[i];var next=point.Position;
   if(!next.IsFinite)throw new InvalidOperationException("Invalid native movement point");
   if(point.Instant) {
    Add(spans,start,previous,false);Add(spans,previous,next,true);start=previous=next;continue;
   }
   if((next-previous).LengthSquared==0)continue;
   if((previous-start).LengthSquared>0&&!StraightForward(start,previous,next)) {Add(spans,start,previous,false);start=previous;}
   previous=next;
  }
  Add(spans,start,previous,false);
  return end;
 }
 static bool StraightForward(Point3 a,Point3 b,Point3 c) {
  var u=b-a;var v=c-b;
  var cross=new Point3(u.Y*v.Z-u.Z*v.Y,u.Z*v.X-u.X*v.Z,u.X*v.Y-u.Y*v.X);
  // Only exact collinearity is coalesced. Turns, including out-and-back motion,
  // are retained; reduction never invents a diagonal across a bent path.
  return cross.LengthSquared==0&&u.Dot(v)>=0;
 }
 static void Add(List<MovementSpan> spans,Point3 a,Point3 b,bool instant){if((b-a).LengthSquared>0)spans.Add(new MovementSpan(a,b,instant));}
}
