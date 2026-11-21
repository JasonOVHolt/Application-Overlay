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
- **Manual check:** **Check for updates** on the **Settings** page checks right away and always reports the result, including
  versions you skipped.
- **Turning it off:** switch off **Check for updates on startup** in **Settings** if you don't want the app to contact
  GitHub. The current version is shown at the top right and next to the button.

### Publishing a release
The app compares its own `<Version>` (in `src/OverlayApp.csproj`) with the newest release's tag. The tag can be written
`V2.1`, `v2.1.0` or just `2.1`; drafts and pre-releases are ignored. To release:
1. Bump `<Version>` in `src/OverlayApp.csproj` (e.g. `2.2` → `3.0`).
2. Build the standalone exe (see below).
3. On GitHub, create a release tagged with that version (e.g. `V3.0`) and attach `dist\OverlayApp.exe`.

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
   and the selected overlay has an outline in the accent colour. Placement is free by default. Turn on **Snap to edges** for an
   overlay (in **Overlay settings** or its right-click menu) to have it stick flush to a monitor's edge when you drag or resize
   it near one (20 px by default; see **Snap distance** in **Settings**). Overlays snap both to the top of the taskbar and to
   the very bottom of the screen, whichever is closer. They also snap to each other: flush side by side, and with their edges
   lined up when they're next to each other. New overlays start with the **New overlays snap to edges** setting.
   **Right-click** an unlocked overlay, or any overlay in the **Overlays** list (locked or not), for quick options: lock or
   unlock, fit to the source's shape, select area / remove crop, opacity, fade, snap to edges, client area only, save as
   preset, hide or show, and close.
   While overlays are unlocked you can also fine-tune them:
   - **Arrow keys** move the selected overlay 1 px (hold Shift for 10 px).
   - **Shift + drag an edge** resizes it while keeping the source's shape, so there are no black bars.
   - **Mouse wheel** over an overlay changes its opacity in 5% steps; **Ctrl + wheel** makes it bigger or smaller around its centre.
3. **Overlay settings** apply to the selected overlay: opacity, a "client area only" setting that hides the title bar, and crop per side.
   Each side can crop up to 98%. Opposite sides can't overlap: at least 2% always stays visible.
   For a precise area, such as a small corner, press **Select area**. It shows the whole source live. Drag a rectangle around
   the part you want, move or resize it by its edges and corners, and use the arrow keys to nudge it one pixel at a time
   (Shift+arrows resize). The overlay updates as you drag. **Apply** or Enter keeps it; **Cancel** or Esc puts it back.
   **Fade when the mouse is over it** (per overlay): while overlays are locked, moving the mouse over this overlay fades it
   almost out of sight, so you can see and click what's underneath. It comes back when the mouse leaves.
   Use the buttons in the card header to fit the overlay to its source's shape or to close it. You can also close an overlay with the ✕ in
   the list or the Delete key.
4. **Hide overlays for a moment** without closing them:
   - Press **Hide all**, or the hide/show hotkey from any app (default **Ctrl+Alt+H**), to hide every overlay.
     Press either again (the button then reads **Show all**) to bring them all back exactly as they were.
   - Use the eye button on an overlay in the **Overlays** list to hide or show just that one. Hidden overlays are dimmed
     and marked "hidden".
   - Hidden overlays keep their position, size, crop and opacity, and are skipped when other overlays snap.
5. **Presets** save an overlay's look so you can reuse it on any overlay. A preset holds the overlay's position, size, crop,
   opacity, "client area only" and fade settings, but not which window it shows.
   - **Saving:** select an overlay, then press the save icon in **Overlay settings**, or **Save selected overlay** on the
     **Presets** tab, and give the preset a name.
   - **Applying:** open the **Presets** tab (next to **Windows**). Select the overlay to change, then double-click a preset
     (or select it and press Enter, or use its ▶ button).
   - **Managing:** each preset has rename (✎, or F2) and delete (✕, or the Delete key) buttons. The search box filters
     presets by name.
   - **Placement across setups:** a preset remembers its monitor and the corner it was nearest. Applying it puts the
     overlay at the same distance from that corner even if the monitor's resolution, scaling or taskbar has changed. If
     that monitor isn't connected, the overlay's current monitor is used.
   - Presets are saved in `%AppData%\OverlayApp\settings.json`.
6. **If the source window closes**, its overlay waits for a while (1 minute by default; change it in **Settings**) for that app to open a new window (for example a video
   player that restarts) and reconnects to it automatically, keeping its position, size and crop. It prefers a window
   with the same title and never switches to a window that was already open. If nothing reopens in time, the overlay closes.
7. **Locking** makes an overlay click-through, so clicks reach whatever is underneath. Each overlay locks on its own: use the
   padlock in the **Overlays** list, **Lock this overlay** in Overlay settings, or **Lock** in its right-click menu.
   **Overlay mode** (or its hotkey from any app, default **Ctrl+Alt+O**) locks **all** overlays; pressing it again unlocks
   them all. If only some are locked, the hotkey locks the rest. New overlays start unlocked so you can place them.
8. **Settings** (top right) holds the app-wide options. Changes apply straight away and are saved to
   `%AppData%\OverlayApp\settings.json`. Press **Back** or Esc to return.
   - **Appearance:** **Dark**, **Light** or **Match Windows** (follows Windows' light/dark app mode, even while running),
     and an accent colour for buttons, switches and the selected overlay's outline. The last swatch (the rainbow ring)
     opens a colour picker for any colour: drag in the square and the hue bar, or type a hex code such as `#FF8800`.
     The app previews the colour as you pick; **Cancel** puts the old one back.
   - **Overlays:** how close an edge must be before an overlay snaps; how long a closed window's overlay waits for it to
     reopen (**Off** closes the overlay straight away); and the opacity, fade and snap settings new overlays start with.
   - **Hotkeys:** click a box and press a new combination for overlay mode or hide/show. Esc cancels, Backspace clears.
   - **Startup:** **Start with Windows** opens the app when you sign in. **Keep running in the notification area** makes the
     close button hide the window instead of quitting, so overlays and hotkeys keep working; click the icon by the clock to
     open the app again, or right-click it for overlay mode, hide/show, Settings and **Exit**.
   - **Updates**.
   - **Backup:** **Export…** saves your settings and presets to a `.json` file; **Import…** loads one (for example on
     another PC). Importing uses that file's settings and adds its presets, replacing yours that have the same name.
     "Start with Windows" isn't included, since it belongs to each PC.
   - **Reset** puts every setting back to its default; presets are kept.

   Starting the app while it's already running brings up the running copy instead of opening a second one.
