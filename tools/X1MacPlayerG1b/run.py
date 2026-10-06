import pathlib,subprocess,json,time
r=pathlib.Path(__file__).resolve().parents[2];out=r/'docs/research/mac-compatibility/x1-player-g1b';cases=[]
for name,args in [
 ('semantic',['semantic','work/data/rotation.mp4']),
 ('reverse-cfr',['reverse','work/g1b/long-cfr.mp4']),
 ('reverse-vfr',['reverse','work/g1b/long-vfr.mp4']),
 ('audio-1',['play','work/data/audio-sync.mp4','work/data/audio-native.f32','30','1','1','1','0']),
 ('audio-half',['play','work/data/audio-sync.mp4','work/data/audio-native.f32','15','.5','1','1','0']),
 ('audio-double',['play','work/data/audio-sync.mp4','work/data/audio-native.f32','15','2','1','1','0']),
 ('1080-hw-off',['play','work/data/perf-1920x1080.mp4','work/data/audio-native.f32','20','1','1','0','0']),
 ('1080-hw-color',['play','work/data/perf-1920x1080.mp4','work/data/audio-native.f32','20','1','1','1','0']),
 ('4k-hw-off',['play','work/data/perf-3840x2160.mp4','work/data/audio-native.f32','30','1','1','0','0']),
 ('4k-hw-color',['play','work/data/perf-3840x2160.mp4','work/data/audio-native.f32','30','1','1','1','0']),
 ('1080-sw',['play','work/data/perf-1920x1080.mp4','work/data/audio-native.f32','15','1','0','1','0']),
 ('4k-sw',['play','work/data/perf-3840x2160.mp4','work/data/audio-native.f32','15','1','0','1','0']),
 ('4k-cpu-copy',['play','work/data/perf-3840x2160.mp4','work/data/audio-native.f32','15','1','1','1','1']),
 ('lifecycle',['lifecycle','work/data/color.mp4'])]:
 start=time.monotonic()
 with (out/(name+'.jsonl')).open('w') as stdout,(out/(name+'.log')).open('w') as stderr:
  p=subprocess.run(['/usr/bin/time','-l',str(r/'work/g1b/integrated')]+args,cwd=r,stdout=stdout,stderr=stderr,timeout=180)
 case={'name':name,'args':args,'returncode':p.returncode,'seconds':time.monotonic()-start};cases.append(case);print(case,flush=True)
 (out/'process-results.json').write_text(json.dumps(cases,indent=2))
 if p.returncode:raise SystemExit('Failed case '+name)
