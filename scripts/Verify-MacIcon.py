#!/usr/bin/env python3
"""Verify actual bundle metadata, native-decoded icon sizes and unmodified master pixels."""
import hashlib
import json
import plistlib
from pathlib import Path
import struct
import subprocess
import sys
import tempfile

bundle, evidence = map(Path, sys.argv[1:3])
evidence.mkdir(parents=True, exist_ok=True)
metadata = plistlib.loads((bundle / 'Contents/Info.plist').read_bytes())
assert metadata['CFBundleIconFile'] == 'LightflowStudio.icns'
icon = bundle / 'Contents/Resources' / metadata['CFBundleIconFile']
raw = icon.read_bytes()
assert raw[:4] == b'icns' and struct.unpack_from('>I', raw, 4)[0] == len(raw)
master = Path(__file__).resolve().parent.parent / 'Lightflow.Desktop.MacOS/Assets/lightflow-icon-master-1024.png'
assert hashlib.sha256(master.read_bytes()).hexdigest() == 'e3ff3bdda774e394790a89f210b5b7d7024ccf0c169511f1222c59d55ec7de5a'
with tempfile.TemporaryDirectory(prefix='icon-', dir=evidence) as temp:
    temp = Path(temp)
    decoded = temp / 'decoded.iconset'
    subprocess.run(['iconutil', '-c', 'iconset', str(icon), '-o', str(decoded)], check=True)
    sizes = {}
    for point in (16, 32, 128, 256, 512):
        for scale in (1, 2):
            name = f'icon_{point}x{point}' + ('@2x' if scale == 2 else '') + '.png'
            png = (decoded / name).read_bytes()
            assert png[:8] == b'\x89PNG\r\n\x1a\n'
            dimensions = struct.unpack_from('>II', png, 16)
            assert dimensions == (point * scale, point * scale), (name, dimensions)
            sizes[name] = dimensions
    # Native decode/reencode to the same bitmap format avoids mistaking PNG metadata
    # changes for pixel changes. These files never enter the app or review ZIP.
    for source, name in ((master, 'source.bmp'), (decoded / 'icon_512x512@2x.png', 'decoded.bmp')):
        subprocess.run(['sips', '-s', 'format', 'bmp', str(source), '--out', str(temp / name)], check=True, stdout=subprocess.DEVNULL)
    assert (temp / 'source.bmp').read_bytes() == (temp / 'decoded.bmp').read_bytes(), 'Master pixels changed'
report = {'status': 'PASS', 'icon': str(icon), 'icon_sha256': hashlib.sha256(raw).hexdigest(), 'sizes': sizes,
          'master_pixels': 'Native decoded 1024 representation exactly matches approved master bitmap',
          'transparency': 'Approved opaque composition retained; no alpha mask invented',
          'gui': 'Finder/Dock/switcher pending authorized visual capture; no cache reset performed'}
(evidence / 'report.json').write_text(json.dumps(report, indent=2) + '\n')
print('Mac icon verification PASS: 10 representations and unchanged 1024 master pixels')
