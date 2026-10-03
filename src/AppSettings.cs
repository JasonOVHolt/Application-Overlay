using System.Text.Json;

namespace OverlayApp;

/// <summary>User settings persisted to %AppData%\OverlayApp\settings.json.</summary>
internal sealed class AppSettings
{
    public Keys ToggleHotkey { get; set; } = Keys.Control | Keys.Alt | Keys.O;

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
