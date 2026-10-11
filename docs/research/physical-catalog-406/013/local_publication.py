"""Internal task-owned APFS publication control. No physical-drive writes."""
import ctypes, fcntl, json, os, pathlib, uuid
root = pathlib.Path.cwd() / 'work' / ('apfs-publication-' + str(uuid.uuid4()))
root.mkdir()
lib = ctypes.CDLL('/usr/lib/libSystem.B.dylib', use_errno=True)
fd = os.open(root, os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW)
results = []
def rename(source, target, flags):
    ctypes.set_errno(0)
    rc = lib.renameatx_np(fd, source.encode(), fd, target.encode(), ctypes.c_uint(flags))
    results.append({'source': source, 'target': target, 'flags': flags, 'return': rc,
                    'errno': ctypes.get_errno() if rc else 0})
    return rc
try:
    stage = root / 'stage';stage.mkdir()
    with (stage / 'synthetic.bin').open('xb') as f:
        f.write(b'LF013 qualified-local namespace control');f.flush();os.fsync(f.fileno());fcntl.fcntl(f.fileno(),51)
    identity = stage.stat().st_ino
    assert rename('stage','published',4) == 0
    assert (root/'published').stat().st_ino == identity
    (root/'collision-stage').mkdir()
    assert rename('collision-stage','published',4) == -1 and results[-1]['errno'] == 17
    assert (root/'published'/'synthetic.bin').read_bytes() == b'LF013 qualified-local namespace control'
    for name in ['file-stage','file-existing']:
        with (root/name).open('xb') as f:f.write(name.encode());f.flush();os.fsync(f.fileno())
    assert rename('file-stage','file-published',4) == 0
    assert rename('file-existing','file-published',4) == -1 and results[-1]['errno'] == 17
    os.fsync(fd);fcntl.fcntl(fd,51)
finally:os.close(fd)
output={'results':results,'namespace_identity_preserved':True,'collision_bytes_preserved':True,
        'directory_fsync':0,'directory_fullfsync':0,'hardware_durability':'UNQUALIFIED'}
with (pathlib.Path.cwd()/'work/evidence/apfs-publication.json').open('x') as f:json.dump(output,f,indent=2)
print(json.dumps(output))
