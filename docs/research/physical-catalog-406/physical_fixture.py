"""LF-BOTH-RES-007: owner-approved bounded physical fixture; exactly one new task root.
No product admission override, existing owner data, eject, Windows, force removal or repair.
"""
import ctypes, errno, fcntl, hashlib, json, os, pathlib, sqlite3, stat, subprocess, sys, time, uuid
WORK=pathlib.Path(__file__).resolve().parent
LIMIT=256*1024**2
LOG={'agent':'LF-BOTH-RES-007','evidence':'physical ExFAT; synthetic Python/system SQLite, not production provider/schema',
     'sqlite':sqlite3.sqlite_version,'python':sys.version,'checks':[],'operations':[],'errors':[], 'timings_ms':{}}
LIB=ctypes.CDLL(str(WORK.parent/'Lightflow.Platform.MacOS/native/libLightflowStorage.dylib'))
LIB.lf_storage_active_descriptors.restype=ctypes.c_int
def check(name,value):
    LOG['checks'].append({'name':name,'passed':bool(value)})
    if not value:raise AssertionError(name)
def run(*args):
    p=subprocess.run(args,capture_output=True,text=True,timeout=20)
    if p.returncode:raise RuntimeError({'command':args,'exit':p.returncode,'stderr':p.stderr})
    return p.stdout
def disk(location):
    import plistlib
    return plistlib.loads(subprocess.check_output(['diskutil','info','-plist',str(location)],timeout=20))
def native(path):return json.loads(run(str(WORK/'root_safety'),str(path)))
class Guard:
    def __init__(self,volume,root=None,root_identity=None):
        self.volume=volume;self.mount=pathlib.Path(volume['MountPoint'])
        self.mountfd=os.open(self.mount,os.O_RDONLY|os.O_DIRECTORY|os.O_NOFOLLOW)
        self.mountstat=os.fstat(self.mountfd);self.mountfacts=native(self.mount)
        self.root=None;self.rootfd=None;self.rootstat=None;self.peak=0;self.preflight('mount baseline')
        if root is not None:
            self.root=pathlib.Path(root)
            self.rootfd=os.open(self.root,os.O_RDONLY|os.O_DIRECTORY|os.O_NOFOLLOW)
            self.rootstat=os.fstat(self.rootfd)
            if root_identity is not None:assert [self.rootstat.st_dev,self.rootstat.st_ino]==root_identity
            self.preflight('child existing task root')
    def preflight(self,label):
        v=disk(self.mount)
        for k in ['VolumeName','VolumeUUID','DiskUUID','ParentWholeDisk','DeviceIdentifier','FilesystemType','BusProtocol']:
            if v.get(k)!=self.volume.get(k):raise RuntimeError('volume changed: '+k)
        if not v['WritableVolume'] or v['Internal'] or v['FreeSpace']<LIMIT:raise RuntimeError('volume state unsafe')
        ms=os.stat(self.mount,follow_symlinks=False)
        if (ms.st_dev,ms.st_ino)!=(self.mountstat.st_dev,self.mountstat.st_ino) or stat.S_ISLNK(ms.st_mode):raise RuntimeError('mount root changed')
        facts=native(self.mount if self.root is None else self.root)
        for k in ['volumeUUID','filesystem','source','mount','fsid']:
            if facts[k]!=self.mountfacts[k]:raise RuntimeError('native mount binding changed: '+k)
        if facts['alias'] or facts['cloud'] or facts['readonly'] or not facts['local']:raise RuntimeError('alias/cloud/readonly/locality ambiguity')
        if self.root is not None:
            rs=os.stat(self.root,follow_symlinks=False);held=os.fstat(self.rootfd)
            if (rs.st_dev,rs.st_ino)!=(self.rootstat.st_dev,self.rootstat.st_ino) or (held.st_dev,held.st_ino)!=(rs.st_dev,rs.st_ino):raise RuntimeError('task root replaced')
            if stat.S_ISLNK(rs.st_mode) or self.root.resolve()!=self.root or self.root.parent!=self.mount:raise RuntimeError('task containment ambiguity')
            footprint=self.measure()
            if max(footprint.values())>LIMIT:raise RuntimeError('task footprint exceeded')
        LOG['operations'].append({'preflight':label,'utc':time.time()})
    def measure(self):
        logical=0;allocated=os.fstat(self.rootfd).st_blocks*512
        # Enumeration is strictly inside the exclusively created task root.
        for name in os.listdir(self.rootfd):
            s=os.stat(name,dir_fd=self.rootfd,follow_symlinks=False)
            if not stat.S_ISREG(s.st_mode):raise RuntimeError('unexpected link/subdirectory in task root')
            facts=native(self.root/name)
            if facts['alias'] or facts['cloud']:raise RuntimeError('unexpected file alias')
            logical+=s.st_size;allocated+=s.st_blocks*512
        self.peak=max(self.peak,allocated,logical)
        return {'logical_bytes':logical,'allocated_bytes_including_root':allocated}
    def create_root(self):
        name='LF-BOTH-RES-007-Qualification-'+str(uuid.uuid4())
        self.preflight('exclusive directory create')
        try:os.stat(name,dir_fd=self.mountfd,follow_symlinks=False)
        except FileNotFoundError:pass
        else:raise RuntimeError('UUID collision; nothing replaced')
        os.mkdir(name,dir_fd=self.mountfd) # exclusive, no replacement
        self.root=self.mount/name
        self.rootfd=os.open(name,os.O_RDONLY|os.O_DIRECTORY|os.O_NOFOLLOW,dir_fd=self.mountfd)
        self.rootstat=os.fstat(self.rootfd);self.preflight('created root bound')
        return self.root
    def path(self,name):
        if '/' in name or name in ['.','..'] or not name:raise RuntimeError('invalid fixture leaf')
        self.preflight(name)
        try:
            s=os.stat(name,dir_fd=self.rootfd,follow_symlinks=False)
            if not stat.S_ISREG(s.st_mode):raise RuntimeError('unexpected fixture link')
        except FileNotFoundError:pass
        return self.root/name
    def create(self,name,data=b''):
        self.path(name)
        if len(data)>1024*1024:raise RuntimeError('unbounded fixture data')
        fd=os.open(name,os.O_CREAT|os.O_EXCL|os.O_WRONLY|os.O_NOFOLLOW,0o600,dir_fd=self.rootfd)
        try:
            with os.fdopen(fd,'wb',closefd=False) as f:f.write(data);f.flush();os.fsync(fd)
        finally:os.close(fd)
        return self.root/name
    def read(self,name):
        self.path(name);fd=os.open(name,os.O_RDONLY|os.O_NOFOLLOW,dir_fd=self.rootfd)
        try:
            with os.fdopen(fd,'rb',closefd=False) as f:return f.read()
        finally:os.close(fd)
    def publish(self,source,target):
        self.path(source);self.path(target)
        libc=ctypes.CDLL(None,use_errno=True)
        libc.renameatx_np.argtypes=[ctypes.c_int,ctypes.c_char_p,ctypes.c_int,ctypes.c_char_p,ctypes.c_uint]
        if libc.renameatx_np(self.rootfd,source.encode(),self.rootfd,target.encode(),4):
            e=ctypes.get_errno();raise OSError(e,os.strerror(e)) # RENAME_EXCL: no replacement
    def close(self):
        if self.rootfd is not None:os.close(self.rootfd);self.rootfd=None
        os.close(self.mountfd)
def connection(g,name,readonly=False):
    path=g.path(name)
    c=sqlite3.connect('file:'+str(path)+('?mode=ro&immutable=1' if readonly else '?mode=rw'),uri=True,timeout=.25)
    if not readonly:
        g.path(name);c.execute('pragma temp_store=MEMORY')
        g.path(name);c.execute('pragma synchronous=FULL')
        g.path(name);c.execute('pragma foreign_keys=ON')
    return c
def snapshot(g,name):
    c=connection(g,name,True)
    try:return {'integrity':c.execute('pragma integrity_check').fetchone()[0],
                'identity':c.execute('select * from identity').fetchall(),
                'authored':c.execute('select * from authored order by assetid').fetchall()}
    finally:c.close()
def digest(data):return hashlib.sha256(data).hexdigest()
if len(sys.argv)>1 and sys.argv[1]=='--child':
    meta=json.loads(sys.argv[2]);g=Guard(meta['volume'],meta['root'],meta['root_identity']);c=None
    try:
        c=connection(g,'LightflowCatalog.db');g.path('LightflowCatalog.db')
        try:c.execute('begin immediate');print('unexpected writer acquisition');sys.exit(2)
        except sqlite3.OperationalError as e:
            print(json.dumps({'error':str(e),'code':getattr(e,'sqlite_errorcode',None),'name':getattr(e,'sqlite_errorname',None)}))
            sys.exit(0 if str(e)=='database is locked' else 3)
    finally:
        if c is not None:c.close()
        g.close()
g=None;c=None;b=None;root=None
local=WORK/'evidence'/('physical-run-'+str(uuid.uuid4())+'.json')
try:
    check('no counted native anchors before physical writes',LIB.lf_storage_active_descriptors()==0)
    v=disk('JRPhoto4T')
    import plistlib
    old=plistlib.loads((WORK/'evidence/volume.plist').read_bytes())
    check('prior volume and partition identity match',all(v[k]==old[k] for k in ['VolumeUUID','DiskUUID','VolumeName']))
    d=disk(v['ParentWholeDisk']);check('physical SanDisk external USB ExFAT',d['VirtualOrPhysical']=='Physical' and d['MediaName']=='Extreme Pro 55AF' and v['FilesystemType']=='exfat' and v['BusProtocol']=='USB' and not v['Internal'])
    resume=None
    if len(sys.argv)==3 and sys.argv[1]=='--resume':
        resume=json.loads(pathlib.Path(sys.argv[2]).read_text())
        if resume.get('errors')!=["AssertionError('cross-process physical SQLite writer excluded')"]:raise RuntimeError('resume only supported for preserved diagnostic failure')
        g=Guard(v,resume['root'],resume['root_identity']);root=g.root
        owner=json.loads(g.read('OWNERSHIP.json'));check('resume existing exclusive ownership matches',owner['agent']==LOG['agent'] and owner['run']==root.name and owner['root_identity']==resume['root_identity'])
        LOG['resumed_from']=str(pathlib.Path(sys.argv[2]).name)
    else:
        g=Guard(v);root=g.create_root()
    LOG['root']=str(root);LOG['volume']=v;LOG['physical_disk']=d
    LOG['root_identity']=[g.rootstat.st_dev,g.rootstat.st_ino]
    if resume is None:
        g.create('OWNERSHIP.json',json.dumps({'agent':LOG['agent'],'run':root.name,'root_identity':LOG['root_identity'],'limit':LIMIT},indent=2).encode())
        g.create('LightflowCatalog.db')
    t=time.perf_counter();c=connection(g,'LightflowCatalog.db')
    g.path('LightflowCatalog.db');check('physical WAL enabled',c.execute('pragma journal_mode=WAL').fetchone()[0]=='wal')
    LOG['pragmas']={k:c.execute('pragma '+k).fetchone()[0] for k in ['journal_mode','synchronous','fullfsync','checkpoint_fullfsync','busy_timeout','temp_store']}
    check('physical synchronous FULL',LOG['pragmas']['synchronous']==2)
    if resume is None:
        g.path('LightflowCatalog.db');c.executescript('create table identity(catalogid text,rootid text); create table authored(assetid text primary key,rating integer,keywords text,collection text,note text,marker integer,subclip text);')
        ids=(str(uuid.uuid4()),str(uuid.uuid4()))
        rows=[(str(uuid.uuid4()),i%6,'synthetic keyword '+str(i),'synthetic collection','authored note',i*10,'10:20') for i in range(32)]
        g.path('LightflowCatalog.db');c.execute('insert into identity values(?,?)',ids)
        g.path('LightflowCatalog.db');c.executemany('insert into authored values(?,?,?,?,?,?,?)',rows)
        g.path('LightflowCatalog.db');tcommit=time.perf_counter();c.commit();LOG['timings_ms']['commit']=(time.perf_counter()-tcommit)*1000
    else:
        prior=snapshot(g,'LightflowCatalog.db');check('resumed closed fixture integrity',prior['integrity']=='ok' and len(prior['authored'])==32)
        ids=tuple(prior['identity'][0]);rows=[tuple(row) for row in prior['authored']]
    LOG['timings_ms']['create_including_guards']=(time.perf_counter()-t)*1000
    LOG['sidecars_active']={n:os.stat(n,dir_fd=g.rootfd).st_size for n in ['LightflowCatalog.db-wal','LightflowCatalog.db-shm']}
    check('physical WAL SHM companion files',all(LOG['sidecars_active'][n]>0 for n in LOG['sidecars_active']))
    g.path('LightflowCatalog.db');c.execute('begin immediate')
    meta={'volume':{k:v[k] for k in ['MountPoint','VolumeName','VolumeUUID','DiskUUID','ParentWholeDisk','DeviceIdentifier','FilesystemType','BusProtocol']},'root':str(root),'root_identity':LOG['root_identity']}
    g.preflight('cross-process writer test')
    p=subprocess.run([sys.executable,str(__file__),'--child',json.dumps(meta)],capture_output=True,text=True,timeout=30)
    LOG['locking_child']={'exit':p.returncode,'stdout':p.stdout,'stderr':p.stderr}
    check('cross-process physical SQLite writer excluded',p.returncode==0)
    g.path('LightflowCatalog.db');c.rollback()
    g.path('LightflowCatalog.db');c.execute('begin immediate')
    g.path('LightflowCatalog.db');c.execute('update authored set note=? where assetid=?',('Mac physical authored delta',rows[0][0]))
    g.path('LightflowCatalog.db');c.commit()
    g.create('backup.db');b=connection(g,'backup.db');g.path('backup.db');g.path('LightflowCatalog.db');t=time.perf_counter();c.backup(b);g.path('backup.db');b.close();b=None
    LOG['timings_ms']['backup']=(time.perf_counter()-t)*1000
    g.path('LightflowCatalog.db');t=time.perf_counter();LOG['checkpoint']=c.execute('pragma wal_checkpoint(TRUNCATE)').fetchone()
    LOG['timings_ms']['checkpoint']=(time.perf_counter()-t)*1000
    check('physical checkpoint successful',LOG['checkpoint']==(0,0,0))
    g.path('LightflowCatalog.db');t=time.perf_counter();c.close();c=None;LOG['timings_ms']['close']=(time.perf_counter()-t)*1000
    LOG['sidecars_after_close']={s:os.stat('LightflowCatalog.db'+s,dir_fd=g.rootfd).st_size if (root/('LightflowCatalog.db'+s)).exists() else None for s in ['-wal','-shm']}
    check('closed physical WAL checkpointed',LOG['sidecars_after_close']['-wal'] in (None,0))
    t=time.perf_counter();expected=snapshot(g,'LightflowCatalog.db');LOG['timings_ms']['reopen_integrity_including_guards']=(time.perf_counter()-t)*1000
    check('physical integrity and identities',expected['integrity']=='ok' and expected['identity']==[ids] and len(expected['authored'])==32)
    expected_rows=[tuple([*row[:4],('Mac physical authored delta' if row[0]==rows[0][0] else row[4]),*row[5:]]) for row in rows]
    check('all authored fields and IDs preserved',expected['authored']==sorted(expected_rows))
    check('SQLite-aware backup preserves every fixture row',snapshot(g,'backup.db')==expected)
    g.create('restored.db',g.read('backup.db'));check('restored fixture integrity and state',snapshot(g,'restored.db')==expected)
    data=g.read('LightflowCatalog.db');sourcehash=digest(data)
    t=time.perf_counter();g.create('closed-copy.partial',data);check('closed staging hash verified',digest(g.read('closed-copy.partial'))==sourcehash)
    try:
        g.publish('closed-copy.partial','closed-copy.db');LOG['closed_copy_publication']='RENAME_EXCL succeeded'
    except OSError as e:
        if e.errno not in (errno.ENOTSUP,errno.EINVAL,errno.ENOSYS):raise
        LOG['closed_copy_publication']={'exclusive_rename_error':str(e),'errno':e.errno,'fallback':'new O_EXCL closed copy; no atomic publication claim'}
        g.create('closed-copy.db',data)
    LOG['timings_ms']['closed_copy_verify_publish_including_guards']=(time.perf_counter()-t)*1000
    check('published copy hash verified',digest(g.read('closed-copy.db'))==sourcehash)
    check('published closed copy state',snapshot(g,'closed-copy.db')==expected)
    g.create('interrupted-copy.partial',data[:1024]);check('truncated staging rejected',digest(g.read('interrupted-copy.partial'))!=sourcehash)
    g.create('mismatch-copy.partial',b'intentionally mismatched synthetic staging')
    check('mismatched staging rejected',digest(g.read('mismatch-copy.partial'))!=sourcehash)
    check('rollback retains source and prior good destination',digest(g.read('LightflowCatalog.db'))==sourcehash and digest(g.read('closed-copy.db'))==sourcehash)
    check('failed staging never activated',snapshot(g,'closed-copy.db')==expected)
    # Supported flush APIs operate only on one new task-owned canary, not a user file.
    g.create('flush-canary.bin',b'LF007 physical flush API observation\n')
    g.path('flush-canary.bin');fd=os.open('flush-canary.bin',os.O_RDWR|os.O_NOFOLLOW,dir_fd=g.rootfd)
    try:
        g.path('flush-canary.bin');t=time.perf_counter();os.fsync(fd);LOG['flush']={'fsync':'returned success','fsync_ms':(time.perf_counter()-t)*1000}
        g.path('flush-canary.bin');t=time.perf_counter()
        try:fcntl.fcntl(fd,51);LOG['flush']['F_FULLFSYNC']='returned success'
        except OSError as e:LOG['flush']['F_FULLFSYNC']={'errno':e.errno,'error':str(e)}
        LOG['flush']['F_FULLFSYNC_ms']=(time.perf_counter()-t)*1000
    finally:os.close(fd)
    LOG['snapshot']=expected;LOG['hashes']={n:digest(g.read(n)) for n in os.listdir(g.rootfd)}
    g.preflight('final retained fixture verification');LOG['footprint_before_manifest']=g.measure();LOG['peak_allocated_observed']=g.peak
    check('native descriptor count remains zero',LIB.lf_storage_active_descriptors()==0)
    check('bounded physical footprint',max(g.measure().values())<=LIMIT)
    LOG['passed']=True
    # Manifest excludes its own hash; no circular integrity claim. Every fixture/staging hash is retained.
    public={k:val for k,val in LOG.items() if k not in ['volume','physical_disk','root','operations','python']}
    public['task_directory']=root.name;public['authorization']='owner-approved physical Mac fixture, <=256 MiB; no eject or Windows'
    g.create('manifest.json',json.dumps(public,indent=2).encode())
    LOG['final_footprint']=g.measure();LOG['peak_allocated_observed']=g.peak
    g.preflight('post-manifest verification');check('manifest retained',bool(g.read('manifest.json')))
    check('final hashes stable',all(digest(g.read(n))==h for n,h in LOG['hashes'].items()))
except BaseException as e:
    LOG['passed']=False;LOG['errors'].append(repr(e));raise
finally:
    if c is not None:c.close()
    if b is not None:b.close()
    if g is not None:g.close()
    LOG['native_descriptors_after']=LIB.lf_storage_active_descriptors()
    LOG['task_directory_handles_after']=0
    local.write_text(json.dumps(LOG,indent=2));print(local)
