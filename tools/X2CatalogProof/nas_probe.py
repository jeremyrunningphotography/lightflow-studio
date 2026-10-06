"""Bounded owner-authorized SMB filesystem and CLOSED snapshot transport probe.
No live database on NAS, mount changes, permission changes or owner-file reads.
"""
from pathlib import Path
import hashlib, json, os, subprocess, sys, uuid
root=Path(__file__).resolve().parents[2]
allowed=Path('/Volumes/Development/codex')
mounts=subprocess.check_output(['mount','-t','smbfs'],text=True)
matching=[s for s in mounts.splitlines() if '/Development on /Volumes/Development (smbfs,' in s and 'JRVault' in s]
if len(matching)!=1 or not allowed.is_dir() or allowed.is_symlink():
    raise SystemExit('Expected owner-authorized SMB mount/folder unavailable; no writes.')
run='x2-nas-'+uuid.uuid4().hex
nas=allowed/run
local=root/'work/evidence'/run
local.mkdir()
report={'scope':str(nas),'share':'smb://JRVault/Development','protocol':'smbfs','liveSQLiteOnNAS':False,'rows':[],'limitations':['No disconnect/unmount or permission fault induced','fsync success is not proof of remote power-loss durability','No SQLite network locking/WAL qualification','No production MediaRoot service qualification in this filesystem-only probe']}
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def exclusive(p,data):
    with p.open('xb') as f:f.write(data);f.flush();os.fsync(f.fileno())
try:
    nas.mkdir()
    for label,a,b in [('case','Case.mov','case.mov'),('unicode','caf\u00e9.mov','cafe\u0301.mov'),('backslash','nested/name.mov','nested\\name.mov'),('spaces','trim.mov',' trim.mov ')]:
        pa,pb=nas/a,nas/b
        pa.parent.mkdir(parents=True,exist_ok=True)
        exclusive(pa,('A:'+label).encode())
        try:exclusive(pb,('B:'+label).encode());status='created'
        except FileExistsError:status='existing-name'
        sa,sb=pa.stat(),pb.stat()
        report['rows'].append({'label':label,'a':a,'b':b,'secondCreate':status,'sameFileId':(sa.st_dev,sa.st_ino)==(sb.st_dev,sb.st_ino),'aDevice':sa.st_dev,'aInode':sa.st_ino,'bDevice':sb.st_dev,'bInode':sb.st_ino,'hashA':sha(pa),'hashB':sha(pb)})
    for name in ['CON.mov','colon:name.mov','question?.mov','trailing-dot.mov.','emoji-\U0001f600.mov']:
        try:exclusive(nas/name,b'X2 disposable name probe');result='created'
        except OSError as ex:result={'errno':ex.errno,'message':str(ex)}
        report['rows'].append({'name':name,'result':result})
    source=root/'work/data/final-control-export/catalog.db'
    manifest=json.loads((source.parent/'manifest.json').read_text())
    source_hash=sha(source)
    assert source_hash.upper()==manifest['databaseSha256']
    target=nas/'closed-catalog.db'
    exclusive(target,source.read_bytes())
    assert sha(target)==source_hash
    renamed=nas/'closed-catalog-renamed.db';target.rename(renamed)
    returned=local/'returned-catalog.db';exclusive(returned,renamed.read_bytes())
    assert sha(returned)==source_hash
    snapshot=local/'returned-snapshot.json'
    subprocess.run([sys.executable,str(root/'tools/X2CatalogProof/run.py'),str(root/'tools/X2CatalogProof/bin/Release/net8.0/X2CatalogProof.dll'),'snapshot',str(returned),str(snapshot)],cwd=root,check=True)
    assert json.loads(snapshot.read_text())==json.loads((source.parent/'snapshot.json').read_text())
    report['closedBackupTransport']={'sourceSha256':source_hash,'nasReadbackSha256':sha(renamed),'returnedSha256':sha(returned),'allTableSnapshotEqual':True,'operations':['exclusive create','flush/fsync','readback','same-directory rename','copy back to local','local pinned-provider all-table read']}
    report['status']='completed-bounded-probe'
except BaseException as ex:
    report['status']='failed';report['error']=repr(ex);raise
finally:
    (local/'NAS_PROBE.json').write_text(json.dumps(report,indent=2,ensure_ascii=False)+'\n')
    print(json.dumps({'evidence':str(local),'nasFixtureRetained':str(nas),'status':report['status']}))
