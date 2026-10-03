# Window Overlay

A small Windows app that mirrors any window into an always-on-top overlay. Use it to watch a
movie while you work in another app. The overlay only shows the image: in overlay mode, mouse
clicks go straight through to the app underneath.

## How it works
It uses the Windows **DWM thumbnail API**, the same thing that draws taskbar previews and Alt+Tab.
The compositor draws the source window's live image on the GPU. That means:
- video plays at full frame rate with almost no CPU cost
- the source window can be **covered** by other windows and still mirrors fine
- the source window must **not be minimised**, because Windows stops drawing minimised windows

## Build & run
Requires the .NET 8 SDK.
```
dotnet run --project src
```

To build a single standalone `dist\OverlayApp.exe` that runs without .NET installed:
```
dotnet publish src -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -o dist
```

## Usage
1. Pick a window from the list (Refresh if it's missing) and press **Start overlay**. You can also double-click the window in the list.
2. Move the overlay by dragging it, and resize it by dragging its edges.
3. Tick **Overlay mode** or press the toggle hotkey from any app (default **Ctrl+Alt+O**). This locks the overlay and makes it click-through.
   Press the hotkey again to unlock it. To change the hotkey, click the **Toggle hotkey** box and press a new combination.
   Your choice is saved to `%AppData%\OverlayApp\settings.json`.
4. Options: opacity, a "client area only" setting that hides the title bar, and crop per side, which is handy for cutting a
   browser's video area out of the page.
