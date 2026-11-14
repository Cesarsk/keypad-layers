namespace KeypadLayers;

internal sealed class PreferencesDialog : Form
{
    private readonly CheckBox windows = new() { Text = "Start with Windows", AutoSize = true, Margin = new Padding(0, 10, 0, 16) };
    private readonly CheckBox minimized = new() { Text = "Start minimized to tray", AutoSize = true, Margin = new Padding(0, 0, 0, 20) };
    private readonly ComboBox theme = Theme.Combo();
    private readonly Button save = Theme.Button("Save", true);
    internal PreferencesDialog(Settings settings, IStartupRegistration startup, Func<bool> saveSettings)
    {
        Text = "Settings"; Font = new Font("Segoe UI", 10); Icon = AppIcon.Value;
        ClientSize = new Size(400, 365); FormBorderStyle = FormBorderStyle.FixedDialog; StartPosition = FormStartPosition.CenterParent; MaximizeBox = MinimizeBox = false;
        var body = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(26) };
        body.Controls.Add(Theme.Label("Settings", 21));
        windows.Checked = startup.IsEnabled; minimized.Checked = settings.StartMinimized;
        body.Controls.Add(windows); body.Controls.Add(minimized); body.Controls.Add(Theme.Label("Theme", 10, true));
        theme.Items.AddRange(["Dark", "Light"]); theme.SelectedIndex = (int)settings.Theme; theme.Width = 340;
        body.Controls.Add(theme); body.Controls.Add(save); Controls.Add(body); Theme.Apply(this);
        save.Click += (_, _) =>
        {
            bool oldEnabled = startup.IsEnabled;
            bool oldSetting = settings.StartWithWindows, oldMinimized = settings.StartMinimized;
            var oldTheme = settings.Theme;
            try
            {
                startup.SetEnabled(windows.Checked);
                settings.StartWithWindows = windows.Checked; settings.StartMinimized = minimized.Checked; settings.Theme = (ThemeMode)theme.SelectedIndex;
                if (!saveSettings())
                {
                    startup.SetEnabled(oldEnabled); settings.StartWithWindows = oldSetting; settings.StartMinimized = oldMinimized; settings.Theme = oldTheme;
                    return;
                }
                DialogResult = DialogResult.OK; Close();
            }
            catch (Exception error) { MessageBox.Show(this, error.Message, "Settings", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        };
        AcceptButton = save;
    }
    internal void ExerciseUi(bool enable, bool minimize, ThemeMode mode)
    {
        windows.Checked = enable; minimized.Checked = minimize; theme.SelectedIndex = (int)mode; save.PerformClick();
    }
}
