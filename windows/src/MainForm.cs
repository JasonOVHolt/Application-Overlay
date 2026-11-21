using static OverlayApp.NativeMethods;

namespace OverlayApp;

internal sealed class MainForm : Form
{
    private const int LockHotkeyId = 1, HideHotkeyId = 2; // WM_HOTKEY ids

    // Windows / Presets card
    private readonly TabStrip _sourceTabs = new("Windows", "Presets");
    private readonly WindowListBox _windowList = new() { Dock = DockStyle.Fill };
    private readonly TextBox _searchBox = new() { PlaceholderText = "Search windows" };
    private readonly ModernButton _refreshButton = new("", ButtonKind.Secondary, Theme.GlyphRefresh);
    private readonly Label _windowsHint = MakeLabel(
        "Double-click a window (or press Enter) to add an overlay. Add the same window more than once to show different cropped parts of it. "
        + "Sources can be covered by other windows but must not be minimised.", Theme.Small, Theme.TextMuted);
    private readonly PresetListBox _presetList = new();
    private readonly Label _presetEmpty = new()
    {
        Text = "No presets yet.\nSelect an overlay and press Save selected overlay to keep its position, size and crop.",
        TextAlign = ContentAlignment.MiddleCenter,
        ForeColor = Theme.TextMuted,
        BackColor = Theme.Surface,
        Font = Theme.Small,
    };
    private readonly ModernButton _savePresetButton = new("Save selected overlay", ButtonKind.Primary, Theme.GlyphSave);
    private Control _presetsBar = null!; // built in BuildWindowsCard

    // Overlays card
    private readonly ModernButton _addButton = new("Add overlay", ButtonKind.Primary, Theme.GlyphAdd) { Dock = DockStyle.Fill };
    private readonly ModernButton _hideAllButton = new("Hide all", ButtonKind.Secondary, Theme.GlyphView) { Dock = DockStyle.Fill, Enabled = false, GlyphSlashed = true };
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
    private readonly ToggleSwitch _snapSwitch = new("Snap to edges", "Sticks to screen edges and other overlays when dragged") { Dock = DockStyle.Top };
    private readonly ToggleSwitch _defaultSnapSwitch = new("New overlays snap to edges", "Each overlay can still be changed in Overlay settings") { Dock = DockStyle.Top };

    // Settings page
    private readonly ModernButton _settingsButton = new("Settings", ButtonKind.Ghost, Theme.GlyphSettings);
    private readonly ModernButton _backButton = new("Back", ButtonKind.Ghost, Theme.GlyphBack);
    private Control _mainArea = null!, _settingsPage = null!; // built in BuildLayout
    private readonly SegmentedControl _themePicker = new("Dark", "Light", "Match Windows") { Dock = DockStyle.Top };
    private readonly AccentPicker _accentPicker = new() { Dock = DockStyle.Top };
    private readonly FieldHeader _snapDistanceHeader = new("Snap distance", "How close an edge must be before an overlay snaps to it") { Dock = DockStyle.Top };
    private readonly Slider _snapDistanceSlider = new(5, 60, 20) { Dock = DockStyle.Top };
    private readonly SegmentedControl _reconnectPicker = new("Off", "30 s", "1 min", "2 min", "5 min") { Dock = DockStyle.Top };
    private static readonly int[] ReconnectChoices = { 0, 30, 60, 120, 300 }; // seconds, matching _reconnectPicker
    private readonly FieldHeader _defaultOpacityHeader = new("Opacity of new overlays") { Dock = DockStyle.Top };
    private readonly Slider _defaultOpacitySlider = new(10, 100, 100) { Dock = DockStyle.Top };
    private readonly ToggleSwitch _defaultFadeSwitch = new("New overlays fade when the mouse is over them", "Each overlay can still be changed in Overlay settings") { Dock = DockStyle.Top };
    private readonly ToggleSwitch _startupSwitch = new("Start with Windows", "Opens when you sign in") { Dock = DockStyle.Top };
    private readonly ToggleSwitch _traySwitch = new("Keep running in the notification area", "Closing this window keeps overlays and hotkeys running") { Dock = DockStyle.Top };
    private readonly ModernButton _resetButton = new("Reset settings", ButtonKind.Secondary, Theme.GlyphReset);
    private readonly ModernButton _exportButton = new("Export…", ButtonKind.Secondary, Theme.GlyphExport);
    private readonly ModernButton _importButton = new("Import…", ButtonKind.Secondary, Theme.GlyphImport);

    // Notification area
    private readonly NotifyIcon _tray = new() { Text = "Window Overlay" };
    private readonly uint _showMessage; // posted by a second launch of the app: show this window
    private bool _exiting, _trayTipShown, _startHidden;
    private bool _syncingLockAll; // updating the "lock all" switch to match the overlays, not applying it
    private readonly HotkeyBox _lockHotkeyBox = new();
    private readonly HotkeyBox _hideHotkeyBox = new();

    // Selected-overlay settings card
    private readonly Label _settingsSubtitle = MakeLabel("", Theme.Small, Theme.TextMuted);
    private readonly ModernButton _savePresetIconButton = new("", ButtonKind.Ghost, Theme.GlyphSave);
    private readonly ModernButton _fitButton = new("", ButtonKind.Ghost, Theme.GlyphFit);
    private readonly ModernButton _removeButton = new("", ButtonKind.Ghost, Theme.GlyphDelete);
    private readonly ToggleSwitch _clientOnlySwitch = new("Client area only", "Hide the title bar and borders") { Dock = DockStyle.Top };
    private readonly ToggleSwitch _lockSwitch = new("Lock this overlay", "Clicks pass through it; unlock it to move or resize it") { Dock = DockStyle.Top };
    private readonly ToggleSwitch _fadeSwitch = new("Fade when the mouse is over it", "While locked, so you can see and click what's underneath") { Dock = DockStyle.Top };
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
    private readonly ToggleSwitch _autoUpdateSwitch = new("Check for updates on startup", "Asks GitHub whether a newer version is out") { Dock = DockStyle.Top };
    private readonly Card _updateBanner = new() { Dock = DockStyle.Fill, Visible = false, Accented = true };
    private readonly Label _updateTitle = MakeLabel("", Theme.BodyStrong, Theme.Text);
    private readonly Label _updateDetail = MakeLabel("", Theme.Small, Theme.TextMuted);
    private UpdateInfo? _availableUpdate;
    private bool _checkingForUpdates;

    private readonly StatusBar _status = new() { Dock = DockStyle.Fill };
    private readonly ToolTip _toolTip = new();
    private readonly System.Windows.Forms.Timer _reconnectTimer = new() { Interval = 1000 }; // runs while overlays wait for their app

    private readonly AppSettings _settings;
    private readonly List<OverlayItem> _overlays = new();
    private List<WindowInfo> _allWindows = new();
    private int _nextOverlayNumber = 1;
    private bool _loadingSettings;
    private readonly GlobalHotkey _lockHotkey;
    private readonly GlobalHotkey _hideHotkey;

    /// <param name="settings">Loaded (and theme applied) by Program before any control is created.</param>
    /// <param name="startedWithWindows">Launched by the "Start with Windows" entry at sign-in.</param>
    /// <param name="showMessage">Window message a second launch posts to bring this window back.</param>
    public MainForm(AppSettings settings, bool startedWithWindows, uint showMessage)
    {
        _settings = settings;
        _showMessage = showMessage;
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
        ClientSize = new Size(1020, 960); // MinimumSize is set per monitor in ApplyMinimumSize; OnLoad shrinks it to fit the screen

        _lockHotkey = new GlobalHotkey(this, LockHotkeyId, "overlay mode", _lockHotkeyBox, _settings.ToggleHotkey);
        _hideHotkey = new GlobalHotkey(this, HideHotkeyId, "hide/show", _hideHotkeyBox, _settings.HideHotkey);

        BuildLayout();
        LoadPreferences();
        SetUpTray();
        if (startedWithWindows)
        {
            // At sign-in, stay out of the way: in the notification area if that's on, else minimised.
            if (_settings.KeepRunningInTray) _startHidden = true;
            else WindowState = FormWindowState.Minimized;
        }

        _refreshButton.Click += (_, _) => RefreshWindowList();
        _searchBox.TextChanged += (_, _) => { ApplyFilter(); ApplyPresetFilter(); };
        _searchBox.KeyDown += OnSearchKeyDown;
        _windowList.DoubleClick += (_, _) => AddOverlay();
        _windowList.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { AddOverlay(); e.Handled = true; } };

        _addButton.Click += (_, _) => AddOverlay();
        _hideAllButton.Click += (_, _) => ToggleHideAll();
        _stopAllButton.Click += (_, _) => StopAll();
        _overlayList.SelectedIndexChanged += (_, _) => OnOverlaySelectionChanged();
        _overlayList.RemoveRequested += (_, item) => RemoveOverlay(item);
        _overlayList.VisibilityToggleRequested += (_, item) =>
        {
            item.Hidden = !item.Hidden;
            UpdateOverlayUi();
        };
        _overlayList.LockToggleRequested += (_, item) => SetLocked(item, !item.Form.ClickThrough);

        _overlayList.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Delete && SelectedOverlay is OverlayItem item) { RemoveOverlay(item); e.Handled = true; }
        };
        _clickThroughSwitch.CheckedChanged += (_, _) => ApplyClickThrough();
        _checkUpdatesButton.Click += async (_, _) => await CheckForUpdatesAsync(manual: true);

        _fitButton.Click += (_, _) => SelectedOverlay?.Form.FitToSource();
        _removeButton.Click += (_, _) => { if (SelectedOverlay is OverlayItem item) RemoveOverlay(item); };
        _clientOnlySwitch.CheckedChanged += (_, _) => ApplySelectedSettings();
        _fadeSwitch.CheckedChanged += (_, _) => ChangeSelected(f => f.FadeOnHover = _fadeSwitch.Checked);
        _snapSwitch.CheckedChanged += (_, _) => ChangeSelected(f => f.SnapToEdges = _snapSwitch.Checked);
        _overlayList.ContextMenuRequested += (_, request) =>
        {
            _overlayList.SelectedItem = request.Item;
            request.Item.Form.OverlayMenu?.Show(request.ScreenPoint);
        };
        _lockSwitch.CheckedChanged += (_, _) =>
        {
            if (!_loadingSettings && SelectedOverlay is OverlayItem item) SetLocked(item, _lockSwitch.Checked);
        };
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

        _sourceTabs.SelectedIndexChanged += (_, _) => ShowSourceTab(_sourceTabs.SelectedIndex);
        _reconnectTimer.Tick += (_, _) => CheckWaitingOverlays();
        _savePresetButton.Click += (_, _) => SavePreset();
        _savePresetIconButton.Click += (_, _) => SavePreset();
        _presetList.ApplyRequested += (_, preset) => ApplyPreset(preset);
        _presetList.RenameRequested += (_, preset) => RenamePreset(preset);
        _presetList.DeleteRequested += (_, preset) => DeletePreset(preset);
        _presetList.DoubleClick += (_, _) => { if (_presetList.SelectedItem is OverlayPreset p) ApplyPreset(p); };
        _presetList.KeyDown += (_, e) =>
        {
            if (_presetList.SelectedItem is not OverlayPreset preset) return;
            if (e.KeyCode == Keys.Enter) ApplyPreset(preset);
            else if (e.KeyCode == Keys.F2) RenamePreset(preset);
            else if (e.KeyCode == Keys.Delete) DeletePreset(preset);
            else return;
            e.Handled = true;
        };

        foreach (var hotkey in new[] { _lockHotkey, _hideHotkey })
        {
            // Suspend the global hotkey while recording so pressing the current combo is captured.
            hotkey.Box.Enter += (_, _) => hotkey.Suspend();
            hotkey.Box.Leave += (_, _) => hotkey.Resume();
            hotkey.Box.HotkeyChanged += (_, _) => OnHotkeyRecorded(hotkey);
            _toolTip.SetToolTip(hotkey.Box, "Click, then press keys. Esc cancels, Backspace clears.");
        }

        _toolTip.SetToolTip(_refreshButton, "Refresh window list");
        _toolTip.SetToolTip(_settingsButton, "Theme, hotkeys, startup and other app-wide settings");
        _toolTip.SetToolTip(_backButton, "Back to your windows and overlays (Esc)");
        _toolTip.SetToolTip(_fitButton, "Fit overlay to the source's shape");
        _toolTip.SetToolTip(_removeButton, "Close this overlay");
        _toolTip.SetToolTip(_cropSelect, "Drag a rectangle over the source to choose exactly what to show");
        _toolTip.SetToolTip(_savePresetIconButton, "Save this overlay's position, size and crop as a preset");
        _toolTip.SetToolTip(_savePresetButton, "Save the selected overlay's position, size and crop as a preset");

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
        version.Margin = new Padding(0, 9, 8, 0);
        _settingsButton.BackColor = Theme.Background;
        _settingsButton.Size = new Size(_settingsButton.LogicalPreferredWidth, 34);
        _settingsButton.Margin = Padding.Empty;
        versionRow.Controls.Add(version);
        versionRow.Controls.Add(_settingsButton);
        header.Controls.Add(versionRow, 1, 0);
        header.SetRowSpan(versionRow, 2);
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

        // The main area and the settings page share the middle row; one is visible at a time.
        var pages = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Background, Margin = Padding.Empty };
        _mainArea = main;
        _settingsPage = BuildSettingsPage(gap);
        _settingsPage.Visible = false;
        pages.Controls.Add(main);
        pages.Controls.Add(_settingsPage);
        root.Controls.Add(pages, 0, 2);

        root.Controls.Add(_status, 0, 3);
        _status.Margin = new Padding(2, 6, 0, 0);
        _status.Height = 30;

        Controls.Add(root);
    }

    /// <summary>Left card: "Windows" and "Presets" tabs sharing one search box.</summary>
    private Card BuildWindowsCard()
    {
        var card = new Card { Dock = DockStyle.Fill, Margin = Padding.Empty };

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, BackColor = Theme.Surface, Margin = Padding.Empty };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // Windows tab hint
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // Presets tab: save button + hint

        var search = new TextHost(_searchBox, Theme.GlyphSearch);
        var toolbar = new ToolbarRow(_sourceTabs, search, _refreshButton, leftGap: 16, rightGap: 8)
        {
            Dock = DockStyle.Fill,
            Height = 34,
            Margin = new Padding(0, 0, 0, 10),
        };
        layout.Controls.Add(toolbar, 0, 0);

        // Both lists share the middle; the selected tab decides which is visible.
        var lists = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Surface, Margin = Padding.Empty };
        _windowList.Dock = _presetList.Dock = _presetEmpty.Dock = DockStyle.Fill;
        lists.Controls.AddRange(new Control[] { _windowList, _presetList, _presetEmpty });
        layout.Controls.Add(lists, 0, 1);

        _windowsHint.MaximumSize = new Size(520, 0);
        _windowsHint.Margin = new Padding(0, 10, 0, 0);
        layout.Controls.Add(_windowsHint, 0, 2);

        var presetsBar = new TableLayoutPanel
        {
            AutoSize = true, Dock = DockStyle.Fill, ColumnCount = 2, BackColor = Theme.Surface, Margin = new Padding(0, 10, 0, 0),
        };
        presetsBar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        presetsBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _savePresetButton.Size = new Size(_savePresetButton.LogicalPreferredWidth, 34);
        _savePresetButton.Margin = new Padding(0, 0, 12, 0);
        var presetsHint = MakeLabel("Select an overlay, then double-click a preset (or press Enter) to apply its position, size and crop.",
            Theme.Small, Theme.TextMuted);
        presetsHint.MaximumSize = new Size(330, 0);
        presetsHint.Anchor = AnchorStyles.Left;
        presetsBar.Controls.Add(_savePresetButton, 0, 0);
        presetsBar.Controls.Add(presetsHint, 1, 0);
        _presetsBar = presetsBar;
        layout.Controls.Add(presetsBar, 0, 3);

        card.Controls.Add(layout);
        ShowSourceTab(0);
        return card;
    }

    private Panel BuildSidePanel(int width, int gap)
    {
        var side = new CardStack(gap) { Dock = DockStyle.Fill, Margin = new Padding(gap, 0, 0, 0) };

        var overlaysCard = BuildCard(width, "Overlays", null, rows =>
        {
            var buttons = new TableLayoutPanel { Height = 38, Dock = DockStyle.Top, ColumnCount = 3, BackColor = Theme.Surface, Margin = Padding.Empty };
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 29));
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 29));
            _addButton.Margin = new Padding(0, 0, 4, 0);
            _hideAllButton.Margin = new Padding(4, 0, 4, 0);
            _stopAllButton.Margin = new Padding(4, 0, 0, 0);
            buttons.Controls.Add(_addButton, 0, 0);
            buttons.Controls.Add(_hideAllButton, 1, 0);
            buttons.Controls.Add(_stopAllButton, 2, 0);
            rows.Add(buttons);
            rows.Add(Spacer(10));

            var listHost = new Panel { Dock = DockStyle.Top, Height = 44 * 5 + 4, BackColor = Theme.Surface };
            listHost.Controls.Add(_overlayList);
            listHost.Controls.Add(_overlayEmpty);
            rows.Add(listHost);
            rows.Add(Spacer(10));

            rows.Add(_clickThroughSwitch);
        });

        var settingsCard = BuildCard(width, "Overlay settings", new Control[] { _savePresetIconButton, _fitButton, _removeButton }, rows =>
        {
            _settingsSubtitle.AutoSize = false;
            _settingsSubtitle.AutoEllipsis = true;
            _settingsSubtitle.Dock = DockStyle.Top;
            _settingsSubtitle.Height = 24;
            _settingsSubtitle.TextAlign = ContentAlignment.TopLeft;
            rows.Add(_settingsSubtitle);
            rows.Add(_lockSwitch);
            rows.Add(Spacer(6));

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

            rows.Add(Spacer(8));
            rows.Add(_fadeSwitch);
            rows.Add(Spacer(4));
            rows.Add(_snapSwitch);

            foreach (var label in new[] { opacityLabel, _opacityValue, cropLabel }) _settingsLabels.Add((label, () => Theme.Text));
        });

        _settingsControls.AddRange(new Control[]
        {
            _savePresetIconButton, _fitButton, _removeButton, _opacitySlider, _clientOnlySwitch, _cropReset, _cropSelect,
            _fadeSwitch, _lockSwitch, _snapSwitch,
        });
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

    private Control HotkeyRow(string label, HotkeyBox box)
    {
        var row = new TableLayoutPanel { Height = 34, Dock = DockStyle.Top, ColumnCount = 2, BackColor = Theme.Surface, Margin = Padding.Empty };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180));
        var text = MakeLabel(label, Theme.Body, Theme.Text);
        text.Anchor = AnchorStyles.Left;
        row.Controls.Add(text, 0, 0);
        row.Controls.Add(new TextHost(box, Theme.GlyphKeyboard) { Dock = DockStyle.Fill, Margin = Padding.Empty }, 1, 0);
        return row;
    }

    private Control Spacer(int height) =>
        new Panel { Dock = DockStyle.Top, Height = height, BackColor = Theme.Surface };

    // ---------------------------------------------------------------- settings page

    /// <summary>App-wide settings, in two columns of cards under a "Back" bar.</summary>
    private Control BuildSettingsPage(int gap)
    {
        var page = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, BackColor = Theme.Background, Margin = Padding.Empty };
        page.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        page.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var bar = new FlowLayoutPanel { AutoSize = true, WrapContents = false, BackColor = Theme.Background, Margin = new Padding(0, 0, 0, 10) };
        _backButton.BackColor = Theme.Background;
        _backButton.Size = new Size(_backButton.LogicalPreferredWidth, 34);
        _backButton.Margin = new Padding(0, 0, 10, 0);
        var heading = MakeLabel("Settings", Theme.Section, Theme.Text);
        heading.BackColor = Theme.Background;
        heading.Margin = new Padding(0, 6, 0, 0);
        bar.Controls.Add(_backButton);
        bar.Controls.Add(heading);
        page.Controls.Add(bar, 0, 0);

        var columns = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = Theme.Background, Margin = Padding.Empty };
        columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        columns.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var left = new CardStack(gap) { Dock = DockStyle.Fill, Margin = new Padding(0, 0, gap / 2, 0) };
        var right = new CardStack(gap) { Dock = DockStyle.Fill, Margin = new Padding(gap / 2, 0, 0, 0) };
        columns.Controls.Add(left, 0, 0);
        columns.Controls.Add(right, 1, 0);
        page.Controls.Add(columns, 0, 1);

        left.Controls.Add(BuildCard(0, "Appearance", null, rows =>
        {
            rows.Add(new FieldHeader("Theme") { Dock = DockStyle.Top });
            rows.Add(_themePicker);
            rows.Add(Spacer(14));
            rows.Add(new FieldHeader("Accent colour", "Buttons, switches and the outline of the selected overlay") { Dock = DockStyle.Top });
            rows.Add(Spacer(4));
            rows.Add(_accentPicker);
        }));

        left.Controls.Add(BuildCard(0, "Overlays", null, rows =>
        {
            rows.Add(_snapDistanceHeader);
            rows.Add(_snapDistanceSlider);
            rows.Add(Spacer(12));
            rows.Add(new FieldHeader("When a window closes, wait for it to reopen", "Its overlay then reconnects to the app's new window") { Dock = DockStyle.Top });
            rows.Add(Spacer(4));
            rows.Add(_reconnectPicker);
            rows.Add(Spacer(14));
            rows.Add(_defaultOpacityHeader);
            rows.Add(_defaultOpacitySlider);
            rows.Add(Spacer(6));
            rows.Add(_defaultFadeSwitch);
            rows.Add(Spacer(4));
            rows.Add(_defaultSnapSwitch);
        }));

        right.Controls.Add(BuildCard(0, "Hotkeys", null, rows =>
        {
            rows.Add(Spacer(2));
            rows.Add(HotkeyRow("Overlay mode (lock all)", _lockHotkeyBox));
            rows.Add(Spacer(8));
            rows.Add(HotkeyRow("Hide/show all overlays", _hideHotkeyBox));
        }));

        right.Controls.Add(BuildCard(0, "Startup", null, rows =>
        {
            rows.Add(_startupSwitch);
            rows.Add(Spacer(4));
            rows.Add(_traySwitch);
        }));

        right.Controls.Add(BuildCard(0, "Updates", null, rows =>
        {
            rows.Add(_autoUpdateSwitch);
            rows.Add(Spacer(8));
            var row = new TableLayoutPanel { Height = 34, Dock = DockStyle.Top, ColumnCount = 2, BackColor = Theme.Surface, Margin = Padding.Empty };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            var version = MakeLabel($"You have version {UpdateChecker.Format(UpdateChecker.CurrentVersion)}", Theme.Body, Theme.TextMuted);
            version.Anchor = AnchorStyles.Left;
            _checkUpdatesButton.Size = new Size(_checkUpdatesButton.LogicalPreferredWidth + 8, 34);
            _checkUpdatesButton.Kind = ButtonKind.Secondary;
            _checkUpdatesButton.Anchor = AnchorStyles.Right;
            _checkUpdatesButton.Margin = Padding.Empty;
            row.Controls.Add(version, 0, 0);
            row.Controls.Add(_checkUpdatesButton, 1, 0);
            rows.Add(row);
        }));

        right.Controls.Add(BuildCard(0, "Backup", null, rows =>
        {
            var row = new TableLayoutPanel { Height = 40, Dock = DockStyle.Top, ColumnCount = 3, BackColor = Theme.Surface, Margin = Padding.Empty };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            var note = MakeLabel("Save settings and presets to a file, or load them.", Theme.Small, Theme.TextMuted);
            note.MaximumSize = new Size(190, 0);
            note.Anchor = AnchorStyles.Left;
            foreach (var button in new[] { _exportButton, _importButton })
            {
                button.Size = new Size(button.LogicalPreferredWidth, 34);
                button.Anchor = AnchorStyles.Right;
                button.Margin = new Padding(8, 0, 0, 0);
            }
            row.Controls.Add(note, 0, 0);
            row.Controls.Add(_exportButton, 1, 0);
            row.Controls.Add(_importButton, 2, 0);
            rows.Add(row);
        }));

        right.Controls.Add(BuildCard(0, "Reset", null, rows =>
        {
            var row = new TableLayoutPanel { Height = 40, Dock = DockStyle.Top, ColumnCount = 2, BackColor = Theme.Surface, Margin = Padding.Empty };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            var note = MakeLabel("Put everything on this page back to its default. Your presets are kept.", Theme.Small, Theme.TextMuted);
            note.MaximumSize = new Size(250, 0);
            note.Anchor = AnchorStyles.Left;
            _resetButton.Size = new Size(_resetButton.LogicalPreferredWidth, 34);
            _resetButton.Anchor = AnchorStyles.Right;
            _resetButton.Margin = Padding.Empty;
            row.Controls.Add(note, 0, 0);
            row.Controls.Add(_resetButton, 1, 0);
            rows.Add(row);
        }));

        return page;
    }

    /// <summary>Puts the saved app-wide settings on the settings page, then hooks up their handlers.</summary>
    private void LoadPreferences()
    {
        ShowPreferenceValues();
        _startupSwitch.Checked = StartWithWindows.IsEnabled;
        OverlayItem.ReconnectWindow = TimeSpan.FromSeconds(_settings.ReconnectSeconds);
        UpdatePreferenceText();

        _settingsButton.Click += (_, _) => ShowSettingsPage(true);
        _backButton.Click += (_, _) => ShowSettingsPage(false);
        _themePicker.SelectedIndexChanged += (_, _) => SavePreference(s => s.ThemeMode = (ThemeMode)_themePicker.SelectedIndex, applyTheme: true);
        _accentPicker.SelectedChanged += (_, _) => SavePreference(s => s.Accent = _accentPicker.SelectedName, applyTheme: true);
        _accentPicker.HoverChanged += (_, _) => _toolTip.SetToolTip(_accentPicker, _accentPicker.HoverName);
        _accentPicker.CustomRequested += (_, _) => PickCustomAccent();
        _defaultSnapSwitch.CheckedChanged += (_, _) => SavePreference(s => s.SnapToEdges = _defaultSnapSwitch.Checked);
        _snapDistanceSlider.ValueChanged += (_, _) =>
        {
            SavePreference(s => s.SnapDistance = _snapDistanceSlider.Value);
            foreach (var item in _overlays) item.Form.SnapDistanceLogical = _snapDistanceSlider.Value;
        };
        _reconnectPicker.SelectedIndexChanged += (_, _) =>
        {
            SavePreference(s => s.ReconnectSeconds = ReconnectChoices[_reconnectPicker.SelectedIndex]);
            OverlayItem.ReconnectWindow = TimeSpan.FromSeconds(_settings.ReconnectSeconds);
        };
        _defaultOpacitySlider.ValueChanged += (_, _) => SavePreference(s => s.DefaultOpacity = _defaultOpacitySlider.Value);
        _defaultFadeSwitch.CheckedChanged += (_, _) => SavePreference(s => s.DefaultFadeOnHover = _defaultFadeSwitch.Checked);
        _startupSwitch.CheckedChanged += (_, _) =>
        {
            if (_loadingSettings || StartWithWindows.Set(_startupSwitch.Checked)) return;
            SetStatus("Couldn't change the Windows startup setting.", StatusKind.Warning);
            _loadingSettings = true;
            _startupSwitch.Checked = StartWithWindows.IsEnabled;
            _loadingSettings = false;
        };
        _traySwitch.CheckedChanged += (_, _) =>
        {
            SavePreference(s => s.KeepRunningInTray = _traySwitch.Checked);
            _tray.Visible = _traySwitch.Checked;
        };
        _autoUpdateSwitch.CheckedChanged += (_, _) => SavePreference(s => s.CheckForUpdates = _autoUpdateSwitch.Checked);
        _resetButton.Click += (_, _) => ResetPreferences();
        _exportButton.Click += (_, _) => ExportSettings();
        _importButton.Click += (_, _) => ImportSettings();

        Theme.Changed += OnThemeChanged;
        Microsoft.Win32.SystemEvents.UserPreferenceChanged += OnWindowsPreferenceChanged;
    }

    /// <summary>Shows <see cref="_settings"/> on the settings page's controls without treating it as a change.</summary>
    private void ShowPreferenceValues()
    {
        bool wasLoading = _loadingSettings;
        _loadingSettings = true;
        _themePicker.SelectedIndex = (int)_settings.ThemeMode;
        _accentPicker.CustomColor = Theme.TryParseHex(_settings.CustomAccent, out var custom) ? custom : null;
        _accentPicker.SelectedName = _settings.Accent;
        _defaultSnapSwitch.Checked = _settings.SnapToEdges;
        _snapDistanceSlider.Value = _settings.SnapDistance;
        _reconnectPicker.SelectedIndex = ReconnectIndex(_settings.ReconnectSeconds);
        _defaultOpacitySlider.Value = _settings.DefaultOpacity;
        _defaultFadeSwitch.Checked = _settings.DefaultFadeOnHover;
        _traySwitch.Checked = _settings.KeepRunningInTray;
        _autoUpdateSwitch.Checked = _settings.CheckForUpdates;
        _loadingSettings = wasLoading;
    }

    /// <summary>
    /// Takes every app-wide setting (not presets) from <paramref name="source"/>, applies them all and saves.
    /// Returns hotkeys that couldn't be used because another app has them.
    /// </summary>
    private List<string> ApplyPreferencesFrom(AppSettings source)
    {
        _settings.ThemeMode = Enum.IsDefined(source.ThemeMode) ? source.ThemeMode : ThemeMode.Dark;
        _settings.Accent = source.Accent;
        _settings.CustomAccent = source.CustomAccent;
        _settings.SnapToEdges = source.SnapToEdges;
        _settings.SnapDistance = Math.Clamp(source.SnapDistance, 5, 60);
        _settings.ReconnectSeconds = ReconnectChoices[ReconnectIndex(source.ReconnectSeconds)];
        _settings.DefaultOpacity = Math.Clamp(source.DefaultOpacity, 10, 100);
        _settings.DefaultFadeOnHover = source.DefaultFadeOnHover;
        _settings.KeepRunningInTray = source.KeepRunningInTray;
        _settings.CheckForUpdates = source.CheckForUpdates;
        _settings.SkippedUpdateTag = source.SkippedUpdateTag;
        var taken = SetHotkeys(source.ToggleHotkey, source.HideHotkey);

        ShowPreferenceValues();
        Theme.Apply(_settings.ThemeMode, _settings.AccentColor());
        _tray.Visible = _settings.KeepRunningInTray;
        OverlayItem.ReconnectWindow = TimeSpan.FromSeconds(_settings.ReconnectSeconds);
        foreach (var item in _overlays)
        {
            item.Form.SnapDistanceLogical = _settings.SnapDistance;
        }
        _settings.Save();
        UpdatePreferenceText();
        UpdateHotkeyText();
        return taken;
    }

    /// <summary>Changes both hotkeys (released together first so swapping them can't clash). Returns any taken by another app.</summary>
    private List<string> SetHotkeys(Keys lockKeys, Keys hideKeys)
    {
        if (lockKeys != Keys.None && lockKeys == hideKeys) hideKeys = Keys.None;
        _lockHotkey.Suspend();
        _hideHotkey.Suspend();
        var taken = new List<string>();
        foreach (var (hotkey, keys) in new[] { (_lockHotkey, lockKeys), (_hideHotkey, hideKeys) })
        {
            if (hotkey.TryChange(keys))
            {
                if (hotkey == _lockHotkey) _settings.ToggleHotkey = keys; else _settings.HideHotkey = keys;
            }
            else
            {
                taken.Add($"{HotkeyBox.Describe(keys)} ({hotkey.Name})");
                hotkey.Resume();
            }
            hotkey.Box.Hotkey = hotkey.Keys;
        }
        return taken;
    }

    private void ExportSettings()
    {
        using var dialog = new SaveFileDialog
        {
            Title = "Export settings and presets",
            Filter = "Window Overlay settings (*.json)|*.json|All files (*.*)|*.*",
            FileName = "Window Overlay settings.json",
            OverwritePrompt = true,
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            _settings.ExportTo(dialog.FileName);
            int count = _settings.Presets.Count;
            SetStatus($"Exported your settings and {count} preset{(count == 1 ? "" : "s")} to {Path.GetFileName(dialog.FileName)}.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SetStatus($"Couldn't export: {ex.Message}", StatusKind.Warning);
        }
    }

    private void ImportSettings()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Import settings and presets",
            Filter = "Window Overlay settings (*.json)|*.json|All files (*.*)|*.*",
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        AppSettings imported;
        try
        {
            imported = AppSettings.ImportFrom(dialog.FileName);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or System.Text.Json.JsonException)
        {
            SetStatus($"Couldn't import {Path.GetFileName(dialog.FileName)}: {ex.Message}", StatusKind.Warning);
            return;
        }

        int presets = imported.Presets.Count;
        int replaced = imported.Presets.Count(p => FindPreset(p.Name) != null);
        using var confirm = new PromptDialog("Import settings",
            $"Use the settings in {Path.GetFileName(dialog.FileName)} and add its {presets} preset{(presets == 1 ? "" : "s")}"
            + (replaced > 0 ? $" ({replaced} with the same name as yours will be replaced)?" : "?"),
            "Import");
        if (confirm.ShowDialog(this) != DialogResult.OK) return;

        foreach (var preset in imported.Presets.Where(p => !string.IsNullOrWhiteSpace(p.Name)))
        {
            int existing = _settings.Presets.FindIndex(p => string.Equals(p.Name, preset.Name, StringComparison.OrdinalIgnoreCase));
            if (existing >= 0) _settings.Presets[existing] = preset;
            else _settings.Presets.Add(preset);
        }
        var taken = ApplyPreferencesFrom(imported);
        RefreshPresetList();
        SetStatus(taken.Count == 0
            ? $"Imported settings and {presets} preset{(presets == 1 ? "" : "s")}."
            : $"Imported settings and presets, except {string.Join(" and ", taken)}: another app is using it.",
            taken.Count == 0 ? StatusKind.Idle : StatusKind.Warning);
    }

    private static int ReconnectIndex(int seconds)
    {
        int best = 0;
        for (int i = 1; i < ReconnectChoices.Length; i++)
            if (Math.Abs(ReconnectChoices[i] - seconds) < Math.Abs(ReconnectChoices[best] - seconds)) best = i;
        return best;
    }

    private void SavePreference(Action<AppSettings> change, bool applyTheme = false)
    {
        if (_loadingSettings) return;
        change(_settings);
        _settings.Save();
        UpdatePreferenceText();
        if (applyTheme) Theme.Apply(_settings.ThemeMode, _settings.AccentColor());
    }

    /// <summary>Opens the colour picker for a custom accent. The app previews the colour live; Cancel puts it back.</summary>
    private void PickCustomAccent()
    {
        var start = _settings.Accent == Theme.CustomAccentName ? _settings.AccentColor()
            : _accentPicker.CustomColor ?? Theme.Accent;
        using var dialog = new ColorPickerDialog("Custom accent colour", start);
        dialog.ColorChanged += (_, _) => Theme.Apply(_settings.ThemeMode, dialog.Color);
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _settings.CustomAccent = Theme.ToHex(dialog.Color);
            _accentPicker.CustomColor = dialog.Color;
            if (_accentPicker.SelectedName == Theme.CustomAccentName)
                SavePreference(s => s.Accent = Theme.CustomAccentName, applyTheme: true); // already selected: save the new colour
            else
                _accentPicker.SelectedName = Theme.CustomAccentName; // selects it, which saves and applies
            SetStatus($"Accent colour set to {Theme.ToHex(dialog.Color)}.");
        }
        else
        {
            Theme.Apply(_settings.ThemeMode, _settings.AccentColor()); // undo the preview
        }
    }

    private void UpdatePreferenceText()
    {
        _snapDistanceHeader.Value = $"{_snapDistanceSlider.Value} px";
        _defaultOpacityHeader.Value = $"{_defaultOpacitySlider.Value}%";
        _startupSwitch.Description = _traySwitch.Checked
            ? "Opens in the notification area when you sign in"
            : "Opens minimised when you sign in";
        _startupSwitch.Invalidate();
    }

    private void ShowSettingsPage(bool show)
    {
        if (show)
        {
            // Someone may have changed the startup entry elsewhere (e.g. Task Manager's Startup tab).
            _loadingSettings = true;
            _startupSwitch.Checked = StartWithWindows.IsEnabled;
            _loadingSettings = false;
        }
        _settingsPage.Visible = show;
        _mainArea.Visible = !show;
        _settingsButton.Visible = !show;
        if (show) _backButton.Focus(); else _windowList.Focus();
    }

    /// <summary>Puts every setting on the settings page back to its default (presets are kept).</summary>
    private void ResetPreferences()
    {
        using var dialog = new PromptDialog("Reset settings",
            "Put every setting on the Settings page back to its default? Your presets are kept.", "Reset", danger: true);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        _startupSwitch.Checked = false; // removes the startup entry
        var taken = ApplyPreferencesFrom(new AppSettings());
        SetStatus(taken.Count == 0
            ? "Settings are back to their defaults."
            : $"Settings are back to their defaults, except {string.Join(" and ", taken)}: another app is using it.",
            taken.Count == 0 ? StatusKind.Idle : StatusKind.Warning);
    }

    private void OnThemeChanged(object? sender, EventArgs e)
    {
        UpdateSettingsUi(); // dimmed label colours
        foreach (var item in _overlays)
        {
            item.Form.Invalidate(); // edit-mode outline
            if (item.Form.OverlayMenu != null) ThemedMenuRenderer.Apply(item.Form.OverlayMenu);
        }
        if (_tray.ContextMenuStrip != null) ThemedMenuRenderer.Apply(_tray.ContextMenuStrip);
    }

    private void OnWindowsPreferenceChanged(object? sender, Microsoft.Win32.UserPreferenceChangedEventArgs e)
    {
        // Windows' light/dark app mode changed: follow it if "Match Windows" is chosen.
        if (_settings.ThemeMode != ThemeMode.System || !IsHandleCreated) return;
        BeginInvoke(() => Theme.Apply(_settings.ThemeMode, _settings.AccentColor()));
    }

    // ---------------------------------------------------------------- notification area

    private void SetUpTray()
    {
        _tray.Icon = Icon ?? SystemIcons.Application;
        _tray.Visible = _settings.KeepRunningInTray;

        var menu = new ContextMenuStrip();
        var open = new ToolStripMenuItem("Open Window Overlay", null, (_, _) => ShowFromTray());
        open.Font = new Font(menu.Font, FontStyle.Bold);
        var lockItem = new ToolStripMenuItem("Lock all overlays (overlay mode)", null, (_, _) => _clickThroughSwitch.Checked = !_clickThroughSwitch.Checked);
        var hideItem = new ToolStripMenuItem("Hide all overlays", null, (_, _) => ToggleHideAll());
        var settingsItem = new ToolStripMenuItem("Settings", null, (_, _) => { ShowFromTray(); ShowSettingsPage(true); });
        var exit = new ToolStripMenuItem("Exit", null, (_, _) => ExitApp());
        menu.Items.AddRange(new ToolStripItem[] { open, new ToolStripSeparator(), lockItem, hideItem, settingsItem, new ToolStripSeparator(), exit });
        menu.Opening += (_, _) =>
        {
            lockItem.Checked = _clickThroughSwitch.Checked;
            lockItem.ShortcutKeyDisplayString = _lockHotkey.Keys == Keys.None ? null : _lockHotkey.Label;
            bool allHidden = _overlays.Count > 0 && _overlays.All(o => o.Hidden);
            hideItem.Text = allHidden ? "Show all overlays" : "Hide all overlays";
            hideItem.ShortcutKeyDisplayString = _hideHotkey.Keys == Keys.None ? null : _hideHotkey.Label;
            hideItem.Enabled = _overlays.Count > 0;
        };
        ThemedMenuRenderer.Apply(menu);
        _tray.ContextMenuStrip = menu;
        _tray.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) ShowFromTray(); };
    }

    private void ShowFromTray()
    {
        Show();
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        Activate();
    }

    private void ExitApp()
    {
        _exiting = true;
        Close();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // With "keep running" on, the close button only hides the window; overlays and hotkeys carry on.
        if (_settings.KeepRunningInTray && e.CloseReason == CloseReason.UserClosing && !_exiting)
        {
            e.Cancel = true;
            Hide();
            if (!_trayTipShown)
            {
                _trayTipShown = true;
                _tray.ShowBalloonTip(4000, "Window Overlay is still running",
                    "Your overlays and hotkeys keep working. Click the icon here to open the app, or right-click it to exit.", ToolTipIcon.None);
            }
            return;
        }
        base.OnFormClosing(e);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        // Esc leaves the settings page (unless a hotkey box is recording, where Esc cancels).
        if (keyData == Keys.Escape && _settingsPage.Visible && ActiveControl is not HotkeyBox)
        {
            ShowSettingsPage(false);
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    // ---------------------------------------------------------------- form lifecycle

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.ApplyTitleBar(Handle);
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
        var taken = new[] { _lockHotkey, _hideHotkey }.Where(h => !h.Resume()).ToList();
        if (taken.Count > 0)
            SetStatus(string.Join(" ", taken.Select(h => $"{h.Label} (the {h.Name} hotkey) is already used by another app."))
                + " Pick another hotkey.", StatusKind.Warning);
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
        if (_startHidden) BeginInvoke(Hide); // started at sign-in: only the notification area icon
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
        _lockHotkey.Suspend();
        _hideHotkey.Suspend();
        _reconnectTimer.Stop();
        Theme.Changed -= OnThemeChanged;
        Microsoft.Win32.SystemEvents.UserPreferenceChanged -= OnWindowsPreferenceChanged;
        _tray.Visible = false;
        _tray.Dispose();
        StopAll();
        base.OnFormClosed(e);
    }

    protected override void WndProc(ref Message m)
    {
        if (_showMessage != 0 && m.Msg == _showMessage)
        {
            ShowFromTray(); // the app was started again
            return;
        }
        if (m.Msg == WM_HOTKEY)
        {
            switch (m.WParam.ToInt32())
            {
                case LockHotkeyId:
                    _clickThroughSwitch.Checked = !_clickThroughSwitch.Checked;
                    return;
                case HideHotkeyId:
                    ToggleHideAll();
                    return;
            }
        }
        base.WndProc(ref m);
    }

    // ---------------------------------------------------------------- window list

    private void OnSearchKeyDown(object? sender, KeyEventArgs e)
    {
        // Arrow keys / Enter in the search box drive the list on the current tab.
        ListBox list = PresetsTabShown ? _presetList : _windowList;
        if (e.KeyCode is Keys.Down or Keys.Up && list.Items.Count > 0)
        {
            int delta = e.KeyCode == Keys.Down ? 1 : -1;
            list.SelectedIndex = Math.Clamp(list.SelectedIndex + delta, 0, list.Items.Count - 1);
            e.Handled = e.SuppressKeyPress = true;
        }
        else if (e.KeyCode == Keys.Enter)
        {
            if (list.SelectedIndex < 0 && list.Items.Count > 0) list.SelectedIndex = 0;
            if (!PresetsTabShown) AddOverlay();
            else if (_presetList.SelectedItem is OverlayPreset preset) ApplyPreset(preset);
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

        CreateOverlay(w);
        if (IsIconic(w.Handle))
            SetStatus($"{w.Title} is minimised. Restore it from the taskbar to see it in the overlay.", StatusKind.Warning);
    }

    /// <summary>Creates, places and lists a new overlay of a window.</summary>
    private OverlayItem CreateOverlay(WindowInfo w)
    {
        var form = new OverlayForm
        {
            ClickThrough = false, // new overlays start unlocked so they can be placed
            SnapToEdges = _settings.SnapToEdges,
            SnapDistanceLogical = _settings.SnapDistance,
            ClientAreaOnly = true,
            OverlayOpacity = _settings.DefaultOpacity / 100.0,
            FadeOnHover = _settings.DefaultFadeOnHover,
        };
        form.OtherOverlays = () => _overlays
            .Where(o => o.Form != form && !o.Form.IsDisposed && o.Form.Visible)
            .Select(o => o.Form.Bounds);
        var item = new OverlayItem(_nextOverlayNumber++, w, form);
        form.OverlayMenu = BuildOverlayMenu(item);

        form.SourceLost += (_, _) => StartWaitingForSource(item);
        form.OpacityChangedOnOverlay += (_, _) =>
        {
            if (item == SelectedOverlay) OnOverlaySelectionChanged(); // show the wheel's change on the slider
            _overlayList.Invalidate();
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
        SyncLockAll();
        UpdateOverlayUi();
        return item;
    }

    /// <summary>Right-click menu of an unlocked overlay.</summary>
    private ContextMenuStrip BuildOverlayMenu(OverlayItem item)
    {
        var form = item.Form;
        var menu = new ContextMenuStrip();
        void Select() => _overlayList.SelectedItem = item;

        var lockItem = new ToolStripMenuItem("Lock (clicks pass through)", null, (_, _) => SetLocked(item, !form.ClickThrough));
        var snap = new ToolStripMenuItem("Snap to edges", null, (_, _) =>
        {
            form.SnapToEdges = !form.SnapToEdges;
            if (item == SelectedOverlay) OnOverlaySelectionChanged();
        });
        var fit = new ToolStripMenuItem("Fit to source's shape", null, (_, _) => form.FitToSource());
        var crop = new ToolStripMenuItem("Select area…", null, (_, _) => { Select(); OpenCropPicker(); });
        var resetCrop = new ToolStripMenuItem("Remove crop", null, (_, _) =>
        {
            form.Crop = default;
            if (item == SelectedOverlay) ShowCropOnSliders(form.Crop);
            _overlayList.Invalidate();
        });
        var opacity = new ToolStripMenuItem("Opacity");
        foreach (int percent in new[] { 100, 90, 75, 50, 25 })
        {
            var choice = new ToolStripMenuItem($"{percent}%", null, (_, _) =>
            {
                form.OverlayOpacity = percent / 100.0;
                if (item == SelectedOverlay) OnOverlaySelectionChanged();
                _overlayList.Invalidate();
            }) { Tag = percent };
            opacity.DropDownItems.Add(choice);
        }
        var fade = new ToolStripMenuItem("Fade when the mouse is over it", null, (_, _) =>
        {
            form.FadeOnHover = !form.FadeOnHover;
            if (item == SelectedOverlay) OnOverlaySelectionChanged();
            _overlayList.Invalidate();
        });
        var clientOnly = new ToolStripMenuItem("Client area only", null, (_, _) =>
        {
            form.ClientAreaOnly = !form.ClientAreaOnly;
            if (item == SelectedOverlay) OnOverlaySelectionChanged();
        });
        var preset = new ToolStripMenuItem("Save as preset…", null, (_, _) => { Select(); SavePreset(); });
        var hide = new ToolStripMenuItem("Hide", null, (_, _) => { item.Hidden = !item.Hidden; UpdateOverlayUi(); });
        var close = new ToolStripMenuItem("Close overlay", null, (_, _) => RemoveOverlay(item));

        menu.Items.AddRange(new ToolStripItem[]
        {
            lockItem, new ToolStripSeparator(), fit, crop, resetCrop, opacity, fade, snap, clientOnly,
            new ToolStripSeparator(), preset, hide, close,
        });
        menu.Opening += (_, _) =>
        {
            Select();
            var c = form.Crop;
            resetCrop.Enabled = c.Left > 0 || c.Top > 0 || c.Right > 0 || c.Bottom > 0;
            int current = (int)Math.Round(form.OverlayOpacity * 100);
            foreach (ToolStripMenuItem choice in opacity.DropDownItems) choice.Checked = (int)choice.Tag! == current;
            fade.Checked = form.FadeOnHover;
            clientOnly.Checked = form.ClientAreaOnly;
            lockItem.Text = form.ClickThrough ? "Unlock (to move or resize it)" : "Lock (clicks pass through)";
            snap.Checked = form.SnapToEdges;
            hide.Text = item.Hidden ? "Show" : "Hide";
        };
        ThemedMenuRenderer.Apply(menu);
        return menu;
    }

    /// <summary>Locks (click-through) or unlocks one overlay.</summary>
    private void SetLocked(OverlayItem item, bool locked)
    {
        item.Form.ClickThrough = locked;
        if (item == SelectedOverlay)
        {
            _loadingSettings = true;
            _lockSwitch.Checked = locked;
            _loadingSettings = false;
        }
        SyncLockAll();
        UpdateOverlayUi();
    }

    /// <summary>Shows "lock all" as on exactly when every overlay is locked (without re-applying it).</summary>
    private void SyncLockAll()
    {
        _syncingLockAll = true;
        _clickThroughSwitch.Checked = _overlays.Count > 0 && _overlays.All(o => o.Form.ClickThrough);
        _syncingLockAll = false;
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
        item.Form.OverlayMenu?.Dispose();
        item.Dispose();
        SyncLockAll();
        UpdateOverlayUi();
    }

    // ---------------------------------------------------------------- reconnecting to reopened windows

    /// <summary>
    /// The overlay's window closed. Instead of closing the overlay, keep it (position, crop, style) for a
    /// while and reconnect if the same app opens a new window, e.g. a browser or player restarting.
    /// </summary>
    private void StartWaitingForSource(OverlayItem item)
    {
        if (item.Removed) return;
        if (OverlayItem.ReconnectWindow <= TimeSpan.Zero)
        {
            RemoveOverlay(item);
            SetStatus($"Overlay {item.Number} closed because its window ({item.Title}) closed.");
            return;
        }
        // Only windows opened from now on count, so the overlay never jumps to one that was already open.
        item.StartWaiting(GetCapturableWindows(includeIcons: false).Select(w => w.Handle));
        item.Form.PlaceholderText = $"Waiting for {item.ProcessName} to reopen…\n(drag to move • drag edges to resize)";
        _reconnectTimer.Start();
        UpdateOverlayUi();
        SetStatus($"Overlay {item.Number}'s window ({item.Title}) closed. Waiting up to "
            + $"{(int)OverlayItem.ReconnectWindow.TotalSeconds} s for {item.ProcessName} to open it again.", StatusKind.Warning);
    }

    private void CheckWaitingOverlays()
    {
        var waiting = _overlays.Where(o => o.WaitingSince != null).ToList();
        if (waiting.Count == 0)
        {
            _reconnectTimer.Stop();
            return;
        }

        var windows = GetCapturableWindows(includeIcons: false);
        foreach (var item in waiting)
        {
            // A new window from the same app (and of the same kind, when known), preferring the same title.
            var candidates = windows.Where(w => !item.WindowsOpenAtLoss.Contains(w.Handle)
                && string.Equals(w.ProcessName, item.ProcessName, StringComparison.OrdinalIgnoreCase)
                && (item.ClassName.Length == 0 || w.ClassName == item.ClassName)).ToList();
            var match = candidates.FirstOrDefault(w => w.Title == item.Title) ?? candidates.FirstOrDefault();

            if (match != null)
            {
                string oldTitle = item.Title;
                item.SetSource(match with { Icon = GetWindowIcon(match.Handle) });
                item.Form.PlaceholderText = OverlayForm.DefaultPlaceholder;
                item.Form.Source = match.Handle;
                SetStatus($"Overlay {item.Number} reconnected to {item.ProcessName}"
                    + (match.Title == oldTitle ? "." : $" (\"{match.Title}\")."), StatusKind.Live);
                RefreshWindowList();
            }
            else if (item.WaitRemaining <= TimeSpan.Zero)
            {
                RemoveOverlay(item);
                SetStatus($"Overlay {item.Number} closed: {item.ProcessName} didn't reopen its window within "
                    + $"{(int)OverlayItem.ReconnectWindow.TotalSeconds} s.", StatusKind.Warning);
            }
        }
        UpdateOverlayUi(updateStatus: false);
    }

    private void StopAll()
    {
        foreach (var item in _overlays.ToList()) RemoveOverlay(item);
        _clickThroughSwitch.Checked = false;
    }

    /// <summary>The "lock all" switch (or its hotkey): lock every overlay, or unlock them all.</summary>
    private void ApplyClickThrough()
    {
        if (_syncingLockAll) return;
        foreach (var item in _overlays) item.Form.ClickThrough = _clickThroughSwitch.Checked;
        if (SelectedOverlay is OverlayItem selected)
        {
            _loadingSettings = true;
            _lockSwitch.Checked = selected.Form.ClickThrough;
            _loadingSettings = false;
        }
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
            _fadeSwitch.Checked = form.FadeOnHover;
            _snapSwitch.Checked = form.SnapToEdges;
            _lockSwitch.Checked = form.ClickThrough;
            _loadingSettings = false;
            ShowCropOnSliders(form.Crop);
        }
        UpdateSettingsUi();
    }

    /// <summary>Applies a setting to the selected overlay (unless the controls are just being loaded).</summary>
    private void ChangeSelected(Action<OverlayForm> change)
    {
        if (_loadingSettings || SelectedOverlay is not OverlayItem item) return;
        change(item.Form);
        _overlayList.Invalidate();
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

    /// <summary>Hides every overlay, or shows them all again if they're all hidden already.</summary>
    private void ToggleHideAll()
    {
        if (_overlays.Count == 0) return;
        bool hide = _overlays.Any(o => !o.Hidden);
        foreach (var item in _overlays) item.Hidden = hide;
        UpdateOverlayUi();
    }

    /// <param name="updateStatus">False to leave the status line alone (e.g. a once-a-second refresh).</param>
    private void UpdateOverlayUi(bool updateStatus = true)
    {
        int count = _overlays.Count;
        int hidden = _overlays.Count(o => o.Hidden);
        bool allHidden = count > 0 && hidden == count;
        _stopAllButton.Enabled = count > 0;
        _hideAllButton.Enabled = count > 0;
        _hideAllButton.Text = allHidden ? "Show all" : "Hide all";
        _hideAllButton.GlyphSlashed = !allHidden; // slashed eye = "Hide all"; plain eye = "Show all"
        _overlayEmpty.Visible = count == 0;
        _overlayList.Visible = count > 0;
        _overlayList.Invalidate(); // eye buttons and "hidden" labels

        var liveCounts = new Dictionary<IntPtr, int>();
        foreach (var item in _overlays.Where(o => o.WaitingSince == null))
            liveCounts[item.Source] = liveCounts.GetValueOrDefault(item.Source) + 1;
        _windowList.LiveCounts = liveCounts;

        UpdateSettingsUi();

        if (!updateStatus) return;
        if (count == 0)
            SetStatus("No overlays running.");
        else if (allHidden)
            SetStatus((count == 1 ? "The overlay is hidden" : $"All {count} overlays are hidden") + " · bring them back with Show all"
                + (_hideHotkey.Keys == Keys.None ? "." : $" or {_hideHotkey.Label}."));
        else
            SetStatus($"{count} overlay{(count == 1 ? "" : "s")} running"
                + (hidden > 0 ? $" · {hidden} hidden" : "")
                + LockSummary(),
                StatusKind.Live);
    }

    private string LockSummary()
    {
        int locked = _overlays.Count(o => o.Form.ClickThrough);
        if (locked == _overlays.Count) return " · locked (click-through)";
        if (locked == 0) return " · unlocked: drag to move, drag edges to resize, right-click for options";
        return $" · {locked} locked, {_overlays.Count - locked} unlocked";
    }

    private void UpdateSettingsUi()
    {
        var selected = SelectedOverlay;
        foreach (var control in _settingsControls) control.Enabled = selected != null;
        _savePresetButton.Enabled = selected != null;
        _settingsSubtitle.Text = selected == null
            ? "Select an overlay to change its settings."
            : $"Overlay {selected.Number} · {selected.Title}";
        // Labels are dimmed by colour rather than Enabled=false, which draws embossed grey text.
        foreach (var (label, normal) in _settingsLabels)
            label.ForeColor = selected != null ? normal() : Theme.Track;
    }

    // ---------------------------------------------------------------- presets

    private bool PresetsTabShown => _sourceTabs.SelectedIndex == 1;

    private void ShowSourceTab(int index)
    {
        bool presets = index == 1;
        _windowList.Visible = !presets;
        _refreshButton.Visible = !presets;
        _windowsHint.Visible = !presets;
        _presetsBar.Visible = presets;
        _searchBox.PlaceholderText = presets ? "Search presets" : "Search windows";
        if (presets) RefreshPresetList(); else _presetList.Visible = _presetEmpty.Visible = false;
        _refreshButton.Parent?.PerformLayout();
    }

    private void RefreshPresetList(string? select = null)
    {
        select ??= (_presetList.SelectedItem as OverlayPreset)?.Name;
        _sourceTabs.SetTabText(1, _settings.Presets.Count == 0 ? "Presets" : $"Presets ({_settings.Presets.Count})");
        ApplyPresetFilter(select);
    }

    private void ApplyPresetFilter() => ApplyPresetFilter((_presetList.SelectedItem as OverlayPreset)?.Name);

    private void ApplyPresetFilter(string? select)
    {
        string filter = _searchBox.Text.Trim();
        _presetList.BeginUpdate();
        _presetList.Items.Clear();
        foreach (var preset in _settings.Presets)
        {
            if (filter.Length > 0 && !preset.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)) continue;
            int index = _presetList.Items.Add(preset);
            if (preset.Name == select) _presetList.SelectedIndex = index;
        }
        _presetList.EndUpdate();

        bool none = _settings.Presets.Count == 0;
        _presetList.Visible = PresetsTabShown && !none;
        _presetEmpty.Visible = PresetsTabShown && none;
    }

    private OverlayPreset? FindPreset(string name) =>
        _settings.Presets.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Saves the selected overlay's placement, size, crop and opacity as a named preset.</summary>
    private void SavePreset()
    {
        if (SelectedOverlay is not OverlayItem item)
        {
            SetStatus("Select an overlay to save as a preset.", StatusKind.Warning);
            return;
        }

        int n = _settings.Presets.Count + 1;
        while (FindPreset($"Preset {n}") != null) n++;
        using var dialog = new PromptDialog("Save preset",
            $"Save Overlay {item.Number}'s position, size and crop as a preset named:",
            "Save", input: $"Preset {n}",
            validate: name => name.Length == 0 ? ("Enter a name.", null, null)
                : FindPreset(name) != null ? (null, "A preset with this name already exists and will be replaced.", "Replace")
                : (null, null, null));
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        var preset = OverlayPreset.Capture(dialog.Value, item.Form);
        int existing = _settings.Presets.FindIndex(p => string.Equals(p.Name, preset.Name, StringComparison.OrdinalIgnoreCase));
        if (existing >= 0) _settings.Presets[existing] = preset;
        else _settings.Presets.Add(preset);
        _settings.Save();
        RefreshPresetList(preset.Name);
        SetStatus($"{(existing >= 0 ? "Replaced" : "Saved")} preset \"{preset.Name}\" ({preset.Summary}).");
    }

    /// <summary>Applies a preset's placement, size, crop and opacity to the selected overlay (its window stays the same).</summary>
    private void ApplyPreset(OverlayPreset preset)
    {
        if (SelectedOverlay is not OverlayItem item)
        {
            SetStatus($"Select an overlay (in the Overlays list) to apply \"{preset.Name}\" to.", StatusKind.Warning);
            return;
        }

        var form = item.Form;
        preset.ApplyLook(form);
        var target = preset.PlacementNow(form.Bounds);
        form.Bounds = target;
        // Moving onto a monitor with different scaling makes Windows rescale the window; set it again there.
        if (form.Bounds != target) form.Bounds = target;

        OnOverlaySelectionChanged(); // show the new values on the settings controls
        _overlayList.Invalidate();
        SetStatus($"Applied \"{preset.Name}\" to Overlay {item.Number}.");
    }

    private void RenamePreset(OverlayPreset preset)
    {
        using var dialog = new PromptDialog("Rename preset", $"New name for \"{preset.Name}\":", "Rename", input: preset.Name,
            validate: name => name.Length == 0 ? ("Enter a name.", null, null)
                : FindPreset(name) is { } other && other != preset ? ("Another preset already has this name.", null, null)
                : (null, null, null));
        if (dialog.ShowDialog(this) != DialogResult.OK || dialog.Value == preset.Name) return;

        string old = preset.Name;
        preset.Name = dialog.Value;
        _settings.Save();
        RefreshPresetList(preset.Name);
        SetStatus($"Renamed \"{old}\" to \"{preset.Name}\".");
    }

    private void DeletePreset(OverlayPreset preset)
    {
        using var dialog = new PromptDialog("Delete preset", $"Delete the preset \"{preset.Name}\"? This can't be undone.", "Delete",
            danger: true);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        _settings.Presets.Remove(preset);
        _settings.Save();
        RefreshPresetList();
        SetStatus($"Deleted preset \"{preset.Name}\".");
    }

    // ---------------------------------------------------------------- hotkey

    private void OnHotkeyRecorded(GlobalHotkey hotkey)
    {
        var keys = hotkey.Box.Hotkey;
        var other = hotkey == _lockHotkey ? _hideHotkey : _lockHotkey;
        string? problem =
            keys != Keys.None && keys == other.Keys ? $"{HotkeyBox.Describe(keys)} is already the {other.Name} hotkey."
            : !hotkey.TryChange(keys) ? $"{HotkeyBox.Describe(keys)} is already used by another app."
            : null;
        if (problem != null)
        {
            SetStatus($"{problem} Kept {hotkey.Label}.", StatusKind.Warning);
            hotkey.Box.Hotkey = hotkey.Keys;
            hotkey.Resume();
            return;
        }

        if (hotkey == _lockHotkey) _settings.ToggleHotkey = keys;
        else _settings.HideHotkey = keys;
        _settings.Save();
        UpdateHotkeyText();
        SetStatus(keys == Keys.None
            ? $"The {hotkey.Name} hotkey is cleared."
            : $"The {hotkey.Name} hotkey is now {hotkey.Label}.");
    }

    private void UpdateHotkeyText()
    {
        _clickThroughSwitch.Description = _lockHotkey.Keys == Keys.None
            ? "Lock all overlays so clicks pass through"
            : $"Lock all overlays, click-through · {_lockHotkey.Label}";
        _clickThroughSwitch.Invalidate();
        _toolTip.SetToolTip(_hideAllButton, _hideHotkey.Keys == Keys.None
            ? "Hide every overlay for now, or bring them back"
            : $"Hide every overlay for now, or bring them back ({_hideHotkey.Label})");
    }

    private void SetStatus(string text, StatusKind kind = StatusKind.Idle) => _status.Set(text, kind);
}
