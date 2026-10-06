#!/usr/bin/env python3
import os,sys,subprocess
from pathlib import Path
root=Path(__file__).resolve().parents[2]
env=os.environ.copy()
for key,rel in {'DOTNET_CLI_HOME':'.cache/x3-iosurface/cli','NUGET_PACKAGES':'.cache/nuget','TMPDIR':'.cache/x3-iosurface/tmp'}.items():
 p=root/rel;p.mkdir(parents=True,exist_ok=True);env[key]=str(p)
env['DOTNET_CLI_TELEMETRY_OPTOUT']='1'
if sys.argv[1]=='bridge':
 out=root/'work/x3-iosurface';out.mkdir(parents=True,exist_ok=True)
 cmd=['xcrun','clang++','-std=c++17','-fobjc-arc','-dynamiclib','-O2','-mmacosx-version-min=12.0',str(root/'tools/X3AvaloniaIOSurface/bridge.mm'),'-framework','Foundation','-framework','AppKit','-framework','Metal','-framework','IOSurface','-o',str(out/'libx3bridge.dylib')]
else:
 env['DYLD_LIBRARY_PATH']=str(root/'work/x3-iosurface')
 cmd=[str(root/'work/toolchain/dotnet8/dotnet'),*sys.argv[1:]]
raise SystemExit(subprocess.call(cmd,cwd=root,env=env))
