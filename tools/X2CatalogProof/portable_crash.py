"""Kill only the PID spawned here; retained Catalog and WAL are never removed."""
import argparse,json,os,pathlib,subprocess,time
P=pathlib.Path;root=P(__file__).resolve().parents[2]
a=argparse.ArgumentParser();a.add_argument('--local',type=P,required=True);a.add_argument('--catalog',type=P,required=True);args=a.parse_args()
local=args.local.resolve();catalog=args.catalog.resolve()
# The runner itself only accepts existing task-workspace paths. External aliases are
# supplied lexically by the explicit operator; filesystem scope must be checked first.
assert args.local.is_dir() and args.catalog.is_dir()
signal=local/'crash-ready.json';assert not signal.exists()
exe=root/'work/toolchain/dotnet/dotnet';dll=root/'tools/X2CatalogProof/bin/Release/net8.0/X2CatalogProof.dll'
command=[str(exe),str(dll),'portable-crash-child',str(args.local),str(args.catalog),str(signal)]
with (local/'crash-child.log').open('w') as log:
 child=subprocess.Popen(command,cwd=root,stdout=log,stderr=log)
 try:
  deadline=time.monotonic()+20
  while not signal.exists():
   assert child.poll() is None,'child exited before readiness'
   assert time.monotonic()<deadline,'child readiness timeout'
   time.sleep(.05)
  ready=json.loads(signal.read_text());assert ready['pid']==child.pid
  wal=catalog/'LightflowCatalog.db-wal';walbytes=wal.stat().st_size;assert walbytes>0
  child.kill();child.wait(timeout=10)
  subprocess.run([str(exe),str(dll),'portable-crash-recover',str(args.local),str(args.catalog),str(local/'crash-recovery.json')],cwd=root,check=True)
  (local/'CRASH.json').write_text(json.dumps({'termination':'SIGKILL of spawned process only','walPresentBeforeKill':True,'walBytesBeforeKill':walbytes,'exitCode':child.returncode,'recovery':json.loads((local/'crash-recovery.json').read_text()),'scope':'process failure; not OS power loss or physical removal'},indent=2)+'\n')
 finally:
  if child.poll() is None:child.kill();child.wait(timeout=10)
