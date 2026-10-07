"""Offline evidence assertions only: never launch native experiments."""
from pathlib import Path
import json,gzip,hashlib
r=Path(__file__).resolve().parents[2];p=r/'docs/research/mac-compatibility/x1-player-completion'
m=json.loads((p/'RAW_EVIDENCE_MANIFEST.json').read_text())
for name,item in m.items():
 b=(p/'raw'/name).read_bytes();assert hashlib.sha256(b).hexdigest()==item['sha256']
 if name.endswith('.gz'):
  raw=gzip.decompress(b);assert hashlib.sha256(raw).hexdigest()==item['raw_sha256'];assert len(raw.splitlines())==item['rows']
def rows(name):return [json.loads(x) for x in gzip.decompress((p/'raw'/f'{name}.jsonl.gz').read_bytes()).splitlines()]
perf=json.loads((p/'4K_PERFORMANCE_RESULTS.json').read_text())
for name in ['4k-backend-color','4k-backend-off']:
 x=perf[name];assert x['frames']==1800 and x['scheduled_skips']==0 and x['queue_depth_max']==1 and x['native_drawable_callbacks']==0
semantic=rows('semantic-ready');captures=[x for x in semantic if x.get('capture_serial',0)]
assert all(x['capture_serial']==x['published_serial']==x['serial'] and x['producer_completed'] and x['consumer_completed'] and x['cpu_oracle_max_channel_error']<=1 for x in captures)
assert any(x.get('stale_rejected') for x in semantic)
assert next(x for x in semantic if x.get('action')=='stale-invariant')['retained_unchanged']
fault=rows('render-fault');assert next(x for x in fault if x.get('kind')=='render-delay-invariant')['held_hash_unchanged']
starved=rows('actual-starvation')[0];assert starved['withheld_refills']==3 and starved['is_running']==1 and starved['after']>starved['enqueued_samples']/48000
loop=rows('loop');n=0
for i,x in enumerate(loop):
 if x.get('kind')=='loop-boundary':
  nxt=next(y for y in loop[i+1:] if y.get('kind')=='frame');assert nxt['pts']==0 and nxt['generation']==x['generation'];n+=1
assert n==7
protocol=json.loads((p/'STATE_AUTHORITY_RESULTS.json').read_text());assert protocol['accepted']>0 and protocol['rejected']>0
assert json.loads((p/'OPERATION_STATE_MATRIX.json').read_text())['physical_scanout_required'] is False
print('PASS: raw integrity, bounded capacity, same-token native capture, stale rejection, held pixels, negative underrun, loop identity and simulated protocol; no physical/UI acceptance claim')
