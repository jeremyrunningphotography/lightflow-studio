"""Run existing bounded matrix into fresh evidence; never overwrite failed runs."""
import pathlib,subprocess,json,time,os
r=pathlib.Path(__file__).resolve().parents[3];p=r/'docs/research/mac-compatibility/x1-player-audio-completion/native-output-rca';raw=p/'qualification';raw.mkdir(exist_ok=True);data=r/'work/audio/data-root';data.mkdir(parents=True,exist_ok=True);audio=r/'work/data/audio-sync.mp4'
cases=[('underrun','starve',[]),('av-half','av',[str(audio),'.5','20']),('av-one','av',[str(audio),'1','20']),('av-double','av',[str(audio),'2','15']),('live-rates','rates',[str(audio),'1','15']),('loops','loops',[str(audio),'1','9']),('av-recovery','recover',[str(audio),'1','5']),('start-restart-stress','stress',[])]
results=[]
for name,mode,extra in cases:
 args=[str(r/'work/audio/audio-proof'),mode,str(audio)]+extra+['--data-root',str(data)];t=time.monotonic()
 with (raw/(name+'.jsonl')).open('w') as a,(raw/(name+'.stderr.txt')).open('w') as b:
  try:rc=subprocess.run(args,stdout=a,stderr=b,timeout=90,env={**os.environ,'X1_STRESS_COUNT':'12'}).returncode
  except subprocess.TimeoutExpired:rc='timeout'
 results.append({'name':name,'returncode':rc,'seconds':time.monotonic()-t,'command':args});(p/'qualification-processes.json').write_text(json.dumps(results,indent=2)+'\n');print(json.dumps(results[-1]),flush=True)
 if rc:break
