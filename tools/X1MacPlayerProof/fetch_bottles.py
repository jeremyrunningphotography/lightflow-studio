"""Task-local Homebrew bottle extraction; never installs into Homebrew."""
import hashlib, json, pathlib, subprocess, urllib.request
root = pathlib.Path(__file__).resolve().parents[2] / 'work/deps/bottles'
root.mkdir(parents=True, exist_ok=True)
seen = {}
pinfile=pathlib.Path(__file__).resolve().parents[2]/'docs/research/mac-compatibility/x1-player/dependency-provenance.json'
pins={d['name']:d for d in json.loads(pinfile.read_text())} if pinfile.exists() else {}
def get(url, headers=None):
    return urllib.request.urlopen(urllib.request.Request(url, headers=headers or {})).read()
def fetch(name):
    if name in seen: return
    if name in pins:
        p=pins[name];data={'versions':{'stable':p['version']},'revision':p['revision'],'dependencies':p['dependencies'],'license':p['license'],
            'bottle':{'stable':{'files':{'arm64_tahoe':p['bottle']}}},'ruby_source_path':p['formula_source'],'ruby_source_checksum':p['formula_sha256']}
    else:
        data = json.loads(get('https://formulae.brew.sh/api/formula/' + name + '.json'))
    seen[name] = data
    for dep in data['dependencies']: fetch(dep)
    files = data['bottle']['stable']['files']
    bottle = files.get('arm64_tahoe') or files.get('arm64_sequoia') or files.get('all')
    if not bottle: raise RuntimeError('No arm64 bottle: ' + name)
    url = bottle['url']
    scope = 'repository:homebrew/core/' + name.replace('@', '/') + ':pull'
    token = json.loads(get('https://ghcr.io/token?service=ghcr.io&scope=' + scope))['token']
    archive = root / (name + '.tar.gz')
    raw = archive.read_bytes() if archive.exists() else get(url, {'Authorization': 'Bearer ' + token})
    assert hashlib.sha256(raw).hexdigest() == bottle['sha256']
    archive.write_bytes(raw)
    subprocess.run(['tar', '-xzf', str(archive), '-C', str(root)], check=True)
    print(name, data['versions']['stable'], bottle['sha256'], flush=True)
fetch('mpv')
(root / 'provenance.json').write_text(json.dumps(seen, indent=2))
