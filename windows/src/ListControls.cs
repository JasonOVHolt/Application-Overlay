using System.Drawing.Drawing2D;

namespace OverlayApp;

/// <summary>Owner-drawn list with icon + two-line rows and hover/selection styling.</summary>
internal abstract class RowListBox : ListBox
{
    private int _hoverIndex = -1;

    protected RowListBox(int itemHeight)
    {
        DrawMode = DrawMode.OwnerDrawFixed;
        BorderStyle = BorderStyle.None;
        IntegralHeight = false;
        BackColor = Theme.Surface;
        ForeColor = Theme.Text;
        Font = Theme.Body;
        ItemHeight = Math.Min(255, this.S(itemHeight));
    }

    protected int HoverIndex => _hoverIndex;
    protected Point MousePos { get; private set; } = new(-1, -1);

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.UseDarkScrollbars(Handle);
    }

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
    protected RectangleF DrawRow(DrawItemEventArgs e, Bitmap? icon, string title, string subtitle, int rightReserve, int iconSize)
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
            g.DrawImage(icon, iconRect);
        }
        else
        {
            TextRenderer.DrawText(g, Theme.GlyphApp, Theme.IconLarge, iconRect, Theme.TextMuted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.PreserveGraphicsTranslateTransform);
        }

        int textLeft = iconRect.Right + this.S(12);
        int right = (int)r.Right - this.S(12) - rightReserve;
        var flags = TextFormatFlags.NoPadding | TextFormatFlags.PreserveGraphicsTranslateTransform | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix;
        int titleH = TextRenderer.MeasureText("Ag", Theme.BodyStrong).Height;
        int subH = TextRenderer.MeasureText("Ag", Theme.Small).Height;
        int y = (int)(r.Y + (r.Height - titleH - subH) / 2);
        TextRenderer.DrawText(g, title, Theme.BodyStrong, new Rectangle(textLeft, y, right - textLeft, titleH), Theme.Text, flags);
        TextRenderer.DrawText(g, subtitle, Theme.Small, new Rectangle(textLeft, y + titleH, right - textLeft, subH), Theme.TextMuted, flags);
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
        var badgeSize = TextRenderer.MeasureText(badgeText, Theme.Small);
        int reserve = count > 0 ? badgeSize.Width + this.S(20) : 0;

        var r = DrawRow(e, w.Icon, w.Title, w.ProcessName, reserve, this.S(24));

        if (count > 0)
        {
            var g = e.Graphics;
            float right = r.Right - this.S(12);
            var badge = new RectangleF(right - badgeSize.Width - this.S(12), r.Y + (r.Height - this.S(20)) / 2,
                badgeSize.Width + this.S(12), this.S(20));
            Theme.FillRounded(g, Color.FromArgb(40, Theme.Success), badge, badge.Height / 2);
            TextRenderer.DrawText(g, badgeText, Theme.Small, Rectangle.Round(badge), Theme.Success,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.PreserveGraphicsTranslateTransform);
        }
    }
}

/// <summary>List of running overlays, each with a close button.</summary>
internal sealed class OverlayListBox : RowListBox
{
    private const int WM_LBUTTONDOWN = 0x0201;
    private const int WM_LBUTTONDBLCLK = 0x0203;
    private const string GlyphClose = "";

    public event EventHandler<OverlayItem>? RemoveRequested;

    public OverlayListBox() : base(44) { }

    private Rectangle CloseRect(int index)
    {
        var b = GetItemRectangle(index);
        int s = this.S(26);
        return new Rectangle(b.Right - s - this.S(10), b.Y + (b.Height - s) / 2, s, s);
    }

    protected override bool HoverPartChanged(int index, Point from, Point to)
    {
        var close = CloseRect(index);
        return close.Contains(from) != close.Contains(to);
    }

    protected override void WndProc(ref Message m)
    {
        // Handle the close button before the list treats the click as a selection.
        if (m.Msg is WM_LBUTTONDOWN or WM_LBUTTONDBLCLK)
        {
            long lp = m.LParam.ToInt64();
            var pt = new Point((short)(lp & 0xFFFF), (short)((lp >> 16) & 0xFFFF));
            int index = IndexFromPoint(pt);
            if (index >= 0 && CloseRect(index).Contains(pt) && Items[index] is OverlayItem item)
            {
                RemoveRequested?.Invoke(this, item);
                return;
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

        DrawRow(e, item.Icon, item.Title, item.Summary, this.S(30), this.S(20));

        bool selected = (e.State & DrawItemState.Selected) != 0;
        if (selected || e.Index == HoverIndex)
        {
            var g = e.Graphics;
            var close = CloseRect(e.Index);
            bool overClose = close.Contains(MousePos);
            if (overClose)
            {
                using var hover = new SolidBrush(Color.FromArgb(90, Theme.Danger));
                g.FillEllipse(hover, close);
            }
            TextRenderer.DrawText(g, GlyphClose, Theme.IconSmall, close, overClose ? Color.White : Theme.TextMuted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.PreserveGraphicsTranslateTransform);
        }
    }
}
