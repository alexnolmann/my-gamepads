using System.Runtime.InteropServices;
using System.Text.Json;
using Nefarius.ViGEm.Client;

static class Diagnostics
{
    public static async Task Probe()
    {
        var result = new List<object>();
        var devices = Hid.Enumerate();
        foreach (var d in devices.Where(Profile.Accept))
        {
            using var h = Hid.CreateFile(d.Path, 0xC0000000, 3, 0, 3, 0x40000000, 0);
            if (h.IsInvalid) { result.Add(new { d.Path, Error = Marshal.GetLastWin32Error() }); continue; }
            var b = new byte[d.Length];
            result.Add(new { d.Path, d.Name, d.Length });
            using var stream = new FileStream(h, FileAccess.ReadWrite, d.Length, true);
            using var ct = new CancellationTokenSource(3000);
            try { var n = await stream.ReadAsync(b, ct.Token); result.Add(new { StreamLength = n, Data = Convert.ToHexString(b.AsSpan(0, n)) }); }
            catch (Exception e) { result.Add(new { StreamError = e.Message }); }
        }
        File.WriteAllText(AppPaths.FilePath("probe.json"), JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
    }
    [StructLayout(LayoutKind.Sequential)] public struct XState { public uint Packet; public ushort Buttons; public byte LT, RT; public short LX, LY, RX, RY; }
    [DllImport("xinput1_4.dll")] static extern uint XInputGetState(uint index, out XState state);
    static PadState ToPad(XState s) => new(s.Buttons, s.LT, s.RT, s.LX, s.LY, s.RX, s.RY);
    public static async Task Run()
    {
        var result = new List<object>();
        var devices = Hid.Enumerate().Where(Profile.Accept).ToArray();
        result.Add(new { Devices = devices.Select(d => new { VID = d.Vid.ToString("X4"), PID = d.Pid.ToString("X4"), d.Name, d.Length }).ToArray() });
        try
        {
            using (var client = new ViGEmClient())
            {
                var a = client.CreateXbox360Controller(); var b = client.CreateXbox360Controller();
                a.AutoSubmitReport = b.AutoSubmitReport = false;
                bool ac = false, bc = false;
                try
                {
                    a.Connect(); ac = true; b.Connect(); bc = true;
                    var first = new PadState(0x1000, 31, 73, 12345, -23456, 7890, -1234);
                    var second = new PadState(0x2000, 98, 12, -3210, 7654, -12789, 23456);
                    Profile.Submit(a, first); Profile.Submit(b, second); await Task.Delay(800);
                    var matches = new List<int>();
                    for (uint i = 0; i < 4; i++) if (XInputGetState(i, out var s) == 0) { var p = ToPad(s); if (p == first || p == second) matches.Add((int)i); result.Add(new { XInputSlot = i, State = p }); }
                    if (matches.Count != 2) throw new Exception("Два независимых XInput-устройства не подтверждены");
                    result.Add(new { VirtualPair = "PASS", Slots = matches });
                    for (int j = 0; j < 20; j++) { Profile.Submit(a, default); Profile.Submit(b, default); await Task.Delay(20); }
                    foreach (int slot in matches) { var code = XInputGetState((uint)slot, out var zero); result.Add(new { NeutralAfterChange = slot, ErrorCode = code, State = ToPad(zero) }); if (code != 0 || ToPad(zero) != default) throw new Exception("Нейтраль виртуального контроллера не подтверждена"); }
                }
                finally { if (ac) { Profile.Submit(a, default); a.Disconnect(); } if (bc) { Profile.Submit(b, default); b.Disconnect(); } }
            }
            await Task.Delay(2500);
            await using (var bridge = new Bridge())
            {
                bridge.Start(); await Task.Delay(7000);
                var snapshots = bridge.Snapshot();
                result.Add(new { Live = snapshots });
                int confirmed = 0;
                var available = new Dictionary<uint, PadState>();
                for (uint i = 0; i < 4; i++) { var error = XInputGetState(i, out var xs); if (error == 0) available[i] = ToPad(xs); result.Add(new { LiveXInputSlot = i, ErrorCode = error, State = error == 0 ? ToPad(xs) : default }); }
                foreach (var p in snapshots.Where(p => p.Reports > 0))
                {
                    var match = available.Where(k => k.Value == p.State).Select(k => (uint?)k.Key).FirstOrDefault();
                    if (match.HasValue) { available.Remove(match.Value); confirmed++; }
                }
                result.Add(new { PhysicalReporting = snapshots.Count(p => p.Reports > 0), XInputMatched = confirmed });
                if (devices.Length > 0 && confirmed != devices.Length) throw new Exception("Не все физические геймпады подтверждены в XInput");
            }
        }
        catch (Exception e) { result.Add(new { Error = e.ToString() }); Environment.ExitCode = 1; }
        await Task.Delay(400);
        var remaining = new List<uint>();
        for (uint i = 0; i < 4; i++) if (XInputGetState(i, out _) == 0) remaining.Add(i);
        result.Add(new { RemainingXInputSlots = remaining });
        File.WriteAllText(AppPaths.FilePath("diagnostics.json"), JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true, IncludeFields = true }));
    }
    public static void Test()
    {
        int assertions = 0;
        void Check(bool ok, string label) { assertions++; if (!ok) throw new Exception(label); }
        byte[] neutral = [1, 128, 128, 128, 128, 8, 0, 0, 0, 0];
        Check(Profile.Decode(neutral, out var s) && s == default, "neutral");
        foreach (var (mask, expected) in new[] { (0x10, 0x4000), (0x20, 0x1000), (0x40, 0x2000), (0x80, 0x8000) })
        { var b = (byte[])neutral.Clone(); b[5] |= (byte)mask; Check(Profile.Decode(b, out s) && s.Buttons == expected, "face"); }
        ushort[] hats = [1, 9, 8, 10, 2, 6, 4, 5, 0];
        for (int i = 0; i < hats.Length; i++) { var b = (byte[])neutral.Clone(); b[5] = (byte)i; Check(Profile.Decode(b, out s) && s.Buttons == hats[i], "hat"); }
        ushort[] extras = [0x100, 0x200, 0, 0, 0x20, 0x10, 0x40, 0x80];
        for (int i = 0; i < 8; i++) { var b = (byte[])neutral.Clone(); b[6] = (byte)(1 << i); Check(Profile.Decode(b, out s) && s.Buttons == extras[i], "shoulder/start/stick"); }
        var edge = (byte[])neutral.Clone(); edge[1] = 0; edge[2] = 0; edge[3] = 255; edge[4] = 255; edge[8] = 255; edge[9] = 127;
        Check(Profile.Decode(edge, out s) && s.LX == -32768 && s.LY == 32767 && s.RX == 32767 && s.RY == -32768 && s.LT == 255 && s.RT == 127, "ranges");
        var bt = new byte[78]; bt[0] = 0x11; neutral.AsSpan(1).CopyTo(bt.AsSpan(3)); System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(bt.AsSpan(74), Profile.BluetoothCrc(bt.AsSpan(0, 74))); Check(Profile.Decode(bt, out s) && s == default, "bluetooth");
        bt[3] ^= 1; Check(!Profile.Decode(bt, out _), "bluetooth CRC rejection");
        Check(!Profile.Decode(new byte[] { 0x99, 0, 0 }, out _), "unknown"); Check(!Profile.Decode(new byte[] { 1 }, out _), "short");
        File.WriteAllText(AppPaths.FilePath("tests.txt"), $"PASS: {assertions} assertions\n");
    }
}

