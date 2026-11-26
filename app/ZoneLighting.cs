using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;

static class ZoneLighting
{
    public const int Place = -1;
    public static readonly string[] Names = { "Bottom left", "Bottom right", "Top left", "Top right" };
    public static readonly EffectInfo[] Effects = MouseProtocol.Effects.Concat(new[]
    {
        new EffectInfo
        {
            Id = 0x04, Name = "Wave", Speed = true, Direction = true,
            MinPeriod = 1000, MaxPeriod = 20000, DefaultPeriod = 5000
        }
    }).ToArray();
    public static readonly int[] Directions = { 6, 1, 3, 8 };
    public static readonly string[] DirText = { "Left", "Right", "Center out", "Center in" };

    // Drawn by G-Lite, not the keyboard: the loudness of the PC's sound sets each zone's level, sent as Static (ZonesPage.AudioTick).
    // The id is the per-key Audio effect's (LiveEffects), so a lighting text means the same effect on either screen.
    public static readonly EffectInfo Audio = new EffectInfo { Id = 0xE2, Name = "Audio", Color = true };

    // A live effect's id (LiveEffects) is known too, so a lighting text holding one, from a keyboard with 0x8080, reads back.
    public static EffectInfo EffectById(byte id)
    {
        return Effects.FirstOrDefault(effect => effect.Id == id) ?? (id == Audio.Id ? Audio : LiveEffects.Info(id));
    }

    // 0x8080, per-key colours on the G512 and G513 generation, from OpenLogi (MIT) and g810-led (GPL, facts only): fn 3 takes 00 01
    // (keys, by USB usage), the count as two bytes, then key R G B; three in a long report (g810-led and OpenLogi send 14 in a 64-byte
    // one, which G-Lite doesn't write). Neither waits for a reply to fn 3, so they're posted; fn 5 shows them and its reply is waited
    // for, after any to the colours. Untried on a keyboard.
    public static void SendKeys(Hidpp hidpp, Dictionary<int, int> colorByLightingId)
    {
        byte feature = hidpp.FeatureIndex(0x8080);
        var keys = colorByLightingId.Where(pair => GameModeKeys.Usage(pair.Key) >= 0).ToList();
        for (int at = 0; at < keys.Count; at += 3)
        {
            var batch = keys.Skip(at).Take(3).ToList();
            var parameters = new List<byte> { 0x00, 0x01, 0x00, (byte)batch.Count };
            foreach (var pair in batch)
                parameters.AddRange(new[]
                {
                    (byte)GameModeKeys.Usage(pair.Key), (byte)(pair.Value >> 16), (byte)(pair.Value >> 8), (byte)pair.Value
                });
            hidpp.PostUnlogged(feature, 3, parameters.ToArray());
        }
        hidpp.CallUnlogged(feature, 5);
    }

    // A keyboard's key map: with "kind": "zones", each key's 0x8070 zone in "ids" (the G213's zones were checked on a real one,
    // docs/G213.md); otherwise lighting ids, every key in zone 0 of a one-zone keyboard (the G413, drawn from a photo of it), which
    // also serve its Game view.
    public static string Picture(FoundDevice found)
    {
        if (found.Kind != "keyboard") return null;
        string name = found.ShortName.ToUpper();
        // The G512 and G513 have the G413's layout, from their photos (docs/keyboards-8070.md); the G PRO keyboard, G410, G810 and
        // G910 are drawn from theirs.
        return name == "G213" ? "g213.json"
            : name.StartsWith("G413") || name.StartsWith("G512") || name.StartsWith("G513") ? "g413.json"
            : name.StartsWith("G PRO") ? "g-pro.json"
            : name.StartsWith("G410") ? "g410.json"
            : name.StartsWith("G810") ? "g810.json"
            : name.StartsWith("G910") ? "g910.json"
            : null;
    }

    // Wave 0x04: OpenRGB's layout for the G915 (docs/G815-G915.md): period low at 6, direction 7, intensity 8, period high 9.
    public static byte[] Encode(MouseLight light)
    {
        if (light.Effect != 0x04) return MouseProtocol.Encode(light);
        var parameters = new byte[10];
        parameters[6] = (byte)light.Period;
        parameters[7] = (byte)light.Direction;
        parameters[8] = (byte)Math.Max(1, Math.Min(100, light.Bright));
        parameters[9] = (byte)(light.Period >> 8);
        return parameters;
    }

    public static Color ColorAt(MouseLight light, double x, double seconds)
    {
        if (light == null || light.Effect == 0x00) return Color.Black;
        var color = Color.FromArgb(light.Color >> 16 & 0xFF, light.Color >> 8 & 0xFF, light.Color & 0xFF);
        double period = Math.Max(1, light.Period) / 1000.0, brightness = Math.Max(1, Math.Min(100, light.Bright)) / 100.0;
        switch (light.Effect)
        {
            case 0x01: return KeyboardView.Scale(color, brightness);
            case 0x0A: return KeyboardView.Scale(color, brightness * (0.5 - 0.5 * Math.Cos(2 * Math.PI * seconds / period)));
            case 0x03: return KeyboardView.Scale(KeyboardView.Hsv(seconds / period, 1, 1), brightness);
            case 0x04:
                double offset = light.Direction == 1 ? -x
                    : light.Direction == 3 ? -Math.Abs(x - 0.5) * 2
                    : light.Direction == 8 ? Math.Abs(x - 0.5) * 2
                    : x;
                return KeyboardView.Scale(KeyboardView.Hsv(seconds / period + offset, 1, 1), brightness);
        }
        return Color.Black;
    }

    // 0x8070 locations: 1 the keys, 2 the logo, 6–11 primary 1 to 6 (the G213's five areas, see docs/G213.md).
    public static bool IsArea(int place) { return place >= 6 && place <= 11; }

    public static string ZoneName(int place, int index)
    {
        return place == 1 ? "Keys" : place == 2 ? "Logo" : IsArea(place) ? "Zone " + (place - 5) : "Zone " + (index + 1);
    }

    // fn 0 bytes 3–4 are the extended capabilities, and bit 3 says the LEDs have one colour (docs/lighting-control.md): set on a
    // G413 Silver, whose backlight is white (docs/reports/g413-c33a.txt), clear on the G213 and the G560.
    public static List<Dictionary<int, int>> Zones(Hidpp hidpp, out bool monochrome)
    {
        byte feature = hidpp.FeatureIndex(0x8070);
        var zones = new List<Dictionary<int, int>>();
        var device = hidpp.Call(feature, 0);
        monochrome = (device[4] & 0x08) != 0;
        int count = device[0];
        for (int zone = 0; zone < count; zone++)
        {
            var info = hidpp.Call(feature, 1, (byte)zone, 0xFF, 0);
            var map = new Dictionary<int, int>();
            map[Place] = info[1] << 8 | info[2];
            for (int effect = 0; effect < info[3]; effect++)
            {
                var reply = hidpp.Call(feature, 2, (byte)zone, (byte)effect, 0xFF, 0);
                map[reply[2] << 8 | reply[3]] = effect;
            }
            zones.Add(map);
        }
        return zones;
    }

    public static void SetLight(Hidpp hidpp, int zone, Dictionary<int, int> effects, MouseLight light, bool logged = true)
    {
        int index;
        if (!effects.TryGetValue(light.Effect, out index)) return;
        var parameters = new byte[] { (byte)zone, (byte)index }.Concat(Encode(light)).ToArray();
        if (logged) hidpp.Call(hidpp.FeatureIndex(0x8070), 3, parameters);
        else hidpp.CallUnlogged(hidpp.FeatureIndex(0x8070), 3, parameters);
    }

    public static void SetColor(Hidpp hidpp, int zone, Dictionary<int, int> effects, int color, int bright)
    {
        int index;
        if (!effects.TryGetValue(0x01, out index)) return;
        var light = new MouseLight { Effect = 0x01, Color = color, Bright = bright };
        hidpp.CallUnlogged(hidpp.FeatureIndex(0x8070), 3, new byte[] { (byte)zone, (byte)index }.Concat(Encode(light)).ToArray());
    }

    public static List<Zone> ScreenZones()
    {
        var areas = new[]
        {
            new RectangleF(0.05f, 0.55f, 0.4f, 0.35f), new RectangleF(0.55f, 0.55f, 0.4f, 0.35f),
            new RectangleF(0.05f, 0.1f, 0.4f, 0.35f), new RectangleF(0.55f, 0.1f, 0.4f, 0.35f)
        };
        return areas.Select((area, i) => new Zone { Name = Names[i], MarkColor = ScreenSettings.MarkColors[i], Area = area }).ToList();
    }

    public static ScreenSettings Screen(string text)
    {
        var setup = ScreenSettings.FromText(text);
        if (string.IsNullOrEmpty(text) || setup.Zones.Count != 4) setup.Zones = ScreenZones();
        for (int i = 0; i < 4; i++) setup.Zones[i].Name = Names[i];
        return setup;
    }

    public static string ToText(MouseLight[] lights)
    {
        return string.Join(";", lights.Select(light => light == null ? ""
            : string.Join(",", light.Effect, light.Color.ToString("X6"), light.Period, light.Bright)
                + (light.Effect == 0x04 || LiveEffects.Is(light.Effect) ? "," + light.Direction : "")));
    }

    public static MouseLight[] FromText(string text, int zones)
    {
        var parts = (text ?? "").Split(';');
        var lights = new MouseLight[zones];
        for (int zone = 0; zone < zones; zone++)
            lights[zone] = ParseLight(parts.Length == 1 ? parts[0] : zone < parts.Length ? parts[zone] : "");
        return lights.Any(light => light != null) ? lights : null;
    }

    static MouseLight ParseLight(string text)
    {
        var parts = text.Split(',');
        byte effect;
        int color, period, bright;
        if (parts.Length < 4 || !byte.TryParse(parts[0], out effect) || ZoneLighting.EffectById(effect) == null
            || !int.TryParse(parts[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out color)
            || !int.TryParse(parts[2], out period) || !int.TryParse(parts[3], out bright)) return null;
        var effectInfo = ZoneLighting.EffectById(effect);
        if (effectInfo.Speed) period = Math.Max(effectInfo.MinPeriod, Math.Min(effectInfo.MaxPeriod, period));
        int direction;
        var allowed = LiveEffects.Is(effect) ? LiveEffects.Directions : Directions;
        if (parts.Length < 5 || !int.TryParse(parts[4], out direction) || !allowed.Contains(direction))
            direction = LiveEffects.Is(effect) ? LiveEffects.DefaultDirection(effect) : 6;
        return new MouseLight
        {
            Effect = effect, Color = color & 0xFFFFFF, Period = period,
            Bright = Math.Max(1, Math.Min(100, bright)), Direction = direction
        };
    }
}
