import pathlib,subprocess,json,time
r=pathlib.Path(__file__).resolve().parents[3];p=r/'docs/research/mac-compatibility/x1-player-audio-completion/output-qualification';results=[]
for name,rate,seconds in [('loop-range-half','.5','7'),('loop-range-one','1','4'),('loop-range-double','2','4')]:
 output=p/(name+'.jsonl');assert not output.exists(),'preserve previous run'
 args=[str(r/'work/audio/audio-proof'),'loops',str(r/'work/data/audio-sync.mp4'),str(r/'work/data/audio-sync.mp4'),rate,seconds,'--data-root',str(r/'work/audio/data-root')];t=time.monotonic()
 with output.open('w') as a,(p/(name+'.stderr.txt')).open('w') as b:rc=subprocess.run(args,stdout=a,stderr=b,timeout=25).returncode
 rows=[json.loads(x) for x in output.read_text().splitlines()];loops=[x for x in rows if x['kind']=='loop-rebase'];frames=[x for x in rows if x['kind']=='frame'];assert not rc and len(loops)>=3
 for x in frames:assert 0<=x['source_pts']<1
 for x in loops:
  assert x['drained_source_end']==1 and x['exclusive_source_end']==1
  assert x['first_audio_source']==0 and x['first_video_pts']==0 and x['last_video_pts']<1
  prior=next(y for y in rows if y['kind']=='drain-complete' and y['generation']==x['generation']-1)
  assert prior['running']==0 and prior['running_status']==0 and prior['decoded_samples']==48000 and prior['protected_samples']<=prior['supplied']
 result={'name':name,'rate':float(rate),'returncode':rc,'seconds':time.monotonic()-t,'frame_count':len(frames),'last_video_max':max(x['last_video_pts'] for x in loops),'source_clock_endpoint':loops[0]['last_audio_source'],'drained_source_end':1,'loops':loops,'drains':[x for x in rows if x['kind']=='drain-complete']};results.append(result);print(name,len(loops),'PASS',flush=True)
(p/'loop-range-results.json').write_text(json.dumps(results,indent=2)+'\n')
