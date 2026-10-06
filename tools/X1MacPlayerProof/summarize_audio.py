import json,pathlib,statistics
root=pathlib.Path(__file__).resolve().parents[2];out=root/'docs/research/mac-compatibility/x1-player'
rows=[json.loads(s) for s in (out/'mpv-audio-sync.jsonl').read_text().splitlines()]
steady=[r for r in rows if r['phase']=='audio-play' and r['index']>=5 and r['avsync']!=-999]
clock=[json.loads(s) for s in (out/'coreaudio-results.jsonl').read_text().splitlines()]
valid=[r for r in clock if r['status']==0 and r['flags']&1 and r['index']>=5]
first,last=valid[0],valid[-1]
elapsed=last['wall_seconds']-first['wall_seconds'];samples=(last['sample_time']-first['sample_time'])/48000
result={'mpv':{'samples':len(steady),'measured_wall_interval_seconds':steady[-1]['monotonic']-steady[0]['monotonic'],
    'avsync_seconds_min':min(r['avsync'] for r in steady),'avsync_seconds_max':max(r['avsync'] for r in steady),
    'avsync_seconds_mean':statistics.mean(r['avsync'] for r in steady),'avsync_end_minus_start':steady[-1]['avsync']-steady[0]['avsync'],
    'speed_values':sorted(set(r['speed'] for r in rows if r['phase']=='audio-speed')),
    'reopen_volume':rows[-1]['volume'],'pause_retained_hash_equal':next(r for r in rows if r['phase']=='audio-pause')['hash']==next(r for r in rows if r['phase']=='audio-retained')['hash']},
    'coreaudio':{'samples':len(valid),'wall_seconds':elapsed,'audio_sample_seconds':samples,'difference_seconds':samples-elapsed,'clock_rate_ratio':samples/elapsed,
    'discontinuities':sum(r['discontinuity'] for r in valid),'errors':sum(r['status']!=0 for r in clock)},
    'limits':'mpv avsync is an engine/driver estimate, not loopback measurement. CoreAudio is an independent sample-clock probe, not an integrated controlled Player AV-sync implementation. All output muted; physical audibility/device change/sleep-wake unqualified.'}
speed=[r for r in rows if r['phase']=='audio-speed']
result['mpv']['speed_segments']=[]
for j in range(6):
    segment=[r for r in speed if j*5<=r['index']<(j+1)*5]
    a,b=segment[0],segment[-1]
    result['mpv']['speed_segments'].append({'requested':a['speed'],'observed_media_seconds_per_wall_second':(b['time_pos']-a['time_pos'])/(b['monotonic']-a['monotonic'])})
loops=[r for r in rows if r['phase']=='audio-loop']
result['mpv']['loop_observed_min']=min(r['time_pos'] for r in loops)
result['mpv']['loop_observed_max']=max(r['time_pos'] for r in loops)
result['mpv']['loop_wraps']=sum(b['time_pos']<a['time_pos'] for a,b in zip(loops,loops[1:]))
(out/'audio-summary.json').write_text(json.dumps(result,indent=2));print(json.dumps(result,indent=2))
