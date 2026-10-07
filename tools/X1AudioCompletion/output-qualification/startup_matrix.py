import pathlib,subprocess,json,time
r=pathlib.Path(__file__).resolve().parents[3];p=r/'docs/research/mac-compatibility/x1-player-audio-completion/output-qualification';results=[];pids=[]
# Each hypothesis returns to the same baseline. No global device/rate change.
for i,mode in enumerate([0,1,0,2,0,3,0,1,0,2,0,3]):
 t=time.monotonic();out=subprocess.run([str(r/'work/native-output-rca/startup'),str(mode)],capture_output=True,text=True,timeout=25);rows=[json.loads(x) for x in out.stdout.splitlines()];start=next(x for x in rows if x.get('api')=='start');pids.append(start['taskPID']);start.pop('taskPID')
 for x in rows:x.pop('taskPID',None)
 (p/f'startup-{i:02}.jsonl').write_text(''.join(json.dumps(x)+'\n' for x in rows));(p/f'startup-{i:02}.stderr.txt').write_text(out.stderr)
 results.append({'case':i,'mode':mode,'returncode':out.returncode,'elapsed':time.monotonic()-t,'start':start,'callbacks':max((x.get('callbacks',0) for x in rows)),'nonzero':[x for x in rows if x.get('status',0)!=0]});print(json.dumps(results[-1]),flush=True)
 if out.returncode:break
(p/'startup-matrix.json').write_text(json.dumps(results,indent=2)+'\n');(r/'work/native-output-rca/pids.json').write_text(json.dumps(pids))
