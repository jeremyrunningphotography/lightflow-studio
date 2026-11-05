#!/usr/bin/env python3
"""Read-only same-source Windows-style extraction. Never writes beside source media."""
import pathlib,json,subprocess,time,resource
repo=pathlib.Path(__file__).resolve().parents[2];root=repo/'artifacts/lf036-performance';selection=json.loads((root/'private/source-selection.json').read_text());baseline=json.loads((root/'evidence/baseline-direct.json').read_text());rows=[]
for source in selection:
 prior=next(x for x in baseline if x['alias']==source['alias']);duration=float(prior['codec']['format']['duration']);requested=duration/2 if duration<1 else min(30,max(1,duration*.1));dest=root/'private'/f"{source['alias']}-ffmpeg.jpg";assert dest.is_relative_to(root)
 start=time.monotonic();cpu=resource.getrusage(resource.RUSAGE_CHILDREN);process=subprocess.run(['/opt/homebrew/bin/ffmpeg','-hide_banner','-loglevel','error','-y','-ss',f'{requested:.9f}','-i',source['path'],'-an','-frames:v','1','-vf','scale=512:512:force_original_aspect_ratio=decrease','-c:v','mjpeg','-q:v','3','-f','image2',str(dest)],capture_output=True,timeout=120)
 after=resource.getrusage(resource.RUSAGE_CHILDREN);row=dict(alias=source['alias'],requested=requested,elapsedMs=(time.monotonic()-start)*1000,exitCode=process.returncode,cpuSeconds=after.ru_utime+after.ru_stime-cpu.ru_utime-cpu.ru_stime,maxChildRss=after.ru_maxrss);rows.append(row);(root/'evidence/ffmpeg-comparison.json').write_text(json.dumps(rows,indent=2));print(row,flush=True)
 # Native/library diagnostics may contain filenames; keep original tool stderr private.
 (root/'private'/f"{source['alias']}-ffmpeg-stderr.txt").write_bytes(process.stderr)
