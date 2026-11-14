namespace KeypadLayers;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        if (args.Length >= 2 && args[0] == "--self-test")
            return SelfTests.Run(args[1]);
        if (args.Length >= 2 && args[0] == "--probe")
        {
            try
            {
                var devices = KeypadDevice.Find();
                File.WriteAllText(args[1], System.Text.Json.JsonSerializer.Serialize(devices, SettingsStore.JsonOptions));
                return 0;
            }
            catch (Exception error) { File.WriteAllText(args[1], error.ToString()); return 1; }
        }
        using var instance = new Mutex(true, @"Local\KeypadLayers.Desktop", out bool first);
        if (!first)
        {
            MessageBox.Show("Keypad Layers is already running. Open it from the system tray.", "Keypad Layers");
            return 0;
        }
        try
        {
            var store = new SettingsStore();
            var (settings, warning) = store.Load();
            Theme.Set(settings.Theme);
            using var window = new MainWindow(settings, store, new WindowsActions());
            window.Shown += (_, _) =>
            {
                if (warning is not null) MessageBox.Show(window, warning, "Settings recovery", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            };
            using var context = new KeypadApplicationContext(window, settings.StartMinimized || args.Contains("--minimized"));
            Application.Run(context);
            return 0;
        }
        catch (Exception error)
        {
            MessageBox.Show(error.Message, "Keypad Layers", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }
}
