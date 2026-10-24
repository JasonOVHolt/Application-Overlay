using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using static OverlayApp.NativeMethods;

namespace OverlayApp;

/// <summary>
/// Dialog for picking the visible area of an overlay by dragging a rectangle over a live view of the
/// whole source window. The live view is a DWM thumbnail, which always draws on top of its window, so
/// the selection is drawn by a separate transparent window (<see cref="SelectionLayer"/>) kept over it.
/// </summary>
internal sealed class CropPickerForm : Form
{
    private readonly IntPtr _source;
    private readonly bool _clientAreaOnly;
    private readonly Size _sourceSize;
    private readonly Crop _original;
    private readonly Action<Crop> _preview;
    private readonly string _subtitle;
    private readonly SelectionLayer _layer;
    private readonly ModernButton _apply = new("Apply", ButtonKind.Primary);
    private readonly ModernButton _cancel = new("Cancel", ButtonKind.Secondary);
    private readonly ModernButton _reset = new("Whole window", ButtonKind.Ghost, Theme.GlyphReset);
    private IntPtr _thumb;
    private Rectangle _previewRect;

    /// <summary>The chosen crop (valid when the dialog returns OK).</summary>
    public Crop Result { get; private set; }

    /// <param name="preview">Called as the selection changes, so the overlay can show the result live.</param>
    public CropPickerForm(IntPtr source, bool clientAreaOnly, Size sourceSize, Crop current, Action<Crop> preview, string subtitle)
    {
        _source = source;
        _clientAreaOnly = clientAreaOnly;
        _sourceSize = sourceSize;
        _original = current;
        _preview = preview;
        _subtitle = subtitle;
        Result = current;

        // Child controls are created in 96-DPI units and scaled by WinForms; the dialog's own layout
        // (LayoutDialog) is computed in real pixels for the monitor it's on.
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;

        Text = "Select area";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        TopMost = true; // overlays are always on top; without this they could cover the picker and its buttons
        KeyPreview = true;
        BackColor = Theme.Background;
        Font = Theme.Body;
        DoubleBuffered = true;

        _layer = new SelectionLayer();
        _layer.SelectionChanged += (_, _) => OnSelectionChanged();
        _layer.Committed += (_, _) => Apply();

        _apply.Click += (_, _) => Apply();
        _cancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        _reset.Click += (_, _) => _layer.Selection = new RectangleF(PointF.Empty, _previewRect.Size);
        _reset.Font = Theme.Small;
        Controls.AddRange(new Control[] { _apply, _cancel, _reset });
    }

    private int HeaderHeight => this.S(58);
    private int FooterHeight => this.S(62);
    private int Pad => this.S(16);

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        LayoutDialog(centerOnOwner: true);
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        // Dragged to a monitor with different scaling: redo the layout in the new pixels, keeping the selection.
        var crop = _layer.Visible ? SelectionToCrop() : _original;
        base.OnDpiChanged(e);
        LayoutDialog(centerOnOwner: false);
        UpdateThumbnail();
        PositionLayer();
        if (_layer.Visible) SetSelection(crop);
    }

    /// <summary>Sizes the dialog, preview and buttons in pixels for the current monitor.</summary>
    private void LayoutDialog(bool centerOnOwner)
    {
        // Size the preview to the source's shape, as large as fits in ~80% of the screen.
        var area = (centerOnOwner && Owner != null ? Screen.FromControl(Owner) : Screen.FromControl(this)).WorkingArea;
        int maxW = (int)(area.Width * 0.8) - 2 * Pad, maxH = (int)(area.Height * 0.8) - HeaderHeight - FooterHeight;
        double scale = Math.Min(maxW / (double)_sourceSize.Width, maxH / (double)_sourceSize.Height);
        var preview = new Size(Math.Max(1, (int)(_sourceSize.Width * scale)), Math.Max(1, (int)(_sourceSize.Height * scale)));
        int width = Math.Max(preview.Width + 2 * Pad, this.S(720));
        ClientSize = new Size(width, HeaderHeight + preview.Height + FooterHeight);
        _previewRect = new Rectangle((width - preview.Width) / 2, HeaderHeight, preview.Width, preview.Height);

        // Footer buttons, right-aligned; "Whole window" on the left.
        int buttonH = this.S(36), y = ClientSize.Height - FooterHeight + (FooterHeight - buttonH) / 2;
        _apply.SetBounds(ClientSize.Width - Pad - this.S(110), y, this.S(110), buttonH);
        _cancel.SetBounds(_apply.Left - this.S(8) - this.S(100), y, this.S(100), buttonH);
        _reset.SetBounds(Pad, y, _reset.PreferredWidth, buttonH);
        foreach (var b in new[] { _apply, _cancel, _reset }) b.BackColor = Theme.Background;

        // Windows centred the dialog at its default size; centre it again now it has its real size.
        // After a monitor change, stay put but keep the dialog on its new screen.
        var center = centerOnOwner && Owner != null
            ? new Point(Owner.Left + Owner.Width / 2, Owner.Top + Owner.Height / 2)
            : centerOnOwner ? new Point(area.Left + area.Width / 2, area.Top + area.Height / 2)
            : new Point(Left + Width / 2, Top + Height / 2);
        Location = new Point(
            Math.Clamp(center.X - Width / 2, area.Left, Math.Max(area.Left, area.Right - Width)),
            Math.Clamp(center.Y - Height / 2, area.Top, Math.Max(area.Top, area.Bottom - Height)));
        Invalidate();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.UseDarkTitleBar(Handle);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        RegisterThumbnail();
        _layer.Show(this); // owned, so it always stays above this dialog
        PositionLayer();

        // Start from the current crop (only once the layer has its real size, or it would be clamped).
        SetSelection(_original);
        ActiveControl = _apply;
    }

    private void SetSelection(Crop crop)
    {
        var size = _previewRect.Size;
        _layer.Selection = RectangleF.FromLTRB(
            (float)(crop.Left * size.Width), (float)(crop.Top * size.Height),
            (float)((1 - crop.Right) * size.Width), (float)((1 - crop.Bottom) * size.Height));
    }

    protected override void OnLocationChanged(EventArgs e)
    {
        base.OnLocationChanged(e);
        PositionLayer();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        if (_thumb != IntPtr.Zero) DwmUnregisterThumbnail(_thumb);
        _thumb = IntPtr.Zero;
        _layer.Close();
        if (DialogResult != DialogResult.OK) _preview(_original); // undo the live preview
        base.OnFormClosed(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _layer.Dispose();
        base.Dispose(disposing);
    }

    private void PositionLayer()
    {
        if (!IsHandleCreated || !_layer.Visible) return;
        _layer.Bounds = new Rectangle(PointToScreen(_previewRect.Location), _previewRect.Size);
        _layer.Render();
    }

    private void RegisterThumbnail()
    {
        if (DwmRegisterThumbnail(Handle, _source, out _thumb) != 0) { _thumb = IntPtr.Zero; return; }
        UpdateThumbnail();
    }

    private void UpdateThumbnail()
    {
        if (_thumb == IntPtr.Zero) return;
        // Borderless window, so window and client coordinates are the same.
        var props = new DWM_THUMBNAIL_PROPERTIES
        {
            dwFlags = DWM_TNP_VISIBLE | DWM_TNP_RECTDESTINATION | DWM_TNP_OPACITY | DWM_TNP_SOURCECLIENTAREAONLY,
            rcDestination = new RECT(_previewRect.Left, _previewRect.Top, _previewRect.Right, _previewRect.Bottom),
            opacity = 255,
            fVisible = true,
            fSourceClientAreaOnly = _clientAreaOnly,
        };
        DwmUpdateThumbnailProperties(_thumb, ref props);
    }

    private Crop SelectionToCrop()
    {
        var s = _layer.Selection;
        float w = _previewRect.Width, h = _previewRect.Height;
        return new Crop(
            Math.Clamp(s.Left / w, 0, 1), Math.Clamp(s.Top / h, 0, 1),
            Math.Clamp(1 - s.Right / w, 0, 1), Math.Clamp(1 - s.Bottom / h, 0, 1));
    }

    private void OnSelectionChanged()
    {
        _preview(SelectionToCrop());
        var s = _layer.Selection;
        double toSource = _sourceSize.Width / (double)_previewRect.Width;
        _layer.Label = $"{Math.Round(s.Width * toSource)} × {Math.Round(s.Height * toSource)} px";
        _layer.Render();
    }

    private void Apply()
    {
        Result = SelectionToCrop();
        DialogResult = DialogResult.OK;
        Close();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        // Arrow keys nudge the selection by one pixel; with Shift they resize it.
        Keys key = keyData & Keys.KeyCode;
        bool shift = (keyData & Keys.Shift) != 0;
        int dx = key == Keys.Left ? -1 : key == Keys.Right ? 1 : 0;
        int dy = key == Keys.Up ? -1 : key == Keys.Down ? 1 : 0;
        if (dx != 0 || dy != 0)
        {
            _layer.Nudge(dx, dy, resize: shift);
            return true;
        }
        if (keyData == Keys.Enter) { Apply(); return true; }
        if (keyData == Keys.Escape) { DialogResult = DialogResult.Cancel; Close(); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void WndProc(ref Message m)
    {
        // Drag the dialog by its header.
        if (m.Msg == WM_NCHITTEST)
        {
            long lp = m.LParam.ToInt64();
            var pt = PointToClient(new Point((short)(lp & 0xFFFF), (short)((lp >> 16) & 0xFFFF)));
            if (pt.Y < HeaderHeight) { m.Result = (IntPtr)HTCAPTION; return; }
        }
        base.WndProc(ref m);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        using (var border = new Pen(Theme.Border)) g.DrawRectangle(border, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);

        var flags = TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix;
        int titleH = TextRenderer.MeasureText("Ag", this.Scaled(Theme.Section)).Height;
        int top = (HeaderHeight - titleH - TextRenderer.MeasureText("Ag", this.Scaled(Theme.Small)).Height) / 2;
        TextRenderer.DrawText(g, "Select area", this.Scaled(Theme.Section), new Rectangle(Pad, top, ClientSize.Width - 2 * Pad, titleH), Theme.Text, flags);
        TextRenderer.DrawText(g, _subtitle, this.Scaled(Theme.Small), new Rectangle(Pad, top + titleH, ClientSize.Width - 2 * Pad, titleH),
            Theme.TextMuted, flags);

        // Under the thumbnail: visible only if the source can't be drawn (e.g. it's minimised).
        using (var bg = new SolidBrush(Theme.Input)) g.FillRectangle(bg, _previewRect);
        TextRenderer.DrawText(g, "The source window is minimised. Restore it to pick an area.", this.Scaled(Theme.Body), _previewRect,
            Theme.TextMuted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);

        var hint = "Drag to select · drag edges to resize · arrows nudge, Shift+arrows resize";
        int left = _reset.Right + this.S(12), right = _cancel.Left - this.S(12);
        TextRenderer.DrawText(g, hint, this.Scaled(Theme.Small),
            new Rectangle(left, ClientSize.Height - FooterHeight, Math.Max(0, right - left), FooterHeight),
            Theme.TextMuted, TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
    }
}

/// <summary>
/// Transparent window over the picker's live preview. Dims everything outside the selection and lets the
/// user draw, move and resize the selection. Coordinates are pixels relative to this window.
/// </summary>
internal sealed class SelectionLayer : Form
{
    private enum DragMode { None, Create, Move, Resize }

    private RectangleF _selection;
    private DragMode _mode;
    private ResizeEdges _edges;
    private PointF _dragStart;
    private RectangleF _dragStartSelection;
    private IntPtr _dib, _memDc;
    private Size _dibSize;

    public event EventHandler? SelectionChanged;
    public event EventHandler? Committed;

    public string Label { get; set; } = "";

    public SelectionLayer()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
            return cp;
        }
    }

    private float MinSize => this.S(8);
    private float HandleHit => this.S(8);

    public RectangleF Selection
    {
        get => _selection;
        set
        {
            _selection = Clamp(Normalize(value));
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private RectangleF LayerRect => new(0, 0, Width, Height);

    private static RectangleF Normalize(RectangleF r) =>
        RectangleF.FromLTRB(Math.Min(r.Left, r.Right), Math.Min(r.Top, r.Bottom), Math.Max(r.Left, r.Right), Math.Max(r.Top, r.Bottom));

    private RectangleF Clamp(RectangleF r)
    {
        if (Width <= 0 || Height <= 0) return r;
        float left = Math.Clamp(r.Left, 0, Width), top = Math.Clamp(r.Top, 0, Height);
        float right = Math.Clamp(r.Right, 0, Width), bottom = Math.Clamp(r.Bottom, 0, Height);
        return RectangleF.FromLTRB(left, top, right, bottom);
    }

    /// <summary>Moves the selection by (dx, dy) pixels, or grows/shrinks its right/bottom edge.</summary>
    public void Nudge(int dx, int dy, bool resize)
    {
        var s = _selection;
        if (resize)
        {
            Selection = new RectangleF(s.X, s.Y, Math.Max(MinSize, s.Width + dx), Math.Max(MinSize, s.Height + dy));
        }
        else
        {
            float x = Math.Clamp(s.X + dx, 0, Width - s.Width), y = Math.Clamp(s.Y + dy, 0, Height - s.Height);
            Selection = new RectangleF(x, y, s.Width, s.Height);
        }
    }

    // ------------------------------------------------------------------ mouse

    protected override void WndProc(ref Message m)
    {
        // Clicking here must not take focus from the dialog (it handles Enter/Esc/arrows).
        if (m.Msg == WM_MOUSEACTIVATE)
        {
            m.Result = (IntPtr)MA_NOACTIVATE;
            return;
        }
        base.WndProc(ref m);
    }

    private ResizeEdges EdgesAt(PointF p)
    {
        var s = _selection;
        float h = HandleHit;
        bool nearX = p.X >= s.Left - h && p.X <= s.Right + h;
        bool nearY = p.Y >= s.Top - h && p.Y <= s.Bottom + h;
        var e = ResizeEdges.None;
        if (nearY && Math.Abs(p.X - s.Left) <= h) e |= ResizeEdges.Left;
        else if (nearY && Math.Abs(p.X - s.Right) <= h) e |= ResizeEdges.Right;
        if (nearX && Math.Abs(p.Y - s.Top) <= h) e |= ResizeEdges.Top;
        else if (nearX && Math.Abs(p.Y - s.Bottom) <= h) e |= ResizeEdges.Bottom;
        return e;
    }

    private static Cursor CursorFor(ResizeEdges e) => e switch
    {
        ResizeEdges.Left or ResizeEdges.Right => Cursors.SizeWE,
        ResizeEdges.Top or ResizeEdges.Bottom => Cursors.SizeNS,
        ResizeEdges.Left | ResizeEdges.Top or ResizeEdges.Right | ResizeEdges.Bottom => Cursors.SizeNWSE,
        _ => Cursors.SizeNESW,
    };

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        Capture = true; // keep receiving moves if the pointer leaves the layer mid-drag
        _dragStart = e.Location;
        _dragStartSelection = _selection;
        _edges = EdgesAt(e.Location);
        _mode = _edges != ResizeEdges.None ? DragMode.Resize
              : CanMove && _selection.Contains(e.Location) ? DragMode.Move
              : DragMode.Create;
    }

    /// <summary>
    /// A selection covering the whole preview can't move, so dragging inside it draws a new one instead
    /// (otherwise there'd be nowhere to start a new selection when the dialog opens uncropped).
    /// </summary>
    private bool CanMove => _selection.Width < Width - 1 || _selection.Height < Height - 1;

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        PointF p = new(Math.Clamp(e.X, 0, Width), Math.Clamp(e.Y, 0, Height));
        float dx = p.X - _dragStart.X, dy = p.Y - _dragStart.Y;
        var s = _dragStartSelection;

        switch (_mode)
        {
            case DragMode.None:
                var edges = EdgesAt(e.Location);
                Cursor = edges != ResizeEdges.None ? CursorFor(edges)
                       : CanMove && _selection.Contains(e.Location) ? Cursors.SizeAll
                       : Cursors.Cross;
                break;

            case DragMode.Create:
                if (Math.Abs(dx) >= MinSize || Math.Abs(dy) >= MinSize)
                    Selection = RectangleF.FromLTRB(_dragStart.X, _dragStart.Y, p.X, p.Y);
                break;

            case DragMode.Move:
                float x = Math.Clamp(s.X + dx, 0, Width - s.Width), y = Math.Clamp(s.Y + dy, 0, Height - s.Height);
                Selection = new RectangleF(x, y, s.Width, s.Height);
                break;

            case DragMode.Resize:
                float left = s.Left, top = s.Top, right = s.Right, bottom = s.Bottom;
                if (_edges.HasFlag(ResizeEdges.Left)) left = Math.Min(s.Left + dx, right - MinSize);
                if (_edges.HasFlag(ResizeEdges.Right)) right = Math.Max(s.Right + dx, left + MinSize);
                if (_edges.HasFlag(ResizeEdges.Top)) top = Math.Min(s.Top + dy, bottom - MinSize);
                if (_edges.HasFlag(ResizeEdges.Bottom)) bottom = Math.Max(s.Bottom + dy, top + MinSize);
                Selection = RectangleF.FromLTRB(left, top, right, bottom);
                break;
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        Capture = false;
        _mode = DragMode.None;
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        if (_selection.Contains(e.Location)) Committed?.Invoke(this, EventArgs.Empty);
    }

    // ------------------------------------------------------------------ drawing

    /// <summary>Redraws the layer (per-pixel alpha) at its current bounds.</summary>
    public void Render()
    {
        if (!IsHandleCreated || Width <= 0 || Height <= 0) return;
        EnsureBitmap();

        using (var bmp = new Bitmap(Width, Height, Width * 4, PixelFormat.Format32bppPArgb, DibBits))
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit; // ClearType needs an opaque background
            g.CompositingMode = CompositingMode.SourceCopy;
            // Dim everything; then make the selection see-through. Alpha 1 (not 0) inside so it still
            // receives the mouse: fully transparent pixels of a layered window are click-through.
            using (var dim = new SolidBrush(Color.FromArgb(150, 0, 0, 0))) g.FillRectangle(dim, LayerRect);
            using (var hole = new SolidBrush(Color.FromArgb(1, 0, 0, 0))) g.FillRectangle(hole, _selection);
            g.CompositingMode = CompositingMode.SourceOver;

            var s = _selection;
            using (var pen = new Pen(Theme.Accent, this.S(2))) g.DrawRectangle(pen, s.X, s.Y, s.Width, s.Height);

            // Thirds guides help line things up.
            using (var guide = new Pen(Color.FromArgb(70, 255, 255, 255), 1))
            {
                for (int i = 1; i <= 2; i++)
                {
                    g.DrawLine(guide, s.X + s.Width * i / 3, s.Y, s.X + s.Width * i / 3, s.Bottom);
                    g.DrawLine(guide, s.X, s.Y + s.Height * i / 3, s.Right, s.Y + s.Height * i / 3);
                }
            }

            float hs = this.S(5);
            using var handleFill = new SolidBrush(Color.White);
            using var handleEdge = new Pen(Theme.Accent, this.S(2));
            foreach (var (hx, hy) in new[]
            {
                (s.Left, s.Top), (s.Right, s.Top), (s.Left, s.Bottom), (s.Right, s.Bottom),
                (s.Left + s.Width / 2, s.Top), (s.Left + s.Width / 2, s.Bottom), (s.Left, s.Top + s.Height / 2), (s.Right, s.Top + s.Height / 2),
            })
            {
                // Keep handles fully visible when the selection touches the edge of the preview.
                float x = Math.Clamp(hx, hs + 1, Width - hs - 1), y = Math.Clamp(hy, hs + 1, Height - hs - 1);
                g.FillRectangle(handleFill, x - hs, y - hs, hs * 2, hs * 2);
                g.DrawRectangle(handleEdge, x - hs, y - hs, hs * 2, hs * 2);
            }

            if (!string.IsNullOrEmpty(Label))
            {
                var size = TextRenderer.MeasureText(Label, this.Scaled(Theme.Small));
                float lx = Math.Clamp(s.X, 0, Math.Max(0, Width - size.Width - this.S(12)));
                float ly = s.Y - size.Height - this.S(10) >= 0 ? s.Y - size.Height - this.S(10) : s.Y + this.S(6);
                var pill = new RectangleF(lx, ly, size.Width + this.S(12), size.Height + this.S(4));
                Theme.FillRounded(g, Color.FromArgb(220, 24, 24, 28), pill, pill.Height / 2);
                using var text = new SolidBrush(Color.White);
                g.DrawString(Label, this.Scaled(Theme.Small), text, pill.X + this.S(6), pill.Y + this.S(2));
            }
        }

        var screenDc = GetDC(IntPtr.Zero);
        try
        {
            var dst = new POINT(Left, Top);
            var size = new SIZE { cx = Width, cy = Height };
            var src = new POINT(0, 0);
            var blend = new BLENDFUNCTION { BlendOp = AC_SRC_OVER, SourceConstantAlpha = 255, AlphaFormat = AC_SRC_ALPHA };
            UpdateLayeredWindow(Handle, screenDc, ref dst, ref size, _memDc, ref src, 0, ref blend, ULW_ALPHA);
        }
        finally
        {
            ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    private IntPtr DibBits { get; set; }

    /// <summary>(Re)creates the 32-bit top-down DIB the layer is drawn into when the size changes.</summary>
    private void EnsureBitmap()
    {
        if (_dib != IntPtr.Zero && _dibSize == Size) return;
        FreeBitmap();
        var header = new BITMAPINFOHEADER
        {
            biSize = System.Runtime.InteropServices.Marshal.SizeOf<BITMAPINFOHEADER>(),
            biWidth = Width,
            biHeight = -Height, // negative: top-down rows, matching GDI+'s layout
            biPlanes = 1,
            biBitCount = 32,
        };
        var screenDc = GetDC(IntPtr.Zero);
        _memDc = CreateCompatibleDC(screenDc);
        ReleaseDC(IntPtr.Zero, screenDc);
        _dib = CreateDIBSection(_memDc, ref header, 0, out var bits, IntPtr.Zero, 0);
        DibBits = bits;
        SelectObject(_memDc, _dib);
        _dibSize = Size;
    }

    private void FreeBitmap()
    {
        if (_memDc != IntPtr.Zero) DeleteDC(_memDc);
        if (_dib != IntPtr.Zero) DeleteObject(_dib);
        _memDc = _dib = IntPtr.Zero;
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        Selection = _selection; // re-clamp to the new size
    }

    protected override void Dispose(bool disposing)
    {
        FreeBitmap();
        base.Dispose(disposing);
    }

    [Flags]
    private enum ResizeEdges { None = 0, Left = 1, Right = 2, Top = 4, Bottom = 8 }
}
