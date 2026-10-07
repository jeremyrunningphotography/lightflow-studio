"""Task-only derivative; never edits any accepted proof source."""
from pathlib import Path
import subprocess
r=Path(__file__).resolve().parents[2]
ns={'__file__':str(r/'tools/X1PlayerCompletion/build.py')}
base=(r/'tools/X1PlayerCompletion/build.py').read_text().split("o=r/'work/completion'")[0]
exec(base,ns)
s=ns['s']
lo=s.index('class Audio {');hi=s.index('static void semantic(',lo)
s=s[:lo]+(r/'tools/X1AudioCompletion/audio.hpp').read_text()+'\n'+s[hi:]
s=s[:s.index('// Bounded derivative:')]+(r/'tools/X1AudioCompletion/main.mm').read_text()
out=r/'work/audio';out.mkdir(parents=True,exist_ok=True)
(out/'generated.mm').write_text(s)
p=r/'work/deps/lgpl-ffmpeg'
subprocess.run(['clang++','-std=c++17','-O2','-fobjc-arc',str(out/'generated.mm'),'-I'+str(p/'include'),'-L'+str(p/'lib'),'-lavformat','-lavcodec','-lavutil','-lavfilter','-lswscale','-framework','Metal','-framework','AppKit','-framework','QuartzCore','-framework','CoreVideo','-framework','IOSurface','-framework','AudioToolbox','-o',str(out/'audio-proof')],check=True)
