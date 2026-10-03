using System.Drawing.Drawing2D;

namespace OverlayApp;

/// <summary>Base for owner-drawn controls: double buffered and painted on the card surface.</summary>
internal abstract class DrawnControl : Control
{
    protected DrawnControl()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
               | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Surface;
        ForeColor = Theme.Text;
        Font = Theme.Body;
    }

    protected static Graphics Smooth(Graphics g)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        return g;
    }
}

/// <summary>Rounded panel used to group related controls.</summary>
internal sealed class Card : Panel
{
    public Card()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
               | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Surface;
        Padding = new Padding(this.S(16), this.S(14), this.S(16), this.S(14));
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Theme.Background);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        Theme.FillRounded(g, Theme.Surface, r, this.S(10));
        Theme.DrawRounded(g, Theme.Border, r, this.S(10));
    }
}

internal enum ButtonKind { Primary, Secondary, Danger, Ghost }

/// <summary>Flat rounded button with an optional Segoe MDL2 glyph.</summary>
internal sealed class ModernButton : DrawnControl
{
    private bool _hover, _pressed;
    private ButtonKind _kind;
    private string? _glyph;

    public ModernButton(string text, ButtonKind kind = ButtonKind.Secondary, string? glyph = null)
    {
        SetStyle(ControlStyles.Selectable | ControlStyles.StandardClick, true);
        Text = text;
        _kind = kind;
        _glyph = glyph;
        Font = Theme.BodyStrong;
        Cursor = Cursors.Hand;
        Height = this.S(36);
    }

    public ButtonKind Kind { get => _kind; set { _kind = value; Invalidate(); } }
    public string? Glyph { get => _glyph; set { _glyph = value; Invalidate(); } }

    public int PreferredWidth
    {
        get
        {
            int w = this.S(28);
            if (_glyph != null) w += TextRenderer.MeasureText(_glyph, Theme.Icon).Width;
            if (!string.IsNullOrEmpty(Text)) w += TextRenderer.MeasureText(Text, Font).Width;
            return w;
        }
    }

    protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); Invalidate(); }
    protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Cursor = Enabled ? Cursors.Hand : Cursors.Default; Invalidate(); }
    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hover = true; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover = _pressed = false; Invalidate(); }
    protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); if (e.Button == MouseButtons.Left) { _pressed = true; Invalidate(); } }
    protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); _pressed = false; Invalidate(); }
    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        if (e.KeyCode is Keys.Space or Keys.Enter) OnClick(EventArgs.Empty);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = Smooth(e.Graphics);
        g.Clear(BackColor);

        (Color fill, Color hover, Color pressed, Color text, Color? border) = _kind switch
        {
            ButtonKind.Primary => (Theme.Accent, Theme.AccentHover, Theme.AccentPressed, Color.White, (Color?)null),
            ButtonKind.Danger => (Theme.Danger, Theme.DangerHover, Theme.Danger, Color.White, null),
            ButtonKind.Ghost => (BackColor, Theme.SurfaceHover, Theme.Border, Theme.Text, null),
            _ => (Theme.SurfaceHover, Theme.Border, Theme.Track, Theme.Text, Theme.Border),
        };

        Color bg = !Enabled ? Theme.Blend(fill, BackColor, 0.55f) : _pressed ? pressed : _hover ? hover : fill;
        Color fg = Enabled ? text : Theme.Blend(text, BackColor, 0.55f);

        var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        float radius = this.S(6);
        Theme.FillRounded(g, bg, r, radius);
        if (border is Color b && Enabled) Theme.DrawRounded(g, b, r, radius);
        if (Focused && ShowFocusCues) Theme.DrawRounded(g, Theme.AccentHover, RectangleF.Inflate(r, -1, -1), radius, 2);

        // Glyph + text, centred together.
        var flags = TextFormatFlags.NoPadding | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine;
        int gap = this.S(8);
        Size glyphSize = _glyph != null ? TextRenderer.MeasureText(g, _glyph, Theme.Icon, Size.Empty, flags) : Size.Empty;
        Size textSize = string.IsNullOrEmpty(Text) ? Size.Empty : TextRenderer.MeasureText(g, Text, Font, Size.Empty, flags);
        int total = glyphSize.Width + textSize.Width + (glyphSize.Width > 0 && textSize.Width > 0 ? gap : 0);
        int x = (Width - total) / 2;

        if (_glyph != null)
        {
            TextRenderer.DrawText(g, _glyph, Theme.Icon, new Rectangle(x, 0, glyphSize.Width, Height), fg, flags);
            x += glyphSize.Width + gap;
        }
        if (textSize.Width > 0)
            TextRenderer.DrawText(g, Text, Font, new Rectangle(x, 0, textSize.Width + 2, Height), fg, flags);
    }
}

/// <summary>On/off switch with a title and an optional description line.</summary>
internal sealed class ToggleSwitch : DrawnControl
{
    private readonly System.Windows.Forms.Timer _anim = new() { Interval = 15 };
    private bool _checked;
    private float _pos; // 0 = off, 1 = on (animated)
    private bool _hover;

    public event EventHandler? CheckedChanged;

    public ToggleSwitch(string text, string? description = null)
    {
        SetStyle(ControlStyles.Selectable | ControlStyles.StandardClick, true);
        Text = text;
        Description = description;
        Cursor = Cursors.Hand;
        Height = this.S(description == null ? 30 : 44);
        _anim.Tick += (_, _) =>
        {
            float target = _checked ? 1f : 0f;
            _pos += Math.Sign(target - _pos) * 0.2f;
            if (Math.Abs(target - _pos) < 0.2f) { _pos = target; _anim.Stop(); }
            Invalidate();
        };
    }

    public string? Description { get; set; }

    public bool Checked
    {
        get => _checked;
        set
        {
            if (_checked == value) return;
            _checked = value;
            if (IsHandleCreated && Visible) _anim.Start(); else _pos = value ? 1f : 0f;
            Invalidate();
            CheckedChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _anim.Dispose();
        base.Dispose(disposing);
    }

    protected override void OnClick(EventArgs e) { base.OnClick(e); Focus(); Checked = !Checked; }
    protected override void OnKeyUp(KeyEventArgs e) { base.OnKeyUp(e); if (e.KeyCode == Keys.Space) Checked = !Checked; }
    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hover = true; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover = false; Invalidate(); }
    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = Smooth(e.Graphics);
        g.Clear(BackColor);

        int sw = this.S(40), sh = this.S(22);
        var track = new RectangleF(Width - sw - 1, (Height - sh) / 2f, sw, sh);
        Color off = _hover ? Theme.Border : Theme.Track;
        Theme.FillRounded(g, Theme.Blend(off, Theme.Accent, _pos), track, sh / 2f);
        if (Focused && ShowFocusCues) Theme.DrawRounded(g, Theme.AccentHover, RectangleF.Inflate(track, 2, 2), sh / 2f + 2, 1.5f);

        float knob = sh - this.S(6);
        float kx = track.X + this.S(3) + (sw - knob - this.S(6)) * _pos;
        using (var white = new SolidBrush(Color.White))
            g.FillEllipse(white, kx, track.Y + this.S(3), knob, knob);

        var textArea = new Rectangle(0, 0, Width - sw - this.S(12), Height);
        var flags = TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine;
        if (Description == null)
        {
            TextRenderer.DrawText(g, Text, Theme.Body, textArea, Theme.Text, flags | TextFormatFlags.VerticalCenter);
        }
        else
        {
            int titleH = TextRenderer.MeasureText(Text, Theme.Body).Height;
            int descH = TextRenderer.MeasureText(Description, Theme.Small).Height;
            int y = (Height - titleH - descH) / 2;
            TextRenderer.DrawText(g, Text, Theme.Body, new Rectangle(0, y, textArea.Width, titleH), Theme.Text, flags);
            TextRenderer.DrawText(g, Description, Theme.Small, new Rectangle(0, y + titleH, textArea.Width, descH), Theme.TextMuted, flags);
        }
    }
}

/// <summary>Horizontal slider with an accent-filled track.</summary>
internal sealed class Slider : DrawnControl
{
    private int _min, _max = 100, _value;
    private bool _dragging, _hover;

    public event EventHandler? ValueChanged;

    public Slider(int min, int max, int value)
    {
        SetStyle(ControlStyles.Selectable, true);
        _min = min;
        _max = max;
        _value = Math.Clamp(value, min, max);
        Cursor = Cursors.Hand;
        Height = this.S(26);
    }

    public int Value
    {
        get => _value;
        set
        {
            value = Math.Clamp(value, _min, _max);
            if (value == _value) return;
            _value = value;
            Invalidate();
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private float Radius => this.S(8);
    private float TrackLeft => Radius + 1;
    private float TrackWidth => Math.Max(1, Width - 2 * Radius - 2);

    private void SetFromX(int x) =>
        Value = _min + (int)Math.Round((x - TrackLeft) / TrackWidth * (_max - _min));

    protected override bool IsInputKey(Keys keyData) =>
        keyData is Keys.Left or Keys.Right or Keys.Up or Keys.Down || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        switch (e.KeyCode)
        {
            case Keys.Left or Keys.Down: Value--; break;
            case Keys.Right or Keys.Up: Value++; break;
            case Keys.PageDown: Value -= 10; break;
            case Keys.PageUp: Value += 10; break;
            case Keys.Home: Value = _min; break;
            case Keys.End: Value = _max; break;
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        Focus();
        _dragging = true;
        SetFromX(e.X);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragging) SetFromX(e.X);
    }

    protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); _dragging = false; Invalidate(); }
    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hover = true; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover = false; Invalidate(); }
    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        Value += Math.Sign(e.Delta) * Math.Max(1, (_max - _min) / 20);
        if (e is HandledMouseEventArgs h) h.Handled = true;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = Smooth(e.Graphics);
        g.Clear(BackColor);

        float th = this.S(4);
        float cy = Height / 2f;
        float t = _max == _min ? 0 : (_value - _min) / (float)(_max - _min);
        float thumbX = TrackLeft + TrackWidth * t;

        Theme.FillRounded(g, Theme.Track, new RectangleF(TrackLeft, cy - th / 2, TrackWidth, th), th / 2);
        if (thumbX > TrackLeft)
            Theme.FillRounded(g, Enabled ? Theme.Accent : Theme.TextMuted, new RectangleF(TrackLeft, cy - th / 2, thumbX - TrackLeft, th), th / 2);

        float r = Radius * (_dragging ? 1.1f : 1f);
        if (_hover || _dragging || (Focused && ShowFocusCues))
        {
            using var halo = new SolidBrush(Color.FromArgb(60, Theme.Accent));
            g.FillEllipse(halo, thumbX - r - 4, cy - r - 4, 2 * r + 8, 2 * r + 8);
        }
        using var thumb = new SolidBrush(Color.White);
        g.FillEllipse(thumb, thumbX - r, cy - r, 2 * r, 2 * r);
        using var ring = new Pen(Theme.Accent, this.S(2));
        g.DrawEllipse(ring, thumbX - r + 1, cy - r + 1, 2 * r - 2, 2 * r - 2);
    }
}

/// <summary>Rounded host that gives a borderless TextBox a themed frame and optional icon.</summary>
internal sealed class TextHost : Panel
{
    private readonly string? _glyph;

    public TextHost(TextBox box, string? glyph = null)
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
               | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Box = box;
        _glyph = glyph;
        BackColor = Theme.Surface;
        Height = this.S(34);
        Cursor = Cursors.IBeam;

        box.BorderStyle = BorderStyle.None;
        box.BackColor = Theme.Input;
        box.ForeColor = Theme.Text;
        box.Font = Theme.Body;
        box.GotFocus += (_, _) => Invalidate();
        box.LostFocus += (_, _) => Invalidate();
        Controls.Add(box);
        Click += (_, _) => box.Focus();
    }

    public TextBox Box { get; }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        int left = this.S(10) + (_glyph != null ? this.S(22) : 0);
        Box.SetBounds(left, (Height - Box.Height) / 2, Math.Max(10, Width - left - this.S(10)), Box.Height);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Theme.Surface);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        Theme.FillRounded(g, Theme.Input, r, this.S(6));
        Theme.DrawRounded(g, Box.Focused ? Theme.Accent : Theme.Border, r, this.S(6), Box.Focused ? 1.5f : 1f);

        if (_glyph != null)
        {
            TextRenderer.DrawText(g, _glyph, Theme.Icon, new Rectangle(this.S(10), 0, this.S(20), Height),
                Theme.TextMuted, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }
}

internal enum StatusKind { Idle, Live, Warning }

/// <summary>Bottom status line with a coloured indicator dot.</summary>
internal sealed class StatusBar : DrawnControl
{
    private StatusKind _kind;

    public StatusBar()
    {
        BackColor = Theme.Background;
        Height = this.S(28);
    }

    public void Set(string text, StatusKind kind)
    {
        Text = text;
        _kind = kind;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = Smooth(e.Graphics);
        g.Clear(BackColor);

        Color dot = _kind switch
        {
            StatusKind.Live => Theme.Success,
            StatusKind.Warning => Theme.Warning,
            _ => Theme.TextMuted,
        };
        float d = this.S(8);
        float cy = Height / 2f;
        if (_kind == StatusKind.Live)
        {
            using var glow = new SolidBrush(Color.FromArgb(60, dot));
            g.FillEllipse(glow, 2 - 3, cy - d / 2 - 3, d + 6, d + 6);
        }
        using (var b = new SolidBrush(dot)) g.FillEllipse(b, 2, cy - d / 2, d, d);

        var textRect = new Rectangle((int)(d + this.S(10)), 0, Width - (int)d - this.S(10), Height);
        TextRenderer.DrawText(g, Text, Theme.Body, textRect, _kind == StatusKind.Idle ? Theme.TextMuted : Theme.Text,
            TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
    }
}

/// <summary>Owner-drawn list of windows with app icons, process names and a LIVE badge.</summary>
internal sealed class WindowListBox : ListBox
{
    private int _hoverIndex = -1;
    private IntPtr _liveHandle;

    public WindowListBox()
    {
        DrawMode = DrawMode.OwnerDrawFixed;
        BorderStyle = BorderStyle.None;
        IntegralHeight = false;
        BackColor = Theme.Surface;
        ForeColor = Theme.Text;
        Font = Theme.Body;
        ItemHeight = Math.Min(255, this.S(48));
    }

    /// <summary>The window currently being mirrored (gets a LIVE badge).</summary>
    public IntPtr LiveHandle
    {
        get => _liveHandle;
        set { _liveHandle = value; Invalidate(); }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.UseDarkScrollbars(Handle);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int index = IndexFromPoint(e.Location);
        if (index == _hoverIndex) return;
        InvalidateItem(_hoverIndex);
        _hoverIndex = index;
        InvalidateItem(_hoverIndex);
        Cursor = index >= 0 ? Cursors.Hand : Cursors.Default;
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        InvalidateItem(_hoverIndex);
        _hoverIndex = -1;
    }

    private void InvalidateItem(int index)
    {
        if (index >= 0 && index < Items.Count) Invalidate(GetItemRectangle(index));
    }

    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var bg = new SolidBrush(BackColor)) g.FillRectangle(bg, e.Bounds);
        if (e.Index < 0 || e.Index >= Items.Count || Items[e.Index] is not WindowInfo w) return;

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

        // Icon
        int iconSize = this.S(24);
        var iconRect = new Rectangle((int)r.X + this.S(12), (int)(r.Y + (r.Height - iconSize) / 2), iconSize, iconSize);
        if (w.Icon != null)
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(w.Icon, iconRect);
        }
        else
        {
            TextRenderer.DrawText(g, Theme.GlyphApp, Theme.IconLarge, iconRect, Theme.TextMuted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }

        // LIVE badge
        int right = (int)r.Right - this.S(12);
        if (w.Handle == _liveHandle)
        {
            const string live = "LIVE";
            var size = TextRenderer.MeasureText(live, Theme.Small);
            var badge = new RectangleF(right - size.Width - this.S(12), r.Y + (r.Height - this.S(20)) / 2, size.Width + this.S(12), this.S(20));
            Theme.FillRounded(g, Color.FromArgb(40, Theme.Success), badge, badge.Height / 2);
            TextRenderer.DrawText(g, live, Theme.Small, Rectangle.Round(badge), Theme.Success,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            right = (int)badge.X - this.S(8);
        }

        // Title + process name
        int textLeft = iconRect.Right + this.S(12);
        var flags = TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix;
        int titleH = TextRenderer.MeasureText("Ag", Theme.BodyStrong).Height;
        int procH = TextRenderer.MeasureText("Ag", Theme.Small).Height;
        int y = (int)(r.Y + (r.Height - titleH - procH) / 2);
        TextRenderer.DrawText(g, w.Title, Theme.BodyStrong, new Rectangle(textLeft, y, right - textLeft, titleH), Theme.Text, flags);
        TextRenderer.DrawText(g, w.ProcessName, Theme.Small, new Rectangle(textLeft, y + titleH, right - textLeft, procH), Theme.TextMuted, flags);
    }
}
