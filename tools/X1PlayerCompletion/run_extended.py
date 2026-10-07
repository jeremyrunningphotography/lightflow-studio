from pathlib import Path
import subprocess,json,time
r=Path(__file__).resolve().parents[2];out=r/'docs/research/mac-compatibility/x1-player-completion/raw';records=[]
for name,args in [('semantic-ready',['semantic',str(r/'work/data/color.mp4')]),('tempo-content',['tempo']),('actual-starvation',['starvation',str(r/'work/data/audio-native.f32')]),('startup-metric-correction',['backend',str(r/'work/data/long-3840x2160.mp4'),str(r/'work/data/audio-native.f32'),'5','1','1','0','0','0'])]:
 cmd=[str(r/'work/completion/completion')]+args;cmd+=['--data-root',str(r/'work/data-root')];t=time.monotonic()
 with (out/f'{name}.jsonl').open('w') as a,(out/f'{name}.log').open('w') as b:p=subprocess.run(cmd,cwd=r,stdout=a,stderr=b,timeout=40)
 records.append({'case':name,'command':cmd,'returncode':p.returncode,'seconds':time.monotonic()-t});print(records[-1],flush=True)
 if p.returncode:break
(out.parent/'EXTENDED_PROCESS_RESULTS.json').write_text(json.dumps(records,indent=2)+'\n')
