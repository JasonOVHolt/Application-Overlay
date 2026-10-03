# Window Overlay

Mirror any window into an always-on-top overlay. For example, keep a movie playing in a corner while you work in another app.
Overlays show the image only. When they're locked, clicks pass straight through to whatever is underneath.

| Platform | Folder | Built with | Status |
|---|---|---|---|
| Windows 10/11 | [`windows/`](windows/README.md) | C# / WinForms, DWM thumbnails | Tested. Standalone exe in `windows/dist/` |
| macOS 13+ | [`macos/`](macos/README.md) | Swift / SwiftUI, ScreenCaptureKit | Written but **not yet compiled**: needs a first build on a Mac |
| Linux (X11) | [`linux/`](linux/README.md) | Python / GTK 3, GStreamer | Window listing and snapping tested; GUI not yet run |

All three have the same features:
- **Multiple overlays:** run several at once, including several of the same window, each cropped to a different part.
- **Per-overlay settings:** opacity and crop for each overlay. Windows and Linux also have "client area only", which hides the title bar.
- **Overlay mode:** locks every overlay and makes them all click-through. A global hotkey toggles it (default Ctrl+Alt+O, or ⌃⌥O on
  macOS), and you can change the hotkey.
- **Snap to edges:** an optional setting that snaps overlays to screen edges (both the taskbar/Dock edge and the real screen edge)
  and to each other.

See each folder's README for how to build and run it, and for that platform's limitations.
