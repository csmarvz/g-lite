using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

// The G300 doesn't speak HID++ either: like the G600, feature reports on its FF80 collection. F0 holds the active profile (high
// nibble) and DPI level (bits 1-2); F3 to F5 one 35-byte report per profile. Layout and save from PeriHub (MIT,
// docs/protocols/logitech-g300.md), which saved on a real G300 (its issues #16 and #21), and libratbag's driver; see docs/G300.md.
// Nothing changes live: a change applies when the profile report is saved, then read back and compared.
class G300Mouse : IMouse
{
    public const byte ActiveReport = 0xF0, FirstProfileReport = 0xF3;
    public const int ProfileCount = 3, ReportLength = 35, ButtonCount = 9;
    const int LightAt = 1, RateAt = 2, LevelsAt = 3, LevelCount = 4, ButtonsAt = 8, ButtonLength = 3, DpiUnit = 250, MaxUnits = 10,
        DefaultFlag = 0x80;
    // By rate code: 00 is 1000 Hz.
    static readonly int[] RateByCode = { 1000, 125, 250, 500 };

    // Each of red, green and blue is on or off: Off, or Static in one of eight colours.
    static readonly EffectInfo[] OffAndStatic = MouseProtocol.Effects.Where(effect => effect.Id == 0x00 || effect.Id == 0x01).ToArray();
    public EffectInfo[] Effects { get { return OffAndStatic; } }
    public bool HasFactory { get { return false; } }

    // F0 also gives the level in use (bits 1-2), which isn't shown: the stage on the screen is the profile's default level, the one
    // Save writes, so a save doesn't move the default to whatever level the DPI buttons left the mouse on.
    public MouseProtocol.State Read(Hidpp hidpp)
    {
        var active = hidpp.GetFeature(ActiveReport);
        int profile = active[1] >> 4;
        if (profile >= ProfileCount) throw new IOException("The G300 reported profile " + (profile + 1) + " of " + ProfileCount);
        return Decode(ReadReport(hidpp, profile));
    }

    static byte[] ReadReport(Hidpp hidpp, int profile)
    {
        var report = hidpp.GetFeature((byte)(FirstProfileReport + profile));
        if (report.Length < ReportLength || report[0] != FirstProfileReport + profile)
            throw new IOException("The G300 sent an unexpected report for profile " + (profile + 1));
        return report.Take(ReportLength).ToArray();
    }

    public static MouseProtocol.State Decode(byte[] report)
    {
        var stages = Enumerable.Range(0, LevelCount).Select(i => (report[LevelsAt + i] & 0x7F) * DpiUnit).ToArray();
        int stage = DefaultStageOf(report);
        int rate = report[RateAt] < RateByCode.Length ? RateByCode[report[RateAt]] : 0;
        var state = new MouseProtocol.State
        {
            // The report id names the profile, for the backups.
            SaveOnly = true, Onboard = true, Profile = report, Sector = report[0], SectorSize = ReportLength,
            Stages = stages, Stage = stage, Dpi = stages[stage],
            Dpis = Enumerable.Range(1, MaxUnits).Select(i => i * DpiUnit).Concat(stages).Distinct().OrderBy(dpi => dpi).ToArray(),
            Rate = rate, Rates = RateByCode.OrderBy(hz => hz).ToArray(),
            ButtonCount = ButtonCount,
            Buttons = Enumerable.Range(0, ButtonCount).Select(i => Binding(report, ButtonsAt + ButtonLength * i)).ToArray(),
            Light = Light(report[LightAt]),
            Effects = OffAndStatic,
        };
        state.Zones[0] = new Dictionary<int, int>();
        return state;
    }

    static int DefaultStageOf(byte[] report)
    {
        for (int i = 0; i < LevelCount; i++) if ((report[LevelsAt + i] & DefaultFlag) != 0) return i;
        return 0;
    }

    static MouseLight Light(byte bits)
    {
        if ((bits & 7) == 0) return new MouseLight { Effect = 0x00, Color = 0xFFFFFF };
        return new MouseLight
        {
            Effect = 0x01,
            Color = ((bits & 1) != 0 ? 0xFF0000 : 0) | ((bits & 2) != 0 ? 0x00FF00 : 0) | ((bits & 4) != 0 ? 0x0000FF : 0)
        };
    }

    // The nearest of the eight: a channel is on from half the brightest one, so a dark red stays red. Brightness has no say (the
    // screen turns it off for this mouse); black, which has no hue, is white.
    public static int ColorBits(MouseLight light)
    {
        if (light.Effect == 0x00) return 0;
        int red = light.Color >> 16 & 0xFF, green = light.Color >> 8 & 0xFF, blue = light.Color & 0xFF;
        int brightest = Math.Max(red, Math.Max(green, blue));
        if (brightest == 0) return 7;
        return (red * 2 >= brightest ? 1 : 0) | (green * 2 >= brightest ? 2 : 0) | (blue * 2 >= brightest ? 4 : 0);
    }

    // A button is a function, then a modifier and a key that count when the function is 0. Shown as the HID++ binding that does the
    // same, so the Bindings view names it; Entry turns it back.
    public static byte[] Binding(byte[] report, int at)
    {
        byte code = report[at], modifiers = report[at + 1], key = report[at + 2];
        if (code >= 0x01 && code <= 0x09)
        {
            int mask = 1 << (code - 1);
            return new byte[] { 0x80, 0x01, (byte)(mask >> 8), (byte)mask };
        }
        int special;
        if (Specials.TryGetValue(code, out special)) return new byte[] { 0x90, (byte)special, 0x00, 0x00 };
        if (code == 0x00) return modifiers == 0 && key == 0 ? new byte[] { 0x80, 0x00, 0x00, 0x00 } : Bindings.Key(modifiers, key);
        return new byte[] { 0xA6, code, modifiers, key };
    }

    // The G300 entry for a binding, or null when the G300 has nothing for it.
    public static byte[] Entry(byte[] binding)
    {
        if (binding == null || binding.Length < 4) return null;
        if (binding[0] == 0x80 && binding[1] == 0x00) return new byte[3];
        if (binding[0] == 0x80 && binding[1] == 0x01)
        {
            int mask = binding[2] << 8 | binding[3];
            for (int code = 1; code <= 9; code++) if (mask == 1 << (code - 1)) return new byte[] { (byte)code, 0, 0 };
            return null;
        }
        if (binding[0] == 0x80 && binding[1] == 0x02) return new byte[] { 0x00, binding[2], binding[3] };
        if (binding[0] == 0x90)
        {
            // Cycle profile does what Next profile does with three profiles.
            int special = binding[1] == 0x0A ? 0x08 : binding[1];
            foreach (var pair in Specials) if (pair.Value == special) return new byte[] { (byte)pair.Key, 0, 0 };
        }
        if (binding[0] == 0xA6) return new byte[] { binding[1], binding[2], binding[3] };
        return null;
    }

    // DPI up, down, cycle; next profile; DPI shift; default DPI (not in the Bindings list, so kept but not offered).
    static readonly Dictionary<int, int> Specials = new Dictionary<int, int>
    {
        { 0x0A, 0x03 }, { 0x0B, 0x04 }, { 0x0C, 0x05 }, { 0x0D, 0x08 }, { 0x0E, 0x07 }, { 0x0F, 0x06 },
    };

    // The profile with what's wanted: the stage's DPI, that stage as the default (the one used after a profile change), the rate,
    // the colour and buttons 3 to 9. The left and right buttons keep theirs, as in LGS and PeriHub. The light byte's other bits and
    // the DPI shift stay as read, and so does a button whose binding didn't change.
    public static byte[] Build(byte[] old, MouseSettings want)
    {
        var built = (byte[])old.Clone();
        int stage = want.Stage >= 0 && want.Stage < LevelCount ? want.Stage : DefaultStageOf(built);
        int units = want.Dpi / DpiUnit;
        // A level's DPI as read stays, even outside 250 to 2500, so a save that changes something else isn't refused.
        if (want.Dpi != (old[LevelsAt + stage] & 0x7F) * DpiUnit)
        {
            if (units < 1 || units > MaxUnits || units * DpiUnit != want.Dpi)
                throw new RefusedException("The G300 takes 250 to 2500 DPI in steps of 250");
            built[LevelsAt + stage] = (byte)units;
        }
        for (int i = 0; i < LevelCount; i++) built[LevelsAt + i] = (byte)(built[LevelsAt + i] & 0x7F | (i == stage ? DefaultFlag : 0));
        if (want.Rate > 0)
        {
            int code = Array.IndexOf(RateByCode, want.Rate);
            if (code < 0) throw new RefusedException("The G300 takes 125, 250, 500 or 1000 Hz");
            built[RateAt] = (byte)code;
        }
        if (want.Light != null) built[LightAt] = (byte)(built[LightAt] & ~7 | ColorBits(want.Light));
        if (want.Buttons != null)
            for (int i = 0; i < Math.Min(ButtonCount, want.Buttons.Length); i++)
            {
                int at = ButtonsAt + ButtonLength * i;
                if (Binding(old, at).SequenceEqual(want.Buttons[i])) continue;
                if (i < 2) throw new RefusedException("The G300's left and right buttons keep what they do");
                var entry = Entry(want.Buttons[i]);
                if (entry == null) throw new RefusedException("Button " + (i + 1) + ": the G300 can't do that");
                entry.CopyTo(built, at);
            }
        return built;
    }

    // One write of the whole report, as libratbag and PeriHub do, then read back: PeriHub waits half a second, and reads a few times
    // since the mouse is slow to take it.
    static void Write(Hidpp hidpp, byte[] report)
    {
        if (report.Length != ReportLength || report[0] < FirstProfileReport || report[0] >= FirstProfileReport + ProfileCount)
            throw new RefusedException("Not a G300 profile");
        hidpp.SetFeature(report);
        for (int attempt = 0; attempt < 3; attempt++)
        {
            Thread.Sleep(attempt == 0 ? 500 : 100);
            if (ReadReport(hidpp, report[0] - FirstProfileReport).SequenceEqual(report)) return;
        }
        throw new IOException("The G300 reads back something else than what was saved");
    }

    public void SendDpi(Hidpp hidpp, MouseProtocol.State state, MouseSettings want, MouseSettings sent) { }
    public void SetLight(Hidpp hidpp, MouseProtocol.State state, MouseLight light) { }
    public void SetRate(Hidpp hidpp, int hz) { }
    public void SetInvert(Hidpp hidpp, bool on) { throw new NotSupportedException(); }
    public byte[] ReadProfile(Hidpp hidpp, MouseProtocol.State state) { return ReadReport(hidpp, state.Profile[0] - FirstProfileReport); }
    public void Setup(Hidpp hidpp, MouseProtocol.State state, MouseSettings want) { throw new RefusedException("The G300 needs no setup"); }
    // The mouse's profile button may have moved it to another profile since the screen read this one, and Save is for the one in use.
    public void Save(Hidpp hidpp, MouseProtocol.State state, byte[] now, MouseSettings want)
    {
        int active = hidpp.GetFeature(ActiveReport)[1] >> 4;
        if (FirstProfileReport + active != now[0])
            throw new RefusedException("The G300 is on profile " + (active + 1) + " now, not the one shown: nothing was saved");
        Write(hidpp, Build(now, want));
    }
    public void Restore(Hidpp hidpp, MouseProtocol.State state, byte[] now, byte[] data)
    {
        if (data == null) throw new RefusedException("The G300 has no factory profile to restore");
        Write(hidpp, data);
    }
    public string Describe(byte[] profile) { return "profile " + (profile[0] - FirstProfileReport + 1); }
    public int DefaultStage(byte[] profile) { return DefaultStageOf(profile); }
}
