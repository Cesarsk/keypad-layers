using System.Runtime.InteropServices;
using System.Text.Json;
using System.Diagnostics;
using Microsoft.Win32;

namespace KeypadLayers;

internal static class SelfTests
{
    private sealed class RecordingOutput : IActionOutput
    {
        public List<Mapping> Actions { get; } = [];
        public void Execute(Mapping mapping) => Actions.Add(mapping.Copy());
    }
    private sealed class FakeStartup : IStartupRegistration
    {
        public bool IsEnabled { get; private set; }
        public void SetEnabled(bool enabled) => IsEnabled = enabled;
    }
    private sealed class FakeLighting : ILightingDevice
    {
        public bool Connected { get; set; } = true;
        public bool Fail { get; set; }
        public TaskCompletionSource? Pending { get; set; }
        public List<int> Applied { get; } = [];
        public IReadOnlyList<DeviceInfo> Find() => Connected ? [new("test-device", "3 keys + 1 knob · 1189:8890", 65)] : [];
        public Task ApplyAsync(DeviceInfo device, int mode)
        {
            Applied.Add(mode);
            return Fail ? Task.FromException(new IOException("Device disconnected")) : Pending?.Task ?? Task.CompletedTask;
        }
    }
    public static int Run(string directory)
    {
        Directory.CreateDirectory(directory);
        var results = new List<string>();
        void Check(bool condition, string name) { if (!condition) throw new InvalidOperationException(name); results.Add("PASS  " + name); }
        void Reject(Action operation, string name) { bool rejected = false; try { operation(); } catch (Exception e) when (e is ArgumentException or InvalidDataException or InvalidOperationException or JsonException) { rejected = true; } Check(rejected, name); }
        try
        {
            var config = Settings.Defaults(); config.Validate();
            Check(config.Layers.Count == 3 && config.Layers.All(l => l.Bindings.Count == 6), "Three default layers define all six controls");
            var output = new RecordingOutput(); var engine = new LayerEngine(config, output);
            engine.Trigger(ControlId.Clockwise);
            Check(output.Actions.Count == 1 && output.Actions[0].Value == "Volume up", "Rotating the knob routes the active layer's action");
            engine.Trigger(ControlId.KnobPress); engine.Trigger(ControlId.Key1);
            Check(engine.ActiveLayer.Name == "Editing" && output.Actions[^1].Value == "Ctrl+C", "Knob press changes the live layer and routes the next key correctly");
            engine.Trigger(ControlId.KnobPress); engine.Trigger(ControlId.KnobPress);
            Check(engine.ActiveLayer.Name == "Media", "Next layer wraps to the first");
            engine.ActiveLayer.Bindings[ControlId.Key1] = new() { Kind = ActionKind.PreviousLayer };
            engine.Trigger(ControlId.Key1); Check(engine.ActiveLayer.Name == "Windows", "Previous layer wraps backward");
            engine.ActiveLayer.Bindings[ControlId.Key1] = new() { Kind = ActionKind.SwitchLayer, Value = config.Layers[1].Id };
            engine.Trigger(ControlId.Key1); Check(engine.ActiveLayer.Name == "Editing", "Direct layer jump works");
            int oldCount = output.Actions.Count;
            engine.ActiveLayer.Bindings[ControlId.Key1] = new(); engine.Trigger(ControlId.Key1);
            Check(output.Actions.Count == oldCount, "Unassigned controls emit no action");
            var copy = config.Layers[0].Duplicate(); copy.Bindings[ControlId.Key2].Value = "Mute";
            Check(config.Layers[0].Bindings[ControlId.Key2].Value == "Play / pause", "Duplicated layers do not share mutable bindings");
            string deleted = config.Layers[0].Id;
            config.Layers[1].Bindings[ControlId.Key1] = new() { Kind = ActionKind.SwitchLayer, Value = deleted };
            engine.Remove(deleted); config.Validate();
            Check(config.Layers.Count == 2 && config.StartupLayerId == config.Layers[0].Id && config.Layers[0].Bindings[ControlId.Key1].Kind == ActionKind.NextLayer, "Removing a layer repairs startup and direct jumps");
            engine.Remove(config.Layers[1].Id); Reject(() => engine.Remove(config.Layers[0].Id), "Cannot delete the last layer");
            var store = new SettingsStore(Path.Combine(directory, "profile.json")); store.Save(config);
            var loaded = store.Load().Settings;
            Check(loaded.Layers[0].Id == config.Layers[0].Id, "Profile round-trip preserves layer identity");
            config.Layers[0].Name = "Renamed"; store.Save(config);
            int previousInvalidFiles = Directory.GetFiles(directory, "profile.json.invalid-*").Length;
            File.WriteAllText(store.Path, "broken json");
            var recovered = store.Load();
            Check(recovered.Warning is not null && recovered.Settings.Layers[0].Name != "Renamed", "Corrupt settings recover the last valid backup");
            Check(Directory.GetFiles(directory, "profile.json.invalid-*").Length == previousInvalidFiles + 1, "Recovery preserves the unreadable original");
            Reject(() => SettingsStore.Deserialize("{\"Layers\":[]}"), "Reject profiles without a layer");
            var broken = Settings.Defaults(); broken.Layers[0].Bindings.Remove(ControlId.Key1);
            Reject(broken.Validate, "Reject incomplete control maps");
            broken = Settings.Defaults(); broken.Layers[0].Bindings[ControlId.Key1] = new() { Kind = ActionKind.SwitchLayer, Value = "missing" };
            Reject(broken.Validate, "Reject dangling layer references");
            broken = Settings.Defaults(); broken.Layers[1].Id = broken.Layers[0].Id;
            Reject(broken.Validate, "Reject duplicate layer IDs");
            Reject(() => Shortcut.Parse("Ctrl+F13"), "Prevent mappings from recursively using the reserved input signals");
            Reject(() => Shortcut.Parse("Ctrl+"), "Reject incomplete shortcuts");
            Check(Shortcut.Parse("Ctrl+Shift+S").Modifiers.Length == 2 && Shortcut.Parse("Win+Left").Key == 0x25 && Shortcut.Parse("Ctrl+1").Key == 0x31, "Shortcut parsing handles modifiers, arrows, and digits");
            Check(Marshal.SizeOf<Native.Input>() == (IntPtr.Size == 8 ? 40 : 28), "SendInput structure matches Windows ABI");
            Check(Native.Unicode('é').Data.Keyboard.ScanCode == 'é' && Native.Unicode('é', true).Data.Keyboard.Flags == 6, "Unicode typing emits complete key-down/key-up pairs");
            var reports = KeypadDevice.TriggerReports(65).ToArray();
            Check(reports.Length == 24 && reports.All(r => r.Length == 65), "One-time setup emits four reports for each of six controls");
            Check(reports[0][0] == 3 && reports[0][1] == 0xFE && reports[0][2] == 1 && reports[3][1] == 0xAA && reports[3][2] == 0xAA, "Hardware setup uses the documented 8890 transaction envelope");
            Check(new byte[] { reports[2][1], reports[6][1], reports[10][1], reports[14][1], reports[18][1], reports[22][1] }.SequenceEqual(new byte[] { 1, 2, 3, 13, 14, 15 }), "Key and encoder positions use the 8890 control IDs");
            Check(Enumerable.Range(0, 6).All(i => reports[i * 4 + 2][6] == 0x68 + i && reports[i * 4 + 2][2] == 0x11), "Only hardware layer 1 is mapped to F13-F18");
            Reject(() => KeypadDevice.TriggerReports(8).ToArray(), "Refuse an incompatible HID report size");
            var lightingReports = KeypadDevice.LightingReports(65, 2).ToArray();
            Check(lightingReports.Length == 3 && lightingReports.All(r => r.Length == 65) && lightingReports[0].Take(3).SequenceEqual(new byte[] { 3, 0xA1, 1 }) && lightingReports[1].Take(4).SequenceEqual(new byte[] { 3, 0xB0, 0x18, 2 }) && lightingReports[2].Take(3).SequenceEqual(new byte[] { 3, 0xAA, 0xA1 }), "LED reports match the vendor's 8890 lighting transaction");
            Check(lightingReports.All(r => r[1] is 0xA1 or 0xB0 or 0xAA) && lightingReports[2][2] != 0xAA, "Lighting reports never use a key-binding command or commit");
            Check(KeypadDevice.LightingReports(65, 0).ElementAt(1)[3] == 0 && KeypadDevice.LightingReports(65, 1).ElementAt(1)[3] == 1, "Off and Mode 1 use their distinct firmware mode values");
            Reject(() => KeypadDevice.LightingReports(65, 3).ToArray(), "Reject LED modes absent from the vendor app");
            Reject(() => KeypadDevice.LightingReports(8, 1).ToArray(), "Reject an incompatible LED output report size");
            var lightingSettings = Settings.Defaults();
            Check(lightingSettings.LastLedMode is null, "Existing profiles do not invent an applied LED mode");
            lightingSettings.LastLedMode = 3; Reject(lightingSettings.Validate, "Reject invalid saved LED preferences"); lightingSettings.LastLedMode = null;
            var lightingStore = new SettingsStore(Path.Combine(directory, "lighting.json")); var fakeLighting = new FakeLighting();
            using (var lighting = new LightingDialog(lightingSettings, () => { lightingStore.Save(lightingSettings); return true; }, fakeLighting))
            {
                lighting.Show(); Application.DoEvents();
                Check(fakeLighting.Applied.Count == 0 && lighting.CanApply, "Opening Lighting scans the device without changing its LEDs");
                lighting.SelectMode(2); lighting.ClickApply(); Application.DoEvents();
                Check(fakeLighting.Applied.SequenceEqual(new[] { 2 }) && lightingStore.Load().Settings.LastLedMode == 2, "Apply button sends the selected LED mode and persists it");
                lighting.ClickOff(); Application.DoEvents();
                Check(fakeLighting.Applied.SequenceEqual(new[] { 2, 0 }) && lightingSettings.LastLedMode == 0, "Turn off button sends mode zero");
                fakeLighting.Pending = new TaskCompletionSource(); lighting.SelectMode(1); lighting.ClickApply();
                Check(lighting.IsBusy && !lighting.CanApply && lightingSettings.LastLedMode == 0, "LED writes disable repeated applies and wait before changing saved state");
                fakeLighting.Pending.SetResult();
                var timeout = Stopwatch.StartNew(); while (lighting.IsBusy && timeout.ElapsedMilliseconds < 3000) { Application.DoEvents(); Thread.Sleep(1); }
                Check(!lighting.IsBusy && lighting.CanApply && lightingSettings.LastLedMode == 1, "LED controls recover after an asynchronous write completes");
                fakeLighting.Pending = null; fakeLighting.Fail = true; lighting.SelectMode(2); lighting.ClickApply(); Application.DoEvents();
                Check(!lighting.IsBusy && lightingSettings.LastLedMode == 1 && lighting.ResultText.Contains("failed", StringComparison.OrdinalIgnoreCase), "A failed LED write retains the last applied mode and reports the failure");
                fakeLighting.Fail = false; lighting.SelectMode(1); lighting.ClickApply(); Application.DoEvents();
                Check(lightingSettings.LastLedMode == 1 && lighting.ResultText.Contains("sent to the keypad"), "A successful retry clears the LED failure state");
                foreach (var mode in Enum.GetValues<ThemeMode>())
                {
                    Theme.Set(mode); Theme.Apply(lighting); Application.DoEvents();
                    using var image = new Bitmap(lighting.Width, lighting.Height); lighting.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size));
                    image.Save(Path.Combine(directory, "lighting-" + mode.ToString().ToLowerInvariant() + ".png"));
                }
                lighting.Close(); Theme.Set(ThemeMode.Dark);
            }
            using (var disconnected = new LightingDialog(lightingSettings, () => true, new FakeLighting { Connected = false }))
            {
                disconnected.Show(); Application.DoEvents(); Check(!disconnected.CanApply, "Lighting cannot apply when no supported keypad is connected"); disconnected.Close();
            }
            const string displayCommand = """C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe -NoProfile -WindowStyle Hidden -Command "$d=Add-Type -MemberDefinition ('[DllImport('+[char]34+'user32.dll'+[char]34+')] public static extern System.IntPtr SendMessage(System.IntPtr hWnd, uint msg, System.UIntPtr wParam, System.IntPtr lParam);') -Name DisplayPower -PassThru; Start-Sleep -Seconds 1; [void]$d::SendMessage([IntPtr]0xFFFF,0x0112,[UIntPtr]::new([uint32]0xF170),[IntPtr]2)""" + "\"";
            var parsed = CommandLine.Parse(displayCommand);
            Check(parsed.Executable + " " + parsed.Arguments == displayCommand, "PowerShell display-off example keeps its exact arguments without executing it");
            var quoted = CommandLine.Parse("\"C:\\Program Files\\Tool\\app.exe\" --name \"two words\" $value");
            Check(quoted.Executable == @"C:\Program Files\Tool\app.exe" && quoted.Arguments == "--name \"two words\" $value", "Command parser separates quoted executable paths without rewriting arguments");
            Reject(() => CommandLine.Parse("\"missing.exe"), "Reject unterminated executable quotes");
            Reject(() => CommandLine.Parse("app.exe\nsecond command"), "Reject multiline command input");
            var commandMapping = new Mapping { Kind = ActionKind.Command, Value = "powershell.exe -NoProfile -NonInteractive -Command \"[Console]::Write('command-test')\"", WorkingDirectory = directory, RunHidden = true };
            var start = CommandLine.StartInfo(commandMapping); start.RedirectStandardOutput = true; start.RedirectStandardError = true;
            using (var child = Process.Start(start) ?? throw new InvalidOperationException("Command did not start."))
            {
                string stdout = child.StandardOutput.ReadToEnd();
                Check(child.WaitForExit(15000) && child.ExitCode == 0 && stdout == "command-test", "Custom command runs hidden with arguments and working directory");
            }
            Check(!start.UseShellExecute && start.CreateNoWindow && start.WindowStyle == ProcessWindowStyle.Hidden, "Hidden command options use direct process launch");
            commandMapping.RunHidden = false;
            Check(!CommandLine.StartInfo(commandMapping).CreateNoWindow, "Command window visibility is configurable");
            var commandCopy = commandMapping.Copy(); commandCopy.WorkingDirectory = "different";
            Check(commandMapping.WorkingDirectory == directory && !commandCopy.RunHidden, "Duplicating bindings preserves independent command options");
            string testKey = @"Software\KeypadLayers\Tests\" + Guid.NewGuid().ToString("N");
            try
            {
                var registration = new StartupRegistration(@"C:\Apps with spaces\KeypadLayers.exe", testKey);
                registration.SetEnabled(true);
                Check(registration.IsEnabled && registration.Command == "\"C:\\Apps with spaces\\KeypadLayers.exe\" --startup", "Windows startup registration quotes the executable path");
                registration.SetEnabled(false); Check(!registration.IsEnabled, "Disabling startup removes the registration");
            }
            finally { Registry.CurrentUser.DeleteSubKey(testKey, false); }
            var preferences = Settings.Defaults(); var prefsStore = new SettingsStore(Path.Combine(directory, "preferences.json")); var fakeStartup = new FakeStartup();
            using (var dialog = new PreferencesDialog(preferences, fakeStartup, () => { prefsStore.Save(preferences); return true; }))
            {
                dialog.Show(); Application.DoEvents(); dialog.ExerciseUi(true, true, ThemeMode.Light);
            }
            Check(fakeStartup.IsEnabled && preferences.StartMinimized && prefsStore.Load().Settings.Theme == ThemeMode.Light, "Settings UI applies startup, tray launch, and theme preferences");
            using (var dialog = new PreferencesDialog(preferences, fakeStartup, () => { prefsStore.Save(preferences); return true; }))
            {
                dialog.Show(); Application.DoEvents(); dialog.ExerciseUi(false, false, ThemeMode.Dark);
            }
            Check(!fakeStartup.IsEnabled && !preferences.StartMinimized && prefsStore.Load().Settings.Theme == ThemeMode.Dark, "Settings UI disables startup and minimized launch");
            using (var hiddenWindow = new MainWindow(Settings.Defaults(), new SettingsStore(Path.Combine(directory, "hidden.json")), new RecordingOutput(), true))
            using (var context = new KeypadApplicationContext(hiddenWindow, true))
            {
                Application.DoEvents();
                Check(!hiddenWindow.Visible && hiddenWindow.IsHandleCreated, "Minimized startup creates the message loop without showing the editor");
                hiddenWindow.OpenEditor(); Application.DoEvents();
                Check(hiddenWindow.Visible && hiddenWindow.WindowState == FormWindowState.Normal, "Opening from the tray restores the editor");
                hiddenWindow.Close();
            }
            bool shownDuringHiddenMessageLoop = false;
            using (var loopWindow = new MainWindow(Settings.Defaults(), new SettingsStore(Path.Combine(directory, "hidden-loop.json")), new RecordingOutput(), true))
            using (var context = new KeypadApplicationContext(loopWindow, true))
            using (var timer = new System.Windows.Forms.Timer { Interval = 25 })
            {
                timer.Tick += (_, _) => { timer.Stop(); shownDuringHiddenMessageLoop = loopWindow.Visible; loopWindow.Close(); };
                timer.Start(); Application.Run(context);
            }
            Check(!shownDuringHiddenMessageLoop, "Minimized startup stays hidden through the real message loop and exits when closed");
            var overlayArea = Screen.FromPoint(Cursor.Position).WorkingArea;
            using (var overlay = new LayerOverlay())
            {
                var overlayPosition = new Point(overlayArea.Right - overlay.Width - 24, overlayArea.Bottom - overlay.Height - 24);
                overlay.ShowLayer("Editing"); Application.DoEvents();
                Check(overlay.Location == overlayPosition, "Layer overlay appears at the requested corner on its first display");
                overlay.Hide(); overlay.ShowLayer("Media"); Application.DoEvents();
                Check(overlay.Location == overlayPosition, "Layer overlay keeps the requested corner on later displays");
                overlay.Hide();
            }
            using var window = new MainWindow(Settings.Defaults(), new SettingsStore(Path.Combine(directory, "ui-profile.json")), new RecordingOutput(), true);
            window.Show(); Application.DoEvents(); window.ExerciseUi(); Application.DoEvents();
            Check(true, "Editor buttons add, rename, activate, bind, duplicate, delete, pause, and persist actual UI state");
            using var screenshot = new Bitmap(window.Width, window.Height); window.DrawToBitmap(screenshot, new Rectangle(Point.Empty, screenshot.Size));
            screenshot.Save(Path.Combine(directory, "editor.png"));
            window.Close();
            using (var themeWindow = new MainWindow(Settings.Defaults(), new SettingsStore(Path.Combine(directory, "theme-profile.json")), new RecordingOutput(), true))
            {
                themeWindow.Show(); Application.DoEvents(); themeWindow.CaptureThemes(directory); themeWindow.Close();
                Check(true, "Dark and light themes render with contrasting control text");
            }
            File.WriteAllLines(Path.Combine(directory, "self-test.txt"), results.Append($"PASS  {results.Count} checks; no hardware settings written."));
            return 0;
        }
        catch (Exception error)
        {
            File.WriteAllLines(Path.Combine(directory, "self-test.txt"), results.Append("FAIL  " + error));
            return 1;
        }
    }
}
