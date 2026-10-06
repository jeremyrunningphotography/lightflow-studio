"""Mac-only launcher: reject a missing/misclassified mount before creating test data."""
import argparse,json,pathlib,subprocess,uuid
P=pathlib.Path;root=P(__file__).resolve().parents[2]
a=argparse.ArgumentParser();a.add_argument('--catalog-parent',type=P,required=True);a.add_argument('--backup-parent',type=P,required=True);a.add_argument('--expected-filesystem',choices=['apfs','exfat'],required=True);a.add_argument('--source',type=P,required=True);args=a.parse_args()
parent=args.catalog_parent.absolute();backup=args.backup_parent.absolute()
assert parent.is_dir() and backup.is_dir(),'existing authorized parents required'
probe=root/'work/volume_probe'
subprocess.run(['cc','-Wall','-Wextra',str(root/'tools/X2CatalogProof/volume_probe.c'),'-o',str(probe)],check=True)
volume=json.loads(subprocess.check_output([str(probe),str(parent)],text=True,timeout=10))
if volume['filesystem']!=args.expected_filesystem or not volume['local'] or volume['readOnly']:
 raise SystemExit('Refusing test: actual volume does not match the requested writable local filesystem: '+json.dumps(volume))
ident=uuid.uuid4().hex[:12];local=root/'work/data'/('local-portability-'+ident);catalog=parent/('catalog-'+ident)
assert not local.exists() and not catalog.exists()
subprocess.run(['python3',str(root/'tools/X2CatalogProof/run.py'),str(root/'tools/X2CatalogProof/bin/Release/net8.0/X2CatalogProof.dll'),'local-portability',str(local),str(catalog),str(args.source.absolute()),str(backup)],cwd=root,check=True)
(local/'VOLUME.json').write_text(json.dumps(volume,indent=2)+'\n')
print('Completed task-local output: '+str(local))
