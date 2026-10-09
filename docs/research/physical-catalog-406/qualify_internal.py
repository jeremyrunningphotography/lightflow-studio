"""LF-BOTH-RES-007 internal APFS image baseline; never touches physical drive."""
import ctypes, hashlib, json, pathlib, sqlite3, subprocess, sys, time, uuid
ROOT = pathlib.Path(__file__).resolve().parent
RUN = ROOT / ('internal-' + uuid.uuid4().hex)
RUN.mkdir()
MOUNT = RUN / 'mount'
MOUNT.mkdir()
IMAGE = RUN / 'baseline.sparseimage'
LOG = {'agent':'LF-BOTH-RES-007','sqlite':sqlite3.sqlite_version,'python':sys.version,
       'evidence':'synthetic Python/system SQLite on internal APFS disk image; not production provider or physical qualification',
       'commands':[], 'checks':[], 'timings_ms':{}, 'native':[]}
lib = ctypes.CDLL(str(ROOT.parent / 'Lightflow.Platform.MacOS/native/libLightflowStorage.dylib'))
lib.lf_storage_create.restype=ctypes.c_void_p
lib.lf_storage_destroy.argtypes=[ctypes.c_void_p]
lib.lf_storage_probe.argtypes=[ctypes.c_void_p,ctypes.c_char_p,ctypes.c_int]
lib.lf_storage_probe.restype=ctypes.c_void_p
lib.lf_storage_free.argtypes=[ctypes.c_void_p]
lib.lf_storage_active_descriptors.restype=ctypes.c_int
def command(*args):
    p=subprocess.run(args,capture_output=True,text=True,timeout=60)
    LOG['commands'].append({'argv':args,'exit':p.returncode,'stdout':p.stdout,'stderr':p.stderr})
    if p.returncode: raise RuntimeError(p.stderr)
def check(name, value):
    LOG['checks'].append({'name':name,'passed':bool(value)})
    if not value: raise AssertionError(name)
def probe():
    context=lib.lf_storage_create()
    try:
        ptr=lib.lf_storage_probe(context,str(MOUNT).encode(),0)
        try: facts=json.loads(ctypes.string_at(ptr))
        finally: lib.lf_storage_free(ptr)
        LOG['native'].append(facts)
        check('native mount anchor held during probe',lib.lf_storage_active_descriptors()>0)
        return facts
    finally:
        lib.lf_storage_destroy(context)
        check('native anchors disposed before ordinary detach',lib.lf_storage_active_descriptors()==0)
def sha(path): return hashlib.sha256(path.read_bytes()).hexdigest()
def snapshot(path):
    c=sqlite3.connect('file:'+str(path)+'?mode=ro&immutable=1',uri=True)
    try:
        return {'integrity':c.execute('pragma integrity_check').fetchone()[0],
                'identity':c.execute('select * from identity').fetchall(),
                'authored':c.execute('select * from authored order by assetid').fetchall()}
    finally: c.close()
mounted=False
try:
    command('hdiutil','create','-size','256m','-fs','APFS','-volname','LF007InternalFixture','-type','SPARSE','-o',str(IMAGE))
    command('hdiutil','attach',str(IMAGE),'-nobrowse','-mountpoint',str(MOUNT)); mounted=True
    first=probe()
    check('APFS image local filesystem',first['filesystem']=='apfs' and first['locality']=='Local')
    db=MOUNT/'LightflowCatalog.db'
    t=time.perf_counter(); c=sqlite3.connect(db,timeout=.2)
    check('WAL enabled',c.execute('pragma journal_mode=WAL').fetchone()[0]=='wal')
    c.execute('pragma synchronous=FULL'); c.execute('pragma foreign_keys=ON')
    check('synchronous FULL',c.execute('pragma synchronous').fetchone()[0]==2)
    LOG['pragmas']={k:c.execute('pragma '+k).fetchone()[0] for k in ['journal_mode','synchronous','fullfsync','checkpoint_fullfsync','busy_timeout']}
    c.executescript('create table identity(catalogid text,rootid text); create table authored(assetid text primary key,rating integer,keywords text,collection text,note text,marker integer,subclip text);')
    identity=(str(uuid.uuid4()),str(uuid.uuid4()))
    c.execute('insert into identity values(?,?)',identity)
    rows=[(str(uuid.uuid4()),i%6,'keyword '+str(i),'collection','authored note',i*10,'10:20') for i in range(32)]
    c.executemany('insert into authored values(?,?,?,?,?,?,?)',rows); c.commit()
    LOG['timings_ms']['create_commit']=(time.perf_counter()-t)*1000
    check('WAL and SHM sidecars present',pathlib.Path(str(db)+'-wal').exists() and pathlib.Path(str(db)+'-shm').exists())
    c.execute('begin immediate')
    child="import sqlite3,sys; c=sqlite3.connect(sys.argv[1],timeout=.2);\ntry: c.execute('begin immediate');sys.exit(2)\nexcept sqlite3.OperationalError as e: print(str(e));sys.exit(0 if 'locked' in str(e) else 3)\nfinally:c.close()"
    p=subprocess.run([sys.executable,'-c',child,str(db)],capture_output=True,text=True,timeout=10)
    LOG['locking_child']={'code':p.returncode,'stdout':p.stdout,'stderr':p.stderr}
    check('second process writer rejected while transaction held',p.returncode==0); c.rollback()
    backup=MOUNT/'backup.db'; b=sqlite3.connect(backup); c.backup(b); b.close()
    t=time.perf_counter(); checkpoint=c.execute('pragma wal_checkpoint(TRUNCATE)').fetchone(); c.close()
    LOG['checkpoint']=checkpoint; LOG['timings_ms']['checkpoint_close']=(time.perf_counter()-t)*1000
    check('checkpoint completed',checkpoint==(0,0,0))
    LOG['sidecars_after_close']={suffix: pathlib.Path(str(db)+suffix).stat().st_size if pathlib.Path(str(db)+suffix).exists() else None for suffix in ['-wal','-shm']}
    check('closed checkpoint leaves no uncheckpointed WAL',LOG['sidecars_after_close']['-wal'] in (None,0))
    expected=snapshot(db)
    check('reopen identity and authored state',expected['integrity']=='ok' and expected['identity']==[identity] and len(expected['authored'])==32)
    check('SQLite backup state matches',snapshot(backup)==expected)
    restored=RUN/'restored.db'; restored.write_bytes(backup.read_bytes())
    check('restore logical equality',snapshot(restored)==expected)
    t=time.perf_counter(); copied=RUN/'closed-copy.db'; copied.write_bytes(db.read_bytes())
    LOG['timings_ms']['closed_copy']=(time.perf_counter()-t)*1000
    check('closed copy hash equality',sha(db)==sha(copied)); check('closed copy logical equality',snapshot(copied)==expected)
    source_hash=sha(db); final_hash=sha(copied)
    stage=RUN/'interrupted.partial'; stage.write_bytes(db.read_bytes()[:1024])
    check('interrupted copy rejected before publication',sha(stage)!=source_hash and sha(copied)==final_hash)
    stage.write_bytes(b'failed verification')
    check('failed verification retains prior destination and source',sha(stage)!=source_hash and sha(db)==source_hash and sha(copied)==final_hash)
    stage.unlink(); check('rollback removes only task staging',not stage.exists() and sha(copied)==final_hash)
    ro=sqlite3.connect('file:'+str(copied)+'?mode=ro&immutable=1',uri=True)
    try:
        try: ro.execute('update authored set rating=0'); check('read-only URI rejects authoring',False)
        except sqlite3.OperationalError as e:
            LOG['readonly_error']=str(e);check('read-only URI rejects authoring','readonly' in str(e))
    finally:ro.close()
    command('hdiutil','detach',str(MOUNT));mounted=False
    command('hdiutil','attach',str(IMAGE),'-readonly','-nobrowse','-mountpoint',str(MOUNT));mounted=True
    second=probe(); check('read-only remount metadata rejects writes',not second['write'])
    check('remount receives different binding',first['mountEpoch']!=second['mountEpoch'])
    ro=sqlite3.connect('file:'+str(db)+'?mode=ro&immutable=1',uri=True)
    try:
        try:ro.execute('update authored set rating=0');check('readonly image rejects authoring',False)
        except sqlite3.OperationalError as e:check('readonly image rejects authoring','readonly' in str(e))
    finally:ro.close()
    check('read-only remount preserved hashes and state',sha(db)==source_hash and snapshot(db)==expected)
    LOG['hashes']={str(p.relative_to(RUN)):sha(p) for p in [db,backup,copied,restored]}
    LOG['snapshot']=expected
    LOG['passed']=True
except BaseException as e:
    LOG['passed']=False;LOG['failure']=repr(e);raise
finally:
    if mounted:
        try:command('hdiutil','detach',str(MOUNT));LOG['ordinary_detach']=True
        except Exception as e:LOG['detach_failure']=repr(e)
    LOG['final_native_descriptors']=lib.lf_storage_active_descriptors()
    (RUN/'results.json').write_text(json.dumps(LOG,indent=2))
    print(RUN/'results.json')
