using static OverlayApp.NativeMethods;

namespace OverlayApp;

/// <summary>Crop amounts as fractions (0..0.45) of the source width/height.</summary>
internal readonly record struct Crop(double Left, double Top, double Right, double Bottom);

/// <summary>
/// Borderless, always-on-top window that mirrors another window via a DWM thumbnail.
/// The DWM composites the live image on the GPU, so video plays smoothly and nothing is
/// copied through this process. In click-through mode all mouse input passes to whatever
/// is underneath.
/// </summary>
internal sealed class OverlayForm : Form
{
    private const int BorderPx = 4;
    private const int GripPx = 10;

    // Pixels of this colour become fully transparent (used for the letterbox area).
    private static readonly Color KeyColor = Color.FromArgb(255, 0, 255);
    private static readonly Color EditBackColor = Theme.Background;
    private static readonly Color EditBorderColor = Theme.Accent;

    private readonly System.Windows.Forms.Timer _watchTimer;
    private IntPtr _source;
    private IntPtr _thumb;
    private Size _lastSourceSize;
    private bool _clickThrough;
    private byte _alpha = 255;
    private bool _clientAreaOnly = true;
    private Crop _crop;

    /// <summary>Raised when the mirrored window is closed.</summary>
    public event EventHandler? SourceLost;

    public OverlayForm()
    {
        Text = "Overlay";
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        MinimumSize = new Size(80, 50);
        BackColor = EditBackColor;
        DoubleBuffered = true;

        _watchTimer = new System.Windows.Forms.Timer { Interval = 250 };
        _watchTimer.Tick += (_, _) => WatchSource();
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_TOPMOST;
            if (_clickThrough) cp.ExStyle |= WS_EX_TRANSPARENT | WS_EX_NOACTIVATE;
            return cp;
        }
    }

    public IntPtr Source
    {
        get => _source;
        set
        {
            _source = value;
            if (IsHandleCreated) RegisterThumbnail();
        }
    }

    public bool ClickThrough
    {
        get => _clickThrough;
        set
        {
            _clickThrough = value;
            BackColor = value ? KeyColor : EditBackColor;
            if (!IsHandleCreated) return;
            ApplyExStyle();
            UpdateThumbnail();
            Invalidate();
        }
    }

    /// <summary>Overall overlay opacity, 0.1 .. 1.0.</summary>
    public double OverlayOpacity
    {
        get => _alpha / 255.0;
        set
        {
            _alpha = (byte)Math.Round(Math.Clamp(value, 0.1, 1.0) * 255);
            if (IsHandleCreated) ApplyLayeredAttributes();
        }
    }

    public bool ClientAreaOnly
    {
        get => _clientAreaOnly;
        set { _clientAreaOnly = value; UpdateThumbnail(); }
    }

    public Crop Crop
    {
        get => _crop;
        set { _crop = value; UpdateThumbnail(); }
    }

    /// <summary>Resizes the overlay (keeping its width) so it matches the source's aspect ratio.</summary>
    public void FitToSource()
    {
        var src = GetCroppedSourceSize();
        if (src.Width <= 0 || src.Height <= 0) return;

        int inset = _clickThrough ? 0 : BorderPx * 2;
        int contentWidth = ClientSize.Width - inset;
        int contentHeight = (int)Math.Round(contentWidth * (double)src.Height / src.Width);
        ClientSize = new Size(ClientSize.Width, contentHeight + inset);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplyLayeredAttributes();
        RegisterThumbnail();
        _watchTimer.Start();
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        _watchTimer.Stop();
        UnregisterThumbnail();
        base.OnHandleDestroyed(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _watchTimer.Dispose();
        base.Dispose(disposing);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        UpdateThumbnail();
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (_clickThrough) return;

        using var pen = new Pen(EditBorderColor, BorderPx);
        var r = ClientRectangle;
        r.Inflate(-BorderPx / 2, -BorderPx / 2);
        e.Graphics.DrawRectangle(pen, r);

        // Only visible when no source is drawn (e.g. the source is minimised).
        TextRenderer.DrawText(e.Graphics,
            "Drag to move • drag edges to resize\nSource may be minimised",
            Font, ClientRectangle, Color.Gainsboro,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_NCHITTEST && !_clickThrough)
        {
            long lp = m.LParam.ToInt64();
            var pt = PointToClient(new Point((short)(lp & 0xFFFF), (short)((lp >> 16) & 0xFFFF)));
            m.Result = (IntPtr)HitTest(pt);
            return;
        }
        base.WndProc(ref m);
    }

    private int HitTest(Point p)
    {
        bool left = p.X < GripPx, right = p.X >= ClientSize.Width - GripPx;
        bool top = p.Y < GripPx, bottom = p.Y >= ClientSize.Height - GripPx;

        if (top && left) return HTTOPLEFT;
        if (top && right) return HTTOPRIGHT;
        if (bottom && left) return HTBOTTOMLEFT;
        if (bottom && right) return HTBOTTOMRIGHT;
        if (left) return HTLEFT;
        if (right) return HTRIGHT;
        if (top) return HTTOP;
        if (bottom) return HTBOTTOM;
        return HTCAPTION; // anywhere else drags the window
    }

    private void ApplyExStyle()
    {
        int ex = GetWindowLong(Handle, GWL_EXSTYLE);
        ex |= WS_EX_LAYERED | WS_EX_TOOLWINDOW;
        if (_clickThrough) ex |= WS_EX_TRANSPARENT | WS_EX_NOACTIVATE;
        else ex &= ~(WS_EX_TRANSPARENT | WS_EX_NOACTIVATE);
        SetWindowLong(Handle, GWL_EXSTYLE, ex);
        ApplyLayeredAttributes();
    }

    private void ApplyLayeredAttributes()
    {
        uint colorKey = (uint)(KeyColor.R | (KeyColor.G << 8) | (KeyColor.B << 16));
        SetLayeredWindowAttributes(Handle, colorKey, _alpha, LWA_ALPHA | LWA_COLORKEY);
    }

    private void RegisterThumbnail()
    {
        UnregisterThumbnail();
        if (_source == IntPtr.Zero || !IsWindow(_source)) return;

        if (DwmRegisterThumbnail(Handle, _source, out _thumb) != 0)
        {
            _thumb = IntPtr.Zero;
            return;
        }
        _lastSourceSize = Size.Empty;
        UpdateThumbnail();
    }

    private void UnregisterThumbnail()
    {
        if (_thumb == IntPtr.Zero) return;
        DwmUnregisterThumbnail(_thumb);
        _thumb = IntPtr.Zero;
    }

    private Size GetSourceSize()
    {
        if (_thumb == IntPtr.Zero) return Size.Empty;

        if (_clientAreaOnly)
        {
            return GetClientRect(_source, out RECT rc) ? new Size(rc.Width, rc.Height) : Size.Empty;
        }
        return DwmQueryThumbnailSourceSize(_thumb, out SIZE size) == 0 ? new Size(size.cx, size.cy) : Size.Empty;
    }

    private Rectangle GetSourceCropRect(Size src)
    {
        int left = (int)(src.Width * _crop.Left);
        int top = (int)(src.Height * _crop.Top);
        int right = src.Width - (int)(src.Width * _crop.Right);
        int bottom = src.Height - (int)(src.Height * _crop.Bottom);
        return Rectangle.FromLTRB(left, top, Math.Max(left + 1, right), Math.Max(top + 1, bottom));
    }

    private Size GetCroppedSourceSize()
    {
        var src = GetSourceSize();
        return src.IsEmpty ? Size.Empty : GetSourceCropRect(src).Size;
    }

    private void UpdateThumbnail()
    {
        if (_thumb == IntPtr.Zero) return;

        var src = GetSourceSize();
        _lastSourceSize = src;
        var props = new DWM_THUMBNAIL_PROPERTIES
        {
            dwFlags = DWM_TNP_VISIBLE | DWM_TNP_RECTDESTINATION | DWM_TNP_RECTSOURCE
                    | DWM_TNP_OPACITY | DWM_TNP_SOURCECLIENTAREAONLY,
            opacity = 255,
            fSourceClientAreaOnly = _clientAreaOnly,
        };

        if (src.Width <= 0 || src.Height <= 0)
        {
            props.fVisible = false;
            DwmUpdateThumbnailProperties(_thumb, ref props);
            return;
        }

        var crop = GetSourceCropRect(src);

        // Letterbox the cropped source into the available area, preserving aspect ratio.
        var area = ClientRectangle;
        if (!_clickThrough) area.Inflate(-BorderPx, -BorderPx);
        double scale = Math.Min(area.Width / (double)crop.Width, area.Height / (double)crop.Height);
        int w = Math.Max(1, (int)(crop.Width * scale));
        int h = Math.Max(1, (int)(crop.Height * scale));
        int x = area.X + (area.Width - w) / 2;
        int y = area.Y + (area.Height - h) / 2;

        props.fVisible = true;
        props.rcDestination = new RECT(x, y, x + w, y + h);
        props.rcSource = new RECT(crop.Left, crop.Top, crop.Right, crop.Bottom);
        DwmUpdateThumbnailProperties(_thumb, ref props);
    }

    private void WatchSource()
    {
        if (_source == IntPtr.Zero) return;

        if (!IsWindow(_source))
        {
            UnregisterThumbnail();
            _source = IntPtr.Zero;
            SourceLost?.Invoke(this, EventArgs.Empty);
            return;
        }

        // Follow the source if it is resized (e.g. a player toggling fullscreen).
        if (GetSourceSize() != _lastSourceSize) UpdateThumbnail();
    }
}
