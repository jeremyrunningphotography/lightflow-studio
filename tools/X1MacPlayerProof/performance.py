import hashlib,json,pathlib,subprocess,time
root=pathlib.Path(__file__).resolve().parents[2];out=root/'docs/research/mac-compatibility/x1-player';results=[]
for w,h in [(1920,1080),(3840,2160)]:
    name=f'perf-{w}x{h}';path=root/'work/data'/f'{name}.mp4'
    cmd=['ffmpeg','-v','error','-y','-f','lavfi','-i',f'testsrc2=size={w}x{h}:rate=30:duration=2','-c:v','libx264','-threads','2','-preset','fast','-g','60','-bf','3','-pix_fmt','yuv420p',str(path)]
    if not path.exists():subprocess.run(cmd,check=True)
    for hw in (0,1):
        start=time.monotonic();p=subprocess.run(['/usr/bin/time','-l',str(root/'work/decode_probe_lgpl'),str(path),str(hw),'no-save'],cwd=root,capture_output=True,text=True)
        # Keep performance frames outside versioned semantic corpus; preserve measured resource log.
        (root/'work/results'/f'{name}-{hw}.jsonl').write_text(p.stdout)
        (out/f'{name}-{hw}.log').write_text(p.stderr)
        rows=[json.loads(s) for s in p.stdout.splitlines()]
        results.append({'fixture':name,'command':cmd,'sha256':hashlib.sha256(path.read_bytes()).hexdigest(),'hardware_requested':bool(hw),'elapsed_seconds':time.monotonic()-start,'returncode':p.returncode,
            'linear_frames':sum(r['phase']=='linear' for r in rows),'hardware_frames':sum(r.get('hw',0) for r in rows),'resource_log':f'{name}-{hw}.log'})
        print(name,hw,p.returncode,flush=True)
(out/'performance.json').write_text(json.dumps(results,indent=2))
if any(s['returncode'] for s in results): raise SystemExit('Performance probe failed; inspect logs')
