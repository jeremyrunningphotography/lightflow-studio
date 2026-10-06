import concurrent.futures, json, pathlib, subprocess
root=pathlib.Path(__file__).resolve().parents[2]
out=root/'docs/research/mac-compatibility/x1-player'
def run(f):
    count=len(f.get('frames',[]))-1 if 'frames' in f else 119
    with (out/('mpv-'+f['name']+'.jsonl')).open('w') as stdout, (out/('mpv-'+f['name']+'.log')).open('w') as stderr:
        p=subprocess.run([str(root/'work/mpv_probe'),str(root/f['file']),str(count)],stdout=stdout,stderr=stderr,timeout=120)
    print(f['name'],p.returncode,flush=True);return {'fixture':f['name'],'returncode':p.returncode}
with concurrent.futures.ThreadPoolExecutor(max_workers=4) as pool:
    codes=list(pool.map(run,json.loads((out/'fixture-manifest.json').read_text())))
(out/'mpv-process-results.json').write_text(json.dumps(codes,indent=2))
if any(r['returncode'] and not (r['fixture']=='rotation' and r['returncode']==-6) for r in codes):
    raise SystemExit('Unexpected mpv probe failure; inspect logs')
