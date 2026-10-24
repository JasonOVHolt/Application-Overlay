using System.Text.Json;

namespace OverlayApp;

/// <summary>User settings persisted to %AppData%\OverlayApp\settings.json.</summary>
internal sealed class AppSettings
{
    public Keys ToggleHotkey { get; set; } = Keys.Control | Keys.Alt | Keys.O;

    /// <summary>Overlays snap flush to monitor edges when dragged or resized near them.</summary>
    public bool SnapToEdges { get; set; }

    /// <summary>Ask GitHub for a newer release when the app starts.</summary>
    public bool CheckForUpdates { get; set; } = true;

    /// <summary>Release tag the user chose to skip; startup checks don't mention it again.</summary>
    public string? SkippedUpdateTag { get; set; }

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

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // Not being able to save settings shouldn't break the app.
        }
    }
}
