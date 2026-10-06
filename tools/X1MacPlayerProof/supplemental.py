import hashlib,json,pathlib,re,subprocess
root=pathlib.Path(__file__).resolve().parents[2];out=root/'docs/research/mac-compatibility/x1-player'
def run(cmd):return subprocess.check_output(cmd,text=True)
config=(root/'work/deps/ffmpeg-source/config.h').read_text()
libraries=[]
for p in (root/'work/deps/lgpl-ffmpeg/lib').glob('*.dylib'):
    if p.is_symlink():continue
    libraries.append({'file':p.name,'sha256':hashlib.sha256(p.read_bytes()).hexdigest(),'linkage':run(['otool','-L',str(p)])})
build={'version':'n9.0.1','source':'https://github.com/FFmpeg/FFmpeg/tree/bf1b838f2ab88b4f8fd83443325c782ea0e0f7fa','source_commit':'bf1b838f2ab88b4f8fd83443325c782ea0e0f7fa',
 'configuration':next(line for line in config.splitlines() if line.startswith('#define FFMPEG_CONFIGURATION')),
 'license':next(line for line in config.splitlines() if line.startswith('#define FFMPEG_LICENSE')),
 'license_flags':[line for line in config.splitlines() if re.match(r'#define CONFIG_(GPL|VERSION3|NONFREE|GPLV3|LGPLV3) ',line)],
 'source_changes':run(['git','-C',str(root/'work/deps/ffmpeg-source'),'diff']),
 'libraries':libraries,'probe_linkage':run(['otool','-L',str(root/'work/decode_probe_lgpl')]),
 'limits':'Minimal decode capability configuration, not a full production format matrix or signed/notarized distribution. No external libraries, GPL, nonfree, or version3 enabled. Dynamic linking and corresponding-source/license/EULA compliance still required.'}
(out/'lgpl-build-provenance.json').write_text(json.dumps(build,indent=2))
video=root/'work/data/audio-sync.mp4';pcm=root/'work/data/audio-sync.f32'
native=root/'work/data/audio-native.f32'
audio={'file':'work/data/audio-sync.mp4','sha256':hashlib.sha256(video.read_bytes()).hexdigest(),
 'generation_command':['ffmpeg','-v','error','-y','-f','lavfi','-i','testsrc2=size=320x180:rate=30:duration=40','-f','lavfi','-i','sine=frequency=1000:sample_rate=48000:duration=40','-c:v','libx264','-threads','1','-g','120','-bf','3','-c:a','aac','-shortest','work/data/audio-sync.mp4'],
 'pcm_command':['ffmpeg','-v','error','-y','-i','work/data/audio-sync.mp4','-vn','-ac','1','-ar','48000','-f','f32le','work/data/audio-sync.f32'],
 'pcm_sha256':hashlib.sha256(pcm.read_bytes()).hexdigest(),'pcm_sample_count':pcm.stat().st_size//4,'pcm_sample_rate':48000,
 'native_lgpl_pcm_sha256':hashlib.sha256(native.read_bytes()).hexdigest(),'native_lgpl_pcm_equals_cli_oracle':native.read_bytes()==pcm.read_bytes(),
 'streams':json.loads(run(['ffprobe','-v','error','-show_streams','-of','json',str(video)]))['streams']}
(out/'audio-sync-fixture.json').write_text(json.dumps(audio,indent=2))
print('Saved minimal LGPL build, library hashes/linkage and supplemental audio fixture provenance')
