using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;

class LightRange
{
    public int Min, Max, Step;

    public int Snap(int value) { return Math.Max(Min, Math.Min(Max, Min + (int)Math.Round((value - Min) / (double)Step) * Step)); }
}

class LightState
{
    public const int Zones = 7, Static = 0, Breathe = 1, Cycle = 2, Wave = 3;
    public static readonly string[] Effects = { "Static", "Breathe", "Cycle", "Wave" };
    public const int MinPeriod = 1000, MaxPeriod = 20000;
    public bool FrontOn, BackOn = true;
    public int Lumen, Kelvin, BackLevel = 100, Effect, Period = 5000;
    public int[] Colors = Enumerable.Repeat(0xFFFFFF, Zones).ToArray();

    public LightState Clone()
    {
        var copy = (LightState)MemberwiseClone();
        copy.Colors = (int[])Colors.Clone();
        return copy;
    }

    public string FrontText() { return (FrontOn ? 1 : 0) + "," + Lumen + "," + Kelvin; }

    public string BackText()
    {
        return (BackOn ? 1 : 0) + "," + BackLevel + "," + string.Join("/", Colors.Select(color => color.ToString("X6")))
            + "," + Effect + "," + Period;
    }

    public bool ReadFront(string text)
    {
        var parts = (text ?? "").Split(',');
        int lumen, kelvin;
        if (parts.Length != 3 || !int.TryParse(parts[1], out lumen) || !int.TryParse(parts[2], out kelvin)) return false;
        FrontOn = parts[0] == "1";
        Lumen = lumen;
        Kelvin = kelvin;
        return true;
    }

    public bool ReadBack(string text)
    {
        var parts = (text ?? "").Split(',');
        int level, effect = Static, period = 5000;
        if ((parts.Length != 3 && parts.Length != 5) || !int.TryParse(parts[1], out level)) return false;
        if (parts.Length == 5
            && (!int.TryParse(parts[3], out effect) || !int.TryParse(parts[4], out period) || effect < 0 || effect >= Effects.Length))
            return false;
        var colors = parts[2].Split('/');
        if (colors.Length != Zones) return false;
        var parsed = new int[Zones];
        for (int i = 0; i < Zones; i++)
            if (!int.TryParse(colors[i], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out parsed[i])) return false;
        BackOn = parts[0] == "1";
        BackLevel = Math.Max(1, Math.Min(100, level));
        Colors = parsed.Select(color => color & 0xFFFFFF).ToArray();
        Effect = effect;
        Period = Math.Max(MinPeriod, Math.Min(MaxPeriod, period));
        return true;
    }
}

static class LitraProtocol
{
    static byte FrontLightFeature(Hidpp hidpp) { return hidpp.FeatureIndex(0x1990); }

    public static bool FrontOn(Hidpp hidpp) { return (hidpp.Call(FrontLightFeature(hidpp), 0)[0] & 1) != 0; }

    public static void SetFrontOn(Hidpp hidpp, bool on) { hidpp.Call(FrontLightFeature(hidpp), 1, (byte)(on ? 1 : 0)); }

    public static LightRange Range(Hidpp hidpp, int fn, int low, int high, int step, int lowest, int highest)
    {
        var reply = hidpp.Call(FrontLightFeature(hidpp), fn);
        int min = reply[1] << 8 | reply[2], max = reply[3] << 8 | reply[4], resolution = reply[5] << 8 | reply[6];
        if (min < lowest || max > highest || max <= min) return new LightRange { Min = low, Max = high, Step = step };
        return new LightRange { Min = min, Max = max, Step = resolution > 0 && resolution <= (max - min) ? resolution : 1 };
    }

    public static LightRange Lumens(Hidpp hidpp) { return Range(hidpp, 2, 30, 400, 1, 1, 5000); }

    public static LightRange Kelvins(Hidpp hidpp) { return Range(hidpp, 7, 2700, 6500, 100, 1000, 12000); }

    public static int Get(Hidpp hidpp, int fn) { var reply = hidpp.Call(FrontLightFeature(hidpp), fn); return reply[0] << 8 | reply[1]; }

    public static void Set(Hidpp hidpp, int fn, int value) { hidpp.Call(FrontLightFeature(hidpp), fn, (byte)(value >> 8), (byte)value); }

    public static int BackLevel(Hidpp hidpp) { var reply = hidpp.Call(hidpp.FeatureIndex(0x8040), 1); return reply[0] << 8 | reply[1]; }

    public static void SetBackLevel(Hidpp hidpp, int level) { hidpp.Call(hidpp.FeatureIndex(0x8040), 2, 0, (byte)level); }

    public static bool? BackOn(Hidpp hidpp)
    {
        try { return (hidpp.Call(hidpp.FeatureIndex(0x8040), 3)[0] & 1) != 0; }
        catch (HidppError) { return null; }
    }

    public static void SetBackOn(Hidpp hidpp, bool on)
    {
        try { hidpp.Call(hidpp.FeatureIndex(0x8040), 4, (byte)(on ? 1 : 0)); }
        catch (HidppError) { }
    }

    public static void Colors(Hidpp hidpp, int[] colors, bool quiet)
    {
        byte feature = hidpp.FeatureIndex(0x8081);
        for (int start = 0; start < colors.Length; start += 4)
        {
            var parameters = new List<byte>();
            for (int i = start; i < start + 4; i++)
            {
                if (i >= colors.Length) { parameters.AddRange(new byte[] { 0xFF, 0, 0, 0 }); continue; }
                int color = colors[i];
                parameters.Add((byte)(i + 1));
                parameters.AddRange(new[] { color >> 16 & 0xFF, color >> 8 & 0xFF, color & 0xFF }
                    .Select(channel => (byte)Math.Max(1, channel)));
            }
            if (quiet) hidpp.CallUnlogged(feature, 1, parameters.ToArray()); else hidpp.Call(feature, 1, parameters.ToArray());
        }
        if (quiet) hidpp.CallUnlogged(feature, 7, 0, 0, 1, 0, 0); else hidpp.Call(feature, 7, 0, 0, 1, 0, 0);
    }

    public static string EffectName(byte id)
    {
        switch (id)
        {
            case 0x00: return "Off";
            case 0x01: return "Static";
            case 0x03: return "Colour cycle";
            case 0x04: return "Colour wave";
            case 0x0A: return "Breathe";
            case 0x13: return "Streaming";
            case 0x15: return "Smooth colour cycle";
            case 0x16: return "Smooth colour wave";
            case 0x18: return "Two colours";
        }
        return "Effect " + id.ToString("X2");
    }

    public static byte[] EffectBytes(int group, int index, byte id, LightState state)
    {
        var parameters = new byte[13];
        parameters[0] = (byte)group;
        parameters[1] = (byte)index;
        int color = state.Colors[0], other = state.Colors[LightState.Zones - 1], period = state.Period;
        // The keyboards' encoding: smooth cycle and wave (0x15, 0x16) take 00 FF, the period at bytes 6–7 and the intensity at 8,
        // not the older cycle's layout, which the Beam LX ignored (tried on a real one, 2026-11).
        if (id == 0x01 || id == 0x03 || id == 0x04 || id == 0x0A || id == 0x0B || id == 0x15 || id == 0x16)
            Array.Copy(G915Lighting.Encode(id, new Lighting { Effect = id, Color = color, Period = period }), 0, parameters, 2, 10);
        else
        {
            parameters[2] = (byte)(color >> 16); parameters[3] = (byte)(color >> 8); parameters[4] = (byte)color;
            // Two colours, the period at parameters 7–8 and the intensity at 9 (spec x8071 v4, effect 24);
            // the light's own default period is 3000 ms.
            if (id == 0x18)
            {
                parameters[5] = (byte)(other >> 16); parameters[6] = (byte)(other >> 8); parameters[7] = (byte)other;
                parameters[8] = (byte)(period >> 8); parameters[9] = (byte)period;
                parameters[10] = 100;
            }
        }
        parameters[12] = 1;
        return parameters;
    }

    // 0x8071 fn 1 needs software control of the clusters (fn 5 bit 0) first: without it the Beam LX answers 0x05
    // (its report, docs/lighting-control.md).
    public static void TryEffect(Hidpp hidpp, int group, int index, byte id, LightState state)
    {
        hidpp.Call(hidpp.FeatureIndex(0x8071), 5, 0x01, 0x01, 0x00);
        hidpp.Call(hidpp.FeatureIndex(0x8071), 1, EffectBytes(group, index, id, state));
    }

    public static void Release(Hidpp hidpp)
    {
        try { hidpp.Call(hidpp.FeatureIndex(0x8071), 5, 0x01, 0x00, 0x00); }
        catch (HidppError) { }
    }

    public static int[] Frame(LightState state, double milliseconds)
    {
        double phase = milliseconds % state.Period / state.Period;
        var frame = new int[LightState.Zones];
        for (int zone = 0; zone < frame.Length; zone++)
        {
            if (state.Effect == LightState.Breathe) frame[zone] = Scale(state.Colors[zone], 0.5 - 0.5 * Math.Cos(2 * Math.PI * phase));
            else if (state.Effect == LightState.Cycle) frame[zone] = Hue(phase);
            else if (state.Effect == LightState.Wave) frame[zone] = Hue(phase - zone / (double)frame.Length);
            else frame[zone] = state.Colors[zone];
        }
        return frame;
    }

    static int Scale(int color, double factor)
    {
        return (int)Math.Round((color >> 16 & 0xFF) * factor) << 16
            | (int)Math.Round((color >> 8 & 0xFF) * factor) << 8
            | (int)Math.Round((color & 0xFF) * factor);
    }

    public static int Hue(double turn)
    {
        double position = (turn % 1 + 1) % 1 * 6;
        int sector = (int)position % 6;
        int rise = (int)Math.Round((position - Math.Floor(position)) * 255), fall = 255 - rise;
        var rgb = new[]
        {
            new[] { 255, rise, 0 }, new[] { fall, 255, 0 }, new[] { 0, 255, rise },
            new[] { 0, fall, 255 }, new[] { rise, 0, 255 }, new[] { 255, 0, fall }
        }[sector];
        return rgb[0] << 16 | rgb[1] << 8 | rgb[2];
    }

    public static List<Zone> ScreenZones()
    {
        return Enumerable.Range(0, LightState.Zones)
            .Select(i => new Zone
            {
                Name = (i + 1).ToString(),
                MarkColor = ScreenSettings.MarkColors[i],
                Area = new RectangleF((float)Math.Round(i / 7.0 + 0.01, 3), 0.1f, (float)Math.Round(1 / 7.0 - 0.02, 3), 0.8f)
            })
            .ToList();
    }

    public static ScreenSettings Screen(string text)
    {
        var setup = ScreenSettings.FromText(text);
        if (string.IsNullOrEmpty(text) || setup.Zones.Count != LightState.Zones) setup.Zones = ScreenZones();
        return setup;
    }
}
