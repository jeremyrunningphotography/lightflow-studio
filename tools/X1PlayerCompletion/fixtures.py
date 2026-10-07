from pathlib import Path
import json,subprocess,hashlib
r=Path(__file__).resolve().parents[2];p=r/'docs/research/mac-compatibility/x1-player';data=r/'work/data';data.mkdir(parents=True,exist_ok=True);out=r/'docs/research/mac-compatibility/x1-player-completion';commands=[]
base=str(r)
items=json.loads((p/'fixture-manifest.json').read_text())
commands.append(next(x['command'] for x in items if x['name']=='color'))
commands.append(json.loads((p/'audio-sync-fixture.json').read_text())['generation_command'])
for name in ['perf-1920x1080','perf-3840x2160']:commands.append(next(x['command'] for x in json.loads((p/'performance.json').read_text()) if x['fixture']==name))
for cmd in commands:
 cmd=[x.replace('/Users/jeremyrunning/Git/agents/Agent-X1-Mac-Player',base) for x in cmd];subprocess.run(cmd,cwd=r,check=True)
for size in ['1920x1080','3840x2160']:
 cmd=['ffmpeg','-v','error','-y','-stream_loop','-1','-i',str(data/f'perf-{size}.mp4'),'-t','140','-c','copy',str(data/f'long-{size}.mp4')];subprocess.run(cmd,check=True);commands.append(cmd)
prefix=r/'work/deps/lgpl-ffmpeg';binary=r/'work/completion/audio_decode'
subprocess.run(['clang','-O2',str(r/'tools/X1MacPlayerProof/audio_decode_probe.c'),'-I'+str(prefix/'include'),'-L'+str(prefix/'lib'),'-lavformat','-lavcodec','-lavutil','-lswresample','-o',str(binary)],check=True)
subprocess.run([str(binary),str(data/'audio-sync.mp4'),str(data/'audio-native.f32')],check=True)
manifest=[]
for f in sorted(data.glob('*')):
 item={'file':str(f.relative_to(r)),'bytes':f.stat().st_size,'sha256':hashlib.sha256(f.read_bytes()).hexdigest()}
 if f.suffix=='.mp4':item['probe']=json.loads(subprocess.check_output(['ffprobe','-v','error','-show_streams','-show_format','-of','json',str(f)]))
 manifest.append(item)
(out/'FIXTURES.json').write_text(json.dumps({'generator':subprocess.check_output(['ffmpeg','-version'],text=True).splitlines()[0],'commands':commands,'inputs':manifest},indent=2)+'\n')
