using System.Runtime.InteropServices;
using System.Text.Json.Serialization;
using static OverlayApp.NativeMethods;

namespace OverlayApp;

/// <summary>
/// A saved overlay style: placement, size, crop, opacity and client-area setting, with no source window
/// attached, so it can be applied to any overlay.
/// </summary>
/// <remarks>
/// Placement is stored relative to the nearest corner of a monitor's work area, in 96-DPI units, so a
/// "bottom-right corner" preset stays in the bottom-right corner when that monitor's resolution, scaling
/// or taskbar changes. If the monitor is gone, the overlay's current monitor is used instead.
/// </remarks>
internal sealed class OverlayPreset
{
    public string Name { get; set; } = "";

    /// <summary>Screen.DeviceName of the monitor the overlay was on, e.g. \\.\DISPLAY2.</summary>
    public string Monitor { get; set; } = "";

    /// <summary>Anchored to the right/bottom edge of the work area rather than the left/top.</summary>
    public bool FromRight { get; set; }
    public bool FromBottom { get; set; }

    /// <summary>Distance from the anchored edges, and size, in 96-DPI units.</summary>
    public int OffsetX { get; set; }
    public int OffsetY { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }

    public double CropLeft { get; set; }
    public double CropTop { get; set; }
    public double CropRight { get; set; }
    public double CropBottom { get; set; }
    public double Opacity { get; set; } = 1;
    public bool ClientAreaOnly { get; set; } = true;

    // Older presets without this load with the default.
    public bool FadeOnHover { get; set; }

    /// <summary>Null in presets saved before snapping was a per-overlay setting (applying them leaves it as it is).</summary>
    public bool? SnapToEdges { get; set; }

    [JsonIgnore]
    public Crop Crop => new(CropLeft, CropTop, CropRight, CropBottom);

    /// <summary>Captures an overlay's current style.</summary>
    public static OverlayPreset Capture(string name, OverlayForm overlay)
    {
        var bounds = overlay.Bounds;
        var screen = Screen.FromRectangle(bounds);
        var area = screen.WorkingArea;
        double toLogical = 96.0 / DpiOf(bounds);

        bool fromRight = area.Right - bounds.Right < bounds.Left - area.Left;
        bool fromBottom = area.Bottom - bounds.Bottom < bounds.Top - area.Top;
        int offsetX = fromRight ? area.Right - bounds.Right : bounds.Left - area.Left;
        int offsetY = fromBottom ? area.Bottom - bounds.Bottom : bounds.Top - area.Top;

        var crop = overlay.Crop;
        return new OverlayPreset
        {
            Name = name,
            Monitor = screen.DeviceName,
            FromRight = fromRight,
            FromBottom = fromBottom,
            OffsetX = (int)Math.Round(offsetX * toLogical),
            OffsetY = (int)Math.Round(offsetY * toLogical),
            Width = (int)Math.Round(bounds.Width * toLogical),
            Height = (int)Math.Round(bounds.Height * toLogical),
            CropLeft = crop.Left,
            CropTop = crop.Top,
            CropRight = crop.Right,
            CropBottom = crop.Bottom,
            Opacity = overlay.OverlayOpacity,
            ClientAreaOnly = overlay.ClientAreaOnly,
            FadeOnHover = overlay.FadeOnHover,
            SnapToEdges = overlay.SnapToEdges,
        };
    }

    /// <summary>Applies everything except placement to <paramref name="overlay"/>.</summary>
    public void ApplyLook(OverlayForm overlay)
    {
        overlay.ClientAreaOnly = ClientAreaOnly;
        overlay.OverlayOpacity = Opacity;
        overlay.Crop = Crop;
        overlay.FadeOnHover = FadeOnHover;
        if (SnapToEdges is bool snap) overlay.SnapToEdges = snap;
    }

    /// <summary>Where the overlay should go now, in physical pixels, given the monitors connected right now.</summary>
    /// <param name="fallback">Used when the saved monitor isn't connected (normally the overlay's current bounds).</param>
    public Rectangle PlacementNow(Rectangle fallback)
    {
        var screen = Screen.AllScreens.FirstOrDefault(s => s.DeviceName == Monitor) ?? Screen.FromRectangle(fallback);
        var area = screen.WorkingArea;
        double toPixels = DpiOf(area) / 96.0;
        int Px(int logical) => (int)Math.Round(logical * toPixels);

        // Never bigger than the monitor, and kept on it even if it shrank.
        int width = Math.Min(Px(Width), screen.Bounds.Width);
        int height = Math.Min(Px(Height), screen.Bounds.Height);
        int x = FromRight ? area.Right - Px(OffsetX) - width : area.Left + Px(OffsetX);
        int y = FromBottom ? area.Bottom - Px(OffsetY) - height : area.Top + Px(OffsetY);
        x = Math.Clamp(x, screen.Bounds.Left, screen.Bounds.Right - width);
        y = Math.Clamp(y, screen.Bounds.Top, screen.Bounds.Bottom - height);
        return new Rectangle(x, y, width, height);
    }

    /// <summary>Short description for the presets list, e.g. "bottom-right · 640×360 · cropped · 80%".</summary>
    [JsonIgnore]
    public string Summary
    {
        get
        {
            string corner = $"{(FromBottom ? "bottom" : "top")}-{(FromRight ? "right" : "left")}";
            string text = $"{corner} · {Width}×{Height}";
            var screen = Screen.AllScreens.FirstOrDefault(s => s.DeviceName == Monitor);
            if (Screen.AllScreens.Length > 1)
                text += screen != null ? $" · display {DisplayNumber(Monitor)}" : " · display not connected";
            if (CropLeft > 0 || CropTop > 0 || CropRight > 0 || CropBottom > 0) text += " · cropped";
            if (Opacity < 0.995) text += $" · {(int)Math.Round(Opacity * 100)}%";
            if (FadeOnHover) text += " · fades";
            if (SnapToEdges == true) text += " · snaps";
            return text;
        }
    }

    public override string ToString() => Name; // list text and accessibility name

    /// <summary>"\\.\DISPLAY3" → "3".</summary>
    private static string DisplayNumber(string deviceName)
    {
        var digits = new string(deviceName.Reverse().TakeWhile(char.IsDigit).Reverse().ToArray());
        return digits.Length > 0 ? digits : deviceName;
    }

    /// <summary>The effective DPI of the monitor that contains most of <paramref name="r"/>.</summary>
    private static int DpiOf(Rectangle r)
    {
        var rect = new RECT(r.Left, r.Top, r.Right, r.Bottom);
        var monitor = MonitorFromRect(ref rect, MONITOR_DEFAULTTONEAREST);
        return GetDpiForMonitor(monitor, 0, out uint dpi, out _) == 0 && dpi > 0 ? (int)dpi : 96;
    }

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint dpiX, out uint dpiY);
}
