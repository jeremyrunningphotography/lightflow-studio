"""Offline assertions on measured native rows, separate from simulated oracle."""
import json,pathlib,statistics,hashlib,gzip
r=pathlib.Path(__file__).resolve().parents[3];p=r/'docs/research/mac-compatibility/x1-player-audio-completion/output-qualification';result={};assertions=0
for f in sorted((p/'qualification').glob('*.jsonl*')):
 text=gzip.decompress(f.read_bytes()).decode() if f.suffix=='.gz' else f.read_text(); name=f.name.split('.jsonl')[0]
 rows=[json.loads(x) for x in text.splitlines()];clocks=[x for x in rows if x['kind']=='clock'];previous={}
 for x in clocks:
  assert 0<=x['protected_samples']<=x['supplied']+1e-7
  assert abs(x['source']-(x['origin']+x['protected_samples']/48000*x['rate']))<1e-8
  assert x['protected_samples']>=previous.get(x['generation'],0);previous[x['generation']]=x['protected_samples'];assertions+=3
 frames=[x for x in rows if x['kind']=='frame'];offsets=[x['wall_offset_ms'] for x in frames];ordered=sorted(abs(x) for x in offsets)
 stats={'clock_rows':len(clocks),'frames':len(frames),'offset_wall_ms':{'min':min(offsets),'max':max(offsets),'median':statistics.median(offsets),'p95_abs':ordered[int(.95*(len(ordered)-1))]} if offsets else None,'events':[x for x in rows if x['kind'] in ['clock-invalidated','underrun-recovery','pause-invariant','rate-transition','loop-rebase','av-recovery','av-summary','stress-summary','queue-count','drain-request','drain-complete']]}
 for x in rows:
  if x['kind']=='epoch-start':assert x['status']==0;assertions+=1
  if x['kind']=='stale-injection':assert x['rejected'] and x['clock_unchanged'];assertions+=1
  if x['kind']=='av-summary':assert x['active_queues']==0 and x['known_invalid_schedules']==0;assertions+=1
  if x['kind']=='stress-summary':assert x['active_queues']==0 and x['initial_failures']==0;assertions+=1
 if name=='underrun':
  depleted=next(x for x in clocks if x.get('tag')=='starvation-end');assert not depleted['valid'] and depleted['raw_samples']>depleted['supplied'] and depleted['protected_samples']<=depleted['supplied'];assertions+=1
  stats['depleted']=depleted
 if name=='live-rates':
  transitions=[x for x in rows if x['kind']=='rate-transition'];assert [(x['from'],x['to']) for x in transitions]==[(1,2),(2,1),(1,.5),(.5,1)];assertions+=1
 if name=='loops':
  loops=[x for x in rows if x['kind']=='loop-rebase'];assert len(loops)>=3
  for x in loops:
   assert abs(x['last_audio_source']-1)<1e-8
   assert x['first_audio_source']==0 and x['first_video_pts']==0
   drain=next(y for y in rows if y['kind']=='drain-complete' and y['generation']==x['generation']-1)
   assert drain['running']==0 and drain['running_status']==0 and drain['protected_samples']==drain['supplied'];assertions+=3
  assertions+=1
 stats['epochs']=[]
 for event in [x for x in rows if x['kind'] in ['rate-transition','loop-rebase','av-recovery']]:
  gen=event['generation'];fs=[x for x in frames if x['generation']==gen];cs=[x for x in clocks if x['generation']==gen]
  settled=[x['wall_offset_ms'] for x in fs if x['wall']>event.get('wall',0)+.2]
  stats['epochs'].append({'generation':gen,'first_frame_source_pts':fs[0]['source_pts'] if fs else None,'first_frame_audio_source':fs[0]['audio_source'] if fs else None,'first_frame_gap_ms':(fs[0]['wall']-event['last_render_wall'])*1000 if fs and 'last_render_wall' in event else None,'clock_origin':cs[0]['origin'] if cs else None,'settled_max_abs_wall_offset_ms':max(map(abs,settled)) if settled else None})
 result[name]=stats
result['assertions']=assertions;(p/'native-analysis.json').write_text(json.dumps(result,indent=2)+'\n');print(json.dumps({k:{'frames':v.get('frames'),'offset':v.get('offset_wall_ms')} for k,v in result.items() if isinstance(v,dict)},indent=2));print('assertions',assertions)
