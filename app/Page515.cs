using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Threading;
using Timer = System.Windows.Forms.Timer;
using System.Windows.Forms;
using Microsoft.Win32;

class Page515 : DevicePage
{
    static readonly int[] IdleSecs = { 0, 15, 30, 60, 120, 300, 600 };
    static readonly string[] IdleText = { "Never", "15 s", "30 s", "1 min", "2 min", "5 min", "10 min" };
    static readonly int[] DimPcts = { 0, 10, 25, 50, 75 };
    static readonly string[] DimText = { "Dark", "10%", "25%", "50%", "75%" };
    static readonly int[] SleepSecs = { 0, 60, 120, 300, 600, 900, 1800 };
    static readonly string[] SleepText = { "Never", "1 min", "2 min", "5 min", "10 min", "15 min", "30 min" };
    static readonly int[] Directions = { 6, 1, 3, 8 };
    static readonly string[] DirText = { "Left", "Right", "Center out", "Center in" };
    const byte ScreenEffect = 0xFE;
    string Remembered { get { return "lighting." + found.SettingsKey; } }
    string Saved { get { return "screen." + found.SettingsKey; } }
    string Active { get { return "screen.on." + found.SettingsKey; } }
    bool Screening { get { return current.Effect == ScreenEffect; } }
    bool leftScreen;

    readonly KeyboardKinds kind;
    readonly bool live, canScreen;
    List<byte[]> clusters;
    ScreenSetup setup;
    readonly Timer screenTimer = new Timer { Interval = 100 }, snapTimer = new Timer { Interval = 1000 },
        watchTimer = new Timer { Interval = 500 };
    volatile bool sending, failing, statusDirty, layoutDirty, capturing, snapping, watching, claimed;
    volatile string problem;
    bool released, force, iso, away;
    volatile int idleLevel;
    int watchTicks;
    int battery = -1, layoutCode = -1, selectedZone = 1;
    bool charging;
    DateTime nextBattery, nextTry, nextRefresh;
    HashSet<int> present;
    int[] shown, sent;
    readonly Control idleArea, screenBox;
    readonly List<Control> plainLight = new List<Control>(), screenLight = new List<Control>();
    readonly Dropdown monitorDropdown;
    readonly ScreenPreview preview;
    readonly ZoneList zoneList;
    readonly FlatButton addZone;
    readonly Slider boostSlider, screenBright, rateSlider;
    readonly Label boostValue, screenBrightValue, rateValue;
    readonly Toggle smoothToggle, batteryToggle, lockToggle;
    readonly Action presenceChanged;
    readonly EventHandler exiting;

    Lighting current = new Lighting(), stored, bar;
    int part;
    readonly SegmentedPicker partPicker;
    readonly Toggle perZone;
    const byte Ripple = 0x17;
    bool Parts { get { return kind.Bar != null; } }
    Lighting Viewed { get { return part == 2 && bar != null ? bar : current; } }
    int brightness = 100, storedBrightness = -1;
    bool loading, dirty, unknownStored, broken;
    int previewSequence, edits;
    readonly Timer debounce = new Timer { Interval = 120 };

    internal override bool Busy { get { return debounce.Enabled; } }

    readonly Label lightHead, colorHead, directionHead, speedHead, speedValue, brightValue;
    readonly SegmentedPicker effects, directions;
    readonly ColorRow colors;
    readonly Slider speed, bright;
    readonly Dropdown idleBox, dimBox, sleepBox;
    readonly int never;
    readonly FlatButton saveButton, backupsButton;
    readonly KeyPicture picture;
    Backups.Entry fix;

    public Page515(FoundDevice found, Hidpp fake, MouseEventHandler drag, KeyboardKinds kind)
        : base(found, fake, drag, kind.Keys.Name, "keyboard", kind.Prefix)
    {
        this.kind = kind;
        live = !kind.Saves;
        debounce.Tick += (s, e) => { debounce.Stop(); Preview(); };
        if (!live && found.WirelessId != 0) Backups.Move(found.WirelessId.ToString("X4"), BackupId);

        if (!live)
        {
            backupsButton = BackupsButton("keyboard's lighting");
            backupsButton.Click += (s, e) => ShowBackups();
        }
        else if (kind.Profile)
        {
            backupsButton = BackupsButton("keyboard's profile");
            backupsButton.Click += (s, e) => ShowProfileBackups();
        }

        var pictureBox = Ui.Add(this, new RoundedBox(), 20, 118, 616, 209);
        Ui.Text(pictureBox, "Live on the keyboard", 14, 12, () => Theme.Dim, 9f, Ui.Semibold);
        picture = Ui.Add(pictureBox, new KeyPicture(kind.Keys), 14, 36, 588, 161);
        if (Parts)
        {
            picture.Bar = new HashSet<int>(kind.Bar);
            picture.PartPicked += onBar => { if (Screening || !Known() || bar == null) return; part = onBar ? 2 : 1; RefreshControls(); };
        }

        canScreen = found.Has(0x8081);
        setup = ScreenSetup.FromText(Settings.Get(Saved));
        var light = Ui.Add(this, new RoundedBox(), 20, 339, 380, 272);
        lightHead = Ui.Text(light, "Lighting", 14, 12, () => Theme.Dim, 9f, Ui.Semibold);
        var effectHead = Ui.Text(light, "Effect", 14, 40, () => Theme.Dim, 9f);
        var names = G515Profile.Effects.Select(effect => effect.Name).ToList();
        if (canScreen) names.Add("Screen");
        effects = Ui.Add(light, new SegmentedPicker { Items = names.ToArray(), AccessibleName = "Effect" }, 14, 58, 352, 32);
        effects.Picked += i =>
        {
            if (i == G515Profile.Effects.Length)
            {
                part = 0;
                bar = null;
                if (!Screening) { current.Effect = ScreenEffect; ScreenOn(); }
                Changed(true);
                return;
            }
            if (Screening) ScreenOff();
            var effect = G515Profile.Effects[i];
            var target = Target();
            target.Effect = effect.Id;
            if (effect.Speed && (target.Period < effect.MinPeriod || target.Period > effect.MaxPeriod))
                target.Period = effect.DefaultPeriod;
            Changed(true);
        };
        colorHead = Ui.Text(light, "Color", 14, 98, () => Theme.Dim, 9f);
        colors = new ColorRow(light, 115);
        colors.Picked += color => { Target().Color = color; Changed(true); };

        directionHead = Ui.Text(light, "Direction", 14, 156, () => Theme.Dim, 9f);
        directions = Ui.Add(light, new SegmentedPicker { Items = DirText, AccessibleName = "Direction" }, 14, 174, 352, 32);
        directions.Picked += i => { Target().Direction = Directions[i]; Changed(true); };

        Ui.Text(light, kind.Bar != null ? "Brightness (all)" : "Brightness", 14, 214, () => Theme.Dim, 9f);
        brightValue = Value(light, 14, 214, 172);
        bright = Ui.Add(light, new Slider { AccessibleName = "Brightness" }, 14, 234, 172, 24);
        bright.Changed += (s, e) => BrightnessChanged(bright.Value);
        speedHead = Ui.Text(light, "Speed", 194, 214, () => Theme.Dim, 9f);
        speedValue = Value(light, 194, 214, 172);
        speed = Ui.Add(light, new Slider { AccessibleName = "Speed" }, 194, 234, 172, 24);
        speed.Changed += (s, e) =>
        {
            var target = Target();
            var effect = G515Profile.EffectById(target.Effect);
            target.Period = effect.MaxPeriod - (effect.MaxPeriod - effect.MinPeriod) * speed.Value / 100;
            Changed(false);
        };

        foreach (Control control in light.Controls)
            if (control != lightHead && control != effectHead && control != effects) plainLight.Add(control);

        screenLight.Add(Ui.Text(light, "Monitor", 14, 98, () => Theme.Dim, 9f));
        monitorDropdown = Ui.Add(light, new Dropdown { AccessibleName = "Monitor" }, 14, 116, 200, 30);
        monitorDropdown.Picked += i =>
        {
            var all = Screen.AllScreens;
            if (i < 0 || i >= all.Length) return;
            setup.Monitor = all[i].Primary ? "" : all[i].DeviceName;
            ScreenChanged();
            Snap();
        };
        var frame = Ui.Add(light, new RoundedBox(), 14, 152, 200, 110);
        screenLight.Add(frame);
        preview = Ui.Add(frame, new ScreenPreview(), 6, 6, 188, 98);
        preview.Picked += PickZone;
        preview.Done += ScreenChanged;
        screenLight.Add(monitorDropdown);
        screenLight.Add(Ui.Text(light, "Zones", 226, 98, () => Theme.Dim, 9f));
        zoneList = Ui.Add(light, new ZoneList(), 226, 116, 140, 110);
        zoneList.Picked += PickZone;
        zoneList.Removed += i =>
        {
            setup.Remove(i);
            selectedZone = Math.Max(0, Math.Min(i, setup.Zones.Count - 1));
            ScreenChanged();
        };
        addZone = Ui.Add(light, new FlatButton { Text = "Add", AccessibleName = "Add zone" }, 226, 232, 66, 30);
        addZone.Click += (s, e) =>
        {
            int i = setup.Add();
            if (i >= 0) selectedZone = i;
            ScreenChanged();
        };
        var resetZones = Ui.Add(light, new FlatButton { Text = "Reset", AccessibleName = "Reset zones" }, 300, 232, 66, 30);
        resetZones.Click += (s, e) =>
        {
            setup.Reset();
            selectedZone = 1;
            ScreenChanged();
        };
        screenLight.AddRange(new Control[] { zoneList, addZone, resetZones });
        if (Parts)
        {
            const int shift = 44;
            foreach (Control control in light.Controls) if (control != lightHead) control.Top += shift;
            light.Height += shift;
            perZone = Ui.Add(light, new Toggle { AccessibleName = "Lighting per zone" }, 14, 46, 34, 18);
            perZone.Changed += () => PerZone(perZone.Checked);
            var perZoneLabel = Ui.Text(light, "Lighting per zone", 56, 44, () => Theme.Text, 9f);
            perZone.Follow(perZoneLabel);
            partPicker = Ui.Add(light, new SegmentedPicker { Items = new[] { "Keys", "Light bar" }, AccessibleName = "Part" },
                190, 38, 176, 30);
            partPicker.Picked += i => { part = i + 1; RefreshControls(); };
        }
        foreach (var control in screenLight) control.Visible = false;
        picture.KeysPicked += picked =>
        {
            foreach (int key in picked) if (picture.IdOf(key) >= 0) setup.Keys[picture.IdOf(key)] = selectedZone;
            ScreenChanged();
        };

        screenBox = Ui.Add(this, new RoundedBox { Visible = false }, 412, 339, 224, Parts ? 316 : 272);
        Ui.Text(screenBox, "Screen", 14, 12, () => Theme.Dim, 9f, Ui.Semibold);
        Ui.Text(screenBox, "Color boost", 14, 36, () => Theme.Dim, 9f);
        boostValue = Value(screenBox, 14, 36, 196);
        boostSlider = Ui.Add(screenBox, new Slider { AccessibleName = "Color boost" }, 14, 54, 196, 20);
        boostSlider.Changed += (s, e) =>
        {
            setup.Boost = boostSlider.Value; boostValue.Text = setup.Boost + "%"; force = true;
            Edited(); debounce.Stop(); debounce.Start();
        };
        Ui.Text(screenBox, "Brightness", 14, 78, () => Theme.Dim, 9f);
        screenBrightValue = Value(screenBox, 14, 78, 196);
        screenBright = Ui.Add(screenBox, new Slider { AccessibleName = "Brightness" }, 14, 96, 196, 20);
        screenBright.Changed += (s, e) => BrightnessChanged(screenBright.Value);
        Ui.Text(screenBox, "Update rate", 14, 120, () => Theme.Dim, 9f);
        rateValue = Value(screenBox, 14, 120, 196);
        rateSlider = Ui.Add(screenBox, new Slider { AccessibleName = "Update rate", Max = 9, Step = 1 }, 14, 138, 196, 20);
        rateSlider.Changed += (s, e) =>
        {
            setup.Interval = 1000 - rateSlider.Value * 100;
            rateValue.Text = setup.Interval + " ms";
            screenTimer.Interval = setup.Interval;
            Edited();
            debounce.Stop();
            debounce.Start();
        };
        smoothToggle = Option(screenBox, "Smooth changes", 166, () => setup.Smooth = !setup.Smooth);
        batteryToggle = Option(screenBox, "Pause on low battery", 194, () => setup.PauseOnBattery = !setup.PauseOnBattery);
        lockToggle = Option(screenBox, "Pause when screen off or locked", 222, () => setup.PauseWhenLocked = !setup.PauseWhenLocked, 36);

        screenTimer.Tick += (s, e) => Tick();
        snapTimer.Tick += (s, e) => Snap();
        watchTimer.Tick += (s, e) => Watch();
        if (!Manual) watchTimer.Start();
        presenceChanged = () => { if (IsHandleCreated) BeginInvoke((Action)PresenceChanged); };
        Presence.Changed += presenceChanged;
        exiting = (s, e) => Exiting();
        Application.ApplicationExit += exiting;

        var idle = Ui.Add(this, new RoundedBox(), 412, 339, 224, Parts ? 316 : 272);
        idleArea = idle;
        bool note = live && !kind.Profile;
        Ui.Text(idle, note ? "Saving" : "When idle", 14, 12, () => Theme.Dim, 9f, Ui.Semibold);
        var noteText = Ui.Text(idle,
            "G-Lite can't save to this keyboard yet. It keeps your lighting and sets it again when G-Lite starts and when you open this screen. Picking None gives the keyboard its own lighting back."
                + (kind.Guessed ? " The key picture is a guess for this model." : ""),
            14, 40, () => Theme.Dim, 9f);
        noteText.AutoSize = false;
        noteText.Size = new Size(196, 200);
        noteText.Visible = note;
        // The G915 TKL reads 0 as at once, not never (tried on an owner's keyboard), so its profile keyboards don't offer Never.
        never = kind.Profile ? 1 : 0;
        idleBox = Choice(idle, "Dim after", IdleText.Skip(never).ToArray(), 40, i => current.Idle = IdleSecs[i + never]);
        dimBox = Choice(idle, "Dim to", DimText, 98, i => current.Dim = DimPcts[i]);
        sleepBox = Choice(idle, "Turn off after", SleepText.Skip(never).ToArray(), 156, i => current.Sleep = SleepSecs[i + never]);
        if (note) foreach (Control control in idle.Controls) if (control != noteText && control.Top > 30) control.Visible = false;

        saveButton = Ui.Add(this, new FlatButton { Style = ButtonStyle.Primary, Text = "Save to keyboard", Visible = !note },
            496, 685, 140, 32);
        saveButton.Click += (s, e) => Save();

        if (canScreen && Settings.Get(Active) == "1")
        {
            current.Effect = ScreenEffect;
            ScreenOn();
        }
        RefreshControls();
        if (live) Load();
    }

    Lighting Target() { return part == 2 && bar != null ? bar : current; }

    void PerZone(bool on)
    {
        if (on == (bar != null) || Screening) { RefreshControls(); return; }
        if (on)
        {
            bar = current.Clone();
            if (bar.Effect == Ripple) bar.Effect = 0x01;
            part = 1;
        }
        else
        {
            bar = null;
            part = 0;
        }
        Changed(true);
    }

    string BarText(int level) { return bar == null ? "" : "#" + bar.ToText(level); }

    Lighting BarFrom(string[] pieces)
    {
        if (!Parts || pieces.Length < 2) return null;
        int ignored;
        var lighting = Lighting.FromText(pieces[1], out ignored);
        if (lighting.Effect == Ripple || lighting.Effect == ScreenEffect || G515Profile.EffectById(lighting.Effect) == null)
            lighting.Effect = 0x01;
        return lighting;
    }

    Toggle Option(Control parent, string text, int y, Action flip, int tall = 20)
    {
        var toggle = Ui.Add(parent, new Toggle { AccessibleName = text }, 14, y, 34, 18);
        toggle.Changed += () => { flip(); ScreenChanged(); };
        var label = Ui.Text(parent, text, 56, y, () => Theme.Text, 9f);
        label.AutoSize = false;
        label.Size = new Size(156, tall);
        toggle.Follow(label);
        return toggle;
    }

    void BrightnessChanged(int value)
    {
        brightness = value;
        brightValue.Text = brightness + "%";
        screenBrightValue.Text = brightness + "%";
        if (bright.Value != value) { bright.Value = value; bright.Invalidate(); }
        if (screenBright.Value != value) { screenBright.Value = value; screenBright.Invalidate(); }
        dirty = true;
        edits++;
        Edited();
        saveButton.Enabled = !broken && !Screening;
        picture.Display(Known() ? current : null, Known() ? bar : null, brightness);
        ClearStatus();
        debounce.Stop();
        debounce.Start();
    }

    void PickZone(int i)
    {
        selectedZone = i;
        preview.Selected = i;
        zoneList.Selected = i;
        preview.Invalidate();
        zoneList.Invalidate();
        ShowLive();
    }

    void ScreenOn()
    {
        Settings.Set(Active, "1");
        Settings.Set(Saved, setup.ToText());
        dirty = true;
        force = true;
        released = false;
        shown = sent = null;
        nextTry = DateTime.MinValue;
        LogUserAction("Screen effect on");
        screenTimer.Interval = setup.Interval;
        screenTimer.Start();
        snapTimer.Start();
        Snap();
    }

    void ScreenOff()
    {
        screenTimer.Stop();
        snapTimer.Stop();
        Settings.Remove(Active);
        shown = sent = null;
        released = false;
        LogUserAction("Screen effect off");
        leftScreen = true;
        if (claimed) RunOnDevice(hidpp => { if (live) G915Lighting.Release(hidpp); else G515Profile.Release(hidpp); claimed = false; });
    }

    void ScreenChanged()
    {
        Settings.Set(Saved, setup.ToText());
        force = true;
        Edited();
        SyncScreen();
        ClearStatus();
    }

    string Paused()
    {
        if (setup.PauseWhenLocked && (Presence.Locked || Presence.DisplayOff)) return "Paused while the screen is off or locked.";
        if (setup.PauseOnBattery && battery >= 0 && !charging && battery < ScreenSetup.BatteryLimit)
            return "Paused: battery below " + ScreenSetup.BatteryLimit + "%.";
        return null;
    }

    int ZoneOfKey(int key) { return setup.ZoneOf(picture.IdOf(key), picture.XOf(key)); }

    void ShowLive()
    {
        if (!Screening) return;
        var colors = new Color[picture.Count];
        var outlined = new bool[picture.Count];
        for (int key = 0; key < picture.Count; key++)
        {
            int zone = ZoneOfKey(key), color = shown != null && zone < shown.Length ? shown[zone] : 0;
            colors[key] = Color.FromArgb(color >> 16 & 0xFF, color >> 8 & 0xFF, color & 0xFF);
            outlined[key] = zone == selectedZone;
        }
        picture.ShowLive(colors, outlined);
    }

    void Claim(Hidpp hidpp, bool quiet)
    {
        if (!quiet) { ClaimLights(hidpp); return; }
        if (found.Has(0x8101)) hidpp.CallUnlogged(hidpp.FeatureIndex(0x8101), 6, 0x05);
        hidpp.CallUnlogged(hidpp.FeatureIndex(0x8071), 5, 0x01, 0x03, 0x04);
        if (!found.Has(0x8101)) try { hidpp.CallUnlogged(hidpp.FeatureIndex(0x8071), 8, 0x01, 0x01, 0x00); } catch (HidppError) { }
    }

    void ClaimLights(Hidpp hidpp)
    {
        if (found.Has(0x8101)) G515Profile.Claim(hidpp);
        else G915Lighting.Claim(hidpp);
    }

    void Failed(Exception error)
    {
        claimed = false;
        sent = null;
        failing = true;
        nextTry = DateTime.UtcNow.AddSeconds(5);
        problem = "The screen effect couldn't reach the keyboard: " + error.Message;
    }

    void Worked()
    {
        if (!failing) return;
        failing = false;
        statusDirty = true;
    }

    void Tick()
    {
        if (layoutDirty) { layoutDirty = false; ApplyLayout(); }
        var why = problem;
        if (why != null) { problem = null; ShowStatus(why, true); }
        else if (statusDirty) { statusDirty = false; ClearStatus(); }
        if (!Screening || sending || DateTime.UtcNow < nextTry) return;
        bool readBattery = found.Has(0x1004) && DateTime.UtcNow >= nextBattery;
        if (readBattery) nextBattery = DateTime.UtcNow.AddSeconds(60);
        if (Paused() != null)
        {
            if (released && !readBattery) return;
            bool release = !released;
            released = true;
            sending = true;
            RunOnDevice(hidpp =>
            {
                try
                {
                    if (release && claimed) { if (live) G915Lighting.Release(hidpp); else G515Profile.Release(hidpp); claimed = false; }
                    if (readBattery) ReadBattery(hidpp);
                    statusDirty = true;
                    Worked();
                }
                catch (Exception ex) { Failed(ex); throw; }
                finally { sending = false; }
            }, null, message => { });
            return;
        }
        if (released) { released = false; force = true; ClearStatus(); }
        if (capturing) return;
        capturing = true;
        var monitor = ScreenCapture.Find(setup.Monitor).Bounds;
        var zones = setup.Zones.ToList();
        var context = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        ThreadPool.QueueUserWorkItem(state =>
        {
            int[] sampled = null;
            try { sampled = ScreenCapture.Sample(monitor, zones); } catch (Exception) { }
            try
            {
                context.Post(posted =>
                {
                    capturing = false;
                    if (sampled != null && !IsDisposed && Screening && !sending && sampled.Length == setup.Zones.Count)
                        Sampled(sampled, readBattery);
                    else if (readBattery) nextBattery = DateTime.UtcNow;
                }, null);
            }
            catch (Exception) { }
        });
    }

    void Sampled(int[] sampled, bool readBattery)
    {
        bool fresh = shown == null || shown.Length != sampled.Length;
        if (fresh) shown = new int[sampled.Length];
        bool refresh = DateTime.UtcNow >= nextRefresh;
        if (refresh) nextRefresh = DateTime.UtcNow.AddSeconds(2);
        bool changed = force || refresh || fresh || sent == null || sent.Length != shown.Length;
        for (int zone = 0; zone < sampled.Length; zone++)
        {
            int target = ScreenCapture.Boost(sampled[zone], setup.Boost);
            shown[zone] = setup.Smooth && !fresh ? ScreenCapture.Blend(shown[zone], target, 0.45) : target;
            if (!changed && ScreenCapture.Distance(shown[zone], sent[zone]) > 6) changed = true;
        }
        ShowLive();
        if (!changed && !readBattery) return;
        var groups = new Dictionary<int, List<int>>();
        var done = new HashSet<int>();
        for (int key = 0; key < picture.Count; key++)
        {
            int id = picture.IdOf(key);
            if (id < 0 || !done.Add(id)) continue;
            int color = shown[ZoneOfKey(key)];
            List<int> list;
            if (!groups.TryGetValue(color, out list)) groups[color] = list = new List<int>();
            list.Add(id);
        }
        force = false;
        sent = (int[])shown.Clone();
        sending = true;
        bool send = changed, readLayout = layoutCode < 0 && !live && found.Has(0x4540);
        RunOnDevice(hidpp =>
        {
            try
            {
                if (readLayout) { layoutCode = hidpp.Call(hidpp.FeatureIndex(0x4540), 0)[0]; layoutDirty = true; }
                if (send)
                {
                    if (present == null) present = KeyLights.Present(hidpp);
                    if (!claimed || refresh) { Claim(hidpp, claimed); claimed = true; }
                    foreach (var list in groups.Values) list.RemoveAll(id => !present.Contains(id));
                    KeyLights.Send(hidpp, groups);
                }
                if (readBattery) { ReadBattery(hidpp); statusDirty = true; }
                Worked();
            }
            catch (Exception ex) { Failed(ex); throw; }
            finally { sending = false; }
        }, null, message => { });
    }

    void ReadBattery(Hidpp hidpp)
    {
        var reply = hidpp.CallUnlogged(hidpp.FeatureIndex(0x1004), 1);
        battery = reply[0];
        charging = reply[2] != 0;
    }

    void ApplyLayout()
    {
        if (iso || layoutCode <= 2) return;
        var model = kind.Iso;
        if (model == null) return;
        iso = true;
        picture.SetModel(model);
        force = true;
        if (Screening) SyncScreen();
    }

    void Snap()
    {
        if (!Screening || !Visible || snapping) return;
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
                    if (IsDisposed || ScreenCapture.Find(setup.Monitor).Bounds != monitor)
                    {
                        image.Dispose();
                        if (!IsDisposed) Snap();
                        return;
                    }
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

    void SyncScreen()
    {
        var all = Screen.AllScreens;
        var chosen = ScreenCapture.Find(setup.Monitor);
        monitorDropdown.Items.Clear();
        monitorDropdown.Items.AddRange(all.Select(ScreenCapture.Label));
        monitorDropdown.Index = Array.IndexOf(all, chosen);
        selectedZone = Math.Max(0, Math.Min(selectedZone, setup.Zones.Count - 1));
        preview.Zones = setup.Zones;
        preview.Selected = selectedZone;
        preview.Monitor = chosen.Bounds.Size;
        preview.Invalidate();
        var counts = new int[setup.Zones.Count];
        var seen = new HashSet<int>();
        for (int key = 0; key < picture.Count; key++) if (picture.IdOf(key) >= 0 && seen.Add(picture.IdOf(key))) counts[ZoneOfKey(key)]++;
        zoneList.Zones = setup.Zones;
        zoneList.Selected = selectedZone;
        zoneList.Count = i => i < counts.Length ? counts[i] : 0;
        zoneList.Invalidate();
        addZone.Enabled = setup.Zones.Count < ScreenSetup.MaxZones;
        boostSlider.Value = setup.Boost;
        boostValue.Text = setup.Boost + "%";
        screenBright.Value = brightness;
        screenBrightValue.Text = brightness + "%";
        rateSlider.Value = (1000 - setup.Interval) / 100;
        rateValue.Text = setup.Interval + " ms";
        smoothToggle.Checked = setup.Smooth;
        batteryToggle.Checked = setup.PauseOnBattery;
        lockToggle.Checked = setup.PauseWhenLocked;
        foreach (var s in new Control[] { boostSlider, screenBright, rateSlider }) s.Invalidate();
        screenTimer.Interval = setup.Interval;
        ShowLive();
    }

    internal void Exiting()
    {
        if (Screening)
        {
            if (!claimed) return;
            RunOnDevice(hidpp => { if (live) G915Lighting.Release(hidpp); else G515Profile.Release(hidpp); claimed = false; });
        }
        else if (live || !dirty) return;
        else Release();
        Queue.Wait(2000);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            screenTimer.Dispose();
            watchTimer.Dispose();
            snapTimer.Dispose();
            Presence.Changed -= presenceChanged;
            Application.ApplicationExit -= exiting;
            if (preview.Image != null) preview.Image.Dispose();
        }
        base.Dispose(disposing);
    }

    static string BackupId { get { return "G515"; } }

    public override void Restyle() { RefreshControls(); }

    protected override void Lost() { claimed = false; clusters = null; present = null; }

    protected override bool Damaged { get { return broken; } }

    protected override bool Unsaved
    {
        get
        {
            if (current == null) return false;
            if (live && kind.Profile) return dirty || Screening;
            if (live) return Known();
            return stored != null && (dirty || Screening);
        }
    }

    protected override string Current()
    {
        return current.ToText(brightness) + BarText(brightness) + (Screening ? "|" + setup.ToText() : "");
    }

    protected override void ApplyPreset(string text)
    {
        int level;
        var parts = text.Split('|');
        var pieces = parts[0].Split('#');
        var lighting = Lighting.FromText(pieces[0], out level);
        if (lighting.Effect == ScreenEffect && !canScreen) lighting.Effect = 0x01;
        bar = lighting.Effect == ScreenEffect ? null : BarFrom(pieces);
        part = bar == null ? 0 : 1;
        bool was = Screening;
        if (parts.Length > 1) setup = ScreenSetup.FromText(parts[1]);
        current = lighting;
        brightness = Math.Max(0, Math.Min(100, level));
        if (was && !Screening) ScreenOff();
        if (!was && Screening) ScreenOn();
        if (Screening) ScreenChanged();
        Changed(true);
    }

    Dropdown Choice(Control parent, string label, string[] items, int y, Action<int> set)
    {
        Ui.Text(parent, label, 14, y, () => Theme.Dim, 9f);
        var dropdown = Ui.Add(parent, new Dropdown { AccessibleName = label }, 14, y + 18, 196, 32);
        dropdown.Items.AddRange(items);
        dropdown.Picked += i => { set(i); Changed(false); };
        return dropdown;
    }

    bool Known() { return live ? !unknownStored : !(unknownStored && !dirty); }

    static int Nearest(int[] values, int value)
    {
        int best = 0;
        for (int i = 0; i < values.Length; i++) if (Math.Abs(values[i] - value) < Math.Abs(values[best] - value)) best = i;
        return best;
    }

    void RefreshControls()
    {
        loading = true;
        bool screening = Screening;
        foreach (var control in plainLight) control.Visible = !screening;
        foreach (var control in screenLight) control.Visible = screening;
        screenBox.Visible = screening;
        idleArea.Visible = !screening && !kind.NoIdleTimes;
        if (picture.Live != screening) picture.Live = screening;
        var view = Viewed;
        var effect = G515Profile.EffectById(view.Effect);
        bool known = Known();
        bool color = known && effect != null && effect.Color, hasDirection = known && effect != null && effect.Direction,
            hasSpeed = known && effect != null && effect.Speed;
        lightHead.Text = known ? "Lighting"
            : broken ? "Lighting (half written on the keyboard)"
            : live ? "Lighting (as on the keyboard)"
            : "Lighting (factory animation)";
        effects.Index = known ? Array.FindIndex(G515Profile.Effects, info => info.Id == view.Effect) : -1;
        effects.Off = part == 2 ? new[] { Array.FindIndex(G515Profile.Effects, info => info.Id == Ripple) } : new int[0];
        effects.Invalidate();
        if (partPicker != null)
        {
            perZone.Checked = bar != null && !screening;
            perZone.Enabled = known && !screening;
            partPicker.Enabled = known && !screening && bar != null;
            partPicker.Index = bar != null && !screening ? part - 1 : -1;
            partPicker.Invalidate();
            picture.Part = screening || !known || bar == null ? 0 : part;
        }
        SetDimmed(colorHead, color);
        colors.RefreshControls(view.Color, color);
        SetDimmed(directionHead, hasDirection);
        directions.Enabled = hasDirection;
        directions.Index = hasDirection ? Array.IndexOf(Directions, view.Direction) : -1;
        SetDimmed(speedHead, hasSpeed);
        speed.Enabled = hasSpeed;
        if (hasSpeed) speed.Value = (effect.MaxPeriod - view.Period) * 100 / (effect.MaxPeriod - effect.MinPeriod);
        speedValue.Text = hasSpeed ? speed.Value + "%" : "";
        speed.Invalidate();
        bright.Value = brightness;
        brightValue.Text = brightness + "%";
        bright.Invalidate();
        idleBox.Index = Nearest(IdleSecs.Skip(never).ToArray(), current.Idle);
        dimBox.Index = Nearest(DimPcts, current.Dim);
        sleepBox.Index = Nearest(SleepSecs.Skip(never).ToArray(), current.Sleep);
        dimBox.Enabled = current.Idle > 0;
        saveButton.Enabled = dirty && !broken;
        picture.Display(known ? current : null, known ? bar : null, brightness);
        if (screening)
        {
            foreach (var control in plainLight) control.Visible = false;
            effects.Index = G515Profile.Effects.Length;
            lightHead.Text = "Lighting";
            saveButton.Enabled = false;
            SyncScreen();
        }
        loading = false;
    }

    void Changed(bool now)
    {
        if (loading) return;
        if (live) unknownStored = false;
        dirty = true;
        edits++;
        Edited();
        RefreshControls();
        ClearStatus();
        debounce.Stop();
        if (now) Preview(); else debounce.Start();
    }

    protected override void ClearStatus()
    {
        if (broken) { ShowBroken(); return; }
        if (DynamicLightingOn())
            ShowStatus("Windows Dynamic Lighting is on and may override this.", true,
                () => Process.Start("ms-settings:personalization-lighting"), "Open Windows settings");
        else if (Screening)
        {
            var why = Paused();
            if (why != null) ShowStatus(why, true);
            else if (ScreenCapture.Missing(setup.Monitor)) ShowStatus("The chosen monitor isn't connected. Using the main monitor.", true);
            else ShowStatus("Live only.");
        }
        else if (live && kind.Profile)
            ShowStatus(dirty ? "Set by G-Lite, not saved to the keyboard" : Known() ? "Matches the keyboard" : "");
        else if (live) ShowStatus(Known() ? "Set by G-Lite, not saved to the keyboard" : "");
        else ShowStatus(dirty ? "Not saved to the keyboard" : stored == null ? "" : "Matches the keyboard");
    }

    public static int TestDynamicLighting = -1;

    static bool DynamicLightingOn()
    {
        if (TestDynamicLighting >= 0) return TestDynamicLighting == 1;
        using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Lighting"))
            return key != null && Equals(key.GetValue("AmbientLightingEnabled"), 1);
    }

    void ShowBroken()
    {
        const string half = "The lighting saved on the keyboard is half written.";
        var last = fix;
        if (last != null)
            ShowStatus(half + " Last backup: " + When(last) + ".", true,
                () => Restore(last.Files, "the backup from " + When(last)), "Restore backup");
        else ShowStatus(half, true, () => Restore(new G515Profile.Files(), "the factory lighting"), "Restore factory lighting");
    }

    void ShowBackups()
    {
        BackupMenu(backupsButton, BackupId, "Restore the factory lighting", () => Restore(new G515Profile.Files(), "the factory lighting"),
            entry => Backups.Describe(entry.Files), entry => G515Profile.FilesAreValid(entry.Files),
            entry => Restore(entry.Files, "the backup from " + When(entry)));
    }

    void Restore(G515Profile.Files backup, string what)
    {
        debounce.Stop();
        previewSequence++;
        saveButton.Enabled = false;
        ShowStatus("Restoring " + what + "…");
        LogUserAction("Restore " + what);
        int level = storedBrightness;
        RunOnDevice(hidpp =>
        {
            Backups.Save(BackupId, G515Profile.Read(hidpp), DeviceLabel);
            G515Profile.Restore(hidpp, backup);
            claimed = false;
            if (level >= 0) G515Profile.SetBrightness(hidpp, level);
        }, () =>
        {
            Note = "Restored " + what;
            dirty = false;
            Load();
        });
    }

    protected override void Revert()
    {
        if (Screening)
        {
            ScreenOff();
            dirty = true;
            current = stored != null ? stored.Clone() : new Lighting();
        }
        if (live) { Forget(); if (kind.Profile) Load(); return; }
        if (stored == null) { RefreshControls(); ClearStatus(); return; }
        current = stored.Clone();
        brightness = storedBrightness;
        Release();
        RefreshControls();
        ClearStatus();
    }

    void Preview()
    {
        if (Screening)
        {
            Settings.Set(Saved, setup.ToText());
            int screenLevel = brightness;
            RunOnDevice(hidpp =>
            {
                if (screenLevel != G515Profile.Brightness(hidpp)) G515Profile.SetBrightness(hidpp, screenLevel);
            }, ClearStatus);
            return;
        }
        idleLevel = 0;
        var lighting = current.Clone();
        int level = brightness, startedAt = ++previewSequence;
        bool repaint = leftScreen;
        leftScreen = false;
        if (live) { Apply(lighting, bar == null ? null : bar.Clone(), level, startedAt); return; }
        RunOnDevice(hidpp =>
        {
            if (startedAt != previewSequence) return;
            if (level != G515Profile.Brightness(hidpp)) G515Profile.SetBrightness(hidpp, level);
            if (!claimed && !repaint && lighting.Same(stored)) return;
            if (!claimed) { G515Profile.Claim(hidpp); claimed = true; }
            try { G515Profile.Preview(hidpp, lighting); }
            catch (HidppError ex) { if (ex.Code != 5) throw; G515Profile.Claim(hidpp); G515Profile.Preview(hidpp, lighting); }
        }, () => { if (startedAt == previewSequence) ClearStatus(); });
    }

    void Save()
    {
        if (live) { SaveProfile(); return; }
        var lighting = current.Clone();
        int level = brightness;
        saveButton.Enabled = false;
        ShowStatus("Saving…");
        LogUserAction("Save to keyboard");
        RunOnDevice(hidpp =>
        {
            var files = G515Profile.Snapshot(hidpp);
            Backups.Save(BackupId, files, DeviceLabel);
            G515Profile.Store(hidpp, files, lighting);
            claimed = false;
        }, () =>
        {
            stored = lighting;
            storedBrightness = level;
            dirty = false;
            unknownStored = false;
            RefreshControls();
            ShowStatus("Saved to the keyboard");
        });
    }

    string ProfileBackups { get { return found.SettingsKey; } }

    // Saves into the active onboard profile, after a backup of it. The keyboard then runs the lighting itself, so nothing is
    // remembered to send again.
    void SaveProfile()
    {
        var lighting = current.Clone();
        var others = bar == null ? null : bar.Clone();
        int editsAtStart = edits;
        debounce.Stop();
        previewSequence++;
        saveButton.Enabled = false;
        ShowStatus("Saving…");
        LogUserAction("Save to keyboard");
        RunOnDevice(hidpp =>
        {
            if (clusters == null) clusters = G915Lighting.Clusters(hidpp);
            int sector;
            var profile = G915Lighting.ReadProfile(hidpp, out sector);
            var built = G915Lighting.Build(profile, clusters, lighting, others, !kind.NoIdleTimes);
            Backups.Save(ProfileBackups, new Dictionary<string, byte[]> { { sector.ToString("X4"), profile } }, DeviceLabel);
            G915Lighting.Store(hidpp, sector, built);
            claimed = false;
        }, () =>
        {
            Settings.Remove(Remembered);
            stored = lighting;
            unknownStored = false;
            if (editsAtStart == edits) dirty = false;
            RefreshControls();
            ShowStatus("Saved to the keyboard");
        });
    }

    static int BackupSector(Backups.Entry entry)
    {
        int sector;
        return entry.Data.Count == 1
            && int.TryParse(entry.Data.Keys.First(), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out sector)
            && sector > 0 && sector < 0x100
            && MouseProtocol.Valid(entry.Data.Values.First()) ? sector : -1;
    }

    void ShowProfileBackups()
    {
        BackupMenu(backupsButton, ProfileBackups, "Restore the factory lighting", () => RestoreProfile(null),
            entry =>
            {
                if (BackupSector(entry) < 0) return "damaged, can't be restored";
                var lighting = G915Lighting.Decode(entry.Data.Values.First(), clusters == null ? 1 : G915Lighting.Keys(clusters));
                return lighting == null ? "an effect G-Lite doesn't know" : G515Profile.EffectById(lighting.Effect).Name;
            },
            entry => BackupSector(entry) > 0, RestoreProfile);
    }

    // A null backup restores the factory lighting.
    void RestoreProfile(Backups.Entry entry)
    {
        int sector = entry == null ? -1 : BackupSector(entry);
        var backup = entry == null ? null : entry.Data.Values.First();
        string what = entry == null ? "the factory lighting" : "the backup from " + When(entry);
        debounce.Stop();
        previewSequence++;
        saveButton.Enabled = false;
        ShowStatus("Restoring " + what + "…");
        LogUserAction("Restore " + what);
        RunOnDevice(hidpp =>
        {
            int now;
            var current = G915Lighting.ReadProfile(hidpp, out now);
            if (backup != null && now != sector)
                throw new RefusedException("The keyboard runs another profile than the one in this backup");
            var profile = backup ?? G915Lighting.FactoryLighting(hidpp, now, current);
            Backups.Save(ProfileBackups, new Dictionary<string, byte[]> { { now.ToString("X4"), current } }, DeviceLabel);
            G915Lighting.Store(hidpp, now, profile);
            claimed = false;
        }, () =>
        {
            Settings.Remove(Remembered);
            Note = "Restored " + what;
            dirty = false;
            Load();
        });
    }

    void Apply(Lighting lighting, Lighting others, int level, int startedAt)
    {
        bool known = !unknownStored, sent = false;
        RunOnDevice(hidpp =>
        {
            if (startedAt != previewSequence) return;
            if (clusters == null) clusters = G915Lighting.Clusters(hidpp);
            if (level != G515Profile.Brightness(hidpp)) G515Profile.SetBrightness(hidpp, level);
            if (!known) return;
            ClaimLights(hidpp);
            G915Lighting.Preview(hidpp, clusters, lighting, others);
            sent = true;
        }, () =>
        {
            if (sent && startedAt == previewSequence)
                Settings.Set(Remembered, lighting.ToText(level) + (others == null ? "" : "#" + others.ToText(level)));
            if (startedAt == previewSequence) ClearStatus();
        });
    }

    void Forget()
    {
        Settings.Remove(Remembered);
        bar = null;
        part = 0;
        unknownStored = true;
        dirty = false;
        edits++;
        int startedAt = ++previewSequence;
        debounce.Stop();
        RefreshControls();
        ClearStatus();
        int level = -1;
        RunOnDevice(hidpp => { G915Lighting.Release(hidpp); level = G515Profile.Brightness(hidpp); },
            () => { if (startedAt == previewSequence && level >= 0) { brightness = level; RefreshControls(); } });
    }

    void LoadLive()
    {
        dirty = false;
        debounce.Stop();
        ShowStatus("Reading the keyboard…");
        int editsAtStart = edits, startedAt = previewSequence;
        int level = 100;
        string kept = Settings.Get(Remembered);
        Lighting lighting = null, barLighting = null;
        int savedLevel = -1;
        if (kept != null)
            try
            {
                var pieces = kept.Split('#');
                lighting = Lighting.FromText(pieces[0], out savedLevel);
                barLighting = BarFrom(pieces);
            }
            catch (Exception) { lighting = null; barLighting = null; }
        bool dropped = false;
        bool screening = Screening, readLayout = layoutCode < 0 && found.Has(0x4540);
        Lighting saved = null;
        RunOnDevice(hidpp =>
        {
            if (readLayout) layoutCode = hidpp.Call(hidpp.FeatureIndex(0x4540), 0)[0];
            clusters = G915Lighting.Clusters(hidpp);
            // Only when nothing is remembered: a remembered lighting replaces what's saved. Optional, so any failure leaves the
            // screen on live lighting as before.
            if (kind.Profile && lighting == null)
                try { int sector; saved = G915Lighting.Decode(G915Lighting.ReadProfile(hidpp, out sector), G915Lighting.Keys(clusters)); }
                catch (Exception) { }
            if (lighting != null && startedAt == previewSequence && !screening)
            {
                if (savedLevel >= 0 && savedLevel <= 100 && savedLevel != G515Profile.Brightness(hidpp))
                    G515Profile.SetBrightness(hidpp, savedLevel);
                ClaimLights(hidpp);
                try { G915Lighting.Preview(hidpp, clusters, lighting, barLighting); }
                catch (RefusedException) { G915Lighting.Release(hidpp); dropped = true; }
            }
            level = G515Profile.Brightness(hidpp);
        }, () =>
        {
            if (dropped) { Settings.Remove(Remembered); lighting = null; }
            storedBrightness = level;
            if (editsAtStart == edits)
            {
                dirty = lighting != null && kind.Profile;
                if (lighting == null && saved != null) lighting = saved;
                stored = lighting == null ? null : lighting.Clone();
                unknownStored = lighting == null;
                brightness = level;
                if (lighting != null && !Screening) { current = lighting; bar = barLighting; part = barLighting == null ? 0 : 1; }
            }
            ApplyLayout();
            RefreshControls();
            Loaded();
        });
    }

    public override void Load()
    {
        if (live) { LoadLive(); return; }
        bool editing = dirty && !Screening;
        if (!editing) { dirty = false; debounce.Stop(); }
        ShowStatus("Reading the keyboard…");
        int editsAtStart = edits;
        Lighting lighting = null;
        int level = 100;
        bool unknown = false, half = false, readLayout = layoutCode < 0 && found.Has(0x4540);
        RunOnDevice(hidpp =>
        {
            if (readLayout) layoutCode = hidpp.Call(hidpp.FeatureIndex(0x4540), 0)[0];
            G515Profile.Files files;
            try { files = G515Profile.Snapshot(hidpp); }
            catch (HalfWrittenException) { half = true; level = G515Profile.Brightness(hidpp); return; }
            lighting = G515Profile.Decode(files.UserProfile ?? files.FactoryProfile);
            unknown = lighting == null;
            if (lighting == null) lighting = new Lighting();
            level = G515Profile.Brightness(hidpp);
        }, () =>
        {
            ApplyLayout();
            broken = half;
            fix = half ? Backups.List(BackupId).FirstOrDefault(entry => G515Profile.FilesAreValid(entry.Files)) : null;
            storedBrightness = level;
            stored = half ? null : lighting.Clone();
            unknownStored = half || unknown;
            if (editsAtStart == edits && !editing)
            {
                brightness = level;
                if (!half && !Screening) current = lighting;
            }
            if (Screening) dirty = true;
            RefreshControls();
            Loaded();
        });
    }

    void PresenceChanged()
    {
        if (Screening) { Tick(); ClearStatus(); return; }
        bool now = Presence.Locked || Presence.DisplayOff;
        if (live || now == away) return;
        away = now;
        if (!dirty) return;
        if (away) { if (idleLevel != 2) GiveBack(); }
        else Preview();
    }

    internal void Watch()
    {
        if (live || Screening || !dirty || !claimed || away) { idleLevel = 0; return; }
        Idle();
        if (++watchTicks % 6 != 0 || watching || idleLevel == 2) return;
        watching = true;
        var lighting = current.Clone();
        int level = brightness;
        RunOnDevice(hidpp =>
        {
            try
            {
                if (!claimed || hidpp.CallUnlogged(hidpp.FeatureIndex(0x8071), 5, 0x00)[1] != 0) return;
                G515Profile.Claim(hidpp);
                claimed = true;
                if (level != G515Profile.Brightness(hidpp)) G515Profile.SetBrightness(hidpp, level);
                Show(hidpp, lighting, idleLevel);
            }
            catch (HidppError) { }
            catch (TimeoutException) { }
            finally { watching = false; }
        }, null, message => { });
    }

    void Idle()
    {
        int idle = WindowHelpers.IdleSeconds();
        int level = current.Sleep > 0 && idle >= current.Sleep ? 2 : current.Idle > 0 && idle >= current.Idle ? 1 : 0;
        if (level == idleLevel) return;
        idleLevel = level;
        var lighting = current.Clone();
        RunOnDevice(hidpp =>
        {
            if (!claimed) return;
            try { Show(hidpp, lighting, level); }
            catch (HidppError) { }
            catch (TimeoutException) { }
        }, null, message => { });
    }

    static void Show(Hidpp hidpp, Lighting lighting, int level)
    {
        if (level == 2) G515Profile.Preview(hidpp, new Lighting { Effect = 0 });
        else G515Profile.Preview(hidpp, lighting, level == 1 ? lighting.Dim : 100);
    }

    public void Release()
    {
        debounce.Stop();
        if (!dirty) return;
        dirty = false;
        GiveBack();
    }

    void GiveBack()
    {
        debounce.Stop();
        idleLevel = 0;
        int level = storedBrightness;
        var back = unknownStored || stored == null ? null : stored.Clone();
        bool repaint = leftScreen && back != null;
        leftScreen = false;
        previewSequence++;
        RunOnDevice(hidpp =>
        {
            if (level >= 0) G515Profile.SetBrightness(hidpp, level);
            if (!claimed && !repaint) return;
            if (!claimed) G515Profile.Claim(hidpp);
            try { if (back != null) G515Profile.Preview(hidpp, back); }
            catch (HidppError ex) { if (ex.Code != 5) throw; }
            G515Profile.Release(hidpp);
            claimed = false;
        });
    }
}
