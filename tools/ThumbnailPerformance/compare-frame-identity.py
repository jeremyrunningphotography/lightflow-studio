#!/usr/bin/env python3
"""Source PTS comparison; private source frames remain outside portable/public evidence."""
import json,pathlib,subprocess,re,hashlib
from fractions import Fraction
repo=pathlib.Path(__file__).resolve().parents[2];root=repo/'artifacts/lf036-performance';sources=json.loads((root/'private/source-selection.json').read_text());baseline=json.loads((root/'evidence/baseline-direct.json').read_text());native=json.loads((root/'evidence/corrected-direct.json').read_text());rows=[]
for item in sources:
 original=next(x for x in baseline if x['alias']==item['alias']);current=next(x for x in native if x['alias']==item['alias']);request=current['poster']['requested'];output=root/'private'/f"{item['alias']}-copyts-reference.jpg"
 result=subprocess.run(['/opt/homebrew/bin/ffmpeg','-hide_banner','-loglevel','info','-y','-copyts','-ss',f'{request:.9f}','-i',item['path'],'-an','-frames:v','1','-vf','scale=512:512:force_original_aspect_ratio=decrease,showinfo','-c:v','mjpeg','-q:v','3','-f','image2',str(output)],capture_output=True,text=True,timeout=120)
 assert result.returncode==0;(root/'private'/f"{item['alias']}-copyts-log.txt").write_text(result.stderr)
 match=re.search(r'\bn:\s*0\s+pts:\s*(-?\d+)\s+pts_time:',result.stderr);base=re.search(r'config in time_base:\s*(\d+/\d+)',result.stderr);assert match and base
 ffTime=Fraction(int(match[1]))*Fraction(base[1]);av=current['poster'];avTime=Fraction(av['actualValue'],av['actualTimescale']);primary=next(s for s in original['codec']['streams']if s.get('codec_type')=='video')
 rate=primary['r_frame_rate'];average=primary['avg_frame_rate'];start=max(0,float(min(avTime,ffTime))-1)
 samples=json.loads(subprocess.check_output(['/opt/homebrew/bin/ffprobe','-v','error','-probesize','32768','-select_streams','v:0','-read_intervals',f'{start:.6f}%+5','-show_packets','-show_entries','packet=pts:stream=time_base','-of','json',item['path']],timeout=90))
 timebase=Fraction(samples['streams'][0]['time_base']);times=sorted(set(Fraction(int(p['pts']))*timebase for p in samples['packets']if'pts'in p))
 avOrdinal=min(range(len(times)),key=lambda i:abs(times[i]-avTime));ffOrdinal=min(range(len(times)),key=lambda i:abs(times[i]-ffTime));assert abs(times[avOrdinal]-avTime)<Fraction(1,1000000) and abs(times[ffOrdinal]-ffTime)<Fraction(1,1000000)
 previous=root/'private'/f"{item['alias']}-ffmpeg.jpg";same=hashlib.sha256(previous.read_bytes()).digest()==hashlib.sha256(output.read_bytes()).digest();assert same,'Timestamp instrumentation changed reference pixels'
 rows.append(dict(alias=item['alias'],requested=request,nominalFrameRate=rate,averageFrameRate=average,ordinalBasis='sorted source presentation samples in bounded seek window',nativeActualValue=av['actualValue'],nativeActualTimescale=av['actualTimescale'],ffmpegPts=int(match[1]),ffmpegTimeBase=base[1],nativeOrdinal=avOrdinal,ffmpegOrdinal=ffOrdinal,ordinalDelta=avOrdinal-ffOrdinal,referencePixelsUnchanged=same))
(root/'evidence/owner-frame-comparison.json').write_text(json.dumps(rows,indent=2));print(json.dumps(rows,indent=2))
