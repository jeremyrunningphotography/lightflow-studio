"""Owner-only audible acceptance. Never treats listening as an automated pass."""
import argparse,math,os,pathlib,shutil,struct,subprocess,sys,wave
r=pathlib.Path(__file__).resolve().parents[3];work=r/'work/owner-listening'
a=argparse.ArgumentParser();a.add_argument('--data-root',required=True);a.add_argument('--prepare-only',action='store_true');args=a.parse_args();data=pathlib.Path(args.data_root).resolve();assert data==work/'data-root','Use the exact task-owned listening data root'
work.mkdir(parents=True,exist_ok=True);data.mkdir(exist_ok=True)
wav=work/'rhythm.wav';media=work/'rhythm.m4a'
# Deterministic original four-note rhythmic fixture; no owner media or music license.
notes=[440,554.365,659.255,880]
with wave.open(str(wav),'wb') as f:
 f.setparams((1,2,48000,0,'NONE','not compressed'));samples=bytearray()
 for i in range(48000*40):
  t=i/48000;phase=t%.25;env=min(1,phase/.008,max(0,(.18-phase)/.008));hz=notes[int(t/.25)%4];v=.09*env*(math.sin(2*math.pi*hz*t)+.25*math.sin(4*math.pi*hz*t));samples.extend(struct.pack('<h',int(32767*v)))
 f.writeframes(samples)
subprocess.run(['/usr/bin/afconvert','-f','m4af','-d','aac ','-b','128000',str(wav),str(media)],check=True)
if args.prepare_only:
 print('Prepared deterministic fixture only; no listening acceptance or playback.');sys.exit(0)
assert not (r/'work/deps').exists() and not (r/'work/audio').exists(),'Existing task build present; preserve it before running the owner rebuild'
try:
 with (work/'build.log').open('w') as log:
  print('Building the same pinned LGPL task-only harness (once)...',flush=True)
  subprocess.run(['git','clone','--depth','1','--branch','n9.0.1','https://github.com/FFmpeg/FFmpeg.git',str(r/'work/deps/ffmpeg-source')],stdout=log,stderr=log,check=True)
  subprocess.run([sys.executable,str(r/'tools/X1MacPlayerG1b/build_ffmpeg.py')],stdout=log,stderr=log,check=True)
  subprocess.run([sys.executable,str(r/'tools/X1AudioCompletion/build.py')],stdout=log,stderr=log,check=True)
 for label,mode,rate,seconds in [('0.5x','av','.5','6'),('1x','av','1','4'),('2x','av','2','4'),('live 1-2-1-half-1','rates','1','15'),('repeated loops','loops','1','9')]:
  print('Listen:',label,'— judge pitch, clicks and continuity yourself.',flush=True)
  cmd=[str(r/'work/audio/audio-proof'),mode,str(r/'work/data/audio-sync.mp4'),str(media),rate,seconds,'--data-root',str(data)]
  with (work/(mode+'-'+rate+'.jsonl')).open('w') as out,(work/(mode+'-'+rate+'.stderr.txt')).open('w') as err:
   proc=subprocess.Popen(cmd,stdout=out,stderr=err,env={**os.environ,'X1_AUDIBLE':'1'})
   try:
    rc=proc.wait(timeout=90)
   except BaseException:
    proc.terminate()
    try:proc.wait(timeout=3)
    except subprocess.TimeoutExpired:proc.kill();proc.wait()
    raise
   if rc:raise RuntimeError(f'{label} failed: {rc}; inspect task log')
 print('Owner listening observations remain unrecorded until you report them. No automatic subjective pass.')
finally:
 for name in ['work/audio','work/deps']:
  if (r/name).exists():shutil.rmtree(r/name)
