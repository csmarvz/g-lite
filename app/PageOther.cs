using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Windows.Forms;

class OtherSettings
{
    // Away, Near and Powered are the backlight's fade delays in seconds: hands away from the keys, hands near them, on external power.
    public const int Bright = 0, Dpi = 1, Shift = 2, Invert = 3, Thumb = 4, Light = 5, Auto = 6, Level = 7, Away = 8, Near = 9, Powered = 10, Count = 11;
    public static readonly int[] Delays = { Away, Near, Powered };
    static readonly string[] Keys = { "bright", "dpi", "shift", "invert", "thumb", "light", "auto", "level", "away", "near", "powered" };

    // -1 is a setting the device doesn't have, or one this set leaves alone. Shift is SmartShift's threshold: 1–50, or 255 when it's off (the wheel never switches to free spin).
    readonly int[] values = Enumerable.Repeat(-1, Count).ToArray();

    public int this[int setting] { get { return values[setting]; } set { values[setting] = value; } }

    public OtherSettings Clone()
    {
        var copy = new OtherSettings();
        values.CopyTo(copy.values, 0);
        return copy;
    }

    public void Merge(OtherSettings over) { for (int setting = 0; setting < Count; setting++) if (over[setting] >= 0) values[setting] = over[setting]; }

    public string ToText() { return string.Join(",", Enumerable.Range(0, Count).Where(setting => values[setting] >= 0).Select(setting => Keys[setting] + ":" + values[setting])); }

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

// The settings of Logitech devices that aren't G devices. Bytes from Solaar's settings, OpenLogi's captures of an MX Master 3S and MX Keys S, and the MX Creative Keypad's report. See docs/other-devices.md.
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
            if (capabilities != null && (capabilities[1] & 0x08) != 0 && mode != null) set[OtherSettings.Invert] = (mode[0] & 0x04) != 0 ? 1 : 0;
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
                // Mode 2 (temporary manual) is automatic nudged with the keys: the keyboard goes back to automatic at the next power-on (spec x1982 v3).
                int mode = config[1] >> 3 & 3;
                if ((config[2] & 0x08) != 0 && (config[2] & 0x20) != 0) set[OtherSettings.Auto] = mode == 1 || mode == 2 ? 1 : 0;
                var info = (config[2] & 0x20) != 0 ? Try(device, 0x1982, 2) : null;
                // In mode 3 the level is fn 0's b5, the stored setting, like Solaar. Outside it b5 reads 0 (spec x1982 v3), so the slider shows
                // fn 2's b1, the level lit now (it fades when idle).
                if (info != null && info[0] > 1) { state.Levels = info[0]; set[OtherSettings.Level] = Math.Min(info[0] - 1, mode == 3 ? config[5] : info[1]); }
                // Bytes 6–11: the fade delays, little-endian, in units of 5 s (spec x1982 v3, named by Solaar). Offered only when all three read,
                // since an older version of the feature may not have them.
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
            if (state.Hosts > 0 && found.Has(0x1815)) try { ReadNames(device, state); } catch (TimeoutException) { state.PairedKnown = false; }
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

    public static string Name(int setting) { return new[] { "brightness", "pointer speed", "SmartShift", "invert scrolling", "thumb wheel", "backlight", "backlight", "backlight", "backlight", "backlight", "backlight" }[setting]; }

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
        send(OtherSettings.Bright, () => device.Call(device.FeatureIndex(0x8040), 2, (byte)(want[OtherSettings.Bright] >> 8), (byte)want[OtherSettings.Bright]));
        send(OtherSettings.Dpi, () => device.Call(device.FeatureIndex(0x2201), 3, 0, (byte)(want[OtherSettings.Dpi] >> 8), (byte)want[OtherSettings.Dpi]));
        // 0 in a SmartShift byte leaves it, so the wheel's mode, set with its button, isn't touched. 0x2110 goes back to its third byte
        // (autoDisengageDefault) at each reset, so it gets the threshold too; 0x2111 keeps its settings and its third byte is the torque (specs x2110, x2111).
        send(OtherSettings.Shift, () => device.Call(device.FeatureIndex(state.ShiftFeature), state.ShiftFeature == 0x2111 ? 2 : 1, 0, (byte)want[OtherSettings.Shift], state.ShiftFeature == 0x2111 ? (byte)0 : (byte)want[OtherSettings.Shift]));
        // Read just before writing: Logi Options+ may have diverted the wheel since (bit 0). Only bit 2 changes. Bit 1 (high resolution) makes Windows scroll many times too fast.
        send(OtherSettings.Invert, () =>
        {
            var mode = device.Call(device.FeatureIndex(0x2121), 1);
            device.Call(device.FeatureIndex(0x2121), 2, (byte)(mode[0] & ~0x04 | (want[OtherSettings.Invert] == 1 ? 0x04 : 0)), mode[1], mode[2]);
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
                // Mode 2 can't be set (spec x1982 v3), so a keyboard in it is written back as automatic. Outside mode 3 the stored level reads 0,
                // so going to mode 3 without a level picked takes the level lit now (fn 2 byte 1) rather than turning the backlight off.
                var config = device.Call(device.FeatureIndex(0x1982), 0);
                int now = config[1] >> 3 & 3;
                int mode = want[OtherSettings.Auto] == 1 ? 1 : want[OtherSettings.Auto] == 0 || want[OtherSettings.Level] >= 0 && want[OtherSettings.Level] != sent[OtherSettings.Level] ? 3 : now == 2 ? 1 : now;
                byte options = (byte)(config[1] & ~0x18 | mode << 3);
                bool picked = want[OtherSettings.Level] >= 0 && want[OtherSettings.Level] != sent[OtherSettings.Level];
                int level = mode != 3 ? 0 : picked ? want[OtherSettings.Level] : now == 3 ? config[5] : device.Call(device.FeatureIndex(0x1982), 2)[1];
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
                device.Call(device.FeatureIndex(0x1982), 1, new byte[] { (byte)(on ? 1 : 0), options, 0xFF, (byte)level }.Concat(delays).Concat(new byte[] { 0, 0 }).ToArray());
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

class PageOtherDevice : DevicePage
{
    class SettingBox { public string Title; public bool Half; public Func<RoundedBox, int, int> Fill; }

    // current is on the screen, sent is on the device, stored is what was read at load, and remembered holds only what the user changed: it's sent again at each load.
    OtherSettings current = new OtherSettings(), stored, sent, remembered, rememberedAtLoad;
    OtherDevice.State state;
    bool loading, dirty;
    int sequence;
    readonly Timer debounce = new Timer { Interval = 120 };

    Slider bright, dpi, shift, level;
    Label brightValue, dpiValue, shiftValue, levelValue, shiftHead, levelHead;
    Toggle shiftOn, invert, thumb, light, auto;
    Label delayHead;
    readonly Label[] delayLabels = new Label[3];
    readonly Dropdown[] delayDropdowns = new Dropdown[3];
    SegmentedPicker hosts;
    RoundedBox hostsBox;

    internal override bool Busy { get { return debounce.Enabled; } }

    string Remembered { get { return "device." + found.SettingsKey; } }

    string Noun { get { return found.Kind == "mouse" || found.Kind == "keyboard" ? found.Kind : "device"; } }

    public PageOtherDevice(FoundDevice found, Hidpp fake, MouseEventHandler drag) : base(found, fake, drag, found.ShortName, "device", "profile." + found.SettingsKey + ".")
    {
        debounce.Tick += (sender, e) => { debounce.Stop(); Send(); };
        remembered = OtherSettings.FromText(Settings.Get(Remembered));
        rememberedAtLoad = remembered.Clone();
        var parts = new List<SettingBox>();
        if (found.Has(0x8040)) parts.Add(new SettingBox { Title = found.Kind == "device" ? "Screens" : "Brightness", Fill = Brightness });
        if (found.Has(0x2201)) parts.Add(new SettingBox { Title = "Pointer", Fill = Pointer });
        if (found.Has(0x2110) || found.Has(0x2111) || found.Has(0x2121)) parts.Add(new SettingBox { Title = "Scroll wheel", Half = true, Fill = Wheel });
        if (found.Has(0x2150)) parts.Add(new SettingBox { Title = "Thumb wheel", Half = true, Fill = ThumbWheel });
        if (found.Has(0x1982)) parts.Add(new SettingBox { Title = "Backlight", Half = true, Fill = Backlight });
        if (found.Has(0x1814)) parts.Add(new SettingBox { Title = "Easy-Switch", Fill = EasySwitch });
        Lay(parts);
        RefreshControls();
        if (remembered.ToText() != "") Load();
    }

    void Lay(List<SettingBox> parts)
    {
        int top = 118;
        for (int index = 0; index < parts.Count; index++)
        {
            var part = parts[index];
            bool pair = part.Half && index + 1 < parts.Count && parts[index + 1].Half;
            if (!pair)
            {
                var box = Section(part.Title, 20, top, 616);
                box.Height = part.Fill(box, 588) + 14;
                top += box.Height + 12;
                continue;
            }
            var left = Section(part.Title, 20, top, 380);
            var right = Section(parts[index + 1].Title, 412, top, 224);
            int height = Math.Max(part.Fill(left, 352), parts[index + 1].Fill(right, 196)) + 14;
            left.Height = right.Height = height;
            top += height + 12;
            index++;
        }
    }

    RoundedBox Section(string title, int x, int y, int width)
    {
        var box = Ui.Add(this, new RoundedBox(), x, y, width, 40);
        Ui.Text(box, title, 14, 12, () => Theme.Dim, 9f, Ui.Semibold);
        return box;
    }

    Slider SliderRow(RoundedBox box, string name, int y, int width, out Label head, out Label value)
    {
        head = Ui.Text(box, name, 14, y, () => Theme.Dim, 9f);
        value = Value(box, 14, y, width);
        return Ui.Add(box, new Slider { AccessibleName = name }, 14, y + 20, width, 24);
    }

    Toggle ToggleRow(RoundedBox box, string name, string note, int y, int width)
    {
        var toggle = Ui.Add(box, new Toggle { AccessibleName = name }, 14, y + 2, 34, 18);
        toggle.Follow(Ui.Text(box, name, 60, y, () => Theme.Text, 9.75f));
        if (note != null) AddHint(box, note, 60, y + 22, width - 46, 36);
        return toggle;
    }

    static Label AddHint(RoundedBox box, string text, int x, int y, int width, int height)
    {
        var label = Ui.Text(box, text, x, y, () => Theme.Dim, 9f);
        label.AutoSize = false;
        label.Size = new Size(width, height);
        return label;
    }

    int Brightness(RoundedBox box, int width)
    {
        Label head;
        bright = SliderRow(box, "Brightness", 40, width, out head, out brightValue);
        bright.Changed += (sender, e) => Change(OtherSettings.Bright, bright.Value);
        AddHint(box, found.Kind == "device" ? "How bright the key screens are. Logi Options+ may set it again when it starts." : "Logi Options+ may set it again when it starts.", 14, 98, width, 20);
        return 118;
    }

    int Pointer(RoundedBox box, int width)
    {
        Label head;
        dpi = SliderRow(box, "Speed", 40, width, out head, out dpiValue);
        dpi.Step = 1;
        dpi.Changed += (sender, e) => { if (state != null && dpi.Value < state.Dpis.Length) Change(OtherSettings.Dpi, state.Dpis[dpi.Value]); };
        return 84;
    }

    int Wheel(RoundedBox box, int width)
    {
        int y = 40;
        if (found.Has(0x2110) || found.Has(0x2111))
        {
            shiftOn = ToggleRow(box, "SmartShift", "Switches from ratchet to free spin when you scroll fast.", y, width);
            shiftOn.Changed += () => Change(OtherSettings.Shift, shiftOn.Checked ? shift.Value + 1 : 255);
            shift = SliderRow(box, "Switch to free spin at", y + 62, width, out shiftHead, out shiftValue);
            shift.Max = 49;
            shift.Step = 1;
            shift.Changed += (sender, e) => Change(OtherSettings.Shift, shift.Value + 1);
            y += 118;
        }
        if (found.Has(0x2121))
        {
            invert = ToggleRow(box, "Invert scrolling", null, y, width);
            invert.Changed += () => Change(OtherSettings.Invert, invert.Checked ? 1 : 0);
            y += 34;
        }
        return y;
    }

    int ThumbWheel(RoundedBox box, int width)
    {
        thumb = ToggleRow(box, "Invert", null, 40, width);
        thumb.AccessibleName = "Invert the thumb wheel";
        thumb.Changed += () => Change(OtherSettings.Thumb, thumb.Checked ? 1 : 0);
        return 74;
    }

    int Backlight(RoundedBox box, int width)
    {
        // Plugged in, the MX Keys S's hand sensor seems off: turned on, it stays dark until a key is pressed. On battery it lights at once (an owner, 2026-11).
        light = ToggleRow(box, "Backlight", "Plugged in, the keys light up when you next type.", 40, width);
        light.Changed += () => Change(OtherSettings.Light, light.Checked ? 1 : 0);
        auto = ToggleRow(box, "Automatic", "Dims by itself with the room's light.", 102, width);
        auto.Changed += () => Change(OtherSettings.Auto, auto.Checked ? 1 : 0);
        level = SliderRow(box, "Level", 164, width, out levelHead, out levelValue);
        level.Step = 1;
        level.Changed += (sender, e) => Change(OtherSettings.Level, level.Value);
        delayHead = Ui.Text(box, "Fades after", 14, 220, () => Theme.Dim, 9f);
        string[] names = { "Hands away", "Hands near", "Plugged in" };
        int column = (width - 2 * 12) / names.Length;
        for (int index = 0; index < names.Length; index++)
        {
            int setting = OtherSettings.Delays[index], x = 14 + index * (column + 12);
            delayLabels[index] = Ui.Text(box, names[index], x, 242, () => Theme.Dim, 9f);
            delayDropdowns[index] = Ui.Add(box, new Dropdown { AccessibleName = "Fades after, " + names[index].ToLowerInvariant() }, x, 262, column, 30);
            delayDropdowns[index].Picked += picked => Change(setting, DelayChoices(current[setting])[picked]);
        }
        return 306;
    }

    int EasySwitch(RoundedBox box, int width)
    {
        hostsBox = box;
        hosts = Ui.Add(box, new SegmentedPicker { AccessibleName = "Computer" }, 14, 36, width, 32);
        hosts.Picked += AskSwitch;
        AddHint(box, "Switching sends the " + Noun + " to that computer, like its Easy-Switch button.", 14, 76, width, 20);
        return 96;
    }

    string HostName(int host)
    {
        var name = state != null && host < state.Names.Length ? state.Names[host] : null;
        return (host + 1) + (state != null && host < state.Paired.Length && !state.Paired[host] ? ": –" : name == null ? "" : ": " + name);
    }

    void AskSwitch(int host)
    {
        if (state == null || host == state.Host) return;
        hosts.Index = state.Host;
        hosts.Invalidate();
        if (!state.Paired[host]) { ShowStatus("Nothing is paired on computer " + (host + 1) + " yet. Pair it with the Easy-Switch button first."); return; }
        var name = state.Names[host] ?? "computer " + (host + 1);
        ShowStatus("Switch the " + Noun + " to " + name + "? It leaves this PC until you switch back" + (state.PairedKnown ? "." : ". If nothing is paired there, press its Easy-Switch button to come back."), false, () => Switch(host), "Switch");
    }

    void Switch(int host)
    {
        LogUserAction("Switch to computer " + (host + 1));
        RunOnDevice(device => OtherDevice.Switch(device, host), () => ShowStatus("Switched to computer " + (host + 1) + ". Press the Easy-Switch button to come back."));
    }

    void Change(int setting, int value)
    {
        if (loading || state == null || current[setting] < 0) return;
        current[setting] = value;
        remembered[setting] = value;
        Changed();
    }

    void Changed()
    {
        dirty = true;
        sequence++;
        Edited();
        RefreshControls();
        ClearStatus();
        debounce.Stop();
        debounce.Start();
    }

    void RefreshControls()
    {
        loading = true;
        bool ready = state != null;
        if (bright != null)
        {
            bool on = ready && current[OtherSettings.Bright] >= 0;
            bright.Enabled = on;
            bright.Max = ready ? state.BrightMax : 100;
            bright.Value = Math.Max(0, current[OtherSettings.Bright]);
            brightValue.Text = !on ? "" : state.BrightMax == 100 ? current[OtherSettings.Bright] + "%" : current[OtherSettings.Bright] + " of " + state.BrightMax;
        }
        if (dpi != null)
        {
            bool on = ready && current[OtherSettings.Dpi] > 0;
            dpi.Enabled = on && state.Dpis.Length > 1;
            dpi.Max = on ? Math.Max(1, state.Dpis.Length - 1) : 100;
            dpi.Value = on ? OtherDevice.Nearest(state.Dpis, current[OtherSettings.Dpi]) : 0;
            dpiValue.Text = on ? current[OtherSettings.Dpi] + " DPI" : "";
        }
        if (shiftOn != null)
        {
            int threshold = current[OtherSettings.Shift];
            bool has = ready && threshold >= 0, on = has && threshold < 255;
            shiftOn.Enabled = has;
            shiftOn.Checked = on;
            shift.Enabled = on;
            shift.Value = Math.Max(0, Math.Min(49, (on ? threshold : ready ? state.ShiftDefault : 10) - 1));
            SetDimmed(shiftHead, on);
            shiftValue.Text = on ? (threshold / 4.0).ToString("0.##", CultureInfo.InvariantCulture) + " turns a second" : "";
        }
        if (invert != null) { invert.Enabled = ready && current[OtherSettings.Invert] >= 0; invert.Checked = current[OtherSettings.Invert] == 1; }
        if (thumb != null) { thumb.Enabled = ready && current[OtherSettings.Thumb] >= 0; thumb.Checked = current[OtherSettings.Thumb] == 1; }
        if (light != null)
        {
            bool lit = current[OtherSettings.Light] == 1;
            light.Enabled = ready && current[OtherSettings.Light] >= 0;
            light.Checked = lit;
            auto.Enabled = ready && current[OtherSettings.Auto] >= 0 && lit;
            auto.Checked = lit && current[OtherSettings.Auto] == 1;
            bool manual = ready && current[OtherSettings.Level] >= 0 && lit && current[OtherSettings.Auto] != 1;
            level.Enabled = manual;
            level.Max = ready && state.Levels > 1 ? state.Levels - 1 : 1;
            level.Value = Math.Max(0, current[OtherSettings.Level]);
            SetDimmed(levelHead, manual);
            levelValue.Text = ready && current[OtherSettings.Level] >= 0 ? current[OtherSettings.Level] + 1 + " of " + state.Levels : "";
            bool delays = ready && current[OtherSettings.Away] > 0 && lit;
            SetDimmed(delayHead, delays);
            for (int index = 0; index < delayDropdowns.Length; index++)
            {
                int seconds = current[OtherSettings.Delays[index]];
                var choices = DelayChoices(seconds);
                delayDropdowns[index].Items.Clear();
                delayDropdowns[index].Items.AddRange(choices.Select(DelayText));
                delayDropdowns[index].Index = Array.IndexOf(choices, seconds);
                delayDropdowns[index].Enabled = delays;
                SetDimmed(delayLabels[index], delays);
            }
        }
        if (hosts != null)
        {
            int count = ready ? state.Hosts : 0;
            hostsBox.Visible = !ready || count > 1;
            hosts.Items = count > 1 ? Enumerable.Range(0, count).Select(HostName).ToArray() : new[] { "1", "2", "3" };
            hosts.Enabled = count > 1;
            hosts.Index = count > 1 ? state.Host : -1;
            hosts.Invalidate();
        }
        foreach (var control in new Control[] { bright, dpi, shift, level }) if (control != null) control.Invalidate();
        loading = false;
    }

    static readonly int[] DelaySteps = { 5, 10, 15, 30, 60, 120, 300, 600, 1800, 3600 };

    // The usual delays, with the keyboard's own when it isn't one of them.
    static int[] DelayChoices(int seconds) { return seconds <= 0 || DelaySteps.Contains(seconds) ? DelaySteps : DelaySteps.Concat(new[] { seconds }).OrderBy(step => step).ToArray(); }

    static string DelayText(int seconds)
    {
        if (seconds % 3600 == 0) return seconds / 3600 + " h";
        if (seconds % 60 == 0) return seconds / 60 + " min";
        return seconds + " s";
    }

    public override void Restyle() { RefreshControls(); }

    protected override bool Unsaved { get { return dirty; } }

    protected override string Current() { return current.ToText(); }

    protected override void ApplyPreset(string text)
    {
        if (state == null) return;
        var preset = OtherDevice.KeepSupported(OtherSettings.FromText(text), state);
        for (int setting = 0; setting < OtherSettings.Count; setting++) if (preset[setting] >= 0 && preset[setting] != current[setting]) remembered[setting] = preset[setting];
        current.Merge(preset);
        Changed();
    }

    protected override void Revert()
    {
        if (stored == null) return;
        current = stored.Clone();
        remembered = rememberedAtLoad.Clone();
        Changed();
        dirty = false;
        ClearStatus();
    }

    protected override void ClearStatus()
    {
        ShowStatus(state != null && dirty ? "Applied to the " + Noun + "." : "");
    }

    // A refused setting goes back to what the device has, and isn't remembered or sent again.
    void Refused(List<int> refused, OtherSettings device)
    {
        foreach (var setting in refused) { current[setting] = device[setting]; remembered[setting] = -1; }
        Settings.Set(Remembered, remembered.ToText());
        if (refused.Count == 0) { ClearStatus(); return; }
        RefreshControls();
        ShowStatus("The " + Noun + " refused the " + string.Join(" and the ", refused.Select(OtherDevice.Name).Distinct()) + ".", true);
    }

    void Send()
    {
        var want = current.Clone();
        var before = sent.Clone();
        var at = state;
        int startedAt = ++sequence;
        List<int> refused = null;
        RunOnDevice(device =>
        {
            if (startedAt != sequence) return;
            refused = OtherDevice.Apply(device, at, want, sent);
            foreach (var setting in refused) want[setting] = before[setting];
            sent = want;
        }, () => { if (startedAt == sequence && refused != null) Refused(refused, before); });
    }

    public override void Load()
    {
        if (debounce.Enabled) { debounce.Stop(); Send(); }
        ShowStatus("Reading the " + Noun + "…");
        OtherDevice.State read = null;
        var keep = remembered.Clone();
        List<int> refused = null;
        OtherSettings before = null;
        int startedAt = sequence;
        RunOnDevice(device =>
        {
            read = OtherDevice.Read(device, found);
            before = read.Set.Clone();
            var want = read.Set.Clone();
            want.Merge(OtherDevice.KeepSupported(keep, read));
            // With Automatic on, the level follows the room's light, so the one remembered from manual mode isn't sent.
            if (want[OtherSettings.Auto] == 1) want[OtherSettings.Level] = read.Set[OtherSettings.Level];
            if (want.Same(read.Set)) return;
            refused = OtherDevice.Apply(device, read, want, read.Set);
            foreach (var setting in refused) want[setting] = before[setting];
            read.Set = want;
        }, () =>
        {
            state = read;
            stored = read.Set.Clone();
            if (startedAt == sequence)
            {
                current = read.Set.Clone();
                sent = read.Set.Clone();
                dirty = false;
            }
            RefreshControls();
            Loaded();
            if (refused != null && refused.Count > 0) Refused(refused, before);
            rememberedAtLoad = remembered.Clone();
        });
    }
}
