# LF-MAC-DEV-036 audio visibility parity qualification

Run from the task clone with three arguments: an immutable task fixture folder, a
NEW local data root, and an evidence JSON path. Every path must be beneath
`artifacts/lf036-audio-visibility`. The harness cannot accept personal media paths.

The mixed fixture uses JPEG/PNG, the previously qualified public Canon CR2 sample,
synthetic MP4/MOV, one deliberately corrupt MP4, MP3/WAV/FLAC and a text file,
repeated under Child; Other contains one JPEG. See fixture-matrix.json and the prior
Phase B fixture attribution. Audio files are fixture-generation outputs of the host
FFmpeg tool, not a product dependency. No shipping decoder or classification change.

Production Mac session + BrowserViewModel paths cover direct/cached/recursive reads,
refresh/reopen, every publication, visible-order selection, hidden-ID rejection,
rapid replacement after observed fingerprinting, source/thumbnail identity and
hash/mtime cache reuse. Raw Catalog repositories separately verify audio records,
authored ratings and Collections. Results distinguish cache/model projection timing
from actual display presentation. GUI smoke is separate.

Only disposable Catalog/cache/report outputs are written. No fixture file is mutated
by the harness. No Windows package qualification or Gate A PASS is implied.
