"""SIGKILL only a subprocess created here; preserve its WAL, then reopen via production logic."""
import pathlib, os, subprocess, time, json
root=pathlib.Path(__file__).resolve().parents[2]
env=os.environ.copy()
for key, sub in {"DOTNET_CLI_HOME":"cli","NUGET_PACKAGES":"nuget","TMPDIR":"tmp","TMP":"tmp","TEMP":"tmp"}.items():
    env[key]=str(root/"work"/sub)
env.update(DOTNET_CLI_TELEMETRY_OPTOUT="1", DOTNET_NOLOGO="1", X2_WORKSPACE=str(root))
exe=root/"work/toolchain/dotnet/dotnet"
dll=root/"tools/X2CatalogProof/bin/Release/net8.0/X2CatalogProof.dll"
ready=root/"work/evidence/crash-ready.json"
if ready.exists(): raise RuntimeError("Use a fresh crash signal path; evidence already exists")
with (root/"work/evidence/crash-child.log").open("w") as log:
    p=subprocess.Popen([str(exe),str(dll),"crash-child",str(root/"work/data/local-control-02"),str(ready)],cwd=root,env=env,stdout=log,stderr=log)
    try:
        deadline=time.monotonic()+20
        while not ready.exists() and p.poll() is None and time.monotonic()<deadline: time.sleep(0.05)
        if not ready.exists(): raise RuntimeError("Crash child did not reach transaction boundary")
        claim=json.loads(ready.read_text())
        assert claim["pid"]==p.pid
        p.kill(); code=p.wait(timeout=10)
        state={"ownedPid":p.pid,"exitCode":code,"signal":"SIGKILL","ready":claim,"walExistsAfterKill":(root/"work/data/local-control-02/Catalog/LightflowCatalog.db-wal").exists()}
        (root/"work/evidence/crash-process.json").write_text(json.dumps(state,indent=2))
    finally:
        if p.poll() is None: p.kill(); p.wait(timeout=10)
with (root/"work/evidence/crash-recover.log").open("w") as log:
    subprocess.run([str(exe),str(dll),"crash-recover",str(root/"work/data/local-control-02"),str(root/"work/evidence/crash-recover.json")],cwd=root,env=env,stdout=log,stderr=log,check=True,timeout=20)
print("Owned SIGKILL/reopen complete; raw evidence saved")
