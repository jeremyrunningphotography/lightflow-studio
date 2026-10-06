"""Owner-authorized disposable live NAS probes; kill only owned child handles."""
from pathlib import Path
import os,json,uuid,subprocess,time,sys,hashlib
r=Path(__file__).resolve().parents[2]
parent=Path('/Volumes/Development/codex')
mount=subprocess.check_output(['mount','-t','smbfs'],text=True)
if not any('JRVault' in x and '/Development on /Volumes/Development (smbfs,' in x for x in mount.splitlines()) or parent.is_symlink() or not parent.is_dir():raise SystemExit('Authorized mount unavailable')
name=sys.argv[1] if len(sys.argv)>1 else 'x2-live-'+uuid.uuid4().hex;nas=parent/name
if len(sys.argv)==1:
 nas.mkdir();(nas/'X2-OWNED.json').write_text(json.dumps({'task':str(r),'run':name}))
else:
 assert name.startswith('x2-live-') and '/' not in name and json.loads((nas/'X2-OWNED.json').read_text())['task']==str(r)
local=r/'work/data'/name;local.mkdir(exist_ok=True)
env=os.environ.copy()
for k,v in {'DOTNET_CLI_HOME':'cli','NUGET_PACKAGES':'nuget','NUGET_HTTP_CACHE_PATH':'nuget-http','TMPDIR':'tmp','TMP':'tmp','TEMP':'tmp','DOTNET_BUNDLE_EXTRACT_BASE_DIR':'native-extract'}.items():
 p=r/'work'/v;p.mkdir(exist_ok=True);env[k]=str(p)
env.update(X2_WORKSPACE=str(r),DOTNET_CLI_TELEMETRY_OPTOUT='1',DOTNET_NOLOGO='1')
exe=r/'work/toolchain/dotnet/dotnet';dll=r/'tools/X2CatalogProof/bin/Release/net8.0/X2CatalogProof.dll'
commands=[]
def run(args,label,timeout=150):
 argv=[str(exe),str(dll),*map(str,args)];commands.append({'argv':argv,'timeout':timeout})
 with (local/(label+'.log')).open('w') as f:
  p=subprocess.Popen(argv,env=env,cwd=r,stdout=f,stderr=subprocess.STDOUT)
  try:code=p.wait(timeout=timeout)
  except BaseException:p.kill();p.wait();raise
 if code:raise RuntimeError(label+' failed '+str(code))
commands.append({'mount': [x for x in mount.splitlines() if '/Development on /Volumes/Development' in x]})
cap=subprocess.run(['smbutil','statshares','-m','/Volumes/Development'],capture_output=True,text=True);(local/'smb-capabilities.txt').write_text(cap.stdout+cap.stderr)
st=os.statvfs(parent)
(local/'NAS_ENVIRONMENT.json').write_text(json.dumps({'nas':str(nas),'local':str(local),'mountType':'smbfs','statvfs':{'blockSize':st.f_bsize,'fragmentSize':st.f_frsize,'blocks':st.f_blocks,'available':st.f_bavail,'flags':st.f_flag,'nameMax':st.f_namemax},'capabilitiesExit':cap.returncode,'branch':subprocess.check_output(['git','branch','--show-current'],cwd=r,text=True).strip(),'headBeforeHarnessCommit':subprocess.check_output(['git','rev-parse','HEAD'],cwd=r,text=True).strip(),'baseline':'aed6c2906637c8a5a9d71c1c18ac24bed48a8724','clientTimeZone':'America/Los_Angeles'},indent=2))
try:
 run(['nas-alternative',local,nas],'nas-alternative')
 crash=[]
 for journal in ['delete']:
  db=nas/('catalog/LightflowCatalog.db' if journal=='wal' else 'delete-control/catalog.db')
  ready=local/(journal+'-ready.json');argv=[str(exe),str(dll),'nas-crash-child',str(local),str(nas),str(ready),journal]
  commands.append({'argv':argv})
  with (local/(journal+'-crash-child.log')).open('w') as f:
   p=subprocess.Popen(argv,env=env,cwd=r,stdout=f,stderr=subprocess.STDOUT)
   try:
    deadline=time.monotonic()+35
    while not ready.exists() and p.poll() is None and time.monotonic()<deadline:time.sleep(.1)
    if not ready.exists():raise RuntimeError('Crash child readiness timeout')
    signal=json.loads(ready.read_text());assert signal['pid']==p.pid
    p.kill();code=p.wait(timeout=10)
    artifacts={ext:{'exists':Path(str(db)+ext).exists(),'bytes':Path(str(db)+ext).stat().st_size if Path(str(db)+ext).exists() else None} for ext in ['-wal','-shm','-journal']}
   finally:
    if p.poll() is None:p.kill();p.wait(timeout=10)
  run(['nas-crash-recover',local,nas,local/(journal+'-recovery.json'),journal],journal+'-recovery')
  run(['snapshot',r/'work/data/final-control-export/catalog.db',local/'source-snapshot.json'],'source-local-snapshot') if not (local/'source-snapshot.json').exists() else None
  # NAS snapshot is read via a local proof method allowing NAS-owned db only in recovery; recovery JSON captures integrity. Full live state retained in results.
  crash.append({'journal':journal,'pid':signal['pid'],'exit':code,'artifactsAfterKill':artifacts,'recovery':'passed','limits':'process termination, not server/network/power failure'})
 (local/'CRASH_RESULTS.json').write_text(json.dumps(crash,indent=2))
 (local/'STATUS.json').write_text(json.dumps({'status':'completed observational controls','nas':str(nas),'local':str(local),'liveNASsupport':'NOT established'}))
finally:
 (local/'COMMANDS.json').write_text(json.dumps(commands,indent=2))
 print(json.dumps({'localEvidence':str(local),'retainedNAS':str(nas)}))
