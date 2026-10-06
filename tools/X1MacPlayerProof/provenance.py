import hashlib,json,os,pathlib,subprocess
root=pathlib.Path(__file__).resolve().parents[2];out=root/'docs/research/mac-compatibility/x1-player'
def run(args,env=None):
    p=subprocess.run(args,capture_output=True,text=True,env=env);return {'command':args,'returncode':p.returncode,'stdout':p.stdout,'stderr':p.stderr}
def digest(p):return hashlib.sha256(p.read_bytes()).hexdigest()
env=os.environ.copy();env['DYLD_LIBRARY_PATH']=str(root/'work/deps/lib')
bottles=json.loads((root/'work/deps/bottles/provenance.json').read_text())
inventory=[]
for name,d in bottles.items():
    files=d['bottle']['stable']['files'];b=files.get('arm64_tahoe') or files.get('arm64_sequoia') or files.get('all')
    inventory.append({'name':name,'version':d['versions']['stable'],'revision':d['revision'],'license':d.get('license'),'dependencies':d['dependencies'],'bottle':b,'formula_source':d.get('ruby_source_path'),'formula_sha256':d.get('ruby_source_checksum')})
(out/'dependency-provenance.json').write_text(json.dumps(inventory,indent=2))
environment={'date':'2026-10-05','workspace':str(root),'main':'e302a20d888161d6face6e311610d67eab9c1512','branch':'codex/368-mac-player-proof',
 'model':'MacBook Pro Mac14,9','chip':'Apple M2 Pro (12 CPU / 19 GPU cores)','ram_gib':32,'macos':'26.6.2 (25G83)','process_arch':'arm64',
 'dotnet':'No dotnet on PATH or standard /usr/local/share/dotnet, /opt/homebrew/share/dotnet, ~/.dotnet paths. Native C/Objective-C proof does not use .NET.',
 'xcode':run(['xcodebuild','-version']),'clt':run(['xcode-select','-p']),'clang':run(['clang','--version']),'swift':run(['swift','--version']),
 'metal_codec':run([str(root/'work/codec_probe')]),'fixture_ffmpeg':run(['ffmpeg','-version']),
 'mpv':run([str(root/'work/mpv_probe'),'--version']),
 'mpv_source':run(['git','-C',str(root/'work/deps/mpv'),'rev-parse','HEAD']),
 'controlled_ffmpeg_source':run(['git','-C',str(root/'work/deps/ffmpeg-source'),'rev-parse','HEAD']),
 'mpv_original_bottle_sha256':digest(root/'work/deps/bottles/mpv.tar.gz'),
 'mpv_relocated_dylib_sha256':digest(root/'work/deps/bottles/opt/mpv/lib/libmpv.2.dylib'),
 'relocation':'Only task-owned Mach-O install names changed to @rpath, linked through task-local lib path; ad-hoc signed. Original downloaded bottle SHA verified before extraction. Not a signed/notarized distribution artifact.'}
(out/'environment.json').write_text(json.dumps(environment,indent=2))
print(environment['mpv']['stdout']);print('Saved sanitized environment and dependency provenance')
