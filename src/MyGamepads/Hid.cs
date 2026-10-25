using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

static class Hid
{
    static readonly Dictionary<string, Device> known = new(StringComparer.OrdinalIgnoreCase);
    [StructLayout(LayoutKind.Sequential)] struct Interface { public int Size; public Guid Guid; public int Flags; public nint Reserved; }
    [StructLayout(LayoutKind.Sequential)] struct Attributes { public int Size; public ushort Vid, Pid, Version; }
    [DllImport("hid.dll")] static extern void HidD_GetHidGuid(out Guid guid);
    [DllImport("hid.dll")] static extern bool HidD_GetAttributes(SafeFileHandle h, ref Attributes a);
    [DllImport("hid.dll")] static extern bool HidD_GetProductString(SafeFileHandle h, byte[] b, int length);
    [DllImport("hid.dll")] static extern bool HidD_GetPreparsedData(SafeFileHandle h, out nint p);
    [DllImport("hid.dll")] static extern bool HidD_FreePreparsedData(nint p);
    [DllImport("hid.dll")] static extern int HidP_GetCaps(nint p, nint caps);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode)] static extern nint SetupDiGetClassDevs(ref Guid g, string? e, nint w, uint flags);
    [DllImport("setupapi.dll")] static extern bool SetupDiEnumDeviceInterfaces(nint set, nint dev, ref Guid guid, uint index, ref Interface data);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool SetupDiGetDeviceInterfaceDetail(nint set, ref Interface data, nint detail, uint size, out uint required, nint dev);
    [DllImport("setupapi.dll")] static extern bool SetupDiDestroyDeviceInfoList(nint set);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] public static extern SafeFileHandle CreateFile(string path, uint access, uint share, nint security, uint creation, uint flags, nint template);
    public static List<Device> Enumerate(bool all = false)
    {
        HidD_GetHidGuid(out var guid); var set = SetupDiGetClassDevs(ref guid, null, 0, 0x12);
        var result = new List<Device>();
        try
        {
            for (uint i = 0; ; i++)
            {
                var info = new Interface { Size = Marshal.SizeOf<Interface>() };
                if (!SetupDiEnumDeviceInterfaces(set, 0, ref guid, i, ref info)) break;
                SetupDiGetDeviceInterfaceDetail(set, ref info, 0, 0, out var size, 0);
                var detail = Marshal.AllocHGlobal((int)size);
                try
                {
                    Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
                    if (!SetupDiGetDeviceInterfaceDetail(set, ref info, detail, size, out _, 0)) continue;
                    var path = Marshal.PtrToStringUni(detail + 4)!;
                    if (!all && !(path.Contains("054c", StringComparison.OrdinalIgnoreCase) && (path.Contains("05c4", StringComparison.OrdinalIgnoreCase) || path.Contains("09cc", StringComparison.OrdinalIgnoreCase)))) continue;
                    if (known.TryGetValue(path, out var existing)) { result.Add(existing); continue; }
                    using var h = CreateFile(path, 0, 3, 0, 3, 0, 0);
                    if (h.IsInvalid) { if (known.TryGetValue(path, out var cached)) result.Add(cached); continue; }
                    var a = new Attributes { Size = Marshal.SizeOf<Attributes>() };
                    if (!HidD_GetAttributes(h, ref a)) continue;
                    var name = new byte[256]; HidD_GetProductString(h, name, name.Length);
                    ushort length = 0;
                    if (HidD_GetPreparsedData(h, out var p))
                    {
                        var caps = Marshal.AllocHGlobal(64);
                        try { if (HidP_GetCaps(p, caps) >= 0) length = (ushort)Marshal.ReadInt16(caps, 4); }
                        finally { Marshal.FreeHGlobal(caps); HidD_FreePreparsedData(p); }
                    }
                    var device = new Device(path, a.Vid, a.Pid, length, System.Text.Encoding.Unicode.GetString(name).TrimEnd('\0'));
                    result.Add(device); known[path] = device;
                }
                finally { Marshal.FreeHGlobal(detail); }
            }
        }
        finally { SetupDiDestroyDeviceInfoList(set); }
        return result;
    }
}
record Device(string Path, ushort Vid, ushort Pid, ushort Length, string Name);
