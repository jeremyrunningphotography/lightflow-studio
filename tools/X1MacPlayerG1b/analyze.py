"""Derive audit summaries from complete native rows. Fail on authority/oracle violations."""
import gzip,hashlib,json,pathlib,re,statistics,math
r=pathlib.Path(__file__).resolve().parents[2];o=r/'docs/research/mac-compatibility/x1-player-g1b'
def read(name):
 p=o/(name+'.jsonl')
 data=p.read_text() if p.exists() else gzip.decompress((o/'raw'/(name+'.jsonl.gz')).read_bytes()).decode()
 return [json.loads(s) for s in data.splitlines()]
def write(name,value): (o/(name+'.json')).write_text(json.dumps(value,indent=2)+'\n')
def stats(values):
 v=sorted(values)
 if not v:return None
 return {'n':len(v),'min':v[0],'mean':statistics.mean(v),'p50':v[len(v)//2],'p95':v[min(len(v)-1,math.ceil(len(v)*.95)-1)],'p99':v[min(len(v)-1,math.ceil(len(v)*.99)-1)],'max':v[-1]}
def slope(x,y):
 xm,ym=statistics.mean(x),statistics.mean(y)
 return sum((a-xm)*(b-ym) for a,b in zip(x,y))/sum((a-xm)**2 for a in x)
names=[f.stem for f in o.glob('*.jsonl')] or [f.name[:-9] for f in (o/'raw').glob('*.jsonl.gz')]
allrows={n:read(n) for n in sorted(names)}
input_pts={e['file']:set(e['pts']) for e in json.loads((o/'INPUT_PTS_ORACLE.json').read_text())}
authority=[]
for name,rows in allrows.items():
 frames=[x for x in rows if 'presented_ack' in x]
 for f in frames:
  assert f['pts'] in input_pts[f['session']],(name,f)
  assert f['producer_completed'] and f['consumer_completed'],(name,f)
  if f['presented_ack']:
   assert f['presented_time']>0 and f['published_serial']==f['serial'],(name,f)
  else:assert f['capture_serial']==0 and f['published_serial']!=f['serial'],(name,f)
  if f['capture_serial']:assert f['capture_serial']==f['serial'] and f['capture_pixels']!='0000000000000000'
 authority.append({'case':name,'present_attempts':len(frames),'valid_ack':sum(f['presented_ack'] for f in frames),'unacknowledged':sum(not f['presented_ack'] for f in frames)})
semantic=allrows['semantic'];frames=[x for x in semantic if 'presented_ack' in x];expected={'paused':0,'paused-retry':0,'forward':512,'reverse':0,'seek':30720}
for x in frames:
 if x['action'] in expected:assert x['pts']==expected[x['action']]
assert next(x for x in semantic if x.get('action')=='stale-invariant')['retained_unchanged']
write('FRAME_AUTHORITY_RESULTS',{'checks':authority,'semantic_expected_pts':expected,'timebase':[1,15360],'stale_retained_unchanged':True,'limits':['single Asset/session per case; source replacement/concurrent callbacks not qualified','no physical scanout oracle; Mac was observed locked after original matrix, transition time unknown; post-owner-unlock reruns are separately identified','publication is only last confirmed native presentation, not a guarantee that a rehosted/occluded view is currently showing it','GPU-only large playback has no per-frame CPU pixel hash/oracle; zero error in those rows means not evaluated']})
color=[]
for name,rows in allrows.items():
 selected=[x for x in rows if x.get('action') in ['rotation-color-capture','hardware-rotation-color-capture']]
 if selected:
  assert len(selected)==20
  assert len({(x['source_clockwise_quarters'],x['camera'],x['creative'],x['compare']) for x in selected})==20
  assert all(x['pts']==30720 for x in selected)
  color.append({'case':name,'variants':len(selected),'acknowledged':sum(x['presented_ack'] for x in selected),'max_8bit_channel_error':max(x['cpu_oracle_max_channel_error'] for x in selected),'table_hashes':[selected[0]['camera_table_hash'],selected[0]['creative_table_hash']]})
write('COLOR_CAPTURE_RESULTS',{'variants':color,'retained_lease':allrows['leases'][-1],'limits':'CPU oracle is FFmpeg decoded BGRA plus independent CPU quarter-turn/trilinear stages. SDR limited-range BT.601 NV12 only; hardware conversion uses nearest chroma. No HDR/ICC/wide-gamut claim. Capture is backend surface readback, not a screenshot of UI overlays.'})
reverse=[]
for name in ['reverse-cfr','reverse-vfr']:
 rows=allrows[name]
 fixture=next(e for e in json.loads((o/'fixtures.json').read_text()) if e['name']==name.replace('reverse-','long-'))
 assert [x['pts'] for x in rows if x.get('phase')=='linear-oracle']==fixture['pts']
 checks=[x for x in rows if x.get('phase')=='reverse'];assert len(checks)==300 and all(x['exact'] for x in checks)
 reverse.append({'case':name,'oracle':next(x for x in rows if x.get('phase')=='oracle'),'exact_checks':len(checks),'cache':next(x for x in rows if x.get('phase')=='cache-summary'),'latency_ms':stats([x['ms'] for x in checks]),'miss_latency_ms':stats([x['ms'] for x in checks if not x['cache_hit']]),'decoded_per_miss':stats([x['decoded'] for x in checks if not x['cache_hit']]),'rss_bytes':stats([x['rss'] for x in checks]),'forward_after_reverse':rows[-1]})
write('REVERSE_STEP_RESULTS',{'cases':reverse,'limits':['three fixed neighborhoods (EOF, middle, near index 300), 100 predecessor queries each; not random exhaustive queries','linear native pixel oracle plus independent ffprobe PTS list in fixtures.json','16 MiB conservative cache accounting only at 320x180; native frame refs retained, not a 4K reverse-memory qualification','cancelled-generation suppression is separately simulated; interruption of an expensive reverse decode not qualified']})
perf=[];audio=[]
for name,rows in allrows.items():
 p=[x for x in rows if x.get('action')=='playback']
 if not p:continue
 a=[x for x in p if x['presented_ack']];summary=next(x for x in rows if x.get('action')=='playback-summary');seconds=summary['seconds']
 assert sum(x['dropped_before_present'] for x in p)==summary['dropped']
 for x in p:assert x['pts']%512==0 and x['tb_num']==1 and x['tb_den']==15360
 logpath=o/(name+'.log');log=(logpath if logpath.exists() else o/'raw'/(name+'.log')).read_text();m=re.search(r'([\d.]+) real\s+([\d.]+) user\s+([\d.]+) sys',log)
 cpu=(float(m[2])+float(m[3]))/float(m[1])*100 if m else None
 first=[x for x in a if x['wall_elapsed']<10];last=[x for x in a if x['wall_elapsed']>seconds-10]
 row={'case':name,'requested_seconds':seconds,'frames_submitted':len(p),'native_acknowledged':len(a),'ack_fps':len(a)/seconds,'scheduled_skips':summary['dropped'],'frame_pts_minus_audio_clock_ms':stats([x['av_offset_ms'] for x in a]),'producer_gpu_command_ms':stats([x['gpu_ms'] for x in p]),'cpu_readback_upload_ms':stats([x['cpu_copy_ms'] for x in p]),'rss_during_playback_bytes':stats([x['rss'] for x in p]),'rss_after_seek_capture_bytes':summary['rss'],'process_cpu_percent_one_core':cpu,'first_10_seconds':{'ack':len(first),'mean_gpu_ms':statistics.mean(x['gpu_ms'] for x in first),'mean_rss':statistics.mean(x['rss'] for x in first)},'last_10_seconds':{'ack':len(last),'mean_gpu_ms':statistics.mean(x['gpu_ms'] for x in last),'mean_rss':statistics.mean(x['rss'] for x in last)},'loops':max(x['loop_cycle'] for x in p),'thermal_samples':[x for x in rows if x.get('phase')=='thermal-rss'],'mode':summary}
 perf.append(row)
 x=[v['wall_elapsed'] for v in a];y=[(v['audio_source_seconds']-v['wall_elapsed']*v['speed'])*1000 for v in a]
 audio.append({'case':name,'speed':summary['speed'],'interval_seconds':seconds,'source_pts_minus_audio_sample_clock_ms':stats([v['av_offset_ms'] for v in a]),'audio_clock_vs_wall_slope_ms_per_second':slope(x,y) if not row['loops'] else None,'mean_offset_first_5_seconds':statistics.mean(v['av_offset_ms'] for v in a if v['wall_elapsed']<5),'mean_offset_last_5_seconds':statistics.mean(v['av_offset_ms'] for v in a if v['wall_elapsed']>seconds-5),'pause':next(v for v in rows if v.get('action')=='audio-pause'),'resume':next(v for v in rows if v.get('action')=='audio-resume'),'seek_resume':next(v for v in rows if v.get('action')=='seek-resume-clock'),'loops':row['loops']})
write('PERFORMANCE_RESULTS',{'cases':perf,'limits':['serial blocking render/present/capture experiment, not optimized Player','30fps synthetic H264 2-second 1080p/4K files replayed with queue restart at each loop; audio is parallel predecoded 40-second input, not same 2-second encoded AV stream','GPU statistic is producer command only; consumer/compositor GPU utilization not instrumented','decode_batch_ms includes scheduling wait and must not be interpreted as pure decode cost; throughput is integrated acknowledged fps','two decoded frames and three output slots are bounded; RSS is measured process resident memory, not an allocation/leak diagnosis','no underrun detector, physical AV loopback, power metrics or thermal saturation run; 60/120 second trend only; Mac lock was observed afterward, exact transition time unknown, desktop visibility not continuously monitored; post-unlock 20/30-second cases separately identified','CPU copy includes readback, allocation and consumer texture upload, excluding capture hashing; not an Avalonia bitmap benchmark']})
write('AUDIO_SYNC_RESULTS',{'cases':audio,'injected_restart':allrows['audio-fault'],'architecture':'predecoded FFmpeg AAC mono F32/48kHz -> native LGPL atempo -> AudioQueue three 1024-sample buffers, gain zero; source clock = seek origin + played sample time / 48000 * speed; video chooses decoded source PTS <= clock + 1/60s*speed, then waits for native presented handler','limits':['source-frame offset sampled after callback, not at physical display/speaker instant','predecode and affine tempo mapping do not prove streaming packet PTS, atempo startup/seek sample origin or pitch/audibility','speed segments .5/1/2 are separate opens, not mid-playback changes; .125/.25/4 not qualified here','loop disposes/restarts queue: measured playback, explicit discontinuity; no seamless-loop claim','silent step stops audio; volume parameter roundtrip only; no preserved volume across source opens','injected AudioQueue stop/recreation is not an actual underrun or device-change detector']})
write('LIFECYCLE_RESULTS',{'baseline':allrows['lifecycle'],'active_fullscreen_reopen':allrows['shown-lifecycle'],'after_owner_unlock':allrows.get('shown-lifecycle-unlocked',[]),'limits':['actual fullscreen style/size and native acknowledgements recorded; Spaces navigation/focus restoration not qualified','deactivate request left NSApp.active true, so deactivate/reactivate transition is unresolved','only scale 2 display; cross-display backing-scale transition not exercised','pre-unlock first/retry acknowledgements sometimes absent; after owner unlock all four reopen retries acknowledged but first draws did not; general recovery/visibility policy unresolved','no physical sleep/wake, output-device switch, GPU device loss or audio-underrun injection']})
print('Derived authority, Color, audio, reverse, performance and lifecycle summaries; exact checks passed')
