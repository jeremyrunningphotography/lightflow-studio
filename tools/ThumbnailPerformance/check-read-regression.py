#!/usr/bin/env python3
"""Generated tail-moov oracle: verify the new byte bound fails the preserved original reader."""
import ctypes,pathlib,struct,json,time,sys,subprocess
repo=pathlib.Path(__file__).resolve().parents[2];root=repo/'artifacts/lf036-performance';private=root/'private';original=private/'generated-tiny.mp4';padded=private/'generated-tail-moov.mp4'
class Info(ctypes.Structure):_fields_=[('duration',ctypes.c_double)]+[(x,ctypes.c_int)for x in ['width','height','orientation','mechanism']]+[('value',ctypes.c_int64),('scale',ctypes.c_int)]
class Trace(ctypes.Structure):_fields_=[(x,ctypes.c_double)for x in ['open','init','metadata','frame','pixels','read','total']]+[(x,ctypes.c_int64)for x in ['bytes','reads','requests']]+[('active',ctypes.c_int),('metadataStatus',ctypes.c_int),('error',ctypes.c_int64)]
if len(sys.argv)==1:
 helper=ctypes.CDLL(str(repo/'Lightflow.Platform.MacOS.Tests/bin/Release/net8.0/libLightflowVolumeTests.dylib'));helper.lf_test_thumbnail_video.argtypes=[ctypes.c_char_p];original.unlink(missing_ok=True);assert helper.lf_test_thumbnail_video(str(original).encode())==0
 raw=original.read_bytes();at=0
 while at+8<len(raw):
  length,kind=struct.unpack('>I4s',raw[at:at+8]);length=struct.unpack('>Q',raw[at+8:at+16])[0]if length==1 else length
  if kind==b'moov':break
  assert length>=8;at+=length
 assert raw[at+4:at+8]==b'moov'
 with padded.open('wb')as f:f.write(raw[:at]);f.write(struct.pack('>I4s',64*1024*1024,b'free'));f.seek(64*1024*1024-8,1);f.write(raw[at:])
 rows=[]
 for name in ['original','corrected']:
  result=subprocess.run([sys.executable,__file__,name],capture_output=True,text=True,check=True)
  rows+=json.loads(result.stdout)
 assert all(x['bounded']and x['status']==0 for x in rows if x['reader']=='corrected')
 assert any(not x['bounded']for x in rows if x['reader']=='original')
 (root/'evidence/read-amplification-regression.json').write_text(json.dumps(rows,indent=2))
 print(json.dumps(rows,indent=2))
else:
 name=sys.argv[1];path=root/'baseline-native/libLightflowStorage-profile.dylib'if name=='original'else repo/'Lightflow.Platform.MacOS/bin/Release/net8.0/libLightflowStorage.dylib'
 lib=ctypes.CDLL(str(path));fn=lib.lf_thumbnail_profile;fn.argtypes=[ctypes.c_char_p,ctypes.c_int,ctypes.c_double,ctypes.c_char_p,ctypes.POINTER(ctypes.c_int),ctypes.POINTER(Info),ctypes.POINTER(Trace)];rows=[]
 for mode in [0,2]:
  flag=ctypes.c_int();info=Info();trace=Trace();start=time.monotonic();status=fn(str(padded).encode(),mode,1,str(private/f'generated-{name}.png').encode(),ctypes.byref(flag),ctypes.byref(info),ctypes.byref(trace));rows.append(dict(reader=name,mode=mode,status=status,bytesRead=trace.bytes,elapsedMs=(time.monotonic()-start)*1000,bounded=trace.bytes<32*1024*1024,actualValue=info.value,actualTimescale=info.scale))
 print(json.dumps(rows))
