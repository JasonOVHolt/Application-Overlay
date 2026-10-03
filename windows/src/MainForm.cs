using static OverlayApp.NativeMethods;

namespace OverlayApp;

internal sealed class MainForm : Form
{
    private const int HotkeyId = 1;

    // Windows card
    private readonly WindowListBox _windowList = new() { Dock = DockStyle.Fill };
    private readonly TextBox _searchBox = new() { PlaceholderText = "Search windows" };
    private readonly ModernButton _refreshButton = new("", ButtonKind.Secondary, Theme.GlyphRefresh);

    // Overlays card
    private readonly ModernButton _addButton = new("Add overlay", ButtonKind.Primary, Theme.GlyphAdd) { Dock = DockStyle.Fill };
    private readonly ModernButton _stopAllButton = new("Stop all", ButtonKind.Secondary, Theme.GlyphStop) { Dock = DockStyle.Fill, Enabled = false };
    private readonly OverlayListBox _overlayList = new() { Dock = DockStyle.Fill };
    private readonly Label _overlayEmpty = new()
    {
        Dock = DockStyle.Fill,
        Text = "No overlays yet.\nPick a window and press Add overlay.",
        TextAlign = ContentAlignment.MiddleCenter,
        ForeColor = Theme.TextMuted,
        BackColor = Theme.Surface,
        Font = Theme.Small,
    };
    private readonly ToggleSwitch _clickThroughSwitch = new("Overlay mode", "Lock all overlays and let clicks pass through") { Dock = DockStyle.Top };
    private readonly ToggleSwitch _snapSwitch = new("Snap to edges", "Overlays stick to screen edges and to each other") { Dock = DockStyle.Top };
    private readonly HotkeyBox _hotkeyBox = new();

    // Selected-overlay settings card
    private readonly Label _settingsSubtitle = MakeLabel("", Theme.Small, Theme.TextMuted);
    private readonly ModernButton _fitButton = new("", ButtonKind.Ghost, Theme.GlyphFit);
    private readonly ModernButton _removeButton = new("", ButtonKind.Ghost, Theme.GlyphDelete);
    private readonly ToggleSwitch _clientOnlySwitch = new("Client area only", "Hide the title bar and borders") { Dock = DockStyle.Top };
    private readonly Slider _opacitySlider = new(10, 100, 100) { Dock = DockStyle.Top };
    private readonly Label _opacityValue = MakeLabel("100%", Theme.BodyStrong, Theme.Text);
    private readonly Slider _cropLeft = new(0, 45, 0) { Dock = DockStyle.Fill };
    private readonly Slider _cropTop = new(0, 45, 0) { Dock = DockStyle.Fill };
    private readonly Slider _cropRight = new(0, 45, 0) { Dock = DockStyle.Fill };
    private readonly Slider _cropBottom = new(0, 45, 0) { Dock = DockStyle.Fill };
    private readonly ModernButton _cropReset = new("Reset", ButtonKind.Ghost, Theme.GlyphReset);
    private readonly List<Control> _settingsControls = new();
    private readonly List<(Label Label, Func<Color> Normal)> _settingsLabels = new();

    private readonly StatusBar _status = new() { Dock = DockStyle.Fill };
    private readonly ToolTip _toolTip = new();

    private readonly AppSettings _settings = AppSettings.Load();
    private readonly List<OverlayItem> _overlays = new();
    private List<WindowInfo> _allWindows = new();
    private int _nextOverlayNumber = 1;
    private bool _loadingSettings;
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
        ClientSize = new Size(this.S(1020), this.S(860));
        MinimumSize = new Size(this.S(860), this.S(600));

        BuildLayout();

        _refreshButton.Click += (_, _) => RefreshWindowList();
        _searchBox.TextChanged += (_, _) => ApplyFilter();
        _searchBox.KeyDown += OnSearchKeyDown;
        _windowList.DoubleClick += (_, _) => AddOverlay();
        _windowList.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { AddOverlay(); e.Handled = true; } };

        _addButton.Click += (_, _) => AddOverlay();
        _stopAllButton.Click += (_, _) => StopAll();
        _overlayList.SelectedIndexChanged += (_, _) => OnOverlaySelectionChanged();
        _overlayList.RemoveRequested += (_, item) => RemoveOverlay(item);
        _overlayList.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Delete && SelectedOverlay is OverlayItem item) { RemoveOverlay(item); e.Handled = true; }
        };
        _clickThroughSwitch.CheckedChanged += (_, _) => ApplyClickThrough();
        _snapSwitch.Checked = _settings.SnapToEdges;
        _snapSwitch.CheckedChanged += (_, _) =>
        {
            _settings.SnapToEdges = _snapSwitch.Checked;
            _settings.Save();
            foreach (var item in _overlays) item.Form.SnapToEdges = _snapSwitch.Checked;
        };

        _fitButton.Click += (_, _) => SelectedOverlay?.Form.FitToSource();
        _removeButton.Click += (_, _) => { if (SelectedOverlay is OverlayItem item) RemoveOverlay(item); };
        _clientOnlySwitch.CheckedChanged += (_, _) => ApplySelectedSettings();
        _opacitySlider.ValueChanged += (_, _) => ApplySelectedSettings();
        foreach (var slider in CropSliders)
            slider.ValueChanged += (_, _) => ApplySelectedSettings();
        _cropReset.Click += (_, _) =>
        {
            foreach (var slider in CropSliders) slider.Value = 0;
        };

        // Suspend the global hotkey while recording so pressing the current combo is captured.
        _hotkeyBox.Enter += (_, _) => UnregisterToggleHotkey();
        _hotkeyBox.Leave += (_, _) => { if (!_hotkeyRegistered) RegisterToggleHotkey(_settings.ToggleHotkey); };
        _hotkeyBox.HotkeyChanged += (_, _) => OnHotkeyRecorded();
        _hotkeyBox.Hotkey = _settings.ToggleHotkey;

        _toolTip.SetToolTip(_refreshButton, "Refresh window list");
        _toolTip.SetToolTip(_fitButton, "Fit overlay to the source's shape");
        _toolTip.SetToolTip(_removeButton, "Close this overlay");
        _toolTip.SetToolTip(_hotkeyBox, "Click, then press keys. Esc cancels, Backspace clears.");

        UpdateHotkeyText();
        UpdateOverlayUi();
        SetStatus("Pick a window and press Add overlay.");
    }

    private Slider[] CropSliders => new[] { _cropLeft, _cropTop, _cropRight, _cropBottom };

    private OverlayItem? SelectedOverlay => _overlayList.SelectedItem as OverlayItem;

    private static Label MakeLabel(string text, Font font, Color color) => new()
    {
        Text = text,
        Font = font,
        ForeColor = color,
        BackColor = Theme.Surface,
        AutoSize = true,
        Margin = Padding.Empty,
    };

    // ---------------------------------------------------------------- layout

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
        var subtitle = MakeLabel("Mirror any windows into always-on-top overlays that clicks pass straight through.", Theme.Body, Theme.TextMuted);
        subtitle.BackColor = Theme.Background;
        subtitle.Margin = new Padding(this.S(2), this.S(2), 0, 0);
        header.Controls.Add(title, 0, 0);
        header.Controls.Add(subtitle, 0, 1);
        root.Controls.Add(header, 0, 0);

        // Main area: window list on the left, overlays + settings on the right
        int sideWidth = this.S(390);
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

        var hint = MakeLabel("Double-click a window (or press Enter) to add an overlay. Add the same window more than once to show different cropped parts of it. "
            + "Sources can be covered by other windows but must not be minimised.", Theme.Small, Theme.TextMuted);
        hint.MaximumSize = new Size(this.S(520), 0);
        hint.Margin = new Padding(0, this.S(10), 0, 0);
        layout.Controls.Add(hint, 0, 2);

        card.Controls.Add(layout);
        return card;
    }

    private Panel BuildSidePanel(int width, int gap)
    {
        var side = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Background, Margin = new Padding(gap, 0, 0, 0), AutoScroll = true };

        var overlaysCard = BuildCard(width, "Overlays", null, rows =>
        {
            var buttons = new TableLayoutPanel { Height = this.S(38), Dock = DockStyle.Top, ColumnCount = 2, BackColor = Theme.Surface, Margin = Padding.Empty };
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60));
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
            _addButton.Margin = new Padding(0, 0, this.S(4), 0);
            _stopAllButton.Margin = new Padding(this.S(4), 0, 0, 0);
            buttons.Controls.Add(_addButton, 0, 0);
            buttons.Controls.Add(_stopAllButton, 1, 0);
            rows.Add(buttons);
            rows.Add(Spacer(10));

            var listHost = new Panel { Dock = DockStyle.Top, Height = this.S(44) * 3 + this.S(4), BackColor = Theme.Surface };
            listHost.Controls.Add(_overlayList);
            listHost.Controls.Add(_overlayEmpty);
            rows.Add(listHost);
            rows.Add(Spacer(10));

            rows.Add(_clickThroughSwitch);
            rows.Add(Spacer(8));

            var hotkeyRow = new TableLayoutPanel { Height = this.S(34), Dock = DockStyle.Top, ColumnCount = 2, BackColor = Theme.Surface, Margin = Padding.Empty };
            hotkeyRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            hotkeyRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, this.S(180)));
            var hotkeyLabel = MakeLabel("Toggle hotkey", Theme.Body, Theme.Text);
            hotkeyLabel.Anchor = AnchorStyles.Left;
            hotkeyRow.Controls.Add(hotkeyLabel, 0, 0);
            hotkeyRow.Controls.Add(new TextHost(_hotkeyBox, Theme.GlyphKeyboard) { Dock = DockStyle.Fill, Margin = Padding.Empty }, 1, 0);
            rows.Add(hotkeyRow);
            rows.Add(Spacer(10));
            rows.Add(_snapSwitch);
        });

        var settingsCard = BuildCard(width, "Overlay settings", new Control[] { _fitButton, _removeButton }, rows =>
        {
            _settingsSubtitle.AutoSize = false;
            _settingsSubtitle.AutoEllipsis = true;
            _settingsSubtitle.Dock = DockStyle.Top;
            _settingsSubtitle.Height = this.S(24);
            _settingsSubtitle.TextAlign = ContentAlignment.TopLeft;
            rows.Add(_settingsSubtitle);

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

            rows.Add(Spacer(8));
            rows.Add(_clientOnlySwitch);
            rows.Add(Spacer(8));

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

            // Two crop sliders per row: Left/Right, Top/Bottom.
            var cropGrid = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 7, RowCount = 2, BackColor = Theme.Surface, Margin = Padding.Empty };
            foreach (var style in new[]
            {
                new ColumnStyle(SizeType.Absolute, this.S(50)), new ColumnStyle(SizeType.Percent, 50), new ColumnStyle(SizeType.Absolute, this.S(36)),
                new ColumnStyle(SizeType.Absolute, this.S(12)),
                new ColumnStyle(SizeType.Absolute, this.S(50)), new ColumnStyle(SizeType.Percent, 50), new ColumnStyle(SizeType.Absolute, this.S(36)),
            }) cropGrid.ColumnStyles.Add(style);

            var cells = new[] { ("Left", _cropLeft, 0, 0), ("Right", _cropRight, 0, 4), ("Top", _cropTop, 1, 0), ("Bottom", _cropBottom, 1, 4) };
            foreach (var (name, slider, row, col) in cells)
            {
                var nameLabel = MakeLabel(name, Theme.Body, Theme.TextMuted);
                nameLabel.Anchor = AnchorStyles.Left;
                var valueLabel = MakeLabel("0%", Theme.Small, Theme.TextMuted);
                valueLabel.Anchor = AnchorStyles.Right;
                slider.Margin = new Padding(0, this.S(2), this.S(4), 0);
                slider.ValueChanged += (_, _) =>
                {
                    valueLabel.Text = $"{slider.Value}%";
                    valueLabel.ForeColor = slider.Value > 0 ? Theme.Text : Theme.TextMuted;
                };
                cropGrid.Controls.Add(nameLabel, col, row);
                cropGrid.Controls.Add(slider, col + 1, row);
                cropGrid.Controls.Add(valueLabel, col + 2, row);
                _settingsLabels.Add((nameLabel, () => Theme.TextMuted));
                _settingsLabels.Add((valueLabel, () => slider.Value > 0 ? Theme.Text : Theme.TextMuted));
            }
            cropGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, this.S(30)));
            cropGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, this.S(30)));
            cropGrid.Height = this.S(30) * 2;
            rows.Add(cropGrid);

            foreach (var label in new[] { opacityLabel, _opacityValue, cropLabel }) _settingsLabels.Add((label, () => Theme.Text));
        });

        _settingsControls.AddRange(new Control[] { _fitButton, _removeButton, _opacitySlider, _clientOnlySwitch, _cropReset });
        _settingsControls.AddRange(CropSliders);

        overlaysCard.Location = new Point(0, 0);
        settingsCard.Location = new Point(0, overlaysCard.Bottom + gap);
        side.Controls.Add(overlaysCard);
        side.Controls.Add(settingsCard);

        // Cards follow the panel's width so a vertical scrollbar never forces a horizontal one.
        side.HandleCreated += (_, _) => Theme.UseDarkScrollbars(side.Handle);
        side.Layout += (_, _) =>
        {
            int w = side.ClientSize.Width;
            if (overlaysCard.Width != w) overlaysCard.Width = w;
            if (settingsCard.Width != w) settingsCard.Width = w;
        };
        return side;
    }

    /// <summary>Builds a card with a section title (plus optional header buttons) and rows stacked top to bottom.</summary>
    private Card BuildCard(int width, string title, Control[]? headerButtons, Action<List<Control>> addRows)
    {
        var card = new Card { Width = width, Margin = Padding.Empty };
        var rows = new List<Control>();

        var header = new TableLayoutPanel { Dock = DockStyle.Top, Height = this.S(32), BackColor = Theme.Surface, Margin = Padding.Empty, ColumnCount = 1 + (headerButtons?.Length ?? 0) };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var heading = MakeLabel(title, Theme.Section, Theme.Text);
        heading.Anchor = AnchorStyles.Left | AnchorStyles.Top;
        header.Controls.Add(heading, 0, 0);
        if (headerButtons != null)
        {
            for (int i = 0; i < headerButtons.Length; i++)
            {
                header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
                headerButtons[i].Size = new Size(this.S(30), this.S(28));
                headerButtons[i].Margin = new Padding(this.S(4), 0, 0, 0);
                headerButtons[i].Anchor = AnchorStyles.Top | AnchorStyles.Right;
                header.Controls.Add(headerButtons[i], i + 1, 0);
            }
        }
        rows.Add(header);
        addRows(rows);

        // Dock=Top stacks in reverse add order, so add from the bottom up.
        for (int i = rows.Count - 1; i >= 0; i--) card.Controls.Add(rows[i]);
        card.Height = card.Padding.Vertical + rows.Sum(r => r.Height);
        return card;
    }

    private Control Spacer(int height) =>
        new Panel { Dock = DockStyle.Top, Height = this.S(height), BackColor = Theme.Surface };

    // ---------------------------------------------------------------- form lifecycle

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.UseDarkTitleBar(Handle);
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);

        // Shrink to fit smaller screens (the settings column scrolls if needed).
        var area = Screen.FromControl(this).WorkingArea;
        Size = new Size(Math.Min(Width, area.Width - 40), Math.Min(Height, area.Height - 40));
        MinimumSize = new Size(Math.Min(MinimumSize.Width, area.Width - 40), Math.Min(MinimumSize.Height, area.Height - 40));
        CenterToScreen();

        RefreshWindowList();
        ActiveControl = _windowList;
        if (!RegisterToggleHotkey(_settings.ToggleHotkey))
            SetStatus($"{HotkeyBox.Describe(_settings.ToggleHotkey)} is already used by another app. Pick another hotkey or use the switch.", StatusKind.Warning);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        UnregisterToggleHotkey();
        StopAll();
        base.OnFormClosed(e);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_HOTKEY && m.WParam.ToInt32() == HotkeyId)
        {
            _clickThroughSwitch.Checked = !_clickThroughSwitch.Checked;
            return;
        }
        base.WndProc(ref m);
    }

    // ---------------------------------------------------------------- window list

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
            AddOverlay();
            e.Handled = e.SuppressKeyPress = true;
        }
        else if (e.KeyCode == Keys.Escape)
        {
            _searchBox.Clear();
            e.Handled = e.SuppressKeyPress = true;
        }
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
        var selected = (_windowList.SelectedItem as WindowInfo)?.Handle ?? IntPtr.Zero;
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

    // ---------------------------------------------------------------- overlays

    private void AddOverlay()
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

        var form = new OverlayForm
        {
            ClickThrough = _clickThroughSwitch.Checked,
            SnapToEdges = _snapSwitch.Checked,
            ClientAreaOnly = true,
            OverlayOpacity = 1.0,
        };
        form.OtherOverlays = () => _overlays
            .Where(o => o.Form != form && !o.Form.IsDisposed && o.Form.Visible)
            .Select(o => o.Form.Bounds);
        var item = new OverlayItem(_nextOverlayNumber++, w, form);

        form.SourceLost += (_, _) =>
        {
            RemoveOverlay(item);
            SetStatus($"Overlay {item.Number} closed because its window ({item.Title}) was closed.", StatusKind.Warning);
        };
        form.FormClosed += (_, _) => RemoveOverlay(item); // e.g. Alt+F4 on the overlay itself
        form.Activated += (_, _) => _overlayList.SelectedItem = item; // clicking an overlay selects it here

        // Default placement: bottom-right of this screen, cascading so overlays don't stack exactly.
        var area = Screen.FromControl(this).WorkingArea;
        var size = new Size(area.Width / 3, area.Height / 3);
        int offset = (_overlays.Count % 6) * this.S(36);
        int margin = this.S(24) + offset;
        form.Bounds = new Rectangle(area.Right - size.Width - margin, area.Bottom - size.Height - margin, size.Width, size.Height);

        form.Source = w.Handle;
        form.Show();
        form.FitToSource();
        // Fitting changes the height; keep the overlay's bottom edge where it was so it stays on screen.
        if (form.Bottom > area.Bottom - margin)
            form.Top = Math.Max(area.Top, area.Bottom - margin - form.Height);

        _overlays.Add(item);
        _overlayList.Items.Add(item);
        _overlayList.SelectedItem = item;
        UpdateOverlayUi();

        if (IsIconic(w.Handle))
            SetStatus($"{w.Title} is minimised. Restore it from the taskbar to see it in the overlay.", StatusKind.Warning);
    }

    private void RemoveOverlay(OverlayItem item)
    {
        if (item.Removed) return;
        item.Removed = true;

        int index = _overlayList.Items.IndexOf(item);
        _overlays.Remove(item);
        _overlayList.Items.Remove(item);
        if (_overlayList.Items.Count > 0 && _overlayList.SelectedIndex < 0)
            _overlayList.SelectedIndex = Math.Min(Math.Max(index, 0), _overlayList.Items.Count - 1);

        if (!item.Form.IsDisposed) item.Form.Close();
        item.Dispose();
        UpdateOverlayUi();
    }

    private void StopAll()
    {
        foreach (var item in _overlays.ToList()) RemoveOverlay(item);
        _clickThroughSwitch.Checked = false;
    }

    private void ApplyClickThrough()
    {
        foreach (var item in _overlays) item.Form.ClickThrough = _clickThroughSwitch.Checked;
        UpdateOverlayUi();
    }

    private void OnOverlaySelectionChanged()
    {
        var selected = SelectedOverlay;
        foreach (var item in _overlays) item.Form.Selected = item == selected;

        if (selected != null)
        {
            // Load the overlay's settings into the controls without re-applying them.
            _loadingSettings = true;
            var form = selected.Form;
            _opacitySlider.Value = (int)Math.Round(form.OverlayOpacity * 100);
            _clientOnlySwitch.Checked = form.ClientAreaOnly;
            _cropLeft.Value = (int)Math.Round(form.Crop.Left * 100);
            _cropTop.Value = (int)Math.Round(form.Crop.Top * 100);
            _cropRight.Value = (int)Math.Round(form.Crop.Right * 100);
            _cropBottom.Value = (int)Math.Round(form.Crop.Bottom * 100);
            _loadingSettings = false;
        }
        UpdateSettingsUi();
    }

    private void ApplySelectedSettings()
    {
        _opacityValue.Text = $"{_opacitySlider.Value}%";
        if (_loadingSettings || SelectedOverlay is not OverlayItem item) return;

        var form = item.Form;
        form.ClientAreaOnly = _clientOnlySwitch.Checked;
        form.OverlayOpacity = _opacitySlider.Value / 100.0;
        form.Crop = new Crop(
            _cropLeft.Value / 100.0, _cropTop.Value / 100.0,
            _cropRight.Value / 100.0, _cropBottom.Value / 100.0);
        _overlayList.Invalidate(); // summary line shows opacity / cropped
    }

    private void UpdateOverlayUi()
    {
        int count = _overlays.Count;
        _stopAllButton.Enabled = count > 0;
        _overlayEmpty.Visible = count == 0;
        _overlayList.Visible = count > 0;

        var liveCounts = new Dictionary<IntPtr, int>();
        foreach (var item in _overlays)
            liveCounts[item.Source] = liveCounts.GetValueOrDefault(item.Source) + 1;
        _windowList.LiveCounts = liveCounts;

        UpdateSettingsUi();

        if (count == 0)
            SetStatus("No overlays running.");
        else
            SetStatus($"{count} overlay{(count == 1 ? "" : "s")} running"
                + (_clickThroughSwitch.Checked ? " · locked (click-through)" : " · unlocked: drag to move, drag edges to resize"),
                StatusKind.Live);
    }

    private void UpdateSettingsUi()
    {
        var selected = SelectedOverlay;
        foreach (var control in _settingsControls) control.Enabled = selected != null;
        _settingsSubtitle.Text = selected == null
            ? "Select an overlay to change its settings."
            : $"Overlay {selected.Number} · {selected.Title}";
        // Labels are dimmed by colour rather than Enabled=false, which draws embossed grey text.
        foreach (var (label, normal) in _settingsLabels)
            label.ForeColor = selected != null ? normal() : Theme.Track;
    }

    // ---------------------------------------------------------------- hotkey

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
            ? "Lock all overlays and let clicks pass through"
            : $"Lock all, click-through · {HotkeyBox.Describe(hotkey)}";
        _clickThroughSwitch.Invalidate();
    }

    private void SetStatus(string text, StatusKind kind = StatusKind.Idle) => _status.Set(text, kind);
}
