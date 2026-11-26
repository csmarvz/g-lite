using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

class OtherSettings
{
    // Away, Near and Powered are the backlight's fade delays in seconds: hands away from the keys, hands near them, on external power.
    // Effect is a 0x1982 effect id, Saver the backlight's battery saving, MediaKeys 0x40A3's state (1: F1–F12 are media keys) and
    // Disabled 0x4521's bits (1 Caps Lock, 2 Num Lock, 4 Scroll Lock, 8 Insert, 16 Windows).
    public const int Bright = 0, Dpi = 1, Shift = 2, Invert = 3, Thumb = 4, Light = 5, Auto = 6, Level = 7,
        Away = 8, Near = 9, Powered = 10, Effect = 11, Saver = 12, MediaKeys = 13, Disabled = 14, Count = 15;
    public static readonly int[] Delays = { Away, Near, Powered };
    static readonly string[] Keys =
    {
        "bright", "dpi", "shift", "invert", "thumb", "light", "auto", "level", "away", "near", "powered", "effect", "saver", "mediakeys",
        "disabled"
    };

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
// and MX Keys S, and the reports of an MX Creative Keypad and an MX Mechanical Mini. See docs/other-devices.md.
static class OtherDevice
{
    // 0x1814 alone doesn't get the screen: a device with only Easy-Switch has nothing else to set.
    public static readonly int[] Features = { 0x8040, 0x2201, 0x2110, 0x2111, 0x2121, 0x2150, 0x1982, 0x40A3, 0x4521 };

    // 0x1982's effects, in Logi Options+'s order; 1 (none) isn't offered.
    public static readonly int[] EffectIds = { 0, 3, 2, 6, 4, 5 };
    public static readonly string[] EffectNames = { "Static", "Contrast", "Breathing", "Waves", "Reaction", "Random" };

    public static readonly int[] DisableBits = { 1, 2, 4, 8, 16 };
    public static readonly string[] DisableNames = { "Caps Lock", "Num Lock", "Scroll Lock", "Insert", "Windows key" };

    // Devices with 0x40A0 and a coarse battery (the K295) keep the info page and its media keys toggle.
    public static bool Supports(FoundDevice found) { return !found.MediaKeys && Features.Any(found.Has); }

    // The keyboards drawn on their screen, matched by name; the layout comes from a photo. The Mac version has the same keys.
    public static string Picture(FoundDevice found)
    {
        if (found.ShortName.StartsWith("MX Mechanical Mini", StringComparison.OrdinalIgnoreCase)) return "mx-mechanical-mini.json";
        return found.ShortName.StartsWith("MX Keys S", StringComparison.OrdinalIgnoreCase) ? "mx-keys-s.json" : null;
    }

    public class State
    {
        public OtherSettings Set = new OtherSettings();
        public int BrightMax = 100, ShiftDefault = 10, ShiftFeature, Levels, Hosts, Host = -1;
        // 0x1982's effect bits (bit n is effect n) and 0x4521's keys that can be disabled.
        public int Effects, Disableable;
        // The computer 0x40A3 is read and set for: this one's Easy-Switch index, read before any switch.
        public byte FnKeysHost = 0xFF;
        public int[] Dpis = new int[0];
        public string[] Names = new string[0];
        public bool[] Paired = new bool[0];
        public bool PairedKnown;
        // 0x4540's layout code, read only for a keyboard with a picture (KeyLayout.IsIso).
        public int LayoutCode = -1;
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
                // b2 bit 2: battery saving is supported, b1 bit 2 it's on (off below 10%, Logi Options+); b3–4 the effects, a bit
                // each (OpenLogi's notes on x1982 v3). Only Static and None (the MX Keys S) is no choice.
                if ((config[2] & 0x04) != 0) set[OtherSettings.Saver] = (config[1] & 0x04) != 0 ? 1 : 0;
                state.Effects = config[3] | config[4] << 8;
                bool effects = (state.Effects & ~3) != 0;
                var info = (config[2] & 0x20) != 0 || effects ? Try(device, 0x1982, 2) : null;
                // fn 2 b3 is the effect shown now. One the list doesn't have (None, maybe, while the backlight is off) reads as FF:
                // the row stays, with nothing picked, and fn 1 takes FF as unchanged.
                if (effects && info != null)
                    set[OtherSettings.Effect] = info[3] < 16 && (state.Effects >> info[3] & 1) != 0 && Array.IndexOf(EffectIds, (int)info[3]) >= 0 ? info[3] : 0xFF;
                // In mode 3 the level is fn 0's b5, the stored setting, like Solaar. Outside it b5 reads 0 (spec x1982 v3),
                // so the slider shows fn 2's b1, the level lit now (it fades when idle).
                if ((config[2] & 0x20) != 0 && info != null && info[0] > 1)
                {
                    state.Levels = info[0];
                    set[OtherSettings.Level] = Math.Min(info[0] - 1, mode == 3 ? config[5] : info[1]);
                    // Faded, it reads 0, which turning Automatic off would store as off.
                    if (mode != 3 && set[OtherSettings.Level] == 0) set[OtherSettings.Level] = Math.Max(1, (info[0] - 1) / 2);
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
        // fn 0 `<host>` asks for a computer's: host, state (1: F1–F12 are media keys, as Logi Options+ shows it), default,
        // capabilities (an MX Mechanical Mini answers `FF 01 01 01`; OpenLogi's notes); fn 1 `<host> <state> 00` sets it.
        // The MX Keys S takes FF but ignores it, and reads 00 for it (2026-11-26): the computer's Easy-Switch index works on it and on
        // the Mini, so FF is only for a device that doesn't say it.
        if (found.Has(0x40A3))
        {
            state.FnKeysHost = state.Host >= 0 ? (byte)state.Host : (byte)0xFF;
            var fnKeys = Try(device, 0x40A3, 0, state.FnKeysHost, 0, 0);
            if (fnKeys != null) set[OtherSettings.MediaKeys] = fnKeys[1] & 1;
        }
        // fn 0 the keys that can be disabled, fn 1 those that are (OpenLogi's notes; the MX Mechanical Mini answers 1D: no Num Lock).
        if (found.Has(0x4521))
        {
            var keys = Try(device, 0x4521, 0);
            var disabled = keys != null && (keys[0] & 0x1F) != 0 ? Try(device, 0x4521, 1) : null;
            if (disabled != null) { state.Disableable = keys[0] & 0x1F; set[OtherSettings.Disabled] = disabled[0] & state.Disableable; }
        }
        if (Picture(found) != null && found.Has(0x4540))
        {
            var layout = Try(device, 0x4540, 0);
            if (layout != null) state.LayoutCode = layout[0];
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
            "backlight", "backlight", "backlight", "backlight", "backlight", "backlight", "backlight", "backlight",
            "F1–F12 setting", "disabled keys"
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
        send(OtherSettings.MediaKeys, () => device.Call(device.FeatureIndex(0x40A3), 1, state.FnKeysHost, (byte)want[OtherSettings.MediaKeys], 0));
        send(OtherSettings.Disabled, () => device.Call(device.FeatureIndex(0x4521), 2, (byte)want[OtherSettings.Disabled]));
        var lightSettings = new[] { OtherSettings.Light, OtherSettings.Auto, OtherSettings.Level, OtherSettings.Effect, OtherSettings.Saver }
            .Concat(OtherSettings.Delays).ToArray();
        bool backlight = lightSettings.Any(setting => want[setting] >= 0 && want[setting] != sent[setting]);
        if (backlight)
        {
            try
            {
                // The keyboard stores this write. Delays not picked go back as read; the level is sent only in mode 3, else 0, like Solaar.
                // Mode 2 can't be set (spec x1982 v3), so a keyboard in it is written back as automatic.
                // Outside mode 3 the stored level reads 0, which is off, so going to mode 3 without a level picked takes the one the
                // screen shows (0 too), else the level lit now (fn 2 byte 1). Not fn 2 first: in automatic it's the room's level, and
                // the screen still shows the one picked before (seen on an MX Mechanical Mini, 2026-11-26).
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
                if (want[OtherSettings.Saver] >= 0) options = (byte)(options & ~0x04 | (want[OtherSettings.Saver] == 1 ? 0x04 : 0));
                // FF leaves the effect as it is.
                bool effect = want[OtherSettings.Effect] >= 0 && want[OtherSettings.Effect] != sent[OtherSettings.Effect];
                // Already in mode 3, a level not picked is the stored one, which the keyboard's own keys may have changed since.
                bool picked = want[OtherSettings.Level] >= 0 && want[OtherSettings.Level] != sent[OtherSettings.Level];
                bool fromLitNow = mode == 3 && !picked && now != 3 && want[OtherSettings.Level] < 0;
                int level = mode != 3 ? 0
                    : picked ? want[OtherSettings.Level]
                    : now == 3 ? config[5]
                    : !fromLitNow ? want[OtherSettings.Level]
                    : device.Call(device.FeatureIndex(0x1982), 2)[1];
                // A level the screen doesn't show that reads 0 (faded, or never set) would store the backlight off: take the middle one.
                if (fromLitNow && level == 0) level = Math.Max(1, (state.Levels - 1) / 2);
                bool on = want[OtherSettings.Light] >= 0 ? want[OtherSettings.Light] == 1 : (config[0] & 1) != 0;
                var delays = new byte[6];
                for (int index = 0; index < OtherSettings.Delays.Length; index++)
                {
                    int setting = OtherSettings.Delays[index];
                    int units = want[setting] > 0 ? want[setting] / 5 : config[6 + 2 * index] | config[7 + 2 * index] << 8;
                    delays[2 * index] = (byte)units;
                    delays[2 * index + 1] = (byte)(units >> 8);
                }
                device.Call(device.FeatureIndex(0x1982), 1,
                    new byte[] { (byte)(on ? 1 : 0), options, effect ? (byte)want[OtherSettings.Effect] : (byte)0xFF, (byte)level }
                    .Concat(delays)
                    .Concat(new byte[] { 0, 0 })
                    .ToArray());
            }
            catch (HidppError) { refused.AddRange(lightSettings.Where(setting => want[setting] >= 0)); }
        }
        return refused;
    }

    public static void Switch(Hidpp device, int host) { device.SendWithoutReply(device.FeatureIndex(0x1814), 1, (byte)host); }

    // Keys an app like Logi Options+ diverted to itself (0x1B04) do nothing while it isn't running, until the device reconnects
    // (docs/mx-key-bindings.md). This gives them back: fn 3 with the valid bit (one above) of each diversion flag that's on (diverted
    // 0x01, persistently 0x04, raw XY 0x10, force raw XY 0x40) and the flag off, remap 0 (unchanged). It never diverts. Returns how many.
    public static int ReleaseDiverted(Hidpp device)
    {
        byte feature = device.FeatureIndex(0x1B04);
        int count = device.CallUnlogged(feature, 0)[0], released = 0;
        for (int index = 0; index < Math.Min(count, 48); index++)
            // A control the device refuses doesn't keep the others diverted.
            try
            {
                var info = device.CallUnlogged(feature, 1, (byte)index);
                // Neither divertable (0x20, 0x40) nor raw XY (0x100, 0x200): nothing can have diverted it.
                if ((info[4] & 0x60) == 0 && (info[8] & 0x03) == 0) continue;
                var reporting = device.CallUnlogged(feature, 2, info[0], info[1]);
                int clear = new[] { 0x01, 0x04, 0x10, 0x40 }.Where(flag => (reporting[2] & flag) != 0).Sum(flag => flag << 1);
                if (clear == 0) continue;
                device.Call(feature, 3, info[0], info[1], (byte)clear, 0, 0);
                released++;
            }
            catch (HidppError) { }
        return released;
    }

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
                case OtherSettings.Effect: if (value > 15 || (state.Effects >> value & 1) == 0) value = -1; break;
                case OtherSettings.Disabled: value &= state.Disableable; break;
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
