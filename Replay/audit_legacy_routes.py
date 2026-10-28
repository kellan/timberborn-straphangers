#!/usr/bin/env python3
"""Audit v0.1.4/v0.1.5 movement telemetry; never interpolate legacy group logs."""
import argparse,csv,json,math
from pathlib import Path
from collections import defaultdict

def read_rows(folder,name,session):
 rows=[]
 for filename in ['previous-'+name,name]:
  p=folder/filename
  if not p.exists():continue
  data=p.read_bytes();data=data[:data.rfind(b'\n')+1]
  for row in csv.DictReader(data.decode('utf-8-sig').splitlines()):
   if row.get('session_id')==session and None not in row and all(v is not None for v in row.values()):rows.append(row)
 return rows

def audit(folder,session=None):
 folder=Path(folder)
 if session is None:
  p=folder/'observer-status.txt'
  if p.exists():
   session=next((l.split(': ',1)[1] for l in p.read_text().splitlines() if l.startswith('Session: ')),None)
 if not session:raise ValueError('No movement session ID found; legacy path-corner logs are not replayable position traces.')
 portals=defaultdict(dict)
 for r in read_rows(folder,'entrances.csv',session):portals[int(r['revision'])][r['station_id']]=r
 routes=read_rows(folder,'sample-routes.csv',session);routes.sort(key=lambda r:int(r['observation_id']))
 events=defaultdict(list)
 for r in read_rows(folder,'events.csv',session):events[(r['passenger_id'],int(r['observation_id']))].append(r)
 def vector(r,prefix):return tuple(float(r[prefix+'_'+a]) for a in 'xyz')
 def dot(a,b):return sum(x*y for x,y in zip(a,b))
 def sub(a,b):return tuple(x-y for x,y in zip(a,b))
 def side(d):return 1 if d>1e-6 else -1 if d< -1e-6 else 0
 states={};last={};last_revision={};errors=[];crossings=0;discontinuities=0;missing=0;native_walks=0;long_native_walks=0
 for r in routes:
  pid=r['passenger_id'];obs=int(r['observation_id']);revision=int(r['revision']);key=(pid,obs)
  if revision not in portals:
   missing+=1;continue
  a=vector(r,'from');b=vector(r,'to');delta=sub(b,a)
  if pid in last and sum(v*v for v in sub(last[pid],a))>1e-8:errors.append(f'Position trace gap for {pid}, observation {obs}')
  last[pid]=b;expected=[]
  kind=r.get('observation_kind','')
  if kind:
   if kind not in ('native-walk','instant-edge','baseline-reset'):errors.append(f'Unknown native movement kind at observation {obs}: {kind}')
   large=kind!='native-walk'
   if kind=='native-walk':
    native_walks+=1
    if dot(delta,delta)>4:long_native_walks+=1
  else:large=dot(delta,delta)>4
  if large:discontinuities+=1
  for gate_id,g in portals[revision].items():
   center=vector(g,'center');n=vector(g,'inward');width=float(g['half_width']);height=float(g['height_tolerance'])
   da=dot(sub(a,center),n);db=dot(sub(b,center),n);signature=(center,n,width,height);sk=(pid,gate_id)
   prior=states.get(sk);continuous=bool(prior and prior[0]==signature)
   if continuous:
    for v in range(last_revision.get(pid,revision),revision+1):
     historical=portals.get(v,{}).get(gate_id)
     if historical is None or (vector(historical,'center'),vector(historical,'inward'),float(historical['half_width']),float(historical['height_tolerance']))!=signature:
      continuous=False;break
   before=prior[1] if continuous else side(da)
   after=side(db)
   if not large and before and after and before!=after:
    t=0.0 if abs(da)<=1e-6 else -da/(db-da)
    if -1e-6<=t<=1+1e-6:
     q=tuple(a[i]+delta[i]*max(0,min(1,t)) for i in range(3));offset=sub(q,center)
     if abs(offset[0]*n[2]-offset[2]*n[0])<=width+1e-6 and abs(offset[1])<=height+1e-6:expected.append((gate_id,'entry' if after>0 else 'exit',q))
   states[sk]=(signature,after if after or large else before)
  last_revision[pid]=revision
  actual=events.get(key,[]);crossings+=len(expected)
  if sorted((g,e) for g,e,_ in expected)!=sorted((e['station_id'],e['event']) for e in actual):errors.append(f'Ledger differs from actual segment at observation {obs}: expected {[(g,e) for g,e,_ in expected]}, got {[(e["station_id"],e["event"]) for e in actual]}')
  if int(r['entries'])!=sum(e=='entry' for _,e,_ in expected) or int(r['exits'])!=sum(e=='exit' for _,e,_ in expected):errors.append(f'Sample totals differ at observation {obs}')
  if (r['discontinuity']=='1')!=large:errors.append(f'Discontinuity flag differs at observation {obs}')
  for gate_id,event,point in expected:
   row=next((e for e in actual if e['station_id']==gate_id and e['event']==event),None)
   if row is not None and sum(v*v for v in sub(vector(row,'cross'),point))>1e-8:errors.append(f'Crossing coordinate differs at observation {obs}')
 return dict(session_id=session,position_segments=len(routes),sampled_passengers=len(last),replayed_crossings=crossings,skipped_discontinuities=discontinuities,native_walk_segments=native_walks,native_walk_segments_over_two_tiles=long_native_walks,segments_missing_geometry=missing,errors=errors,telemetry_replay_passed=bool(routes) and crossings>0 and missing==0 and not errors,scope='Audits recorded actual movement versus doorway geometry and the crossing ledger. It does not establish that every inward entry completed a transport ride or validate unsampled passengers.')
if __name__=='__main__':
 parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--logs',required=True);parser.add_argument('--session');parser.add_argument('--output');a=parser.parse_args()
 try:
  result=audit(a.logs,a.session)
 except ValueError as e:
  result=dict(telemetry_replay_passed=False,error=str(e),requires_new_position_telemetry=True)
 text=json.dumps(result,indent=2);print(text)
 if a.output:Path(a.output).write_text(text+'\n')
 raise SystemExit(0 if result['telemetry_replay_passed'] else 1)
