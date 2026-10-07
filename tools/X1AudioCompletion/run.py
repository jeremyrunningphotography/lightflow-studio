"""Serial native matrix; preserves failures and stops when output cannot start."""
from pathlib import Path
import subprocess,json,time
r=Path(__file__).resolve().parents[2]
p=r/'docs/research/mac-compatibility/x1-player-audio-completion'
out=p/'raw';out.mkdir(parents=True,exist_ok=True)
exe=r/'work/audio/audio-proof';audio=r/'work/data/audio-sync.mp4'
root=r/'work/audio/data-root';root.mkdir(parents=True,exist_ok=True)
cases=[('underrun','starve',[]),('av-half','av',[str(audio),'.5','20']),('av-one','av',[str(audio),'1','20']),('av-double','av',[str(audio),'2','15']),('live-rates','rates',[str(audio),'1','15']),('loops','loops',[str(audio),'1','9']),('av-recovery','recover',[str(audio),'1','5']),('start-restart-stress','stress',[])]
results=[]
for name,mode,extra in cases:
 args=[str(exe),mode,str(audio)]+extra+['--data-root',str(root)];t=time.monotonic()
 with (out/f'{name}.jsonl').open('w') as stdout,(out/f'{name}.log').open('w') as stderr:
  try: rc=subprocess.run(args,stdout=stdout,stderr=stderr,timeout=90,cwd=r).returncode
  except subprocess.TimeoutExpired:rc='timeout'
 results.append({'name':name,'command':args,'returncode':rc,'seconds':time.monotonic()-t})
 (p/'PROCESS_RESULTS.json').write_text(json.dumps(results,indent=2)+'\n')
 print(results[-1],flush=True)
 if rc:break
