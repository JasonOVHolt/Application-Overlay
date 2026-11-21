using System.Drawing.Drawing2D;

namespace OverlayApp;

/// <summary>Owner-drawn list with icon + two-line rows and hover/selection styling.</summary>
internal abstract class RowListBox : ListBox
{
    private int _hoverIndex = -1;
    private readonly int _logicalItemHeight; // 96-DPI units

    protected RowListBox(int itemHeight)
    {
        _logicalItemHeight = itemHeight;
        DrawMode = DrawMode.OwnerDrawFixed;
        BorderStyle = BorderStyle.None;
        IntegralHeight = false;
        BackColor = Theme.Surface;
        ForeColor = Theme.Text;
        Font = Theme.Body;
        ItemHeight = itemHeight;
    }

    protected int HoverIndex => _hoverIndex;
    protected Point MousePos { get; private set; } = new(-1, -1);

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.ApplyScrollbars(Handle);
        ApplyItemHeight();
    }

    // WinForms scales fonts and bounds for a new monitor's DPI, but not a ListBox's item height.
    protected override void RescaleConstantsForDpi(int deviceDpiOld, int deviceDpiNew)
    {
        base.RescaleConstantsForDpi(deviceDpiOld, deviceDpiNew);
        ApplyItemHeight();
    }

    private void ApplyItemHeight() => ItemHeight = Math.Min(255, this.S(_logicalItemHeight));

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var previous = MousePos;
        MousePos = e.Location;
        int index = IndexFromPoint(e.Location);
        if (index >= 0 && !GetItemRectangle(index).Contains(e.Location)) index = -1; // below the last row
        Cursor = index >= 0 ? Cursors.Hand : Cursors.Default;

        // Only repaint when something visible changes, not on every mouse move.
        if (index != _hoverIndex)
        {
            InvalidateItem(_hoverIndex);
            _hoverIndex = index;
            InvalidateItem(_hoverIndex);
        }
        else if (index >= 0 && HoverPartChanged(index, previous, e.Location))
        {
            InvalidateItem(index);
        }
    }

    /// <summary>Whether moving within a row changes a sub-part's hover state (e.g. a close button).</summary>
    protected virtual bool HoverPartChanged(int index, Point from, Point to) => false;

    /// <summary>Draws one row; called with an off-screen buffer so rows never flicker.</summary>
    protected abstract void DrawItemContent(DrawItemEventArgs e);

    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        if (e.Bounds.Width <= 0 || e.Bounds.Height <= 0) return;
        using var buffer = BufferedGraphicsManager.Current.Allocate(e.Graphics, e.Bounds);
        DrawItemContent(new DrawItemEventArgs(buffer.Graphics, e.Font, e.Bounds, e.Index, e.State, e.ForeColor, e.BackColor));
        buffer.Render(e.Graphics);
    }

    private const int WM_ERASEBKGND = 0x0014;

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_ERASEBKGND)
        {
            // Rows paint their own background; erase only the empty area below the last row.
            // Erasing behind the rows too is what makes them blink on every repaint.
            int rowsBottom = Math.Max(0, (Items.Count - TopIndex) * ItemHeight);
            if (rowsBottom < ClientSize.Height)
            {
                using var g = Graphics.FromHdc(m.WParam);
                using var bg = new SolidBrush(BackColor);
                g.FillRectangle(bg, 0, rowsBottom, ClientSize.Width, ClientSize.Height - rowsBottom);
            }
            m.Result = (IntPtr)1;
            return;
        }
        base.WndProc(ref m);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        MousePos = new Point(-1, -1);
        InvalidateItem(_hoverIndex);
        _hoverIndex = -1;
    }

    protected void InvalidateItem(int index)
    {
        if (index >= 0 && index < Items.Count) Invalidate(GetItemRectangle(index));
    }

    protected void ClearRow(DrawItemEventArgs e)
    {
        // Fill without anti-aliasing: soft edges would leave faint lines between rows.
        var mode = e.Graphics.SmoothingMode;
        e.Graphics.SmoothingMode = SmoothingMode.None;
        using (var bg = new SolidBrush(BackColor)) e.Graphics.FillRectangle(bg, e.Bounds);
        e.Graphics.SmoothingMode = mode;
    }

    /// <summary>Draws the row background, icon, title and subtitle. Returns the row's inner rect.</summary>
    /// <param name="dimmed">Draws the icon and title faded, e.g. for a hidden overlay.</param>
    /// <param name="glyph">Icon glyph to show when there is no <paramref name="icon"/> bitmap.</param>
    protected RectangleF DrawRow(DrawItemEventArgs e, Bitmap? icon, string title, string subtitle, int rightReserve, int iconSize,
        bool dimmed = false, string? glyph = null)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        ClearRow(e);

        bool selected = (e.State & DrawItemState.Selected) != 0;
        var r = new RectangleF(e.Bounds.X + this.S(2), e.Bounds.Y + this.S(2), e.Bounds.Width - this.S(4), e.Bounds.Height - this.S(4));
        float radius = this.S(8);

        if (selected)
        {
            Theme.FillRounded(g, Color.FromArgb(48, Theme.Accent), r, radius);
            Theme.DrawRounded(g, Color.FromArgb(140, Theme.Accent), r, radius);
        }
        else if (e.Index == _hoverIndex)
        {
            Theme.FillRounded(g, Theme.SurfaceHover, r, radius);
        }

        var iconRect = new Rectangle((int)r.X + this.S(12), (int)(r.Y + (r.Height - iconSize) / 2), iconSize, iconSize);
        if (icon != null)
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            if (dimmed)
            {
                using var faded = new System.Drawing.Imaging.ImageAttributes();
                faded.SetColorMatrix(new System.Drawing.Imaging.ColorMatrix { Matrix33 = 0.35f });
                g.DrawImage(icon, iconRect, 0, 0, icon.Width, icon.Height, GraphicsUnit.Pixel, faded);
            }
            else
            {
                g.DrawImage(icon, iconRect);
            }
        }
        else
        {
            TextRenderer.DrawText(g, glyph ?? Theme.GlyphApp, this.Scaled(Theme.IconLarge), iconRect, Theme.TextMuted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.PreserveGraphicsTranslateTransform);
        }

        int textLeft = iconRect.Right + this.S(12);
        int right = (int)r.Right - this.S(12) - rightReserve;
        var flags = TextFormatFlags.NoPadding | TextFormatFlags.PreserveGraphicsTranslateTransform | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix;
        int titleH = TextRenderer.MeasureText("Ag", this.Scaled(Theme.BodyStrong)).Height;
        int subH = TextRenderer.MeasureText("Ag", this.Scaled(Theme.Small)).Height;
        int y = (int)(r.Y + (r.Height - titleH - subH) / 2);
        TextRenderer.DrawText(g, title, this.Scaled(Theme.BodyStrong), new Rectangle(textLeft, y, right - textLeft, titleH),
            dimmed ? Theme.TextMuted : Theme.Text, flags);
        TextRenderer.DrawText(g, subtitle, this.Scaled(Theme.Small), new Rectangle(textLeft, y + titleH, right - textLeft, subH), Theme.TextMuted, flags);
        return r;
    }
}

/// <summary>List of capturable windows, with a LIVE badge on windows that have overlays.</summary>
internal sealed class WindowListBox : RowListBox
{
    private IReadOnlyDictionary<IntPtr, int> _liveCounts = new Dictionary<IntPtr, int>();

    public WindowListBox() : base(48) { }

    /// <summary>Number of overlays currently showing each window.</summary>
    public IReadOnlyDictionary<IntPtr, int> LiveCounts
    {
        get => _liveCounts;
        set { _liveCounts = value; Invalidate(); }
    }

    protected override void DrawItemContent(DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= Items.Count || Items[e.Index] is not WindowInfo w)
        {
            ClearRow(e);
            return;
        }

        _liveCounts.TryGetValue(w.Handle, out int count);
        string badgeText = count > 1 ? $"LIVE ×{count}" : "LIVE";
        var badgeSize = TextRenderer.MeasureText(badgeText, this.Scaled(Theme.Small));
        int reserve = count > 0 ? badgeSize.Width + this.S(20) : 0;

        var r = DrawRow(e, w.Icon, w.Title, w.ProcessName, reserve, this.S(24));

        if (count > 0)
        {
            var g = e.Graphics;
            float right = r.Right - this.S(12);
            var badge = new RectangleF(right - badgeSize.Width - this.S(12), r.Y + (r.Height - this.S(20)) / 2,
                badgeSize.Width + this.S(12), this.S(20));
            Theme.FillRounded(g, Color.FromArgb(40, Theme.Success), badge, badge.Height / 2);
            TextRenderer.DrawText(g, badgeText, this.Scaled(Theme.Small), Rectangle.Round(badge), Theme.Success,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.PreserveGraphicsTranslateTransform);
        }
    }
}

/// <summary>List of running overlays, each with a hide/show (eye) button and a close button.</summary>
internal sealed class OverlayListBox : RowListBox
{
    private const int WM_LBUTTONDOWN = 0x0201;
    private const int WM_LBUTTONDBLCLK = 0x0203;
    private const TextFormatFlags GlyphFlags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
        | TextFormatFlags.NoPadding | TextFormatFlags.PreserveGraphicsTranslateTransform;

    public event EventHandler<OverlayItem>? RemoveRequested;
    public event EventHandler<OverlayItem>? VisibilityToggleRequested;
    public event EventHandler<OverlayItem>? LockToggleRequested;

    /// <summary>Right-click (or the menu key) on a row: show that overlay's menu at this screen point.</summary>
    public event EventHandler<OverlayMenuRequest>? ContextMenuRequested;

    public OverlayListBox() : base(44) { }

    private Rectangle CloseRect(int index)
    {
        var b = GetItemRectangle(index);
        int s = this.S(26);
        return new Rectangle(b.Right - s - this.S(10), b.Y + (b.Height - s) / 2, s, s);
    }

    private Rectangle EyeRect(int index)
    {
        var close = CloseRect(index);
        return new Rectangle(close.X - close.Width - this.S(4), close.Y, close.Width, close.Height);
    }

    private Rectangle LockRect(int index)
    {
        var eye = EyeRect(index);
        return new Rectangle(eye.X - eye.Width - this.S(4), eye.Y, eye.Width, eye.Height);
    }

    protected override bool HoverPartChanged(int index, Point from, Point to)
    {
        var close = CloseRect(index);
        var eye = EyeRect(index);
        var lockRect = LockRect(index);
        return close.Contains(from) != close.Contains(to) || eye.Contains(from) != eye.Contains(to)
            || lockRect.Contains(from) != lockRect.Contains(to);
    }

    protected override void WndProc(ref Message m)
    {
        const int WM_CONTEXTMENU = 0x007B;
        if (m.Msg == WM_CONTEXTMENU && ContextMenuRequested != null)
        {
            long lp = m.LParam.ToInt64();
            int index;
            Point screen;
            if (lp == -1) // menu key / Shift+F10: the selected row
            {
                index = SelectedIndex;
                if (index < 0) return;
                var row = GetItemRectangle(index);
                screen = PointToScreen(new Point(row.X + this.S(40), row.Bottom));
            }
            else
            {
                screen = new Point((short)(lp & 0xFFFF), (short)((lp >> 16) & 0xFFFF));
                index = IndexFromPoint(PointToClient(screen));
            }
            if (index >= 0 && index < Items.Count && Items[index] is OverlayItem target)
                ContextMenuRequested(this, new OverlayMenuRequest(target, screen));
            return;
        }
        // Handle the row's buttons before the list treats the click as a selection.
        if (m.Msg is WM_LBUTTONDOWN or WM_LBUTTONDBLCLK)
        {
            long lp = m.LParam.ToInt64();
            var pt = new Point((short)(lp & 0xFFFF), (short)((lp >> 16) & 0xFFFF));
            int index = IndexFromPoint(pt);
            if (index >= 0 && Items[index] is OverlayItem item)
            {
                if (CloseRect(index).Contains(pt))
                {
                    RemoveRequested?.Invoke(this, item);
                    return;
                }
                if (EyeRect(index).Contains(pt))
                {
                    VisibilityToggleRequested?.Invoke(this, item);
                    return;
                }
                if (LockRect(index).Contains(pt))
                {
                    LockToggleRequested?.Invoke(this, item);
                    return;
                }
            }
        }
        base.WndProc(ref m);
    }

    protected override void DrawItemContent(DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= Items.Count || Items[e.Index] is not OverlayItem item)
        {
            ClearRow(e);
            return;
        }

        DrawRow(e, item.Icon, item.Title, item.Summary, this.S(92), this.S(20), dimmed: item.Hidden);

        var g = e.Graphics;
        bool selected = (e.State & DrawItemState.Selected) != 0;
        bool active = selected || e.Index == HoverIndex;

        // The eye stays visible while the overlay is hidden, so that state is always obvious.
        if (active || item.Hidden)
        {
            var eye = EyeRect(e.Index);
            bool overEye = eye.Contains(MousePos);
            Color background = overEye ? Theme.Border
                : selected ? Theme.Blend(BackColor, Theme.Accent, 48 / 255f)
                : e.Index == HoverIndex ? Theme.SurfaceHover
                : BackColor;
            if (overEye)
            {
                using var hover = new SolidBrush(background);
                g.FillEllipse(hover, eye);
            }
            Color color = overEye ? Theme.Text : item.Hidden ? Theme.Warning : Theme.TextMuted;
            TextRenderer.DrawText(g, Theme.GlyphView, this.Scaled(Theme.Icon), eye, color, GlyphFlags);
            if (item.Hidden)
            {
                float side = eye.Width * 0.5f;
                var box = new RectangleF(eye.X + (eye.Width - side) / 2, eye.Y + (eye.Height - side) / 2, side, side);
                Theme.DrawSlash(g, box, color, background, Math.Max(1f, this.S(3) / 2f));
            }
        }

        // Lock: always shown while locked (accent), otherwise on hover/selection as an open padlock.
        bool locked = item.Form.ClickThrough;
        if (active || locked)
        {
            var lockRect = LockRect(e.Index);
            bool overLock = lockRect.Contains(MousePos);
            if (overLock)
            {
                using var hover = new SolidBrush(Theme.Border);
                g.FillEllipse(hover, lockRect);
            }
            Color color = overLock ? Theme.Text : locked ? Theme.Accent : Theme.TextMuted;
            TextRenderer.DrawText(g, locked ? Theme.GlyphLock : Theme.GlyphUnlock, this.Scaled(Theme.Icon), lockRect, color, GlyphFlags);
        }

        if (active)
        {
            var close = CloseRect(e.Index);
            bool overClose = close.Contains(MousePos);
            if (overClose)
            {
                using var hover = new SolidBrush(Color.FromArgb(90, Theme.Danger));
                g.FillEllipse(hover, close);
            }
            TextRenderer.DrawText(g, Theme.GlyphClose, this.Scaled(Theme.IconSmall), close,
                overClose ? Color.White : Theme.TextMuted, GlyphFlags);
        }
    }
}

/// <summary>Saved presets, each with Apply, Rename and Delete buttons (shown on hover or selection).</summary>
internal sealed class PresetListBox : RowListBox
{
    private const int WM_LBUTTONDOWN = 0x0201;
    private const int WM_LBUTTONDBLCLK = 0x0203;
    private const TextFormatFlags GlyphFlags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
        | TextFormatFlags.NoPadding | TextFormatFlags.PreserveGraphicsTranslateTransform;

    private enum RowButton { Delete, Rename, Apply } // right to left

    public event EventHandler<OverlayPreset>? ApplyRequested;
    public event EventHandler<OverlayPreset>? RenameRequested;
    public event EventHandler<OverlayPreset>? DeleteRequested;

    public PresetListBox() : base(48) { }

    private Rectangle ButtonRect(int index, RowButton button)
    {
        var b = GetItemRectangle(index);
        int s = this.S(26);
        int right = b.Right - this.S(10) - (int)button * (s + this.S(4));
        return new Rectangle(right - s, b.Y + (b.Height - s) / 2, s, s);
    }

    private RowButton? ButtonAt(int index, Point p)
    {
        foreach (RowButton button in Enum.GetValues<RowButton>())
            if (ButtonRect(index, button).Contains(p)) return button;
        return null;
    }

    protected override bool HoverPartChanged(int index, Point from, Point to) => ButtonAt(index, from) != ButtonAt(index, to);

    protected override void WndProc(ref Message m)
    {
        // Handle the row's buttons before the list treats the click as a selection.
        if (m.Msg is WM_LBUTTONDOWN or WM_LBUTTONDBLCLK)
        {
            long lp = m.LParam.ToInt64();
            var pt = new Point((short)(lp & 0xFFFF), (short)((lp >> 16) & 0xFFFF));
            int index = IndexFromPoint(pt);
            if (index >= 0 && Items[index] is OverlayPreset preset && ButtonAt(index, pt) is RowButton button)
            {
                SelectedIndex = index;
                (button switch
                {
                    RowButton.Apply => ApplyRequested,
                    RowButton.Rename => RenameRequested,
                    _ => DeleteRequested,
                })?.Invoke(this, preset);
                return;
            }
        }
        base.WndProc(ref m);
    }

    protected override void DrawItemContent(DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= Items.Count || Items[e.Index] is not OverlayPreset preset)
        {
            ClearRow(e);
            return;
        }

        DrawRow(e, null, preset.Name, preset.Summary, this.S(94), this.S(22), glyph: Theme.GlyphPreset);

        bool selected = (e.State & DrawItemState.Selected) != 0;
        if (!selected && e.Index != HoverIndex) return;

        var g = e.Graphics;
        foreach (var (button, glyph, hoverColor) in new[]
        {
            (RowButton.Apply, Theme.GlyphPlay, Theme.Accent),
            (RowButton.Rename, Theme.GlyphEdit, Theme.Border),
            (RowButton.Delete, Theme.GlyphClose, Color.FromArgb(90, Theme.Danger)),
        })
        {
            var rect = ButtonRect(e.Index, button);
            bool over = rect.Contains(MousePos);
            if (over)
            {
                using var hover = new SolidBrush(hoverColor);
                g.FillEllipse(hover, rect);
            }
            var font = button == RowButton.Delete ? this.Scaled(Theme.IconSmall) : this.Scaled(Theme.Icon);
            Color color = over ? (button == RowButton.Rename ? Theme.Text : Color.White)
                : button == RowButton.Apply ? Theme.Accent : Theme.TextMuted;
            TextRenderer.DrawText(g, glyph, font, rect, color, GlyphFlags);
        }
    }
}

/// <summary>Which overlay's menu to show, and where (screen coordinates).</summary>
internal sealed record OverlayMenuRequest(OverlayItem Item, Point ScreenPoint);
