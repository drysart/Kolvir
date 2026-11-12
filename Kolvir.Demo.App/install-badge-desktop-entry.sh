#!/bin/bash
# Installs the desktop entry the Linux dock needs in order to show the sample's app badge.
#
# GNOME's dock badges an application whose installed desktop entry matches the application id in the
# badge signal, and the demo's id is derived from its instance key: "Kolvir" becomes
# dev.kolvir.ikolvir. Without this entry the App badge page changes nothing on the dock, silently.
#
# Usage: ./install-badge-desktop-entry.sh [path-to-binary]
# Defaults to the Debug build this script sits beside.

set -euo pipefail

here="$(cd "$(dirname "$0")" && pwd)"
binary="${1:-$here/bin/Debug/net10.0/KolvirDemoApp}"

if [ ! -x "$binary" ]; then
    echo "no executable at $binary - build the sample first (dotnet build) or pass its path" >&2
    exit 1
fi

target="$HOME/.local/share/applications/dev.kolvir.ikolvir.desktop"
mkdir -p "$(dirname "$target")"

sed "s|^Exec=.*|Exec=$binary|" "$here/dev.kolvir.ikolvir.desktop" > "$target"

echo "installed $target"
echo "  Exec=$binary"
echo "Run the sample, open the App badge page, and the count appears on the dock icon."
