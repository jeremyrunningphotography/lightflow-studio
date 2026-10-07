import json,pathlib,subprocess,time
root=pathlib.Path(__file__).resolve().parents[3];out=root/'docs/research/mac-compatibility/x1-player-audio-completion/native-output-rca';results=[]
# Serial, one changed dimension per step. Each process creates/disposes once.
cases=[('minimal-stress-'+str(i),['2','4800','0','silent']) for i in range(5)]+[('mono',['1','4800','0','silent']),('mono-1024',['1','1024','0','silent']),('mono-1024-service',['1','1024','1','silent'])]
for name,args in cases:
 t=time.monotonic();r=subprocess.run([str(root/'work/native-output-rca/minimal'),*args],capture_output=True,text=True,timeout=30)
 (out/(name+'.jsonl')).write_text(r.stdout);(out/(name+'.stderr.txt')).write_text(r.stderr)
 rows=[json.loads(x) for x in r.stdout.splitlines()];a={'case':name,'args':args,'returncode':r.returncode,'elapsed':time.monotonic()-t,'start':next(x for x in rows if x.get('api')=='AudioQueueStart'),'final':rows[-1]};results.append(a);print(json.dumps(a),flush=True)
 if r.returncode:break
(out/'matrix.json').write_text(json.dumps(results,indent=2)+'\n')
