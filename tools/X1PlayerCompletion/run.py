from pathlib import Path
import subprocess,json,time
r=Path(__file__).resolve().parents[2];out=r/'docs/research/mac-compatibility/x1-player-completion/raw';out.mkdir(parents=True,exist_ok=True);cases=[]
# Serial native execution, queue gain zero, no system output/sleep changes.
for name,mode,media,seconds,rate,color,transitions,faults,loops in [
 ('4k-native-baseline','native','long-3840x2160',12,1,1,0,0,0),
 ('4k-backend-color','backend','long-3840x2160',60,1,1,0,0,0),
 ('4k-backend-off','backend','long-3840x2160',60,1,0,0,0,0),
 ('1080-backend-color','backend','long-1920x1080',15,1,1,0,0,0),
 ('av-half','backend','audio-sync',20,.5,1,0,0,0),
 ('av-one','backend','audio-sync',20,1,1,0,0,0),
 ('av-double','backend','audio-sync',15,2,1,0,0,0),
 ('speed-live','backend','audio-sync',22,1,1,1,0,0),
 ('loop','backend','audio-sync',8,1,1,0,0,1),
 ('recovery','backend','audio-sync',8,1,1,0,1,0),
]:
 args=[str(r/'work/completion/completion'),mode,str(r/f'work/data/{media}.mp4'),str(r/'work/data/audio-native.f32'),str(seconds),str(rate),str(color),str(transitions),str(faults),str(loops)]
 args+=['--data-root',str(r/'work/data-root')];t=time.monotonic()
 with (out/f'{name}.jsonl').open('w') as a,(out/f'{name}.log').open('w') as b:p=subprocess.run(['/usr/bin/time','-l']+args,stdout=a,stderr=b,cwd=r,timeout=seconds+60)
 cases.append({'name':name,'command':args,'returncode':p.returncode,'seconds':time.monotonic()-t});print(cases[-1],flush=True);(out.parent/'PROCESS_RESULTS.json').write_text(json.dumps(cases,indent=2)+'\n')
 if p.returncode:break
