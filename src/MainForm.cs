using static OverlayApp.NativeMethods;

namespace OverlayApp;

internal sealed class MainForm : Form
{
    private const int HotkeyId = 1;

    private readonly WindowListBox _windowList = new() { Dock = DockStyle.Fill };
    private readonly TextBox _searchBox = new() { PlaceholderText = "Search windows" };
    private readonly ModernButton _refreshButton = new("", ButtonKind.Secondary, Theme.GlyphRefresh);
    private readonly ModernButton _startStopButton = new("Start overlay", ButtonKind.Primary, Theme.GlyphPlay) { Dock = DockStyle.Fill };
    private readonly ModernButton _fitButton = new("Fit", ButtonKind.Secondary, Theme.GlyphFit) { Dock = DockStyle.Fill, Enabled = false };
    private readonly ToggleSwitch _clickThroughSwitch = new("Overlay mode", "Lock in place and let clicks pass through") { Dock = DockStyle.Top };
    private readonly HotkeyBox _hotkeyBox = new();
    private readonly ToggleSwitch _clientOnlySwitch = new("Client area only", "Hide the title bar and borders") { Dock = DockStyle.Top };
    private readonly Slider _opacitySlider = new(10, 100, 100) { Dock = DockStyle.Top };
    private readonly Label _opacityValue = MakeLabel("100%", Theme.BodyStrong, Theme.Text);
    private readonly Slider _cropLeft = new(0, 45, 0) { Dock = DockStyle.Fill };
    private readonly Slider _cropTop = new(0, 45, 0) { Dock = DockStyle.Fill };
    private readonly Slider _cropRight = new(0, 45, 0) { Dock = DockStyle.Fill };
    private readonly Slider _cropBottom = new(0, 45, 0) { Dock = DockStyle.Fill };
    private readonly ModernButton _cropReset = new("Reset", ButtonKind.Ghost, Theme.GlyphReset);
    private readonly StatusBar _status = new() { Dock = DockStyle.Fill };

    private readonly AppSettings _settings = AppSettings.Load();
    private List<WindowInfo> _allWindows = new();
    private OverlayForm? _overlay;
    private bool _hotkeyRegistered;

    public MainForm()
    {
        Text = "Window Overlay";
        // Use the icon embedded in the exe for the title bar and taskbar.
        try { Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!); } catch { }
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Theme.Background;
        ForeColor = Theme.Text;
        Font = Theme.Body;
        ClientSize = new Size(this.S(980), this.S(700));
        MinimumSize = new Size(this.S(820), this.S(680));

        BuildLayout();

        _refreshButton.Click += (_, _) => RefreshWindowList();
        _startStopButton.Click += (_, _) => { if (_overlay == null) StartOverlay(); else StopOverlay(); };
        _fitButton.Click += (_, _) => _overlay?.FitToSource();
        _searchBox.TextChanged += (_, _) => ApplyFilter();
        _searchBox.KeyDown += OnSearchKeyDown;
        _windowList.DoubleClick += (_, _) => StartOverlay();
        _windowList.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { StartOverlay(); e.Handled = true; } };
        _windowList.SelectedIndexChanged += (_, _) => OnSelectionChanged();
        _clickThroughSwitch.CheckedChanged += (_, _) => ApplySettings();
        _clientOnlySwitch.CheckedChanged += (_, _) => ApplySettings();
        _opacitySlider.ValueChanged += (_, _) => ApplySettings();
        foreach (var slider in new[] { _cropLeft, _cropTop, _cropRight, _cropBottom })
            slider.ValueChanged += (_, _) => ApplySettings();
        _cropReset.Click += (_, _) =>
        {
            foreach (var slider in new[] { _cropLeft, _cropTop, _cropRight, _cropBottom }) slider.Value = 0;
        };

        // Suspend the global hotkey while recording so pressing the current combo is captured.
        _hotkeyBox.Enter += (_, _) => UnregisterToggleHotkey();
        _hotkeyBox.Leave += (_, _) => { if (!_hotkeyRegistered) RegisterToggleHotkey(_settings.ToggleHotkey); };
        _hotkeyBox.HotkeyChanged += (_, _) => OnHotkeyRecorded();
        _hotkeyBox.Hotkey = _settings.ToggleHotkey;
        _clientOnlySwitch.Checked = true;
        UpdateHotkeyText();

        SetStatus("Pick a window and press Start overlay.");
    }

    private static Label MakeLabel(string text, Font font, Color color) => new()
    {
        Text = text,
        Font = font,
        ForeColor = color,
        BackColor = Theme.Surface,
        AutoSize = true,
        Margin = Padding.Empty,
    };

    private void BuildLayout()
    {
        int gap = this.S(14);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(this.S(22), this.S(18), this.S(22), this.S(8)),
            ColumnCount = 1,
            RowCount = 3,
            BackColor = Theme.Background,
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, this.S(36)));

        // Header
        var header = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, ColumnCount = 1, BackColor = Theme.Background, Margin = new Padding(0, 0, 0, gap) };
        var title = MakeLabel("Window Overlay", Theme.Title, Theme.Text);
        title.BackColor = Theme.Background;
        var subtitle = MakeLabel("Mirror any window into an always-on-top overlay that clicks pass straight through.", Theme.Body, Theme.TextMuted);
        subtitle.BackColor = Theme.Background;
        subtitle.Margin = new Padding(this.S(2), this.S(2), 0, 0);
        header.Controls.Add(title, 0, 0);
        header.Controls.Add(subtitle, 0, 1);
        root.Controls.Add(header, 0, 0);

        // Main area: window list on the left, settings on the right
        int sideWidth = this.S(370);
        var main = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = Theme.Background, Margin = Padding.Empty };
        main.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        main.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, sideWidth + gap));
        main.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        main.Controls.Add(BuildWindowsCard(), 0, 0);
        main.Controls.Add(BuildSidePanel(sideWidth, gap), 1, 0);
        root.Controls.Add(main, 0, 1);

        root.Controls.Add(_status, 0, 2);
        _status.Margin = new Padding(this.S(2), this.S(6), 0, 0);

        Controls.Add(root);
    }

    private Card BuildWindowsCard()
    {
        var card = new Card { Dock = DockStyle.Fill, Margin = Padding.Empty };

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, BackColor = Theme.Surface, Margin = Padding.Empty };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        // Toolbar: title, search, refresh
        var toolbar = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 3, BackColor = Theme.Surface, Margin = new Padding(0, 0, 0, this.S(10)) };
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var heading = MakeLabel("Windows", Theme.Section, Theme.Text);
        heading.Anchor = AnchorStyles.Left;
        heading.Margin = new Padding(0, 0, this.S(16), 0);
        var search = new TextHost(_searchBox, Theme.GlyphSearch) { Dock = DockStyle.Fill, Margin = new Padding(0, 0, this.S(8), 0) };
        _refreshButton.Size = new Size(this.S(34), this.S(34));
        _refreshButton.Margin = Padding.Empty;
        toolbar.Controls.Add(heading, 0, 0);
        toolbar.Controls.Add(search, 1, 0);
        toolbar.Controls.Add(_refreshButton, 2, 0);
        layout.Controls.Add(toolbar, 0, 0);

        _windowList.Margin = Padding.Empty;
        layout.Controls.Add(_windowList, 0, 1);

        var hint = MakeLabel("Double-click a window (or press Enter) to start. The source can be covered by other windows but must not be minimised.",
            Theme.Small, Theme.TextMuted);
        hint.AutoSize = true;
        hint.MaximumSize = new Size(this.S(520), 0);
        hint.Margin = new Padding(0, this.S(10), 0, 0);
        layout.Controls.Add(hint, 0, 2);

        card.Controls.Add(layout);
        return card;
    }

    private Panel BuildSidePanel(int width, int gap)
    {
        var side = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Background, Margin = new Padding(gap, 0, 0, 0), AutoScroll = true };

        var overlayCard = BuildCard(width, "Overlay", rows =>
        {
            var buttons = new TableLayoutPanel { Height = this.S(38), Dock = DockStyle.Top, ColumnCount = 2, BackColor = Theme.Surface, Margin = Padding.Empty };
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 62));
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38));
            _startStopButton.Margin = new Padding(0, 0, this.S(4), 0);
            _fitButton.Margin = new Padding(this.S(4), 0, 0, 0);
            buttons.Controls.Add(_startStopButton, 0, 0);
            buttons.Controls.Add(_fitButton, 1, 0);
            rows.Add(buttons);

            rows.Add(Spacer(12));
            rows.Add(_clickThroughSwitch);
            rows.Add(Spacer(10));

            var hotkeyRow = new TableLayoutPanel { Height = this.S(34), Dock = DockStyle.Top, ColumnCount = 2, BackColor = Theme.Surface, Margin = Padding.Empty };
            hotkeyRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            hotkeyRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, this.S(170)));
            var hotkeyLabel = MakeLabel("Toggle hotkey", Theme.Body, Theme.Text);
            hotkeyLabel.Anchor = AnchorStyles.Left;
            hotkeyRow.Controls.Add(hotkeyLabel, 0, 0);
            hotkeyRow.Controls.Add(new TextHost(_hotkeyBox, Theme.GlyphKeyboard) { Dock = DockStyle.Fill, Margin = Padding.Empty }, 1, 0);
            rows.Add(hotkeyRow);

            var help = MakeLabel("Click the box, then press keys · Esc cancels · Backspace clears", Theme.Small, Theme.TextMuted);
            help.AutoSize = false;
            help.Dock = DockStyle.Top;
            help.Height = this.S(24);
            help.TextAlign = ContentAlignment.BottomRight;
            rows.Add(help);
        });

        var appearanceCard = BuildCard(width, "Appearance", rows =>
        {
            var opacityRow = new TableLayoutPanel { Height = this.S(22), Dock = DockStyle.Top, ColumnCount = 2, BackColor = Theme.Surface, Margin = Padding.Empty };
            opacityRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            opacityRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            var opacityLabel = MakeLabel("Opacity", Theme.Body, Theme.Text);
            opacityLabel.Anchor = AnchorStyles.Left;
            _opacityValue.Anchor = AnchorStyles.Right;
            opacityRow.Controls.Add(opacityLabel, 0, 0);
            opacityRow.Controls.Add(_opacityValue, 1, 0);
            rows.Add(opacityRow);
            rows.Add(_opacitySlider);

            rows.Add(Spacer(10));
            rows.Add(_clientOnlySwitch);
            rows.Add(Spacer(10));

            var cropHeader = new TableLayoutPanel { Height = this.S(30), Dock = DockStyle.Top, ColumnCount = 2, BackColor = Theme.Surface, Margin = Padding.Empty };
            cropHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            cropHeader.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            var cropLabel = MakeLabel("Crop", Theme.Body, Theme.Text);
            cropLabel.Anchor = AnchorStyles.Left;
            _cropReset.Size = new Size(_cropReset.PreferredWidth, this.S(28));
            _cropReset.Font = Theme.Small;
            _cropReset.Anchor = AnchorStyles.Right;
            _cropReset.Margin = Padding.Empty;
            cropHeader.Controls.Add(cropLabel, 0, 0);
            cropHeader.Controls.Add(_cropReset, 1, 0);
            rows.Add(cropHeader);

            var cropGrid = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 3, RowCount = 4, BackColor = Theme.Surface, Margin = Padding.Empty };
            cropGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, this.S(58)));
            cropGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            cropGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, this.S(40)));
            int row = 0;
            foreach (var (name, slider) in new[] { ("Left", _cropLeft), ("Top", _cropTop), ("Right", _cropRight), ("Bottom", _cropBottom) })
            {
                cropGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, this.S(30)));
                var nameLabel = MakeLabel(name, Theme.Body, Theme.TextMuted);
                nameLabel.Anchor = AnchorStyles.Left;
                var valueLabel = MakeLabel("0%", Theme.Body, Theme.TextMuted);
                valueLabel.Anchor = AnchorStyles.Right;
                slider.Margin = new Padding(0, this.S(2), this.S(6), 0);
                slider.ValueChanged += (_, _) =>
                {
                    valueLabel.Text = $"{slider.Value}%";
                    valueLabel.ForeColor = slider.Value > 0 ? Theme.Text : Theme.TextMuted;
                };
                cropGrid.Controls.Add(nameLabel, 0, row);
                cropGrid.Controls.Add(slider, 1, row);
                cropGrid.Controls.Add(valueLabel, 2, row);
                row++;
            }
            cropGrid.Height = this.S(30) * 4;
            rows.Add(cropGrid);
        });

        overlayCard.Location = new Point(0, 0);
        appearanceCard.Location = new Point(0, overlayCard.Bottom + gap);
        side.Controls.Add(overlayCard);
        side.Controls.Add(appearanceCard);
        return side;
    }

    /// <summary>Builds a card with a section title and the given rows stacked top to bottom.</summary>
    private Card BuildCard(int width, string title, Action<List<Control>> addRows)
    {
        var card = new Card { Width = width, Margin = Padding.Empty };
        var rows = new List<Control>();

        var heading = MakeLabel(title, Theme.Section, Theme.Text);
        heading.AutoSize = false;
        heading.Dock = DockStyle.Top;
        heading.Height = this.S(32);
        heading.TextAlign = ContentAlignment.TopLeft;
        rows.Add(heading);
        addRows(rows);

        // Dock=Top stacks in reverse add order, so add from the bottom up.
        for (int i = rows.Count - 1; i >= 0; i--) card.Controls.Add(rows[i]);
        card.Height = card.Padding.Vertical + rows.Sum(r => r.Height);
        return card;
    }

    private Control Spacer(int height) =>
        new Panel { Dock = DockStyle.Top, Height = this.S(height), BackColor = Theme.Surface };

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.UseDarkTitleBar(Handle);
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        RefreshWindowList();
        ActiveControl = _windowList;
        if (!RegisterToggleHotkey(_settings.ToggleHotkey))
            SetStatus($"{HotkeyBox.Describe(_settings.ToggleHotkey)} is already used by another app. Pick another hotkey or use the switch.", StatusKind.Warning);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        UnregisterToggleHotkey();
        StopOverlay();
        base.OnFormClosed(e);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_HOTKEY && m.WParam.ToInt32() == HotkeyId)
        {
            if (_overlay != null) _clickThroughSwitch.Checked = !_clickThroughSwitch.Checked;
            return;
        }
        base.WndProc(ref m);
    }

    private void OnSearchKeyDown(object? sender, KeyEventArgs e)
    {
        // Arrow keys / Enter in the search box drive the list.
        if (e.KeyCode is Keys.Down or Keys.Up && _windowList.Items.Count > 0)
        {
            int delta = e.KeyCode == Keys.Down ? 1 : -1;
            _windowList.SelectedIndex = Math.Clamp(_windowList.SelectedIndex + delta, 0, _windowList.Items.Count - 1);
            e.Handled = e.SuppressKeyPress = true;
        }
        else if (e.KeyCode == Keys.Enter)
        {
            if (_windowList.SelectedIndex < 0 && _windowList.Items.Count > 0) _windowList.SelectedIndex = 0;
            StartOverlay();
            e.Handled = e.SuppressKeyPress = true;
        }
        else if (e.KeyCode == Keys.Escape)
        {
            _searchBox.Clear();
            e.Handled = e.SuppressKeyPress = true;
        }
    }

    private bool RegisterToggleHotkey(Keys hotkey)
    {
        UnregisterToggleHotkey();
        if (hotkey == Keys.None) return true;

        uint mods = MOD_NOREPEAT;
        if (hotkey.HasFlag(Keys.Control)) mods |= MOD_CONTROL;
        if (hotkey.HasFlag(Keys.Alt)) mods |= MOD_ALT;
        if (hotkey.HasFlag(Keys.Shift)) mods |= MOD_SHIFT;

        _hotkeyRegistered = RegisterHotKey(Handle, HotkeyId, mods, (uint)(hotkey & Keys.KeyCode));
        return _hotkeyRegistered;
    }

    private void UnregisterToggleHotkey()
    {
        if (!_hotkeyRegistered) return;
        UnregisterHotKey(Handle, HotkeyId);
        _hotkeyRegistered = false;
    }

    private void OnHotkeyRecorded()
    {
        var newHotkey = _hotkeyBox.Hotkey;
        if (!RegisterToggleHotkey(newHotkey))
        {
            SetStatus($"{HotkeyBox.Describe(newHotkey)} is already used by another app. Kept {HotkeyBox.Describe(_settings.ToggleHotkey)}.", StatusKind.Warning);
            _hotkeyBox.Hotkey = _settings.ToggleHotkey;
            RegisterToggleHotkey(_settings.ToggleHotkey);
            return;
        }

        _settings.ToggleHotkey = newHotkey;
        _settings.Save();
        UpdateHotkeyText();
        SetStatus(newHotkey == Keys.None
            ? "Toggle hotkey cleared."
            : $"Toggle hotkey set to {HotkeyBox.Describe(newHotkey)}.");
    }

    private void UpdateHotkeyText()
    {
        var hotkey = _settings.ToggleHotkey;
        _clickThroughSwitch.Description = hotkey == Keys.None
            ? "Lock in place and let clicks pass through"
            : $"Lock and let clicks pass through · {HotkeyBox.Describe(hotkey)}";
        _clickThroughSwitch.Invalidate();
    }

    private void RefreshWindowList()
    {
        var oldWindows = _allWindows;
        _allWindows = GetCapturableWindows();
        ApplyFilter();
        foreach (var w in oldWindows) w.Icon?.Dispose();
    }

    private void ApplyFilter()
    {
        var selected = (_windowList.SelectedItem as WindowInfo)?.Handle ?? _overlay?.Source ?? IntPtr.Zero;
        string filter = _searchBox.Text.Trim();

        _windowList.BeginUpdate();
        _windowList.Items.Clear();
        foreach (var w in _allWindows)
        {
            if (filter.Length > 0
                && !w.Title.Contains(filter, StringComparison.OrdinalIgnoreCase)
                && !w.ProcessName.Contains(filter, StringComparison.OrdinalIgnoreCase))
                continue;

            int index = _windowList.Items.Add(w);
            if (w.Handle == selected) _windowList.SelectedIndex = index;
        }
        _windowList.EndUpdate();
    }

    private void OnSelectionChanged()
    {
        // Switch the running overlay to the newly selected window.
        if (_overlay != null && _windowList.SelectedItem is WindowInfo w && w.Handle != _overlay.Source)
        {
            _overlay.Source = w.Handle;
            _windowList.LiveHandle = w.Handle;
            SetStatus($"Mirroring {w.Title}", StatusKind.Live);
        }
    }

    private void StartOverlay()
    {
        if (_windowList.SelectedItem is not WindowInfo w)
        {
            SetStatus("Select a window first.", StatusKind.Warning);
            return;
        }
        if (!IsWindow(w.Handle))
        {
            SetStatus("That window no longer exists.", StatusKind.Warning);
            RefreshWindowList();
            return;
        }

        if (_overlay == null)
        {
            _overlay = new OverlayForm();
            _overlay.SourceLost += (_, _) =>
            {
                StopOverlay();
                SetStatus("The mirrored window was closed.", StatusKind.Warning);
                RefreshWindowList();
            };
            _overlay.FormClosed += (_, _) => { _overlay = null; UpdateButtons(); };

            // Default placement: bottom-right corner of the screen this window is on.
            var area = Screen.FromControl(this).WorkingArea;
            var size = new Size(area.Width / 3, area.Height / 3);
            _overlay.Bounds = new Rectangle(area.Right - size.Width - 24, area.Bottom - size.Height - 24, size.Width, size.Height);
        }

        _overlay.Source = w.Handle;
        ApplySettings();
        _overlay.Show();
        _overlay.FitToSource();
        _windowList.LiveHandle = w.Handle;
        UpdateButtons();

        if (IsIconic(w.Handle))
            SetStatus($"{w.Title} is minimised. Restore it from the taskbar to see it in the overlay.", StatusKind.Warning);
        else
            SetStatus($"Mirroring {w.Title}", StatusKind.Live);
    }

    private void StopOverlay()
    {
        var overlay = _overlay;
        _overlay = null;
        overlay?.Close();
        overlay?.Dispose();
        _clickThroughSwitch.Checked = false;
        _windowList.LiveHandle = IntPtr.Zero;
        UpdateButtons();
        if (overlay != null) SetStatus("Overlay stopped.");
    }

    private void ApplySettings()
    {
        _opacityValue.Text = $"{_opacitySlider.Value}%";
        if (_overlay == null) return;

        _overlay.ClickThrough = _clickThroughSwitch.Checked;
        _overlay.ClientAreaOnly = _clientOnlySwitch.Checked;
        _overlay.OverlayOpacity = _opacitySlider.Value / 100.0;
        _overlay.Crop = new Crop(
            _cropLeft.Value / 100.0, _cropTop.Value / 100.0,
            _cropRight.Value / 100.0, _cropBottom.Value / 100.0);
    }

    private void UpdateButtons()
    {
        bool running = _overlay != null;
        _startStopButton.Text = running ? "Stop overlay" : "Start overlay";
        _startStopButton.Glyph = running ? Theme.GlyphStop : Theme.GlyphPlay;
        _startStopButton.Kind = running ? ButtonKind.Danger : ButtonKind.Primary;
        _fitButton.Enabled = running;
    }

    private void SetStatus(string text, StatusKind kind = StatusKind.Idle) => _status.Set(text, kind);
}
