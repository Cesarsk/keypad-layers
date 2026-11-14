using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace KeypadLayers;

internal sealed record Shortcut(ushort[] Modifiers, ushort Key)
{
    public static Shortcut Parse(string text)
    {
        var parts = text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0 || text.EndsWith('+')) throw new ArgumentException("Enter a shortcut such as Ctrl+Shift+S or Win+Left. Use OemPlus for the + key.");
        var modifiers = new List<ushort>();
        foreach (string part in parts[..^1])
        {
            ushort modifier = part.ToUpperInvariant() switch { "CTRL" or "CONTROL" => 0x11, "SHIFT" => 0x10, "ALT" => 0x12, "WIN" or "WINDOWS" => 0x5B, _ => throw new ArgumentException("Unknown modifier: " + part) };
            if (!modifiers.Contains(modifier)) modifiers.Add(modifier);
        }
        string last = parts[^1];
        if (last.Length == 1 && char.IsAsciiDigit(last[0])) last = "D" + last;
        if (!Enum.TryParse<Keys>(last, true, out var key) || !Enum.IsDefined(key) || (int)key <= 0 || (int)key > 0xFE)
            throw new ArgumentException("Unknown key. Examples: A, Enter, Space, Left, F5, OemPlus.");
        if (key is >= Keys.F13 and <= Keys.F18) throw new ArgumentException("F13-F18 are reserved for the keypad's six input signals.");
        if (key is Keys.ControlKey or Keys.ShiftKey or Keys.Menu or Keys.LWin or Keys.RWin) throw new ArgumentException("A shortcut needs a key after its modifiers.");
        return new(modifiers.ToArray(), (ushort)key);
    }
}

internal sealed class WindowsActions : IActionOutput
{
    public static readonly IReadOnlyDictionary<string, ushort> MediaKeys = new Dictionary<string, ushort>
    {
        ["Play / pause"] = 0xB3, ["Mute"] = 0xAD, ["Volume up"] = 0xAF, ["Volume down"] = 0xAE,
        ["Next track"] = 0xB0, ["Previous track"] = 0xB1, ["Stop"] = 0xB2
    };
    public void Execute(Mapping mapping)
    {
        switch (mapping.Kind)
        {
            case ActionKind.Shortcut:
                var shortcut = Shortcut.Parse(mapping.Value);
                var pressed = shortcut.Modifiers.Where(m => (Native.GetAsyncKeyState(m) & 0x8000) == 0).ToArray();
                Native.Send(pressed.Select(m => Native.Key(m)).Concat([Native.Key(shortcut.Key), Native.Key(shortcut.Key, true)]).Concat(pressed.Reverse().Select(m => Native.Key(m, true))).ToArray());
                break;
            case ActionKind.Text:
                Native.Send(mapping.Value.SelectMany(c => new[] { Native.Unicode(c), Native.Unicode(c, true) }).ToArray());
                break;
            case ActionKind.Media:
                ushort key = MediaKeys[mapping.Value];
                Native.Send([Native.Key(key), Native.Key(key, true)]);
                break;
            case ActionKind.Launch:
                Process.Start(new ProcessStartInfo(mapping.Value) { UseShellExecute = true });
                break;
            case ActionKind.Command:
                using (Process.Start(CommandLine.StartInfo(mapping))) { }
                break;
        }
    }
}

internal sealed class KeypadHook : IDisposable
{
    private readonly Native.HookProc callback;
    private readonly HashSet<uint> held = [];
    private nint handle;
    public bool Enabled { get; set; } = true;
    public event Action<ControlId>? Pressed;
    public KeypadHook()
    {
        callback = OnKey;
        handle = Native.SetWindowsHookEx(13, callback, Native.GetModuleHandle(null), 0);
        if (handle == 0) throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not listen for keypad input.");
    }
    private nint OnKey(int code, nint message, nint data)
    {
        if (code < 0) return Native.CallNextHookEx(handle, code, message, data);
        var key = Marshal.PtrToStructure<Native.KeyboardHookData>(data);
        if ((key.Flags & 0x10) != 0 || key.VirtualKey is < 0x7C or > 0x81) return Native.CallNextHookEx(handle, code, message, data);
        bool down = message == 0x100 || message == 0x104;
        bool up = message == 0x101 || message == 0x105;
        // Always consume a key-up for a key-down we consumed, even if paused in between.
        if (up && held.Remove(key.VirtualKey)) return 1;
        if (!Enabled) return Native.CallNextHookEx(handle, code, message, data);
        if (down && held.Add(key.VirtualKey)) Pressed?.Invoke((ControlId)(key.VirtualKey - 0x7C));
        return 1;
    }
    public void Dispose()
    {
        if (handle != 0) Native.UnhookWindowsHookEx(handle);
        handle = 0;
        GC.KeepAlive(callback);
    }
}

internal static class Native
{
    internal delegate nint HookProc(int code, nint message, nint data);
    [StructLayout(LayoutKind.Sequential)] internal struct KeyboardHookData { public uint VirtualKey, ScanCode, Flags, Time; public nuint ExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] internal struct Input { public uint Type; public InputUnion Data; }
    [StructLayout(LayoutKind.Explicit)] internal struct InputUnion { [FieldOffset(0)] public KeyboardInput Keyboard; [FieldOffset(0)] public MouseInput Mouse; }
    [StructLayout(LayoutKind.Sequential)] internal struct KeyboardInput { public ushort VirtualKey, ScanCode; public uint Flags, Time; public nuint ExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] internal struct MouseInput { public int X, Y; public uint MouseData, Flags, Time; public nuint ExtraInfo; }
    internal static Input Key(ushort key, bool up = false) => new() { Type = 1, Data = new() { Keyboard = new() { VirtualKey = key, Flags = up ? 2u : 0u } } };
    internal static Input Unicode(char character, bool up = false) => new() { Type = 1, Data = new() { Keyboard = new() { ScanCode = character, Flags = 4u | (up ? 2u : 0u) } } };
    internal static void Send(Input[] inputs)
    {
        if (inputs.Length == 0) return;
        if (SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>()) != inputs.Length)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows blocked this action. If the target app runs as administrator, Keypad Layers must run at the same permission level.");
    }
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, Input[] inputs, int size);
    [DllImport("user32.dll", SetLastError = true)] internal static extern nint SetWindowsHookEx(int id, HookProc callback, nint module, uint threadId);
    [DllImport("user32.dll")] internal static extern nint CallNextHookEx(nint hook, int code, nint message, nint data);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] internal static extern nint GetModuleHandle(string? name);
    [DllImport("user32.dll")] internal static extern short GetAsyncKeyState(int key);
}
