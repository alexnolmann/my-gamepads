using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.Xbox360;

// Only the identity actually observed on this PC is accepted.
static class Profile
{
    public static bool Accept(Device d) => d.Vid == 0x054c && (d.Pid == 0x05c4 || d.Pid == 0x09cc) && d.Length >= 10;
    public static bool Decode(ReadOnlySpan<byte> data, out PadState s)
    {
        s = default;
        int offset = data.Length > 0 ? data[0] switch { 1 => 1, 0x11 => 3, _ => -1 } : -1;
        if (offset < 0 || data.Length < offset + 9) return false;
        if (data[0] == 0x11 && (data.Length < 78 || System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(data[74..78]) != BluetoothCrc(data[..74]))) return false;
        var b = data[offset..];
        ushort buttons = 0;
        int hat = b[4] & 15;
        if (hat is 0 or 1 or 7) buttons |= 1;
        if (hat is 3 or 4 or 5) buttons |= 2;
        if (hat is 5 or 6 or 7) buttons |= 4;
        if (hat is 1 or 2 or 3) buttons |= 8;
        if ((b[4] & 0x20) != 0) buttons |= 0x1000; // Cross -> A
        if ((b[4] & 0x40) != 0) buttons |= 0x2000; // Circle -> B
        if ((b[4] & 0x10) != 0) buttons |= 0x4000; // Square -> X
        if ((b[4] & 0x80) != 0) buttons |= 0x8000; // Triangle -> Y
        if ((b[5] & 1) != 0) buttons |= 0x100;
        if ((b[5] & 2) != 0) buttons |= 0x200;
        if ((b[5] & 0x10) != 0) buttons |= 0x20;
        if ((b[5] & 0x20) != 0) buttons |= 0x10;
        if ((b[5] & 0x40) != 0) buttons |= 0x40;
        if ((b[5] & 0x80) != 0) buttons |= 0x80;
        if ((b[6] & 1) != 0) buttons |= 0x400;
        s = new(buttons, b[7], b[8], Axis(b[0]), Axis(b[1], true), Axis(b[2]), Axis(b[3], true));
        return true;
    }
    public static uint BluetoothCrc(ReadOnlySpan<byte> data)
    {
        uint crc = 0xffffffff;
        void Add(byte value) { crc ^= value; for (int bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xedb88320u : 0); }
        Add(0xa1); foreach (var b in data) Add(b);
        return ~crc;
    }
    static short Axis(byte b, bool invert = false)
    {
        double v = b >= 128 ? (b - 128) / 127.0 : (b - 128) / 128.0;
        v = Math.Abs(v) <= .08 ? 0 : Math.Sign(v) * (Math.Abs(v) - .08) / .92;
        if (invert) v = -v;
        return (short)Math.Round(v * (v < 0 ? 32768 : 32767));
    }
    public static void Submit(IXbox360Controller pad, PadState s)
    {
        pad.SetButtonsFull(s.Buttons);
        pad.SetSliderValue(Xbox360Slider.LeftTrigger, s.LT);
        pad.SetSliderValue(Xbox360Slider.RightTrigger, s.RT);
        pad.SetAxisValue(Xbox360Axis.LeftThumbX, s.LX);
        pad.SetAxisValue(Xbox360Axis.LeftThumbY, s.LY);
        pad.SetAxisValue(Xbox360Axis.RightThumbX, s.RX);
        pad.SetAxisValue(Xbox360Axis.RightThumbY, s.RY);
        pad.SubmitReport();
    }
}
readonly record struct PadState(ushort Buttons, byte LT, byte RT, short LX, short LY, short RX, short RY);
