"""Offline audit of durable G1b evidence; optional retained-input/build verification."""
import ast,gzip,hashlib,json,pathlib,re,subprocess
r=pathlib.Path(__file__).resolve().parents[2];o=r/'docs/research/mac-compatibility/x1-player-g1b';sha=lambda b:hashlib.sha256(b).hexdigest()
for p in (r/'tools/X1MacPlayerG1b').glob('*.py'):ast.parse(p.read_text(),filename=str(p))
for p in o.rglob('*.json'):json.loads(p.read_text())
manifest=json.loads((o/'RAW_EVIDENCE_MANIFEST.json').read_text())
for e in manifest:
 b=(o/e['file']).read_bytes();assert sha(b)==e['sha256'];data=gzip.decompress(b);assert sha(data)==e['uncompressed_sha256'] and len(data.splitlines())==e['rows']
 for line in data.splitlines():json.loads(line)
p=json.loads((o/'DEPENDENCY_PROVENANCE.json').read_text())
assert p['ffmpeg']['commit']=='bf1b838f2ab88b4f8fd83443325c782ea0e0f7fa' and not p['ffmpeg']['source_changes']
assert 'LGPL version 2.1 or later' in p['ffmpeg']['license']
assert all(x.endswith(' 0') for x in p['ffmpeg']['license_flags'])
assert '--enable-shared' in p['ffmpeg']['configuration'] and '--enable-gpl' not in p['ffmpeg']['configuration']
assert p['avalonia']['commit']=='6dd9eb473b74a56cc42e5bc118cfe918b48a940b'
for e in p['proof']:
 if 'source' in e:assert sha((r/e['source']).read_bytes())==e['sha256']
 elif (r/e['file']).exists():assert sha((r/e['file']).read_bytes())==e['sha256']
for e in p['inputs']:
 if (r/e['file']).exists():assert sha((r/e['file']).read_bytes())==e['sha256']
for e in json.loads((o/'fixtures.json').read_text()):
 if (r/e['path']).exists():assert sha((r/e['path']).read_bytes())==e['sha256']
for n in ['process-results.json','supplemental-process-results.json','unlocked-process-results.json']:
 for e in json.loads((o/n).read_text()):assert e['returncode']==e.get('expected_returncode',0)
for p in o.glob('*.md'):
 for target in re.findall(r'\]\(([^)]+)\)',p.read_text()):
  if not re.match(r'https?://',target):assert (p.parent/target.split('#')[0]).exists(),(p,target)
for x in json.loads((o/'FAULT_MATRIX.json').read_text())['rows']:
 assert x['status'] in ['proven','simulated','reasoned','unresolved'];assert (o/x['evidence']).exists(),x
subprocess.run(['python3',str(r/'tools/X1MacPlayerG1b/analyze.py')],check=True)
color=json.loads((o/'COLOR_CAPTURE_RESULTS.json').read_text())['variants']
sw=next(x for x in color if x['case']=='semantic');hw=next(x for x in color if x['case']=='hardware-color-unlocked')
assert sw['acknowledged']==hw['acknowledged']==20 and sw['max_8bit_channel_error']<=1 and hw['max_8bit_channel_error']<=2
base='7b7f1c2cac5426f7df145921d8890adde6f9c490'
for frozen in ['docs/research/mac-compatibility/x1-player','tools/X1MacPlayerProof']:
 assert not subprocess.check_output(['git','diff',base,'--',frozen])
print('PASS: archive integrity, exact authority/PTS/reverse/lease checks, Color bounds, source/input pins, license flags, links, process exits and frozen G1 evidence')
