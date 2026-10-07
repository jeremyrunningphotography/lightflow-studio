"""Offline evidence verifier. Never launches native audio/UI processes."""
from pathlib import Path
import json,gzip,hashlib
r=Path(__file__).resolve().parents[2];p=r/'docs/research/mac-compatibility/x1-player-audio-completion'
manifest=json.loads((p/'RAW_EVIDENCE_MANIFEST.json').read_text())
for name,item in manifest.items():
 b=(p/'raw'/name).read_bytes();assert hashlib.sha256(b).hexdigest()==item['sha256']
 if name.endswith('.gz'):
  raw=gzip.decompress(b);assert hashlib.sha256(raw).hexdigest()==item['raw_sha256'];assert len(raw.splitlines())==item['rows']
def rows(name):return [json.loads(x) for x in gzip.decompress((p/'raw'/f'{name}.jsonl.gz').read_bytes()).splitlines()]
feed=rows('stream-feed-qualified')
rates=[x for x in feed if x['kind']=='stream-feed'];assert len(rates)==3
assert all(x['decoded_samples']==480000 and x['max_fifo']<=4816 for x in rates)
assert next(x for x in rates if x['rate']==1)['produced_samples']==480000
assert json.loads((p/'STREAMING_FEED_RESULTS.json').read_text())['one_x_matches_independent_decode']
assert all(x['decoded_samples']==x['produced_samples']==24000 and x['decoded_paused_without_pull'] for x in feed if x['kind']=='feed-seek-backpressure')
stress=rows('start-stress-final');starts=[x for x in stress if x['kind']=='epoch-start']
assert len(starts)==6 and all(x['supplied']==3072 and x['status']==-66681 for x in starts)
assert next(x for x in stress if x['kind']=='stress-summary')['active_queues']==0
assert rows('underrun')[0]['status']==-66681
assert rows('silent-start')[0]['start_status']==-66681
assert rows('runloop-start')[0]['start_status']==-66681
assert not any(x.get('kind')=='frame' for x in rows('underrun'))
assert json.loads((p/'FAULT_MATRIX.json').read_text())['g1']=='REVISE / UNPASSED'
assert json.loads((p/'CLOCK_PROTOCOL_SIMULATION.json').read_text())['scope'].startswith('independent')
for f in p.glob('*.json'):json.loads(f.read_text())
print('PASS: raw integrity, native bounded feed/content/seek/backpressure, 6 explicit start failures, zero queue accounting; no native underrun/AV success claim')
