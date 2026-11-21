using System.Drawing.Drawing2D;

namespace OverlayApp;

/// <summary>
/// Themed colour picker: a saturation/brightness square, a hue bar and a hex box, with the old and
/// new colours side by side. <see cref="ColorChanged"/> fires (at most every 40 ms) while the colour
/// is being chosen so the caller can preview it.
/// </summary>
internal sealed class ColorPickerDialog : Form
{
    private readonly SatValBox _box = new();
    private readonly HueBar _hue = new();
    private readonly TextBox _hex = new() { MaxLength = 9 };
    private readonly Label _note;
    private readonly ColorPreview _preview;
    private readonly ModernButton _ok = new("Use colour", ButtonKind.Primary);
    private readonly System.Windows.Forms.Timer _notify = new() { Interval = 40 };
    private bool _pending, _updatingHex;

    /// <summary>The chosen colour so far (raised for live previews).</summary>
    public event EventHandler? ColorChanged;

    public ColorPickerDialog(string title, Color initial)
    {
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        Text = title;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        TopMost = true; // overlays are always on top; keep the dialog above them
        BackColor = Theme.Surface;
        ForeColor = Theme.Text;
        Font = Theme.Body;
        KeyPreview = true;
        ClientSize = new Size(340, 372);

        _box.SetBounds(10, 10, 320, 196);
        _hue.SetBounds(12, 212, 316, 22);
        Controls.Add(_box);
        Controls.Add(_hue);

        _preview = new ColorPreview(initial) { Location = new Point(18, 248), Size = new Size(64, 34) };
        Controls.Add(_preview);
        var hexLabel = new Label
        {
            Text = "Hex",
            AutoSize = true,
            Location = new Point(96, 256),
            ForeColor = Theme.TextMuted,
            BackColor = Theme.Surface,
        };
        Controls.Add(hexLabel);
        var host = new TextHost(_hex) { Location = new Point(132, 248), Size = new Size(190, 34) };
        Controls.Add(host);
        _note = new Label
        {
            AutoSize = false,
            Location = new Point(18, 288),
            Size = new Size(304, 20),
            Font = Theme.Small,
            ForeColor = Theme.TextMuted,
            BackColor = Theme.Surface,
            Text = "Drag in the square and bar, or type a hex code.",
        };
        Controls.Add(_note);

        var cancel = new ModernButton("Cancel", ButtonKind.Secondary) { Size = new Size(90, 34) };
        _ok.Size = new Size(110, 34);
        _ok.Location = new Point(ClientSize.Width - 18 - _ok.Width, ClientSize.Height - 18 - _ok.Height);
        cancel.Location = new Point(_ok.Left - 8 - cancel.Width, _ok.Top);
        _ok.Click += (_, _) => Accept();
        cancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        Controls.Add(_ok);
        Controls.Add(cancel);

        var (h, s, v) = ToHsv(initial);
        _hue.Hue = h;
        _box.Set(h, s, v);
        _preview.New = initial;
        SetHexText(initial);

        _hue.HueChanged += (_, _) => { _box.Hue = _hue.Hue; OnPicked(); };
        _box.Changed += (_, _) => OnPicked();
        _hex.TextChanged += (_, _) => OnHexTyped();
        _hex.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { Accept(); e.SuppressKeyPress = true; } };
        _notify.Tick += (_, _) =>
        {
            _notify.Stop();
            if (!_pending) return;
            _pending = false;
            ColorChanged?.Invoke(this, EventArgs.Empty);
        };
    }

    /// <summary>The colour currently chosen.</summary>
    public Color Color => FromHsv(_box.Hue, _box.Saturation, _box.Value);

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.ApplyTitleBar(Handle);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        _box.Focus();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape)
        {
            DialogResult = DialogResult.Cancel;
            Close();
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _notify.Dispose();
        base.Dispose(disposing);
    }

    private void Accept()
    {
        if (!_hex.Focused || Theme.TryParseHex(_hex.Text, out _))
        {
            DialogResult = DialogResult.OK;
            Close();
        }
    }

    /// <summary>The square or hue bar moved: show the colour and its code.</summary>
    private void OnPicked()
    {
        var color = Color;
        _preview.New = color;
        SetHexText(color);
        ShowValid(true);
        QueueNotify();
    }

    private void OnHexTyped()
    {
        if (_updatingHex) return;
        bool valid = Theme.TryParseHex(_hex.Text, out var color);
        ShowValid(valid || _hex.Text.Trim().Length == 0);
        if (!valid) return;

        var (h, s, v) = ToHsv(color);
        if (s == 0) h = _hue.Hue; // greys have no hue: keep the bar where it is
        _hue.SetHueSilently(h);
        _box.Set(h, s, v);
        _preview.New = color;
        QueueNotify();
    }

    private void ShowValid(bool valid)
    {
        _note.Text = valid
            ? "Drag in the square and bar, or type a hex code."
            : "That isn't a colour code. Use 6 hex digits, like #FF8800.";
        _note.ForeColor = valid ? Theme.TextMuted : Theme.Danger;
        _ok.Enabled = valid;
    }

    private void SetHexText(Color color)
    {
        if (_hex.Focused) return; // don't rewrite what the user is typing
        _updatingHex = true;
        _hex.Text = Theme.ToHex(color);
        _updatingHex = false;
    }

    private void QueueNotify()
    {
        _pending = true;
        if (!_notify.Enabled) _notify.Start();
    }

    // ---- HSV ------------------------------------------------------------------------------------

    /// <summary>Hue 0..360, saturation and value 0..1.</summary>
    public static (double H, double S, double V) ToHsv(Color c)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), d = max - min;
        double h = d == 0 ? 0
            : max == r ? 60 * (((g - b) / d) % 6)
            : max == g ? 60 * ((b - r) / d + 2)
            : 60 * ((r - g) / d + 4);
        if (h < 0) h += 360;
        return (h, max == 0 ? 0 : d / max, max);
    }

    public static Color FromHsv(double h, double s, double v)
    {
        h = ((h % 360) + 360) % 360;
        double c = v * s, x = c * (1 - Math.Abs(h / 60 % 2 - 1)), m = v - c;
        (double r, double g, double b) = h switch
        {
            < 60 => (c, x, 0d),
            < 120 => (x, c, 0d),
            < 180 => (0d, c, x),
            < 240 => (0d, x, c),
            < 300 => (x, 0d, c),
            _ => (c, 0d, x),
        };
        static int B(double t) => (int)Math.Round(Math.Clamp(t, 0, 1) * 255);
        return Color.FromArgb(B(r + m), B(g + m), B(b + m));
    }

    // ---- parts ----------------------------------------------------------------------------------

    /// <summary>Saturation left to right, brightness bottom to top, for the current hue.</summary>
    private sealed class SatValBox : DrawnControl
    {
        private double _hue, _sat, _val;
        private bool _dragging;

        public event EventHandler? Changed;

        public SatValBox()
        {
            SetStyle(ControlStyles.Selectable, true);
            Cursor = Cursors.Cross;
        }

        public double Hue { get => _hue; set { _hue = value; Invalidate(); } }
        public double Saturation => _sat;
        public double Value => _val;

        public void Set(double h, double s, double v)
        {
            _hue = h;
            _sat = s;
            _val = v;
            Invalidate();
        }

        // Inset so the marker stays whole at the edges.
        private Rectangle Area => new(this.S(8), this.S(8), Width - this.S(16) - 1, Height - this.S(16) - 1);

        private void SetFrom(Point p)
        {
            var a = Area;
            _sat = Math.Clamp((p.X - a.X) / (double)a.Width, 0, 1);
            _val = Math.Clamp(1 - (p.Y - a.Y) / (double)a.Height, 0, 1);
            Invalidate();
            Changed?.Invoke(this, EventArgs.Empty);
        }

        protected override bool IsInputKey(Keys keyData) =>
            (keyData & Keys.KeyCode) is Keys.Left or Keys.Right or Keys.Up or Keys.Down || base.IsInputKey(keyData);

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            double step = e.Shift ? 0.1 : 0.01;
            switch (e.KeyCode)
            {
                case Keys.Left: _sat = Math.Max(0, _sat - step); break;
                case Keys.Right: _sat = Math.Min(1, _sat + step); break;
                case Keys.Up: _val = Math.Min(1, _val + step); break;
                case Keys.Down: _val = Math.Max(0, _val - step); break;
                default: return;
            }
            Invalidate();
            Changed?.Invoke(this, EventArgs.Empty);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            Focus();
            _dragging = true;
            Capture = true;
            SetFrom(e.Location);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_dragging) SetFrom(e.Location);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            _dragging = false;
            Capture = false;
        }

        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            var a = Area;
            using (var hue = new SolidBrush(FromHsv(_hue, 1, 1))) g.FillRectangle(hue, a);
            var grad = new Rectangle(a.X, a.Y, a.Width + 1, a.Height + 1); // gradients one px larger avoid a seam at the far edge
            using (var white = new LinearGradientBrush(grad, Color.White, Color.FromArgb(0, Color.White), LinearGradientMode.Horizontal))
                g.FillRectangle(white, a);
            using (var black = new LinearGradientBrush(grad, Color.FromArgb(0, Color.Black), Color.Black, LinearGradientMode.Vertical))
                g.FillRectangle(black, a);
            using (var border = new Pen(Focused && ShowFocusCues ? Theme.Accent : Theme.Border)) g.DrawRectangle(border, a);

            // Marker: a ring in a colour that shows up on whatever is under it.
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float x = a.X + (float)(_sat * a.Width), y = a.Y + (float)((1 - _val) * a.Height), r = this.S(7);
            using var outer = new Pen(Color.FromArgb(160, 0, 0, 0), this.S(3));
            using var inner = new Pen(Color.White, this.S(2));
            g.DrawEllipse(outer, x - r, y - r, 2 * r, 2 * r);
            g.DrawEllipse(inner, x - r, y - r, 2 * r, 2 * r);
        }
    }

    /// <summary>Horizontal hue strip (red → yellow → green → cyan → blue → magenta → red).</summary>
    private sealed class HueBar : DrawnControl
    {
        private double _hue;
        private bool _dragging;

        public event EventHandler? HueChanged;

        public HueBar()
        {
            SetStyle(ControlStyles.Selectable, true);
            Cursor = Cursors.Hand;
        }

        public double Hue
        {
            get => _hue;
            set { _hue = value; Invalidate(); HueChanged?.Invoke(this, EventArgs.Empty); }
        }

        public void SetHueSilently(double hue)
        {
            _hue = hue;
            Invalidate();
        }

        private Rectangle Bar => new(this.S(6), this.S(4), Width - this.S(12), Height - this.S(8));

        private void SetFrom(int x)
        {
            var b = Bar;
            Hue = Math.Clamp((x - b.X) / (double)b.Width, 0, 1) * 359.9;
        }

        protected override bool IsInputKey(Keys keyData) => (keyData & Keys.KeyCode) is Keys.Left or Keys.Right || base.IsInputKey(keyData);

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            double step = e.Shift ? 10 : 1;
            if (e.KeyCode == Keys.Left) Hue = Math.Max(0, _hue - step);
            else if (e.KeyCode == Keys.Right) Hue = Math.Min(359.9, _hue + step);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            Focus();
            _dragging = true;
            Capture = true;
            SetFrom(e.X);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_dragging) SetFrom(e.X);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            _dragging = false;
            Capture = false;
        }

        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Smooth(e.Graphics);
            g.Clear(BackColor);
            var b = Bar;
            using (var path = Theme.RoundedRect(b, b.Height / 2f))
            using (var brush = new LinearGradientBrush(new Rectangle(b.X - 1, b.Y, b.Width + 2, b.Height), Color.Red, Color.Red, LinearGradientMode.Horizontal))
            {
                brush.InterpolationColors = new ColorBlend
                {
                    Colors = Enumerable.Range(0, 7).Select(i => FromHsv(i * 60, 1, 1)).ToArray(),
                    Positions = Enumerable.Range(0, 7).Select(i => i / 6f).ToArray(),
                };
                g.FillPath(brush, path);
            }

            float x = b.X + (float)(_hue / 360 * b.Width), w = this.S(8);
            var knob = new RectangleF(x - w / 2, this.S(1), w, Height - this.S(2) - 1);
            Theme.FillRounded(g, FromHsv(_hue, 1, 1), knob, w / 2);
            Theme.DrawRounded(g, Color.White, knob, w / 2, this.S(2));
            if (Focused && ShowFocusCues) Theme.DrawRounded(g, Theme.AccentHover, RectangleF.Inflate(knob, 2, 2), w / 2 + 2, 1.5f);
        }
    }

    /// <summary>The starting colour on the left and the new one on the right.</summary>
    private sealed class ColorPreview : DrawnControl
    {
        private readonly Color _old;
        private Color _new;

        public ColorPreview(Color old)
        {
            _old = old;
            _new = old;
        }

        public Color New { get => _new; set { _new = value; Invalidate(); } }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Smooth(e.Graphics);
            g.Clear(BackColor);
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            float radius = this.S(6);
            using (var path = Theme.RoundedRect(r, radius))
            {
                g.SetClip(path);
                using (var oldBrush = new SolidBrush(_old)) g.FillRectangle(oldBrush, 0, 0, Width / 2f, Height);
                using (var newBrush = new SolidBrush(_new)) g.FillRectangle(newBrush, Width / 2f, 0, Width / 2f, Height);
                g.ResetClip();
            }
            Theme.DrawRounded(g, Theme.Border, r, radius);
        }
    }
}
