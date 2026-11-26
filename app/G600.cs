using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

// The G600 doesn't speak HID++. Its vendor collection has feature report F0, the active mode and DPI level, and F3 to F5, one
// 154-byte report per mode. Layout from libratbag's driver-logitech-g600.c and tulth/g600prog (both MIT), which agree; see
// docs/G600.md. Read only until a write is tried on a real one.
class G600Mouse : IMouse
{
    public const byte ActiveReport = 0xF0, FirstModeReport = 0xF3;
    public const int ModeCount = 3, ReportLength = 154, ButtonCount = 20;
    const int ColorAt = 1, EffectAt = 4, DurationAt = 5, FrequencyAt = 11, DefaultLevelAt = 13, DpisAt = 14, LevelCount = 4,
        ButtonsAt = 31, ButtonLength = 3, DpiUnit = 50, MinDpi = 200, MaxDpi = 8200;
    static readonly int[] ShownRates = { 125, 250, 500, 1000 };

    public EffectInfo[] Effects { get { return MouseProtocol.Effects; } }
    public bool HasFactory { get { return false; } }

    public MouseProtocol.State Read(Hidpp hidpp)
    {
        var active = hidpp.GetFeature(ActiveReport);
        int mode = active[1] >> 4, level = active[1] >> 1 & 3;
        if (mode >= ModeCount) throw new IOException("The G600 reported mode " + (mode + 1) + " of " + ModeCount);
        var report = hidpp.GetFeature((byte)(FirstModeReport + mode));
        if (report.Length < ReportLength || report[0] != FirstModeReport + mode)
            throw new IOException("The G600 sent an unexpected report for mode " + (mode + 1));
        return Decode(report, level);
    }

    public static MouseProtocol.State Decode(byte[] report, int level)
    {
        // A level of 0 is off; the levels after it aren't used.
        var stages = Enumerable.Range(0, LevelCount).Select(i => report[DpisAt + i] * DpiUnit).TakeWhile(dpi => dpi > 0).ToArray();
        int stage = level < stages.Length ? level : -1;
        int rate = 1000 / (report[FrequencyAt] + 1);
        var state = new MouseProtocol.State
        {
            ReadOnly = true, Onboard = true, Profile = report,
            Stages = stages, Stage = stage, Dpi = stage >= 0 ? stages[stage] : stages.Length > 0 ? stages[0] : 0,
            Dpis = Enumerable.Range(MinDpi / DpiUnit, (MaxDpi - MinDpi) / DpiUnit + 1).Select(i => i * DpiUnit).ToArray(),
            Rate = rate, Rates = ShownRates.Concat(new[] { rate }).Distinct().OrderBy(hz => hz).ToArray(),
            ButtonCount = ButtonCount,
            Buttons = Enumerable.Range(0, ButtonCount).Select(i => Binding(report, ButtonsAt + ButtonLength * i)).ToArray(),
            Light = Light(report),
        };
        state.Zones[0] = new Dictionary<int, int>();
        return state;
    }

    // Solid in black is how libratbag turns the light off.
    static MouseLight Light(byte[] report)
    {
        int color = report[ColorAt] << 16 | report[ColorAt + 1] << 8 | report[ColorAt + 2];
        // libratbag caps the duration at 15 s; MouseProtocol.Effects' periods run from 1 to 20 s.
        int period = Math.Min(20000, Math.Max(1000, report[DurationAt] * 1000));
        switch (report[EffectAt])
        {
            case 0: return color == 0 ? new MouseLight { Effect = 0x00, Color = 0 } : new MouseLight { Effect = 0x01, Color = color };
            case 1: return new MouseLight { Effect = 0x0A, Color = color, Period = period };
            case 2: return new MouseLight { Effect = 0x03, Color = color, Period = period };
            default: return null;
        }
    }

    // A G600 button is a mouse code, then a modifier and a key that count when the code is 0. Shown as the HID++ binding that does the
    // same, so the Bindings view names it.
    public static byte[] Binding(byte[] report, int at)
    {
        byte code = report[at], modifiers = report[at + 1], key = report[at + 2];
        if (code >= 0x01 && code <= 0x07) return new byte[] { 0x80, 0x01, 0x00, (byte)(1 << (code - 1)) };
        int special;
        if (Specials.TryGetValue(code, out special)) return new byte[] { 0x90, (byte)special, 0x00, 0x00 };
        if (code == 0x00) return modifiers == 0 && key == 0 ? new byte[] { 0x80, 0x00, 0x00, 0x00 } : Bindings.Key(modifiers, key);
        return new byte[] { 0xA6, code, modifiers, key };
    }

    // DPI up, down, cycle; mode switch (the next mode, like Cycle profile); DPI shift, default DPI, G-shift.
    static readonly Dictionary<int, int> Specials = new Dictionary<int, int>
    {
        { 0x11, 0x03 }, { 0x12, 0x04 }, { 0x13, 0x05 }, { 0x14, 0x0A }, { 0x15, 0x07 }, { 0x16, 0x06 }, { 0x17, 0x0B },
    };

    static Exception ReadOnly() { return new RefusedException("G-Lite only reads the G600 for now"); }

    public void SendDpi(Hidpp hidpp, MouseProtocol.State state, MouseSettings want, MouseSettings sent) { throw ReadOnly(); }
    public void SetLight(Hidpp hidpp, MouseProtocol.State state, MouseLight light) { throw ReadOnly(); }
    public void SetRate(Hidpp hidpp, int hz) { throw ReadOnly(); }
    public void SetInvert(Hidpp hidpp, bool on) { throw new NotSupportedException(); }
    public byte[] ReadProfile(Hidpp hidpp, MouseProtocol.State state) { throw ReadOnly(); }
    public void Setup(Hidpp hidpp, MouseProtocol.State state, MouseSettings want) { throw ReadOnly(); }
    public void Save(Hidpp hidpp, MouseProtocol.State state, byte[] now, MouseSettings want) { throw ReadOnly(); }
    public void Restore(Hidpp hidpp, MouseProtocol.State state, byte[] now, byte[] data) { throw ReadOnly(); }
    public string Describe(byte[] profile) { return "mode " + (profile[0] - FirstModeReport + 1); }
    public int DefaultStage(byte[] profile) { return Math.Max(0, profile[DefaultLevelAt] - 1); }
}
