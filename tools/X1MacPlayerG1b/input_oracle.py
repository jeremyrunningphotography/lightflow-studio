"""Record independent CLI PTS lists for every retained short playback input."""
import json,pathlib,subprocess
r=pathlib.Path(__file__).resolve().parents[2];o=r/'docs/research/mac-compatibility/x1-player-g1b';entries=[]
for p in sorted((r/'work/data').glob('*.mp4')):
 if p.name not in ['rotation.mp4','color.mp4','audio-sync.mp4','perf-1920x1080.mp4','perf-3840x2160.mp4']:continue
 d=json.loads(subprocess.check_output(['ffprobe','-v','error','-select_streams','v:0','-show_frames','-show_streams','-of','json',str(p)]))
 entries.append({'file':str(p.relative_to(r)),'timebase':d['streams'][0]['time_base'],'start_pts':d['streams'][0].get('start_pts'),'pts':[f['pts'] for f in d['frames']]})
(o/'INPUT_PTS_ORACLE.json').write_text(json.dumps(entries,indent=2)+'\n')
