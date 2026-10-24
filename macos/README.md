# Window Overlay for macOS

Native Swift app with the same features as the Windows version. It uses **ScreenCaptureKit** to capture a single window
(covered windows still mirror) and shows it in transparent, always-on-top panels that ignore the mouse while locked.

> **Status:** this version was written on a Windows PC and **has not been compiled or run yet**. Expect a first build on a
> Mac to possibly need small fixes. The snapping logic has unit tests (`swift test`) that mirror the Linux tests.

## Requirements
- macOS 13 Ventura or later.
- Xcode, or just the Command Line Tools (`xcode-select --install`), with Swift 5.9 or later.

## Build & run
From this `macos` folder:
```
bash build-app.sh
open "build/Window Overlay.app"
```
`UNIVERSAL=1 bash build-app.sh` builds one app for both Apple silicon and Intel Macs, which needs full Xcode. To run the
snapping tests:
```
swift test
```

### Screen Recording permission
The first time you open the app, macOS asks for **Screen Recording** permission. Allow it under **System Settings ›
Privacy & Security › Screen Recording**, then quit and reopen the app. Until you do, the window list is empty and the app
shows a banner with a button that opens that settings page.

The build is signed ad hoc, not with an Apple Developer ID. Because of that:
- **First launch:** the first time you open it from Finder you may need to right-click › **Open**.
- **After rebuilding:** macOS may ask for Screen Recording permission again.

## Usage
- **Add overlays:** select a window and press **Add overlay**, or double-click it or press Return. Add the same window
  again to show another cropped part of it.
- **Move and resize:** while unlocked, drag an overlay to move it and drag its edges to resize. Clicking one selects it.
- **Overlay settings:** apply to the selected overlay: opacity and crop for each side (up to 98%; opposite sides
  can't overlap). The Windows version also has a drag-to-select "Select area" picker; it isn't on macOS yet.
- **Overlay mode:** the switch, or the global hotkey (default **⌃⌥O**), locks every overlay and makes them all click-through.
  Click the hotkey button and press a new combination to change it. Esc cancels and Delete clears it.
- **Snap to edges:** overlays snap to the menu bar, the Dock edge, the real screen edges, and each other.
  The setting is remembered.

## Limitations
- **No separate client area.** macOS doesn't separate a window's title bar from its content, so there's no "client area
  only" switch. Use **Crop › Top** to hide a title bar.
- **Minimised windows and other Spaces don't capture.** A source that's minimised to the Dock, or on another Space,
  doesn't produce frames. The overlay shows a message until the source is visible again. Covered windows do work.
- **Protected video may show black.** DRM-protected video (e.g. Apple TV+, Netflix in Safari) is blocked from capture by
  macOS.
- **Clicks don't reach the source.** The overlay shows the image only; clicks and keys never reach the source through it.
