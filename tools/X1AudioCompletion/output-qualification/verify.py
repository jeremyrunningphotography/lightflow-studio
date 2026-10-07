import pathlib,json,hashlib,subprocess,sys
r=pathlib.Path(__file__).resolve().parents[3];p=r/'docs/research/mac-compatibility/x1-player-audio-completion/output-qualification'
for x in json.loads((p/'MANIFEST.json').read_text()):
 b=(p/x['path']).read_bytes();assert len(b)==x['bytes'] and hashlib.sha256(b).hexdigest()==x['sha256'],x['path']
subprocess.run([sys.executable,str(pathlib.Path(__file__).with_name('analyze.py'))],check=True)
locked=json.loads((p/'recurrence-afplay.json').read_text());unlocked=json.loads((p/'unlock-afplay.json').read_text());assert locked['returncode']==1 and '-66681' in locked['stderr'] and unlocked['returncode']==0
assert locked['session_before']['screenLockedReported'] is True and unlocked['session_before']['screenLockedReported'] is not True
for x in json.loads((p/'loop-range-results.json').read_text()):
 rows=[json.loads(s) for s in (p/(x['name']+'.jsonl')).read_text().splitlines()];loops=[y for y in rows if y['kind']=='loop-rebase'];assert len(loops)>=3
 for y in rows:
  if y['kind']=='frame':assert 0<=y['source_pts']<1
  if y['kind']=='clock':assert 0<=y['protected_samples']<=y['supplied']+1e-7
  if y['kind']=='drain-complete':assert y['drained_source_end']==1 and y['decoded_samples']==48000 and y['running']==0 and y['running_status']==0
 for y in loops:assert y['drained_source_end']==1 and y['last_video_pts']<1 and y['first_video_pts']==0 and y['first_audio_source']==0
assert not json.loads((p/'CLEANUP.json').read_text())['task_audio_processes']
print('PASS paired session/output observations, native causal clock and drained/exclusive loop identity; technical G1 PASS recommendation remains owner-gated')
