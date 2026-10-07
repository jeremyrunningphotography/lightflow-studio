#!/usr/bin/env python3
"""Validate immutable saved G3 evidence, without executing a UI or native code."""
import gzip,hashlib,json,sys
from pathlib import Path
R=Path(__file__).resolve().parents[2];D=R/'docs/research/mac-compatibility/x3-g3-completion'
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def get(name):return json.loads((D/name).read_text())
def records(run):return [json.loads(l) for l in gzip.decompress((D/'raw'/(run+'.jsonl.gz')).read_bytes()).splitlines()]
def kind(r,k):return [x['value'] for x in r if x['kind']==k]
if '--seal' in sys.argv:
 files=[p for p in D.rglob('*') if p.is_file() and p.name!='EVIDENCE_MANIFEST.json']
 files += [p for p in (R/'tools/X3G3Completion').rglob('*') if p.is_file() and not any(x in p.parts for x in ['bin','obj','__pycache__'])]
 (D/'EVIDENCE_MANIFEST.json').write_text(json.dumps([{'path':str(p.relative_to(R)),'sha256':sha(p),'bytes':p.stat().st_size} for p in sorted(files)],indent=2)+'\n')
for e in get('EVIDENCE_MANIFEST.json'):assert sha(R/e['path'])==e['sha256'],e['path']
for run in ['final-1','final-2','input-final-qualified']:
 r=records(run);assert not kind(r,'failure'),run
 assert all(v['ok'] for v in kind(r,'assert')),run
 assert kind(r,'teardown')==[{'surfaces':0,'producers':0}],run
 print(run,len(kind(r,'assert')),'passing assertions; teardown balanced')
f=records('final-2');i=records('input-final-qualified')
a=kind(f,'UIAccepted');c=kind(f,'capture-token')
assert len(a)==len(c)==9 and len(kind(f,'rejected'))==6
assert len({v['Serial'] for v in a})==9
for x,y in zip(a,c):
 assert x['Serial']==y['Serial']==y['decoded'] and y['blocked']
 assert x['ColorRevision']==y['ColorRevision'] and x['Generation']==y['Generation']
 assert x['dispatcher'] and not x['physicalScanout']
s=kind(f,'details-summary');assert len(s)==6
for x in s:assert x['maxRows']<=24 and x['uniqueRows']<=25 and x['maxCells']==18*x['maxRows'] and x['multiselect']==2
stock=next(x for x in s if x['count']==100000 and x['mode']=='stock');ret=next(x for x in s if x['count']==100000 and x['mode']=='retained')
assert ret['allocated']<stock['allocated'] and ret['p95']<stock['p95']
assert len(kind(f,'selection-anchor'))==12 and all(x['anchor'] for x in kind(f,'selection-anchor'))
assert kind(i,'input-summary')[0]['menuActions']==1
assert len(kind(i,'shortcut-capture'))==12
menu=json.loads(kind(i,'native-menu')[0]['json']);assert any(x['title']=='Settings…' and x['key']==',' and x['modifiers']==1048576 for x in menu)
v=kind(f,'image-vectors')[0];o=[x for x in v if x['test']=='orientation'];assert len(o)==8 and all(x['passed'] for x in o)
assert o[-1]['pixels']==[2,5,1,4,0,3]
assert len(kind(f,'icc-vector'))==20 and all(x['ok'] for x in kind(f,'icc-vector'))
assert kind(f,'image-lifetime')[0]['buffers']==0
for fixture in ['vector.tiff','tiff-lzw.tiff','tiff-packbits.tiff','vector.heic']:
 x=next(x for x in kind(f,'image-adapter') if x['fixture']==fixture);assert x['imageIO'] and not x['skia']
def ax(name):return json.loads(gzip.decompress((D/'accessibility'/name).read_bytes()))
g=ax('final-2-Grid-selected-native-ax.json.gz');assert any(x['role']=='AXRow' and x['selected'] and '055555' in x['name'] for x in g)
d=ax('final-2-details-retained-native-ax.json.gz');assert any(x['role']=='AXRow' and x['selected'] for x in d);assert any(x['role']=='AXStaticText' and 'column' in x['value'] and 'row' in x['value'] for x in d)
assert kind(f,'lifecycle')[0]['focus'] and kind(f,'lifecycle')[0]['closeReopen']
assert len(get('FAULT_MATRIX.json')['cases'])>=25
print('Evidence integrity and bounded G3 contract checks passed; this does not constitute owner acceptance.')
