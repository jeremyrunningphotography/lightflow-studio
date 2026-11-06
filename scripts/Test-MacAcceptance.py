"""LF-MAC-DEV-036: destructive cases are confined to a NEW disposable fixture."""
import ctypes
import hashlib
import json
from pathlib import Path
import sqlite3
import subprocess
import sys
import uuid

repo = Path(__file__).resolve().parent.parent
exe = Path(sys.argv[1]).resolve()
dotnet = Path(sys.argv[2]).resolve()
base = repo / 'artifacts/lf036-acceptance-workflow/disposable'
root = base / ('preflight-' + uuid.uuid4().hex)
helper = repo / 'tools/MacAcceptance/bin/Release/net8.0/MacAcceptance.dll'
rows = []

def fingerprint(path):
    return {str(p.relative_to(path)): (hashlib.sha256(p.read_bytes()).hexdigest(), p.stat().st_mtime_ns)
            for p in path.rglob('*') if p.is_file()}

def invoke(mode='check', target=root, package=exe):
    return subprocess.run([str(dotnet), str(helper), mode, str(target), str(package)], capture_output=True, text=True)

def check(name, expected=0, **kw):
    before = fingerprint(root)
    result = invoke(**kw)
    assert (result.returncode == 0) == (expected == 0), (name, result.stdout, result.stderr)
    assert before == fingerprint(root), name + ': read-only preflight modified fixture'
    rows.append(dict(name=name, passed=True, exit=result.returncode, readOnly=True, diagnostic=result.stderr.strip()))
    return result

assert not root.exists()
created = invoke('initialize')
assert created.returncode == 0, created.stderr
identity = json.loads(created.stdout)['catalogId']
db = root / 'Lightflow Test/LightflowCatalog.db'
with sqlite3.connect(f'file:{db}?immutable=1', uri=True) as c:
    assert c.execute('SELECT count(*) FROM MediaAssets').fetchone()[0] == 0
rows.append(dict(name='supported first initialization creates empty Catalog', passed=True))
assert json.loads(check('compatible preflight').stdout)['catalogId'] == identity
check('initialize existing is read-only/idempotent', mode='initialize')

def mutate(sql):
    with sqlite3.connect(db) as c:
        c.execute(sql)

for schema in (18, 20, 0):
    mutate(f'PRAGMA user_version={schema}')
    check(f'refuse Catalog schema {schema}', expected=1)
mutate('PRAGMA user_version=19')
mutate('PRAGMA application_id=123')
check('refuse foreign application identity', expected=1)
mutate('PRAGMA application_id=1279677251')  # LFSC
settings = root / 'settings.json'
original = settings.read_bytes()
data = json.loads(original)
data['CatalogId'] = str(uuid.uuid4())
settings.write_text(json.dumps(data))
check('refuse mismatched configured identity', expected=1)
settings.write_bytes(original)
preview = root / 'Previews/previews.db'
with sqlite3.connect(preview) as c:
    c.execute('PRAGMA user_version=99')
    c.execute('PRAGMA application_id=1279676498')
check('refuse incompatible Preview schema', expected=1)
preview.unlink()  # Disposable failure fixture only.
wal = Path(str(db) + '-wal')
wal.write_bytes(b'uncertain recovery fixture')
check('refuse nonempty WAL without recovery', expected=1)
wal.unlink()
journal = Path(str(db) + '.startup-state')
original = journal.read_bytes()
journal.write_text('{}')
check('refuse uncertain ownership evidence', expected=1)
journal.write_bytes(original)
native = ctypes.CDLL(str(helper.parent / 'libLightflowStorage.dylib'))
native.lf_cat_open.argtypes = [ctypes.c_char_p, ctypes.c_int, ctypes.c_int, ctypes.c_int, ctypes.c_int]
fd = native.lf_cat_open(str(root / 'startup-session.state').encode(), 0, 0, 2, 0)
assert fd >= 0
try:
    check('refuse active profile owner', expected=1)
finally:
    ctypes.CDLL(None).close(fd)
partial = base / ('partial-' + uuid.uuid4().hex)
partial.mkdir()
result = invoke('initialize', partial)
assert result.returncode != 0 and list(partial.iterdir()) == []
rows.append(dict(name='partial existing root is never recreated', passed=True))
result = invoke('check', repo / 'acceptance/not-authorized')
assert result.returncode != 0 and not (repo / 'acceptance/not-authorized').exists()
rows.append(dict(name='refuse unauthorized data root', passed=True))
check('refuse unknown package', expected=1, package=repo / 'artifacts/missing-executable')
check('valid state preserved after refusal matrix')
print(json.dumps(dict(passed=True, tests=len(rows), catalogId=identity, disposableRoot=str(root),
                     persistentRootNeverTargeted=True, rows=rows), indent=2))
