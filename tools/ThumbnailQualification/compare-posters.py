#!/usr/bin/env python3
"""Mac-host FFmpeg reference comparison. This does not constitute Windows qualification."""
import subprocess,json,hashlib,pathlib,sys
report=pathlib.Path(sys.argv[1]);out=pathlib.Path(sys.argv[2]);out.mkdir(parents=True,exist_ok=True)
d=json.loads(report.read_text());rows=[]
ffmpeg='/opt/homebrew/bin/ffmpeg';ffprobe='/opt/homebrew/bin/ffprobe'
for ob in d['observations']:
 if ob['Mechanism']!='AVFoundation' or ob['Status']!=0:continue
 source=pathlib.Path(ob['Source']);requested=ob['RequestedSeconds'];actual=ob['ActualValue']/ob['ActualTimescale']
 probe=json.loads(subprocess.check_output([ffprobe,'-v','error','-select_streams','v:0','-show_frames','-show_streams','-of','json',str(source)]))
 frames=[float(x['best_effort_timestamp_time']) for x in probe['frames'] if 'best_effort_timestamp_time'in x]
 mac=min(range(len(frames)),key=lambda i:abs(frames[i]-actual));ref=next((i for i,t in enumerate(frames)if t+1e-7>=requested),len(frames)-1)
 # Same input seek/one-frame/scale/JPEG command as Windows ExtractThumbnail, using this host's FFmpeg.
 reference=out/(source.stem+'-ffmpeg-reference.jpg')
 subprocess.run([ffmpeg,'-hide_banner','-loglevel','error','-y','-ss',f'{requested:.7f}','-i',str(source),'-an','-frames:v','1','-vf','scale=512:512:force_original_aspect_ratio=decrease','-c:v','mjpeg','-q:v','3','-f','image2',str(reference)],check=True)
 thumbnail=next(x['thumbnail']['Path']for x in d['results']if not x['reopen']and x.get('name')==source.name)
 def pixels(path):return subprocess.check_output([ffmpeg,'-v','error','-i',str(path),'-vf','scale=64:36','-pix_fmt','rgb24','-f','rawvideo','-'])
 a,b=pixels(thumbnail),pixels(reference);mae=sum(abs(x-y)for x,y in zip(a,b))/len(a)
 delta=mac-ref
 rows.append(dict(fixture=source.name,sha256=hashlib.sha256(source.read_bytes()).hexdigest(),codec=probe['streams'][0]['codec_name'],averageFrameRate=probe['streams'][0]['avg_frame_rate'],requested=requested,actualValue=ob['ActualValue'],actualTimescale=ob['ActualTimescale'],actual=actual,macOrdinal=mac,ffmpegReferenceOrdinal=ref,ordinalDelta=delta,classification='exact'if delta==0 else '+/-1'if abs(delta)==1 else '+/-2'if abs(delta)==2 else 'greater mismatch',rgbMeanAbsoluteDifference64x36=mae,reference=str(reference)))
result=dict(platform='Mac-host FFmpeg reference; independent Windows packaged reference remains pending',ffmpeg=subprocess.check_output([ffmpeg,'-version'],text=True).splitlines()[0],rows=rows)
(out/'comparison.json').write_text(json.dumps(result,indent=2));print(json.dumps(rows,indent=2))
