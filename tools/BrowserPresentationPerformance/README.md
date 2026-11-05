# LF-MAC-DEV-036 cached Browser presentation measurements

Run from this task's independent clone with three arguments: a private JSON array of
`Alias` / `Path` folder objects, a **new** data root under
`artifacts/lf036-cached-presentation`, and a report path under that root.
A private `private/source-authorization.json` in that artifact root contains the exact
owner-authorized `folder`. Only that exact external folder and existing task-local
artifact fixtures are accepted. Do not commit private maps or source media.

The harness creates a disposable local Catalog, discovers read-only sources and warms
at most 192 previews. It measures three warm folder-to-folder visits and a reopen,
using the production BrowserViewModel/session and monotonic ActivitySource events.
Results distinguish first asset publication, first cached **projection**, and complete
reconciliation. They are not physical-display or cold-device-cache measurements.
The initial timing trial incorrectly counted a prior folder's notification; the
corrected reference comparison uses `baseline-repeat.json`. Never use the older
source-mutating ThumbnailQualification tool against owner files.

Set `LF036_NAV_CANCEL=1` to replace repeated visits with three navigation cancellations
after actual fingerprint work is observed, checking that the new scope wins.
All writes are Catalog/cache/log outputs beneath the task root. Sources are not
modified. Disposable mutation tests belong in the test projects' own fixture roots.
Baseline comparison can resolve verified original package components in a separate
process; never load original and corrected Objective-C libraries into one process.
