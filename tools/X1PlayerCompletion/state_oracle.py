"""Deterministic protocol simulation; not an Avalonia/native publication test."""
from pathlib import Path
import random,json,hashlib
r=Path(__file__).resolve().parents[2];rng=random.Random(368);generation=1;host=1;current=None;pending={};rejected=0;accepted=0;checks=0;events=[]
for serial in range(1,2001):
 if serial%19==0:generation+=1;current=None
 if serial%47==0:host+=1;current=None
 offer={'serial':serial,'generation':generation-(serial%31==0),'host':host-(serial%17==0),'pts':serial*1001,'timebase':[1,30000],'colorRevision':serial%5,'pixels':hashlib.sha256(str((serial,serial%5)).encode()).hexdigest()}
 pending[serial]=offer
 # Bounded arbitrary producer/compositor completion order including stale callbacks.
 for _ in range(rng.randrange(0,2)):
  key=rng.choice(list(pending));token=pending.pop(key)
  if token['generation']!=generation or token['host']!=host or (current and token['serial']<=current['serial']):rejected+=1;continue
  current=token;accepted+=1
  for action in ['pause','step-forward','step-reverse','seek','play','speed','loop','Color','Compare','capture','In','Out','marker','Subclip','review']:
   pinned=dict(current);capture=pinned['pixels'];authored=pinned['pts'];assert capture==current['pixels'] and authored==current['pts'] and pinned['serial']==current['serial'];checks+=1
  events.append({'serial':key,'generation':generation-(serial%31==0),'host':host-(serial%17==0)})
  if not pending:break
p=r/'docs/research/mac-compatibility/x1-player-completion/STATE_AUTHORITY_RESULTS.json';p.write_text(json.dumps({'status':'proven in deterministic protocol simulation only','seed':368,'offered':2000,'accepted':accepted,'rejected':rejected,'operation_identity_checks':checks,'native_UIAccepted_callback':'unimplemented; later isolated X3 qualification required','events':events},indent=2)+'\n')
print('PASS simulated protocol:',accepted,'accepted;',rejected,'rejected;',checks,'identity checks')
