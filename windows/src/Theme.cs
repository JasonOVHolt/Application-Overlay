using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace OverlayApp;

/// <summary>Colours, fonts and drawing helpers shared by the custom controls.</summary>
internal static class Theme
{
    public static readonly Color Background = Color.FromArgb(24, 24, 28);
    public static readonly Color Surface = Color.FromArgb(34, 34, 40);
    public static readonly Color SurfaceHover = Color.FromArgb(44, 44, 52);
    public static readonly Color Input = Color.FromArgb(28, 28, 33);
    public static readonly Color Border = Color.FromArgb(56, 56, 66);
    public static readonly Color Track = Color.FromArgb(62, 62, 72);
    public static readonly Color Text = Color.FromArgb(240, 240, 245);
    public static readonly Color TextMuted = Color.FromArgb(150, 150, 165);
    public static readonly Color Accent = Color.FromArgb(76, 141, 255);
    public static readonly Color AccentHover = Color.FromArgb(104, 160, 255);
    public static readonly Color AccentPressed = Color.FromArgb(58, 120, 230);
    public static readonly Color Danger = Color.FromArgb(229, 72, 77);
    public static readonly Color DangerHover = Color.FromArgb(240, 98, 102);
    public static readonly Color Success = Color.FromArgb(61, 214, 140);
    public static readonly Color Warning = Color.FromArgb(245, 180, 60);

    public static readonly Font Body = new("Segoe UI", 9.5f);
    public static readonly Font BodyStrong = new("Segoe UI Semibold", 9.5f);
    public static readonly Font Small = new("Segoe UI", 8.5f);
    public static readonly Font Section = new("Segoe UI Semibold", 11f);
    public static readonly Font Title = new("Segoe UI Semibold", 16f);
    public static readonly Font Icon = new("Segoe MDL2 Assets", 10f);
    public static readonly Font IconLarge = new("Segoe MDL2 Assets", 14f);
    public static readonly Font IconSmall = new("Segoe MDL2 Assets", 7.5f);

    // Segoe MDL2 Assets glyphs
    public const string GlyphRefresh = "";
    public const string GlyphPlay = "";
    public const string GlyphStop = "";
    public const string GlyphFit = "";
    public const string GlyphSearch = "";
    public const string GlyphApp = "";
    public const string GlyphKeyboard = "";
    public const string GlyphReset = "";
    public const string GlyphAdd = "";
    public const string GlyphDelete = "";
    public const string GlyphCrop = "";
    public const string GlyphDownload = "";
    public const string GlyphClose = "";
    public const string GlyphSync = "";

    // ---- DPI ----------------------------------------------------------------------------------
    // Layout is written in 96-DPI ("100%") units and forms use AutoScaleMode.Dpi, so WinForms scales
    // bounds when a window opens on, or moves to, a monitor with other scaling. WinForms also rescales
    // each control's own Font. Two things are left to us: values computed while painting (use S), and
    // any extra font a control draws with besides its own Font (use Scaled).

    /// <summary>Scales a 96-DPI pixel value to the control's current DPI. For paint-time and runtime values only.</summary>
    public static int S(this Control c, int value) => (int)Math.Round(value * c.DeviceDpi / 96.0);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForSystem();

    /// <summary>DPI that GDI uses to turn font points into pixels in this process (the primary monitor's at sign-in).</summary>
    public static readonly int SystemDpi = (int)GetDpiForSystem();

    private static readonly Dictionary<(Font Font, int Dpi), Font> ScaledFonts = new();

    /// <summary>
    /// <paramref name="font"/> sized for the control's current monitor, matching how WinForms rescales a
    /// control's own Font (points are rendered at <see cref="SystemDpi"/>).
    /// </summary>
    public static Font Scaled(this Control c, Font font)
    {
        int dpi = c.DeviceDpi;
        if (dpi == SystemDpi) return font;
        if (!ScaledFonts.TryGetValue((font, dpi), out var scaled))
        {
            scaled = new Font(font.FontFamily, font.SizeInPoints * dpi / SystemDpi, font.Style, GraphicsUnit.Point);
            ScaledFonts[(font, dpi)] = scaled;
        }
        return scaled;
    }

    public static GraphicsPath RoundedRect(RectangleF r, float radius)
    {
        var path = new GraphicsPath();
        float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        if (d <= 0)
        {
            path.AddRectangle(r);
            return path;
        }
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    public static void FillRounded(Graphics g, Color color, RectangleF r, float radius)
    {
        using var path = RoundedRect(r, radius);
        using var brush = new SolidBrush(color);
        g.FillPath(brush, path);
    }

    public static void DrawRounded(Graphics g, Color color, RectangleF r, float radius, float width = 1f)
    {
        using var path = RoundedRect(r, radius);
        using var pen = new Pen(color, width);
        g.DrawPath(pen, path);
    }

    public static Color Blend(Color from, Color to, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return Color.FromArgb(
            (int)(from.A + (to.A - from.A) * t),
            (int)(from.R + (to.R - from.R) * t),
            (int)(from.G + (to.G - from.G) * t),
            (int)(from.B + (to.B - from.B) * t));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr hWnd, string? pszSubAppName, string? pszSubIdList);

    /// <summary>Dark title bar (Windows 10 20H1+ uses attribute 20, older builds 19).</summary>
    public static void UseDarkTitleBar(IntPtr hwnd)
    {
        int on = 1;
        if (DwmSetWindowAttribute(hwnd, 20, ref on, sizeof(int)) != 0)
            DwmSetWindowAttribute(hwnd, 19, ref on, sizeof(int));
    }

    /// <summary>Dark scrollbars for native controls such as ListBox.</summary>
    public static void UseDarkScrollbars(IntPtr hwnd) => SetWindowTheme(hwnd, "DarkMode_Explorer", null);
}
