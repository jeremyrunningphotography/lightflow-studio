import pathlib,subprocess
r=pathlib.Path(__file__).resolve().parents[2];p=r/'work/deps/lgpl-ffmpeg'
command=['clang++','-std=c++17','-O2','-fobjc-arc',str(r/'tools/X1MacPlayerG1b/integrated.mm'),'-I'+str(p/'include'),'-L'+str(p/'lib'),'-lavformat','-lavcodec','-lavutil','-lavfilter','-lswscale','-framework','Metal','-framework','AppKit','-framework','QuartzCore','-framework','CoreVideo','-framework','IOSurface','-framework','AudioToolbox','-o',str(r/'work/g1b/integrated')]
subprocess.run(command,check=True)
command[4]=str(r/'tools/X1MacPlayerG1b/supplemental.mm')
command[-1]=str(r/'work/g1b/supplemental')
subprocess.run(command,check=True)

command[4]=str(r/"tools/X1MacPlayerG1b/color_hardware.mm")
command[-1]=str(r/"work/g1b/color_hardware")
subprocess.run(command,check=True)
