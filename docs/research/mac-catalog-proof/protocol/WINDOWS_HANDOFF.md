# Deferred Windows handoff — selected B2 contract

Do not execute yet. The selected direction requires a minimal central authority/publication service contract; no service implementation or deployable adapter exists in this proof. The Python oracle is not such a service and must never be used as central authority for real Catalogs.

Preserve the earlier [Windows fixture](../WINDOWS_NAS_FIXTURE_HANDOFF.md) and [round-trip manifest](../CROSS_OS_ROUNDTRIP_MANIFEST.json). They retain the 32-asset authored-state reference and expected identity invariants, but their standalone backup transfer is not protocol qualification.

After owner acceptance of B2 and a separately authorized disposable service/adapter proof:

1. Use a fresh isolated Windows checkout/data root under the repository's private-desktop rules. No SSH or remote desktop automation by X2.
2. Both clients use the versioned platform-neutral contract in STATE_MODEL.json. Select one disposable authority; verify CatalogId/schema/payload hash. Never treat UNC and Mac mount strings as authority identity.
3. Mac acquires A/epoch E, opens local WAL, makes a recorded authored delta and publishes generation G with acknowledgement. Cleanly release.
4. Windows acquires B/epoch E+1, verifies all AssetIds/RootIds and authored tables, opens local WAL, makes the one agreed delta, publishes G+1, acknowledges and releases.
5. Mac reacquires, verifies exact G+1 identity/hash/all authored rows, then exercise the reverse-origin sequence.
6. Hold the former owner after its last validation, perform explicit fenced takeover, then resume its pending publication. Require rejection by the authority at the mutation boundary; merely returning an application error before a filesystem rename is insufficient.
7. Cover same-machine second instance, both mixed-host acquisition directions, ambiguous acknowledgements, stale/wrong parent, crash/restart, interrupted/corrupt upload, protected-mode transitions and explicit previous-generation recovery. Preserve current/previous and independent backups.
8. Treat real NAS/server/network/power interruptions as separate owner-authorized disposable tests. Capture client/service/storage versions and server durable acknowledgement behavior. No elapsed-time SLA is preapproved.

Pass requires no stale promotion, no dual admitted writer, no silent identity/authoring loss, exact publication idempotency and honest local-versus-central save reporting. RootId/case/Unicode/symlink disposition remains independently required. Preview/cache never participates in central generations. Neither this handoff nor B2 closes G2.
