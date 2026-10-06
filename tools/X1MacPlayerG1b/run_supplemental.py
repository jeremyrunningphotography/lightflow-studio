"""Serial native supplemental matrix; expected injected failures are explicit."""
import hashlib,json,pathlib,subprocess,time
root=pathlib.Path(__file__).resolve().parents[2]
out=root/'docs/research/mac-compatibility/x1-player-g1b'
records=[]
for name,args,expected in [
 ('leases',['leases','work/data/color.mp4'],0),
 ('surface-pressure',['pressure','work/data/color.mp4'],13),
 ('shown-lifecycle',['shown-lifecycle','work/data/color.mp4'],0),
 ('audio-fault',['audio-fault','work/data/audio-native.f32'],0),
 ('decode-failure',['leases','work/g1b/does-not-exist.mp4'],2),
 ('1080-hw-sustained',['sustained','work/data/perf-1920x1080.mp4','work/data/audio-native.f32','60'],0),
 ('4k-hw-sustained',['sustained','work/data/perf-3840x2160.mp4','work/data/audio-native.f32','120'],0),
]:
 start=time.monotonic()
 with (out/(name+'.jsonl')).open('w') as stdout,(out/(name+'.log')).open('w') as stderr:
  p=subprocess.run(['/usr/bin/time','-l',str(root/'work/g1b/supplemental')]+args,cwd=root,stdout=stdout,stderr=stderr,timeout=180)
 row={'name':name,'args':args,'returncode':p.returncode,'expected_returncode':expected,'seconds':time.monotonic()-start}
 records.append(row);print(row,flush=True)
 (out/'supplemental-process-results.json').write_text(json.dumps(records,indent=2))
 if p.returncode!=expected:raise SystemExit('Unexpected failure '+name)
