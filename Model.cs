using System.Text.Json;
using System.Text.Json.Serialization;

namespace KeypadLayers;

internal enum ControlId { Key1, Key2, Key3, Counterclockwise, KnobPress, Clockwise }
internal enum ActionKind { None, Shortcut, Text, Media, Launch, NextLayer, PreviousLayer, SwitchLayer, Command }
internal enum ThemeMode { Dark, Light }

internal sealed class Mapping
{
    public ActionKind Kind { get; set; }
    public string Value { get; set; } = "";
    public string WorkingDirectory { get; set; } = "";
    public bool RunHidden { get; set; } = true;
    public Mapping Copy() => new() { Kind = Kind, Value = Value, WorkingDirectory = WorkingDirectory, RunHidden = RunHidden };
}

internal sealed class Layer
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "New layer";
    public Dictionary<ControlId, Mapping> Bindings { get; set; } = Enum.GetValues<ControlId>().ToDictionary(id => id, _ => new Mapping());
    public Layer Duplicate() => new() { Name = Name + " copy", Bindings = Bindings.ToDictionary(p => p.Key, p => p.Value.Copy()) };
    public override string ToString() => Name;
}

internal sealed class Settings
{
    public int SchemaVersion { get; set; } = 1;
    public List<Layer> Layers { get; set; } = [];
    public string StartupLayerId { get; set; } = "";
    public bool ShowOverlay { get; set; } = true;
    public bool DeviceConfigured { get; set; }
    public ThemeMode Theme { get; set; } = ThemeMode.Dark;
    public bool StartWithWindows { get; set; }
    public bool StartMinimized { get; set; }
    public int? LastLedMode { get; set; }

    public static Settings Defaults()
    {
        var media = new Layer { Name = "Media" };
        media.Bindings[ControlId.Key1] = new() { Kind = ActionKind.Media, Value = "Previous track" };
        media.Bindings[ControlId.Key2] = new() { Kind = ActionKind.Media, Value = "Play / pause" };
        media.Bindings[ControlId.Key3] = new() { Kind = ActionKind.Media, Value = "Next track" };
        media.Bindings[ControlId.Counterclockwise] = new() { Kind = ActionKind.Media, Value = "Volume down" };
        media.Bindings[ControlId.Clockwise] = new() { Kind = ActionKind.Media, Value = "Volume up" };
        media.Bindings[ControlId.KnobPress] = new() { Kind = ActionKind.NextLayer };
        var editing = new Layer { Name = "Editing" };
        string[] shortcuts = ["Ctrl+C", "Ctrl+V", "Ctrl+Z", "Ctrl+Shift+Z", "", "Ctrl+Z"];
        foreach (var id in Enum.GetValues<ControlId>())
            editing.Bindings[id] = id == ControlId.KnobPress ? new() { Kind = ActionKind.NextLayer } : new() { Kind = ActionKind.Shortcut, Value = shortcuts[(int)id] };
        var windows = new Layer { Name = "Windows" };
        string[] windowShortcuts = ["Win+Left", "Win+Up", "Win+Right", "Ctrl+Shift+Tab", "", "Ctrl+Tab"];
        foreach (var id in Enum.GetValues<ControlId>())
            windows.Bindings[id] = id == ControlId.KnobPress ? new() { Kind = ActionKind.NextLayer } : new() { Kind = ActionKind.Shortcut, Value = windowShortcuts[(int)id] };
        return new() { Layers = [media, editing, windows], StartupLayerId = media.Id };
    }

    public void Validate()
    {
        if (SchemaVersion != 1) throw new InvalidDataException("This settings version is not supported.");
        if (!Enum.IsDefined(Theme)) throw new InvalidDataException("Unsupported theme.");
        if (LastLedMode is < 0 or > 2) throw new InvalidDataException("Unsupported LED mode.");
        if (Layers is null || Layers.Count is < 1 or > 50 || Layers.Any(l => l is null)) throw new InvalidDataException("A profile must contain 1 to 50 layers.");
        if (Layers.Any(l => string.IsNullOrWhiteSpace(l.Id) || string.IsNullOrWhiteSpace(l.Name) || l.Name.Length > 60)) throw new InvalidDataException("Every layer needs a name (up to 60 characters) and an ID.");
        if (Layers.Select(l => l.Id).Distinct().Count() != Layers.Count) throw new InvalidDataException("Layer IDs must be unique.");
        if (!Layers.Any(l => l.Id == StartupLayerId)) throw new InvalidDataException("The startup layer no longer exists.");
        foreach (var layer in Layers)
        {
            if (layer.Bindings is null || layer.Bindings.Count != 6 || Enum.GetValues<ControlId>().Any(id => !layer.Bindings.ContainsKey(id))) throw new InvalidDataException("Each layer must define all six controls.");
            foreach (var mapping in layer.Bindings.Values)
            {
                if (mapping is null || !Enum.IsDefined(mapping.Kind) || mapping.Value is null) throw new InvalidDataException("Invalid action.");
                if (mapping.Kind == ActionKind.Shortcut) Shortcut.Parse(mapping.Value);
                if (mapping.WorkingDirectory is null || mapping.WorkingDirectory.Length > 4096) throw new InvalidDataException("Invalid working directory.");
                if (mapping.Kind == ActionKind.Command) CommandLine.Parse(mapping.Value);
                if (mapping.Kind is ActionKind.Text or ActionKind.Launch && (string.IsNullOrEmpty(mapping.Value) || mapping.Value.Length > 4096)) throw new InvalidDataException("Enter a value with 1 to 4096 characters.");
                if (mapping.Kind == ActionKind.Media && !WindowsActions.MediaKeys.ContainsKey(mapping.Value)) throw new InvalidDataException("Choose a supported media action.");
                if (mapping.Kind == ActionKind.SwitchLayer && !Layers.Any(l => l.Id == mapping.Value)) throw new InvalidDataException("The target layer does not exist.");
            }
        }
    }
}

internal sealed class SettingsStore(string? path = null)
{
    internal static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };
    public string Path { get; } = path ?? System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KeypadLayers", "settings.json");
    public static Settings Deserialize(string json)
    {
        var settings = JsonSerializer.Deserialize<Settings>(json, JsonOptions) ?? throw new InvalidDataException("Empty settings file.");
        settings.Validate();
        return settings;
    }
    public (Settings Settings, string? Warning) Load()
    {
        if (!File.Exists(Path)) return (Settings.Defaults(), null);
        try { return (Deserialize(File.ReadAllText(Path)), null); }
        catch (Exception error) when (error is JsonException or InvalidDataException or ArgumentException or IOException)
        {
            string preserved = Path + ".invalid-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmssfff");
            File.Copy(Path, preserved);
            try { return (Deserialize(File.ReadAllText(Path + ".bak")), "Settings could not be read. The last valid backup was restored. The unreadable file was preserved at:\n" + preserved); }
            catch (Exception backupError) when (backupError is JsonException or InvalidDataException or ArgumentException or IOException)
            { return (Settings.Defaults(), "Settings and backup could not be read. Default layers were loaded. Your original file was preserved at:\n" + preserved); }
        }
    }
    public void Save(Settings settings)
    {
        settings.Validate();
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        string temp = Path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(settings, JsonOptions));
        if (File.Exists(Path))
        {
            try { Deserialize(File.ReadAllText(Path)); File.Copy(Path, Path + ".bak", true); }
            catch (Exception error) when (error is JsonException or InvalidDataException or ArgumentException) { }
        }
        File.Move(temp, Path, true);
    }
}

internal interface IActionOutput { void Execute(Mapping mapping); }

internal sealed class LayerEngine(Settings settings, IActionOutput output)
{
    public Settings Settings { get; } = settings;
    public string ActiveLayerId { get; private set; } = settings.StartupLayerId;
    public Layer ActiveLayer => Settings.Layers.First(l => l.Id == ActiveLayerId);
    public event Action? LayerChanged;
    public void Activate(string id)
    {
        if (!Settings.Layers.Any(l => l.Id == id)) throw new ArgumentException("Layer does not exist.");
        if (ActiveLayerId == id) return;
        ActiveLayerId = id;
        LayerChanged?.Invoke();
    }
    public Mapping Trigger(ControlId id)
    {
        var mapping = ActiveLayer.Bindings[id].Copy();
        switch (mapping.Kind)
        {
            case ActionKind.NextLayer: Cycle(1); break;
            case ActionKind.PreviousLayer: Cycle(-1); break;
            case ActionKind.SwitchLayer: Activate(mapping.Value); break;
            case ActionKind.None: break;
            default: output.Execute(mapping); break;
        }
        return mapping;
    }
    private void Cycle(int step)
    {
        int index = Settings.Layers.FindIndex(l => l.Id == ActiveLayerId);
        Activate(Settings.Layers[(index + step + Settings.Layers.Count) % Settings.Layers.Count].Id);
    }
    public void Remove(string id)
    {
        if (Settings.Layers.Count == 1) throw new InvalidOperationException("Keep at least one layer.");
        if (ActiveLayerId == id) Activate(Settings.Layers.First(l => l.Id != id).Id);
        Settings.Layers.RemoveAll(l => l.Id == id);
        if (Settings.StartupLayerId == id) Settings.StartupLayerId = Settings.Layers[0].Id;
        foreach (var mapping in Settings.Layers.SelectMany(l => l.Bindings.Values))
            if (mapping.Kind == ActionKind.SwitchLayer && mapping.Value == id) { mapping.Kind = ActionKind.NextLayer; mapping.Value = ""; }
    }
}

internal static class Labels
{
    public static string Control(ControlId id) => id switch { ControlId.Key1 => "Key 1", ControlId.Key2 => "Key 2", ControlId.Key3 => "Key 3", ControlId.Counterclockwise => "Turn left", ControlId.KnobPress => "Knob press", _ => "Turn right" };
    public static string Kind(ActionKind kind) => kind switch { ActionKind.None => "No action", ActionKind.Shortcut => "Keyboard shortcut", ActionKind.Text => "Type text", ActionKind.Media => "Media control", ActionKind.Launch => "Open app or URL", ActionKind.NextLayer => "Next layer", ActionKind.PreviousLayer => "Previous layer", ActionKind.Command => "Run command", _ => "Switch to layer" };
    public static string Describe(Mapping mapping, Settings settings) => mapping.Kind switch
    {
        ActionKind.None => "Unassigned",
        ActionKind.NextLayer => "Next layer",
        ActionKind.PreviousLayer => "Previous layer",
        ActionKind.SwitchLayer => settings.Layers.FirstOrDefault(l => l.Id == mapping.Value)?.Name ?? "Missing layer",
        ActionKind.Command => System.IO.Path.GetFileName(CommandLine.Parse(mapping.Value).Executable),
        _ => mapping.Value.Replace("\r", " ").Replace("\n", " ")
    };
}
