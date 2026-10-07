from pathlib import Path
import json,statistics,gzip,hashlib
r=Path(__file__).resolve().parents[2];p=r/'docs/research/mac-compatibility/x1-player-completion';raw=p/'raw'
def stats(xs):
 xs=sorted(xs)
 return {'n':len(xs),'mean':statistics.mean(xs),'p50':xs[len(xs)//2],'p95':xs[min(len(xs)-1,int(.95*len(xs)))],'min':xs[0],'max':xs[-1]} if xs else None
runs={};transitions={};loops={};recovery={}
for f in sorted(list(raw.glob('*.jsonl'))+list(raw.glob('*.jsonl.gz'))):
 rows=[json.loads(x) for x in (gzip.decompress(f.read_bytes()).decode() if f.name.endswith('.gz') else f.read_text()).splitlines()]
 name=f.name[:-9] if f.name.endswith('.jsonl.gz') else f.stem;frames=[x for x in rows if x.get('kind')=='frame'];summary=next((x for x in reversed(rows) if x.get('kind')=='summary'),None)
 if frames:
  metrics={k:stats([x[k] for x in frames if k!='decode_ms' or 0<=x[k]<1000]) for k in ['decode_ms','selection_decode_ms','import_ms','producer_wait_ms','drawable_ms','consumer_wait_ms','gpu_ms','ack_ms','render_ms','source_offset_before_ms','source_offset_after_ms','wall_offset_after_ms','rss']}
  item={'summary':summary,'metrics':metrics,'frames':len(frames),'scheduled_skips':sum(x['skipped'] for x in frames),'render_ready':sum(x['render_ready'] for x in frames),'native_drawable_callbacks':sum(x['presented_ack'] for x in frames),'render_ready_fps':len(frames)/summary['seconds'] if summary else None,'cpu_percent_one_core':summary['cpu_seconds']/summary['seconds']*100 if summary else None,'queue_depth_max':max(x['queue_depth'] for x in frames),'decode_startup_metric_excluded':sum(x['decode_ms']>=1000 for x in frames),'first_5s_source_offset_mean':statistics.mean(x['source_offset_after_ms'] for x in frames if x['wall']<5),'last_5s_source_offset_mean':statistics.mean(x['source_offset_after_ms'] for x in frames if x['wall']>frames[-1]['wall']-5)}
  runs[name]=item
 transitions[name]=[x for x in rows if x.get('kind')=='speed-transition'];loops[name]=[x for x in rows if x.get('kind')=='loop-boundary'];recovery[name]=[x for x in rows if x.get('kind') in ['rapid-pause-resume','queue-depletion','output-recreated','decode-starvation','actual-audio-starvation','starvation-recovery','render-delay','render-delay-invariant']]
for name,value in [('4K_PERFORMANCE_RESULTS.json',{k:v for k,v in runs.items() if k.startswith(('4k','1080'))}),('AV_SYNC_RESULTS.json',{k:v for k,v in runs.items() if k.startswith('av-')}),('SPEED_TRANSITION_RESULTS.json',{'events':transitions,'run':runs.get('speed-live'),'boundary':'actual queue recreation; predecoded atempo, source identity rebased; not seamless streaming'}),('LOOP_RESULTS.json',{'events':loops,'run':runs.get('loop'),'gapless':False}),('RECOVERY_RESULTS.json',{'events':recovery,'run':runs.get('recovery')})]:
 (p/name).write_text(json.dumps(value,indent=2)+'\n')
manifest={}
for f in sorted(raw.glob('*')):
 if f.suffix=='.jsonl':
  b=f.read_bytes();packed=gzip.compress(b,mtime=0);q=f.with_suffix('.jsonl.gz');q.write_bytes(packed);manifest[q.name]={'sha256':hashlib.sha256(packed).hexdigest(),'raw_sha256':hashlib.sha256(b).hexdigest(),'rows':len(b.splitlines())};f.unlink()
 elif f.name.endswith('.jsonl.gz'):
  b=gzip.decompress(f.read_bytes());manifest[f.name]={'sha256':hashlib.sha256(f.read_bytes()).hexdigest(),'raw_sha256':hashlib.sha256(b).hexdigest(),'rows':len(b.splitlines())}
 else:manifest[f.name]={'sha256':hashlib.sha256(f.read_bytes()).hexdigest()}
(p/'RAW_EVIDENCE_MANIFEST.json').write_text(json.dumps(manifest,indent=2)+'\n')
print(json.dumps({k:{'fps':v['render_ready_fps'],'skip':v['scheduled_skips'],'cpu':v['cpu_percent_one_core'],'offset_source':v['metrics']['source_offset_after_ms']['mean']} for k,v in runs.items()},indent=2))
