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

    protected override void OnEnabledChanged(EventArgs e)
    {
        base.OnEnabledChanged(e);
        Invalidate();
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
        Padding = new Padding(16, 14, 16, 14); // 96-DPI units; AutoScaleMode.Dpi scales it
    }

    /// <summary>Draws the border in the accent colour, to make the card stand out (e.g. the update banner).</summary>
    public bool Accented { get; set; }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Theme.Background);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        Theme.FillRounded(g, Theme.Surface, r, this.S(10));
        Theme.DrawRounded(g, Accented ? Theme.Accent : Theme.Border, r, this.S(10));
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
        Height = 36; // 96-DPI units
    }

    public ButtonKind Kind { get => _kind; set { _kind = value; Invalidate(); } }
    public string? Glyph { get => _glyph; set { _glyph = value; Invalidate(); } }

    /// <summary>Draws a slash through the glyph (e.g. eye → "hidden").</summary>
    public bool GlyphSlashed { get => _glyphSlashed; set { _glyphSlashed = value; Invalidate(); } }
    private bool _glyphSlashed;

    /// <summary><see cref="PreferredWidth"/> in 96-DPI units, for building layouts that AutoScaleMode.Dpi then scales.</summary>
    public int LogicalPreferredWidth => (int)Math.Ceiling(PreferredWidth * 96.0 / DeviceDpi);

    /// <summary>Width that fits the glyph and text, in current pixels.</summary>
    public int PreferredWidth
    {
        get
        {
            int w = this.S(28);
            if (_glyph != null) w += TextRenderer.MeasureText(_glyph, this.Scaled(Theme.Icon)).Width;
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
            ButtonKind.Primary => (Theme.Accent, Theme.AccentHover, Theme.AccentPressed, Theme.OnAccent, (Color?)null),
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
        Size glyphSize = _glyph != null ? TextRenderer.MeasureText(g, _glyph, this.Scaled(Theme.Icon), Size.Empty, flags) : Size.Empty;
        Size textSize = string.IsNullOrEmpty(Text) ? Size.Empty : TextRenderer.MeasureText(g, Text, Font, Size.Empty, flags);
        int total = glyphSize.Width + textSize.Width + (glyphSize.Width > 0 && textSize.Width > 0 ? gap : 0);
        int x = (Width - total) / 2;

        if (_glyph != null)
        {
            TextRenderer.DrawText(g, _glyph, this.Scaled(Theme.Icon), new Rectangle(x, 0, glyphSize.Width, Height), fg, flags);
            if (_glyphSlashed)
            {
                float side = Math.Min(glyphSize.Width, glyphSize.Height) * 0.8f;
                var box = new RectangleF(x + (glyphSize.Width - side) / 2, (Height - side) / 2, side, side);
                Theme.DrawSlash(g, box, fg, bg, Math.Max(1f, this.S(3) / 2f));
            }
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
        Height = description == null ? 30 : 44; // 96-DPI units
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
        Color off = _hover && Enabled ? Theme.Border : Theme.Track;
        Color on = Enabled ? Theme.Accent : Theme.Blend(Theme.Accent, BackColor, 0.6f);
        Theme.FillRounded(g, Theme.Blend(off, on, _pos), track, sh / 2f);
        if (Focused && ShowFocusCues) Theme.DrawRounded(g, Theme.AccentHover, RectangleF.Inflate(track, 2, 2), sh / 2f + 2, 1.5f);

        float knob = sh - this.S(6);
        float kx = track.X + this.S(3) + (sw - knob - this.S(6)) * _pos;
        using (var knobBrush = new SolidBrush(Enabled ? Theme.Blend(Color.White, Theme.OnAccent, _pos) : Theme.TextMuted))
            g.FillEllipse(knobBrush, kx, track.Y + this.S(3), knob, knob);

        var textArea = new Rectangle(0, 0, Width - sw - this.S(12), Height);
        var flags = TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine;
        Color textColor = Enabled ? Theme.Text : Theme.TextMuted;
        if (Description == null)
        {
            TextRenderer.DrawText(g, Text, this.Scaled(Theme.Body), textArea, textColor, flags | TextFormatFlags.VerticalCenter);
        }
        else
        {
            int titleH = TextRenderer.MeasureText(Text, this.Scaled(Theme.Body)).Height;
            int descH = TextRenderer.MeasureText(Description, this.Scaled(Theme.Small)).Height;
            int y = (Height - titleH - descH) / 2;
            TextRenderer.DrawText(g, Text, this.Scaled(Theme.Body), new Rectangle(0, y, textArea.Width, titleH), textColor, flags);
            TextRenderer.DrawText(g, Description, this.Scaled(Theme.Small), new Rectangle(0, y + titleH, textArea.Width, descH), Theme.TextMuted, flags);
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
        Height = 26; // 96-DPI units
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
        Height = 34; // 96-DPI units
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
            TextRenderer.DrawText(g, _glyph, this.Scaled(Theme.Icon), new Rectangle(this.S(10), 0, this.S(20), Height),
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
        Height = 28; // 96-DPI units
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
        TextRenderer.DrawText(g, Text, this.Scaled(Theme.Body), textRect, _kind == StatusKind.Idle ? Theme.TextMuted : Theme.Text,
            TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
    }
}

/// <summary>
/// One row: an item on the left, a square button on the right, and a middle item filling the space
/// between. Laid out by hand rather than with a TableLayoutPanel, whose auto-sized columns drift each
/// time WinForms rescales the window for a monitor with different scaling.
/// </summary>
internal sealed class ToolbarRow : Panel
{
    private readonly Control _left, _fill, _right;
    private readonly int _leftGap, _rightGap; // 96-DPI units

    public ToolbarRow(Control left, Control fill, Control right, int leftGap, int rightGap)
    {
        _left = left;
        _fill = fill;
        _right = right;
        _leftGap = leftGap;
        _rightGap = rightGap;
        BackColor = Theme.Surface;
        Controls.AddRange(new[] { left, fill, right });
    }

    // Report our own height so an auto-sized table row fits us exactly.
    public override Size GetPreferredSize(Size proposedSize) => new(Width, Height);

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        int h = ClientSize.Height;
        var leftSize = _left.GetPreferredSize(Size.Empty);
        _left.SetBounds(0, (h - leftSize.Height) / 2, leftSize.Width, leftSize.Height);
        _right.SetBounds(ClientSize.Width - h, 0, h, h);
        // A hidden right button gives its space to the middle item.
        int x = _left.Right + this.S(_leftGap), right = _right.Visible ? _right.Left - this.S(_rightGap) : ClientSize.Width;
        _fill.SetBounds(x, 0, Math.Max(0, right - x), h);
    }
}

/// <summary>
/// Stacks its children top to bottom at full width, scrolling vertically only (a vertical scrollbar
/// never forces a horizontal one). Laid out by hand so it stays right across monitor scaling changes.
/// </summary>
internal sealed class CardStack : Panel
{
    private readonly int _gap; // 96-DPI units
    private bool _arranging;

    public CardStack(int gap)
    {
        _gap = gap;
        AutoScroll = true;
        BackColor = Theme.Background;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.ApplyScrollbars(Handle);
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        if (!_arranging)
        {
            _arranging = true;
            try
            {
                int gap = this.S(_gap), total = -gap;
                foreach (Control child in Controls) total += child.Height + gap;

                // Decide up front whether a vertical scrollbar is needed and leave room for it. Sizing the
                // cards to the current client width instead lags one step behind the scrollbar appearing,
                // which made a horizontal scrollbar show up too.
                bool needsVertical = total > Height;
                int width = Width - (needsVertical ? SystemInformation.GetVerticalScrollBarWidthForDpi(DeviceDpi) : 0);

                int y = AutoScrollPosition.Y;
                foreach (Control child in Controls)
                {
                    child.SetBounds(0, y, width, child.Height);
                    y += child.Height + gap;
                }
                AutoScrollMinSize = new Size(0, Math.Max(0, total));
            }
            finally
            {
                _arranging = false;
            }
        }
        base.OnLayout(levent);
    }
}

/// <summary>Text tabs with an accent underline under the selected one (e.g. "Windows | Presets").</summary>
internal sealed class TabStrip : DrawnControl
{
    private readonly string[] _tabs;
    private int _selected;
    private int _hover = -1;

    public event EventHandler? SelectedIndexChanged;

    public TabStrip(params string[] tabs)
    {
        SetStyle(ControlStyles.Selectable | ControlStyles.StandardClick, true);
        _tabs = tabs;
        Font = Theme.Section; // WinForms rescales a control's own Font for each monitor
        Cursor = Cursors.Hand;
        Height = 34; // 96-DPI units
    }

    public int SelectedIndex
    {
        get => _selected;
        set
        {
            value = Math.Clamp(value, 0, _tabs.Length - 1);
            if (value == _selected) return;
            _selected = value;
            Invalidate();
            SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void SetTabText(int index, string text)
    {
        _tabs[index] = text;
        Invalidate();
        Parent?.PerformLayout(); // width may change
    }

    private Rectangle[] TabRects()
    {
        var rects = new Rectangle[_tabs.Length];
        int x = 0;
        for (int i = 0; i < _tabs.Length; i++)
        {
            int w = TextRenderer.MeasureText(_tabs[i], Font).Width;
            rects[i] = new Rectangle(x, 0, w, Height);
            x += w + this.S(18);
        }
        return rects;
    }

    public override Size GetPreferredSize(Size proposedSize) => new(TabRects()[^1].Right, Height);

    protected override bool IsInputKey(Keys keyData) => keyData is Keys.Left or Keys.Right || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.Left) SelectedIndex--;
        else if (e.KeyCode == Keys.Right) SelectedIndex++;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int hover = Array.FindIndex(TabRects(), r => r.Contains(e.Location));
        if (hover != _hover) { _hover = hover; Invalidate(); }
    }

    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover = -1; Invalidate(); }
    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        int index = Array.FindIndex(TabRects(), r => r.Contains(e.Location));
        if (index >= 0) SelectedIndex = index;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = Smooth(e.Graphics);
        g.Clear(BackColor);
        var rects = TabRects();
        for (int i = 0; i < _tabs.Length; i++)
        {
            Color color = i == _selected ? Theme.Text : i == _hover ? Theme.Blend(Theme.TextMuted, Theme.Text, 0.5f) : Theme.TextMuted;
            TextRenderer.DrawText(g, _tabs[i], Font, rects[i], color,
                TextFormatFlags.NoPadding | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        }
        var sel = rects[_selected];
        float lineH = this.S(3);
        Theme.FillRounded(g, Theme.Accent, new RectangleF(sel.X, Height - lineH - this.S(1), sel.Width, lineH), lineH / 2);
        if (Focused && ShowFocusCues)
            Theme.DrawRounded(g, Theme.AccentHover, new RectangleF(sel.X - 3, 1, sel.Width + 6, Height - 3), this.S(4), 1.5f);
    }
}

/// <summary>A row of mutually exclusive options, e.g. "Dark | Light | Match Windows".</summary>
internal sealed class SegmentedControl : DrawnControl
{
    private readonly string[] _options;
    private int _selected, _hover = -1;

    public event EventHandler? SelectedIndexChanged;

    public SegmentedControl(params string[] options)
    {
        SetStyle(ControlStyles.Selectable | ControlStyles.StandardClick, true);
        _options = options;
        Cursor = Cursors.Hand;
        Height = 32; // 96-DPI units
        AccessibleRole = AccessibleRole.PageTabList;
    }

    public int SelectedIndex
    {
        get => _selected;
        set
        {
            value = Math.Clamp(value, 0, _options.Length - 1);
            if (value == _selected) return;
            _selected = value;
            Invalidate();
            SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private RectangleF Segment(int i)
    {
        float w = (Width - 1f) / _options.Length;
        return new RectangleF(i * w + 0.5f, 0.5f, w, Height - 1.5f);
    }

    private int IndexAt(Point p)
    {
        for (int i = 0; i < _options.Length; i++) if (Segment(i).Contains(p)) return i;
        return -1;
    }

    protected override bool IsInputKey(Keys keyData) => keyData is Keys.Left or Keys.Right || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.Left) SelectedIndex--;
        else if (e.KeyCode == Keys.Right) SelectedIndex++;
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        int i = IndexAt(e.Location);
        if (i >= 0) SelectedIndex = i;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int i = IndexAt(e.Location);
        if (i != _hover) { _hover = i; Invalidate(); }
    }

    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover = -1; Invalidate(); }
    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = Smooth(e.Graphics);
        g.Clear(BackColor);
        float radius = this.S(6);
        var all = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        Theme.FillRounded(g, Theme.Input, all, radius);
        Theme.DrawRounded(g, Theme.Border, all, radius);

        var flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine
                  | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis;
        for (int i = 0; i < _options.Length; i++)
        {
            var r = RectangleF.Inflate(Segment(i), -this.S(3), -this.S(3));
            if (i == _selected) Theme.FillRounded(g, Theme.Accent, r, radius - 2);
            else if (i == _hover) Theme.FillRounded(g, Theme.SurfaceHover, r, radius - 2);
            Color text = i == _selected ? Theme.OnAccent : Enabled ? Theme.Text : Theme.TextMuted;
            TextRenderer.DrawText(g, _options[i], this.Scaled(Theme.Body), Rectangle.Round(r), text, flags);
        }
        if (Focused && ShowFocusCues) Theme.DrawRounded(g, Theme.AccentHover, RectangleF.Inflate(all, -1, -1), radius, 1.5f);
    }
}

/// <summary>
/// Round swatches for the accent colour: the presets, then a "custom" swatch that asks for the colour
/// picker (it shows the last custom colour, or a rainbow ring with a plus before one is chosen).
/// </summary>
internal sealed class AccentPicker : DrawnControl
{
    private static readonly Color[] Rainbow =
    {
        Color.FromArgb(255, 72, 72), Color.FromArgb(255, 190, 40), Color.FromArgb(90, 210, 80),
        Color.FromArgb(40, 200, 220), Color.FromArgb(70, 120, 255), Color.FromArgb(200, 80, 240),
    };

    private int _selected, _hover = -1;
    private Color? _custom;

    public event EventHandler? SelectedChanged;

    /// <summary>The custom swatch was clicked (or Enter pressed on it): show the colour picker.</summary>
    public event EventHandler? CustomRequested;

    public event EventHandler? HoverChanged;

    public AccentPicker()
    {
        SetStyle(ControlStyles.Selectable | ControlStyles.StandardClick, true);
        Cursor = Cursors.Hand;
        Height = 32; // 96-DPI units
    }

    private static int CustomIndex => Theme.Accents.Length;

    /// <summary>A preset name, or <see cref="Theme.CustomAccentName"/>.</summary>
    public string SelectedName
    {
        get => _selected == CustomIndex ? Theme.CustomAccentName : Theme.Accents[_selected].Name;
        set
        {
            if (string.Equals(value, Theme.CustomAccentName, StringComparison.OrdinalIgnoreCase) && _custom != null)
            {
                Select(CustomIndex);
                return;
            }
            int i = Array.FindIndex(Theme.Accents, a => string.Equals(a.Name, value, StringComparison.OrdinalIgnoreCase));
            Select(Math.Max(0, i));
        }
    }

    /// <summary>The colour shown in the custom swatch (null until one has been picked).</summary>
    public Color? CustomColor
    {
        get => _custom;
        set { _custom = value; Invalidate(); }
    }

    /// <summary>Name of the swatch under the mouse, for a tooltip.</summary>
    public string? HoverName => _hover < 0 ? null
        : _hover == CustomIndex ? (_custom is Color c ? $"Custom ({Theme.ToHex(c)}) Â· click to change" : "Pick any colourâ€¦")
        : Theme.Accents[_hover].Name;

    private void Select(int index)
    {
        index = Math.Clamp(index, 0, CustomIndex);
        if (index == CustomIndex && _custom == null) return;
        if (index == _selected) return;
        _selected = index;
        Invalidate();
        SelectedChanged?.Invoke(this, EventArgs.Empty);
    }

    private Rectangle Swatch(int i)
    {
        int d = this.S(24), gap = this.S(12);
        return new Rectangle(this.S(3) + i * (d + gap), (Height - d) / 2, d, d);
    }

    private int IndexAt(Point p)
    {
        for (int i = 0; i <= CustomIndex; i++)
            if (Rectangle.Inflate(Swatch(i), this.S(4), this.S(4)).Contains(p)) return i;
        return -1;
    }

    protected override bool IsInputKey(Keys keyData) => keyData is Keys.Left or Keys.Right || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.Left) Select(_selected - 1);
        else if (e.KeyCode == Keys.Right)
        {
            if (_selected == CustomIndex - 1 && _custom == null) CustomRequested?.Invoke(this, EventArgs.Empty);
            else Select(_selected + 1);
        }
        else if (e.KeyCode is Keys.Enter or Keys.Space && _selected == CustomIndex) CustomRequested?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        int i = IndexAt(e.Location);
        if (i == CustomIndex) CustomRequested?.Invoke(this, EventArgs.Empty);
        else if (i >= 0) Select(i);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int i = IndexAt(e.Location);
        if (i == _hover) return;
        _hover = i;
        Invalidate();
        HoverChanged?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover = -1; Invalidate(); }
    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = Smooth(e.Graphics);
        g.Clear(BackColor);
        for (int i = 0; i <= CustomIndex; i++)
        {
            var r = Swatch(i);
            Color fill;
            if (i < CustomIndex)
            {
                fill = Theme.Accents[i].Color;
                using var brush = new SolidBrush(fill);
                g.FillEllipse(brush, r);
            }
            else if (_custom is Color custom)
            {
                // The picked colour inside a thin rainbow ring, so it still reads as "custom".
                fill = custom;
                DrawRainbowRing(g, r, this.S(3));
                using var brush = new SolidBrush(custom);
                g.FillEllipse(brush, Rectangle.Inflate(r, -this.S(4), -this.S(4)));
            }
            else
            {
                fill = BackColor;
                DrawRainbowRing(g, r, this.S(4));
                float c = r.Width / 2f, arm = r.Width * 0.2f;
                using var plus = new Pen(Theme.Text, Math.Max(1.5f, this.S(2)));
                g.DrawLine(plus, r.X + c - arm, r.Y + c, r.X + c + arm, r.Y + c);
                g.DrawLine(plus, r.X + c, r.Y + c - arm, r.X + c, r.Y + c + arm);
            }

            if (i == _selected || i == _hover)
            {
                var ring = Rectangle.Inflate(r, this.S(3), this.S(3));
                using var pen = new Pen(i == _selected ? Theme.Text : Theme.Track, this.S(2));
                g.DrawEllipse(pen, ring);
            }
            if (i == _selected)
            {
                using var check = new Pen(Theme.OnColor(fill), Math.Max(1.5f, this.S(2)));
                float cx = r.X + r.Width / 2f, cy = r.Y + r.Height / 2f, u = r.Width / 8f;
                g.DrawLines(check, new[] { new PointF(cx - 2.2f * u, cy), new PointF(cx - 0.6f * u, cy + 1.6f * u), new PointF(cx + 2.4f * u, cy - 1.8f * u) });
            }
        }
        if (Focused && ShowFocusCues)
        {
            var r = Rectangle.Inflate(Swatch(_selected), this.S(6), this.S(6));
            using var pen = new Pen(Theme.AccentHover, 1.5f) { DashStyle = DashStyle.Dot };
            g.DrawEllipse(pen, r);
        }
    }

    private static void DrawRainbowRing(Graphics g, Rectangle r, int width)
    {
        var ring = RectangleF.Inflate(r, -width / 2f, -width / 2f);
        float sweep = 360f / Rainbow.Length;
        for (int i = 0; i < Rainbow.Length; i++)
        {
            using var pen = new Pen(Rainbow[i], width);
            g.DrawArc(pen, ring, -90 + i * sweep, sweep + 1);
        }
    }
}

/// <summary>Title (and optional description) for the setting below it, with an optional value on the right.</summary>
internal sealed class FieldHeader : DrawnControl
{
    private string? _value;

    public FieldHeader(string text, string? description = null)
    {
        Text = text;
        Description = description;
        Height = description == null ? 26 : 42; // 96-DPI units
    }

    public string? Description { get; }

    /// <summary>Shown right-aligned, e.g. "20 px".</summary>
    public string? Value { get => _value; set { _value = value; Invalidate(); } }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        var flags = TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine;
        int valueWidth = 0;
        if (!string.IsNullOrEmpty(_value))
        {
            valueWidth = TextRenderer.MeasureText(_value, this.Scaled(Theme.BodyStrong)).Width;
            int h = TextRenderer.MeasureText(Text, this.Scaled(Theme.Body)).Height;
            int y = Description == null ? (Height - h) / 2 : this.S(4);
            TextRenderer.DrawText(g, _value, this.Scaled(Theme.BodyStrong), new Rectangle(Width - valueWidth, y, valueWidth, h), Theme.Text, flags);
            valueWidth += this.S(10);
        }

        var area = new Rectangle(0, 0, Width - valueWidth, Height);
        if (Description == null)
        {
            TextRenderer.DrawText(g, Text, this.Scaled(Theme.Body), area, Theme.Text, flags | TextFormatFlags.VerticalCenter);
            return;
        }
        int titleH = TextRenderer.MeasureText(Text, this.Scaled(Theme.Body)).Height;
        int descH = TextRenderer.MeasureText(Description, this.Scaled(Theme.Small)).Height;
        TextRenderer.DrawText(g, Text, this.Scaled(Theme.Body), new Rectangle(0, this.S(4), area.Width, titleH), Theme.Text, flags);
        TextRenderer.DrawText(g, Description, this.Scaled(Theme.Small), new Rectangle(0, this.S(4) + titleH, Width, descH), Theme.TextMuted, flags);
    }
}

/// <summary>Draws context menus (overlay right-click, notification area icon) in the app's theme.</summary>
internal sealed class ThemedMenuRenderer : ToolStripProfessionalRenderer
{
    public ThemedMenuRenderer() : base(new Colors()) { RoundedEdges = false; }

    /// <summary>Applies the renderer and theme colours to a menu (call again after the theme changes).</summary>
    public static void Apply(ToolStripDropDown menu)
    {
        menu.Renderer = new ThemedMenuRenderer();
        menu.BackColor = Theme.Surface;
        menu.ForeColor = Theme.Text;
        menu.Font = Theme.Body;
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = e.Item.Enabled ? Theme.Text : Theme.TextMuted;
        base.OnRenderItemText(e);
    }

    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
    {
        e.ArrowColor = Theme.TextMuted;
        base.OnRenderArrow(e);
    }

    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
    {
        // A check in the theme's colours (the default image is black, invisible on a dark menu).
        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        var r = e.ImageRectangle;
        Theme.FillRounded(g, Color.FromArgb(60, Theme.Accent), r, 3);
        using var pen = new Pen(Theme.Accent, Math.Max(1.5f, r.Height / 9f));
        float u = r.Width / 8f, cx = r.X + r.Width / 2f, cy = r.Y + r.Height / 2f;
        g.DrawLines(pen, new[] { new PointF(cx - 2.2f * u, cy), new PointF(cx - 0.6f * u, cy + 1.6f * u), new PointF(cx + 2.4f * u, cy - 1.8f * u) });
    }

    private sealed class Colors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => Theme.Surface;
        public override Color ImageMarginGradientBegin => Theme.Surface;
        public override Color ImageMarginGradientMiddle => Theme.Surface;
        public override Color ImageMarginGradientEnd => Theme.Surface;
        public override Color MenuBorder => Theme.Border;
        public override Color MenuItemBorder => Theme.SurfaceHover;
        public override Color MenuItemSelected => Theme.SurfaceHover;
        public override Color MenuItemSelectedGradientBegin => Theme.SurfaceHover;
        public override Color MenuItemSelectedGradientEnd => Theme.SurfaceHover;
        public override Color MenuItemPressedGradientBegin => Theme.Border;
        public override Color MenuItemPressedGradientEnd => Theme.Border;
        public override Color SeparatorDark => Theme.Border;
        public override Color SeparatorLight => Theme.Surface;
        public override Color CheckBackground => Theme.Surface;
        public override Color CheckSelectedBackground => Theme.SurfaceHover;
        public override Color CheckPressedBackground => Theme.Border;
    }
}
