import pathlib,subprocess
root=pathlib.Path(__file__).resolve().parents[2];tools=root/'tools/X1MacPlayerProof';deps=root/'work/deps'
def build(args):subprocess.run(args,cwd=root,check=True)
build(['clang','-O2',str(tools/'mpv_probe.c'),'-I'+str(deps/'mpv/include'),str(deps/'bottles/opt/mpv/lib/libmpv.2.dylib'),'-Wl,-rpath,'+str(deps/'lib'),'-o',str(root/'work/mpv_probe')])
build(['clang','-O2','-x','objective-c',str(tools/'decode_probe.c'),'-I'+str(deps/'lgpl-ffmpeg/include'),'-L'+str(deps/'lgpl-ffmpeg/lib'),'-lavformat','-lavcodec','-lavutil','-lswscale','-framework','Metal','-framework','CoreVideo','-framework','Foundation','-o',str(root/'work/decode_probe_lgpl')])
build(['clang','-O2','-fobjc-arc',str(tools/'metal_probe.m'),'-framework','Metal','-framework','Foundation','-framework','AppKit','-framework','QuartzCore','-o',str(root/'work/metal_probe')])
build(['clang',str(tools/'coreaudio_probe.c'),'-framework','AudioToolbox','-framework','CoreFoundation','-o',str(root/'work/coreaudio_probe')])
build(['clang',str(tools/'codec_probe.c'),'-framework','VideoToolbox','-framework','CoreMedia','-o',str(root/'work/codec_probe')])
build(['clang','-O2',str(tools/'audio_decode_probe.c'),'-I'+str(deps/'lgpl-ffmpeg/include'),'-L'+str(deps/'lgpl-ffmpeg/lib'),'-lavformat','-lavcodec','-lavutil','-lswresample','-o',str(root/'work/audio_decode_probe')])
