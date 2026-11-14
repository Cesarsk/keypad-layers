namespace KeypadLayers;

internal interface ILightingDevice
{
    IReadOnlyList<DeviceInfo> Find();
    Task ApplyAsync(DeviceInfo device, int mode);
}

internal sealed class UsbLightingDevice : ILightingDevice
{
    public IReadOnlyList<DeviceInfo> Find() => KeypadDevice.Find();
    public Task ApplyAsync(DeviceInfo device, int mode) => Task.Run(() => KeypadDevice.SetLighting(device, mode));
}

internal sealed class LightingDialog : Form
{
    private readonly Settings settings;
    private readonly Func<bool> save;
    private readonly ILightingDevice hardware;
    private readonly ComboBox devices = Theme.Combo(), modes = Theme.Combo();
    private readonly Button scan = Theme.Button("Scan again"), apply = Theme.Button("Apply", true), off = Theme.Button("Turn off");
    private readonly Label result = Theme.Label("", 10, true);
    internal bool IsBusy { get; private set; }
    internal string ResultText => result.Text;
    internal static string ModeName(int mode) => mode == 0 ? "Off" : "Mode " + mode;

    internal LightingDialog(Settings settings, Func<bool> save, ILightingDevice? hardware = null)
    {
        this.settings = settings; this.save = save; this.hardware = hardware ?? new UsbLightingDevice();
        Text = "Lighting"; Font = new Font("Segoe UI", 10); Icon = AppIcon.Value;
        ClientSize = new Size(490, 470); FormBorderStyle = FormBorderStyle.FixedDialog; StartPosition = FormStartPosition.CenterParent; MaximizeBox = MinimizeBox = false;
        var body = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(26), AutoScroll = true };
        body.Controls.Add(Theme.Label("Lighting", 21));
        var connection = Theme.Label("Connect by USB and close MINI KeyBoard.", 10, true); connection.MaximumSize = new Size(438, 0); body.Controls.Add(connection);
        devices.Width = 438; body.Controls.Add(devices); body.Controls.Add(scan);
        body.Controls.Add(Theme.Label("LED mode", 10, true));
        modes.Width = 438; modes.Items.AddRange(["Off", "Mode 1", "Mode 2"]); modes.SelectedIndex = settings.LastLedMode ?? 1; body.Controls.Add(modes);
        var buttons = new FlowLayoutPanel { AutoSize = true, Width = 438, WrapContents = false, Margin = Padding.Empty };
        buttons.Controls.Add(apply); buttons.Controls.Add(off); body.Controls.Add(buttons);
        result.MaximumSize = new Size(438, 0); body.Controls.Add(result);
        var note = Theme.Label("Uses the device's built-in modes. This protocol exposes no separate color or brightness controls.", 9, true); note.MaximumSize = new Size(438, 0); body.Controls.Add(note);
        Controls.Add(body); Theme.Apply(this);
        scan.Click += (_, _) => Scan();
        devices.SelectedIndexChanged += (_, _) => UpdateControls();
        apply.Click += async (_, _) => await ApplyAsync(modes.SelectedIndex);
        off.Click += async (_, _) => { modes.SelectedIndex = 0; await ApplyAsync(0); };
        Shown += (_, _) => Scan();
        FormClosing += (_, e) => { if (IsBusy && e.CloseReason == CloseReason.UserClosing) e.Cancel = true; };
    }

    private void Scan()
    {
        if (IsBusy) return;
        try
        {
            devices.Items.Clear(); foreach (var device in hardware.Find()) devices.Items.Add(device);
            if (devices.Items.Count > 0) devices.SelectedIndex = 0;
            result.Text = devices.Items.Count == 0 ? "No compatible keypad found." : settings.LastLedMode is int mode ? "Last applied in this app: " + ModeName(mode) : "Choose a mode and click Apply.";
        }
        catch (Exception error) { result.Text = "USB scan failed: " + error.Message; }
        UpdateControls();
    }

    private void UpdateControls()
    {
        scan.Enabled = devices.Enabled = modes.Enabled = !IsBusy;
        apply.Enabled = off.Enabled = !IsBusy && devices.SelectedItem is DeviceInfo;
        ControlBox = !IsBusy;
    }

    private async Task ApplyAsync(int mode)
    {
        if (IsBusy || devices.SelectedItem is not DeviceInfo device) return;
        IsBusy = true; UpdateControls(); result.Text = "Applying " + ModeName(mode) + "...";
        try
        {
            await hardware.ApplyAsync(device, mode);
            settings.LastLedMode = mode;
            result.Text = save() ? ModeName(mode) + " sent to the keypad." : ModeName(mode) + " sent, but the preference could not be saved.";
        }
        catch (Exception error) { result.Text = "Lighting failed: " + error.Message; }
        finally { IsBusy = false; UpdateControls(); }
    }

    internal void SelectMode(int mode) => modes.SelectedIndex = mode;
    internal void ClickApply() => apply.PerformClick();
    internal void ClickOff() => off.PerformClick();
    internal bool CanApply => apply.Enabled;
}
