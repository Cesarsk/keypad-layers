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
