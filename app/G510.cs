using System;
using System.Globalization;

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
