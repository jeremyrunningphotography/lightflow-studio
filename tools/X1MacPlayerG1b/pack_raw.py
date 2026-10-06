"""Preserve full native rows in deterministic gzip archives, avoiding a giant textual PR."""
import gzip,hashlib,json,pathlib
r=pathlib.Path(__file__).resolve().parents[2];o=r/'docs/research/mac-compatibility/x1-player-g1b';raw=o/'raw';raw.mkdir(exist_ok=True);manifest=[]
for p in sorted(o.glob('*.jsonl')):
 data=p.read_bytes();target=raw/(p.name+'.gz');packed=gzip.compress(data,compresslevel=9,mtime=0);target.write_bytes(packed)
 manifest.append({'file':str(target.relative_to(o)),'sha256':hashlib.sha256(packed).hexdigest(),'uncompressed_sha256':hashlib.sha256(data).hexdigest(),'rows':len(data.splitlines()),'bytes':len(data),'compressed_bytes':len(packed)})
 p.unlink()
for p in o.glob('*.log'):p.rename(raw/p.name)
if manifest:(o/'RAW_EVIDENCE_MANIFEST.json').write_text(json.dumps(manifest,indent=2)+'\n')
print('Preserved',len(manifest),'complete JSONL archives')
