using System.Text.Json;

namespace OverlayApp;

/// <summary>User settings persisted to %AppData%\OverlayApp\settings.json.</summary>
internal sealed class AppSettings
{
    /// <summary>Turns overlay mode (lock + click-through) on and off.</summary>
    public Keys ToggleHotkey { get; set; } = Keys.Control | Keys.Alt | Keys.O;

    /// <summary>Hides all overlays, or shows them again.</summary>
    public Keys HideHotkey { get; set; } = Keys.Control | Keys.Alt | Keys.H;

    /// <summary>Whether new overlays snap flush to monitor edges and other overlays (each overlay has its own setting).</summary>
    public bool SnapToEdges { get; set; }

    /// <summary>Ask GitHub for a newer release when the app starts.</summary>
    public bool CheckForUpdates { get; set; } = true;

    /// <summary>Release tag the user chose to skip; startup checks don't mention it again.</summary>
    public string? SkippedUpdateTag { get; set; }

    /// <summary>Colour theme of the app's windows.</summary>
    public ThemeMode ThemeMode { get; set; } = ThemeMode.Dark;

    /// <summary>Accent colour name (one of Theme.Accents, or "Custom" for <see cref="CustomAccent"/>).</summary>
    public string Accent { get; set; } = "Blue";

    /// <summary>The colour last picked with the colour picker, as "#RRGGBB" (kept even while a preset accent is chosen).</summary>
    public string? CustomAccent { get; set; }

    /// <summary>The accent colour to use, resolving "Custom".</summary>
    public Color AccentColor() =>
        Accent == Theme.CustomAccentName && Theme.TryParseHex(CustomAccent, out var custom) ? custom : Theme.AccentColor(Accent);

    /// <summary>How close (in 96-DPI pixels) an edge must be before an overlay snaps to it.</summary>
    public int SnapDistance { get; set; } = 20;

    /// <summary>How long an overlay waits for its closed window to reopen; 0 closes it straight away.</summary>
    public int ReconnectSeconds { get; set; } = 60;

    /// <summary>Opacity (percent) that new overlays start with.</summary>
    public int DefaultOpacity { get; set; } = 100;

    /// <summary>Whether new overlays start with "fade when the mouse is over it" on.</summary>
    public bool DefaultFadeOnHover { get; set; }

    /// <summary>Closing the main window hides it to the notification area instead of quitting.</summary>
    public bool KeepRunningInTray { get; set; }

    /// <summary>Saved overlay styles, in the order shown in the Presets tab.</summary>
    public List<OverlayPreset> Presets { get; set; } = new();

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OverlayApp", "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
        }
        catch
        {
            // Corrupt or unreadable settings: fall back to defaults.
        }
        return new AppSettings();
    }

    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    /// <summary>Writes these settings (with presets) to a file of the user's choice.</summary>
    public void ExportTo(string path) => File.WriteAllText(path, JsonSerializer.Serialize(this, WriteOptions));

    /// <summary>Reads settings exported by <see cref="ExportTo"/> (or a copy of settings.json).</summary>
    /// <exception cref="InvalidDataException">The file isn't a Window Overlay settings file.</exception>
    public static AppSettings ImportFrom(string path)
    {
        const string NotSettings = "it isn't a Window Overlay settings file.";
        string text = File.ReadAllText(path);
        try
        {
            using (var doc = JsonDocument.Parse(text))
            {
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object
                    || !(root.TryGetProperty(nameof(Presets), out _) || root.TryGetProperty(nameof(ToggleHotkey), out _)))
                    throw new InvalidDataException(NotSettings);
            }
            return JsonSerializer.Deserialize<AppSettings>(text) ?? throw new InvalidDataException(NotSettings);
        }
        catch (JsonException)
        {
            throw new InvalidDataException(NotSettings);
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, WriteOptions));
        }
        catch
        {
            // Not being able to save settings shouldn't break the app.
        }
    }
}
