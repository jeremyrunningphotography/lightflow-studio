#!/usr/bin/env python3
"""Archive task-owned proof outputs; never runs native experiments."""
import base64, gzip, hashlib, json, shutil, sys, xml.etree.ElementTree as ET
from pathlib import Path
R=Path(__file__).resolve().parents[2]
D=R/'docs/research/mac-compatibility/x3-g3-completion'
C=R/'.cache/g3-completion'
W=R/'work/g3-completion'
D.mkdir(parents=True,exist_ok=True)
def digest(p): return hashlib.sha256(p.read_bytes()).hexdigest()
def save(name,v): (D/name).write_text((json.dumps(v,indent=2)+'\n').replace(str(R),'TASK_ROOT'))
def normalized(b): return b.replace(str(R).encode(),b'TASK_ROOT').replace(str(Path.home()).encode(),b'LOCAL_HOME')
def gz(src,dest):
 dest.parent.mkdir(parents=True,exist_ok=True);dest.write_bytes(gzip.compress(normalized(src.read_bytes()),mtime=0))
def rows(run):return [json.loads(x) for x in (C/run/'results.jsonl').read_text().splitlines()]
def select(r,*k):return [x for x in r if x['kind'] in k]
for p in sorted(W.glob('*.log')): gz(p,D/'raw'/(p.name+'.gz'))
for p in sorted(C.glob('*/results.jsonl')):gz(p,D/'raw'/(p.parent.name+'.jsonl.gz'))
final=rows('final-2'); inp=rows('input-final-qualified')
for name,data in {
 'UIACCEPTED_RESULTS':select(final,'interop','slot','UIAccepted','capture-token','rejected','interop-complete','teardown'),
 'DETAILS_PERFORMANCE_RESULTS':select(final,'population','details-cycle','details-summary','grid-recycling'),
 'SELECTION_ANCHOR_RESULTS':select(final,'selection-anchor','filter-hidden-selection'),
 'SHORTCUT_RESULTS':select(inp,'shortcut-capture','focus-order','input-summary','assert'),
 'MENU_COMMAND_RESULTS':select(inp,'native-menu','input-summary','assert'),
 'ACCESSIBILITY_RESULTS':select(final,'native-ax','automation-peers')+select(inp,'native-ax','automation-peers','focus-order'),
 'IMAGE_PIPELINE_RESULTS':select(final,'image-vectors','image-adapter','heic-encoding','image-lifetime'),
 'COLOR_MANAGEMENT_RESULTS':select(final,'icc-vector','layer-color-adapter','display-profile'),
 'LIFECYCLE_RESULTS':select(final,'activation','fullscreen','lifecycle','teardown'),
}.items():save(name+'.json',{'authoritativeRuns':['final-2','input-final-qualified'],'records':data})
for run in ['final-2','input-final-qualified']:
 for p in (C/run).glob('*native-ax.json'):gz(p,D/'accessibility'/(run+'-'+p.name+'.gz'))
 for p in (C/run).glob('*.png'):
  if run=='final-2' and p.name in ['settings-shortcuts.png','context-menu.png','details-recycling-template.png']:continue
  dest=D/'screenshots'/p.name;dest.parent.mkdir(exist_ok=True);shutil.copyfile(p,dest)
save('FIXTURE_HASHES.json',[{'name':p.name,'sha256':digest(p),'bytes':p.stat().st_size} for p in sorted((C/'final-2/fixtures').iterdir())])
lock=json.loads((R/'tools/X3G3Completion/packages.lock.json').read_text());deps=[]
for name,e in lock['dependencies']['net8.0'].items():
 p=R/'.cache/nuget'/name.lower()/e['resolved']; nus=p/(name.lower()+'.nuspec');t=ET.parse(nus).getroot();ns={'n':t.tag.split('}')[0][1:]};lic=t.find('.//n:license',ns);repo=t.find('.//n:repository',ns)
 deps.append({'name':name,'version':e['resolved'],'license':lic.text if lic is not None else None,'licenseType':lic.attrib if lic is not None else {},'repository':repo.attrib if repo is not None else {},'nugetContentHash':e['contentHash'],'nupkgSha256':digest(p/(name.lower()+'.'+e['resolved']+'.nupkg')),'provenance':'https://api.nuget.org/v3-flatcontainer/'+name.lower()+'/'+e['resolved']+'/'+name.lower()+'.'+e['resolved']+'.nupkg'})
 for f in p.iterdir():
  if f.is_file() and any(w in f.name.lower() for w in ['license','notice','third-party']):
   gz(f,D/'licenses'/(name+'-'+f.name+'.gz'))
for p in [R/'work/avalonia-12.1.3/licence.md',R/'docs/research/mac-compatibility/x3-iosurface/ANGLE_LICENSE.txt']:
 if p.exists():gz(p,D/'licenses'/(p.name+'.gz'))
art=R/'tools/X3G3Completion/bin/Release/net8.0/osx-arm64'
save('VERSION_BASELINE.json',{'main':'1777a4bbb7921ed481f49b204262bb3521ea2922','avalonia':'12.1.3','tableView':'Avalonia.Controls core 12.1.3 MIT','skiaSharp':'3.119.4','harfBuzzSharp':'8.3.1.3','avaloniaSourceCommit':'8eeda4f6f546165b3f72e63c9f42247abb306905','sdk':'8.0.425','nativeBridgeDeploymentTarget':'macOS 12.0','testedOS':'26.6.2 (25G83)','productOSFloor':'not established','packages':deps,'nativeArtifacts':[{'path':str(p.relative_to(R)),'sha256':digest(p),'bytes':p.stat().st_size} for p in [*art.glob('*.dylib'),W/'libx3bridge.dylib']]})
save('RUN_INDEX.json',[{'run':p.parent.name,'assertions':sum(x['kind']=='assert' for x in rows(p.parent.name)),'failures':[x['value'] for x in rows(p.parent.name) if x['kind']=='failure'],'teardown':select(rows(p.parent.name),'teardown')} for p in sorted(C.glob('*/results.jsonl'))])
save('SOURCE_HASHES.json',[{'path':str(p.relative_to(R)),'sha256':digest(p)} for p in sorted((R/'tools/X3G3Completion').iterdir()) if p.is_file()])
print('Archived raw logs, result projections, images, native AX, fixtures and dependency provenance.')
# Compact diagnostic history and license copies without discarding their content.
import io,tarfile
p=R/'work/avalonia-12.1.3/NOTICE.md'
if p.exists():gz(p,D/'licenses/AVALONIA_NOTICE.md.gz')
def pack(paths,dest):
 stream=io.BytesIO()
 with tarfile.open(fileobj=stream,mode='w') as tar:
  for p in sorted(paths):
   payload=p.read_bytes();info=tarfile.TarInfo(str(p.relative_to(D)));info.size=len(payload);info.mtime=0;tar.addfile(info,io.BytesIO(payload))
 dest.write_bytes(gzip.compress(stream.getvalue(),mtime=0))
 for p in paths:p.unlink()
pack(list((D/'licenses').glob('*')),D/'LICENSES.tar.gz')
(D/'licenses').rmdir()
keep={'final-1.jsonl.gz','final-2.jsonl.gz','input-final-qualified.jsonl.gz'}
pack([p for p in (D/'raw').glob('*.gz') if p.name not in keep],D/'DIAGNOSTIC_HISTORY.tar.gz')
