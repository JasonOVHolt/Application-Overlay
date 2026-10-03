namespace OverlayApp;

/// <summary>One running overlay: its window plus what the overlays list shows about it.</summary>
internal sealed class OverlayItem : IDisposable
{
    public OverlayItem(int number, WindowInfo source, OverlayForm form)
    {
        Number = number;
        Source = source.Handle;
        Title = source.Title;
        // Own copy: the window list disposes its icons whenever it refreshes.
        Icon = source.Icon == null ? null : new Bitmap(source.Icon);
        Form = form;
    }

    public int Number { get; }
    public IntPtr Source { get; }
    public string Title { get; }
    public Bitmap? Icon { get; }
    public OverlayForm Form { get; }

    /// <summary>Set once the overlay is being torn down, so close events don't remove it twice.</summary>
    public bool Removed { get; set; }

    public string Summary
    {
        get
        {
            var crop = Form.Crop;
            string text = $"Overlay {Number} · {(int)Math.Round(Form.OverlayOpacity * 100)}% opacity";
            if (crop.Left > 0 || crop.Top > 0 || crop.Right > 0 || crop.Bottom > 0) text += " · cropped";
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
