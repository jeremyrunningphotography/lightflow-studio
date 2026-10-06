"""Build a pinned minimal LGPL FFmpeg entirely inside this task."""
import pathlib,subprocess
root=pathlib.Path(__file__).resolve().parents[2];source=root/'work/deps/ffmpeg-source';prefix=root/'work/deps/lgpl-ffmpeg'
sha=subprocess.check_output(['git','-C',str(source),'rev-parse','HEAD'],text=True).strip()
assert sha=='bf1b838f2ab88b4f8fd83443325c782ea0e0f7fa',sha
assert not subprocess.check_output(['git','-C',str(source),'diff'])
args=['./configure','--prefix='+str(prefix),'--disable-everything','--disable-autodetect','--enable-shared','--disable-static','--disable-programs','--disable-doc','--enable-avfilter','--enable-filter=atempo,abuffer,abuffersink,aformat,aresample','--enable-avcodec','--enable-avformat','--enable-swscale','--enable-swresample','--enable-decoder=h264,hevc,aac,pcm_s16le','--enable-parser=h264,hevc,aac','--enable-demuxer=mov,matroska','--enable-protocol=file','--enable-hwaccel=h264_videotoolbox,hevc_videotoolbox','--enable-videotoolbox','--enable-pthreads']
subprocess.run(args,cwd=source,check=True)
config=(source/'config.h').read_text()
for name in ('GPL','NONFREE','VERSION3'):assert f'#define CONFIG_{name} 0' in config
subprocess.run(['make','-j8'],cwd=source,check=True)
subprocess.run(['make','install'],cwd=source,check=True)
