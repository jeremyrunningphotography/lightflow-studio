#!/usr/bin/env python3
"""Task-owned native process qualification; read-only SQL compares logical state only."""
import argparse, json, os, pathlib, signal, sqlite3, subprocess, time, hashlib
p=argparse.ArgumentParser();p.add_argument('--dotnet',required=True);p.add_argument('--host',required=True);p.add_argument('--data-root',required=True);a=p.parse_args()
root=pathlib.Path(a.data_root).resolve()
if root.exists():raise SystemExit('Qualification root must be new; existing evidence is retained.')
(root/'Catalog').mkdir(parents=True)
log=root/'qualification.jsonl'
base=[a.dotnet,a.host,'--data-root',str(root)]
results=[]
def alarm_timeout(*_):raise TimeoutError("Readiness handshake exceeded 30 seconds.")
signal.signal(signal.SIGALRM,alarm_timeout)
def record(name,passed,**facts):
    row=dict(name=name,passed=passed,**facts);results.append(row)
    with log.open('a') as f:f.write(json.dumps(row)+'\n')
    if not passed:raise AssertionError(name)
def run(command,expected=0):
    t=time.perf_counter();r=subprocess.run(base+command,text=True,capture_output=True,timeout=30)
    with log.open('a') as f:f.write(json.dumps(dict(command=command,exit=r.returncode,stdout=r.stdout,stderr=r.stderr,elapsedMs=(time.perf_counter()-t)*1000))+'\n')
    record('command '+' '.join(command),r.returncode==expected,exit=r.returncode)
    return r
path=root/'Catalog'/'LightflowCatalog.db'
def logical():
    assert not any(pathlib.Path(str(path)+suffix).exists() for suffix in ('-wal','-shm','-journal')), 'Comparison requires fully closed standalone Catalog bytes.'
    with sqlite3.connect(f'file:{path}?mode=ro&immutable=1',uri=True) as c:
        assert c.execute('pragma integrity_check').fetchall()==[('ok',)]
        tables=[r[0] for r in c.execute("select name from sqlite_schema where type='table' and name not like 'sqlite_%' order by name")]
        return {n:sorted(c.execute('select * from "'+n.replace('"','""')+'"').fetchall(),key=repr) for n in tables}
run(['create']);before=logical();run(['collection-add','LF015 owner acceptance']);after=logical()
record('only authored Collection changed',all(before[n]==after[n] for n in before if n!='Collections'),tables=len(before),nonemptyTables=[n for n in before if before[n]])
record('one Collection persisted',len(after['Collections'])==len(before['Collections'])+1)
for i in range(3):run(['inspect']);record('logical reopening '+str(i),logical()==after)
record('unpaired profile remains Dirty',(root/'startup-session.state').read_text()=='Dirty')
record('Catalog retains Validated provenance','Validated' in (root/'Catalog'/'LightflowCatalog.db.startup-state').read_text())
record('no fabricated Preview',not (root/'Previews'/'previews.db').exists())
held=subprocess.Popen(base+['hold'],stdin=subprocess.PIPE,stdout=subprocess.PIPE,stderr=subprocess.PIPE,text=True)
try:
    # Readiness handshake, no timing sleep/retry. Bound read by subprocess communication only after ready.
    signal.alarm(30)
    while True:
        line=held.stdout.readline()
        if not line:raise AssertionError('held host exited before READY')
        if line.startswith('READY:'):break
    signal.alarm(0)
    run(['lease-probe'],2) # Isolate Catalog lease refusal from the separate profile lease.
    run(['inspect'],2)
    held.stdin.write('\n');held.stdin.flush();out,err=held.communicate(timeout=30)
    record('independent host released ownership',held.returncode==0,stdout=out,stderr=err)
finally:
    if held.poll() is None:held.kill();held.communicate(timeout=30)
run(['inspect']);record('writer refusal leaves authored state intact',logical()==after)
# Task-owned host interruption, not a hardware or power-loss test.
held=subprocess.Popen(base+['hold'],stdin=subprocess.PIPE,stdout=subprocess.PIPE,stderr=subprocess.PIPE,text=True)
try:
    signal.alarm(30)
    while True:
        line=held.stdout.readline()
        if not line:raise AssertionError('interruption host exited before READY')
        if line.startswith('READY:'):break
    signal.alarm(0)
    held.kill();held.communicate(timeout=30)
finally:
    if held.poll() is None:held.kill();held.communicate(timeout=30)
run(['inspect']);record('interrupted unpaired reopen preserves logical state',logical()==after)
run(['backup'],2)
run(['inspect']);record('refused unqualified closed-backup operation retains authored state',logical()==after)
summary=dict(agent='LF-MAC-DEV-015',results=results,limitations=['Small current-production-schema fixture with a nonempty Collection; remaining authored tables were empty, not full authored-field coverage.','No Windows return, physical fault, SMB, external media or publication qualification.'])
(root/'summary.json').write_text(json.dumps(summary,indent=2));print(json.dumps(dict(passed=True,checks=len(results),root=str(root))))
