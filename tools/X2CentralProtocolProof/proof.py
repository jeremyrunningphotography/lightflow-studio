"""Bounded nonproduction protocol proof. No service, no production imports or UI.
Only creates new UUID directories beneath explicitly supplied local and NAS parents.
Python SQLite is a disclosed transport/WAL control, not the production provider.
"""
import argparse, ctypes, errno, fcntl, hashlib, json, os, pathlib, platform
import re, signal, sqlite3, subprocess, sys, time, uuid
P = pathlib.Path

def write(p, data):
    with open(p, 'xb') as f:
        f.write(data); f.flush(); os.fsync(f.fileno())

def digest(p): return hashlib.sha256(P(p).read_bytes()).hexdigest()
def dump(p, obj): P(p).write_text(json.dumps(obj, indent=2, sort_keys=True)+'\n')
def timer(fn):
    t=time.perf_counter(); r=fn(); return r, round((time.perf_counter()-t)*1000,3)

def child(mode, path):
    p=P(path)
    if mode in ['lock','flock']:
        with open(p,'r+b') as f:
            try:
                if mode=='lock':fcntl.lockf(f,fcntl.LOCK_EX|fcntl.LOCK_NB,1)
                else:fcntl.flock(f,fcntl.LOCK_EX|fcntl.LOCK_NB)
            except OSError as e: print(json.dumps({'acquired':False,'errno':e.errno}),flush=True); return
            print(json.dumps({'acquired':True}),flush=True);sys.stdin.readline()
    elif mode=='create':
        try: write(p,b'owner'); print('won')
        except FileExistsError: print('exists')
    elif mode=='mkdir':
        try: p.mkdir();print('won')
        except FileExistsError:print('exists')
    elif mode=='crash':
        c=sqlite3.connect(p);c.execute('pragma journal_mode=wal');c.execute('pragma synchronous=full')
        c.execute('create table if not exists edits(id integer primary key, note text)');c.commit()
        c.execute("insert into edits values(1,'committed unpublished')");c.commit()
        c.execute("insert into edits values(2,'uncommitted')")
        print('ready',flush=True);sys.stdin.readline()

def spawn(mode,p): return subprocess.Popen([sys.executable,__file__,'--child',mode,str(p)],stdin=subprocess.PIPE,stdout=subprocess.PIPE,text=True)
def once(mode,p): return subprocess.check_output([sys.executable,__file__,'--child',mode,str(p)],text=True,timeout=15).strip()

class Oracle:
    """SIMULATED atomic authority contract; deliberately no filesystem/service adapter.
    Each method is an indivisible model transition. Cannot prove SMB fencing.
    """
    def __init__(self):
        self.catalog='synthetic-catalog';self.seq=0;self.current='g0';self.previous=None
        self.owner=None;self.epoch=0;self.available=True;self.accepted={};self.dirty=False
    def acquire(self,who):
        if not self.available or self.owner is not None: raise ValueError('unavailable or owned')
        self.epoch+=1;self.owner=who;return (who,self.epoch)
    def valid(self,t): return self.available and t==(self.owner,self.epoch)
    def edit(self,t):
        if not self.valid(t):raise ValueError('protected')
        self.dirty=True
    def publish(self,t,base,ident,valid=True,fail=None):
        if not self.valid(t):raise ValueError('fenced')
        if ident in self.accepted:
            oldbase,result=self.accepted[ident]
            if base!=oldbase:raise ValueError('id reuse')
            return result
        if base!=(self.seq,self.current):raise ValueError('wrong base')
        if not valid or fail in ['upload','verify','before_promote']:raise ValueError('candidate rejected')
        self.previous=self.current;self.seq+=1;self.current=ident
        self.accepted[ident]=(base,(self.seq,ident));self.dirty=False
        if fail=='ack':raise ValueError('ack lost')
        return (self.seq,ident)
    def takeover(self,old,confirmed=False,fenced=False):
        if not self.available or old!=(self.seq,self.current) or not confirmed or not fenced:raise ValueError('unsafe takeover')
        self.epoch+=1;self.owner='B';return ('B',self.epoch)

def model():
    out=[]
    def case(name,fn):
        fn();out.append({'scenario':name,'classification':'simulated','passed':True,'boundary':'atomic authority oracle, not SMB'})
    def rejects(fn):
        try:fn()
        except ValueError:return
        raise AssertionError('expected rejection')
    a=Oracle();t=a.acquire('A');base=(0,'g0')
    case('second owner rejected',lambda:rejects(lambda:a.acquire('B')))
    case('stale heartbeat',lambda:rejects(lambda:a.takeover(base,confirmed=True)))
    case('wrong base generation',lambda:rejects(lambda:a.publish(t,(-1,'g0'),'bad')))
    for fault in ['upload','verify','before_promote']:
        case({'upload':'candidate upload interrupted','verify':'verification failure','before_promote':'promotion failure'}[fault],lambda f=fault:rejects(lambda:a.publish(t,base,'bad',fail=f)))
        assert a.current=='g0' and a.previous is None
    case('candidate corrupt',lambda:rejects(lambda:a.publish(t,base,'bad',valid=False)))
    a.publish(t,base,'g1');assert a.previous=='g0'
    case('duplicate publication',lambda:assert_equal(a.publish(t,base,'g1'),(1,'g1')))
    case('publication retry',lambda:assert_equal(a.publish(t,base,'g1'),(1,'g1')))
    a.available=False
    case('NAS unavailable',lambda:rejects(lambda:a.edit(t)))
    a.available=True
    case('NAS returns unchanged',lambda:a.edit(t))
    case('owner crash',lambda:rejects(lambda:a.acquire('B')))
    t2=a.takeover((1,'g1'),confirmed=True,fenced=True)
    out.append({'scenario':'explicit takeover','classification':'simulated','passed':True,'boundary':'fenced=True is an unimplemented prerequisite, not a discovered SMB primitive'})
    a.publish(t2,(1,'g1'),'g2')
    case('stale owner returns',lambda:rejects(lambda:a.edit(t)))
    case('stale publication rejected',lambda:rejects(lambda:a.publish(t,(1,'g1'),'stale')))
    case('NAS returns changed',lambda:rejects(lambda:a.publish(t2,(1,'g1'),'stale-base')))
    case('lost acknowledgement',lambda:rejects(lambda:a.publish(t2,(2,'g2'),'g3',fail='ack')))
    case('acknowledgement reconciliation',lambda:assert_equal(a.publish(t2,(2,'g2'),'g3'),(3,'g3')))
    assert a.previous=='g2';a.owner=None
    case('clean release',lambda:a.acquire('C'))
    return out

def assert_equal(a,b): assert a==b,(a,b)

def run(local_parent,nas_parent,out):
    if platform.system()!='Darwin':raise RuntimeError('actual primitive slice requires macOS')
    mount=subprocess.check_output(['mount'],text=True)
    smb_mounts=[P(m).resolve() for m in re.findall(r' on (.+?) \(smbfs,',mount)]
    parent=nas_parent.resolve()
    if not parent.is_dir() or not any(m in parent.parents for m in smb_mounts):raise RuntimeError('explicit test parent must be below an existing mounted SMB share')
    ident=uuid.uuid4().hex;local=local_parent/('protocol-'+ident);nas=nas_parent/('protocol-'+ident)
    local.mkdir(parents=True,exist_ok=False);nas.mkdir(exist_ok=False);write(nas/'OWNED',ident.encode());out.mkdir(parents=True,exist_ok=True)
    # Private resumption map is never emitted to public result artifacts.
    dump(local_parent/'latest-protocol-private.json',{'local':str(local),'nas':str(nas),'id':ident})
    obs=[]
    for mode in ['create','mkdir']:
        target=nas/('race-'+mode)
        procs=[spawn(mode,target) for _ in range(8)]
        try: results=[p.communicate(timeout=20)[0].strip() for p in procs]
        finally:
            for p in procs:
                if p.poll() is None:p.kill();p.wait()
        assert results.count('won')==1,results
        obs.append({'primitive':mode,'participants':8,'winners':1,'others_rejected':7,'classification':'proven','scope':'one Mac SMB client, separate processes; not cross-host/server trace'})
    lock=nas/'owner.lock';write(lock,b'A epoch 1 stale heartbeat')
    lock_mode='lock';owner=spawn(lock_mode,lock)
    first=json.loads(owner.stdout.readline())
    obs.append({'primitive':'fcntl byte-range lock','initial_result':first,'classification':'proven','scope':'observed macOS mounted-SMB API result'})
    if not first['acquired']:
        owner.wait(timeout=10);lock_mode='flock';owner=spawn(lock_mode,lock);first=json.loads(owner.stdout.readline())
        obs.append({'primitive':'flock','initial_result':first,'classification':'proven','scope':'observed macOS mounted-SMB API result'})
    try:
        assert first['acquired'],first
        second=json.loads(once(lock_mode,lock));assert not second['acquired']
        owner.kill();owner.wait(timeout=10)
        successor=spawn(lock_mode,lock)
        try:
            reacquired=json.loads(successor.stdout.readline());assert reacquired['acquired'];assert lock.exists()
            # Counterexample: stale client already validated epoch, loses lock, resumes independent rename.
            current=nas/'current.json';write(current,b'{"generation":2,"owner":"B"}')
            candidate=nas/'stale.json';write(candidate,b'{"generation":1,"owner":"A"}')
            os.replace(candidate,current)
            overwritten=json.loads(current.read_text())['generation']==1;assert overwritten
            obs.append({'primitive':'separate '+lock_mode+' + replace','second_process_rejected':True,'owner_killed':True,'metadata_survives':True,'successor_lock_acquired':True,'stale_replace_succeeded_while_successor_locked':True,'classification':'proven','scope':'real SMB rename; loss/return schedule simulated by SIGKILL and separate stale operation, not network partition'})
        finally:
            if successor.poll() is None:successor.communicate('\n',timeout=10)
    finally:
        if owner.poll() is None:owner.kill();owner.wait()
    # macOS no-replace rename observation; no claim of conditional owner-token semantics.
    lib=ctypes.CDLL(None,use_errno=True);rename=lib.renamex_np;rename.argtypes=[ctypes.c_char_p,ctypes.c_char_p,ctypes.c_uint];rename.restype=ctypes.c_int
    x=nas/'rename-source';y=nas/'rename-target';write(x,b'X');write(y,b'Y')
    rc=rename(os.fsencode(x),os.fsencode(y),4);err=ctypes.get_errno();assert y.read_bytes()==b'Y'
    obs.append({'primitive':'renamex_np RENAME_EXCL','return':rc,'errno':err,'target_preserved':True,'classification':'proven','scope':'existing target only; not ownership CAS or restart durability'})
    dump(out/'OWNERSHIP_RESULTS.json',obs)
    # Deterministic synthetic payload scaling. Deliberately not Lightflow schema.
    perf=[];snapshots=[]
    for count in [32,4096,16384]:
        db=local/f'working-{count}.db';c=sqlite3.connect(db)
        assert c.execute('pragma journal_mode=wal').fetchone()[0]=='wal';c.execute('pragma synchronous=full')
        c.execute('create table authored(asset_id text primary key, root_id text, notes text, revision integer)')
        c.executemany('insert into authored values(?,?,?,0)',((str(uuid.uuid5(uuid.NAMESPACE_URL,f'asset-{i}')),'root-1',(hashlib.sha256(str(i).encode()).hexdigest()*64)) for i in range(count)));c.commit()
        for trial in range(3):
            c.execute('update authored set revision=revision+1 where rowid=1');c.commit()
            _,checkpoint=timer(lambda:c.execute('pragma wal_checkpoint(full)').fetchall())
            snapshot=local/f'snapshot-{count}-{trial}.db'
            def backup():
                dest=sqlite3.connect(snapshot);c.backup(dest);assert dest.execute('pragma journal_mode=delete').fetchone()[0]=='delete';dest.close()
            _,snapshot_ms=timer(backup)
            def validate(p):
                q=sqlite3.connect(p.resolve().as_uri()+'?mode=ro',uri=True);assert q.execute('pragma integrity_check').fetchone()[0]=='ok';assert q.execute('select count(*) from authored').fetchone()[0]==count;q.close()
            _,integrity_ms=timer(lambda:validate(snapshot));h,hash_ms=timer(lambda:digest(snapshot))
            candidate=nas/f'candidate-{count}-{trial}.db';_,transfer_ms=timer(lambda:write(candidate,snapshot.read_bytes()))
            returned=local/f'returned-{count}-{trial}.db'
            _,verify_ms=timer(lambda:write(returned,candidate.read_bytes()));assert digest(returned)==h;validate(returned)
            # No candidate is promoted as authoritative: fencing not available.
            perf.append({'assets':count,'trial':trial,'bytes':snapshot.stat().st_size,'checkpoint_ms':checkpoint,'snapshot_ms':snapshot_ms,'integrity_ms':integrity_ms,'hash_ms':hash_ms,'upload_flush_ms':transfer_ms,'readback_ms':verify_ms,'sha256':h,'promotion_ms':None,'promotion_reason':'not performed: no qualified fenced primitive'})
            snapshots.append((candidate,h))
        c.close()
    good,expected=snapshots[0];previous,prevhash=snapshots[1]
    bad=nas/'incomplete-candidate';write(bad,good.read_bytes()[:1024]);assert digest(bad)!=expected
    corrupt=nas/'corrupt-candidate';data=bytearray(good.read_bytes());data[0]^=255;write(corrupt,data);assert digest(corrupt)!=expected
    assert digest(good)==expected and digest(previous)==prevhash
    # Missing manifest recovery: preserve payloads, block automatic choice; select previous explicitly in local simulation.
    assert not (nas/'committed-manifest.json').exists()
    recovered=local/'previous-recovery.db';write(recovered,previous.read_bytes());assert digest(recovered)==prevhash
    # Python SQLite local WAL process crash control.
    db=local/'crash.db';p=spawn('crash',db)
    try:
        assert p.stdout.readline().strip()=='ready';wal_present=P(str(db)+'-wal').exists();p.kill();p.wait(timeout=10)
        q=sqlite3.connect(db);rows=q.execute('select * from edits').fetchall();integrity=q.execute('pragma integrity_check').fetchone()[0];q.close();assert rows==[(1,'committed unpublished')] and integrity=='ok'
    finally:
        if p.poll() is None:p.kill();p.wait()
    faults=model()
    faults.extend([
        {'scenario':'local WAL recovery','classification':'proven','passed':True,'boundary':'local Python SQLite process SIGKILL; not production provider/OS power loss'},
        {'scenario':'current generation missing','classification':'simulated','passed':True,'boundary':'missing manifest blocks selection; no automatic rollback'},
        {'scenario':'previous generation recovery','classification':'proven','passed':True,'boundary':'closed payload readback/hash preserved; authority recovery transition unimplemented'},
        {'scenario':'physical NAS loss/reconnect','classification':'unresolved','passed':None,'boundary':'no disruptive experiment authorized/executed'},
        {'scenario':'server-enforced stale publication rejection','classification':'unresolved','passed':None,'boundary':'separate-lock design counterexample; no qualified replacement primitive'},
        {'scenario':'machine loss','classification':'reasoned only','passed':None,'boundary':'unpublished edits may be lost; central/previous survival depends on durability'}])
    dump(out/'OWNERSHIP_RESULTS.json',obs);dump(out/'PERFORMANCE.json',perf);dump(out/'FAULT_MATRIX.json',faults)
    dump(out/'PUBLICATION_RESULTS.json',{'partial_hash_rejected':True,'corrupt_hash_rejected':True,'current_payload_unchanged':True,'previous_payload_unchanged':True,'missing_manifest_blocks_automatic_open':True,'authority_promotion':'unresolved; intentionally not executed','recovery':'verified closed previous payload copied locally; no authority promotion','durable_server_power_loss':'unresolved'})
    dump(out/'PROVENANCE.json',{'platform':platform.system(),'machine':platform.machine(),'os_release':platform.release(),'python':platform.python_version(),'sqlite':sqlite3.sqlite_version,'source_main':subprocess.check_output(['git','rev-parse','main'],text=True).strip(),'storage':'mounted SMB 3.1.1 NAS/share; disposable uniquely owned directory','runtime_limit':'Python SQLite supplementary model/transport control, not pinned Lightflow provider','disruption':'none','local_wal_present_before_kill':wal_present,'crash_integrity':integrity,'retained_fixtures':True})
    print(json.dumps({'observations':len(obs),'fault_rows':len(faults),'performance_samples':len(perf),'disposition':'B2: pure SMB fencing unresolved; service fallback recommended'},indent=2))

if __name__=='__main__':
    if len(sys.argv)>1 and sys.argv[1]=='--child':child(sys.argv[2],sys.argv[3])
    else:
        a=argparse.ArgumentParser();a.add_argument('--local-parent',type=P,required=True);a.add_argument('--nas-parent',type=P,required=True);a.add_argument('--out',type=P,required=True);args=a.parse_args();run(args.local_parent,args.nas_parent,args.out)
