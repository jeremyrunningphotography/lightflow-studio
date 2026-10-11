"""LF-MAC-RES-013 physical research. Run only with reviewed private baseline and plan.
Creates one exclusive root, retains everything, never ejects or recursively deletes.
Not a production transfer adapter; no hostile namespace-race guarantee.
"""
import base64, ctypes, fcntl, hashlib, json, os, pathlib, platform, plistlib
import sqlite3, stat, subprocess, sys, time

workspace = pathlib.Path.cwd()
evidence = workspace / 'work/evidence'
baseline = json.loads((evidence / 'safety-baseline-private.json').read_text())
root = pathlib.Path(baseline['proposed_root'])
mount = root.parent
libc = ctypes.CDLL('/usr/lib/libSystem.B.dylib', use_errno=True)
log = {'agent': 'LF-MAC-RES-013', 'python': platform.python_version(),
       'sqlite': sqlite3.sqlite_version, 'native': [], 'checks': [], 'timings_ms': {}}
root_fd = None
children = []

def check(name, value):
    log['checks'].append({'name': name, 'passed': bool(value)})
    if not value:
        raise RuntimeError('STOP: ' + name)

def native(name, action):
    start = time.monotonic()
    try:
        action()
        result = {'operation': name, 'return': 0, 'errno': 0}
    except OSError as e:
        result = {'operation': name, 'return': -1, 'errno': e.errno, 'error': os.strerror(e.errno)}
    result['ms'] = (time.monotonic() - start) * 1000
    log['native'].append(result)
    return result

def digest(path):
    with path.open('rb') as stream:
        return hashlib.sha256(stream.read()).hexdigest()

def allocation():
    objects = [root, *root.rglob('*')]
    return sum(p.lstat().st_blocks * 512 for p in objects)

def guard(extra=0):
    # Persistent private identifiers are equality checks, never publication authority.
    v = plistlib.loads(subprocess.check_output(['diskutil', 'info', '-plist', str(mount)]))
    for key in ['VolumeUUID', 'DiskUUID', 'FilesystemType', 'Size', 'WritableVolume']:
        if v[key] != baseline['volume'][key]:
            raise RuntimeError('STOP changed volume facts: ' + key)
    if root_fd is not None:
        a, b = os.fstat(root_fd), root.lstat()
        if (a.st_dev, a.st_ino) != (b.st_dev, b.st_ino) or root.resolve() != root:
            raise RuntimeError('STOP root substitution')
        n = json.loads(subprocess.check_output([str(workspace / 'work/root-safety'), str(root)]))
        if n['alias'] or n['readonly'] or not n['local'] or n['volumeUUID'] != v['VolumeUUID']:
            raise RuntimeError('STOP native root containment')
        if allocation() + extra > baseline['footprint_budget']:
            raise RuntimeError('STOP footprint')

def write(relative, data):
    guard(2 * 1048576)
    parts = pathlib.PurePosixPath(relative).parts
    if '..' in parts or pathlib.PurePosixPath(relative).is_absolute():
        raise RuntimeError('STOP unsafe name')
    parent = root.joinpath(*parts[:-1])
    if parent.resolve() != parent or parent.stat().st_dev != os.fstat(root_fd).st_dev:
        raise RuntimeError('STOP parent substitution')
    fd = os.open(relative, os.O_CREAT | os.O_EXCL | os.O_WRONLY | os.O_NOFOLLOW, 0o600, dir_fd=root_fd)
    try:
        view = memoryview(data)
        while view:
            view = view[os.write(fd, view):]
        os.fsync(fd)
    finally:
        os.close(fd)

def mkdir(name):
    guard(2 * 1048576)
    os.mkdir(name, 0o700, dir_fd=root_fd)

def rename_native(api, source, target, flags):
    guard()
    ctypes.set_errno(0)
    if api == 'renameatx_np':
        result = libc.renameatx_np(root_fd, source.encode(), root_fd, target.encode(), ctypes.c_uint(flags))
    else:
        result = libc.renamex_np(os.fsencode(root / source), os.fsencode(root / target), ctypes.c_uint(flags))
    if result:
        raise OSError(ctypes.get_errno(), 'native rename')

def main():
    global root_fd
    guard()
    check('proposed root absent and direct child', not os.path.lexists(root) and root.parent == mount and mount.resolve() == mount)
    # Exactly one new root, no recursive creation or replacement.
    mount_fd = os.open(mount, os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW)
    try:
        os.mkdir(root.name, 0o700, dir_fd=mount_fd)
        root_fd = os.open(root.name, os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW, dir_fd=mount_fd)
    finally:
        os.close(mount_fd)
    guard()
    write('OWNERSHIP.json', json.dumps({'agent': log['agent'], 'root': root.name, 'limit': baseline['footprint_budget'], 'synthetic_only': True}).encode())
    write('exclusive.bin', b'LF013 exclusive')
    check('O_EXCL collision refuses', native('openat O_EXCL existing file', lambda: write('exclusive.bin', b'wrong'))['errno'] == 17)
    mkdir('exclusive-directory')
    check('mkdir collision refuses', native('mkdirat existing directory', lambda: mkdir('exclusive-directory'))['errno'] == 17)
    write('rename-source', b'same volume rename')
    guard()
    native('renameat same-volume absent target', lambda: os.rename('rename-source', 'renamed', src_dir_fd=root_fd, dst_dir_fd=root_fd))
    check('rename readback', (root / 'renamed').read_bytes() == b'same volume rename')
    write('no-replace-file', b'no replace source')
    mkdir('no-replace-directory')
    for api in ['renameatx_np', 'renamex_np']:
        for source, target in [('no-replace-file', 'no-replace-file-final'), ('no-replace-directory', 'no-replace-directory-final')]:
            result = native(api + ' RENAME_EXCL ' + source, lambda api=api, source=source, target=target: rename_native(api, source, target, 4))
            if result['return'] == 0:
                raise RuntimeError('STOP unexpected capability; review before further writes')
    check('unsupported no-replace preserves staging', (root / 'no-replace-file').read_bytes() == b'no replace source' and (root / 'no-replace-directory').is_dir())
    write('replacement-old', b'prior synthetic object')
    write('replacement-new', b'new synthetic object')
    with (root / 'replacement-old').open('rb') as held:
        old_identity = os.fstat(held.fileno())
        guard()
        native('renameat replacement owned existing target', lambda: os.rename('replacement-new', 'replacement-old', src_dir_fd=root_fd, dst_dir_fd=root_fd))
        check('old descriptor retains prior bytes', held.read() == b'prior synthetic object')
        current = (root / 'replacement-old').stat()
        changed = (old_identity.st_dev, old_identity.st_ino) != (current.st_dev, current.st_ino)
        check('replacement changed object identity', changed)
        check('revoked identity cleanup refused; evidence retained', changed and (root / 'replacement-old').read_bytes() == b'new synthetic object')
    write('swap-first', b'first'); write('swap-second', b'second')
    native('renameatx_np RENAME_SWAP', lambda: rename_native('renameatx_np', 'swap-first', 'swap-second', 2))
    with (root / 'exclusive.bin').open('r+b') as f:
        native('file fsync', lambda: os.fsync(f.fileno()))
        native('file F_FULLFSYNC=51', lambda: fcntl.fcntl(f.fileno(), 51))
    native('directory fsync', lambda: os.fsync(root_fd))
    native('directory F_FULLFSYNC=51', lambda: fcntl.fcntl(root_fd, 51))
    guard()
    native('symlinkat within owned root', lambda: os.symlink('exclusive.bin', 'test-symlink', dir_fd=root_fd))
    guard()
    native('linkat within owned root', lambda: os.link('exclusive.bin', 'test-hardlink', src_dir_fd=root_fd, dst_dir_fd=root_fd, follow_symlinks=False))
    # New lifecycle gap: a long-lived reader pins a WAL checkpoint, then process termination
    # of a specifically owned uncommitted writer recovers without hardware interruption.
    source = evidence / 'local-snapshot.db'
    write('lifecycle.db', source.read_bytes())
    guard(8 * 1048576)
    c = sqlite3.connect(str(root / 'lifecycle.db'), timeout=0.2)
    reader = None
    try:
        check('WAL FULL', c.execute('pragma journal_mode=wal').fetchone()[0] == 'wal')
        c.execute('pragma synchronous=FULL'); c.execute('pragma wal_autocheckpoint=0')
        check('synchronous FULL=2', c.execute('pragma synchronous').fetchone()[0] == 2)
        reader = sqlite3.connect(str(root / 'lifecycle.db'), timeout=0.2)
        reader.execute('begin');before = reader.execute('select note from identity').fetchone()[0]
        c.execute("update identity set note='committed LF013'");c.commit()
        check('reader snapshot excludes concurrent committed writer', reader.execute('select note from identity').fetchone()[0] == before)
        busy = c.execute('pragma wal_checkpoint(TRUNCATE)').fetchone()
        log['reader_held_checkpoint'] = list(busy)
        check('reader-held checkpoint reports busy', busy[0] == 1)
        reader.rollback();reader.close();reader = None
        check('checkpoint after reader release', c.execute('pragma wal_checkpoint(TRUNCATE)').fetchone() == (0, 0, 0))
    finally:
        if reader is not None: reader.close()
        c.close()
    guard(8 * 1048576)
    code = "import sqlite3,sys,time; c=sqlite3.connect(sys.argv[1]); c.execute('pragma synchronous=FULL'); c.execute('begin immediate'); c.execute(\"update identity set note='UNCOMMITTED'\"); print('OWNED_UNCOMMITTED_READY',flush=True); time.sleep(30)"
    child = subprocess.Popen([sys.executable, '-B', '-c', code, str(root / 'lifecycle.db')], stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
    children.append(child)
    check('specific synthetic child at controlled transaction boundary', child.stdout.readline().strip() == 'OWNED_UNCOMMITTED_READY')
    child.kill(); out, err = child.communicate(timeout=5)
    check('owned uncommitted process terminated and reaped', child.returncode == -9 and not err)
    guard(8 * 1048576)
    c = sqlite3.connect(str(root / 'lifecycle.db'))
    try:
        check('restart integrity', c.execute('pragma integrity_check').fetchone()[0] == 'ok')
        check('committed state survives and uncommitted state absent', c.execute('select note from identity').fetchone()[0] == 'committed LF013')
        check('source CatalogId preserved', c.execute('select catalogid from identity').fetchone()[0] == json.loads((evidence / 'source-identity.json').read_text())['CatalogId'])
        check('recovery checkpoint', c.execute('pragma wal_checkpoint(TRUNCATE)').fetchone() == (0, 0, 0))
        # SQLite-aware backup into exclusively preclaimed new synthetic file.
        write('managed-backup.db', b'')
        backup = sqlite3.connect(str(root / 'managed-backup.db'))
        try:
            c.backup(backup);backup.execute('pragma journal_mode=DELETE')
            check('backup integrity and committed state', backup.execute('pragma integrity_check').fetchone()[0] == 'ok' and backup.execute('select note from identity').fetchone()[0] == 'committed LF013')
        finally: backup.close()
    finally: c.close()
    backup_hash = digest(root / 'managed-backup.db')
    write('restored-closed.db', (root / 'managed-backup.db').read_bytes())
    restored = sqlite3.connect((root / 'restored-closed.db').as_uri() + '?mode=ro&immutable=1', uri=True)
    try: check('closed restored state', restored.execute('pragma integrity_check').fetchone()[0] == 'ok' and restored.execute('select note from identity').fetchone()[0] == 'committed LF013')
    finally: restored.close()
    # Use canonical bytes produced by the unchanged accepted C# format implementation.
    plans = json.loads((evidence / 'format-plan.json').read_text())
    names = {}
    for scenario, plan in plans.items():
        directory = plan['Directory']; names[scenario] = directory; mkdir(directory)
        write(directory + '/pending.json', base64.b64decode(plan['Pending']))
        if scenario == 'interrupted':
            write('interrupted.journal', base64.b64decode(plan['Journal']))
            child_code = "import sys,time; print('CLOSED_HANDLES_READY',flush=True); time.sleep(30)"
            child = subprocess.Popen([sys.executable, '-B', '-c', child_code], stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True);children.append(child)
            check('interrupted transfer child has closed handles', child.stdout.readline().strip() == 'CLOSED_HANDLES_READY')
            child.kill();child.communicate(timeout=5)
        data = source.read_bytes()
        if scenario in ['partial', 'cancelled', 'interrupted']: data = data[:1024]
        if scenario == 'hash-mismatch': data = bytes([data[0] ^ 1]) + data[1:]
        write(directory + '/catalog.snapshot', data)
        if scenario not in ['cancelled', 'interrupted']:
            manifest = base64.b64decode(plan['Manifest'])
            if scenario == 'truncated-manifest': manifest = manifest[:20]
            write(directory + '/manifest.json', manifest)
        if scenario not in ['missing-ready', 'cancelled', 'interrupted']:
            write(directory + '/ready.json', base64.b64decode(plan['Ready']))
    write('torn.journal', base64.b64decode(plans['interrupted']['Journal'])[:-5])
    log['scenarios'] = names
    good_before = digest(root / names['good'] / 'catalog.snapshot')
    check('destination collision refuses and preserves prior good bytes', native('O_EXCL prior-good-container payload collision', lambda: write(names['good'] + '/catalog.snapshot', b'wrong'))['errno'] == 17 and digest(root / names['good'] / 'catalog.snapshot') == good_before)
    check('managed backup preserved through failures', digest(root / 'managed-backup.db') == backup_hash)
    check('cancelled staging retained without readiness', not (root / names['cancelled'] / 'ready.json').exists())
    check('physical artifacts not active Catalog filename', all(not (root / n / 'LightflowCatalog.db').exists() for n in names.values()))
    log['companions_final'] = {p.name: p.stat().st_size for p in root.glob('lifecycle.db-*')}
    guard()
    files = []
    for p in sorted(root.rglob('*')):
        s = p.lstat()
        if stat.S_ISREG(s.st_mode): files.append({'relative': str(p.relative_to(root)), 'bytes': s.st_size, 'allocated': s.st_blocks * 512, 'sha256': digest(p)})
    log['files'] = files
    log['allocated_before_manifest'] = allocation()
    write('FINAL-MANIFEST.json', json.dumps(log, indent=2).encode())
    log['allocated_final'] = allocation()
    log['logical_final'] = sum(p.stat().st_size for p in root.rglob('*') if p.is_file())
    log['classification'] = 'CONDITIONAL research only'

try:
    main()
except Exception as error:
    log['classification'] = 'STOP'
    log['error'] = str(error)
finally:
    for child in children:
        if child.poll() is None: child.kill(); child.wait(timeout=5)
    if root_fd is not None: os.close(root_fd)
    with (evidence / 'physical-013-private.json').open('x') as output: json.dump(log, output, indent=2)
    print(log['classification'], len(log['checks']), 'checks', log.get('allocated_final'), log.get('error', ''))
sys.exit(1 if log['classification'] == 'STOP' else 0)
