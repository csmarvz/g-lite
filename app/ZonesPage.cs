using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using Timer = System.Windows.Forms.Timer;

class ZonesPage : DevicePage
{
    readonly int Count;
    readonly bool keyboard;
    readonly string noun;
    MouseLight[] current, sent;
    volatile List<Dictionary<int, int>> zones;
    bool loading, screening, capturing, framing, snapping;
    int sequence, picked, selectedZone;
    List<int> segZones = new List<int>();
    int[] shown;
    volatile int[] framed;
    DateTime nextRefresh;
    ScreenSetup setup;
    readonly Timer debounce = new Timer { Interval = 120 }, screenTimer = new Timer { Interval = 100 },
        snapTimer = new Timer { Interval = 1000 }, frameTimer = new Timer { Interval = 50 };
    readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
    readonly KeyPicture picture;
    readonly KeyLayout iso;
    bool isoShown;
    volatile bool wholeAgain;
    volatile int layout = -1;
    List<int> keyZones;

    internal override bool Busy { get { return debounce.Enabled; } }

    readonly Label lightHead, zoneHead, colorHead, speedHead, speedValue, brightHead, brightValue, note,
        boostValue, rateValue, screenBrightValue;
    readonly SegmentedPicker zonePicker, effects, directions;
    readonly ColorRow colors;
    readonly Slider speed, bright, boost, rate, screenBright;
    readonly RoundedBox light, areas, settingsBox;
    readonly Dropdown monitorDropdown;
    readonly ScreenPreview preview;
    readonly Toggle smoothToggle, lockToggle;
    readonly List<Control> plainLight = new List<Control>();

    string Remembered { get { return "lighting." + found.SettingsKey; } }
    string Saved { get { return "screen." + found.SettingsKey; } }
    string Active { get { return "screen.on." + found.SettingsKey; } }

    public ZonesPage(FoundDevice found, Hidpp fake, MouseEventHandler drag)
        : base(found, fake, drag, found.ShortName, found.Kind == "keyboard" ? "keyboard" : "device",
            "profile." + found.SettingsKey + ".")
    {
        keyboard = found.Kind == "keyboard";
        noun = keyboard ? "keyboard" : "speakers";
        Count = keyboard ? 6 : 4;
        if (!keyboard) segZones = Enumerable.Range(0, Count).ToList();
        sent = new MouseLight[Count];
        debounce.Tick += (s, e) => { debounce.Stop(); Send(); };
        setup = ZoneLighting.Screen(Settings.Get(Saved));

        var model = ZoneLighting.Picture(found);
        if (model != null)
        {
            var pictureBox = Ui.Add(this, new RoundedBox(), 20, 118, 616, 209);
            Ui.Text(pictureBox, "Live on the keyboard", 14, 12, () => Theme.Dim, 9f, Ui.Semibold);
            var ansi = KeyLayout.Load(model);
            iso = ansi.Iso();
            picture = Ui.Add(pictureBox, new KeyPicture(ansi) { Live = true }, 14, 36, 588, 161);
        }
        light = Ui.Add(this, new RoundedBox(), 20, picture != null ? 339 : 118, 616, picture != null ? 310 : 330);
        lightHead = Ui.Text(light, "Lighting", 14, 12, () => Theme.Dim, 9f, Ui.Semibold);
        zoneHead = Ui.Text(light, "Zone", 14, 40, () => Theme.Dim, 9f);
        zonePicker = Ui.Add(light,
            new SegmentedPicker
            {
                Items = keyboard ? new[] { "All" } : new[] { "All" }.Concat(ZoneLighting.Names).ToArray(),
                AccessibleName = "Zone", Index = 0
            },
            14, 58, 440, 32);
        zonePicker.Picked += index => { picked = index; RefreshControls(); };
        Ui.Text(light, "Effect", 14, 98, () => Theme.Dim, 9f);
        var names = ZoneLighting.Effects.Select(effect => effect.Name).ToList();
        if (!keyboard) names.Add("Screen");
        effects = Ui.Add(light, new SegmentedPicker { Items = names.ToArray(), AccessibleName = "Effect" }, 14, 116, 440, 32);
        effects.Picked += index =>
        {
            if (index == ZoneLighting.Effects.Length) { if (!screening) { ScreenOn(); Edited(); } return; }
            if (screening) ScreenOff();
            var effect = ZoneLighting.Effects[index];
            Edit(edited =>
            {
                edited.Effect = effect.Id;
                if (effect.Speed && (edited.Period < effect.MinPeriod || edited.Period > effect.MaxPeriod))
                    edited.Period = effect.DefaultPeriod;
            }, true);
        };
        colorHead = Ui.Text(light, "Color", 14, 156, () => Theme.Dim, 9f);
        colors = new ColorRow(light, 173);
        colors.Picked += color => Edit(edited => edited.Color = color, true);
        directions = Ui.Add(light, new SegmentedPicker { Items = ZoneLighting.DirText, AccessibleName = "Direction", Visible = false },
            14, 174, 440, 32);
        directions.Picked += index =>
        {
            int direction = ZoneLighting.Directions[index];
            Edit(edited => edited.Direction = direction, true);
        };

        brightHead = Ui.Text(light, "Brightness", 14, 214, () => Theme.Dim, 9f);
        brightValue = Value(light, 14, 214, 172);
        bright = Ui.Add(light, new Slider { AccessibleName = "Brightness" }, 14, 234, 172, 24);
        bright.Changed += (s, e) => { int brightness = Math.Max(1, bright.Value); Edit(edited => edited.Bright = brightness, false); };
        speedHead = Ui.Text(light, "Speed", 194, 214, () => Theme.Dim, 9f);
        speedValue = Value(light, 194, 214, 172);
        speed = Ui.Add(light, new Slider { AccessibleName = "Speed" }, 194, 234, 172, 24);
        speed.Changed += (s, e) =>
        {
            var effect = ZoneLighting.EffectById(Displayed().Effect);
            int period = effect.MaxPeriod - (effect.MaxPeriod - effect.MinPeriod) * speed.Value / 100;
            Edit(edited => edited.Period = period, false);
        };
        note = Ui.Text(light, "G-Lite sets the lighting again when it starts and when you open the " + noun + ".",
            14, 272, () => Theme.Dim, 9f);
        note.AutoSize = false;
        note.Size = new Size(588, 40);
        plainLight.AddRange(new Control[] { colorHead, brightHead, brightValue, bright, speedHead, speedValue, speed, note });
        plainLight.AddRange(light.Controls.Cast<Control>().Where(control => control is Swatch));

        areas = Ui.Add(this, new RoundedBox { Visible = false }, 20, 292, 358, 300);
        settingsBox = Ui.Add(this, new RoundedBox { Visible = false }, 390, 292, 246, 300);
        Ui.Text(areas, "Screen areas", 14, 12, () => Theme.Dim, 9f);
        var frame = Ui.Add(areas, new RoundedBox(), 14, 38, 330, 188);
        preview = Ui.Add(frame, new ScreenPreview(), 8, 8, 314, 172);
        preview.Picked += index => { selectedZone = index; preview.Selected = index; preview.Invalidate(); };
        preview.Done += ScreenChanged;
        var resetAreas = Ui.Add(areas, new FlatButton { Text = "Reset areas", AccessibleName = "Reset the screen areas" },
            14, 236, 110, 30);
        resetAreas.Click += (s, e) => { setup.Zones = ZoneLighting.ScreenZones(); selectedZone = 0; ScreenChanged(); };
        Ui.Text(settingsBox, "Monitor", 14, 12, () => Theme.Dim, 9f);
        monitorDropdown = Ui.Add(settingsBox, new Dropdown { AccessibleName = "Monitor" }, 14, 38, 218, 30);
        monitorDropdown.Picked += index =>
        {
            var all = Screen.AllScreens;
            if (index < 0 || index >= all.Length) return;
            setup.Monitor = all[index].Primary ? "" : all[index].DeviceName;
            ScreenChanged();
            Snap();
        };
        Ui.Text(settingsBox, "Brightness", 14, 78, () => Theme.Dim, 9f);
        screenBrightValue = Value(settingsBox, 14, 78, 218);
        screenBright = Ui.Add(settingsBox, new Slider { AccessibleName = "Brightness" }, 14, 96, 218, 24);
        screenBright.Changed += (s, e) =>
        {
            setup.Bright = Math.Max(1, screenBright.Value);
            screenBrightValue.Text = setup.Bright + "%";
            ScreenChanged();
        };
        Ui.Text(settingsBox, "Color boost", 14, 126, () => Theme.Dim, 9f);
        boostValue = Value(settingsBox, 14, 126, 218);
        boost = Ui.Add(settingsBox, new Slider { AccessibleName = "Color boost" }, 14, 144, 218, 24);
        boost.Changed += (s, e) => { setup.Boost = boost.Value; boostValue.Text = setup.Boost + "%"; ScreenChanged(); };
        Ui.Text(settingsBox, "Update rate", 14, 174, () => Theme.Dim, 9f);
        rateValue = Value(settingsBox, 14, 174, 218);
        rate = Ui.Add(settingsBox, new Slider { AccessibleName = "Update rate", Max = 9, Step = 1 }, 14, 192, 218, 24);
        rate.Changed += (s, e) =>
        {
            setup.Interval = 1000 - rate.Value * 100;
            rateValue.Text = setup.Interval + " ms";
            screenTimer.Interval = setup.Interval;
            ScreenChanged();
        };
        smoothToggle = Option(settingsBox, "Smooth changes", 14, 228, 20, () => setup.Smooth = !setup.Smooth);
        lockToggle = Option(settingsBox, "Pause when screen off or locked", 14, 256, 36,
            () => setup.PauseWhenLocked = !setup.PauseWhenLocked);

        screenTimer.Tick += (s, e) => Tick();
        if (picture != null)
        {
            keyZones = Enumerable.Range(0, picture.Count).Select(picture.IdOf).ToList();
            picture.KeysPicked += hits =>
            {
                var listed = zones;
                if (listed == null || hits.Count == 0 || !zonePicker.Enabled) return;
                int at = segZones.IndexOf(keyZones[hits[0]]);
                if (at < 0) return;
                picked = at + 1;
                RefreshControls();
            };
            frameTimer.Tick += (s, e) => ShowFrame();
            frameTimer.Start();
        }
        snapTimer.Tick += (s, e) => Snap();

        current = ZoneLighting.FromText(Settings.Get(Remembered), Count);
        if (!keyboard && Settings.Get(Active) == "1") ScreenOn();
        RefreshControls();
        if (current != null || screening) Load();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            debounce.Dispose();
            screenTimer.Dispose();
            snapTimer.Dispose();
            frameTimer.Dispose();
        }
        base.Dispose(disposing);
    }

    Toggle Option(Control parent, string text, int x, int y, int tall, Action flip)
    {
        var toggle = Ui.Add(parent, new Toggle { AccessibleName = text }, x, y + 2, 34, 18);
        toggle.Changed += () => { flip(); ScreenChanged(); };
        var label = Ui.Text(parent, text, x + 42, y, () => Theme.Text, 9f);
        label.AutoSize = false;
        label.Size = new Size(176, tall);
        toggle.Follow(label);
        return toggle;
    }

    int Zone { get { return picked == 0 || picked > segZones.Count ? 0 : segZones[picked - 1]; } }

    MouseLight Displayed()
    {
        if (current == null) return null;
        return Own(Zone, current[Zone]);
    }

    // An area showing zone 0's effect, which it can't take itself, is shown and edited as Static.
    MouseLight Own(int zone, MouseLight light)
    {
        var listed = zones;
        if (light == null || zone == 0 || listed == null || zone >= listed.Count
            || !ZoneLighting.IsArea(listed[zone][ZoneLighting.Place]) || listed[zone].ContainsKey(light.Effect))
            return light;
        var shown = light.Clone();
        shown.Effect = 0x01;
        return shown;
    }

    void Edit(Action<MouseLight> change, bool now)
    {
        var known = zones;
        if (loading || known == null) return;
        if (current == null) current = Enumerable.Range(0, Count).Select(i => new MouseLight()).ToArray();
        if (picked == 0)
        {
            wholeAgain = true;
            var all = (current[0] ?? new MouseLight()).Clone();
            change(all);
            for (int zone = 0; zone < Count; zone++) current[zone] = all.Clone();
        }
        else
        {
            int zone = Zone;
            var one = (Own(zone, current[zone]) ?? new MouseLight()).Clone();
            change(one);
            current[zone] = one;
            var listed = known;
            // On the G213, setting one area ends the whole keyboard's effect on all of them (docs/G213.md):
            // the areas that followed it get its colour.
            if (zone < listed.Count && ZoneLighting.IsArea(listed[zone][ZoneLighting.Place]))
                for (int area = 0; area < listed.Count && area < Count; area++)
                    if (area != zone && ZoneLighting.IsArea(listed[area][ZoneLighting.Place])
                        && (current[area] == null || !listed[area].ContainsKey(current[area].Effect)))
                    {
                        var follow = (current[0] ?? new MouseLight()).Clone();
                        follow.Effect = (byte)(follow.Effect == 0x00 ? 0x00 : 0x01);
                        current[area] = follow;
                    }
        }
        sequence++;
        Edited();
        RefreshControls();
        debounce.Stop();
        if (now) Send(); else debounce.Start();
    }

    public override void Restyle() { RefreshControls(); }

    protected override string Current()
    {
        return (current == null ? "" : ZoneLighting.ToText(current)) + (screening ? "|" + setup.ToText() : "");
    }

    protected override void ApplyPreset(string text)
    {
        var pieces = text.Split('|');
        var lights = ZoneLighting.FromText(pieces[0], Count);
        bool screen = !keyboard && pieces.Length > 1;
        if (lights == null && !screen) return;
        if (screen) setup = ZoneLighting.Screen(pieces[1]);
        if (screening && !screen) ScreenOff();
        if (lights != null) current = lights;
        wholeAgain = true;
        sequence++;
        if (screen)
        {
            if (!screening) ScreenOn();
            else { screenTimer.Interval = setup.Interval; ScreenChanged(); Snap(); }
        }
        Edited();
        RefreshControls();
        Send();
    }

    protected override void Revert() { }

    protected override void Lost() { zones = null; sent = new MouseLight[Count]; framed = null; }

    void ScreenOn()
    {
        screening = true;
        Settings.Set(Active, "1");
        Settings.Set(Saved, setup.ToText());
        shown = null;
        framed = null;
        LogUserAction("Screen effect on");
        screenTimer.Interval = setup.Interval;
        if (!Manual) { screenTimer.Start(); snapTimer.Start(); }
        Snap();
        RefreshControls();
        ClearStatus();
    }

    // Doesn't send: the edit or preset that ends the Screen effect sends next, and sending here too raced it.
    void ScreenOff()
    {
        screening = false;
        screenTimer.Stop();
        snapTimer.Stop();
        Settings.Remove(Active);
        shown = null;
        framed = null;
        sent = new MouseLight[Count];
        LogUserAction("Screen effect off");
        RefreshControls();
    }

    void ScreenChanged()
    {
        if (loading) return;
        Settings.Set(Saved, setup.ToText());
        framed = null;
        RefreshControls();
        Edited();
    }

    void Tick()
    {
        if (IsDisposed || !screening || zones == null || capturing || framing) return;
        if (setup.PauseWhenLocked && (Presence.Locked || Presence.DisplayOff)) return;
        capturing = true;
        var monitor = ScreenCapture.Find(setup.Monitor).Bounds;
        var areas = setup.Zones.ToList();
        var context = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        ThreadPool.QueueUserWorkItem(state =>
        {
            int[] sampled = null;
            try { sampled = ScreenCapture.Sample(monitor, areas); } catch (Exception) { }
            try { context.Post(posted => { capturing = false; if (sampled != null && !IsDisposed && screening) Sampled(sampled); }, null); }
            catch (Exception) { }
        });
    }

    void Sampled(int[] sampled)
    {
        bool fresh = shown == null || shown.Length != sampled.Length;
        if (fresh) shown = new int[sampled.Length];
        bool refresh = DateTime.UtcNow >= nextRefresh;
        if (refresh) nextRefresh = DateTime.UtcNow.AddSeconds(2);
        var last = framed;
        bool changed = refresh || fresh || last == null || last.Length != sampled.Length;
        for (int area = 0; area < sampled.Length; area++)
        {
            int target = ScreenCapture.Boost(sampled[area], setup.Boost);
            shown[area] = setup.Smooth && !fresh ? ScreenCapture.Blend(shown[area], target, 0.45) : target;
            if (!changed && ScreenCapture.Distance(shown[area], last[area]) > 6) changed = true;
        }
        if (!changed) return;
        var frame = (int[])shown.Clone();
        int level = setup.Bright;
        var listed = zones;
        if (listed == null) return;
        framing = true;
        RunOnDevice(hidpp =>
        {
            try
            {
                for (int zone = 0; zone < frame.Length && zone < listed.Count; zone++)
                    ZoneLighting.SetColor(hidpp, zone, listed[zone], frame[zone], level);
                framed = frame;
            }
            finally { framing = false; }
        }, null, message => { framed = null; });
    }

    void Snap()
    {
        if (!screening || !Visible || snapping) return;
        snapping = true;
        var monitor = ScreenCapture.Find(setup.Monitor).Bounds;
        int width = preview.Width, height = Math.Max(1, width * monitor.Height / Math.Max(1, monitor.Width));
        var context = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        ThreadPool.QueueUserWorkItem(state =>
        {
            Bitmap image = null;
            try { image = ScreenCapture.Shrink(monitor, width, height); } catch (Exception) { }
            try
            {
                context.Post(posted =>
                {
                    snapping = false;
                    if (image == null) return;
                    if (IsDisposed) { image.Dispose(); return; }
                    var old = preview.Image;
                    preview.Monitor = monitor.Size;
                    preview.Image = image;
                    preview.Invalidate();
                    if (old != null) old.Dispose();
                }, null);
            }
            catch (Exception) { if (image != null) image.Dispose(); }
        });
    }

    void RefreshControls()
    {
        loading = true;
        var listed = zones;
        bool ready = listed != null && listed.Count > 0;
        var displayed = Displayed();
        bool known = displayed != null && !screening;
        var effect = known ? ZoneLighting.EffectById(displayed.Effect) : null;
        bool mixed = picked == 0 && current != null && current.Any(zoneLight => zoneLight == null || !zoneLight.Same(current[0]));
        lightHead.Text = listed != null && listed.Count == 0
            ? "Lighting (not found on " + (keyboard ? "this keyboard" : "these speakers") + ")"
            : screening ? "Lighting"
            : !known ? "Lighting (as on the " + noun + ")"
            : mixed ? "Lighting (zones differ, a change sets all of them)"
            : "Lighting";
        zonePicker.Enabled = ready && !screening;
        SetDimmed(zoneHead, !screening);
        zonePicker.Index = picked;
        zonePicker.Invalidate();
        effects.Enabled = ready || screening;
        effects.Index = screening ? ZoneLighting.Effects.Length
            : known ? Array.FindIndex(ZoneLighting.Effects, info => info.Id == displayed.Effect)
            : -1;
        var offered = !ready ? null : picked == 0 ? listed : listed.Skip(Zone).Take(1);
        effects.Off = offered == null ? new int[0]
            : Enumerable.Range(0, ZoneLighting.Effects.Length)
                .Where(i => !offered.Any(zone => zone.ContainsKey(ZoneLighting.Effects[i].Id)))
                .ToArray();
        effects.Invalidate();
        bool color = known && effect.Color, hasSpeed = known && effect.Speed, hasBrightness = known && displayed.Effect != 0;
        SetDimmed(brightHead, hasBrightness);
        bright.Enabled = hasBrightness;
        bright.Value = known ? displayed.Bright : 100;
        brightValue.Text = hasBrightness ? displayed.Bright + "%" : "";
        bright.Invalidate();
        SetDimmed(speedHead, hasSpeed);
        speed.Enabled = hasSpeed;
        if (hasSpeed) speed.Value = (effect.MaxPeriod - displayed.Period) * 100 / (effect.MaxPeriod - effect.MinPeriod);
        speedValue.Text = hasSpeed ? speed.Value + "%" : "";
        speed.Invalidate();
        foreach (var control in plainLight) control.Visible = !screening;
        if (!screening) colors.RefreshControls(known ? displayed.Color : 0xFFFFFF, color);
        bool wave = known && effect.Direction;
        colorHead.Text = wave ? "Direction" : "Color";
        SetDimmed(colorHead, color || wave);
        if (wave) foreach (var control in light.Controls.OfType<Swatch>()) control.Visible = false;
        directions.Visible = !screening && wave;
        directions.Index = wave ? Array.IndexOf(ZoneLighting.Directions, displayed.Direction) : -1;
        directions.Invalidate();
        if (picture != null) ShowFrame();
        areas.Visible = settingsBox.Visible = screening;
        light.Height = light.Width * (screening ? 162 : picture != null ? 310 : 330) / 616;
        var all = Screen.AllScreens;
        var chosen = ScreenCapture.Find(setup.Monitor);
        monitorDropdown.Items.Clear();
        monitorDropdown.Items.AddRange(all.Select(ScreenCapture.Label));
        monitorDropdown.Index = Array.IndexOf(all, chosen);
        preview.Zones = setup.Zones;
        preview.Selected = selectedZone;
        preview.Monitor = chosen.Bounds.Size;
        preview.Invalidate();
        screenBright.Value = setup.Bright;
        screenBrightValue.Text = setup.Bright + "%";
        screenBright.Invalidate();
        boost.Value = setup.Boost;
        boostValue.Text = setup.Boost + "%";
        rate.Value = (1000 - setup.Interval) / 100;
        rateValue.Text = setup.Interval + " ms";
        smoothToggle.Checked = setup.Smooth;
        lockToggle.Checked = setup.PauseWhenLocked;
        boost.Invalidate();
        rate.Invalidate();
        loading = false;
    }

    MouseLight LightOfZone(int zone)
    {
        var listed = zones;
        if (current == null || listed == null || zone < 0 || zone >= listed.Count || zone >= current.Length) return null;
        var own = current[zone];
        return own != null && listed[zone].ContainsKey(own.Effect) ? own : current[0];
    }

    void ShowFrame()
    {
        if (picture == null || !Visible) return;
        var listed = zones;
        if (!isoShown && iso != null && listed != null && layout > 2)
        {
            isoShown = true;
            picture.SetModel(iso);
            keyZones = Enumerable.Range(0, picture.Count).Select(picture.IdOf).ToList();
        }
        double seconds = clock.Elapsed.TotalSeconds;
        var lit = new Color[picture.Count];
        var marked = new bool[picture.Count];
        bool any = current != null && listed != null && !screening;
        for (int key = 0; key < picture.Count; key++)
        {
            int zone = keyZones[key];
            var color = any ? ZoneLighting.ColorAt(LightOfZone(zone), picture.XOf(key), seconds) : Color.Black;
            int max = Math.Max(color.R, Math.Max(color.G, color.B));
            lit[key] = max == 0 ? Theme.KeyOff
                : Ui.Mix(Theme.KeyOff, Color.FromArgb(color.R * 255 / max, color.G * 255 / max, color.B * 255 / max), max / 255.0);
            marked[key] = picked > 0 && zone == Zone;
        }
        picture.ShowLive(lit, marked);
    }

    protected override void ClearStatus()
    {
        var listed = zones;
        if (listed != null && listed.Count == 0)
        {
            ShowStatus(keyboard ? "This keyboard lists no lighting zones" : "These speakers list no lighting zones", true);
            return;
        }
        if (screening) { ShowStatus("Screen effect, not saved to the speakers"); return; }
        // The G213's light button turns the backlight off inside the keyboard: nothing is reported,
        // and lighting sent then stays dark (docs/G213.md).
        if (current != null && keyboard && found.ShortName.ToUpper() == "G213")
        {
            ShowStatus("Set by G-Lite. Keys dark? Press the light button above Pause");
            return;
        }
        ShowStatus(current == null ? "" : "Set by G-Lite, not saved to the " + noun);
    }

    void Send()
    {
        if (current == null) return;
        var want = current.Select(zoneLight => zoneLight == null ? null : zoneLight.Clone()).ToArray();
        // Kept while the Screen effect runs too: they're the lights it goes back to, and a preset may have just set them.
        Settings.Set(Remembered, ZoneLighting.ToText(want));
        if (screening) return;
        var listed = zones;
        int startedAt = ++sequence;
        RunOnDevice(hidpp =>
        {
            if (startedAt != sequence || listed == null) return;
            bool whole = wholeAgain;
            wholeAgain = false;
            Apply(hidpp, listed, want, whole);
        }, () => { if (startedAt == sequence) ClearStatus(); });
    }

    void Apply(Hidpp hidpp, List<Dictionary<int, int>> listed, MouseLight[] want, bool whole = false)
    {
        // An area write ends the whole-keyboard effect on the G213 (docs/G213.md),
        // so All and presets send it again even when it looks unchanged.
        if (whole)
            for (int zone = 0; zone < listed.Count && zone < Count; zone++)
                if (listed[zone][ZoneLighting.Place] == 1 && listed.Any(other => ZoneLighting.IsArea(other[ZoneLighting.Place])))
                    sent[zone] = null;
        for (int zone = 0; zone < listed.Count && zone < Count; zone++)
        {
            if (want[zone] == null || (sent[zone] != null && sent[zone].Same(want[zone]))) continue;
            ZoneLighting.SetLight(hidpp, zone, listed[zone], want[zone]);
            sent[zone] = want[zone];
        }
    }

    public override void Load()
    {
        debounce.Stop();
        ShowStatus("Reading the " + noun + "…");
        List<Dictionary<int, int>> read = null;
        var want = current == null || screening ? null
            : current.Select(zoneLight => zoneLight == null ? null : zoneLight.Clone()).ToArray();
        RunOnDevice(hidpp =>
        {
            read = ZoneLighting.Zones(hidpp);
            if (picture != null && layout < 0 && found.Has(0x4540))
                try { layout = hidpp.Call(hidpp.FeatureIndex(0x4540), 0)[0]; } catch (HidppError) { layout = 0; }
            sent = new MouseLight[Count];
            if (want != null) Apply(hidpp, read, want);
        }, () =>
        {
            zones = read;
            framed = null;
            if (keyboard)
            {
                var listed = read.Take(Count).ToList();
                bool areas = listed.Any(zone => ZoneLighting.IsArea(zone[ZoneLighting.Place]));
                // With areas (the G213), the whole-keyboard zone is what All sets.
                segZones = Enumerable.Range(0, listed.Count).Where(i => !(areas && listed[i][ZoneLighting.Place] == 1)).ToList();
                zonePicker.Items = new[] { "All" }
                    .Concat(segZones.Select(i => ZoneLighting.ZoneName(listed[i][ZoneLighting.Place], i)))
                    .ToArray();
                if (picked > segZones.Count) picked = 0;
            }
            RefreshControls();
            Loaded();
        });
    }
}
