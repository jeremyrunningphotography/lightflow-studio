#!/bin/sh
# Bounded development bundle, unsigned and unnotarized. Run from the reviewed clone.
set -eu
if [ "$(uname -s)" != Darwin ] || [ "$(uname -m)" != arm64 ]; then
    echo "Build this development bundle on Apple Silicon macOS." >&2
    exit 64
fi
TASK_REPO=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
cd "$TASK_REPO"
DOTNET_COMMAND=${DOTNET_COMMAND:-dotnet}
OUTPUT="$TASK_REPO/artifacts/mac-shell"
BUNDLE="$OUTPUT/Lightflow Studio.app"
mkdir -p "$BUNDLE/Contents/MacOS" "$BUNDLE/Contents/Resources"
export DOTNET_CLI_HOME="$TASK_REPO/work/dotnet-home"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
"$DOTNET_COMMAND" publish Lightflow.Desktop.MacOS/Lightflow.Desktop.MacOS.csproj -c Release -r osx-arm64 --self-contained true --disable-build-servers -p:RestoreLockedMode=true -p:LightflowPublishLockRoot="$TASK_REPO/Lightflow.Desktop.MacOS/publish-locks" -o "$BUNDLE/Contents/MacOS"
python3 - "$BUNDLE" <<'PY'
import hashlib,json,plistlib,shutil,subprocess,sys
from pathlib import Path
bundle=Path(sys.argv[1]); contents=bundle/'Contents'
(contents/'Info.plist').write_bytes(plistlib.dumps(dict(CFBundleExecutable='LightflowStudio.Mac',CFBundleIdentifier='studio.lightflow.mac.development',CFBundleName='Lightflow Studio',CFBundleDisplayName='Lightflow Studio',CFBundlePackageType='APPL',CFBundleVersion='1',CFBundleShortVersionString='1.0.0',NSHighResolutionCapable=True,LSArchitecturePriority=['arm64'])))
licenses=contents/'Resources'/'ThirdParty'; licenses.mkdir(parents=True,exist_ok=True)
shutil.copy2('THIRD-PARTY-NOTICES.md',licenses/'THIRD-PARTY-NOTICES.md')
# Preserve the restored packages' original license/notice files and license declarations.
cache=Path('.cache/nuget/packages')
lock=json.loads(Path('Lightflow.Desktop.MacOS/publish-locks/Lightflow.Desktop.MacOS.publish.packages.lock.json').read_text())
for framework in lock['dependencies'].values():
    for name, package in framework.items():
        version=package.get('resolved')
        if not version: continue
        package_root=cache/name.lower()/version.lower()
        if not package_root.exists(): continue
        for source in package_root.rglob('*'):
            if source.is_file() and ('license' in source.name.lower() or 'notice' in source.name.lower() or source.suffix=='.nuspec'):
                target=licenses/name/version/source.relative_to(package_root); target.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(source,target)
files={str(p.relative_to(bundle)):hashlib.sha256(p.read_bytes()).hexdigest() for p in bundle.rglob('*') if p.is_file()}
provenance=dict(head=subprocess.check_output(['git','rev-parse','HEAD'],text=True).strip(),branch=subprocess.check_output(['git','branch','--show-current'],text=True).strip(),dirty=bool(subprocess.check_output(['git','status','--porcelain'],text=True)),files=files)
(bundle.parent/'provenance.json').write_text(json.dumps(provenance,indent=2)+'\n')
PY
printf 'Development bundle: %s\n' "$BUNDLE"
