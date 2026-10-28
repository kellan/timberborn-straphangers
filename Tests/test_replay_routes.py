import csv,importlib.util,tempfile
from pathlib import Path
root=Path(__file__).resolve().parent.parent
spec=importlib.util.spec_from_file_location('replay_routes',root/'Replay/audit_legacy_routes.py');module=importlib.util.module_from_spec(spec);spec.loader.exec_module(module)
with tempfile.TemporaryDirectory() as tmp:
 p=Path(tmp);session='fixture-session'
 (p/'observer-status.txt').write_text('Session: '+session+'\n')
 def write(name,rows):
  with (p/name).open('w',newline='') as f:
   w=csv.DictWriter(f,fieldnames=list(rows[0]));w.writeheader();w.writerows(rows)
 gate=dict(session_id=session,revision=1,station_id='gate',center_x=0,center_y=0,center_z=0,inward_x=1,inward_y=0,inward_z=0,half_width=.5,height_tolerance=.15)
 write('entrances.csv',[gate])
 rows=[]
 for obs,a,b,entries,exits in [(1,-.5,0,0,0),(2,0,.5,1,0),(3,.5,.5,0,0),(4,.5,-.5,0,1)]:
  rows.append(dict(session_id=session,observation_id=obs,revision=1,passenger_id='p',from_x=a,from_y=0,from_z=0,to_x=b,to_y=0,to_z=0,entries=entries,exits=exits,discontinuity=0))
 write('sample-routes.csv',rows)
 ledger=[dict(session_id=session,passenger_id='p',observation_id=2,station_id='gate',event='entry',cross_x=0,cross_y=0,cross_z=0),dict(session_id=session,passenger_id='p',observation_id=4,station_id='gate',event='exit',cross_x=0,cross_y=0,cross_z=0)]
 write('events.csv',ledger);r=module.audit(p);assert r['telemetry_replay_passed'] and r['replayed_crossings']==2,r
 print('PASS: route audit accepts a complete entry/exit trace with a plane contact')
 write('events.csv',ledger[1:]);r=module.audit(p);assert not r['telemetry_replay_passed'] and r['errors'],r
 print('PASS: route audit detects a missing crossing in the ledger')
 write('events.csv',ledger+[ledger[-1]]);r=module.audit(p);assert not r['telemetry_replay_passed'] and r['errors'],r
 print('PASS: route audit detects a duplicated crossing')
 # A completed native straight span can exceed two tiles. It must not be
 # dropped just because earlier endpoint-only captures used a two-tile guard.
 native=dict(rows[0]);native.update(observation_id=5,from_x=-3,to_x=3,entries=1,exits=0,observation_kind='native-walk')
 write('sample-routes.csv',[native]);long_event=dict(ledger[0]);long_event['observation_id']=5;write('events.csv',[long_event]);r=module.audit(p);assert r['telemetry_replay_passed'] and r['skipped_discontinuities']==0 and r['native_walk_segments_over_two_tiles']==1,r
 print('PASS: route audit accepts a long completed native straight span')
 native.update(observation_kind='instant-edge',entries=0,discontinuity=1);write('sample-routes.csv',[native]);write('events.csv',ledger);r=module.audit(p);assert not r['errors'] and r['skipped_discontinuities']==1 and r['replayed_crossings']==0,r
 print('PASS: an explicit instant edge resets state without a phantom crossing')
 write('sample-routes.csv',rows)
 write('events.csv',ledger);gate['revision']=2;write('entrances.csv',[gate]);r=module.audit(p);assert not r['telemetry_replay_passed'] and r['segments_missing_geometry']==4,r
 print('PASS: route audit rejects missing geometry rather than guessing')
