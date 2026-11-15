using Microsoft.Win32;

namespace KeypadLayers;

internal interface IStartupRegistration
{
    bool IsEnabled { get; }
    void SetEnabled(bool enabled);
}

internal sealed class StartupRegistration(string? executable = null, string keyPath = @"Software\Microsoft\Windows\CurrentVersion\Run") : IStartupRegistration
{
    private const string ValueName = "KeypadLayers";
    public string Command { get; } = "\"" + (executable ?? Environment.ProcessPath ?? throw new InvalidOperationException("Cannot locate the app executable.")) + "\" --startup";
    public bool IsEnabled
    {
        get { using var key = Registry.CurrentUser.OpenSubKey(keyPath); return string.Equals(key?.GetValue(ValueName) as string, Command, StringComparison.OrdinalIgnoreCase); }
    }
    public void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(keyPath, true);
        if (enabled) key.SetValue(ValueName, Command, RegistryValueKind.String);
        else key.DeleteValue(ValueName, false);
    }
}

internal sealed class KeypadApplicationContext : ApplicationContext
{
    public KeypadApplicationContext(MainWindow window, bool minimized)
    {
        if (minimized)
            window.FormClosed += (_, _) => ExitThread();
        else
            MainForm = window;
        _ = window.Handle;
        window.BeginInvoke(window.StartRuntime);
        if (!minimized) window.OpenEditor();
    }
}
