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

## Updates
- **Startup check:** when the app starts, it asks GitHub for the latest release of
  [JasonOVHolt/Application-Overlay](https://github.com/JasonOVHolt/Application-Overlay/releases). The check uses
  GitHub's public API and runs in the background, so it never delays startup.
- **Update banner:** if the release is newer, a banner offers **Download** (opens the release page), **Skip this version**,
  or ✕ to hide it until next time.
- **Manual check:** **Check for updates** at the top right checks right away and always reports the result, including
  versions you skipped.
- **Turning it off:** switch off **Check for updates on startup** if you don't want the app to contact GitHub. The current
  version is shown next to the button.

### Publishing a release
The app compares its own `<Version>` (in `src/OverlayApp.csproj`) with the newest release's tag. The tag can be written
`V2.1`, `v2.1.0` or just `2.1`; drafts and pre-releases are ignored. To release:
1. Bump `<Version>` in `src/OverlayApp.csproj` (e.g. `2.1` → `2.2`).
2. Build the standalone exe (see below).
3. On GitHub, create a release tagged with that version (e.g. `V2.2`) and attach `dist\OverlayApp.exe`.

Everyone running an older version sees the banner the next time they start the app. The repository it checks is
`<UpdateRepository>` in the same file.

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
   Each side can crop up to 98%. Opposite sides can't overlap: at least 2% always stays visible.
   For a precise area, such as a small corner, press **Select area**. It shows the whole source live. Drag a rectangle around
   the part you want, move or resize it by its edges and corners, and use the arrow keys to nudge it one pixel at a time
   (Shift+arrows resize). The overlay updates as you drag. **Apply** or Enter keeps it; **Cancel** or Esc puts it back.
   Use the buttons in the card header to fit the overlay to its source's shape or to close it. You can also close an overlay with the ✕ in
   the list or the Delete key.
4. Turn on **Overlay mode** or press the toggle hotkey from any app (default **Ctrl+Alt+O**). This locks **all** overlays and makes them
   click-through. Press the hotkey again to unlock them. To change the hotkey, click the **Toggle hotkey** box and press a new combination.
   Your choice is saved to `%AppData%\OverlayApp\settings.json`.
