"""Bounded independent afplay probe; no global audio settings."""
import json, math, pathlib, struct, subprocess, time, wave
root=pathlib.Path(__file__).resolve().parents[3]
work=root/'work/native-output-rca'; work.mkdir(parents=True,exist_ok=True)
p=work/'tone.wav'
with wave.open(str(p),'wb') as w:
 w.setparams((2,2,48000,0,'NONE','not compressed'))
 w.writeframes(b''.join(struct.pack('<hh',*(2*[int(32767*.08*math.sin(2*math.pi*440*i/48000))])) for i in range(96000)))
t=time.monotonic()
try:
 r=subprocess.run(['/usr/bin/afplay',str(p)],capture_output=True,text=True,timeout=25)
 result={'tool':'/usr/bin/afplay','returncode':r.returncode,'stdout':r.stdout,'stderr':r.stderr,'timeout':False}
except subprocess.TimeoutExpired as e:
 result={'tool':'/usr/bin/afplay','timeout':True,'stdout':str(e.stdout),'stderr':str(e.stderr)}
result.update(elapsed_seconds=time.monotonic()-t,format='48k stereo signed int16 LE WAV',tone_hz=440,amplitude=.08,duration_seconds=2,device='unchanged system default',owner_audible_confirmation='not obtained')
print(json.dumps(result,indent=2))
(root/'docs/research/mac-compatibility/x1-player-audio-completion/native-output-rca/ordinary.json').write_text(json.dumps(result,indent=2)+'\n')
