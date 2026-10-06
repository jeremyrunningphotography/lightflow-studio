"""Verify native measurements against independent decoded fixture PTS oracle."""
import json,pathlib
root=pathlib.Path(__file__).resolve().parents[2];out=root/'docs/research/mac-compatibility/x1-player';results=[]
fixtures=json.loads((out/'fixture-manifest.json').read_text())
for f in fixtures:
    if 'frames' not in f:continue
    pts=[r['pts'] for r in f['frames']]
    for hw in ('software','hardware'):
        rows=[json.loads(s) for s in (out/f'ffmpeg-lgpl-{f["name"]}-{hw}.jsonl').read_text().splitlines()]
        frames=[r for r in rows if r['phase']=='linear'];assert [r['pts'] for r in frames]==pts
        hashes={r['pts']:r['hash'] for r in frames};checks=[]
        for r in rows:
            if r['phase']!='seek-predecessor':continue
            target=r['target'];index=next(i for i,p in enumerate(pts) if p>=target)
            expected=pts[index];previous=pts[index-1]
            ok=r['settled']==expected and r['previous']==previous and r['previous_hash']==hashes[previous] and r['hash']==hashes[expected]
            checks.append({'target':target,'expected_settled':expected,'expected_predecessor':previous,'exact_pts_and_pixel_identity':ok});assert ok
        results.append({'fixture':f['name'],'mode':hw,'pts_equal':True,'frames':len(frames),'predecessor_checks':checks})
mpv=json.loads((out/'mpv-summary.json').read_text())
assert all(not r['forward_errors'] and not r['reverse_errors'] and r['retained_after_seek_equal'] for r in mpv)
metal=[json.loads(s) for s in (out/'metal-results.jsonl').read_text().splitlines()]
assert all(r['command_status']==4 for r in metal if 'variant' in r)
assert all(r['max_channel_error']==0 for r in metal if 'variant' in r)
assert any(r.get('retina_drawable')==1 for r in metal)
assert any(r.get('native_source_render_command_status')==4 for r in metal)
cycles=[r for r in metal if 'native_lifecycle_cycle' in r]
assert len(cycles)==20 and all(r['drawable']==1 and r['same_source_texture']==1 and r['window_visible']==0 for r in cycles)
assert json.loads((out/'audio-sync-fixture.json').read_text())['native_lgpl_pcm_equals_cli_oracle']
rotation=next(f for f in fixtures if f['name']=='rotation')
assert any(s.get('rotation')==90 for s in rotation['probe']['streams'][0]['side_data_list'])
assert 'source_clockwise_rotation=-90' in (out/'ffmpeg-lgpl-rotation.log').read_text()
rotation_rows=[json.loads(s) for s in (out/'ffmpeg-lgpl-rotation.jsonl').read_text().splitlines()]
color_pts=[v['pts'] for v in next(f for f in fixtures if f['name']=='color')['frames']]
assert [r['pts'] for r in rotation_rows if r['phase']=='linear']==color_pts
assert 'Assertion failed' in (out/'mpv-rotation.log').read_text()
report={'native_decode':results,'mpv_paused_sweep_oracle_equal':True,'mpv_playback_frame_authority_proven':False,
    'metal_measurements':metal,'G1':'REVISE: capability components exercised, integrated semantic/clock/presentation qualification and owner limits remain open'}
(out/'evidence-verification.json').write_text(json.dumps(report,indent=2))
print('Verified',sum(r['frames'] for r in results),'decoded PTS and',sum(len(r['predecessor_checks']) for r in results),'exact seek/predecessor PTS+pixel checks; G1 remains REVISE')
