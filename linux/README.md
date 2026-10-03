# Window Overlay for Linux

Python + GTK 3 app with the same features as the Windows version. It captures a window with GStreamer's `ximagesrc` and
shows it in borderless, always-on-top windows. While locked, an overlay has an empty input region, so clicks pass through.

> **Status:** the window listing, X11 handling and snapping are tested (on Ubuntu 24.04 under WSL). The full GUI has not
> been run yet.

## Requirements
- **An X11 session.** See [Wayland](#wayland) below.
- **Python 3.8 or later**, with GTK 3 and GStreamer and their Python bindings. Most desktop distros ship these already.
  If anything is missing, `install.sh` lists it. To install them:

| Distro | Command |
|---|---|
| Debian / Ubuntu / Mint | `sudo apt install python3-gi python3-gi-cairo gir1.2-gtk-3.0 gir1.2-gstreamer-1.0 gir1.2-gst-plugins-base-1.0 gstreamer1.0-plugins-base gstreamer1.0-plugins-good gstreamer1.0-x` |
| Fedora | `sudo dnf install python3-gobject python3-cairo gtk3 gstreamer1-plugins-base gstreamer1-plugins-good` |
| Arch / Manjaro | `sudo pacman -S python-gobject python-cairo gtk3 gst-plugins-base gst-plugins-good` |

## Run
Run it straight from this folder:
```
sh window-overlay
```
Or install it for your user, which adds an app-menu entry and a `window-overlay` command. No root is needed:
```
bash install.sh
```
`bash uninstall.sh` removes it. Settings are kept in `~/.config/window-overlay/settings.json`.

Tests, which don't need GTK:
```
python3 -m unittest discover -s tests
```

## Usage
- **Add overlays:** select a window and press **Add overlay**, or double-click it or press Enter. Add the same window
  again to show another cropped part of it.
- **Move and resize:** while unlocked, drag an overlay to move it and drag its edges to resize. Clicking one selects it.
- **Overlay settings:** apply to the selected overlay: opacity, client area only, and crop for each side.
- **Overlay mode:** the switch, or the global hotkey (default **Ctrl+Alt+O**), locks every overlay and makes them all
  click-through. Click the hotkey box and press a new combination to change it. Esc cancels and Backspace clears it.
- **Snap to edges:** overlays snap to panel/dock edges, the real screen edges, and each other. The setting is remembered.

## Wayland
Wayland deliberately stops apps from capturing other apps' windows, placing their own windows anywhere, and grabbing global
hotkeys. Window Overlay therefore runs through **XWayland** on a Wayland session. There:
- **Only X11 apps can be mirrored.** Only apps that themselves run through XWayland show up in the list. Many apps can be
  started that way, e.g. Firefox with `MOZ_ENABLE_WAYLAND=0` or Chromium with `--ozone-platform=x11`.
- **The hotkey may only work while an X11 window has focus.**

For everything to work, log in to an **X11 / Xorg session**. On GNOME's login screen, pick "GNOME on Xorg" from the gear menu.

## Limitations
- **Minimised windows can't be captured.** The overlay shows "Source is minimised" until you restore the window.
- **Covered windows need a compositor.** A covered source mirrors correctly when a compositing window manager is running
  (GNOME, KDE, Xfce with compositing, picom…). Without a compositor, X11 doesn't keep the hidden parts of a window, so
  covered areas may show garbage.
- **Transparency needs a compositor too.** Overlay opacity and the transparent letterbox need a compositor. Without one,
  overlays are opaque with black bars.
- **Capture uses the CPU.** Each frame is copied (30 fps cap), so very large sources use more CPU than on Windows, where the
  compositor does the work on the GPU.
- **Clicks don't reach the source.** The overlay shows the image only; clicks and keys never reach the source through it.
