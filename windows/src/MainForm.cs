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
    // Each side can crop up to 98%; opposite sides are kept from overlapping (at least 2% stays visible).
    private const int MaxCropPercent = 98;
    private readonly Slider _cropLeft = new(0, MaxCropPercent, 0) { Dock = DockStyle.Fill };
    private readonly Slider _cropTop = new(0, MaxCropPercent, 0) { Dock = DockStyle.Fill };
    private readonly Slider _cropRight = new(0, MaxCropPercent, 0) { Dock = DockStyle.Fill };
    private readonly Slider _cropBottom = new(0, MaxCropPercent, 0) { Dock = DockStyle.Fill };
    private readonly ModernButton _cropReset = new("Reset", ButtonKind.Ghost, Theme.GlyphReset);
    private readonly ModernButton _cropSelect = new("Select area", ButtonKind.Ghost, Theme.GlyphCrop);
    private readonly List<Control> _settingsControls = new();
    private readonly List<(Label Label, Func<Color> Normal)> _settingsLabels = new();

    // Updates
    private readonly ModernButton _checkUpdatesButton = new("Check for updates", ButtonKind.Ghost, Theme.GlyphSync);
    private readonly ToggleSwitch _autoUpdateSwitch = new("Check for updates on startup");
    private readonly Card _updateBanner = new() { Dock = DockStyle.Fill, Visible = false, BorderColor = Theme.Accent };
    private readonly Label _updateTitle = MakeLabel("", Theme.BodyStrong, Theme.Text);
    private readonly Label _updateDetail = MakeLabel("", Theme.Small, Theme.TextMuted);
    private UpdateInfo? _availableUpdate;
    private bool _checkingForUpdates;

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
        // Layout below is in 96-DPI units; WinForms scales it for the monitor the window opens on and
        // rescales it when the window moves to a monitor with different scaling.
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;

        Text = "Window Overlay";
        // Use the icon embedded in the exe for the title bar and taskbar.
        try { Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!); } catch { }
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Theme.Background;
        ForeColor = Theme.Text;
        Font = Theme.Body;
        ClientSize = new Size(1020, 860); // MinimumSize is set per monitor in ApplyMinimumSize

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
        _autoUpdateSwitch.Checked = _settings.CheckForUpdates;
        _autoUpdateSwitch.CheckedChanged += (_, _) =>
        {
            _settings.CheckForUpdates = _autoUpdateSwitch.Checked;
            _settings.Save();
        };
        _checkUpdatesButton.Click += async (_, _) => await CheckForUpdatesAsync(manual: true);
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
        foreach (var (slider, opposite, side) in new[]
        {
            (_cropLeft, _cropRight, CropSide.Left), (_cropRight, _cropLeft, CropSide.Right),
            (_cropTop, _cropBottom, CropSide.Top), (_cropBottom, _cropTop, CropSide.Bottom),
        })
        {
            slider.ValueChanged += (_, _) => OnCropSliderChanged(slider, opposite, side);
        }
        _cropReset.Click += (_, _) =>
        {
            foreach (var slider in CropSliders) slider.Value = 0;
        };
        _cropSelect.Click += (_, _) => OpenCropPicker();

        // Suspend the global hotkey while recording so pressing the current combo is captured.
        _hotkeyBox.Enter += (_, _) => UnregisterToggleHotkey();
        _hotkeyBox.Leave += (_, _) => { if (!_hotkeyRegistered) RegisterToggleHotkey(_settings.ToggleHotkey); };
        _hotkeyBox.HotkeyChanged += (_, _) => OnHotkeyRecorded();
        _hotkeyBox.Hotkey = _settings.ToggleHotkey;

        _toolTip.SetToolTip(_refreshButton, "Refresh window list");
        _toolTip.SetToolTip(_fitButton, "Fit overlay to the source's shape");
        _toolTip.SetToolTip(_removeButton, "Close this overlay");
        _toolTip.SetToolTip(_cropSelect, "Drag a rectangle over the source to choose exactly what to show");
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
        int gap = 14;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(22, 18, 22, 8),
            ColumnCount = 1,
            RowCount = 4,
            BackColor = Theme.Background,
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // update banner (hidden until needed)
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // status bar (not Absolute: WinForms double-scales absolute rows)

        // Header: title on the left; version and update controls on the right
        var header = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, BackColor = Theme.Background, Margin = new Padding(0, 0, 0, gap) };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var title = MakeLabel("Window Overlay", Theme.Title, Theme.Text);
        title.BackColor = Theme.Background;
        var subtitle = MakeLabel("Mirror any windows into always-on-top overlays that clicks pass straight through.", Theme.Body, Theme.TextMuted);
        subtitle.BackColor = Theme.Background;
        subtitle.Margin = new Padding(2, 2, 0, 0);
        header.Controls.Add(title, 0, 0);
        header.Controls.Add(subtitle, 0, 1);

        var versionRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, BackColor = Theme.Background, Anchor = AnchorStyles.Right, Margin = Padding.Empty };
        var version = MakeLabel($"v{UpdateChecker.Format(UpdateChecker.CurrentVersion)}", Theme.Small, Theme.TextMuted);
        version.BackColor = Theme.Background;
        version.Margin = new Padding(0, 7, 6, 0);
        _checkUpdatesButton.Font = Theme.Small;
        _checkUpdatesButton.BackColor = Theme.Background;
        _checkUpdatesButton.Size = new Size(_checkUpdatesButton.LogicalPreferredWidth, 28);
        _checkUpdatesButton.Margin = Padding.Empty;
        versionRow.Controls.Add(version);
        versionRow.Controls.Add(_checkUpdatesButton);
        header.Controls.Add(versionRow, 1, 0);
        _autoUpdateSwitch.BackColor = Theme.Background;
        _autoUpdateSwitch.Font = Theme.Small;
        _autoUpdateSwitch.Size = new Size(240, 26);
        _autoUpdateSwitch.Anchor = AnchorStyles.Right;
        _autoUpdateSwitch.Margin = new Padding(0, 2, 0, 0);
        header.Controls.Add(_autoUpdateSwitch, 1, 1);
        root.Controls.Add(header, 0, 0);

        root.Controls.Add(BuildUpdateBanner(gap), 0, 1);

        // Main area: window list on the left, overlays + settings on the right
        int sideWidth = 390;
        var main = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = Theme.Background, Margin = Padding.Empty };
        main.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        main.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, sideWidth + gap));
        main.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        main.Controls.Add(BuildWindowsCard(), 0, 0);
        main.Controls.Add(BuildSidePanel(sideWidth, gap), 1, 0);
        root.Controls.Add(main, 0, 2);

        root.Controls.Add(_status, 0, 3);
        _status.Margin = new Padding(2, 6, 0, 0);
        _status.Height = 30;

        Controls.Add(root);
    }

    private Card BuildWindowsCard()
    {
        var card = new Card { Dock = DockStyle.Fill, Margin = Padding.Empty };

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, BackColor = Theme.Surface, Margin = Padding.Empty };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var heading = MakeLabel("Windows", Theme.Section, Theme.Text);
        var search = new TextHost(_searchBox, Theme.GlyphSearch);
        var toolbar = new ToolbarRow(heading, search, _refreshButton, leftGap: 16, rightGap: 8)
        {
            Dock = DockStyle.Fill,
            Height = 34,
            Margin = new Padding(0, 0, 0, 10),
        };
        layout.Controls.Add(toolbar, 0, 0);

        _windowList.Margin = Padding.Empty;
        layout.Controls.Add(_windowList, 0, 1);

        var hint = MakeLabel("Double-click a window (or press Enter) to add an overlay. Add the same window more than once to show different cropped parts of it. "
            + "Sources can be covered by other windows but must not be minimised.", Theme.Small, Theme.TextMuted);
        hint.MaximumSize = new Size(520, 0);
        hint.Margin = new Padding(0, 10, 0, 0);
        layout.Controls.Add(hint, 0, 2);

        card.Controls.Add(layout);
        return card;
    }

    private Panel BuildSidePanel(int width, int gap)
    {
        var side = new CardStack(gap) { Dock = DockStyle.Fill, Margin = new Padding(gap, 0, 0, 0) };

        var overlaysCard = BuildCard(width, "Overlays", null, rows =>
        {
            var buttons = new TableLayoutPanel { Height = 38, Dock = DockStyle.Top, ColumnCount = 2, BackColor = Theme.Surface, Margin = Padding.Empty };
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60));
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
            _addButton.Margin = new Padding(0, 0, 4, 0);
            _stopAllButton.Margin = new Padding(4, 0, 0, 0);
            buttons.Controls.Add(_addButton, 0, 0);
            buttons.Controls.Add(_stopAllButton, 1, 0);
            rows.Add(buttons);
            rows.Add(Spacer(10));

            var listHost = new Panel { Dock = DockStyle.Top, Height = 44 * 3 + 4, BackColor = Theme.Surface };
            listHost.Controls.Add(_overlayList);
            listHost.Controls.Add(_overlayEmpty);
            rows.Add(listHost);
            rows.Add(Spacer(10));

            rows.Add(_clickThroughSwitch);
            rows.Add(Spacer(8));

            var hotkeyRow = new TableLayoutPanel { Height = 34, Dock = DockStyle.Top, ColumnCount = 2, BackColor = Theme.Surface, Margin = Padding.Empty };
            hotkeyRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            hotkeyRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180));
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
            _settingsSubtitle.Height = 24;
            _settingsSubtitle.TextAlign = ContentAlignment.TopLeft;
            rows.Add(_settingsSubtitle);

            var opacityRow = new TableLayoutPanel { Height = 22, Dock = DockStyle.Top, ColumnCount = 2, BackColor = Theme.Surface, Margin = Padding.Empty };
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

            var cropHeader = new TableLayoutPanel { Height = 30, Dock = DockStyle.Top, ColumnCount = 3, BackColor = Theme.Surface, Margin = Padding.Empty };
            cropHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            cropHeader.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            cropHeader.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            var cropLabel = MakeLabel("Crop", Theme.Body, Theme.Text);
            cropLabel.Anchor = AnchorStyles.Left;
            foreach (var button in new[] { _cropSelect, _cropReset })
            {
                button.Font = Theme.Small;
                button.Size = new Size(button.LogicalPreferredWidth, 28);
                button.Anchor = AnchorStyles.Right;
                button.Margin = new Padding(4, 0, 0, 0);
            }
            cropHeader.Controls.Add(cropLabel, 0, 0);
            cropHeader.Controls.Add(_cropSelect, 1, 0);
            cropHeader.Controls.Add(_cropReset, 2, 0);
            rows.Add(cropHeader);

            // Two crop sliders per row: Left/Right, Top/Bottom.
            var cropGrid = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 7, RowCount = 2, BackColor = Theme.Surface, Margin = Padding.Empty };
            foreach (var style in new[]
            {
                new ColumnStyle(SizeType.Absolute, 50), new ColumnStyle(SizeType.Percent, 50), new ColumnStyle(SizeType.Absolute, 36),
                new ColumnStyle(SizeType.Absolute, 12),
                new ColumnStyle(SizeType.Absolute, 50), new ColumnStyle(SizeType.Percent, 50), new ColumnStyle(SizeType.Absolute, 36),
            }) cropGrid.ColumnStyles.Add(style);

            var cells = new[] { ("Left", _cropLeft, 0, 0), ("Right", _cropRight, 0, 4), ("Top", _cropTop, 1, 0), ("Bottom", _cropBottom, 1, 4) };
            foreach (var (name, slider, row, col) in cells)
            {
                var nameLabel = MakeLabel(name, Theme.Body, Theme.TextMuted);
                nameLabel.Anchor = AnchorStyles.Left;
                var valueLabel = MakeLabel("0%", Theme.Small, Theme.TextMuted);
                valueLabel.Anchor = AnchorStyles.Right;
                slider.Margin = new Padding(0, 2, 4, 0);
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
            // Percent rows, not Absolute: WinForms scales absolute row heights twice on DPI changes.
            cropGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            cropGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            cropGrid.Height = 30 * 2;
            rows.Add(cropGrid);

            foreach (var label in new[] { opacityLabel, _opacityValue, cropLabel }) _settingsLabels.Add((label, () => Theme.Text));
        });

        _settingsControls.AddRange(new Control[] { _fitButton, _removeButton, _opacitySlider, _clientOnlySwitch, _cropReset, _cropSelect });
        _settingsControls.AddRange(CropSliders);

        // CardStack places them top to bottom at full width.
        side.Controls.Add(overlaysCard);
        side.Controls.Add(settingsCard);
        return side;
    }

    /// <summary>Builds a card with a section title (plus optional header buttons) and rows stacked top to bottom.</summary>
    private Card BuildCard(int width, string title, Control[]? headerButtons, Action<List<Control>> addRows)
    {
        var card = new Card { Width = width, Margin = Padding.Empty };
        var rows = new List<Control>();

        var header = new TableLayoutPanel { Dock = DockStyle.Top, Height = 32, BackColor = Theme.Surface, Margin = Padding.Empty, ColumnCount = 1 + (headerButtons?.Length ?? 0) };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var heading = MakeLabel(title, Theme.Section, Theme.Text);
        heading.Anchor = AnchorStyles.Left | AnchorStyles.Top;
        header.Controls.Add(heading, 0, 0);
        if (headerButtons != null)
        {
            for (int i = 0; i < headerButtons.Length; i++)
            {
                header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
                headerButtons[i].Size = new Size(30, 28);
                headerButtons[i].Margin = new Padding(4, 0, 0, 0);
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
        new Panel { Dock = DockStyle.Top, Height = height, BackColor = Theme.Surface };

    // ---------------------------------------------------------------- form lifecycle

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.UseDarkTitleBar(Handle);
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e); // scales the 96-DPI layout for the monitor the window opened on

        // Shrink to fit smaller screens (the settings column scrolls if needed).
        var area = Screen.FromControl(this).WorkingArea;
        Size = new Size(Math.Min(Width, area.Width - 40), Math.Min(Height, area.Height - 40));
        ApplyMinimumSize();
        CenterToScreen();

        RefreshWindowList();
        ActiveControl = _windowList;
        if (!RegisterToggleHotkey(_settings.ToggleHotkey))
            SetStatus($"{HotkeyBox.Describe(_settings.ToggleHotkey)} is already used by another app. Pick another hotkey or use the switch.", StatusKind.Warning);
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        // Moved to a monitor with different scaling. Drop the old minimum first: it's in the old
        // monitor's pixels and could stop Windows from resizing the window to its new size.
        MinimumSize = Size.Empty;
        base.OnDpiChanged(e); // resizes the window and rescales the layout (AutoScaleMode.Dpi)
        ApplyMinimumSize();
    }

    /// <summary>Minimum window size for the current monitor's scaling, never larger than its work area.</summary>
    private void ApplyMinimumSize()
    {
        var area = Screen.FromControl(this).WorkingArea;
        MinimumSize = new Size(Math.Min(this.S(860), area.Width - 40), Math.Min(this.S(600), area.Height - 40));
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        // In the background, after the window is up, so a slow network never delays startup.
        if (_settings.CheckForUpdates) _ = CheckForUpdatesAsync(manual: false);
    }

    // ---------------------------------------------------------------- updates

    private Card BuildUpdateBanner(int gap)
    {
        _updateBanner.Margin = new Padding(0, 0, 0, gap);
        _updateBanner.Height = 64;
        _updateBanner.Padding = new Padding(16, 8, 10, 8);

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 2, BackColor = Theme.Surface, Margin = Padding.Empty };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int i = 0; i < 3; i++) layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 50));

        _updateTitle.Anchor = AnchorStyles.Left | AnchorStyles.Bottom;
        _updateDetail.Anchor = AnchorStyles.Left | AnchorStyles.Top;
        _updateDetail.Margin = new Padding(0, 2, 0, 0);
        layout.Controls.Add(_updateTitle, 0, 0);
        layout.Controls.Add(_updateDetail, 0, 1);

        var download = new ModernButton("Download", ButtonKind.Primary, Theme.GlyphDownload);
        var skip = new ModernButton("Skip this version", ButtonKind.Secondary);
        var dismiss = new ModernButton("", ButtonKind.Ghost, Theme.GlyphClose);
        download.Size = new Size(download.LogicalPreferredWidth, 36);
        skip.Size = new Size(skip.LogicalPreferredWidth, 36);
        dismiss.Size = new Size(36, 36);
        int column = 1;
        foreach (var button in new[] { download, skip, dismiss })
        {
            button.Anchor = AnchorStyles.None;
            button.Margin = new Padding(8, 0, 0, 0);
            layout.Controls.Add(button, column++, 0);
            layout.SetRowSpan(button, 2);
        }

        download.Click += (_, _) =>
        {
            if (_availableUpdate is { } update) UpdateChecker.OpenInBrowser(update.PageUrl);
        };
        skip.Click += (_, _) =>
        {
            if (_availableUpdate is not { } update) return;
            _settings.SkippedUpdateTag = update.Tag;
            _settings.Save();
            _updateBanner.Visible = false;
            SetStatus($"Skipped version {UpdateChecker.Format(update.Version)}. \"Check for updates\" will still show it.");
        };
        dismiss.Click += (_, _) => _updateBanner.Visible = false;
        _toolTip.SetToolTip(download, "Open the release page on GitHub");
        _toolTip.SetToolTip(skip, "Don't mention this version again on startup");
        _toolTip.SetToolTip(dismiss, "Hide until the app is next started");

        _updateBanner.Controls.Add(layout);
        return _updateBanner;
    }

    /// <param name="manual">True when the user pressed "Check for updates": report every outcome and
    /// show skipped versions. Startup checks stay quiet unless there's a new, unskipped version.</param>
    private async Task CheckForUpdatesAsync(bool manual)
    {
        if (_checkingForUpdates) return;
        _checkingForUpdates = true;
        _checkUpdatesButton.Enabled = false;
        if (manual) SetStatus("Checking GitHub for a newer version…");
        try
        {
            var result = await UpdateChecker.CheckAsync();
            if (IsDisposed) return;
            string current = UpdateChecker.Format(UpdateChecker.CurrentVersion);
            switch (result.Status)
            {
                case UpdateCheckStatus.UpdateAvailable when result.Update is { } update:
                    if (manual || update.Tag != _settings.SkippedUpdateTag)
                    {
                        _availableUpdate = update;
                        _updateTitle.Text = $"Window Overlay {UpdateChecker.Format(update.Version)} is available";
                        _updateDetail.Text = $"You have {current} · \"{update.Name}\" on GitHub";
                        _updateBanner.Visible = true;
                    }
                    if (manual) SetStatus($"Version {UpdateChecker.Format(update.Version)} is available.");
                    break;
                case UpdateCheckStatus.UpToDate:
                    if (manual) SetStatus($"You're up to date (v{current}).");
                    break;
                case UpdateCheckStatus.NoReleases:
                    if (manual) SetStatus("No releases have been published on GitHub yet.");
                    break;
                default:
                    if (manual) SetStatus($"Couldn't check for updates: {result.Error}", StatusKind.Warning);
                    break;
            }
        }
        finally
        {
            _checkingForUpdates = false;
            if (!IsDisposed) _checkUpdatesButton.Enabled = true;
        }
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
            _loadingSettings = false;
            ShowCropOnSliders(form.Crop);
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
        _overlayList.Invalidate(); // summary line shows opacity / cropped
    }

    private enum CropSide { Left, Top, Right, Bottom }

    /// <summary>Shows a crop on the sliders (rounded to whole percents) without changing the overlay.</summary>
    private void ShowCropOnSliders(Crop crop)
    {
        _loadingSettings = true;
        _cropLeft.Value = (int)Math.Round(crop.Left * 100);
        _cropTop.Value = (int)Math.Round(crop.Top * 100);
        _cropRight.Value = (int)Math.Round(crop.Right * 100);
        _cropBottom.Value = (int)Math.Round(crop.Bottom * 100);
        _loadingSettings = false;
    }

    /// <summary>
    /// Applies one crop slider. Only that side changes, so a precise crop made with "Select area" isn't
    /// rounded off when another slider is moved.
    /// </summary>
    private void OnCropSliderChanged(Slider slider, Slider opposite, CropSide side)
    {
        if (_loadingSettings) return;
        if (slider.Value + opposite.Value > MaxCropPercent)
        {
            slider.Value = MaxCropPercent - opposite.Value; // re-enters with the clamped value
            return;
        }
        if (SelectedOverlay is not OverlayItem item) return;

        var crop = item.Form.Crop;
        double value = slider.Value / 100.0;
        item.Form.Crop = side switch
        {
            CropSide.Left => crop with { Left = value },
            CropSide.Top => crop with { Top = value },
            CropSide.Right => crop with { Right = value },
            _ => crop with { Bottom = value },
        };
        _overlayList.Invalidate();
    }

    private void OpenCropPicker()
    {
        if (SelectedOverlay is not OverlayItem item) return;
        var form = item.Form;
        var sourceSize = form.SourceSize;
        if (sourceSize.Width <= 0 || sourceSize.Height <= 0)
        {
            SetStatus($"Can't read {item.Title}'s size. Is it minimised?", StatusKind.Warning);
            return;
        }

        using var picker = new CropPickerForm(form.Source, form.ClientAreaOnly, sourceSize, form.Crop,
            crop => form.Crop = crop, $"Overlay {item.Number} · {item.Title}");
        if (picker.ShowDialog(this) == DialogResult.OK)
        {
            form.Crop = picker.Result;
            ShowCropOnSliders(picker.Result);
            _overlayList.Invalidate();
            var px = form.SourceSize;
            SetStatus($"Overlay {item.Number} now shows a {Math.Round(px.Width * (1 - picker.Result.Left - picker.Result.Right))} × "
                + $"{Math.Round(px.Height * (1 - picker.Result.Top - picker.Result.Bottom))} px area.");
        }
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
