using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace OverlayApp;

internal enum ThemeMode { Dark, Light, System }

/// <summary>Colours, fonts and drawing helpers shared by the custom controls.</summary>
internal static class Theme
{
    // ---- Colours ------------------------------------------------------------------------------
    // The palette is swapped by Apply (dark/light theme, accent colour). Painted controls read these
    // properties every time they draw; colours copied into control properties (BackColor, ForeColor)
    // are remapped by Recolor.

    private sealed record Palette(bool IsDark, Color Background, Color Surface, Color SurfaceHover, Color Input,
        Color Border, Color Track, Color Text, Color TextMuted, Color Accent, Color AccentHover, Color AccentPressed,
        Color Danger, Color DangerHover, Color Success, Color Warning)
    {
        // Every role of a palette has a distinct colour, so Recolor can tell them apart.
        public Color[] Roles => new[] { Background, Surface, SurfaceHover, Input, Border, Track, Text, TextMuted,
            Accent, AccentHover, AccentPressed, Danger, DangerHover, Success, Warning };
    }

    /// <summary>Accent colours offered in Settings (the first is the default).</summary>
    public static readonly (string Name, Color Color)[] Accents =
    {
        ("Blue", Color.FromArgb(76, 141, 255)),
        ("Purple", Color.FromArgb(150, 110, 250)),
        ("Pink", Color.FromArgb(232, 84, 160)),
        ("Orange", Color.FromArgb(242, 128, 44)),
        ("Green", Color.FromArgb(38, 176, 104)),
        ("Teal", Color.FromArgb(22, 168, 178)),
    };

    private static Palette _palette = Build(dark: true, Accents[0].Color);

    private static Palette Build(bool dark, Color accent) => dark
        ? new Palette(true,
            Background: Color.FromArgb(24, 24, 28), Surface: Color.FromArgb(34, 34, 40), SurfaceHover: Color.FromArgb(44, 44, 52),
            Input: Color.FromArgb(28, 28, 33), Border: Color.FromArgb(56, 56, 66), Track: Color.FromArgb(62, 62, 72),
            Text: Color.FromArgb(240, 240, 245), TextMuted: Color.FromArgb(150, 150, 165),
            Accent: accent, AccentHover: Blend(accent, Color.White, 0.18f), AccentPressed: Blend(accent, Color.Black, 0.15f),
            Danger: Color.FromArgb(229, 72, 77), DangerHover: Color.FromArgb(240, 98, 102),
            Success: Color.FromArgb(61, 214, 140), Warning: Color.FromArgb(245, 180, 60))
        : new Palette(false,
            Background: Color.FromArgb(240, 240, 244), Surface: Color.FromArgb(255, 255, 255), SurfaceHover: Color.FromArgb(234, 234, 240),
            Input: Color.FromArgb(247, 247, 250), Border: Color.FromArgb(214, 214, 222), Track: Color.FromArgb(196, 196, 206),
            Text: Color.FromArgb(28, 28, 34), TextMuted: Color.FromArgb(104, 104, 118),
            Accent: accent, AccentHover: Blend(accent, Color.Black, 0.10f), AccentPressed: Blend(accent, Color.Black, 0.22f),
            Danger: Color.FromArgb(214, 58, 64), DangerHover: Color.FromArgb(228, 82, 88),
            Success: Color.FromArgb(24, 158, 94), Warning: Color.FromArgb(204, 132, 18));

    public static bool IsDark => _palette.IsDark;
    public static Color Background => _palette.Background;
    public static Color Surface => _palette.Surface;
    public static Color SurfaceHover => _palette.SurfaceHover;
    public static Color Input => _palette.Input;
    public static Color Border => _palette.Border;
    public static Color Track => _palette.Track;
    public static Color Text => _palette.Text;
    public static Color TextMuted => _palette.TextMuted;
    public static Color Accent => _palette.Accent;
    public static Color AccentHover => _palette.AccentHover;
    public static Color AccentPressed => _palette.AccentPressed;
    public static Color Danger => _palette.Danger;
    public static Color DangerHover => _palette.DangerHover;
    public static Color Success => _palette.Success;
    public static Color Warning => _palette.Warning;

    /// <summary>Raised after the palette changes and open windows have been recoloured.</summary>
    public static event EventHandler? Changed;

    /// <summary>Name used in settings for a colour picked with the colour picker (stored separately as hex).</summary>
    public const string CustomAccentName = "Custom";

    /// <summary>One of the preset accents by name, or the first one if the name isn't known.</summary>
    public static Color AccentColor(string? name)
    {
        foreach (var (accentName, color) in Accents)
            if (string.Equals(accentName, name, StringComparison.OrdinalIgnoreCase)) return color;
        return Accents[0].Color;
    }

    /// <summary>Text/icon colour that reads well on top of the accent (white, or near-black on a light accent).</summary>
    public static Color OnAccent => OnColor(Accent);

    public static Color OnColor(Color background) =>
        Luminance(background) > 0.55 ? Color.FromArgb(24, 24, 28) : Color.White;

    /// <summary>Relative luminance, 0 (black) .. 1 (white).</summary>
    public static double Luminance(Color c)
    {
        static double Channel(int v)
        {
            double x = v / 255.0;
            return x <= 0.03928 ? x / 12.92 : Math.Pow((x + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);
    }

    /// <summary>"#RRGGBB" for a colour.</summary>
    public static string ToHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    /// <summary>Parses "#RRGGBB", "RRGGBB", "#RGB" or "RGB" (case-insensitive, spaces ignored).</summary>
    public static bool TryParseHex(string? text, out Color color)
    {
        color = Color.Empty;
        if (text == null) return false;
        string hex = text.Trim().TrimStart('#').Trim();
        if (hex.Length == 3) hex = string.Concat(hex.Select(ch => new string(ch, 2)));
        if (hex.Length != 6 || !int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out int rgb)) return false;
        color = Color.FromArgb((rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);
        return true;
    }

    /// <summary>Whether Windows' app mode is light (Settings > Personalisation > Colours).</summary>
    public static bool WindowsUsesLightTheme()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int v && v != 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Switches the palette and recolours every open window. Does nothing if the colours are unchanged.</summary>
    public static void Apply(ThemeMode mode, Color accent)
    {
        bool dark = mode switch
        {
            ThemeMode.Light => false,
            ThemeMode.System => !WindowsUsesLightTheme(),
            _ => true,
        };
        accent = Color.FromArgb(accent.R, accent.G, accent.B); // opaque, unnamed
        var next = Build(dark, accent);
        // Recolor tells colours apart by value, so a custom accent mustn't equal another colour of the palette.
        var others = next.Roles.Skip(1).Where(c => c != next.Accent && c != next.AccentHover && c != next.AccentPressed).Select(c => c.ToArgb()).ToHashSet();
        for (int i = 0; i < 8 && (others.Contains(next.Accent.ToArgb()) || others.Contains(next.AccentHover.ToArgb()) || others.Contains(next.AccentPressed.ToArgb())); i++)
        {
            accent = Color.FromArgb(accent.R, accent.G, accent.B < 128 ? accent.B + 1 : accent.B - 1);
            next = Build(dark, accent);
        }
        if (next == _palette) return;

        var map = new Dictionary<int, Color>();
        var oldRoles = _palette.Roles;
        var newRoles = next.Roles;
        for (int i = 0; i < oldRoles.Length; i++) map.TryAdd(oldRoles[i].ToArgb(), newRoles[i]);
        _palette = next;

        foreach (Form form in Application.OpenForms) Recolor(form, map);
        Changed?.Invoke(null, EventArgs.Empty);
    }

    private static void Recolor(Control control, Dictionary<int, Color> map)
    {
        if (map.TryGetValue(control.BackColor.ToArgb(), out var back)) control.BackColor = back;
        if (map.TryGetValue(control.ForeColor.ToArgb(), out var fore)) control.ForeColor = fore;

        if (control.IsHandleCreated)
        {
            if (control is Form) ApplyTitleBar(control.Handle);
            if (control is ListBox or CardStack) ApplyScrollbars(control.Handle);
        }
        foreach (Control child in control.Controls) Recolor(child, map);
        control.Invalidate();
    }

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
    public const string GlyphView = "";
    public const string GlyphSave = "";
    public const string GlyphEdit = "";
    public const string GlyphPreset = "";
    public const string GlyphSettings = "";
    public const string GlyphLock = "";
    public const string GlyphUnlock = "";
    public const string GlyphImport = "";
    public const string GlyphExport = "";
    public const string GlyphBack = "";
    // No "hidden" glyph: Segoe MDL2 on older Windows 10 builds lacks one (U+ED1A), so it's drawn as
    // GlyphView with a slash (DrawSlash).

    /// <summary>
    /// Draws a diagonal slash over a glyph, e.g. to turn the eye into "hidden". A wider stroke in
    /// <paramref name="background"/> underneath cuts a clean gap through the glyph.
    /// </summary>
    public static void DrawSlash(Graphics g, RectangleF glyph, Color color, Color background, float width)
    {
        var mode = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var from = new PointF(glyph.Left, glyph.Top);
        var to = new PointF(glyph.Right, glyph.Bottom);
        using (var gap = new Pen(background, width * 3)) g.DrawLine(gap, from, to);
        using (var pen = new Pen(color, width)) g.DrawLine(pen, from, to);
        g.SmoothingMode = mode;
    }

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

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

    /// <summary>Dark or light title bar to match the theme (Windows 10 20H1+ uses attribute 20, older builds 19).</summary>
    public static void ApplyTitleBar(IntPtr hwnd)
    {
        int on = IsDark ? 1 : 0;
        if (DwmSetWindowAttribute(hwnd, 20, ref on, sizeof(int)) != 0)
            DwmSetWindowAttribute(hwnd, 19, ref on, sizeof(int));
        // Windows 10 only repaints the caption in the new colours when its active state is redrawn, so
        // redraw it (without changing which window is active) and refresh the frame.
        const uint NoSizeMoveZorderActivate = 0x0001 | 0x0002 | 0x0004 | 0x0010, FrameChanged = 0x0020;
        const int WM_NCACTIVATE = 0x0086;
        IntPtr active = GetForegroundWindow() == hwnd ? (IntPtr)1 : IntPtr.Zero;
        SendMessage(hwnd, WM_NCACTIVATE, active == IntPtr.Zero ? (IntPtr)1 : IntPtr.Zero, IntPtr.Zero);
        SendMessage(hwnd, WM_NCACTIVATE, active, IntPtr.Zero);
        SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, NoSizeMoveZorderActivate | FrameChanged);
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    /// <summary>Dark or light scrollbars for native controls such as ListBox.</summary>
    public static void ApplyScrollbars(IntPtr hwnd) => SetWindowTheme(hwnd, IsDark ? "DarkMode_Explorer" : "Explorer", null);
}
