#!/usr/bin/env bash
# Installs Window Overlay for the current user (no root needed):
#   ~/.local/share/window-overlay/   the app
#   ~/.local/bin/window-overlay      launcher
#   app menu entry and icon
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
data="${XDG_DATA_HOME:-$HOME/.local/share}"
bin="$HOME/.local/bin"

# Check dependencies first and say exactly what's missing.
if ! python3 - <<'PY' 2>/dev/null
import gi
gi.require_version("Gtk", "3.0"); gi.require_version("Gst", "1.0"); gi.require_foreign("cairo")
from gi.repository import Gst
Gst.init(None)
assert all(Gst.ElementFactory.find(e) for e in ("ximagesrc", "videoconvert", "appsink"))
PY
then
  echo "Missing dependencies. Install them with your package manager:"
  echo "  Debian/Ubuntu/Mint: sudo apt install python3-gi python3-gi-cairo gir1.2-gtk-3.0 gir1.2-gstreamer-1.0 gir1.2-gst-plugins-base-1.0 gstreamer1.0-plugins-base gstreamer1.0-plugins-good gstreamer1.0-x"
  echo "  Fedora:             sudo dnf install python3-gobject python3-cairo gtk3 gstreamer1-plugins-base gstreamer1-plugins-good"
  echo "  Arch/Manjaro:       sudo pacman -S python-gobject python-cairo gtk3 gst-plugins-base gst-plugins-good"
  echo "Then run this installer again."
  exit 1
fi

mkdir -p "$data/window-overlay" "$bin" "$data/applications" "$data/icons/hicolor/256x256/apps"
rm -rf "$data/window-overlay/window_overlay"
cp -r "$here/window_overlay" "$data/window-overlay/"
find "$data/window-overlay" -name __pycache__ -type d -prune -exec rm -rf {} +

cat > "$bin/window-overlay" <<LAUNCHER
#!/usr/bin/env sh
PYTHONPATH="$data/window-overlay\${PYTHONPATH:+:\$PYTHONPATH}" exec python3 -m window_overlay "\$@"
LAUNCHER
chmod +x "$bin/window-overlay"

cp "$here/data/window-overlay.desktop" "$data/applications/"
cp "$here/data/window-overlay.png" "$data/icons/hicolor/256x256/apps/"
command -v update-desktop-database >/dev/null && update-desktop-database "$data/applications" || true
command -v gtk-update-icon-cache >/dev/null && gtk-update-icon-cache -q "$data/icons/hicolor" || true

echo "Installed. Start it from your app menu, or run: window-overlay"
case ":$PATH:" in *":$bin:"*) ;; *) echo "Note: $bin is not on your PATH; add it to run 'window-overlay' from a terminal." ;; esac
