using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

class OtherSettings
{
    // Away, Near and Powered are the backlight's fade delays in seconds: hands away from the keys, hands near them, on external power.
    public const int Bright = 0, Dpi = 1, Shift = 2, Invert = 3, Thumb = 4, Light = 5, Auto = 6, Level = 7,
        Away = 8, Near = 9, Powered = 10, Count = 11;
    public static readonly int[] Delays = { Away, Near, Powered };
    static readonly string[] Keys = { "bright", "dpi", "shift", "invert", "thumb", "light", "auto", "level", "away", "near", "powered" };

    // -1 is a setting the device doesn't have, or one this set leaves alone. Shift is SmartShift's threshold: 1–50, or 255 when it's off
    // (the wheel never switches to free spin).
    readonly int[] values = Enumerable.Repeat(-1, Count).ToArray();

    public int this[int setting] { get { return values[setting]; } set { values[setting] = value; } }

    public OtherSettings Clone()
    {
        var copy = new OtherSettings();
        values.CopyTo(copy.values, 0);
        return copy;
    }

    public void Merge(OtherSettings over)
    {
        for (int setting = 0; setting < Count; setting++) if (over[setting] >= 0) values[setting] = over[setting];
    }

    public string ToText()
    {
        return string.Join(",", Enumerable.Range(0, Count)
            .Where(setting => values[setting] >= 0)
            .Select(setting => Keys[setting] + ":" + values[setting]));
    }

    public static OtherSettings FromText(string text)
    {
        var set = new OtherSettings();
        foreach (var part in (text ?? "").Split(','))
        {
            var pair = part.Split(':');
            int number, setting = pair.Length == 2 ? Array.IndexOf(Keys, pair[0]) : -1;
            if (setting >= 0 && int.TryParse(pair[1], out number) && number >= 0) set[setting] = number;
        }
        return set;
    }

    public bool Same(OtherSettings other) { return ToText() == other.ToText(); }
}

// The settings of Logitech devices that aren't G devices. Bytes from Solaar's settings, OpenLogi's captures of an MX Master 3S
// and MX Keys S, and the MX Creative Keypad's report. See docs/other-devices.md.
static class OtherDevice
{
    // 0x1814 alone doesn't get the screen: a device with only Easy-Switch has nothing else to set.
    public static readonly int[] Features = { 0x8040, 0x2201, 0x2110, 0x2111, 0x2121, 0x2150, 0x1982 };

    // Devices with 0x40A0 and a coarse battery (the K295) keep the info page and its media keys toggle.
    public static bool Supports(FoundDevice found) { return !found.MediaKeys && Features.Any(found.Has); }

    public class State
    {
        public OtherSettings Set = new OtherSettings();
        public int BrightMax = 100, ShiftDefault = 10, ShiftFeature, Levels, Hosts, Host = -1;
        public int[] Dpis = new int[0];
        public string[] Names = new string[0];
        public bool[] Paired = new bool[0];
        public bool PairedKnown;
    }

    static int ReadUInt16BigEndian(byte[] bytes, int offset) { return bytes[offset] << 8 | bytes[offset + 1]; }

    static byte[] Try(Hidpp device, int feature, int fn, params byte[] parameters)
    {
        try { return device.Call(device.FeatureIndex(feature), fn, parameters); }
        catch (HidppError) { return null; }
        catch (NotSupportedException) { return null; }
    }

    public static State Read(Hidpp device, FoundDevice found)
    {
        var state = new State();
        var set = state.Set;
        if (found.Has(0x8040))
        {
            var info = Try(device, 0x8040, 0);
            var now = Try(device, 0x8040, 1);
            if (info != null && ReadUInt16BigEndian(info, 0) > 0) state.BrightMax = ReadUInt16BigEndian(info, 0);
            if (now != null) set[OtherSettings.Bright] = Math.Min(state.BrightMax, ReadUInt16BigEndian(now, 0));
        }
        if (found.Has(0x2201))
        {
            var list = Try(device, 0x2201, 1, 0);
            var now = Try(device, 0x2201, 2, 0);
            if (list != null) state.Dpis = MouseProtocol.DpiList(list);
            int dpi = now == null ? 0 : ReadUInt16BigEndian(now, 1) != 0 ? ReadUInt16BigEndian(now, 1) : ReadUInt16BigEndian(now, 3);
            if (dpi > 0 && state.Dpis.Length > 0) set[OtherSettings.Dpi] = dpi;
        }
        if (found.Has(0x2111))
        {
            var capabilities = Try(device, 0x2111, 0);
            var now = Try(device, 0x2111, 1);
            if (capabilities != null && capabilities[1] > 0) state.ShiftDefault = capabilities[1];
            if (now != null && now[1] > 0) { set[OtherSettings.Shift] = now[1]; state.ShiftFeature = 0x2111; }
        }
        else if (found.Has(0x2110))
        {
            var now = Try(device, 0x2110, 0);
            // The default is what G-Lite last wrote (it survives a reset); FF is SmartShift off, not a threshold to start the slider at.
            if (now != null && now[2] > 0 && now[2] < 0xFF) state.ShiftDefault = now[2];
            if (now != null && now[1] > 0) { set[OtherSettings.Shift] = now[1]; state.ShiftFeature = 0x2110; }
        }
        state.ShiftDefault = Math.Max(1, Math.Min(50, state.ShiftDefault));
        if (found.Has(0x2121))
        {
            var capabilities = Try(device, 0x2121, 0);
            var mode = Try(device, 0x2121, 1);
            if (capabilities != null && (capabilities[1] & 0x08) != 0 && mode != null)
                set[OtherSettings.Invert] = (mode[0] & 0x04) != 0 ? 1 : 0;
        }
        if (found.Has(0x2150))
        {
            var now = Try(device, 0x2150, 1);
            if (now != null) set[OtherSettings.Thumb] = now[1] & 1;
        }
        if (found.Has(0x1982))
        {
            var config = Try(device, 0x1982, 0);
            if (config != null)
            {
                set[OtherSettings.Light] = config[0] & 1;
                // Mode 2 (temporary manual) is automatic nudged with the keys: the keyboard goes back to automatic at the next power-on
                // (spec x1982 v3).
                int mode = config[1] >> 3 & 3;
                if ((config[2] & 0x08) != 0 && (config[2] & 0x20) != 0) set[OtherSettings.Auto] = mode == 1 || mode == 2 ? 1 : 0;
                var info = (config[2] & 0x20) != 0 ? Try(device, 0x1982, 2) : null;
                // In mode 3 the level is fn 0's b5, the stored setting, like Solaar. Outside it b5 reads 0 (spec x1982 v3),
                // so the slider shows fn 2's b1, the level lit now (it fades when idle).
                if (info != null && info[0] > 1)
                {
                    state.Levels = info[0];
                    set[OtherSettings.Level] = Math.Min(info[0] - 1, mode == 3 ? config[5] : info[1]);
                }
                // Bytes 6–11: the fade delays, little-endian, in units of 5 s (spec x1982 v3, named by Solaar).
                // Offered only when all three read, since an older version of the feature may not have them.
                var delays = OtherSettings.Delays.Select((setting, index) => config[6 + 2 * index] | config[7 + 2 * index] << 8).ToArray();
                if (delays.All(units => units > 0))
                    for (int index = 0; index < delays.Length; index++) set[OtherSettings.Delays[index]] = delays[index] * 5;
            }
        }
        if (found.Has(0x1814))
        {
            var info = Try(device, 0x1814, 0);
            if (info != null && info[0] > 1 && info[1] < info[0]) { state.Hosts = Math.Min(6, (int)info[0]); state.Host = info[1]; }
            state.Names = new string[state.Hosts];
            state.Paired = Enumerable.Repeat(true, state.Hosts).ToArray();
            // The names are only shown, so a computer that doesn't answer for them doesn't stop the screen.
            if (state.Hosts > 0 && found.Has(0x1815))
                try { ReadNames(device, state); }
                catch (TimeoutException) { state.PairedKnown = false; }
        }
        return state;
    }

    static void ReadNames(Hidpp device, State state)
    {
        var capabilities = Try(device, 0x1815, 0);
        if (capabilities == null) return;
        state.PairedKnown = true;
        for (int host = 0; host < state.Hosts; host++)
        {
            var info = Try(device, 0x1815, 1, (byte)host);
            if (info == null) { state.PairedKnown = false; continue; }
            state.Paired[host] = info[1] == 1 || host == state.Host;
            if (!state.Paired[host] || (capabilities[0] & 1) == 0 || info[4] == 0) continue;
            var bytes = new List<byte>();
            while (bytes.Count < info[4])
            {
                var part = Try(device, 0x1815, 3, (byte)host, (byte)bytes.Count);
                if (part == null) break;
                bytes.AddRange(part.Skip(2).Take(Math.Min(14, info[4] - bytes.Count)));
            }
            if (bytes.Count > 0) state.Names[host] = Encoding.UTF8.GetString(bytes.ToArray()).TrimEnd('\0').Trim();
        }
    }

    public static string Name(int setting)
    {
        return new[]
        {
            "brightness", "pointer speed", "SmartShift", "invert scrolling", "thumb wheel",
            "backlight", "backlight", "backlight", "backlight", "backlight", "backlight"
        }[setting];
    }

    // Each setting is sent on its own, so one the device refuses doesn't stop the others. Returns the refused settings.
    public static List<int> Apply(Hidpp device, State state, OtherSettings want, OtherSettings sent)
    {
        var refused = new List<int>();
        Action<int, Action> send = (setting, write) =>
        {
            if (want[setting] < 0 || want[setting] == sent[setting]) return;
            try { write(); }
            catch (HidppError) { refused.Add(setting); }
        };
        send(OtherSettings.Bright, () => device.Call(device.FeatureIndex(0x8040), 2,
            (byte)(want[OtherSettings.Bright] >> 8), (byte)want[OtherSettings.Bright]));
        send(OtherSettings.Dpi, () => device.Call(device.FeatureIndex(0x2201), 3,
            0, (byte)(want[OtherSettings.Dpi] >> 8), (byte)want[OtherSettings.Dpi]));
        // 0 in a SmartShift byte leaves it, so the wheel's mode, set with its button, isn't touched. 0x2110 goes back to its third byte
        // (autoDisengageDefault) at each reset, so it gets the threshold too; 0x2111 keeps its settings and its third byte is the torque
        // (specs x2110, x2111).
        send(OtherSettings.Shift, () => device.Call(device.FeatureIndex(state.ShiftFeature), state.ShiftFeature == 0x2111 ? 2 : 1,
            0, (byte)want[OtherSettings.Shift], state.ShiftFeature == 0x2111 ? (byte)0 : (byte)want[OtherSettings.Shift]));
        // Read just before writing: Logi Options+ may have diverted the wheel since (bit 0). Only bit 2 changes.
        // Bit 1 (high resolution) makes Windows scroll many times too fast.
        send(OtherSettings.Invert, () =>
        {
            var mode = device.Call(device.FeatureIndex(0x2121), 1);
            device.Call(device.FeatureIndex(0x2121), 2,
                (byte)(mode[0] & ~0x04 | (want[OtherSettings.Invert] == 1 ? 0x04 : 0)), mode[1], mode[2]);
        });
        send(OtherSettings.Thumb, () =>
        {
            var now = device.Call(device.FeatureIndex(0x2150), 1);
            device.Call(device.FeatureIndex(0x2150), 2, now[0], (byte)want[OtherSettings.Thumb], 0);
        });
        var lightSettings = new[] { OtherSettings.Light, OtherSettings.Auto, OtherSettings.Level }.Concat(OtherSettings.Delays).ToArray();
        bool backlight = lightSettings.Any(setting => want[setting] >= 0 && want[setting] != sent[setting]);
        if (backlight)
        {
            try
            {
                // The keyboard stores this write. Delays not picked go back as read; the level is sent only in mode 3, else 0, like Solaar.
                // Mode 2 can't be set (spec x1982 v3), so a keyboard in it is written back as automatic.
                // Outside mode 3 the stored level reads 0, so going to mode 3 without a level picked takes the level lit now
                // (fn 2 byte 1) rather than turning the backlight off.
                var config = device.Call(device.FeatureIndex(0x1982), 0);
                int now = config[1] >> 3 & 3;
                int mode;
                if (want[OtherSettings.Auto] == 1) mode = 1;
                else if (want[OtherSettings.Auto] == 0
                    || (want[OtherSettings.Level] >= 0 && want[OtherSettings.Level] != sent[OtherSettings.Level]))
                    mode = 3;
                else if (now == 2) mode = 1;
                else mode = now;
                byte options = (byte)(config[1] & ~0x18 | mode << 3);
                bool picked = want[OtherSettings.Level] >= 0 && want[OtherSettings.Level] != sent[OtherSettings.Level];
                int level = mode != 3 ? 0
                    : picked ? want[OtherSettings.Level]
                    : now == 3 ? config[5]
                    : device.Call(device.FeatureIndex(0x1982), 2)[1];
                // A level nobody picked that reads 0 (faded, or never set) would store the backlight off: take the middle level instead.
                if (mode == 3 && !picked && level == 0) level = Math.Max(1, (state.Levels - 1) / 2);
                bool on = want[OtherSettings.Light] >= 0 ? want[OtherSettings.Light] == 1 : (config[0] & 1) != 0;
                var delays = new byte[6];
                for (int index = 0; index < OtherSettings.Delays.Length; index++)
                {
                    int setting = OtherSettings.Delays[index];
                    int units = want[setting] > 0 ? want[setting] / 5 : config[6 + 2 * index] | config[7 + 2 * index] << 8;
                    delays[2 * index] = (byte)units;
                    delays[2 * index + 1] = (byte)(units >> 8);
                }
                device.Call(device.FeatureIndex(0x1982), 1, new byte[] { (byte)(on ? 1 : 0), options, 0xFF, (byte)level }
                    .Concat(delays)
                    .Concat(new byte[] { 0, 0 })
                    .ToArray());
            }
            catch (HidppError) { refused.AddRange(lightSettings.Where(setting => want[setting] >= 0)); }
        }
        return refused;
    }

    public static void Switch(Hidpp device, int host) { device.SendWithoutReply(device.FeatureIndex(0x1814), 1, (byte)host); }

    // Keeps only what this device has, in its ranges, so a preset or remembered setting from elsewhere can't send something it lacks.
    public static OtherSettings KeepSupported(OtherSettings over, State state)
    {
        var limited = new OtherSettings();
        for (int setting = 0; setting < OtherSettings.Count; setting++)
        {
            int value = over[setting];
            if (value < 0 || state.Set[setting] < 0) continue;
            switch (setting)
            {
                case OtherSettings.Bright: value = Math.Min(state.BrightMax, value); break;
                case OtherSettings.Dpi: value = state.Dpis[Nearest(state.Dpis, value)]; break;
                case OtherSettings.Shift: if (value > 50 && value != 255) value = -1; break;
                case OtherSettings.Level: value = Math.Min(state.Levels - 1, value); break;
                // Up to 2 hours (Solaar's note on the spec), in steps of 5 s.
                case OtherSettings.Away:
                case OtherSettings.Near:
                case OtherSettings.Powered: value = Math.Max(5, Math.Min(7200, value / 5 * 5)); break;
                default: value = value == 1 ? 1 : 0; break;
            }
            limited[setting] = value;
        }
        return limited;
    }

    public static int Nearest(int[] list, int value)
    {
        int best = 0;
        for (int index = 1; index < list.Length; index++) if (Math.Abs(list[index] - value) < Math.Abs(list[best] - value)) best = index;
        return best;
    }
}
