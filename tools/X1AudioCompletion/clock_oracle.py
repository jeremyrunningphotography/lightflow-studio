"""Independent event-ledger safety oracle; SIMULATION, not AudioQueue evidence."""
import random,json
from pathlib import Path
r=Path(__file__).resolve().parents[2]
class Ledger:
 def __init__(self):self.epoch=0;self.origin=0;self.rate=1;self.ranges=[];self.raw=0;self.played=0;self.valid=False;self.paused=False
 def reset(self,origin,rate):self.epoch+=1;self.origin=origin;self.rate=rate;self.ranges=[];self.raw=0;self.played=0;self.valid=False;self.paused=False
 def submit(self,epoch,n):
  if epoch!=self.epoch:return False
  start=sum(self.ranges);self.ranges.append(n);self.valid=True;return True
 def observe(self,n):
  if not self.valid or self.paused:return self.played
  if n<self.raw:self.valid=False;return self.played
  self.raw=n;cap=sum(self.ranges);self.played=max(self.played,min(n,cap))
  if n>=cap:self.valid=False
  return self.played
rng=random.Random(368378);m=Ledger();assertions=0;events=[]
for epoch in range(100):
 m.reset(rng.random()*10,rng.choice([.5,1,2]));old=m.epoch-1
 assert not m.submit(old,48000);assert m.played==0;assertions+=2
 for k in range(3):m.submit(m.epoch,1024)
 for n in range(0,6000,137):
  before=m.played;p=m.observe(n);assert p<=3072 and p>=before;assertions+=2
 assert not m.valid and m.played==3072;assertions+=1
 m.reset(m.origin+m.played/48000*m.rate,m.rate);m.submit(m.epoch,3072)
 m.observe(1024);m.paused=True;frozen=m.played;m.observe(1000000);assert frozen==m.played;assertions+=1
 # Resume is a new source epoch, never rebases a drifting raw counter in place.
 source=m.origin+frozen/48000*m.rate;m.reset(source,m.rate);m.submit(m.epoch,3072);assert m.observe(0)==0;assertions+=1
 events.append({'epoch':epoch,'stale_rejected':True,'underrun_cap':3072,'pause_frozen':True,'resume_origin':source})
p=r/'docs/research/mac-compatibility/x1-player-audio-completion'
(p/'CLOCK_PROTOCOL_SIMULATION.json').write_text(json.dumps({'scope':'independent safety event ledger simulation; does NOT qualify native AudioQueue or production implementation','seed':368378,'assertions':assertions,'epochs':100,'events':events},indent=2)+'\n')
print('PASS',assertions,'simulated ledger assertions; native runtime still separate')
