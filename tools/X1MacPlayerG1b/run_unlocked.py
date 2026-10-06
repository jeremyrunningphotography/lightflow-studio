"""Repeat bounded visible checks after the owner's explicit unlock confirmation."""
import json,pathlib,subprocess,time
r=pathlib.Path(__file__).resolve().parents[2];o=r/'docs/research/mac-compatibility/x1-player-g1b';cases=[]
for name,binary,args in [
 ('hardware-color-unlocked','color_hardware',['work/data/rotation.mp4']),
 ('shown-lifecycle-unlocked','supplemental',['shown-lifecycle','work/data/color.mp4']),
 ('1080-hw-unlocked','integrated',['play','work/data/perf-1920x1080.mp4','work/data/audio-native.f32','20','1','1','1','0']),
 ('4k-hw-unlocked','integrated',['play','work/data/perf-3840x2160.mp4','work/data/audio-native.f32','30','1','1','1','0']),
]:
 start=time.monotonic()
 with (o/(name+'.jsonl')).open('w') as a,(o/(name+'.log')).open('w') as b:
  p=subprocess.run(['/usr/bin/time','-l',str(r/'work/g1b'/binary)]+args,cwd=r,stdout=a,stderr=b,timeout=90)
 item={'name':name,'binary':binary,'args':args,'returncode':p.returncode,'seconds':time.monotonic()-start,'execution_context':'owner unlock confirmed before this serial matrix; no continuous visibility/lock telemetry'}
 cases.append(item);print(item,flush=True);(o/'unlocked-process-results.json').write_text(json.dumps(cases,indent=2))
 if p.returncode:raise SystemExit(name)
