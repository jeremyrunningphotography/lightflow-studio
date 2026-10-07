import pathlib,subprocess,json,time,os
r=pathlib.Path(__file__).resolve().parents[3];p=r/'docs/research/mac-compatibility/x1-player-audio-completion/native-output-rca';data=r/'work/audio/data-root';data.mkdir(parents=True,exist_ok=True)
args=[str(r/'work/audio/audio-proof'),'stress',str(r/'work/data/audio-sync.mp4'),'--data-root',str(data)]
t=time.monotonic()
with (p/'x1-start-stress.jsonl').open('w') as a,(p/'x1-start-stress.stderr.txt').open('w') as b:
 try:rc=subprocess.run(args,stdout=a,stderr=b,env={**os.environ,'X1_STRESS_COUNT':'6'},timeout=90).returncode
 except subprocess.TimeoutExpired:rc='timeout'
result={'returncode':rc,'seconds':time.monotonic()-t,'count':6,'command':args};(p/'x1-start-process.json').write_text(json.dumps(result,indent=2));print(result)
rows=[json.loads(x) for x in (p/'x1-start-stress.jsonl').read_text().splitlines()];print(json.dumps([x for x in rows if x['kind'] in ['stress-summary','stress-attempt','epoch-start','queue-count']],indent=2))
