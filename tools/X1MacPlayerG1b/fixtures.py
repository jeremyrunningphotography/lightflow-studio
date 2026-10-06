import pathlib,subprocess,json,hashlib
r=pathlib.Path(__file__).resolve().parents[2];data=r/'work/g1b';out=r/'docs/research/mac-compatibility/x1-player-g1b';results=[]
for name,vfr in [('long-cfr',False),('long-vfr',True)]:
 p=data/(name+'.mp4');cmd=['ffmpeg','-v','error','-y','-f','lavfi','-i','testsrc2=size=320x180:rate=30:duration=180']
 if vfr:cmd+=['-vf',"select='if(lt(mod(n,90),30),1,not(mod(n,3)))'",'-fps_mode','vfr']
 cmd+=['-c:v','libx264','-threads','2','-preset','fast','-g','300','-keyint_min','300','-sc_threshold','0','-bf','3','-pix_fmt','yuv420p',str(p)]
 if not p.exists():subprocess.run(cmd,check=True)
 probe=json.loads(subprocess.check_output(['ffprobe','-v','error','-show_frames','-show_streams','-select_streams','v:0','-of','json',str(p)]))
 results.append({'name':name,'path':str(p.relative_to(r)),'command':cmd,'sha256':hashlib.sha256(p.read_bytes()).hexdigest(),'stream':probe['streams'][0],'pts':[f['pts'] for f in probe['frames']]})
(out/'fixtures.json').write_text(json.dumps(results,indent=2))
print('Long-duration fixtures generated/PTS oracle retained')
