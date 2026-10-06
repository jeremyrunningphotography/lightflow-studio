import json,pathlib
from fractions import Fraction
root=pathlib.Path(__file__).resolve().parents[2];out=root/'docs/research/mac-compatibility/x1-player'
summary=[]
for f in json.loads((out/'fixture-manifest.json').read_text()):
    if 'frames' not in f: continue
    rows=[json.loads(s) for s in (out/('mpv-'+f['name']+'.jsonl')).read_text().splitlines()]
    baseline=[r for r in rows if r['phase'] in ('open','forward')]
    assert len(baseline)==len(f['frames']), 'Incomplete forward sweep: '+f['name']
    offset=float(f['stream']['start_time']);expected=[float(Fraction(v['pts'])*Fraction(f['stream']['time_base'])) for v in f['frames']]
    # This exact MOV corpus exposes raw PTS in mpv, despite default rebase-start-time=yes.
    # Record it explicitly; do not mistake a correct raw PTS for a normalized PTS failure.
    forward_errors=[{'index':i,'expected':p,'observed':r['time_pos']} for i,(p,r) in enumerate(zip(expected,baseline)) if abs(p-r['time_pos'])>0.000000001]
    reverse=[r for r in rows if r['phase']=='reverse'];reverse_errors=[]
    assert len(reverse)==len(baseline)-1, 'Incomplete reverse sweep: '+f['name']
    for i,r in enumerate(reverse):
        want=baseline[-i-2]
        if abs(want['time_pos']-r['time_pos'])>0.000000001 or want['hash']!=r['hash']:
            reverse_errors.append({'index':i,'expected_pts':want['time_pos'],'observed_pts':r['time_pos'],'expected_hash':want['hash'],'observed_hash':r['hash']})
    seek=next(r for r in rows if r['phase']=='seek');retained=next(r for r in rows if r['phase']=='retained')
    # Use the recorded forward-render pixel identity as a retained-frame oracle.
    hashes={r['hash']:r['time_pos'] for r in baseline}
    playback=[r for r in rows if r['phase'] in ('play','speed2','pause','loop')]
    identity_mismatch=[{'phase':r['phase'],'time_pos':r['time_pos'],'retained_frame_pts':hashes.get(r['hash'])} for r in playback if r['hash'] in hashes and abs(r['time_pos']-hashes[r['hash']])>0.000001]
    summary.append({'fixture':f['name'],'raw_start':offset,'count':len(baseline),'forward_errors':forward_errors,'reverse_errors':reverse_errors,
        'retained_after_seek_equal':seek['hash']==retained['hash'] and seek['time_pos']==retained['time_pos'],
        'seek_reported':seek['time_pos'],'seek_pixel_pts':hashes.get(seek['hash']),'playback_identity_mismatches':identity_mismatch,
        'video_pts_property_available':any(r['video_pts']!=-999 for r in rows)})
(out/'mpv-summary.json').write_text(json.dumps(summary,indent=2))
for s in summary: print(s['fixture'],'forward',len(s['forward_errors']),'reverse',len(s['reverse_errors']),'seek',s['seek_reported'],s['seek_pixel_pts'],'playback mismatches',len(s['playback_identity_mismatches']))
