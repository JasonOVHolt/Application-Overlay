# Window Overlay for Windows

A small Windows app that mirrors any window into an always-on-top overlay. Use it to watch a
movie while you work in another app. The overlay only shows the image: in overlay mode, mouse
clicks go straight through to the app underneath.

**Quick start:** run `dist\OverlayApp.exe`. It's a single standalone file, so no install is needed and .NET doesn't have to be installed.

## How it works
It uses the Windows **DWM thumbnail API**, the same thing that draws taskbar previews and Alt+Tab.
The compositor draws the source window's live image on the GPU. That means:
- video plays at full frame rate with almost no CPU cost
- the source window can be **covered** by other windows and still mirrors fine
- the source window must **not be minimised**, because Windows stops drawing minimised windows

## Limitations
- **Minimised windows can't be mirrored.** Windows stops drawing a window while it's minimised, so its overlay goes blank.
  Leave the source open behind your other windows instead. It can be fully covered.
- **Exclusive fullscreen games won't mirror.** A game in exclusive fullscreen bypasses the desktop compositor. Switch the game to
  **borderless windowed** (sometimes called "fullscreen windowed") and it will work.
- **Protected streaming video may show black.** Services like Netflix, Disney+ and Prime Video can block their video from being
  captured, especially in Edge and Chrome with hardware acceleration on. Turning off hardware acceleration in the browser sometimes
  helps, but not always.
- **The overlay shows the image only.** Clicks and keys never reach the source window through it. To control the source,
  interact with the source window itself.

## Build & run
Requires Windows 10 or 11 and the .NET 8 SDK. Run these commands from this `windows` folder.
```
dotnet run --project src
```

To build a single standalone `dist\OverlayApp.exe` that runs without .NET installed:
```
dotnet publish src -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -o dist
```

## Usage
1. Pick a window from the list (Refresh if it's missing) and press **Add overlay**. You can also double-click it or press Enter.
   Repeat to add as many overlays as you like. Adding the same window more than once lets you show different cropped parts of it,
   such as a video and a chat panel from one browser window.
2. Move each overlay by dragging it, and resize it by dragging its edges. Clicking an overlay selects it in the **Overlays** list,
   and the selected overlay has a blue border. Placement is free by default. Turn on **Snap to edges** to have overlays stick flush
   to a monitor's edge when you drag or resize them within about 20 px of it. Overlays snap both to the top of the taskbar and to
   the very bottom of the screen, whichever is closer. They also snap to each other: flush side by side, and with their edges
   lined up when they're next to each other. This setting is remembered.
3. **Overlay settings** apply to the selected overlay: opacity, a "client area only" setting that hides the title bar, and crop per side.
   Use the buttons in the card header to fit the overlay to its source's shape or to close it. You can also close an overlay with the ✕ in
   the list or the Delete key.
4. Turn on **Overlay mode** or press the toggle hotkey from any app (default **Ctrl+Alt+O**). This locks **all** overlays and makes them
   click-through. Press the hotkey again to unlock them. To change the hotkey, click the **Toggle hotkey** box and press a new combination.
   Your choice is saved to `%AppData%\OverlayApp\settings.json`.
