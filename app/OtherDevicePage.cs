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
    volatile bool releasePending;
    // What the keys given back (ReleaseIfStopped) came to, set on the work queue: the status line until the next change, so the
    // screen's own read doesn't clear it.
    volatile string releasedNote;
    // Set by an Easy-Switch, so the device's return reads it again.
    volatile bool switched;
    int sequence;
    readonly Timer debounce = new Timer { Interval = 120 };

    // A section for each feature the device has, a setting per row; Easy-Switch is left out once read with one computer.
    readonly RowLayout rows;
    Slider bright, dpi, shift, level;
    Label brightValue, dpiValue, shiftValue, levelValue;
    RowText shiftText, autoText, levelText, effectText;
    Toggle shiftOn, invert, thumb, light, auto, saver, mediaKeys;
    Dropdown effect;
    readonly Toggle[] disableToggles = new Toggle[OtherDevice.DisableBits.Length];
    readonly int[] disableRows = new int[OtherDevice.DisableBits.Length];
    int effectRow, saverRow, mediaKeysRow;
    readonly RowText[] delayTexts = new RowText[3];
    readonly Dropdown[] delayDropdowns = new Dropdown[3];
    SegmentedPicker hosts;
    RoundedBox hostsBox, backlightBox, keysBox;
    int autoRow, levelRow;
    readonly int[] delayRows = new int[3];
    readonly KeyPicture picture;
    readonly BacklightPicture backlightPicture;
    KeyLayout isoModel;
    bool isoShown, pageShown;
    const int HostsWidth = 336, PictureHeight = 168;

    internal override bool Busy { get { return debounce.Enabled; } }

    protected override bool ReopensWhenFound { get { return switched; } }

    // From the work queue, when the connection opens or the receiver says the device is back.
    protected override void Opened(Hidpp hidpp)
    {
        if (switched && IsHandleCreated) BeginInvoke((Action)(() => { switched = false; Load(); }));
    }

    // The picture's effect moves only while the screen is shown.
    public override void Shown() { pageShown = true; base.Shown(); RefreshControls(); }

    public override void Hidden() { pageShown = false; base.Hidden(); RefreshControls(); }

    protected override void Dispose(bool disposing)
    {
        if (disposing && backlightPicture != null) backlightPicture.Dispose();
        base.Dispose(disposing);
    }

    string Remembered { get { return "device." + found.SettingsKey; } }

    string Noun { get { return found.Kind == "mouse" || found.Kind == "keyboard" ? found.Kind : "device"; } }

    public OtherDevicePage(FoundDevice found, Hidpp fake)
        : base(found, fake, found.ShortName, "device", "profile." + found.SettingsKey + ".")
    {
        debounce.Tick += (sender, e) => { debounce.Stop(); Send(); };
        remembered = OtherSettings.FromText(Settings.Get(Remembered));
        rememberedAtLoad = remembered.Clone();
        var model = OtherDevice.Picture(found);
        // The picture stays above the rows, which scroll under it. It only shows the backlight, so it takes no clicks.
        if (model != null)
        {
            var ansi = KeyLayout.Load(model);
            isoModel = ansi.Iso();
            picture = Ui.Add(this, new KeyPicture(ansi) { Live = true, Enabled = false }, 20, BodyTop, Rows.Width, PictureHeight);
            backlightPicture = new BacklightPicture(picture);
        }
        rows = new RowLayout((picture != null ? UseBody(BodyTop + PictureHeight + 12) : UseBody()).Sheet);
        if (found.Has(0x8040)) Brightness();
        if (found.Has(0x2201)) Pointer();
        if (found.Has(0x2110) || found.Has(0x2111) || found.Has(0x2121)) Wheel();
        if (found.Has(0x2150)) ThumbWheel();
        if (found.Has(0x1982)) Backlight();
        if (found.Has(0x40A3) || found.Has(0x4521)) KeySettings();
        if (found.Has(0x1814)) EasySwitch();
        RefreshControls();
        // Keys a stopped Logi Options+ left diverted are given back at the first read, which a screen not shown also makes
        // (SendsAgain), so the preset it sends again isn't skipped.
        releasePending = found.Has(0x1B04);
        if (remembered.ToText() != "") Load();
    }

    protected override bool SendsAgain { get { return base.SendsAgain || releasePending; } }

    // Logi Options+ installed but not running: keys it diverted are dead until the device reconnects. Off in the demo; the traces
    // replace it.
    public static Func<bool> OptionsPlusStopped = () => LogitechSoftware.ByIds(new[] { "optionsplus" })[0]
        .State(LogitechSoftware.RunningProcesses()) == SoftwareState.Stopped;

    // On the work queue. The check lists the PC's processes, so it stays off the UI thread.
    void ReleaseIfStopped(Hidpp device)
    {
        bool stopped;
        try { stopped = OptionsPlusStopped(); }
        catch (Exception ex) { Log.Add(DeviceLabel, "error", "Couldn't look for Logi Options+: " + ex.Message); stopped = false; }
        if (!stopped) return;
        int released = OtherDevice.ReleaseDiverted(device);
        if (released > 0) releasedNote = released + (released == 1 ? " key" : " keys") + " Logi Options+ had taken over work again.";
    }

    // When Settings stops Logi Options+. A device that's away (switched, asleep) fails quietly: it gets its keys back when it
    // reconnects anyway.
    public void ReleaseKeys()
    {
        if (!found.Has(0x1B04) || switched) return;
        RunOnDevice(ReleaseIfStopped, () => { if (releasedNote != null) ShowStatus(releasedNote); }, message => { });
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
        var box = backlightBox = rows.Section("Backlight", out heading);
        // Plugged in, the MX Keys S's hand sensor seems off: turned on, it stays dark until a key is pressed.
        // On battery it lights at once (tried on a real one, 2026-11). The MX Mechanical Mini does the same plugged in (tried on a real one, 2026-11-26).
        int y = rows.Row(box);
        Rows.Row(box, y, "Backlight", "Plugged in, the keys light up when you next type");
        light = Rows.Toggle(box, y, "Backlight");
        light.Changed += () => Change(OtherSettings.Light, light.Checked ? 1 : 0);
        y = autoRow = rows.Row(box);
        autoText = Rows.Row(box, y, "Automatic", "Dims by itself with the room's light");
        auto = Rows.Toggle(box, y, "Automatic");
        auto.Changed += () => Change(OtherSettings.Auto, auto.Checked ? 1 : 0);
        y = levelRow = rows.Row(box);
        levelText = Rows.Row(box, y, "Level", "How bright the keys are, when not automatic");
        level = Rows.Slider(box, y, "Level", out levelValue);
        level.Step = 1;
        // Its level shows while Automatic sets it.
        level.ZeroWhenOff = false;
        level.Changed += (sender, e) => Change(OtherSettings.Level, level.Value);
        y = effectRow = rows.Row(box);
        effectText = Rows.Row(box, y, "Effect", "Fn and the light key switch it too");
        effect = Rows.Dropdown(box, y, "Effect");
        effect.Picked += picked => Change(OtherSettings.Effect, EffectChoices()[picked]);
        string[] names = { "Hands away", "Hands near", "Plugged in" };
        string[] hints = { "Fades after this long without hands near", "Fades after this long with hands near", "Fades after this long, plugged in" };
        for (int index = 0; index < names.Length; index++)
        {
            int setting = OtherSettings.Delays[index];
            y = delayRows[index] = rows.Row(box);
            delayTexts[index] = Rows.Row(box, y, names[index], hints[index]);
            delayDropdowns[index] = Rows.Dropdown(box, y, "Fades after, " + names[index].ToLowerInvariant());
            delayDropdowns[index].Picked += picked => Change(setting, DelayChoices(current[setting])[picked]);
        }
        y = saverRow = rows.Row(box);
        Rows.Row(box, y, "Battery saving", "Turns the backlight off below 10% battery");
        saver = Rows.Toggle(box, y, "Battery saving");
        saver.Changed += () => Change(OtherSettings.Saver, saver.Checked ? 1 : 0);
    }

    // The keyboard's own numbers, as Logi Options+ shows them: 0 is off (an MX Mechanical Mini, 2026-11-26).
    static string LevelText(int value, int levels) { return value + " of " + (levels - 1); }

    int[] EffectChoices()
    {
        return OtherDevice.EffectIds.Where(id => state == null || (state.Effects >> id & 1) != 0).ToArray();
    }

    void KeySettings()
    {
        Label heading;
        keysBox = rows.Section("Keys", out heading);
        if (found.Has(0x40A3))
        {
            int y = mediaKeysRow = rows.Row(keysBox);
            Rows.Row(keysBox, y, "F1–F12 as media keys", "Off, they type F1–F12. Fn and Esc switch it too");
            mediaKeys = Rows.Toggle(keysBox, y, "F1–F12 as media keys");
            mediaKeys.Changed += () => Change(OtherSettings.MediaKeys, mediaKeys.Checked ? 1 : 0);
        }
        if (!found.Has(0x4521)) return;
        for (int index = 0; index < OtherDevice.DisableBits.Length; index++)
        {
            int bit = OtherDevice.DisableBits[index];
            string name = "Disable " + OtherDevice.DisableNames[index];
            int y = disableRows[index] = rows.Row(keysBox);
            Rows.Row(keysBox, y, name, "The key does nothing while this is on");
            var toggle = disableToggles[index] = Rows.Toggle(keysBox, y, name);
            toggle.Changed += () => Change(OtherSettings.Disabled,
                toggle.Checked ? current[OtherSettings.Disabled] | bit : current[OtherSettings.Disabled] & ~bit);
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
        RunOnDevice(device => OtherDevice.Switch(device, host), () =>
        {
            // The picker shows where the device is now, until it comes back (Opened) or the page is shown again.
            switched = true;
            state.Host = host;
            hosts.Index = host;
            hosts.Invalidate();
            ShowStatus("Switched to computer " + (host + 1) + ". Press the Easy-Switch button to come back.");
        });
    }

    void Change(int setting, int value)
    {
        if (loading || state == null || current[setting] < 0) return;
        current[setting] = value;
        if (Remembers(setting)) remembered[setting] = value;
        Changed();
    }

    // The keyboard keeps these and changes them with its own keys (Fn and Esc, Fn and the light key): sent again at each load, they
    // would undo a change made there.
    static bool Remembers(int setting) { return setting != OtherSettings.Effect && setting != OtherSettings.MediaKeys; }

    void Changed()
    {
        dirty = true;
        releasedNote = null;
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
            levelValue.Text = ready && current[OtherSettings.Level] >= 0 ? LevelText(current[OtherSettings.Level], state.Levels) : "";
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
            var effects = EffectChoices();
            bool hasEffect = ready && current[OtherSettings.Effect] >= 0;
            effect.Items.Clear();
            effect.Items.AddRange(effects.Select(id => OtherDevice.EffectNames[Array.IndexOf(OtherDevice.EffectIds, id)]));
            effect.Index = Array.IndexOf(effects, current[OtherSettings.Effect]);
            effect.Enabled = hasEffect && lit;
            effectText.Enable(hasEffect && lit);
            saver.Enabled = ready && current[OtherSettings.Saver] >= 0;
            saver.Checked = current[OtherSettings.Saver] == 1;
            // Once read, the rows the keyboard doesn't have go: on its first firmware, the MX Mechanical Mini's backlight is only on or off.
            rows.ShowRow(backlightBox, effectRow, !ready || hasEffect);
            rows.ShowRow(backlightBox, saverRow, !ready || current[OtherSettings.Saver] >= 0);
            rows.ShowRow(backlightBox, autoRow, !ready || current[OtherSettings.Auto] >= 0);
            rows.ShowRow(backlightBox, levelRow, !ready || current[OtherSettings.Level] >= 0);
            foreach (int row in delayRows) rows.ShowRow(backlightBox, row, !ready || current[OtherSettings.Away] > 0);
        }
        if (keysBox != null)
        {
            if (mediaKeys != null)
            {
                mediaKeys.Enabled = ready && current[OtherSettings.MediaKeys] >= 0;
                mediaKeys.Checked = current[OtherSettings.MediaKeys] == 1;
                rows.ShowRow(keysBox, mediaKeysRow, !ready || current[OtherSettings.MediaKeys] >= 0);
            }
            for (int index = 0; index < disableToggles.Length; index++)
            {
                if (disableToggles[index] == null) continue;
                bool can = ready && current[OtherSettings.Disabled] >= 0 && (state.Disableable & OtherDevice.DisableBits[index]) != 0;
                disableToggles[index].Enabled = can;
                disableToggles[index].Checked = can && (current[OtherSettings.Disabled] & OtherDevice.DisableBits[index]) != 0;
                rows.ShowRow(keysBox, disableRows[index], !ready || can);
            }
            rows.ShowSection(keysBox, !ready || current[OtherSettings.MediaKeys] >= 0 || current[OtherSettings.Disabled] >= 0);
        }
        if (picture != null)
        {
            if (!isoShown && isoModel != null && ready && KeyLayout.IsIso(state.LayoutCode))
            {
                isoShown = true;
                picture.SetModel(isoModel);
            }
            // As bright as the level (0 is off); full when the keyboard has no levels.
            int shownLevel = current[OtherSettings.Level];
            double brightness = ready && shownLevel >= 0 && state.Levels > 1 ? shownLevel / (double)(state.Levels - 1) : 1;
            backlightPicture.Show(ready && current[OtherSettings.Light] == 1, brightness, current[OtherSettings.Effect], pageShown);
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
            if (preset[setting] >= 0 && preset[setting] != current[setting] && Remembers(setting)) remembered[setting] = preset[setting];
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
        ShowStatus(state != null && dirty ? "Applied to the " + Noun + "." : releasedNote ?? "");
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
            if (releasePending) { releasePending = false; ReleaseIfStopped(device); }
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

// An MX keyboard's backlight on its picture: white, as bright as the level, with the keyboard's effect, as seen on an MX Mechanical
// Mini (2026-11-26): Contrast dims the letters, digits, F1–F4, F9–F12 and the arrows, Breathing fades all the keys in and out, Waves sweep from left to right,
// Reaction lights a key when it's pressed, Random lights keys at random. The speeds are guesses. Only while the screen is shown:
// Reaction's key presses come from a hook (KeyPresses) that's removed when it stops.
class BacklightPicture : IDisposable
{
    const int Contrast = 3, Breathing = 2, Waves = 6, Reaction = 4, Random = 5;
    const double BreathSeconds = 4, WaveSeconds = 3, FadeSeconds = 1, TwinkleSeconds = 1.5, ContrastDim = 0.35;
    readonly KeyPicture picture;
    readonly Timer timer = new Timer { Interval = 40 };
    readonly System.Random random = new System.Random();
    readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
    KeyPresses presses;
    // When each key last lit up, for Reaction and Random.
    double[] litAt = new double[0];
    bool lit;
    double brightness;
    int effect;

    public BacklightPicture(KeyPicture picture)
    {
        this.picture = picture;
        timer.Tick += (s, e) => Tick();
    }

    // Minimizing doesn't hide the screen (Hidden), so the ticks idle meanwhile, and Reaction's hook goes until it's back.
    void Tick()
    {
        var form = picture.FindForm();
        bool minimized = form != null && form.WindowState == FormWindowState.Minimized;
        if (minimized && presses != null) { presses.Dispose(); presses = null; }
        if (minimized) return;
        if (effect == Reaction && presses == null) presses = new KeyPresses();
        Draw();
    }

    public void Show(bool on, double level, int newEffect, bool shown)
    {
        lit = on;
        brightness = level;
        effect = newEffect;
        bool moving = shown && on && (effect == Breathing || effect == Waves || effect == Reaction || effect == Random);
        if (moving && !timer.Enabled) timer.Start();
        if (!moving) timer.Stop();
        if (moving && effect == Reaction && presses == null) presses = new KeyPresses();
        if (!(moving && effect == Reaction) && presses != null) { presses.Dispose(); presses = null; }
        Draw();
    }

    void Draw()
    {
        double seconds = clock.Elapsed.TotalSeconds;
        if (litAt.Length != picture.Count) litAt = Enumerable.Repeat(-1000.0, picture.Count).ToArray();
        if (presses != null)
            foreach (int usage in presses.Take())
            {
                int key = KeyOf(usage);
                // Scan code 2B is \ on ANSI keyboards and # on ISO ones (as Echo does).
                if (key < 0 && usage == 0x31) key = KeyOf(0x32);
                if (key >= 0) litAt[key] = seconds;
            }
        var colors = new Color[picture.Count];
        for (int key = 0; key < colors.Length; key++)
            colors[key] = Ui.Mix(Theme.KeyOff, Color.White, lit ? brightness * Math.Max(0, Math.Min(1, Factor(key, seconds))) : 0);
        picture.ShowLive(colors, new bool[colors.Length]);
    }

    int KeyOf(int usage)
    {
        int id = GameModeKeys.LightingId(usage);
        for (int key = 0; key < picture.Count; key++) if (id >= 0 && picture.IdOf(key) == id) return key;
        return -1;
    }

    // The key's share of the level, 0 to 1.
    double Factor(int key, double seconds)
    {
        switch (effect)
        {
            case Contrast:
                int id = picture.IdOf(key);
                // Dim, as on the Mini: letters and digits (lighting ids 01–24), the punctuation among them (2A–35 but `, and ISO's <>,
                // 61), F1–F4 and F9–F12 (37–3A, 3F–42) and the arrows (4C–4F).
                bool dim = (id >= 0x01 && id <= 0x24) || (id >= 0x2A && id <= 0x35 && id != 0x32) || id == 0x61
                    || (id >= 0x37 && id <= 0x3A) || (id >= 0x3F && id <= 0x42) || (id >= 0x4C && id <= 0x4F);
                return dim ? ContrastDim : 1;
            case Breathing:
                return 0.5 - 0.5 * Math.Cos(2 * Math.PI * seconds / BreathSeconds);
            case Waves:
                return 0.5 + 0.5 * Math.Cos(2 * Math.PI * (picture.XOf(key) - seconds / WaveSeconds));
            case Reaction:
                return 1 - (seconds - litAt[key]) / FadeSeconds;
            case Random:
                double age = seconds - litAt[key];
                // About one key in six lit at a time.
                if (age >= TwinkleSeconds && random.NextDouble() < timer.Interval / 1000.0 / TwinkleSeconds / 5)
                {
                    litAt[key] = seconds;
                    age = 0;
                }
                return age < TwinkleSeconds ? Math.Sin(Math.PI * age / TwinkleSeconds) : 0;
            default:
                return 1;
        }
    }

    public void Dispose()
    {
        timer.Dispose();
        if (presses != null) presses.Dispose();
    }
}
