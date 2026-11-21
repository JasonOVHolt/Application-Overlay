namespace OverlayApp;

/// <summary>
/// Small dark dialog with a message, an optional text box and OK/Cancel. Used to name, rename and
/// delete presets. Always on top, since overlays are and could otherwise cover it.
/// </summary>
internal sealed class PromptDialog : Form
{
    private readonly TextBox? _input;
    private readonly Label _note;
    private readonly ModernButton _ok;
    private readonly Func<string, (string? Error, string? Note, string? OkText)>? _validate;
    private readonly string _okText;

    /// <param name="input">Initial text for a text box, or null for a plain confirmation.</param>
    /// <param name="validate">
    /// Checks the text as it's typed: an Error disables OK; a Note is shown as a hint; OkText relabels the
    /// OK button (e.g. "Replace" when the name is already taken).
    /// </param>
    public PromptDialog(string title, string message, string okText, string? input = null,
        Func<string, (string? Error, string? Note, string? OkText)>? validate = null, bool danger = false)
    {
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        Text = title;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        TopMost = true;
        BackColor = Theme.Surface;
        ForeColor = Theme.Text;
        Font = Theme.Body;
        KeyPreview = true;
        ClientSize = new Size(420, input != null ? 168 : 124);
        _okText = okText;
        _validate = validate;

        var messageLabel = new Label
        {
            Text = message,
            AutoSize = false,
            Location = new Point(18, 16),
            Size = new Size(384, 40),
            ForeColor = Theme.Text,
            BackColor = Theme.Surface,
        };
        Controls.Add(messageLabel);

        int y = 60;
        if (input != null)
        {
            _input = new TextBox { Text = input };
            var host = new TextHost(_input) { Location = new Point(18, y), Size = new Size(384, 34) };
            _input.TextChanged += (_, _) => Revalidate();
            Controls.Add(host);
            y += 40;
        }

        _note = new Label
        {
            AutoSize = false,
            Location = new Point(18, y),
            Size = new Size(384, 20),
            Font = Theme.Small,
            ForeColor = Theme.TextMuted,
            BackColor = Theme.Surface,
        };
        Controls.Add(_note);

        _ok = new ModernButton(okText, danger ? ButtonKind.Danger : ButtonKind.Primary) { Size = new Size(100, 34) };
        var cancel = new ModernButton("Cancel", ButtonKind.Secondary) { Size = new Size(90, 34) };
        _ok.Location = new Point(ClientSize.Width - 18 - _ok.Width, ClientSize.Height - 16 - _ok.Height);
        cancel.Location = new Point(_ok.Left - 8 - cancel.Width, _ok.Top);
        _ok.Click += (_, _) => Accept();
        cancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        Controls.Add(_ok);
        Controls.Add(cancel);
        Revalidate();
    }

    /// <summary>The (trimmed) text from the text box.</summary>
    public string Value => _input?.Text.Trim() ?? "";

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.ApplyTitleBar(Handle);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        if (_input != null)
        {
            _input.Focus();
            _input.SelectAll();
        }
        else
        {
            _ok.Focus();
        }
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Enter) { Accept(); return true; }
        if (keyData == Keys.Escape) { DialogResult = DialogResult.Cancel; Close(); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void Revalidate()
    {
        var (error, note, okText) = _validate?.Invoke(Value) ?? (null, null, null);
        _ok.Enabled = error == null;
        _ok.Text = okText ?? _okText;
        _note.Text = error ?? note ?? "";
        _note.ForeColor = error != null ? Theme.Danger : note != null ? Theme.Warning : Theme.TextMuted;
    }

    private void Accept()
    {
        if (!_ok.Enabled) return;
        DialogResult = DialogResult.OK;
        Close();
    }
}
