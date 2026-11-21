# Window Overlay

A Windows app that mirrors any window into an always-on-top overlay. For example, keep a movie playing in a corner while you
work in another app. Overlays show the image only. When they're locked, clicks pass straight through to whatever is underneath.

**Download:** grab `OverlayApp.exe` from the [latest release](https://github.com/JasonOVHolt/Application-Overlay/releases/latest).
It's a single standalone file for Windows 10 and 11. Nothing needs installing.

Features:
- **Multiple overlays:** run several at once, including several of the same window, each cropped to a different part.
- **Per-overlay settings:** opacity, "client area only" (hides the title bar), and crop. Crop with sliders, or drag a
  rectangle around exactly the part you want.
- **Overlay mode:** locks every overlay and makes them click-through. Toggle it with a global hotkey (default Ctrl+Alt+O).
- **Hide and show:** hide every overlay with another global hotkey (default Ctrl+Alt+H), or hide them one at a time.
- **Snap to edges:** an optional setting that snaps overlays to screen edges, the taskbar and each other.
- **Update check:** the app tells you when a newer release is on GitHub.

The source, build steps and limitations are in [`windows/`](windows/README.md).
