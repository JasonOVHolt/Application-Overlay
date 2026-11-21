namespace OverlayApp;

/// <summary>One running overlay: its window plus what the overlays list shows about it.</summary>
internal sealed class OverlayItem : IDisposable
{
    /// <summary>How long an overlay waits for its app to reopen a window (set from Settings; zero = don't wait).</summary>
    public static TimeSpan ReconnectWindow { get; set; } = TimeSpan.FromSeconds(60);

    public OverlayItem(int number, WindowInfo source, OverlayForm form)
    {
        Number = number;
        Form = form;
        SetSource(source);
    }

    public int Number { get; }
    public OverlayForm Form { get; }

    // What the overlay shows. These change when it reconnects to a reopened window.
    public IntPtr Source { get; private set; }
    public string Title { get; private set; } = "";
    public string ProcessName { get; private set; } = "";
    public string ClassName { get; private set; } = "";
    public Bitmap? Icon { get; private set; }

    /// <summary>Set once the overlay is being torn down, so close events don't remove it twice.</summary>
    public bool Removed { get; set; }

    /// <summary>When the source window closed, while waiting for the app to reopen it; otherwise null.</summary>
    public DateTime? WaitingSince { get; private set; }

    /// <summary>Windows that were already open when the source closed (never reconnected to).</summary>
    public HashSet<IntPtr> WindowsOpenAtLoss { get; private set; } = new();

    public TimeSpan WaitRemaining => WaitingSince is { } since ? ReconnectWindow - (DateTime.UtcNow - since) : TimeSpan.Zero;

    public void SetSource(WindowInfo source)
    {
        Source = source.Handle;
        Title = source.Title;
        ProcessName = source.ProcessName;
        ClassName = source.ClassName;
        // Own copy: the window list disposes its icons whenever it refreshes.
        Icon?.Dispose();
        Icon = source.Icon == null ? null : new Bitmap(source.Icon);
        WaitingSince = null;
    }

    public void StartWaiting(IEnumerable<IntPtr> openWindows)
    {
        Source = IntPtr.Zero;
        WaitingSince = DateTime.UtcNow;
        WindowsOpenAtLoss = openWindows.ToHashSet();
    }

    /// <summary>
    /// Temporarily hidden: the overlay window is hidden but keeps its source, position, size and
    /// settings, so showing it again puts it back exactly as it was.
    /// </summary>
    public bool Hidden
    {
        get => _hidden;
        set
        {
            if (_hidden == value) return;
            _hidden = value;
            if (value) Form.Hide();
            else Form.Show(); // ShowWithoutActivation: doesn't take focus from the app in use
        }
    }

    private bool _hidden;

    public string Summary
    {
        get
        {
            if (WaitingSince != null)
                return $"Overlay {Number} · waiting for {ProcessName} to reopen… {Math.Max(0, (int)Math.Ceiling(WaitRemaining.TotalSeconds))}s";
            var crop = Form.Crop;
            string text = $"Overlay {Number} · {(int)Math.Round(Form.OverlayOpacity * 100)}% opacity";
            if (crop.Left > 0 || crop.Top > 0 || crop.Right > 0 || crop.Bottom > 0) text += " · cropped";
            if (Form.ClickThrough) text += " · locked";
            if (Form.FadeOnHover) text += " · fades";
            if (Hidden) text += " · hidden";
            return text;
        }
    }

    public override string ToString() => $"Overlay {Number}: {Title}";

    public void Dispose()
    {
        Icon?.Dispose();
        Form.Dispose();
    }
}
