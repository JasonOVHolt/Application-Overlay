using static OverlayApp.NativeMethods;

namespace OverlayApp;

/// <summary>
/// One system-wide hotkey owned by a window (delivered to it as WM_HOTKEY with <see cref="Id"/>),
/// plus the <see cref="HotkeyBox"/> used to change it.
/// </summary>
internal sealed class GlobalHotkey
{
    private readonly Control _owner;
    private bool _registered;

    public GlobalHotkey(Control owner, int id, string name, HotkeyBox box, Keys keys)
    {
        _owner = owner;
        Id = id;
        Name = name;
        Box = box;
        Keys = keys;
        box.Hotkey = keys;
    }

    public int Id { get; }
    public string Name { get; }
    public HotkeyBox Box { get; }

    /// <summary>The chosen combination (Keys.None for no hotkey), registered or not.</summary>
    public Keys Keys { get; private set; }

    public string Label => HotkeyBox.Describe(Keys);

    /// <summary>Switches to <paramref name="keys"/>. Returns false (and keeps the old one) if another app owns it.</summary>
    public bool TryChange(Keys keys)
    {
        Suspend();
        if (keys != Keys.None && !RegisterNative(keys)) return false;
        Keys = keys;
        return true;
    }

    /// <summary>Releases the hotkey, e.g. while its box records a new one so the current combination reaches the box.</summary>
    public void Suspend()
    {
        if (!_registered) return;
        UnregisterHotKey(_owner.Handle, Id);
        _registered = false;
    }

    /// <summary>Registers the chosen combination if it isn't already. Returns false if another app owns it.</summary>
    public bool Resume() => Keys == Keys.None || _registered || RegisterNative(Keys);

    private bool RegisterNative(Keys keys)
    {
        uint mods = MOD_NOREPEAT;
        if (keys.HasFlag(Keys.Control)) mods |= MOD_CONTROL;
        if (keys.HasFlag(Keys.Alt)) mods |= MOD_ALT;
        if (keys.HasFlag(Keys.Shift)) mods |= MOD_SHIFT;
        _registered = RegisterHotKey(_owner.Handle, Id, mods, (uint)(keys & Keys.KeyCode));
        return _registered;
    }
}
