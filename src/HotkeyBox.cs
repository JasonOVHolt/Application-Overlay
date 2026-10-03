namespace OverlayApp;

/// <summary>
/// Text box that records a key combination instead of text. Click it and press keys:
/// Esc cancels, Backspace/Delete clears the hotkey.
/// </summary>
internal sealed class HotkeyBox : TextBox
{
    private static readonly KeysConverter Converter = new();
    private Keys _hotkey;

    /// <summary>Raised when the user records a new combination (or clears it).</summary>
    public event EventHandler? HotkeyChanged;

    public HotkeyBox()
    {
        ReadOnly = true;
        ShortcutsEnabled = false;
        BackColor = SystemColors.Window;
        Cursor = Cursors.Hand;
        Width = 170;
    }

    public Keys Hotkey
    {
        get => _hotkey;
        set { _hotkey = value; Text = Describe(value); }
    }

    public static string Describe(Keys keys) =>
        keys == Keys.None ? "None" : Converter.ConvertToString(keys) ?? keys.ToString();

    // Let Tab, arrows, Enter, Esc etc. reach OnKeyDown instead of moving focus.
    protected override bool IsInputKey(Keys keyData) => true;

    protected override void OnEnter(EventArgs e)
    {
        base.OnEnter(e);
        Text = "Press a key combination…";
    }

    protected override void OnLeave(EventArgs e)
    {
        base.OnLeave(e);
        Text = Describe(_hotkey);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        e.Handled = true;
        e.SuppressKeyPress = true;

        Keys key = e.KeyCode;
        Keys mods = e.Modifiers;

        if (mods == Keys.None && key == Keys.Escape)
        {
            Finish();
            return;
        }
        if (mods == Keys.None && key is Keys.Back or Keys.Delete)
        {
            _hotkey = Keys.None;
            Finish(changed: true);
            return;
        }
        if (key is Keys.ControlKey or Keys.ShiftKey or Keys.Menu or Keys.LWin or Keys.RWin)
        {
            Text = Describe(mods) + "+…";
            return;
        }

        // Plain letters/numbers would hijack normal typing everywhere, so require Ctrl or Alt
        // unless it's a function key.
        bool isFunctionKey = key is >= Keys.F1 and <= Keys.F24;
        if ((mods & (Keys.Control | Keys.Alt)) == 0 && !isFunctionKey)
        {
            Text = "Add Ctrl or Alt (or use an F-key)";
            return;
        }

        _hotkey = key | mods;
        Finish(changed: true);
    }

    private void Finish(bool changed = false)
    {
        Text = Describe(_hotkey);
        if (changed) HotkeyChanged?.Invoke(this, EventArgs.Empty);
        // Move focus off the box so the next key press isn't captured again.
        Parent?.SelectNextControl(this, true, true, true, true);
    }
}
