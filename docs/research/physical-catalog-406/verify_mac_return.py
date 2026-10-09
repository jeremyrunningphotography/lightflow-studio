"""LF-MAC-RES-007 read-only return verifier; native physical identity is a caller gate."""
import argparse, hashlib, json, pathlib, sqlite3, stat, sys

p = argparse.ArgumentParser()
p.add_argument('--root', required=True)
p.add_argument('--output', required=True)
a = p.parse_args()
root = pathlib.Path(a.root).absolute()
output = pathlib.Path(a.output).absolute()
allowed_output = output.resolve() == output and output != root and root not in output.parents
here = pathlib.Path(__file__).resolve().parent
log = {'agent': 'LF-MAC-RES-007', 'assignment': 7, 'sqlite': sqlite3.sqlite_version,
       'scope': 'immutable synthetic fixture return, not product Catalog integration', 'checks': []}

def check(name, passed):
    log['checks'].append({'name': name, 'passed': bool(passed)})
    if not passed:
        raise RuntimeError('STOP: ' + name)

def digest(path):
    h = hashlib.sha256()
    with path.open('rb') as f:
        for block in iter(lambda: f.read(65536), b''):
            h.update(block)
    return h.hexdigest()

def snapshot(path):
    c = sqlite3.connect(path.as_uri() + '?mode=ro&immutable=1', uri=True)
    try:
        return {'Integrity': c.execute('pragma integrity_check').fetchone()[0],
                'Identity': [[None if v is None else str(v) for v in row]
                             for row in c.execute('select * from identity')],
                'Authored': [[None if v is None else str(v) for v in row]
                            for row in c.execute('select * from authored order by assetid')]}
    finally:
        c.close()

try:
    check('output canonical and outside physical task root', allowed_output)
    check('root no symlink / canonical', not root.is_symlink() and root.resolve() == root)
    check('root is existing directory', root.is_dir())
    device = root.stat().st_dev
    audit = json.loads((here / 'windows-file-audit.json').read_text())
    expected = json.loads((here / 'windows-summary.json').read_text())['details']
    def hashes(phase):
        for f in audit:
            relative = pathlib.PurePosixPath(f['relative'])
            check(phase + ' safe relative leaf ' + f['relative'],
                  not relative.is_absolute() and '..' not in relative.parts)
            path = root.joinpath(*relative.parts)
            for parent in [path, *path.parents]:
                if parent == root:
                    break
                check(phase + ' no linked path ' + f['relative'], not parent.is_symlink())
            s = path.lstat()
            check(phase + ' regular same-volume file ' + f['relative'],
                  stat.S_ISREG(s.st_mode) and s.st_dev == device)
            check(phase + ' length/hash ' + f['relative'],
                  s.st_size == f['bytes'] and digest(path) == f['sha256'])
    hashes('before')
    owner = json.loads((root / 'OWNERSHIP.json').read_text())
    check('historical ownership matches existing run', owner['agent'] == 'LF-BOTH-RES-007'
          and owner['run'] == root.name and owner['limit'] == 268435456)
    check('original Mac logical state', snapshot(root / 'closed-copy.db') == expected['baseline'])
    for name in ['closed-windows.db', 'windows-authored.db', 'backup.db', 'restored.db', 'local-return.db']:
        check(name + ' every row/identity/integrity',
              snapshot(root / 'LF-WIN-RES-007' / name) == expected['expected_final_state'])
    manifest = json.loads((root / 'LF-WIN-RES-007' / 'expected-state.json').read_text())
    check('physical authored manifest', manifest['baseline'] == expected['baseline']
          and manifest['expected'] == expected['expected_final_state'] and manifest['delta'] == expected['delta'])
    hashes('after')
    log['classification'] = 'CONDITIONAL fixture return; native identity and anchor audit recorded separately'
except Exception as e:
    log['classification'] = 'STOP'
    log['error'] = str(e)
finally:
    # Only a task-owned local output is allowed; never overwrite existing evidence.
    if not allowed_output:
        print(json.dumps(log, indent=2))
    else:
        with output.open('x', encoding='utf-8') as f:
            json.dump(log, f, indent=2)
    print(log['classification'], len(log['checks']), 'checks')
sys.exit(1 if log['classification'] == 'STOP' else 0)
