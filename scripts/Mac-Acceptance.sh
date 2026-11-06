#!/bin/sh
# LF-MAC-DEV-036 development workflow. No normal-user paths or automatic reset.
set -eu
TASK_REPO=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
if [ "$#" -ne 2 ]; then
    echo 'Usage: sh scripts/Mac-Acceptance.sh initialize|check|launch "<absolute packaged executable>"' >&2
    exit 64
fi
ACTION=$1
EXECUTABLE=$2
case "$ACTION" in initialize|check|launch) ;; *) exit 64 ;; esac
DOTNET_COMMAND=${DOTNET_COMMAND:-dotnet}
export DOTNET_CLI_HOME="$TASK_REPO/work/dotnet-home"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
"$DOTNET_COMMAND" build "$TASK_REPO/tools/MacAcceptance" -c Release --disable-build-servers -m:1 -p:RestoreLockedMode=true >&2
MODE=$ACTION
if [ "$MODE" = launch ]; then MODE=check; fi
"$DOTNET_COMMAND" "$TASK_REPO/tools/MacAcceptance/bin/Release/net8.0/MacAcceptance.dll" "$MODE" "$TASK_REPO/acceptance/persistent" "$EXECUTABLE"
# Advisory preflight leases end here. The unchanged application reacquires its
# authoritative admission/ownership boundary and can still refuse a raced launch.
if [ "$ACTION" = launch ]; then exec "$EXECUTABLE" --data-root "$TASK_REPO/acceptance/persistent"; fi
