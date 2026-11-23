using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

class OtherDevicePage : DevicePage
{
    class SettingBox { public string Title; public bool Half; public Func<RoundedBox, int, int> Fill; }

    // current is on the screen, sent is on the device, stored is what was read at load, and remembered holds only what the user changed:
    // it's sent again at each load.
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

    public OtherDevicePage(FoundDevice found, Hidpp fake, MouseEventHandler drag)
        : base(found, fake, drag, found.ShortName, "device", "profile." + found.SettingsKey + ".")
    {
        debounce.Tick += (sender, e) => { debounce.Stop(); Send(); };
        remembered = OtherSettings.FromText(Settings.Get(Remembered));
        rememberedAtLoad = remembered.Clone();
        var parts = new List<SettingBox>();
        if (found.Has(0x8040)) parts.Add(new SettingBox { Title = found.Kind == "device" ? "Screens" : "Brightness", Fill = Brightness });
        if (found.Has(0x2201)) parts.Add(new SettingBox { Title = "Pointer", Fill = Pointer });
        if (found.Has(0x2110) || found.Has(0x2111) || found.Has(0x2121))
            parts.Add(new SettingBox { Title = "Scroll wheel", Half = true, Fill = Wheel });
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
        AddHint(box,
            found.Kind == "device"
                ? "How bright the key screens are. Logi Options+ may set it again when it starts."
                : "Logi Options+ may set it again when it starts.",
            14, 98, width, 20);
        return 118;
    }

    int Pointer(RoundedBox box, int width)
    {
        Label head;
        dpi = SliderRow(box, "Speed", 40, width, out head, out dpiValue);
        dpi.Step = 1;
        dpi.Changed += (sender, e) =>
        {
            if (state != null && dpi.Value < state.Dpis.Length) Change(OtherSettings.Dpi, state.Dpis[dpi.Value]);
        };
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
        // Plugged in, the MX Keys S's hand sensor seems off: turned on, it stays dark until a key is pressed.
        // On battery it lights at once (an owner, 2026-11).
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
            delayDropdowns[index] = Ui.Add(box, new Dropdown { AccessibleName = "Fades after, " + names[index].ToLowerInvariant() },
                x, 262, column, 30);
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
        return (host + 1)
            + (state != null && host < state.Paired.Length && !state.Paired[host] ? ": –" : name == null ? "" : ": " + name);
    }

    void AskSwitch(int host)
    {
        if (state == null || host == state.Host) return;
        hosts.Index = state.Host;
        hosts.Invalidate();
        if (!state.Paired[host])
        {
            ShowStatus("Nothing is paired on computer " + (host + 1) + " yet. Pair it with the Easy-Switch button first.");
            return;
        }
        var name = state.Names[host] ?? "computer " + (host + 1);
        ShowStatus("Switch the " + Noun + " to " + name + "? It leaves this PC until you switch back"
                + (state.PairedKnown ? "." : ". If nothing is paired there, press its Easy-Switch button to come back."),
            false, () => Switch(host), "Switch");
    }

    void Switch(int host)
    {
        LogUserAction("Switch to computer " + (host + 1));
        RunOnDevice(device => OtherDevice.Switch(device, host),
            () => ShowStatus("Switched to computer " + (host + 1) + ". Press the Easy-Switch button to come back."));
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
        // While a slider moves, sent at most every 120 ms rather than once it stops.
        if (!debounce.Enabled) debounce.Start();
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
            brightValue.Text = !on ? ""
                : state.BrightMax == 100 ? current[OtherSettings.Bright] + "%"
                : current[OtherSettings.Bright] + " of " + state.BrightMax;
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
        if (invert != null)
        {
            invert.Enabled = ready && current[OtherSettings.Invert] >= 0;
            invert.Checked = current[OtherSettings.Invert] == 1;
        }
        if (thumb != null)
        {
            thumb.Enabled = ready && current[OtherSettings.Thumb] >= 0;
            thumb.Checked = current[OtherSettings.Thumb] == 1;
        }
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
    static int[] DelayChoices(int seconds)
    {
        return seconds <= 0 || DelaySteps.Contains(seconds)
            ? DelaySteps
            : DelaySteps.Concat(new[] { seconds }).OrderBy(step => step).ToArray();
    }

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
        for (int setting = 0; setting < OtherSettings.Count; setting++)
            if (preset[setting] >= 0 && preset[setting] != current[setting]) remembered[setting] = preset[setting];
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
