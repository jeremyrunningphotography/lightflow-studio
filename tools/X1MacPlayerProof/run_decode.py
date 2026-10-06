import json,pathlib,subprocess,time,sys
root=pathlib.Path(__file__).resolve().parents[2];out=root/'docs/research/mac-compatibility/x1-player'
summary=[]
lgpl='--lgpl' in sys.argv;prefix='ffmpeg-lgpl-' if lgpl else 'ffmpeg-'
for f in json.loads((out/'fixture-manifest.json').read_text()):
    if f['name']=='rotation': continue
    for hw in (0,1):
        start=time.monotonic()
        p=subprocess.run([str(root/('work/decode_probe_lgpl' if lgpl else 'work/decode_probe')),str(root/f['file']),str(hw)],cwd=root,capture_output=True,text=True)
        name=f['name']+('-hardware' if hw else '-software')
        (out/(prefix+name+'.jsonl')).write_text(p.stdout)
        (out/(prefix+name+'.log')).write_text(p.stderr)
        rows=[json.loads(s) for s in p.stdout.splitlines()];frames=[r for r in rows if r['phase']=='linear']
        expected=[v['pts'] for v in f['frames']]
        summary.append({'fixture':f['name'],'hardware_requested':bool(hw),'returncode':p.returncode,'seconds':time.monotonic()-start,'frame_count':len(frames),'pts_sequence_equal':expected==[v['pts'] for v in frames],
            'hardware_frames':sum(r['hw'] for r in frames),'predecessors':[r for r in rows if r['phase']!='linear']})
        print(name,p.returncode,len(frames),summary[-1]['pts_sequence_equal'],flush=True)
(out/(prefix+'summary.json')).write_text(json.dumps(summary,indent=2))
if any(s['returncode']!=0 or not s['pts_sequence_equal'] for s in summary): raise SystemExit('Native decode evidence failed; inspect logs')
