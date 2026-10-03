#!/usr/bin/env bash
# Removes what install.sh added. Settings in ~/.config/window-overlay are kept.
set -eu
data="${XDG_DATA_HOME:-$HOME/.local/share}"
rm -rf "$data/window-overlay"
rm -f "$HOME/.local/bin/window-overlay" "$data/applications/window-overlay.desktop" \
      "$data/icons/hicolor/256x256/apps/window-overlay.png"
echo "Uninstalled. (Settings kept in ${XDG_CONFIG_HOME:-$HOME/.config}/window-overlay)"
