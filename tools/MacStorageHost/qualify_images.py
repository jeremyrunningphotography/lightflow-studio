"""Bounded native image checks through the production library. Never force-detach or touch real disks."""
import ctypes
import hashlib
import json
import pathlib
import subprocess
import uuid

root = pathlib.Path(__file__).resolve().parents[2]
work = root / "work" / ("lf006-images-" + uuid.uuid4().hex)
work.mkdir(parents=True)
lib = ctypes.CDLL(str(root / "Lightflow.Platform.MacOS/native/libLightflowStorage.dylib"))
lib.lf_storage_create.restype = ctypes.c_void_p
lib.lf_storage_destroy.argtypes = [ctypes.c_void_p]
lib.lf_storage_probe.argtypes = [ctypes.c_void_p, ctypes.c_char_p, ctypes.c_int]
lib.lf_storage_probe.restype = ctypes.c_void_p
lib.lf_storage_free.argtypes = [ctypes.c_void_p]
lib.lf_storage_active_descriptors.restype = ctypes.c_int
results = []

def command(*args):
    completed = subprocess.run(args, cwd=root, capture_output=True, text=True, timeout=40)
    results.append({"command": list(args), "code": completed.returncode,
                    "stdout": completed.stdout, "stderr": completed.stderr})
    if completed.returncode:
        raise RuntimeError("Task-image command failed; inspect retained results")

def probe(context, path):
    result = lib.lf_storage_probe(context, str(path).encode(), 0)
    if not result:
        raise RuntimeError("No native facts")
    try:
        facts = json.loads(ctypes.string_at(result))
        results.append({"path": str(path), "evidence": "native production library", "facts": facts})
        if "error" in facts:
            raise RuntimeError(facts["error"])
        return facts
    finally:
        lib.lf_storage_free(result)

try:
    for label, filesystem in [("case-sensitive", "Case-sensitive APFS"), ("apfs", "APFS")]:
        image = work / (label + ".sparseimage")
        mount = work / (label + "-mount")
        mount.mkdir()
        command("hdiutil", "create", "-size", "128m", "-fs", filesystem, "-volname", "LF006Fixture", "-type", "SPARSE", "-o", str(image))
        mounted = False
        context = None
        try:
            command("hdiutil", "attach", str(image), "-nobrowse", "-mountpoint", str(mount))
            mounted = True
            first = mount / "Case.bin"
            first.write_bytes(b"LF006 immutable case fixture")
            if label == "case-sensitive":
                (mount / "case.bin").write_bytes(b"LF006 distinct case fixture")
            nfc = mount / "caf\u00e9.bin"
            nfc.write_bytes(b"LF006 immutable Unicode fixture")
            hashes = {p.name: hashlib.sha256(p.read_bytes()).hexdigest() for p in mount.iterdir() if p.is_file()}
            context = lib.lf_storage_create()
            volume = probe(context, mount)
            upper = probe(context, first)
            lower = probe(context, mount / "case.bin")
            unicode_a = probe(context, nfc)
            unicode_b = probe(context, mount / "cafe\u0301.bin")
            assert volume["locality"] == "Local"
            assert volume["caseSensitive"] == (label == "case-sensitive")
            assert (upper["target"] != lower["target"]) == (label == "case-sensitive")
            assert unicode_a["target"] == unicode_b["target"]
            assert hashes == {p.name: hashlib.sha256(p.read_bytes()).hexdigest() for p in mount.iterdir() if p.is_file()}
            lib.lf_storage_destroy(context)
            context = None
            assert lib.lf_storage_active_descriptors() == 0
            command("hdiutil", "detach", str(mount))
            mounted = False
            command("hdiutil", "attach", str(image), "-readonly", "-nobrowse", "-mountpoint", str(mount))
            mounted = True
            context = lib.lf_storage_create()
            readonly = probe(context, mount)
            assert readonly["write"] is False
            assert readonly["mountEpoch"] != volume["mountEpoch"]
            results.append({"name": label, "passed": True, "hashes": hashes,
                            "limitation": "Disk-image evidence; physical external storage and forced removal remain UNQUALIFIED."})
        finally:
            if context:
                lib.lf_storage_destroy(context)
            if mounted:
                command("hdiutil", "detach", str(mount))
    assert lib.lf_storage_active_descriptors() == 0
finally:
    (work / "results.json").write_text(json.dumps(results, indent=2))
    print(work / "results.json")
