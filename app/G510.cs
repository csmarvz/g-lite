using System;
using System.Globalization;
using System.Linq;

class G510Settings
{
    public const int Static = 0, Breathe = 1, Cycle = 2, MinPeriod = 1000, MaxPeriod = 20000;
    public static readonly string[] Effects = { "Off", "Static", "Breathe", "Cycle" };

    public bool On = true;
    public int Color = 0xFFFFFF, Bright = 100, Effect = Static, Period = 5000;

    public G510Settings Clone() { return (G510Settings)MemberwiseClone(); }

    public bool Same(G510Settings other)
    {
        return other != null && On == other.On && Color == other.Color && Bright == other.Bright && Effect == other.Effect
            && (Effect == Static || Period == other.Period);
    }

    public string ToText()
    {
        return (On ? 1 : 0) + "," + Color.ToString("X6") + "," + Bright + (Effect == Static ? "" : "," + Effect + "," + Period);
    }

    public static G510Settings FromText(string text)
    {
        var parts = (text ?? "").Split(',');
        int color, bright, effect = Static, period = 5000;
        if (parts.Length < 3 || !int.TryParse(parts[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out color)
            || !int.TryParse(parts[2], out bright))
            return null;
        if (parts.Length >= 5
            && (!int.TryParse(parts[3], out effect) || effect < Static || effect > Cycle || !int.TryParse(parts[4], out period)))
        {
            effect = Static;
            period = 5000;
        }
        return new G510Settings
        {
            On = parts[0] != "0", Color = color & 0xFFFFFF, Bright = Math.Max(1, Math.Min(100, bright)),
            Effect = effect, Period = Math.Max(MinPeriod, Math.Min(MaxPeriod, period))
        };
    }
}

// The G510 and G510s backlight: one colour for the whole keyboard in feature report 0x05 (`05 R G B`),
// with no brightness or effects of its own, so brightness scales the colour and G-Lite runs Breathe and Cycle
// by sending frames. From the Linux driver hid-lg-g15 (facts only) and an owner's report (docs/G510.md).
static class G510Lighting
{
    public const byte Backlight = 0x05;

    public static int Read(Hidpp hidpp)
    {
        var report = hidpp.GetFeature(Backlight);
        return report[1] << 16 | report[2] << 8 | report[3];
    }

    static int Scale(int color, double factor)
    {
        return (int)((color >> 16 & 0xFF) * factor) << 16 | (int)((color >> 8 & 0xFF) * factor) << 8 | (int)((color & 0xFF) * factor);
    }

    public static int Frame(G510Settings settings, double milliseconds)
    {
        if (!settings.On) return 0;
        double brightness = settings.Bright / 100.0, phase = milliseconds % settings.Period / settings.Period;
        if (settings.Effect == G510Settings.Breathe) return Scale(settings.Color, brightness * (0.5 - 0.5 * Math.Cos(2 * Math.PI * phase)));
        if (settings.Effect == G510Settings.Cycle) return Scale(LitraProtocol.Hue(phase), brightness);
        return Scale(settings.Color, brightness);
    }

    public static void Set(Hidpp hidpp, int color, bool quiet = false)
    {
        var report = new[] { Backlight, (byte)(color >> 16), (byte)(color >> 8), (byte)color };
        if (quiet) hidpp.SetFeatureUnlogged(report); else hidpp.SetFeature(report);
    }
}

// The G510's G keys and Game mode switch. Input report 0x03 (hid-lg-g15, facts only): G1–G8 in byte 1, G9–G16 in byte 2, G17 and
// G18 bits 0 and 1 of byte 3, the Game mode switch bit 2 (the keyboard turns its Windows keys off itself), M1–M3 and MR bits 4 to 7.
// The keyboard keeps no bindings: G-Lite keeps three sets, one per M key, as `gkeys.<device>.m1` to `.m3`, a binding (Buttons.cs's
// four bytes) per key in hex. Nothing reads the G keys yet, so the bindings do nothing until G-Lite listens to report 0x03.
static class G510Keys
{
    public const int Count = 18, Banks = 3;
    public const byte KeysReport = 0x03;
    public static readonly string[] Names = Enumerable.Range(1, Count).Select(number => "G" + number).ToArray();
    static readonly byte[] Unbound = { 0x80, 0x00, 0x00, 0x00 };

    public static byte[][] FromText(string text)
    {
        var parts = (text ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        return Enumerable.Range(0, Count).Select(i =>
        {
            uint value;
            return i < parts.Length && parts[i].Length == 8
                && uint.TryParse(parts[i], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value)
                ? new[] { (byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value }
                : (byte[])Unbound.Clone();
        }).ToArray();
    }

    public static string ToText(byte[][] bindings)
    {
        return string.Join(" ", bindings.Select(binding => string.Concat(binding.Select(part => part.ToString("X2")))));
    }

    // Feature 0x04 lights the M keys, one bit each: M1 0x80, M2 0x40, M3 0x20, MR 0x10 (hid-lg-g15; an owner's report reads 80).
    public const byte BankLights = 0x04;

    public static void LightBank(Hidpp hidpp, int bank) { hidpp.SetFeature(BankLights, (byte)(0x80 >> bank)); }

    // The M key lit, or fallback when none of M1 to M3 is.
    public static int LitBank(Hidpp hidpp, int fallback)
    {
        int mask = hidpp.GetFeature(BankLights)[1];
        for (int bank = 0; bank < Banks; bank++) if ((mask & 0x80 >> bank) != 0) return bank;
        return fallback;
    }

    // Asked for once (GET_REPORT), not listened to.
    public static bool GameMode(Hidpp hidpp)
    {
        var report = hidpp.GetInput(KeysReport);
        if (report.Length < 4) throw new System.IO.IOException("Input report " + KeysReport.ToString("X2") + " is too short");
        return (report[3] & 0x04) != 0;
    }
}
