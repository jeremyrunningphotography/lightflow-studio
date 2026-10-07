#!/usr/bin/env python3
import os,sys,subprocess
from pathlib import Path
root=Path(__file__).resolve().parents[2]
env=os.environ.copy()
for key,rel in {'DOTNET_CLI_HOME':'.cache/g3-completion/cli','NUGET_PACKAGES':'.cache/nuget','TMPDIR':'.cache/g3-completion/tmp'}.items():
 p=root/rel;p.mkdir(parents=True,exist_ok=True);env[key]=str(p)
env['AVALONIA_TELEMETRY_OPTOUT']='1'
env['DOTNET_CLI_TELEMETRY_OPTOUT']='1'
env['DOTNET_GENERATE_ASPNET_CERTIFICATE']='false'
if sys.argv[1]=='bridge':
 out=root/'work/g3-completion';out.mkdir(parents=True,exist_ok=True)
 cmd=['xcrun','clang++','-std=c++17','-fobjc-arc','-dynamiclib','-O2','-mmacosx-version-min=12.0',str(root/'tools/X3G3Completion/bridge.mm'),'-framework','QuartzCore','-framework','ImageIO','-framework','CoreGraphics','-framework','Foundation','-framework','AppKit','-framework','Metal','-framework','IOSurface','-o',str(out/'libx3bridge.dylib')]
else:
 env['DYLD_LIBRARY_PATH']=str(root/'work/g3-completion')
 cmd=[str(root/'work/toolchain/dotnet8/dotnet'),*sys.argv[1:]]
raise SystemExit(subprocess.call(cmd,cwd=root,env=env))
