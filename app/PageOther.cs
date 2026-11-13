using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Windows.Forms;

class OtherSet
{
    public const int Bright = 0, Dpi = 1, Shift = 2, Invert = 3, Thumb = 4, Light = 5, Auto = 6, Level = 7, Count = 8;
    static readonly string[] Keys = { "bright", "dpi", "shift", "invert", "thumb", "light", "auto", "level" };

    // -1 is a setting the device doesn't have, or one this set leaves alone. Shift is SmartShift's threshold: 1–50, or 255 when it's off (the wheel never switches to free spin).
    readonly int[] values = Enumerable.Repeat(-1, Count).ToArray();

    public int this[int setting] { get { return values[setting]; } set { values[setting] = value; } }

    public OtherSet Clone()
    {
        var copy = new OtherSet();
        values.CopyTo(copy.values, 0);
        return copy;
    }

    public void Merge(OtherSet over) { for (int setting = 0; setting < Count; setting++) if (over[setting] >= 0) values[setting] = over[setting]; }

    public string ToText() { return string.Join(",", Enumerable.Range(0, Count).Where(setting => values[setting] >= 0).Select(setting => Keys[setting] + ":" + values[setting])); }

    public static OtherSet FromText(string text)
    {
        var set = new OtherSet();
        foreach (var part in (text ?? "").Split(','))
        {
            var pair = part.Split(':');
            int number, setting = pair.Length == 2 ? Array.IndexOf(Keys, pair[0]) : -1;
            if (setting >= 0 && int.TryParse(pair[1], out number) && number >= 0) set[setting] = number;
        }
        return set;
    }

    public bool Same(OtherSet other) { return ToText() == other.ToText(); }
}

// The settings of Logitech devices that aren't G devices. Bytes from Solaar's settings, OpenLogi's captures of an MX Master 3S and MX Keys S, and the MX Creative Keypad's report. See docs/other-devices.md.
static class Other
{
    // 0x1814 alone doesn't get the screen: a device with only Easy-Switch has nothing else to set.
    public static readonly int[] Features = { 0x8040, 0x2201, 0x2110, 0x2111, 0x2121, 0x2150, 0x1982 };

    // Devices with 0x40A0 and a coarse battery (the K295) keep the info page and its media keys toggle.
    public static bool Fits(Found found) { return !found.MediaKeys && Features.Any(found.Has); }

    public class State
    {
        public OtherSet Set = new OtherSet();
        public int BrightMax = 100, ShiftDefault = 10, ShiftFeature, Levels, Hosts, Host = -1;
        public int[] Dpis = new int[0];
        public string[] Names = new string[0];
        public bool[] Paired = new bool[0];
        public bool PairedKnown;
    }

    static int U16(byte[] bytes, int offset) { return bytes[offset] << 8 | bytes[offset + 1]; }

    static byte[] Try(Hidpp device, int feature, int fn, params byte[] parameters)
    {
        try { return device.Call(device.F(feature), fn, parameters); }
        catch (HidppError) { return null; }
        catch (NotSupportedException) { return null; }
    }

    public static State Read(Hidpp device, Found found)
    {
        var state = new State();
        var set = state.Set;
        if (found.Has(0x8040))
        {
            var info = Try(device, 0x8040, 0);
            var now = Try(device, 0x8040, 1);
            if (info != null && U16(info, 0) > 0) state.BrightMax = U16(info, 0);
            if (now != null) set[OtherSet.Bright] = Math.Min(state.BrightMax, U16(now, 0));
        }
        if (found.Has(0x2201))
        {
            var list = Try(device, 0x2201, 1, 0);
            var now = Try(device, 0x2201, 2, 0);
            if (list != null) state.Dpis = GMouse.DpiList(list);
            int dpi = now == null ? 0 : U16(now, 1) != 0 ? U16(now, 1) : U16(now, 3);
            if (dpi > 0 && state.Dpis.Length > 0) set[OtherSet.Dpi] = dpi;
        }
        if (found.Has(0x2111))
        {
            var capabilities = Try(device, 0x2111, 0);
            var now = Try(device, 0x2111, 1);
            if (capabilities != null && capabilities[1] > 0) state.ShiftDefault = capabilities[1];
            if (now != null && now[1] > 0) { set[OtherSet.Shift] = now[1]; state.ShiftFeature = 0x2111; }
        }
        else if (found.Has(0x2110))
        {
            var now = Try(device, 0x2110, 0);
            if (now != null && now[2] > 0) state.ShiftDefault = now[2];
            if (now != null && now[1] > 0) { set[OtherSet.Shift] = now[1]; state.ShiftFeature = 0x2110; }
        }
        state.ShiftDefault = Math.Max(1, Math.Min(50, state.ShiftDefault));
        if (found.Has(0x2121))
        {
            var capabilities = Try(device, 0x2121, 0);
            var mode = Try(device, 0x2121, 1);
            if (capabilities != null && (capabilities[1] & 0x08) != 0 && mode != null) set[OtherSet.Invert] = (mode[0] & 0x04) != 0 ? 1 : 0;
        }
        if (found.Has(0x2150))
        {
            var now = Try(device, 0x2150, 1);
            if (now != null) set[OtherSet.Thumb] = now[1] & 1;
        }
        if (found.Has(0x1982))
        {
            var config = Try(device, 0x1982, 0);
            if (config != null)
            {
                set[OtherSet.Light] = config[0] & 1;
                if ((config[2] & 0x08) != 0 && (config[2] & 0x20) != 0) set[OtherSet.Auto] = (config[1] >> 3 & 3) == 1 ? 1 : 0;
                var info = (config[2] & 0x20) != 0 ? Try(device, 0x1982, 2) : null;
                // The level is fn 0's b5, the stored setting, like Solaar; fn 2's b1 is the level lit right now, which fades.
                if (info != null && info[0] > 1) { state.Levels = info[0]; set[OtherSet.Level] = Math.Min(info[0] - 1, (int)config[5]); }
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

    public static string Name(int setting) { return new[] { "brightness", "pointer speed", "SmartShift", "invert scrolling", "thumb wheel", "backlight", "backlight", "backlight" }[setting]; }

    // Each setting is sent on its own, so one the device refuses doesn't stop the others. Returns the refused settings.
    public static List<int> Apply(Hidpp device, State state, OtherSet want, OtherSet sent)
    {
        var refused = new List<int>();
        Action<int, Action> send = (setting, write) =>
        {
            if (want[setting] < 0 || want[setting] == sent[setting]) return;
            try { write(); }
            catch (HidppError) { refused.Add(setting); }
        };
        send(OtherSet.Bright, () => device.Call(device.F(0x8040), 2, (byte)(want[OtherSet.Bright] >> 8), (byte)want[OtherSet.Bright]));
        send(OtherSet.Dpi, () => device.Call(device.F(0x2201), 3, 0, (byte)(want[OtherSet.Dpi] >> 8), (byte)want[OtherSet.Dpi]));
        // 0 in a SmartShift byte leaves it, so the wheel's mode, set with its button, isn't touched.
        send(OtherSet.Shift, () => device.Call(device.F(state.ShiftFeature), state.ShiftFeature == 0x2111 ? 2 : 1, 0, (byte)want[OtherSet.Shift], 0));
        // Read just before writing: Logi Options+ may have diverted the wheel since (bit 0). Only bit 2 changes. Bit 1 (high resolution) makes Windows scroll many times too fast.
        send(OtherSet.Invert, () =>
        {
            var mode = device.Call(device.F(0x2121), 1);
            device.Call(device.F(0x2121), 2, (byte)(mode[0] & ~0x04 | (want[OtherSet.Invert] == 1 ? 0x04 : 0)), mode[1], mode[2]);
        });
        send(OtherSet.Thumb, () =>
        {
            var now = device.Call(device.F(0x2150), 1);
            device.Call(device.F(0x2150), 2, now[0], (byte)want[OtherSet.Thumb], 0);
        });
        var lightSettings = new[] { OtherSet.Light, OtherSet.Auto, OtherSet.Level };
        bool backlight = lightSettings.Any(setting => want[setting] >= 0 && want[setting] != sent[setting]);
        if (backlight)
        {
            try
            {
                // The keyboard stores this write. Durations go back as read; the level is sent only in mode 3, else 0, like Solaar.
                var config = device.Call(device.F(0x1982), 0);
                int mode = want[OtherSet.Auto] == 1 ? 1 : want[OtherSet.Auto] == 0 || want[OtherSet.Level] >= 0 && want[OtherSet.Level] != sent[OtherSet.Level] ? 3 : config[1] >> 3 & 3;
                byte options = (byte)(config[1] & ~0x18 | mode << 3);
                byte level = (byte)(mode != 3 ? 0 : want[OtherSet.Level] >= 0 ? want[OtherSet.Level] : config[5]);
                bool on = want[OtherSet.Light] >= 0 ? want[OtherSet.Light] == 1 : (config[0] & 1) != 0;
                device.Call(device.F(0x1982), 1, (byte)(on ? 1 : 0), options, 0xFF, level, config[6], config[7], config[8], config[9], config[10], config[11], config[12], config[13]);
            }
            catch (HidppError) { refused.AddRange(lightSettings.Where(setting => want[setting] >= 0)); }
        }
        return refused;
    }

    public static void Switch(Hidpp device, int host) { device.Fire(device.F(0x1814), 1, (byte)host); }

    // Keeps only what this device has, in its ranges, so a preset or remembered setting from elsewhere can't send something it lacks.
    public static OtherSet Limit(OtherSet over, State state)
    {
        var limited = new OtherSet();
        for (int setting = 0; setting < OtherSet.Count; setting++)
        {
            int value = over[setting];
            if (value < 0 || state.Set[setting] < 0) continue;
            switch (setting)
            {
                case OtherSet.Bright: value = Math.Min(state.BrightMax, value); break;
                case OtherSet.Dpi: value = state.Dpis[Nearest(state.Dpis, value)]; break;
                case OtherSet.Shift: if (value > 50 && value != 255) value = -1; break;
                case OtherSet.Level: value = Math.Min(state.Levels - 1, value); break;
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

class PageOther : DevicePage
{
    class Part { public string Title; public bool Half; public Func<Box, int, int> Fill; }

    // cur is on the screen, sent is on the device, stored is what was read at load, and remembered holds only what the user changed: it's sent again at each load.
    OtherSet cur = new OtherSet(), stored, sent, remembered, rememberedAtLoad;
    Other.State state;
    bool loading, dirty;
    int seq;
    readonly Timer debounce = new Timer { Interval = 120 };

    Slider bright, dpi, shift, level;
    Label brightValue, dpiValue, shiftValue, levelValue, shiftHead, levelHead;
    Toggle shiftOn, invert, thumb, light, auto;
    Seg hosts;
    Box hostsBox;

    internal override bool Busy { get { return debounce.Enabled; } }

    string Remembered { get { return "device." + found.Slug; } }

    string Noun { get { return found.Kind == "mouse" || found.Kind == "keyboard" ? found.Kind : "device"; } }

    public PageOther(Found found, Hidpp fake, MouseEventHandler drag) : base(found, fake, drag, found.Short, "device", "profile." + found.Slug + ".")
    {
        debounce.Tick += (sender, e) => { debounce.Stop(); Send(); };
        remembered = OtherSet.FromText(Settings.Get(Remembered));
        rememberedAtLoad = remembered.Clone();
        var parts = new List<Part>();
        if (found.Has(0x8040)) parts.Add(new Part { Title = found.Kind == "device" ? "Screens" : "Brightness", Fill = Brightness });
        if (found.Has(0x2201)) parts.Add(new Part { Title = "Pointer", Fill = Pointer });
        if (found.Has(0x2110) || found.Has(0x2111) || found.Has(0x2121)) parts.Add(new Part { Title = "Scroll wheel", Half = true, Fill = Wheel });
        if (found.Has(0x2150)) parts.Add(new Part { Title = "Thumb wheel", Half = true, Fill = ThumbWheel });
        if (found.Has(0x1982)) parts.Add(new Part { Title = "Backlight", Half = true, Fill = Backlight });
        if (found.Has(0x1814)) parts.Add(new Part { Title = "Easy-Switch", Fill = EasySwitch });
        Lay(parts);
        Sync();
        if (remembered.ToText() != "") Load();
    }

    void Lay(List<Part> parts)
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

    Box Section(string title, int x, int y, int width)
    {
        var box = Ui.Add(this, new Box(), x, y, width, 40);
        Ui.Text(box, title, 14, 12, () => Theme.Dim, 9f, Ui.Semi);
        return box;
    }

    Slider SliderRow(Box box, string name, int y, int width, out Label head, out Label value)
    {
        head = Ui.Text(box, name, 14, y, () => Theme.Dim, 9f);
        value = Value(box, 14, y, width);
        return Ui.Add(box, new Slider { AccessibleName = name }, 14, y + 20, width, 24);
    }

    Toggle ToggleRow(Box box, string name, string note, int y, int width)
    {
        var toggle = Ui.Add(box, new Toggle { AccessibleName = name }, 14, y + 2, 34, 18);
        toggle.Follow(Ui.Text(box, name, 60, y, () => Theme.Text, 9.75f));
        if (note != null) Hint(box, note, 60, y + 22, width - 46, 36);
        return toggle;
    }

    static Label Hint(Box box, string text, int x, int y, int width, int height)
    {
        var label = Ui.Text(box, text, x, y, () => Theme.Dim, 9f);
        label.AutoSize = false;
        label.Size = new Size(width, height);
        return label;
    }

    int Brightness(Box box, int width)
    {
        Label head;
        bright = SliderRow(box, "Brightness", 40, width, out head, out brightValue);
        bright.Changed += (sender, e) => Change(OtherSet.Bright, bright.Value);
        Hint(box, found.Kind == "device" ? "How bright the key screens are. Logi Options+ may set it again when it starts." : "Logi Options+ may set it again when it starts.", 14, 98, width, 20);
        return 118;
    }

    int Pointer(Box box, int width)
    {
        Label head;
        dpi = SliderRow(box, "Speed", 40, width, out head, out dpiValue);
        dpi.Step = 1;
        dpi.Changed += (sender, e) => { if (state != null && dpi.Value < state.Dpis.Length) Change(OtherSet.Dpi, state.Dpis[dpi.Value]); };
        return 84;
    }

    int Wheel(Box box, int width)
    {
        int y = 40;
        if (found.Has(0x2110) || found.Has(0x2111))
        {
            shiftOn = ToggleRow(box, "SmartShift", "Switches from ratchet to free spin when you scroll fast.", y, width);
            shiftOn.Changed += () => Change(OtherSet.Shift, shiftOn.Checked ? shift.Value + 1 : 255);
            shift = SliderRow(box, "Switch to free spin at", y + 62, width, out shiftHead, out shiftValue);
            shift.Max = 49;
            shift.Step = 1;
            shift.Changed += (sender, e) => Change(OtherSet.Shift, shift.Value + 1);
            y += 118;
        }
        if (found.Has(0x2121))
        {
            invert = ToggleRow(box, "Invert scrolling", null, y, width);
            invert.Changed += () => Change(OtherSet.Invert, invert.Checked ? 1 : 0);
            y += 34;
        }
        return y;
    }

    int ThumbWheel(Box box, int width)
    {
        thumb = ToggleRow(box, "Invert", null, 40, width);
        thumb.AccessibleName = "Invert the thumb wheel";
        thumb.Changed += () => Change(OtherSet.Thumb, thumb.Checked ? 1 : 0);
        return 74;
    }

    int Backlight(Box box, int width)
    {
        light = ToggleRow(box, "Backlight", null, 40, width);
        light.Changed += () => Change(OtherSet.Light, light.Checked ? 1 : 0);
        auto = ToggleRow(box, "Automatic", "Dims by itself with the room's light.", 74, width);
        auto.Changed += () => Change(OtherSet.Auto, auto.Checked ? 1 : 0);
        level = SliderRow(box, "Level", 136, width, out levelHead, out levelValue);
        level.Step = 1;
        level.Changed += (sender, e) => Change(OtherSet.Level, level.Value);
        return 192;
    }

    int EasySwitch(Box box, int width)
    {
        hostsBox = box;
        hosts = Ui.Add(box, new Seg { AccessibleName = "Computer" }, 14, 36, width, 32);
        hosts.Picked += AskSwitch;
        Hint(box, "Switching sends the " + Noun + " to that computer, like its Easy-Switch button.", 14, 76, width, 20);
        return 96;
    }

    string HostName(int host)
    {
        var name = state != null && host < state.Names.Length ? state.Names[host] : null;
        return (host + 1) + (state != null && host < state.Paired.Length && !state.Paired[host] ? " · –" : name == null ? "" : " · " + name);
    }

    void AskSwitch(int host)
    {
        if (state == null || host == state.Host) return;
        hosts.Index = state.Host;
        hosts.Invalidate();
        if (!state.Paired[host]) { Say("Nothing is paired on computer " + (host + 1) + " yet. Pair it with the Easy-Switch button first."); return; }
        var name = state.Names[host] ?? "computer " + (host + 1);
        Say("Switch the " + Noun + " to " + name + "? It leaves this PC until you switch back" + (state.PairedKnown ? "." : ". If nothing is paired there, press its Easy-Switch button to come back."), false, () => Switch(host), "Switch");
    }

    void Switch(int host)
    {
        Did("Switch to computer " + (host + 1));
        Do(device => Other.Switch(device, host), () => Say("Switched to computer " + (host + 1) + ". Press the Easy-Switch button to come back."));
    }

    void Change(int setting, int value)
    {
        if (loading || state == null || cur[setting] < 0) return;
        cur[setting] = value;
        remembered[setting] = value;
        Changed();
    }

    void Changed()
    {
        dirty = true;
        seq++;
        Edited();
        Sync();
        Calm();
        debounce.Stop();
        debounce.Start();
    }

    void Sync()
    {
        loading = true;
        bool ready = state != null;
        if (bright != null)
        {
            bool on = ready && cur[OtherSet.Bright] >= 0;
            bright.Enabled = on;
            bright.Max = ready ? state.BrightMax : 100;
            bright.Value = Math.Max(0, cur[OtherSet.Bright]);
            brightValue.Text = !on ? "" : state.BrightMax == 100 ? cur[OtherSet.Bright] + "%" : cur[OtherSet.Bright] + " of " + state.BrightMax;
        }
        if (dpi != null)
        {
            bool on = ready && cur[OtherSet.Dpi] > 0;
            dpi.Enabled = on && state.Dpis.Length > 1;
            dpi.Max = on ? Math.Max(1, state.Dpis.Length - 1) : 100;
            dpi.Value = on ? Other.Nearest(state.Dpis, cur[OtherSet.Dpi]) : 0;
            dpiValue.Text = on ? cur[OtherSet.Dpi] + " DPI" : "";
        }
        if (shiftOn != null)
        {
            int threshold = cur[OtherSet.Shift];
            bool has = ready && threshold >= 0, on = has && threshold < 255;
            shiftOn.Enabled = has;
            shiftOn.Checked = on;
            shift.Enabled = on;
            shift.Value = Math.Max(0, Math.Min(49, (on ? threshold : ready ? state.ShiftDefault : 10) - 1));
            Shade(shiftHead, on);
            shiftValue.Text = on ? (threshold / 4.0).ToString("0.##", CultureInfo.InvariantCulture) + " turns a second" : "";
        }
        if (invert != null) { invert.Enabled = ready && cur[OtherSet.Invert] >= 0; invert.Checked = cur[OtherSet.Invert] == 1; }
        if (thumb != null) { thumb.Enabled = ready && cur[OtherSet.Thumb] >= 0; thumb.Checked = cur[OtherSet.Thumb] == 1; }
        if (light != null)
        {
            bool lit = cur[OtherSet.Light] == 1;
            light.Enabled = ready && cur[OtherSet.Light] >= 0;
            light.Checked = lit;
            auto.Enabled = ready && cur[OtherSet.Auto] >= 0 && lit;
            auto.Checked = cur[OtherSet.Auto] == 1;
            bool manual = ready && cur[OtherSet.Level] >= 0 && lit && cur[OtherSet.Auto] != 1;
            level.Enabled = manual;
            level.Max = ready && state.Levels > 1 ? state.Levels - 1 : 1;
            level.Value = Math.Max(0, cur[OtherSet.Level]);
            Shade(levelHead, manual);
            levelValue.Text = ready && cur[OtherSet.Level] >= 0 ? cur[OtherSet.Level] + 1 + " of " + state.Levels : "";
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

    public override void Restyle() { Sync(); }

    protected override bool Unsaved { get { return dirty; } }

    protected override string Current() { return cur.ToText(); }

    protected override void Use(string text)
    {
        if (state == null) return;
        var preset = Other.Limit(OtherSet.FromText(text), state);
        for (int setting = 0; setting < OtherSet.Count; setting++) if (preset[setting] >= 0 && preset[setting] != cur[setting]) remembered[setting] = preset[setting];
        cur.Merge(preset);
        Changed();
    }

    protected override void Revert()
    {
        if (stored == null) return;
        cur = stored.Clone();
        remembered = rememberedAtLoad.Clone();
        Changed();
        dirty = false;
        Calm();
    }

    protected override void Calm()
    {
        if (state == null) { Say(""); return; }
        Say((dirty ? "Applied to the " + Noun + ". " : "") + (Noun == "device" ? "Everything else stays in Logi Options+." : "Buttons, gestures and per-app settings stay in Logi Options+."));
    }

    // A refused setting goes back to what the device has, and isn't remembered or sent again.
    void Refused(List<int> refused, OtherSet device)
    {
        foreach (var setting in refused) { cur[setting] = device[setting]; remembered[setting] = -1; }
        Settings.Set(Remembered, remembered.ToText());
        if (refused.Count == 0) { Calm(); return; }
        Sync();
        Say("The " + Noun + " refused the " + string.Join(" and the ", refused.Select(Other.Name).Distinct()) + ".", true);
    }

    void Send()
    {
        var want = cur.Clone();
        var before = sent.Clone();
        var at = state;
        int sequence = ++seq;
        List<int> refused = null;
        Do(device =>
        {
            if (sequence != seq) return;
            refused = Other.Apply(device, at, want, sent);
            foreach (var setting in refused) want[setting] = before[setting];
            sent = want;
        }, () => { if (sequence == seq && refused != null) Refused(refused, before); });
    }

    public override void Load()
    {
        if (debounce.Enabled) { debounce.Stop(); Send(); }
        Say("Reading the " + Noun + "…");
        Other.State read = null;
        var keep = remembered.Clone();
        List<int> refused = null;
        OtherSet before = null;
        int sequence = seq;
        Do(device =>
        {
            read = Other.Read(device, found);
            before = read.Set.Clone();
            var want = read.Set.Clone();
            want.Merge(Other.Limit(keep, read));
            // With Automatic on, the level follows the room's light, so the one remembered from manual mode isn't sent.
            if (want[OtherSet.Auto] == 1) want[OtherSet.Level] = read.Set[OtherSet.Level];
            if (want.Same(read.Set)) return;
            refused = Other.Apply(device, read, want, read.Set);
            foreach (var setting in refused) want[setting] = before[setting];
            read.Set = want;
        }, () =>
        {
            state = read;
            stored = read.Set.Clone();
            if (sequence == seq)
            {
                cur = read.Set.Clone();
                sent = read.Set.Clone();
                dirty = false;
            }
            Sync();
            Loaded();
            if (refused != null && refused.Count > 0) Refused(refused, before);
            rememberedAtLoad = remembered.Clone();
        });
    }
}
