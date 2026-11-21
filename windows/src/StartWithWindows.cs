using Microsoft.Win32;

namespace OverlayApp;

/// <summary>"Start with Windows": a value under the current user's Run key that launches this exe at sign-in.</summary>
internal static class StartWithWindows
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "WindowOverlay";

    /// <summary>Command-line flag added to the Run entry, so the app knows it was started at sign-in.</summary>
    public const string Argument = "--startup";

    private static string Command => $"\"{Environment.ProcessPath}\" {Argument}";

    /// <summary>True when the Run entry exists and starts this copy of the app.</summary>
    public static bool IsEnabled
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKey);
                return string.Equals(key?.GetValue(ValueName) as string, Command, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>Adds or removes the Run entry. Returns false if the registry couldn't be changed.</summary>
    public static bool Set(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (enabled) key.SetValue(ValueName, Command);
            else key.DeleteValue(ValueName, throwOnMissingValue: false);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
