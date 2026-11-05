#!/usr/bin/env python3
"""Read-only native decoder timing. All output must remain in this task workspace."""
import ctypes,json,pathlib,sys,time,threading,subprocess,resource,os
repo=pathlib.Path(__file__).resolve().parents[2];root=repo/'artifacts/lf036-performance'
selection=json.loads(pathlib.Path(os.getenv('LF_SELECTION',str(root/'private/source-selection.json'))).read_text());library=pathlib.Path(sys.argv[1]).resolve();output=pathlib.Path(sys.argv[2]).resolve()
assert output.is_relative_to(root) and not str(output).startswith('/Volumes/JRPhoto4T/')
class Info(ctypes.Structure):_fields_=[('duration',ctypes.c_double),('width',ctypes.c_int),('height',ctypes.c_int),('orientation',ctypes.c_int),('mechanism',ctypes.c_int),('actualValue',ctypes.c_int64),('actualScale',ctypes.c_int)]
lib=ctypes.CDLL(str(library));fn=lib.lf_thumbnail;fn.argtypes=[ctypes.c_char_p,ctypes.c_int,ctypes.c_double,ctypes.c_char_p,ctypes.POINTER(ctypes.c_int),ctypes.POINTER(Info)];fn.restype=ctypes.c_int
profile=bool(os.getenv('LF_PROFILE'))
class Trace(ctypes.Structure):_fields_=[(x,ctypes.c_double)for x in ['openMs','initializationMs','metadataMs','frameMs','pixelsMs','readMs','totalMs']]+[(x,ctypes.c_int64)for x in ['bytesRead','readCalls','requests']]+[('activeReaders',ctypes.c_int),('metadataStatus',ctypes.c_int),('errorCode',ctypes.c_int64)]
if profile:
 fn=lib.lf_thumbnail_profile;fn.argtypes=[ctypes.c_char_p,ctypes.c_int,ctypes.c_double,ctypes.c_char_p,ctypes.POINTER(ctypes.c_int),ctypes.POINTER(Info),ctypes.POINTER(Trace)];fn.restype=ctypes.c_int
baseline=json.loads((root/'evidence/baseline-direct.json').read_text())if profile else[]
rows=[]
for source in selection[:int(os.getenv("LF_LIMIT","3"))]:
 path=pathlib.Path(source['path']);assert path.is_file()and not path.is_symlink();authorized=pathlib.Path(json.loads((root/'private/source-authorization.json').read_text())['folder']).resolve()
 assert path.parent==authorized or path.is_relative_to(root/'local-media')
 probeStart=time.monotonic();probe=next((x['codec']for x in baseline if x['alias']==source['alias']),None)
 if probe is None:probe=json.loads(subprocess.check_output(['/opt/homebrew/bin/ffprobe','-v','error','-show_entries','stream=index,codec_type,codec_name,profile,width,height,pix_fmt,r_frame_rate,avg_frame_rate,bit_rate,color_space,color_transfer,color_primaries:format=duration,size,bit_rate','-of','json',str(path)]))
 probeMs=(time.monotonic()-probeStart)*1000
 row=dict(alias=source['alias'],size=source['size'],codec=probe,ffprobeMs=probeMs)
 for mode in [0,2]:
  info=Info();cancel=ctypes.c_int(0);timer=threading.Timer(30,lambda:setattr(cancel,'value',1));timer.start();start=time.monotonic();cpu=time.process_time()
  duration=float(probe['format']['duration']);requested=(duration/2 if duration<1 else min(30,max(1,duration*.1)))if mode==2 else 0
  destination=root/'private'/f"{source['alias']}-native.png"
  trace=Trace()
  try:
   arguments=[str(path).encode(),mode,requested,str(destination).encode(),ctypes.byref(cancel),ctypes.byref(info)]
   status=fn(*(arguments+[ctypes.byref(trace)]if profile else arguments))
  finally:timer.cancel()
  row['metadata'if mode==0 else'poster']=dict(trace={name:getattr(trace,name)for name,_ in Trace._fields_}if profile else None,elapsedMs=(time.monotonic()-start)*1000,cpuSeconds=time.process_time()-cpu,status=status,cancellationRequested=bool(cancel.value),requested=requested,actualValue=info.actualValue,actualTimescale=info.actualScale,duration=info.duration,width=info.width,height=info.height,rssMax=resource.getrusage(resource.RUSAGE_SELF).ru_maxrss)
  output.write_text(json.dumps(rows+[row],indent=2));print(source['alias'],mode,row['metadata'if mode==0 else'poster']['elapsedMs'],status,flush=True)
 rows.append(row)
output.write_text(json.dumps(rows,indent=2))
