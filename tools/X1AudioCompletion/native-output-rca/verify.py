import pathlib,json,hashlib,subprocess,sys
r=pathlib.Path(__file__).resolve().parents[3];p=r/'docs/research/mac-compatibility/x1-player-audio-completion/native-output-rca'
for row in json.loads((p/'MANIFEST.json').read_text()):
 b=(p/row['path']).read_bytes();assert len(b)==row['bytes'] and hashlib.sha256(b).hexdigest()==row['sha256'],row['path']
subprocess.run([sys.executable,str(pathlib.Path(__file__).with_name('analyze.py'))],check=True)
assert not json.loads((p/'CLEANUP.json').read_text())['task_audio_processes']
print('PASS native RCA archive integrity, measured causal bounds and cleanup; G1 remains REVISE')
