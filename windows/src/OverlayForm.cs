using System.Runtime.InteropServices;
using static OverlayApp.NativeMethods;

namespace OverlayApp;

/// <summary>Crop amounts as fractions (0..1) of the source width/height. Left+Right and Top+Bottom stay below 1.</summary>
internal readonly record struct Crop(double Left, double Top, double Right, double Bottom);

/// <summary>
/// Borderless, always-on-top window that mirrors another window via a DWM thumbnail.
/// The DWM composites the live image on the GPU, so video plays smoothly and nothing is
/// copied through this process. In click-through mode all mouse input passes to whatever
/// is underneath.
/// </summary>
internal sealed class OverlayForm : Form
{
    // Sizes in 96-DPI pixels, scaled to the monitor the overlay is on (DeviceDpi follows it between monitors).
    private int BorderPx => this.S(4);
    private int GripPx => this.S(10);

    // Pixels of this colour become fully transparent (used for the letterbox area).
    private static readonly Color KeyColor = Color.FromArgb(255, 0, 255);
    private static readonly Color EditBackColor = Theme.Background;
    private static readonly Color EditBorderColor = Theme.Accent;
    private static readonly Color EditBorderInactiveColor = Theme.Track;

    private readonly System.Windows.Forms.Timer _watchTimer;
    private IntPtr _source;
    private IntPtr _thumb;
    private Size _lastSourceSize;
    private bool _clickThrough;
    private byte _alpha = 255;
    private bool _clientAreaOnly = true;
    private Crop _crop;
    private bool _selected = true;

    /// <summary>Raised when the mirrored window is closed.</summary>
    public event EventHandler? SourceLost;

    public OverlayForm()
    {
        Text = "Overlay";
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        MinimumSize = new Size(this.S(80), this.S(50));
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

    /// <summary>Whether this is the overlay being edited in the main window (blue border vs grey).</summary>
    public bool Selected
    {
        get => _selected;
        set { _selected = value; Invalidate(); }
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
        MinimumSize = new Size(this.S(80), this.S(50)); // DeviceDpi is now the monitor's, not the system default
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

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        // Moved to a monitor with different scaling: rescale the minimum size, border and grips.
        base.OnDpiChanged(e);
        MinimumSize = new Size(this.S(80), this.S(50));
        UpdateThumbnail();
        Invalidate();
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

        using var pen = new Pen(_selected ? EditBorderColor : EditBorderInactiveColor, BorderPx);
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
        // Remember where the drag started so snapping can be based on the real mouse movement.
        // Record at mouse-down: by WM_ENTERSIZEMOVE the cursor has already passed Windows' drag threshold.
        if (m.Msg == WM_NCLBUTTONDOWN)
        {
            _dragStartRect = new RECT(Left, Top, Right, Bottom);
            _dragStartCursor = Cursor.Position;
            _dragStartRecorded = true;
        }
        else if (m.Msg == WM_ENTERSIZEMOVE && !_dragStartRecorded)
        {
            _dragStartRect = new RECT(Left, Top, Right, Bottom);
            _dragStartCursor = Cursor.Position;
        }
        else if (m.Msg == WM_EXITSIZEMOVE)
        {
            _dragStartRecorded = false;
        }
        if (SnapToEdges && m.Msg is WM_MOVING or WM_SIZING)
        {
            // Windows proposes a rectangle relative to the window's *current* (possibly snapped) position,
            // so snapping that would stick forever. Instead rebuild the rectangle the user is actually asking
            // for from the mouse movement since the drag began, then snap that.
            var proposed = Marshal.PtrToStructure<RECT>(m.LParam);
            var delta = new Size(Cursor.Position.X - _dragStartCursor.X, Cursor.Position.Y - _dragStartCursor.Y);
            var rect = m.Msg == WM_MOVING
                ? SnapMove(Offset(_dragStartRect, delta))
                : SnapSize(ResizeFromStart(m.WParam.ToInt32(), delta, proposed), m.WParam.ToInt32());
            Marshal.StructureToPtr(rect, m.LParam, false);
            m.Result = (IntPtr)1;
            return;
        }
        base.WndProc(ref m);
    }

    /// <summary>When true, dragging or resizing near a monitor edge snaps the overlay flush to it.</summary>
    public bool SnapToEdges { get; set; }

    /// <summary>Screen bounds of the other overlays, which this one also snaps to.</summary>
    public Func<IEnumerable<Rectangle>>? OtherOverlays { get; set; }

    private RECT _dragStartRect;
    private Point _dragStartCursor;
    private bool _dragStartRecorded;

    private int SnapDistance => (int)Math.Round(20 * DeviceDpi / 96.0);

    private static RECT Offset(RECT r, Size d) => new(r.Left + d.Width, r.Top + d.Height, r.Right + d.Width, r.Bottom + d.Height);

    /// <summary>The unsnapped rectangle for a resize: the start rectangle with the dragged edges moved by the mouse delta.</summary>
    private RECT ResizeFromStart(int edge, Size d, RECT proposed)
    {
        var r = _dragStartRect;
        if (edge is WMSZ_LEFT or WMSZ_TOPLEFT or WMSZ_BOTTOMLEFT) r.Left = Math.Min(r.Left + d.Width, r.Right - MinimumSize.Width);
        if (edge is WMSZ_RIGHT or WMSZ_TOPRIGHT or WMSZ_BOTTOMRIGHT) r.Right = Math.Max(r.Right + d.Width, r.Left + MinimumSize.Width);
        if (edge is WMSZ_TOP or WMSZ_TOPLEFT or WMSZ_TOPRIGHT) r.Top = Math.Min(r.Top + d.Height, r.Bottom - MinimumSize.Height);
        if (edge is WMSZ_BOTTOM or WMSZ_BOTTOMLEFT or WMSZ_BOTTOMRIGHT) r.Bottom = Math.Max(r.Bottom + d.Height, r.Top + MinimumSize.Height);
        return edge is >= WMSZ_LEFT and <= WMSZ_BOTTOMRIGHT ? r : proposed;
    }

    /// <summary>
    /// Snap lines for the monitor under the rectangle: both the work-area edges (which stop at the taskbar)
    /// and the full screen edges. Where there's no taskbar the two are the same line.
    /// </summary>
    private (int[] X, int[] Y) SnapLines(RECT r)
    {
        var xs = new List<int>();
        var ys = new List<int>();

        // Ask Windows on every drag rather than using a cached screen list, so moving/resizing the taskbar,
        // auto-hide, resolution or monitor changes are always reflected. Works for a taskbar on any side.
        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (GetMonitorInfo(MonitorFromRect(ref r, MONITOR_DEFAULTTONEAREST), ref info))
        {
            RECT work = info.rcWork, full = info.rcMonitor;
            xs.AddRange(new[] { work.Left, work.Right, full.Left, full.Right });
            ys.AddRange(new[] { work.Top, work.Bottom, full.Top, full.Bottom });
        }

        // Other overlays' edges. Only use an overlay that's alongside this one on the other axis, so its
        // edges act as neighbours (flush side by side, or lined up) rather than tugging from across the screen.
        if (OtherOverlays != null)
        {
            int d = SnapDistance;
            foreach (var o in OtherOverlays())
            {
                if (r.Top <= o.Bottom + d && r.Bottom >= o.Top - d) xs.AddRange(new[] { o.Left, o.Right });
                if (r.Left <= o.Right + d && r.Right >= o.Left - d) ys.AddRange(new[] { o.Top, o.Bottom });
            }
        }

        return (xs.ToArray(), ys.ToArray());
    }

    /// <summary>Offset that moves one of <paramref name="edges"/> onto the closest snap line within range, or 0.</summary>
    private int SnapOffset(int[] edges, int[] lines)
    {
        int best = 0, bestDistance = int.MaxValue;
        foreach (int edge in edges)
        foreach (int line in lines)
        {
            int distance = Math.Abs(line - edge);
            if (distance <= SnapDistance && distance < bestDistance)
            {
                best = line - edge;
                bestDistance = distance;
            }
        }
        return best;
    }

    /// <summary>Moves the whole rectangle so its nearest edge lines up with a nearby snap line.</summary>
    private RECT SnapMove(RECT r)
    {
        var (xs, ys) = SnapLines(r);
        int dx = SnapOffset(new[] { r.Left, r.Right }, xs);
        int dy = SnapOffset(new[] { r.Top, r.Bottom }, ys);
        return new RECT(r.Left + dx, r.Top + dy, r.Right + dx, r.Bottom + dy);
    }

    /// <summary>Snaps only the edges being dragged during a resize.</summary>
    private RECT SnapSize(RECT r, int edge)
    {
        var (xs, ys) = SnapLines(r);
        if (edge is WMSZ_LEFT or WMSZ_TOPLEFT or WMSZ_BOTTOMLEFT) r.Left += SnapOffset(new[] { r.Left }, xs);
        if (edge is WMSZ_RIGHT or WMSZ_TOPRIGHT or WMSZ_BOTTOMRIGHT) r.Right += SnapOffset(new[] { r.Right }, xs);
        if (edge is WMSZ_TOP or WMSZ_TOPLEFT or WMSZ_TOPRIGHT) r.Top += SnapOffset(new[] { r.Top }, ys);
        if (edge is WMSZ_BOTTOM or WMSZ_BOTTOMLEFT or WMSZ_BOTTOMRIGHT) r.Bottom += SnapOffset(new[] { r.Bottom }, ys);
        return r;
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

    /// <summary>Size in pixels of what's being mirrored (client area or whole window), before cropping.</summary>
    public Size SourceSize => GetSourceSize();

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
