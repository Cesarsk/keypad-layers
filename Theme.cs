namespace KeypadLayers;

internal enum ThemeRole { Surface, Muted, Primary, Field }

internal static class Theme
{
    public static ThemeMode Mode { get; private set; } = ThemeMode.Dark;
    public static Color Background => Mode == ThemeMode.Dark ? Color.FromArgb(15, 17, 19) : Color.White;
    public static Color Card => Mode == ThemeMode.Dark ? Color.FromArgb(22, 24, 26) : Color.FromArgb(250, 250, 250);
    public static Color Field => Mode == ThemeMode.Dark ? Color.FromArgb(30, 32, 34) : Color.FromArgb(244, 244, 244);
    public static Color Text => Mode == ThemeMode.Dark ? Color.FromArgb(242, 242, 242) : Color.Black;
    public static Color Muted => Mode == ThemeMode.Dark ? Color.FromArgb(174, 175, 177) : Color.FromArgb(88, 88, 88);
    public static Color Border => Mode == ThemeMode.Dark ? Color.FromArgb(60, 62, 64) : Color.FromArgb(210, 210, 210);
    public static Color Accent => Text;
    public static void Set(ThemeMode mode) => Mode = mode;
    public static Button Button(string text, bool accent = false)
    {
        var button = new ThemedButton { Text = text, Tag = accent ? ThemeRole.Primary : null, AutoSize = false, Height = 36, Width = 130, FlatStyle = FlatStyle.Flat, BackColor = accent ? Text : Field, ForeColor = accent ? Background : Text, Cursor = Cursors.Hand, Margin = new Padding(0, 0, 8, 8) };
        button.FlatAppearance.BorderSize = 1; button.FlatAppearance.BorderColor = accent ? Text : Border;
        return button;
    }
    public static Label Label(string text, float size = 10, bool muted = false) => new() { Text = text, Tag = muted ? ThemeRole.Muted : null, AutoSize = true, Font = new Font("Segoe UI", size), ForeColor = muted ? Muted : Text, Margin = new Padding(0, 0, 0, 8), MaximumSize = new Size(1000, 0) };
    public static TextBox TextBox(bool multiline = false) => new() { BorderStyle = BorderStyle.FixedSingle, BackColor = Field, ForeColor = Text, Multiline = multiline, Width = 278, Height = multiline ? 105 : 28, Margin = new Padding(0, 0, 0, 12) };
    public static ComboBox Combo()
    {
        var combo = new ThemedCombo { DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat, BackColor = Field, ForeColor = Text, Width = 278, Margin = new Padding(0, 0, 0, 12), DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = 24 };
        combo.DrawItem += (_, e) =>
        {
            using var brush = new SolidBrush(Field);
            e.Graphics.FillRectangle(brush, e.Bounds);
            if (e.Index >= 0) TextRenderer.DrawText(e.Graphics, combo.GetItemText(combo.Items[e.Index]), combo.Font, Rectangle.Inflate(e.Bounds, -5, 0), Text, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        };
        return combo;
    }
    public static void Apply(Control root)
    {
        root.ForeColor = root.Tag is ThemeRole.Muted ? Muted : Text;
        root.BackColor = root.Tag is ThemeRole.Surface ? Card : root.Parent?.BackColor ?? Background;
        if (root is TextBoxBase or ComboBox) root.BackColor = Field;
        if (root is ListView) root.BackColor = Card;
        if (root is ListBox) root.BackColor = Background;
        if (root is CheckBox) root.ForeColor = Muted;
        if (root is Button button)
        {
            bool primary = root.Tag is ThemeRole.Primary;
            root.BackColor = primary ? Text : Field; root.ForeColor = primary ? Background : Text;
            button.FlatAppearance.BorderColor = primary ? Text : Border;
        }
        foreach (Control child in root.Controls) Apply(child);
        root.Invalidate(true);
    }
}

internal sealed class ThemedButton : Button
{
    protected override void OnPaint(PaintEventArgs e)
    {
        using var fill = new SolidBrush(Enabled ? BackColor : Theme.Field);
        e.Graphics.FillRectangle(fill, ClientRectangle);
        using var border = new Pen(Enabled ? FlatAppearance.BorderColor : Theme.Border);
        e.Graphics.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
        TextRenderer.DrawText(e.Graphics, Text, Font, Rectangle.Inflate(ClientRectangle, -7, -4), Enabled ? ForeColor : Theme.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis);
        if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(ClientRectangle, -4, -4), ForeColor, BackColor);
    }
}

internal sealed class ThemedCombo : ComboBox
{
    protected override void WndProc(ref Message message)
    {
        base.WndProc(ref message);
        // Native combo boxes omit the owner-drawn selection from WM_PRINT captures.
        if (message.Msg is 0x317 or 0x318 && message.WParam != 0)
        {
            using var graphics = Graphics.FromHdc(message.WParam);
            var bounds = new Rectangle(2, 2, Math.Max(0, Width - SystemInformation.VerticalScrollBarWidth - 4), Math.Max(0, Height - 4));
            using var fill = new SolidBrush(Theme.Field); graphics.FillRectangle(fill, bounds);
            TextRenderer.DrawText(graphics, GetItemText(SelectedItem), Font, Rectangle.Inflate(bounds, -4, 0), Theme.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
    }
}

internal static class AppIcon
{
    private static readonly Lazy<Icon> IconValue = new(() =>
    {
        using var stream = typeof(AppIcon).Assembly.GetManifestResourceStream("KeypadLayers.app.ico") ?? throw new InvalidOperationException("App icon is missing.");
        using var icon = new Icon(stream);
        return (Icon)icon.Clone();
    });
    public static Icon Value => IconValue.Value;
}
