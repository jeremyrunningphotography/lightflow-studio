"""Recreate G1 inputs without writing to the frozen G1 evidence directory."""
import hashlib,json,pathlib,subprocess
r=pathlib.Path(__file__).resolve().parents[2];old=r/'docs/research/mac-compatibility/x1-player';data=r/'work/data';data.mkdir(parents=True,exist_ok=True)
provenance=json.loads((r/'docs/research/mac-compatibility/x1-player-g1b/DEPENDENCY_PROVENANCE.json').read_text())
commands=[]
manifest=json.loads((old/'fixture-manifest.json').read_text())
for name in ['color','nonzero','rotation']:commands.append(next(x['command'] for x in manifest if x['name']==name))
commands.append(json.loads((old/'audio-sync-fixture.json').read_text())['generation_command'])
for name in ['perf-1920x1080','perf-3840x2160']:commands.append(next(x['command'] for x in json.loads((old/'performance.json').read_text()) if x['fixture']==name))
for cmd in commands:
 cmd=[x.replace('/Users/jeremyrunning/Git/agents/Agent-X1-Mac-Player',str(r)) for x in cmd]
 target=r/'work/data'/pathlib.Path(cmd[-1]).name;cmd[-1]=str(target)
 if not target.exists():subprocess.run(cmd,cwd=r,check=True)
pcm=data/'audio-native.f32'
if not pcm.exists():
 p=r/'work/deps/lgpl-ffmpeg';binary=r/'work/g1b/audio_decode'
 subprocess.run(['clang','-O2',str(r/'tools/X1MacPlayerProof/audio_decode_probe.c'),'-I'+str(p/'include'),'-L'+str(p/'lib'),'-lavformat','-lavcodec','-lavutil','-lswresample','-o',str(binary)],check=True)
 subprocess.run([str(binary),str(data/'audio-sync.mp4'),str(pcm)],check=True)
for entry in provenance['inputs']:
 f=r/entry['file'];assert hashlib.sha256(f.read_bytes()).hexdigest()==entry['sha256'],f'Input differs: {f}; generator version matters'
print('Exact retained/regenerated G1 inputs verified; frozen evidence unchanged')
