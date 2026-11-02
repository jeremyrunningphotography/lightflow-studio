#!/bin/sh
# Format/resolution conversion of the unchanged approved v4 master. No crop, mask or padding.
set -eu
TASK_REPO=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
OUTPUT=$1
ICONSET="$OUTPUT/LightflowStudio.iconset"
mkdir -p "$ICONSET"
SOURCE="$TASK_REPO/Lightflow.Desktop.MacOS/Assets/lightflow-icon-master-1024.png"
for size in 16 32 128 256 512; do
    /usr/bin/sips -z "$size" "$size" "$SOURCE" --out "$ICONSET/icon_${size}x${size}.png" >/dev/null
    retina=$((size * 2))
    if [ "$retina" -eq 1024 ]; then
        cp "$SOURCE" "$ICONSET/icon_${size}x${size}@2x.png"
    else
        /usr/bin/sips -z "$retina" "$retina" "$SOURCE" --out "$ICONSET/icon_${size}x${size}@2x.png" >/dev/null
    fi
done
/usr/bin/iconutil -c icns "$ICONSET" -o "$OUTPUT/LightflowStudio.icns"
