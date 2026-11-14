using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace KeypadLayers;

internal sealed record DeviceInfo(string Path, string Name, ushort ReportLength)
{
    public override string ToString() => Name;
}

internal static class KeypadDevice
{
    // CH57x 1189:8890 protocol. Only a 3-key/one-knob unit is supported here.
    // Reference: https://github.com/kriomant/ch57x-keyboard-tool/blob/master/src/keyboard/k8890.rs
    internal static IEnumerable<byte[]> TriggerReports(int reportLength)
    {
        if (reportLength < 9 || reportLength > 1024) throw new InvalidDataException("Unsupported HID output report length.");
        byte[] ids = [1, 2, 3, 13, 14, 15];
        for (int control = 0; control < 6; control++)
        {
            byte[] start = new byte[reportLength];
            new byte[] { 3, 0xFE, 1, 1, 1 }.CopyTo(start, 0);
            yield return start;
            byte[] blank = new byte[reportLength];
            new byte[] { 3, ids[control], 0x11, 1, 0, 0, 0 }.CopyTo(blank, 0);
            yield return blank;
            byte[] key = new byte[reportLength];
            // USB keyboard usages F13-F18 are 0x68-0x6D.
            new byte[] { 3, ids[control], 0x11, 1, 1, 0, (byte)(0x68 + control) }.CopyTo(key, 0);
            yield return key;
            byte[] end = new byte[reportLength];
            new byte[] { 3, 0xAA, 0xAA }.CopyTo(end, 0);
            yield return end;
        }
    }

    public static List<DeviceInfo> Find()
    {
        var found = new List<DeviceInfo>();
        HidD_GetHidGuid(out var guid);
        nint set = SetupDiGetClassDevs(ref guid, null, 0, 0x12);
        if (set == -1) throw new Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            for (uint index = 0; ; index++)
            {
                var item = new InterfaceData { Size = Marshal.SizeOf<InterfaceData>() };
                if (!SetupDiEnumDeviceInterfaces(set, 0, ref guid, index, ref item))
                {
                    int error = Marshal.GetLastWin32Error();
                    if (error != 259) throw new Win32Exception(error);
                    break;
                }
                SetupDiGetDeviceInterfaceDetail(set, ref item, 0, 0, out uint required, 0);
                nint detail = Marshal.AllocHGlobal((int)required);
                try
                {
                    Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
                    if (!SetupDiGetDeviceInterfaceDetail(set, ref item, detail, required, out _, 0)) continue;
                    string path = Marshal.PtrToStringUni(detail + 4) ?? "";
                    if (!path.Contains("vid_1189&pid_8890", StringComparison.OrdinalIgnoreCase) || !path.Contains("&mi_01", StringComparison.OrdinalIgnoreCase)) continue;
                    using var handle = CreateFile(path, 0, 3, 0, 3, 0, 0);
                    if (handle.IsInvalid) continue;
                    var attributes = new Attributes { Size = Marshal.SizeOf<Attributes>() };
                    if (!HidD_GetAttributes(handle, ref attributes) || attributes.Vendor != 0x1189 || attributes.Product != 0x8890) continue;
                    if (!HidD_GetPreparsedData(handle, out nint preparsed)) continue;
                    try
                    {
                        if (HidP_GetCaps(preparsed, out var caps) != 0x110000 || caps.OutputLength < 9) continue;
                        found.Add(new(path, $"3 keys + 1 knob  ·  1189:8890  ·  device {found.Count + 1}", caps.OutputLength));
                    }
                    finally { HidD_FreePreparsedData(preparsed); }
                }
                finally { Marshal.FreeHGlobal(detail); }
            }
        }
        finally { SetupDiDestroyDeviceInfoList(set); }
        return found;
    }

    public static void Configure(DeviceInfo selected)
    {
        // Recheck identity and descriptors immediately before opening for writes.
        var actual = Find().SingleOrDefault(d => d.Path == selected.Path) ?? throw new IOException("The keypad disconnected. Reconnect it and scan again.");
        using var handle = CreateFile(actual.Path, 0x40000000, 3, 0, 3, 0, 0);
        if (handle.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot open the keypad. Close MINI KeyBoard and try again.");
        foreach (var report in TriggerReports(actual.ReportLength))
        {
            if (!WriteFile(handle, report, report.Length, out int written, 0) || written != report.Length)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Setup stopped during a USB write. Some controls may have changed. Reconnect and run setup again.");
            Thread.Sleep(30);
        }
    }

    [StructLayout(LayoutKind.Sequential)] private struct InterfaceData { public int Size; public Guid Guid; public uint Flags; public nuint Reserved; }
    [StructLayout(LayoutKind.Sequential)] private struct Attributes { public int Size; public ushort Vendor, Product, Version; }
    [StructLayout(LayoutKind.Sequential)] private struct Caps
    {
        public ushort Usage, UsagePage, InputLength, OutputLength, FeatureLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[] Reserved;
        public ushort LinkNodes, InputButtons, InputValues, InputData, OutputButtons, OutputValues, OutputData, FeatureButtons, FeatureValues, FeatureData;
    }
    [DllImport("hid.dll")] private static extern void HidD_GetHidGuid(out Guid guid);
    [DllImport("hid.dll")] [return: MarshalAs(UnmanagedType.U1)] private static extern bool HidD_GetAttributes(SafeFileHandle handle, ref Attributes attributes);
    [DllImport("hid.dll")] [return: MarshalAs(UnmanagedType.U1)] private static extern bool HidD_GetPreparsedData(SafeFileHandle handle, out nint data);
    [DllImport("hid.dll")] [return: MarshalAs(UnmanagedType.U1)] private static extern bool HidD_FreePreparsedData(nint data);
    [DllImport("hid.dll")] private static extern int HidP_GetCaps(nint data, out Caps caps);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern nint SetupDiGetClassDevs(ref Guid guid, string? enumerator, nint parent, uint flags);
    [DllImport("setupapi.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetupDiEnumDeviceInterfaces(nint set, nint device, ref Guid guid, uint index, ref InterfaceData data);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetupDiGetDeviceInterfaceDetail(nint set, ref InterfaceData data, nint detail, uint length, out uint required, nint device);
    [DllImport("setupapi.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetupDiDestroyDeviceInfoList(nint set);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern SafeFileHandle CreateFile(string path, uint access, uint share, nint security, uint creation, uint flags, nint template);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool WriteFile(SafeFileHandle handle, byte[] buffer, int length, out int written, nint overlapped);
}
