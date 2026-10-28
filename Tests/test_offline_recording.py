import copy,gzip,hashlib,importlib.util,json,os,shutil,subprocess,sys,tempfile
from pathlib import Path
root=Path(__file__).resolve().parent.parent
spec=importlib.util.spec_from_file_location('offline_auditor',root/'Replay/audit_recording.py');auditmod=importlib.util.module_from_spec(spec);spec.loader.exec_module(auditmod)
artifacts=Path(os.environ.get('STRAPHANGERS_TEST_OUTPUT',str(root/'artifacts/tests')))
fixture=artifacts/'offline-fixture'
result=auditmod.audit(fixture)
assert result['audit_passed'] and result['frames']==6 and result['crossings']==10 and result['actors_captured']==3 and result['complete_colony_coverage'],result
print('PASS: independent auditor replays all native steps, both stations, bots, idle actors and geometry revisions')
tube=result['tube_occupancy']
assert tube['status']=='ready' and tube['records']==11 and tube['passengers']==3 and tube['same_station_excursions']==1,tube
same=next(e for e in tube['excursions'] if e['same_station'])
assert same['status']=='complete' and same['tube_ids']==['tube-one','tube-two'] and same['occupancy_samples']==6,same
assert any(e['tube_evidence']=='unconfirmed' for e in tube['excursions']),tube
print('PASS: same-station excursion survives tube transfers and a construction gap; IDs, bots and initial occupants are preserved; missing occupancy stays unconfirmed')
report=[]
with tempfile.TemporaryDirectory() as tmp:
 tmp=Path(tmp);original=tmp/'original';shutil.copytree(fixture,original);archive=tmp/'standalone.zip'
 auditmod.pack(original,archive);shutil.rmtree(original)
 runner=tmp/'audit_recording.py'
 import zipfile
 with zipfile.ZipFile(archive) as z:runner.write_bytes(z.read('audit_recording.py'))
 proc=subprocess.run([sys.executable,str(runner),'audit',str(archive)],cwd=tmp,capture_output=True,text=True,env={k:v for k,v in os.environ.items() if k not in ('PYTHONPATH','DOTNET_ROOT','DOTNET_CLI_HOME','NUGET_PACKAGES')})
 r=json.loads(proc.stdout);assert proc.returncode==0 and r['audit_passed'] and r['crossings']==10,(proc.stdout,proc.stderr)
 print('PASS: frozen auditor works from the exported archive after deleting original records, without game or SDK access')
 auditmod.pack(fixture,artifacts/'OFFLINE-DEMO.zip')
 def altered(name,mutator):
  dest=tmp/name;shutil.copytree(fixture,dest)
  records=[json.loads(line) for line in gzip.decompress((dest/'trace.jsonl.gz').read_bytes()).splitlines()]
  mutator(records)
  raw=('\n'.join(json.dumps(row,separators=(',',':')) for row in records)+'\n').encode()
  data=gzip.compress(raw);(dest/'trace.jsonl.gz').write_bytes(data)
  manifest=json.loads((dest/'manifest.json').read_text());manifest['trace_sha256']=hashlib.sha256(data).hexdigest();manifest['raw_bytes']=len(raw);manifest['compressed_bytes']=len(data);manifest['records']=len(records);(dest/'manifest.json').write_text(json.dumps(manifest,separators=(',',':')))
  return auditmod.audit(dest)
 def missing_event(records):
  frame=next(row['data'] for row in records if row['type']=='frame')
  next(step for step in frame['steps'] if step['events'])['events'].pop()
 r=altered('missing-event',missing_event);assert not r['audit_passed'] and any('counted crossings differ' in e for e in r['errors']),r
 print('PASS: native movement detects a missing count even with recomputed file checksums')
 def bad_geometry(records):
  record=next(row['data'] for row in records if row['type']=='geometry' and row['data']['gates']);record['gates'][0]['center'][2]+=1
 r=altered('bad-geometry',bad_geometry);assert not r['audit_passed'] and any('Blueprint mismatch' in e for e in r['errors']),r
 print('PASS: independent blueprint reference detects a misplaced doorway')
 def future_point(records):
  frame=next(row['data'] for row in records if row['type']=='frame');frame['claimed_completed_end']+=1
 r=altered('future-point',future_point);assert not r['audit_passed'] and any('smoothing' in e for e in r['errors']),r
 print('PASS: auditor detects counting an untraversed future point')
 def remove_frame(records):
  records.pop(next(i for i,row in enumerate(records) if row['type']=='frame'))
 r=altered('missing-frame',remove_frame);assert not r['audit_passed'] and any('reordered record' in e for e in r['errors']),r
 print('PASS: missing movement records cannot silently pass')
 def bad_delta(records):
  row=next(r['data'] for r in records if r['type']=='tube' and r['data']['action']=='change');row['added']=[]
 r=altered('bad-tube-delta',bad_delta);assert not r['audit_passed'] and any('added IDs disagree' in e for e in r['errors']),r
 print('PASS: missing occupancy passenger ID is detected against the membership snapshot')
 def duplicate_baseline(records):
  row=next(r['data'] for r in records if r['type']=='tube');row['occupants'].append(copy.deepcopy(row['occupants'][0]))
 r=altered('duplicate-occupant',duplicate_baseline);assert not r['audit_passed'] and any('Duplicate tube passenger' in e for e in r['errors']),r
 print('PASS: duplicate occupancy passenger IDs are rejected')
 def failed_observer(records):
  row=next(r['data'] for r in records if r['type']=='tube-status');row['status']='failed'
 r=altered('tube-observer-failed',failed_observer);assert not r['audit_passed'] and any('occupancy observer unavailable' in e for e in r['errors']),r
 print('PASS: an unavailable occupancy observer cannot silently produce a successful audit')
 def delayed_tubes(records):
  for row in records:
   if row['type']=='tube' and row['data']['action']=='change':row['data']['render_frame']=107
 r=altered('render-lag',delayed_tubes);assert r['audit_passed'],r
 e=next(e for e in r['tube_occupancy']['excursions'] if e['same_station']);assert e['tube_evidence']=='nearby-render-frame-only' and e['nearby_occupancy_samples']>0,e
 print('PASS: delayed displayed movement is labeled nearby evidence without claiming precise ordering')
 broken=tmp/'truncated' ;shutil.copytree(fixture,broken);p=broken/'trace.jsonl.gz';p.write_bytes(p.read_bytes()[:-12]);r=auditmod.audit(broken);assert not r['audit_passed'],r
 print('PASS: truncated or corrupt capture is rejected')
 open_capture=tmp/'open';shutil.copytree(fixture,open_capture);m=json.loads((open_capture/'manifest.json').read_text());m['status']='recording';(open_capture/'manifest.json').write_text(json.dumps(m));r=auditmod.audit(open_capture);assert not r['audit_passed'],r
 try:auditmod.pack(open_capture,tmp/'should-not-export.zip');raise AssertionError('Exported active capture')
 except ValueError:pass
 print('PASS: an unsealed live capture cannot be mistaken for a complete export')
 r=auditmod.audit(artifacts/'capacity-fixture');assert r['audit_passed'] and r['frames']==0 and not r['transport_exercised'],r
 print('PASS: bounded capture closes as a valid prefix and declares that no transport was exercised')
 (artifacts/'OFFLINE-RECORDING-RESULT.json').write_text(json.dumps(result,indent=2)+'\n')
