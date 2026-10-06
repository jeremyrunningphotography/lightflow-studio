"""Record pinned source/build/input provenance before task dependencies are removed."""
import pathlib,hashlib,json,subprocess,re
r=pathlib.Path(__file__).resolve().parents[2];o=r/'docs/research/mac-compatibility/x1-player-g1b';src=r/'work/deps/ffmpeg-source';cfg=(src/'config.h').read_text()
run=lambda x:subprocess.check_output(x,text=True).strip()
p={'main':'7b7f1c2cac5426f7df145921d8890adde6f9c490','hardware_reference':'../x1-player/environment.json','same_execution_environment':True,'ffmpeg':{'tag':'n9.0.1','commit':run(['git','-C',str(src),'rev-parse','HEAD']),'source_changes':run(['git','-C',str(src),'diff']),'configuration':next(x for x in cfg.splitlines() if x.startswith('#define FFMPEG_CONFIGURATION')),'license':next(x for x in cfg.splitlines() if x.startswith('#define FFMPEG_LICENSE')),'license_flags':[x for x in cfg.splitlines() if re.match(r'#define CONFIG_(GPL|VERSION3|NONFREE|GPLV3|LGPLV3) ',x)],'libraries':[]},'avalonia':{'tag':'11.3.8','commit':run(['git','-C',str(r/'work/deps/avalonia'),'rev-parse','HEAD']),'license':'MIT','use':'source/API investigation only; no Avalonia runtime execution or production linkage'},'generator':run(['ffmpeg','-version']).splitlines()[0],'generator_distribution':'existing system CLI, GPL/libx264 fixture encoding only; not linked or redistributed with proof','proof':[],'inputs':[]}
for f in sorted((r/'work/deps/lgpl-ffmpeg/lib').glob('*.dylib')):
 if not f.is_symlink():p['ffmpeg']['libraries'].append({'file':f.name,'sha256':hashlib.sha256(f.read_bytes()).hexdigest(),'linkage':run(['otool','-L',str(f)])})
for n in ['integrated','supplemental','color_hardware']:
 f=r/'work/g1b'/n;p['proof'].append({'file':str(f.relative_to(r)),'sha256':hashlib.sha256(f.read_bytes()).hexdigest(),'linkage':run(['otool','-L',str(f)])})
for f in sorted((r/'tools/X1MacPlayerG1b').glob('*.mm')):p['proof'].append({'source':str(f.relative_to(r)),'sha256':hashlib.sha256(f.read_bytes()).hexdigest()})
for n in ['rotation.mp4','color.mp4','nonzero.mp4','audio-sync.mp4','audio-native.f32','perf-1920x1080.mp4','perf-3840x2160.mp4']:
 f=r/'work/data'/n;p['inputs'].append({'file':str(f.relative_to(r)),'sha256':hashlib.sha256(f.read_bytes()).hexdigest(),'bytes':f.stat().st_size})
(o/'DEPENDENCY_PROVENANCE.json').write_text(json.dumps(p,indent=2)+'\n')
print('Saved exact configurations, input/library/build hashes and linkage')
