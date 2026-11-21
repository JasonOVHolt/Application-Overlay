using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace OverlayApp;

internal static class Program
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessage(string name);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(int processId);

    private static readonly IntPtr HWND_BROADCAST = new(0xFFFF);
    private const int ASFW_ANY = -1;

    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        // One instance per copy of the exe: starting it again (e.g. while it sits in the notification
        // area) brings the running one to the front instead of fighting over the hotkeys.
        string id = "WindowOverlay-" + Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes((Environment.ProcessPath ?? "").ToLowerInvariant())))[..16];
        uint showMessage = RegisterWindowMessage(id);
        using var mutex = new Mutex(true, id, out bool firstInstance);
        if (!firstInstance)
        {
            AllowSetForegroundWindow(ASFW_ANY);
            PostMessage(HWND_BROADCAST, showMessage, IntPtr.Zero, IntPtr.Zero);
            return;
        }

        // The theme is applied before any window is built, since controls copy its colours as they're created.
        var settings = AppSettings.Load();
        Theme.Apply(settings.ThemeMode, settings.AccentColor());

        bool startedWithWindows = args.Contains(StartWithWindows.Argument, StringComparer.OrdinalIgnoreCase);
        Application.Run(new MainForm(settings, startedWithWindows, showMessage));
    }
}
