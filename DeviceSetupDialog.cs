namespace KeypadLayers;

internal sealed class DeviceSetupDialog : Form
{
    private readonly ComboBox devices = Theme.Combo();
    private readonly Button scan = Theme.Button("Scan again"), write = Theme.Button("Set up these controls", true);
    private readonly Label result = Theme.Label("", 10, true);
    private readonly Settings settings;
    private readonly Func<bool> save;
    private readonly bool testing;
    private readonly CheckBox acknowledge = new() { Text = "This is my 3-key, 1-knob keypad", AutoSize = true, ForeColor = Theme.Text, Margin = new Padding(0, 10, 0, 12) };
    public DeviceSetupDialog(Settings settings, Func<bool> save, bool testing = false)
    {
        this.settings = settings; this.save = save; this.testing = testing;
        Text = "Keypad setup"; BackColor = Theme.Background; ForeColor = Theme.Text; Font = new Font("Segoe UI", 10); Icon = AppIcon.Value;
        ClientSize = new Size(620, 620); FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = MinimizeBox = false; StartPosition = FormStartPosition.CenterParent;
        var body = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(26), AutoScroll = true };
        body.Controls.Add(Theme.Label("Keypad setup", 21));
        var intro = Theme.Label("Connect by USB and close MINI KeyBoard before setup.", 10, true); intro.MaximumSize = new Size(558, 0); body.Controls.Add(intro);
        devices.Width = 558; body.Controls.Add(devices); body.Controls.Add(scan);
        var signals = Theme.Label("Key 1   →   F13\nKey 2   →   F14\nKey 3   →   F15\nTurn left   →   F16\nKnob press   →   F17\nTurn right   →   F18", 11); signals.Margin = new Padding(0, 6, 0, 16); body.Controls.Add(signals);
        var note = Theme.Label("Setup replaces the six assignments in hardware layer 1. Save your current mappings first; this model has no readback.", 10, true); note.MaximumSize = new Size(558, 0); body.Controls.Add(note);
        body.Controls.Add(acknowledge); write.Width = 260; write.Enabled = false; body.Controls.Add(write);
        result.MaximumSize = new Size(558, 0); body.Controls.Add(result);
        var alternative = Theme.Label("Already using F13–F18? Close this window. These signals are reserved across all keyboards while the app listens.", 9, true); alternative.MaximumSize = new Size(558, 0); body.Controls.Add(alternative);
        Controls.Add(body); Theme.Apply(this);
        acknowledge.CheckedChanged += (_, _) => UpdateWrite(); devices.SelectedIndexChanged += (_, _) => UpdateWrite();
        scan.Click += (_, _) => Scan(); write.Click += async (_, _) => await ConfigureAsync();
        Shown += (_, _) => Scan();
    }
    private void UpdateWrite() => write.Enabled = !testing && acknowledge.Checked && devices.SelectedItem is DeviceInfo;
    private void Scan()
    {
        try
        {
            devices.Items.Clear(); foreach (var device in KeypadDevice.Find()) devices.Items.Add(device);
            if (devices.Items.Count > 0) devices.SelectedIndex = 0;
            result.Text = devices.Items.Count == 0 ? "No supported keypad found. Connect 1189:8890 by USB and scan again." : "Keypad found. Confirm the layout above, then start setup.";
        }
        catch (Exception error) { result.Text = "Could not scan USB devices: " + error.Message; }
        UpdateWrite();
    }
    private async Task ConfigureAsync()
    {
        if (testing || devices.SelectedItem is not DeviceInfo device) return;
        if (MessageBox.Show(this, "Replace hardware layer 1 with the six signals listed above?\n\nAfter setup, keep Keypad Layers running to use your assignments. You can reprogram the keypad with the vendor tool later.", "Review keypad setup", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        write.Enabled = scan.Enabled = acknowledge.Enabled = devices.Enabled = false; ControlBox = false;
        result.Text = "Writing the six input signals. Keep the USB cable connected...";
        try
        {
            await Task.Run(() => KeypadDevice.Configure(device));
            settings.DeviceConfigured = true;
            result.Text = save() ? "Signals written. Test each control in the main window." : "Signals written, but the profile could not be saved. Check the main window.";
        }
        catch (Exception error) { result.Text = "Setup did not finish: " + error.Message; }
        finally { ControlBox = true; scan.Enabled = acknowledge.Enabled = devices.Enabled = true; UpdateWrite(); }
    }
}
