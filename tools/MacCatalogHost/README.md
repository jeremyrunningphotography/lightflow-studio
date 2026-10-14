# Native Catalog lifecycle host

LF-MAC-DEV-015 / #425. Apple Silicon macOS, .NET 8 and Xcode command-line tools are required to build. This console host composes the shared production `CatalogLifecycle`; it has no Preview database or completion peer. Every reopen deeply validates the Catalog, retains Dirty profile evidence and Catalog Validated provenance.

Build from the repository root:

```sh
dotnet publish tools/MacCatalogHost/MacCatalogHost.csproj -c Release -p:RestoreLockedMode=true -o artifacts/lf015-native-host
```

Use a new disposable **absolute** data root on admitted local APFS. Prepare its empty Catalog directory before first creation. Existing Catalogs are never replaced by `create`.

```sh
mkdir -p "$PWD/artifacts/lf015-owner-acceptance/Catalog"
artifacts/lf015-native-host/MacCatalogHost --data-root "$PWD/artifacts/lf015-owner-acceptance" create
artifacts/lf015-native-host/MacCatalogHost --data-root "$PWD/artifacts/lf015-owner-acceptance" inspect
artifacts/lf015-native-host/MacCatalogHost --data-root "$PWD/artifacts/lf015-owner-acceptance" collection-add "Owner acceptance"
artifacts/lf015-native-host/MacCatalogHost --data-root "$PWD/artifacts/lf015-owner-acceptance" inspect
```

Each command closes through the shared lifecycle. Exit 0 means success, 2 means refusal, 64 means invalid arguments, 69 means unsupported architecture. `inspect` prints Catalog identity/schema, Collections, Assets and full integrity results. `hold` and diagnostic `lease-probe` serve the automated process harness; owner acceptance needs neither.

`restore <absolute backup>` consumes shared managed snapshot recovery within the explicit data root. Native backup/restore and safe rollback are covered by tests. Public `backup` uses the shared closed-transfer owner and currently refuses unverified capacity; `move <absolute destination>` refuses unqualified handle-bound publication. No filesystem rename fallback, Smart Collection query operation, root remapping, media import or Preview service is offered.

Run the independent-process qualification against a new root:

```sh
python3 tools/MacCatalogHost/qualification/lifecycle.py --dotnet "$(command -v dotnet)" --host "$PWD/artifacts/lf015-native-host/MacCatalogHost.dll" --data-root "$PWD/artifacts/lf015-process-qualification"
```

The harness owns and terminates only its spawned hosts. It compares all logical tables in fully closed standalone database files. Its small Collection fixture is distinct from the richer native xUnit authored-state fixture. See [implementation and qualification report](../../docs/qualification/native-catalog-425.md).
