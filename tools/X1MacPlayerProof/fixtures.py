"""Generate small deterministic, task-owned media and decoded PTS/hash oracle."""
import hashlib, json, pathlib, subprocess
root = pathlib.Path(__file__).resolve().parents[2]
data = root / 'work/data'; data.mkdir(parents=True, exist_ok=True)
out = root / 'docs/research/mac-compatibility/x1-player'
manifest = []
base = ['ffmpeg','-hide_banner','-loglevel','error','-y','-f','lavfi','-i','testsrc2=size=320x180:rate=30:duration=4']
cases = {
 'cfr': ['-c:v','libx264','-g','30','-bf','0'],
 'bframes': ['-c:v','libx264','-g','60','-bf','3'],
 'long_gop': ['-c:v','libx264','-g','120','-keyint_min','120','-sc_threshold','0','-bf','3'],
 'vfr': ['-vf',"select='if(lt(n,30),1,if(lt(n,60),not(mod(n,2)),not(mod(n,3))))'",'-fps_mode','vfr','-c:v','libx264','-g','120','-bf','3'],
 'nonzero': ['-vf','setpts=PTS+5/TB','-fps_mode','passthrough','-c:v','libx264','-g','120','-bf','3'],
 'audio': ['-f','lavfi','-i','sine=frequency=1000:sample_rate=48000:duration=4','-c:v','libx264','-g','120','-bf','3','-c:a','aac','-shortest'],
 'color': ['-c:v','libx264','-crf','0','-g','30','-bf','0'],
}
for name,args in cases.items():
    path=data/(name+'.mp4'); cmd=base+args+['-threads','1','-pix_fmt','yuv420p',str(path)]
    subprocess.run(cmd,check=True)
    probe=json.loads(subprocess.check_output(['ffprobe','-v','error','-show_frames','-show_streams','-select_streams','v:0','-of','json',str(path)]))
    hashes=subprocess.check_output(['ffmpeg','-v','error','-i',str(path),'-map','0:v:0','-fps_mode','passthrough','-f','framemd5','-']).decode()
    (out/(name+'-framemd5.txt')).write_text(hashes)
    manifest.append({'name':name,'file':'work/data/'+path.name,'sha256':hashlib.sha256(path.read_bytes()).hexdigest(),'command':cmd,
        'stream':probe['streams'][0],'frames':[{'pts':f.get('pts'),'pts_time':f.get('pts_time'),'type':f.get('pict_type'),'key':f.get('key_frame')} for f in probe['frames']]})
path=data/'rotation.mp4'
cmd=['ffmpeg','-v','error','-y','-display_rotation','90','-i',str(data/'color.mp4'),'-c','copy',str(path)]
subprocess.run(cmd,check=True)
rotation_probe=json.loads(subprocess.check_output(['ffprobe','-v','error','-show_streams','-of','json',str(path)]))
assert any(s.get('rotation')==90 for s in rotation_probe['streams'][0].get('side_data_list',[])), 'Missing rotation matrix'
manifest.append({'name':'rotation','file':'work/data/rotation.mp4','sha256':hashlib.sha256(path.read_bytes()).hexdigest(),'command':cmd,
 'probe':rotation_probe})
(out/'fixture-manifest.json').write_text(json.dumps(manifest,indent=2))
print('Generated',len(manifest),'fixtures')
