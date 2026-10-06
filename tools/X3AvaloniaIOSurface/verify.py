#!/usr/bin/env python3
"""Independent PNG/pixel and structured-log oracle; no screenshot synthesis."""
from pathlib import Path
import json,gzip,hashlib,struct,zlib,sys
root=Path(__file__).resolve().parents[2];e=root/'docs/research/mac-compatibility/x3-iosurface'
def png(path):
 data=path.read_bytes();pos=8;packed=b''
 while pos<len(data):
  n=struct.unpack('>I',data[pos:pos+4])[0];kind=data[pos+4:pos+8];chunk=data[pos+8:pos+8+n];pos+=12+n
  if kind==b'IHDR':w,h,depth,color,*_=struct.unpack('>IIBBBBB',chunk);assert depth==8 and color in (2,6)
  if kind==b'IDAT':packed+=chunk
 bpp=4 if color==6 else 3;stride=w*bpp;raw=zlib.decompress(packed);rows=[];prior=bytearray(stride);at=0
 for y in range(h):
  filt=raw[at];at+=1;row=bytearray(raw[at:at+stride]);at+=stride
  for x in range(stride):
   a=row[x-bpp] if x>=bpp else 0;b=prior[x];c=prior[x-bpp] if x>=bpp else 0
   if filt==1:row[x]=(row[x]+a)&255
   elif filt==2:row[x]=(row[x]+b)&255
   elif filt==3:row[x]=(row[x]+(a+b)//2)&255
   elif filt==4:
    p=a+b-c;pa,pb,pc=abs(p-a),abs(p-b),abs(p-c);row[x]=(row[x]+(a if pa<=pb and pa<=pc else b if pb<=pc else c))&255
   else:assert filt==0
  rows.append(row);prior=row
 return w,h,lambda x,y:list(rows[y][x*bpp:x*bpp+3])
rows=[json.loads(x) for x in gzip.open(e/'RUNTIME_ROWS.jsonl.gz','rt')];assert rows[-1]['value']=={'surfaces':0,'producers':0};assert not any(x['kind']=='failure' for x in rows)
assert all(x['value']['match'] for x in rows if x['kind']=='frame-identity')
i=next(x['value'] for x in rows if x['kind']=='input');assert all(i[k] for k in ['overlayWorks','videoWorks','context','player','local']);assert i['Moves']>0 and i['Wheels']>0
w,h,p=png(e/'normal.png');assert (w,h)==(1600,1000)
background=p(50,50);green=p(800,500);assert green==[0,255,0]
outside=[p(199,400),p(1401,400),p(800,179),p(800,821),p(201,181)];assert all(x==background for x in outside)
_,_,off=png(e/'overlays-off.png');assert off(800,500)!=green
_,_,detached=png(e/'detached.png');assert detached(500,400)==background and detached(800,500)==green
_,_,attached=png(e/'reattached.png');assert attached(500,400)==off(500,400)
_,_,transformed=png(e/'opacity-transform.png');assert transformed(800,500)==green and transformed(500,400)!=off(500,400)
checks={'background':background,'overlay':green,'outsideAndRoundedCorner':outside,'overlayOffPixel':off(800,500),'detachBackground':detached(500,400),'reattachVideoPixel':attached(500,400),'opacityTransformVideoPixel':transformed(500,400),'status':'proven offscreen GPU composition snapshots','physicalDisplay':'unresolved','source':'independent standard-library PNG filter decoder; verify.py'}
(e/'OVERLAY_CLIPPING_RESULTS.json').write_text(json.dumps(checks,indent=2)+'\n')
print('PASS: identity, input, bounded resources, rounded/rectangular clipping, z-order, opacity/transform and detach/reattach pixel checks')
