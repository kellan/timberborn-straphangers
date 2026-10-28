#!/usr/bin/env python3
"""Package and replay a sealed recording without Timberborn or a .NET runtime."""
import argparse,collections,gzip,hashlib,io,json,math,pathlib,tempfile,zipfile
from pathlib import Path
OWNER='BoardingCounter.Replay'
MAX_TRACE=24*1024*1024
MAX_RAW=160*1024*1024

class Source:
 def __init__(self,path):
  self.path=Path(path);self.zip=None
  if self.path.is_file():
   self.zip=zipfile.ZipFile(self.path)
   for item in self.zip.infolist():
    if item.is_dir() or '/' in item.filename or '\\' in item.filename or item.filename in ('.','..') or item.file_size>MAX_TRACE:raise ValueError('Unsafe or oversized bundle member')
  self.names=set(self.zip.namelist() if self.zip else (p.name for p in self.path.iterdir() if p.is_file()))
 def open(self,name):
  if name not in self.names or '/' in name or '\\' in name or name in ('.','..'):raise ValueError('Invalid bundle file reference')
  return self.zip.open(name) if self.zip else (self.path/name).open('rb')
 def data(self,name,limit=1024*1024):
  with self.open(name) as f:
   b=f.read(limit+1)
  if len(b)>limit:raise ValueError('Oversized metadata: '+name)
  return b
 def hash(self,name):
  h=hashlib.sha256();n=0
  with self.open(name) as f:
   while True:
    b=f.read(1024*1024)
    if not b:break
    n+=len(b)
    if n>MAX_TRACE:raise ValueError('Oversized recording member')
    h.update(b)
  return h.hexdigest()
 def close(self):
  if self.zip:self.zip.close()

def load_json(data):return json.loads(data,parse_constant=lambda value:(_ for _ in ()).throw(ValueError('Nonstandard JSON constant '+value)))
def point(p):
 if len(p)!=3:raise ValueError('Invalid point shape')
 p=tuple(float(v) for v in p)
 if not all(math.isfinite(v) for v in p):raise ValueError('Invalid coordinate')
 return p

def sub(a,b):return tuple(x-y for x,y in zip(a,b))
def dot(a,b):return sum(x*y for x,y in zip(a,b))
def norm2(a):return dot(a,a)
def close(a,b,tol=1e-9):return norm2(sub(a,b))<=tol*tol
def side(g,p):
 d=dot(sub(p,g['center']),g['inward'])
 return 1 if d>1e-6 else -1 if d< -1e-6 else 0

def rotate(p,o):
 x,y,z=p
 if o=='Cw0':return x,y,z
 if o=='Cw90':return y,-x,z
 if o=='Cw180':return -x,-y,z
 if o=='Cw270':return -y,x,z
 raise ValueError('Unsupported rotation '+str(o))

def check_geometry(g,reference):
 template=g['template'];spec=reference['templates'].get(template)
 if spec is None:raise ValueError('No independent blueprint reference for '+template)
 if spec['block'].get('Flippable'):raise ValueError('Flippable station geometry needs an explicit reference')
 entrance=spec['block']['Entrance']['Coordinates'];outside=tuple(entrance[k] for k in 'XYZ');inside=[]
 for group in spec['navigation']['EdgeGroups']:
  if group['Group']['UseGroup']:continue
  for edge in group['AddedEdges']:
   a=tuple(edge['Start'][k] for k in 'XYZ');b=tuple(edge['End'][k] for k in 'XYZ')
   if a==outside:inside.append(b)
   elif b==outside:inside.append(a)
 if len(set(inside))!=1:raise ValueError('Ambiguous authored pedestrian entrance')
 def world(local):
  r=rotate(local,g['orientation']);v=tuple(r[i]+g['placement'][i] for i in range(3));return v[0]+.5,v[2],v[1]+.5
 a=world(outside);b=world(inside[0]);center=tuple((a[i]+b[i])/2 for i in range(3));inward=sub(b,a)
 for name,want in [('outside',a),('inside',b),('center',center),('inward',inward)]:
  if not close(point(g[name]),want):raise ValueError('Blueprint mismatch for '+g['id']+' '+name)
 if abs(g['half_width']-.5)>1e-12 or abs(g['height_tolerance']-.15)>1e-12:raise ValueError('Unknown doorway measurement rule')
 return dict(g,center=center,inward=inward)

def crossing(g,a,b,before):
 after=side(g,b)
 if not after:return None,before
 result=None
 if before and before!=after:
  da=dot(sub(a,g['center']),g['inward']);db=dot(sub(b,g['center']),g['inward']);t=0 if abs(da)<=1e-6 else -da/(db-da)
  if -1e-6<=t<=1+1e-6:
   q=tuple(a[i]+(b[i]-a[i])*max(0,min(1,t)) for i in range(3));d=sub(q,g['center']);n=g['inward']
   if abs(d[0]*n[2]-d[2]*n[0])<=g['half_width']+1e-6 and abs(d[1])<=g['height_tolerance']+1e-6:result={'station':g['id'],'direction':'entry' if after>0 else 'exit','cross':q}
 return result,after

def event_equal(a,b):return a['station']==b['station'] and a['direction']==b['direction'] and close(point(a['cross']),point(b['cross']),1e-6)

def verify_steps(expected_edges,steps,gap):
 """Verify route coverage, allowing coalescing but never shortcuts around bends."""
 steps=list(steps)
 if gap:
  if not steps or steps[0]['kind']!='baseline-reset' or not close(point(steps[0]['from']),gap[0]) or not close(point(steps[0]['to']),gap[1]):raise ValueError('Missing or incorrect baseline reset')
  if steps[0]['events']:raise ValueError('Baseline reset produced crossings')
  steps=steps[1:]
 i=0
 for step in steps:
  a=point(step['from']);b=point(step['to']);kind=step['kind']
  if i>=len(expected_edges) or not close(a,expected_edges[i][0]):raise ValueError('Runtime movement starts off the traversed path')
  if kind=='instant-edge':
   e=expected_edges[i]
   if not e[2] or not close(b,e[1]) or step['events']:raise ValueError('Incorrect instant-edge handling')
   i+=1;continue
  if kind!='native-walk':raise ValueError('Unknown or unexpected runtime step kind')
  direction=sub(b,a);length=norm2(direction)
  if length==0:raise ValueError('Runtime recorded zero-length movement')
  last_t=-1e-9
  while i<len(expected_edges):
   e=expected_edges[i]
   if e[2]:raise ValueError('Runtime merged an instant edge into continuous travel')
   for p in e[:2]:
    t=dot(sub(p,a),direction)/length;q=tuple(a[j]+t*direction[j] for j in range(3))
    if not close(p,q,1e-7) or t<last_t-1e-8 or t>1+1e-8:raise ValueError('Runtime span shortcuts a bend or reversal')
    last_t=t
   i+=1
   if close(e[1],b):break
  else:raise ValueError('Runtime span does not end on the traversed path')
 if i!=len(expected_edges):raise ValueError('Completed native movement was omitted from runtime observations')

def audit(path):
 src=None;errors=[];frames=0;native_steps=0;future=0;crossings=0;resets=0;seq=0;raw_bytes=0;header=None;end=None;gates={};actors={};seen=set();captured=set();omitted=set();counts={};geometry_revisions=0;revision=None;last_observation=-1
 tubes={};tube_records=0;tube_status="not-recorded";occupancy=[];excursions=[];open_excursions={};unmatched_exits=0;actor_types={}
 def fail(message):
  if len(errors)<100:errors.append(str(message))
 try:
  src=Source(path)
  manifest=load_json(src.data('manifest.json'));reference=load_json(src.data('station-reference.json'))
  if manifest.get('owner')!=OWNER or manifest.get('schema') not in (2,3):raise ValueError('Unsupported recording schema/owner')
  if manifest.get('status')!='sealed':fail('Recording is not cleanly sealed: '+str(manifest.get('status')))
  for name,key in [('trace.jsonl.gz','trace_sha256'),('station-reference.json','reference_sha256'),('audit_recording.py','auditor_sha256')]:
   if src.hash(name)!=manifest.get(key):fail('Recording checksum mismatch: '+name)
  if 'bundle.json' in src.names:
   bundle=load_json(src.data('bundle.json'))
   for name,want in bundle['files'].items():
    if name not in src.names or src.hash(name)!=want:fail('Bundle checksum mismatch: '+name)
  with src.open('trace.jsonl.gz') as compressed,gzip.GzipFile(fileobj=compressed) as trace:
   for line in trace:
    raw_bytes+=len(line)
    if raw_bytes>MAX_RAW or len(line)>3*1024*1024:raise ValueError('Recording exceeds audit safety limits')
    rec=load_json(line);seq+=1
    if rec.get('seq')!=seq:fail('Missing, duplicated or reordered record at '+str(seq))
    kind=rec['type'];d=rec['data']
    if end is not None:fail('Data exists after the end marker')
    if kind=='header':
     if header is not None or seq!=1:fail('Header is not the first unique record')
     header=d
     if d.get('schema')!=manifest['schema'] or d.get('session')!=manifest['session']:fail('Header session/schema mismatch')
     if d.get('mod_version')!=manifest.get('mod_version') or d['metadata'].get('mod_version')!=manifest.get('mod_version'):fail('Mixed observer versions in capture')
     if d.get('auditor_sha256')!=manifest.get('auditor_sha256'):fail('Frozen auditor changed during recording')
     if d.get('reference_sha256')!=manifest.get('reference_sha256'):fail('Authored reference changed during recording')
     if d.get('metadata')!=manifest.get('metadata'):fail('Mixed recording provenance')
     if d['metadata'].get('movement_assembly_sha256')!=reference['movement_assembly_sha256']:fail('Blueprint reference and loaded movement assembly differ')
    elif header is None:raise ValueError('Missing header')
    elif kind=='geometry':
     if geometry_revisions and d['revision']<=revision:fail('Geometry revisions are duplicated or out of order')
     geometry_revisions+=1;next_gates={}
     for g in d['gates']:
      try:next_gates[g['id']]=check_geometry(g,reference)
      except ValueError as e:fail(e)
      if g['id'] not in counts:counts[g['id']]={'id':g['id'],'mode':g['mode'],'first_day':d['day'],'entries':0,'exits':0,'bot_entries':0,'daily':{}}
     for actor in actors.values():
      sides={}
      for gid,g in next_gates.items():sides[gid]=actor['sides'][gid] if gid in gates and g==gates[gid] else side(g,actor['position'])
      actor['sides']=sides
     gates=next_gates;revision=d['revision']
    elif kind=='actor':
     if d['action']=='add':
      if d['id'] in actors:fail('Actor added twice without removal')
      seen.add(d['id']);p=point(d['position']);actor_types[d['id']]=d['bot']
      if d['captured']:captured.add(d['id'])
      else:omitted.add(d['id'])
      actors[d['id']]={'position':p,'bot':d['bot'],'captured':d['captured'],'sides':{gid:side(g,p) for gid,g in gates.items()}}
     elif d['action']=='remove':
      previous=actors.pop(d['id'],None)
      if previous is None or previous['captured']!=d['captured']:fail('Actor removal has no matching lifecycle baseline')
     else:fail('Unknown actor lifecycle action')
    elif kind=='tube-status':
     tube_status=d['status']
     if tube_status!='ready':fail('Tube occupancy observer unavailable: '+d['detail'])
    elif kind=='tube':
     tube_records+=1;tid=d['id'];action=d['action'];members={o['id']:o['bot'] for o in d['occupants']}
     if len(members)!=len(d['occupants']):fail('Duplicate tube passenger ID')
     if len(d['grid'])!=3 or any(type(v)!=int for v in d['grid']):fail('Invalid tube grid position')
     previous=tubes.get(tid)
     if action=='baseline':
      if previous is not None:fail('Duplicate tube baseline')
      before={}
      if d['added'] or d['removed']:fail('Tube baseline fabricated occupancy changes')
     elif action in ('change','remove'):
      if previous is None:fail('Tube change has no baseline')
      before=(previous or {}).get('members',{})
      if previous and previous['grid']!=d['grid']:fail('Tube coordinates changed without a new lifecycle')
      if set(d['added'])!=set(members)-set(before) or len(d['added'])!=len(set(d['added'])):fail('Tube added IDs disagree with snapshot')
      if set(d['removed'])!=set(before)-set(members) or len(d['removed'])!=len(set(d['removed'])):fail('Tube removed IDs disagree with snapshot')
      if action=='remove' and members:fail('Removed tube still has occupants')
      if action=='change' and not d['added'] and not d['removed']:fail('Tube change has no membership delta')
     else:raise ValueError('Unknown tube lifecycle action')
     for aid in set(before)&set(members):
      if before[aid]!=members[aid]:fail('Tube passenger type changed')
     for aid in (list(members) if action=='baseline' else d['added']+d['removed']):
      occupancy.append(dict(actor=aid,bot=members.get(aid,before.get(aid)),tube=tid,frame=d['render_frame'],seq=seq,kind='baseline' if action=='baseline' else ('enter' if aid in d['added'] else 'leave')))
     if action=='remove':tubes.pop(tid,None)
     else:tubes[tid]={'members':members,'grid':d['grid']}
    elif kind=='frame':
     frames+=1
     if not geometry_revisions:fail('Missing geometry baseline before movement')
     for step in d['steps']:
      if step['observation']<=last_observation:fail('Runtime observations are duplicated or out of order')
      last_observation=step['observation']
     if d.get('error'):fail('Observer error inside frame '+str(frames)+': '+d['error'])
     actor=actors.get(d['actor'])
     if actor is None or not actor['captured']:raise ValueError('Frame has no captured actor baseline')
     if d['revision']!=revision:fail('Frame references wrong geometry revision')
     pts=d['native'];actual=point(d['actual_end'])
     if not pts or any(len(row)!=6 for row in pts):raise ValueError('Native frame is empty or has incomplete point records')
     done=next((i for i in range(len(pts)-1,max(-1,len(pts)-3),-1) if point(pts[i][:3])==actual),None)
     if done is None:raise ValueError('Actual endpoint is not the final/penultimate native point')
     if d['claimed_completed_end']!=done:fail('Runtime included untraversed smoothing or omitted completed steps')
     future+=len(pts)-done-1;native_steps+=done
     start=point(pts[0][:3]);gap=None;expected=[];edges=[]
     if actor['position']!=start:
      gap=(actor['position'],start);actor['position']=start;actor['sides']={gid:side(g,start) for gid,g in gates.items()};resets+=1
     for raw in pts[1:done+1]:
      if len(raw)!=6:raise ValueError('Invalid native point record')
      p=point(raw[:3]);instant=bool(raw[5]);speed=float(raw[4]);max_half=float.fromhex('0x1.fffffep+127')*.5
      if instant!=(speed>=max_half):fail('Instant flag does not match native movement speed')
      a=actor['position']
      if a==p:continue
      edges.append((a,p,instant))
      if instant:actor['sides']={gid:side(g,p) for gid,g in gates.items()};resets+=1
      else:
       for gid,g in gates.items():
        e,s=crossing(g,a,p,actor['sides'][gid]);actor['sides'][gid]=s
        if e:
         expected.append(e);c=counts[gid];key='entries' if e['direction']=='entry' else 'exits';c[key]+=1
         if key=='entries' and actor['bot']:c['bot_entries']+=1
         daily=c['daily'].setdefault(d['day'],{'entries':0,'exits':0});daily[key]+=1
      actor['position']=p
     try:verify_steps(edges,d['steps'],gap)
     except ValueError as e:fail('Frame '+str(frames)+': '+str(e))
     actual_events=[e for s in d['steps'] for e in s['events']]
     if len(actual_events)!=len(expected) or any(not event_equal(a,b) for a,b in zip(actual_events,expected)):fail('Frame '+str(frames)+': native movement and counted crossings differ')
     for e in expected:
      if gates[e['station']]['mode']!='tubeway':continue
      aid=d['actor']
      if e['direction']=='entry':
       if aid in open_excursions:excursions.append(dict(open_excursions.pop(aid),status='entry-before-exit'))
       open_excursions[aid]=dict(actor=aid,origin=e['station'],start_frame=d['render_frame'],start_seq=seq,start_day=d['day'],start_progress=d['progress'])
      elif aid in open_excursions:
       excursions.append(dict(open_excursions.pop(aid),destination=e['station'],end_frame=d['render_frame'],end_seq=seq,end_day=d['day'],end_progress=d['progress'],status='complete'))
      else:unmatched_exits+=1
     crossings+=len(expected)
    elif kind=='end':end=d
    else:fail('Unknown record type '+str(kind))
  if end is None:fail('Missing clean end marker')
  else:
   if end.get('status')!='sealed':fail('The recording ended with an observer/capture error')
   for key,value in [('frames',frames),('seen_actors',len(seen)),('captured_actors',len(captured)),('omitted_actors',len(omitted))]:
    if end.get(key)!=value or manifest.get(key)!=value:fail('Capture accounting mismatch: '+key)
   if manifest['schema']>=3 and (end.get('tube_records')!=tube_records or manifest.get('tube_records')!=tube_records):fail('Tube record accounting mismatch')
   wanted={c['id']:c for c in end['totals']}
   if set(wanted)!=set(counts):fail('Final station coverage differs from geometry history')
   for gid,c in counts.items():
    recorded=wanted.get(gid,{})
    for key in ['entries','exits','bot_entries','first_day','mode']:
     if recorded.get(key)!=c[key]:fail('Final count mismatch at '+gid+' '+key)
    daily=[dict(day=day,**values) for day,values in sorted(c['daily'].items())]
    if recorded.get('daily')!=daily:fail('Daily histogram mismatch at '+gid)
    n=min(7,max(0,end['day']-c['first_day']-1));average=sum(c['daily'].get(day,{}).get('entries',0) for day in range(end['day']-n,end['day']))/n if n else None
    if recorded.get('average_days')!=n or (average is None)!=(recorded.get('average') is None) or (average is not None and abs(average-recorded['average'])>1e-9):fail('Daily average mismatch at '+gid)
   coverage='partial' if omitted else 'all'
   if end.get('coverage')!=coverage or manifest.get('coverage')!=coverage:fail('Incorrect actor coverage claim')
  if manifest.get('records')!=seq or manifest.get('raw_bytes')!=raw_bytes:fail('Recording length/sequence accounting mismatch')
  excursions.extend(dict(e,status='open-at-end') for e in open_excursions.values())
  for sample in occupancy:
   if sample['actor'] in actor_types and sample['bot']!=actor_types[sample['actor']]:fail('Occupancy and route passenger types disagree')
  by_actor=collections.defaultdict(list)
  for sample in occupancy:by_actor[sample['actor']].append(sample)
  for e in excursions:
   stop=e.get('end_frame',float('inf'))
   samples=by_actor[e['actor']]
   within=[o for o in samples if e['start_frame']<=o['frame']<=stop]
   nearby=[o for o in samples if e['start_frame']-1<=o['frame']<=stop+1 and o not in within]
   e['same_station']=e.get('destination')==e['origin']
   e['tube_ids']=sorted({o['tube'] for o in within})
   e['occupancy_samples']=len(within)
   e['nearby_occupancy_samples']=len(nearby)
   e['tube_evidence']='observed-in-render-window' if within else ('nearby-render-frame-only' if nearby else 'unconfirmed')
  occupancy_report=dict(status=tube_status,records=tube_records,passengers=len(by_actor),samples=len(occupancy),unmatched_station_exits=unmatched_exits,complete_excursions=sum(e['status']=='complete' for e in excursions),same_station_excursions=sum(e['status']=='complete' and e['same_station'] for e in excursions),excursions=excursions,scope='Passenger IDs from actual tube membership sets. Displayed position can lag logical movement or skip tubes at speed. Render-window evidence is corroboration, not proof of precise event order; a missing sample does not disprove tube use. Baselines are existing occupants, never fabricated boardings. Tube exits do not close station excursions.')
  result=dict(schema=3,tube_occupancy=occupancy_report,auditor_version='0.1.7',fixture=bool(header and header['metadata'].get('fixture')),provenance=header['metadata'] if header else {},session=manifest['session'],mod_version=manifest['mod_version'],sealed=manifest['status']=='sealed',coverage=manifest.get('coverage'),frames=frames,native_steps=native_steps,excluded_future_points=future,geometry_revisions=geometry_revisions,actors_seen=len(seen),actors_captured=len(captured),actors_omitted=len(omitted),crossings=crossings,baseline_resets=resets,errors=errors,audit_passed=not errors,transport_exercised=crossings>0,complete_colony_coverage=not omitted,station_totals=list(counts.values()),scope='Replays recorded native movement and independent authored station geometry. Totals cover the sealed recording window; entries do not prove completed rides.')
  return result
 except (ValueError,KeyError,TypeError,OSError,EOFError,zipfile.BadZipFile) as e:
  return dict(audit_passed=False,errors=errors+[str(e)],frames=frames,scope='Recording is incomplete, corrupt or incompatible; no accuracy claim is made.')
 finally:
  if src:src.close()

def pack(folder,destination):
 src=Source(folder)
 try:
  manifest=load_json(src.data('manifest.json'))
  if manifest.get('status')!='sealed':raise ValueError('Only sealed recordings can be exported; quit the game normally or wait for the bounded capture to close.')
  names=['manifest.json','trace.jsonl.gz','station-reference.json','audit_recording.py'];payload={name:src.data(name,MAX_TRACE) for name in names}
  if hashlib.sha256(payload['trace.jsonl.gz']).hexdigest()!=manifest.get('trace_sha256'):raise ValueError('Trace changed or is corrupt')
  payload['README.txt']=b'This recording is self-contained. Extract it and run: python3 audit_recording.py audit .\nOr run the auditor against this ZIP directly. Timberborn and .NET are not required.\n'
  metadata={'schema':1,'session':manifest['session'],'files':{name:hashlib.sha256(data).hexdigest() for name,data in payload.items()}}
  payload['bundle.json']=json.dumps(metadata,separators=(',',':')).encode()
  with zipfile.ZipFile(destination,'w',zipfile.ZIP_DEFLATED) as z:
   for name,data in payload.items():z.writestr(name,data)
 finally:src.close()

if __name__=='__main__':
 p=argparse.ArgumentParser(description=__doc__);subparsers=p.add_subparsers(dest='command',required=True)
 a=subparsers.add_parser('audit');a.add_argument('recording');a.add_argument('--output')
 b=subparsers.add_parser('pack');b.add_argument('recording');b.add_argument('destination')
 args=p.parse_args()
 if args.command=='pack':pack(args.recording,args.destination);print('Exported self-contained recording to '+args.destination)
 else:
  result=audit(args.recording);text=json.dumps(result,indent=2);print(text)
  if args.output:Path(args.output).write_text(text+'\n')
  raise SystemExit(0 if result['audit_passed'] else 1)
