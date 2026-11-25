using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

class OtherDevicePage : DevicePage
{
    // current is on the screen, sent is on the device, stored is what was read at load, and remembered holds only what the user changed:
    // it's sent again at each load.
    OtherSettings current = new OtherSettings(), stored, sent, remembered, rememberedAtLoad;
    OtherDevice.State state;
    bool loading, dirty;
    int sequence;
    readonly Timer debounce = new Timer { Interval = 120 };

    // A section for each feature the device has, a setting per row; Easy-Switch is left out once read with one computer.
    readonly RowLayout rows;
    Slider bright, dpi, shift, level;
    Label brightValue, dpiValue, shiftValue, levelValue;
    RowText shiftText, autoText, levelText;
    Toggle shiftOn, invert, thumb, light, auto;
    readonly RowText[] delayTexts = new RowText[3];
    readonly Dropdown[] delayDropdowns = new Dropdown[3];
    SegmentedPicker hosts;
    RoundedBox hostsBox;
    const int HostsWidth = 336;

    internal override bool Busy { get { return debounce.Enabled; } }

    string Remembered { get { return "device." + found.SettingsKey; } }

    string Noun { get { return found.Kind == "mouse" || found.Kind == "keyboard" ? found.Kind : "device"; } }

    public OtherDevicePage(FoundDevice found, Hidpp fake)
        : base(found, fake, found.ShortName, "device", "profile." + found.SettingsKey + ".")
    {
        debounce.Tick += (sender, e) => { debounce.Stop(); Send(); };
        remembered = OtherSettings.FromText(Settings.Get(Remembered));
        rememberedAtLoad = remembered.Clone();
        rows = new RowLayout(UseBody().Sheet);
        if (found.Has(0x8040)) Brightness();
        if (found.Has(0x2201)) Pointer();
        if (found.Has(0x2110) || found.Has(0x2111) || found.Has(0x2121)) Wheel();
        if (found.Has(0x2150)) ThumbWheel();
        if (found.Has(0x1982)) Backlight();
        if (found.Has(0x1814)) EasySwitch();
        RefreshControls();
        if (remembered.ToText() != "") Load();
    }

    void Brightness()
    {
        Label heading;
        var box = rows.Section(found.Kind == "device" ? "Screens" : "Brightness", out heading);
        int y = rows.Row(box);
        Rows.Row(box, y, "Brightness", found.Kind == "device" ? "The key screens; Logi Options+ may set it again" : "Logi Options+ may set it again when it starts");
        bright = Rows.Slider(box, y, "Brightness", out brightValue);
        bright.Changed += (sender, e) => Change(OtherSettings.Bright, bright.Value);
    }

    void Pointer()
    {
        Label heading;
        var box = rows.Section("Pointer", out heading);
        int y = rows.Row(box);
        Rows.Row(box, y, "Speed", "How far the pointer moves");
        dpi = Rows.Slider(box, y, "Speed", out dpiValue);
        dpi.Step = 1;
        // A mouse with one DPI shows it, read-only.
        dpi.ZeroWhenOff = false;
        dpi.Changed += (sender, e) =>
        {
            if (state != null && dpi.Value < state.Dpis.Length) Change(OtherSettings.Dpi, state.Dpis[dpi.Value]);
        };
    }

    void Wheel()
    {
        Label heading;
        var box = rows.Section("Scroll wheel", out heading);
        if (found.Has(0x2110) || found.Has(0x2111))
        {
            int y = rows.Row(box);
            Rows.Row(box, y, "SmartShift", "Ratchet to free spin when you scroll fast");
            shiftOn = Rows.Toggle(box, y, "SmartShift");
            shiftOn.Changed += () => Change(OtherSettings.Shift, shiftOn.Checked ? shift.Value + 1 : 255);
            y = rows.Row(box);
            shiftText = Rows.Row(box, y, "Switch to free spin at", "How fast you scroll, in turns a second");
            shift = Rows.Slider(box, y, "Switch to free spin at", out shiftValue);
            shift.Max = 49;
            shift.Step = 1;
            shift.Changed += (sender, e) => Change(OtherSettings.Shift, shift.Value + 1);
        }
        if (found.Has(0x2121))
        {
            int y = rows.Row(box);
            Rows.Row(box, y, "Invert scrolling", "The wheel scrolls the other way");
            invert = Rows.Toggle(box, y, "Invert scrolling");
            invert.Changed += () => Change(OtherSettings.Invert, invert.Checked ? 1 : 0);
        }
    }

    void ThumbWheel()
    {
        Label heading;
        var box = rows.Section("Thumb wheel", out heading);
        int y = rows.Row(box);
        Rows.Row(box, y, "Invert", "The thumb wheel scrolls the other way");
        thumb = Rows.Toggle(box, y, "Invert the thumb wheel");
        thumb.Changed += () => Change(OtherSettings.Thumb, thumb.Checked ? 1 : 0);
    }

    void Backlight()
    {
        Label heading;
        var box = rows.Section("Backlight", out heading);
        // Plugged in, the MX Keys S's hand sensor seems off: turned on, it stays dark until a key is pressed.
        // On battery it lights at once (an owner, 2026-11).
        int y = rows.Row(box);
        Rows.Row(box, y, "Backlight", "Plugged in, the keys light up when you next type");
        light = Rows.Toggle(box, y, "Backlight");
        light.Changed += () => Change(OtherSettings.Light, light.Checked ? 1 : 0);
        y = rows.Row(box);
        autoText = Rows.Row(box, y, "Automatic", "Dims by itself with the room's light");
        auto = Rows.Toggle(box, y, "Automatic");
        auto.Changed += () => Change(OtherSettings.Auto, auto.Checked ? 1 : 0);
        y = rows.Row(box);
        levelText = Rows.Row(box, y, "Level", "How bright the keys are, when not automatic");
        level = Rows.Slider(box, y, "Level", out levelValue);
        level.Step = 1;
        // Its level shows while Automatic sets it.
        level.ZeroWhenOff = false;
        level.Changed += (sender, e) => Change(OtherSettings.Level, level.Value);
        string[] names = { "Hands away", "Hands near", "Plugged in" };
        string[] hints = { "Fades after this long without hands near", "Fades after this long with hands near", "Fades after this long, plugged in" };
        for (int index = 0; index < names.Length; index++)
        {
            int setting = OtherSettings.Delays[index];
            y = rows.Row(box);
            delayTexts[index] = Rows.Row(box, y, names[index], hints[index]);
            delayDropdowns[index] = Rows.Dropdown(box, y, "Fades after, " + names[index].ToLowerInvariant());
            delayDropdowns[index].Picked += picked => Change(setting, DelayChoices(current[setting])[picked]);
        }
    }

    void EasySwitch()
    {
        Label heading;
        hostsBox = rows.Section("Easy-Switch", out heading);
        int y = rows.Row(hostsBox);
        Rows.Row(hostsBox, y, "Computer", "Switching sends the " + Noun + " there");
        hosts = Rows.Picker(hostsBox, y, "Computer", new string[0], HostsWidth);
        hosts.Picked += AskSwitch;
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
            shiftText.Enable(on);
            shiftValue.Text = on ? (threshold / 4.0).ToString("0.##", CultureInfo.InvariantCulture) + "/s" : "";
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
            autoText.Enable(auto.Enabled);
            auto.Checked = lit && current[OtherSettings.Auto] == 1;
            bool manual = ready && current[OtherSettings.Level] >= 0 && lit && current[OtherSettings.Auto] != 1;
            level.Enabled = manual;
            level.Max = ready && state.Levels > 1 ? state.Levels - 1 : 1;
            level.Value = Math.Max(0, current[OtherSettings.Level]);
            levelText.Enable(manual);
            levelValue.Text = ready && current[OtherSettings.Level] >= 0 ? current[OtherSettings.Level] + 1 + " of " + state.Levels : "";
            bool delays = ready && current[OtherSettings.Away] > 0 && lit;
            for (int index = 0; index < delayDropdowns.Length; index++)
            {
                int seconds = current[OtherSettings.Delays[index]];
                var choices = DelayChoices(seconds);
                delayDropdowns[index].Items.Clear();
                delayDropdowns[index].Items.AddRange(choices.Select(DelayText));
                delayDropdowns[index].Index = Array.IndexOf(choices, seconds);
                delayDropdowns[index].Enabled = delays;
                delayTexts[index].Enable(delays);
            }
        }
        if (hosts != null)
        {
            int count = ready ? state.Hosts : 0;
            rows.ShowSection(hostsBox, !ready || count > 1);
            hosts.Items = count > 1 ? Enumerable.Range(0, count).Select(HostName).ToArray() : new[] { "1", "2", "3" };
            hosts.Enabled = count > 1;
            hosts.Index = count > 1 ? state.Host : -1;
            hosts.Invalidate();
        }
        foreach (var control in new Control[] { bright, dpi, shift, level }) if (control != null) control.Invalidate();
        FitBody(rows.Place(Scaled, true));
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
