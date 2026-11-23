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

class KeyboardPage : DevicePage
{
    static readonly int[] IdleSecs = { 0, 15, 30, 60, 120, 300, 600 };
    static readonly string[] IdleText = { "Never", "15 s", "30 s", "1 min", "2 min", "5 min", "10 min" };
    static readonly int[] DimPcts = { 0, 10, 25, 50, 75 };
    static readonly string[] DimText = { "Dark", "10%", "25%", "50%", "75%" };
    static readonly int[] SleepSecs = { 0, 60, 120, 300, 600, 900, 1800 };
    static readonly string[] SleepText = { "Never", "1 min", "2 min", "5 min", "10 min", "15 min", "30 min" };
    static readonly int[] Directions = { 6, 1, 3, 8 }, VerticalDirections = { 6, 1, 7, 2, 3, 8, 0 },
        UpDownDirections = { 6, 1, 7, 2, 3, 8 };
    static readonly string[] DirText = { "Left", "Right", "Center out", "Center in" },
        VerticalDirText = { "Left", "Right", "Up", "Down", "Center out", "Center in", "All" },
        UpDownDirText = { "Left", "Right", "Up", "Down", "Center out", "Center in" };
    int[] directionValues;
    readonly Label intensityHead, intensityValue;
    readonly Slider intensity;
    readonly Toggle livePause, liveBattery, solidToggle;
    readonly SegmentedPicker liveDirections;
    // The pause row's heading, switches and their labels, shown for a live effect in place of the Direction row.
    readonly List<Control> liveControls = new List<Control>();
    const byte ScreenEffect = 0xFE;
    string Remembered { get { return "lighting." + found.SettingsKey; } }
    string Saved { get { return "screen." + found.SettingsKey; } }
    // 1 while the Screen effect runs, the lighting's text while a live effect does.
    string Active { get { return "screen.on." + found.SettingsKey; } }
    string ActiveText { get { return Sampling ? "1" : current.ToText(brightness); } }
    // The Screen effect or a live effect (LiveEffects): G-Lite draws the keys itself, through 0x8081, and nothing can be saved.
    bool Screening { get { return current.Effect == ScreenEffect || LiveEffects.Is(current.Effect); } }
    // The Screen effect itself, with its monitor, zones and Screen box.
    bool Sampling { get { return current.Effect == ScreenEffect; } }
    // A live effect every 40 ms on a cable; without one, every 100 ms, since a G515 over LIGHTSPEED answered busy and looked
    // choppy at 40 (a frame took it 30–39 ms, docs/screen.md).
    int FrameInterval { get { return Sampling ? setup.Interval : found.Link == "USB" ? 40 : 100; } }
    // About 10 messages, the size of a Screen frame the G515 showed in 30–39 ms: Aurora changes nearly every key each frame (about
    // 23 messages on a TKL), which took the G515 longer than the 100 ms between frames.
    const int WirelessKeysPerFrame = 40;
    string EffectName { get { return Sampling ? "Screen" : Info(current.Effect) != null ? Info(current.Effect).Name : "live"; } }
    static EffectInfo Info(byte id) { return G515Profile.EffectById(id) ?? LiveEffects.Info(id); }
    // The Effect dropdown's ids, in order: the keyboards' own effects, then the Screen effect and the live ones on keyboards with 0x8081.
    readonly List<byte> effectIds;
    LiveEffect animation;
    byte animationId;
    KeyField keyField;
    Stopwatch animationClock;
    double lastFrame;
    string animationProblem, runningName = "Screen";
    // The colours last sent by lighting id; only the keys that changed are sent, and all of them every 2 s.
    volatile Dictionary<int, int> sentColors;
    bool individualColors = true;
    // The period and intensity were last set for a live effect, so a keyboard effect picked next takes its own defaults.
    bool liveSettings;
    bool leftScreen;

    readonly KeyboardKinds kind;
    readonly bool live, canScreen;
    List<byte[]> clusters;
    ScreenSettings setup;
    readonly Timer screenTimer = new Timer { Interval = 100 }, snapTimer = new Timer { Interval = 1000 },
        watchTimer = new Timer { Interval = 500 };
    volatile bool sending, failing, statusDirty, layoutDirty, capturing, snapping, watching, claimed;
    // Whether the keyboard's Wave lists Up and Down (G915Lighting.Clusters): -1 until read, then 0 or 1 (ApplyWaveDirections).
    volatile int verticalWave = -1;
    // A G515 twice kept answering every per-key frame with busy until it was turned off and on, though claimed again every 6 s
    // (2026-11-22): after a busy frame, the lighting is given back to the keyboard before the next claim, until a release goes
    // through. Untried on a stuck one.
    volatile bool releaseFirst;
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
    readonly SegmentedPicker directions;
    readonly Dropdown effects;
    readonly ColorRow colors;
    readonly Slider speed, bright;
    readonly Dropdown idleBox, dimBox, sleepBox;
    readonly int never;
    readonly FlatButton saveButton, backupsButton;
    readonly KeyPicture picture;
    Backups.Entry fix;
    readonly SegmentedPicker slotPicker;
    readonly Control pictureBox, lightBox;
    bool wide;
    readonly SegmentedPicker viewPick;
    readonly RoundedBox buttonsBox;
    readonly GameModeBox gameBox;
    readonly ButtonRows buttonRows;
    // Settings, Bindings (the G keys) or Game mode; views holds the ones this keyboard has, in the picker's order.
    const int SettingsView = 0, ButtonsView = 1, GameView = 2;
    readonly List<int> views = new List<int> { SettingsView };
    int openView;
    // The keys the Game mode key turns off besides Windows and Menu, as USB usages (GameModeKeys). They're live, like a lighting not
    // saved: kept in settings and presets, and sent again whenever a connection opens (Opened), since the keyboard forgets them when
    // it loses power, and when G HUB lets go of the keyboard. Replaced, never changed in place, since Opened reads it on the queue.
    // gameMode: the Game view sets the list; the view may also be shown without it (GameModeSupport.NotImplemented).
    readonly bool gameMode;
    volatile List<int> gameKeys = new List<int>();
    string GameKeysSetting { get { return "gamemode." + found.SettingsKey; } }
    // The G keys' bindings as edited, and as the running profile has them. They reach the keyboard on Save only.
    byte[][] gKeys, storedGKeys;
    // The sector gKeys were read from, so Save never writes one slot's G keys into another; and the G keys' own edit count, apart
    // from the lighting's edits.
    int gKeysSector, gKeyEdits;
    bool GKeysChanged { get { return gKeys != null && storedGKeys != null && !Bindings.Same(gKeys, storedGKeys); } }
    List<int> slots = new List<int>();
    int running;
    volatile byte profilesFeature, brightnessFeature;
    volatile bool checkingSlot, checkingBrightness;
    // The keyboard isn't running its onboard profiles (0x8100 fn 2 isn't 1): G HUB switches it off them while it runs (seen on the
    // G815, 2026-11-21), and then fn 4 answers 0, M1-M3 don't show and Save would be refused. Save is off until it runs them again.
    bool takenOver;

    public KeyboardPage(FoundDevice found, Hidpp fake, MouseEventHandler drag, KeyboardKinds kind)
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

        if (kind.Profile)
        {
            slotPicker = Ui.Add(this, new SegmentedPicker { AccessibleName = "Onboard profile", Visible = false }, 264, 685, 144, 32);
            // The running profile's own M key on the keyboard sends nothing and leaves 0x8071 control as it was (tried on the G815,
            // 2026-11-21), so a live effect can't follow it; picking that profile here stops the effect instead.
            slotPicker.Picked += i => { if (i >= 0 && i < slots.Count) SlotSwitched(slots[i], true, Screening && slots[i] == running); };
            FooterPicker = slotPicker;
        }

        pictureBox = Ui.Add(this, new RoundedBox(), 20, 118, 616, 209);
        Ui.Text(pictureBox, "Live on the keyboard", 14, 12, () => Theme.Dim, 9f, Ui.Semibold);
        picture = Ui.Add(pictureBox, new KeyPicture(kind.Keys), 14, 36, 588, 161);
        if (Parts)
        {
            picture.Bar = new HashSet<int>(kind.Bar);
            picture.PartPicked += onBar => { if (Screening || !Known() || bar == null) return; part = onBar ? 2 : 1; RefreshControls(); };
        }

        canScreen = found.Has(0x8081);
        setup = ScreenSettings.FromText(Settings.Get(Saved));
        var light = lightBox = Ui.Add(this, new RoundedBox(), 20, 339, 380, 272);
        lightHead = Ui.Text(light, "Lighting", 14, 12, () => Theme.Dim, 9f, Ui.Semibold);
        var effectHead = Ui.Text(light, "Effect", 14, 40, () => Theme.Dim, 9f);
        effectIds = G515Profile.Effects.Select(effect => effect.Id).ToList();
        var names = G515Profile.Effects.Select(effect => effect.Name).ToList();
        if (canScreen)
        {
            effectIds.Add(ScreenEffect);
            names.Add("Screen");
            effectIds.AddRange(LiveEffects.All.Select(effect => effect.Id));
            names.AddRange(LiveEffects.All.Select(effect => effect.Name));
        }
        effects = Ui.Add(light, new Dropdown { AccessibleName = "Effect" }, 14, 58, 196, 32);
        effects.Items.AddRange(names);
        effects.Badge = i => effectIds[i] == ScreenEffect || LiveEffects.Is(effectIds[i]) ? "Live" : null;
        effects.Picked += i =>
        {
            byte id = effectIds[i];
            if (id == ScreenEffect || LiveEffects.Is(id))
            {
                part = 0;
                bar = null;
                bool was = Screening;
                byte before = current.Effect;
                var liveInfo = LiveEffects.Info(id);
                current.Effect = id;
                if (liveInfo != null && liveInfo.Speed && (current.Period < liveInfo.MinPeriod || current.Period > liveInfo.MaxPeriod))
                    current.Period = liveInfo.DefaultPeriod;
                // The keyboard's intensity (100) means the most of a live effect's density or height; each starts in the middle.
                if (liveInfo != null && !LiveEffects.Is(before)) current.Intensity = LiveEffects.DefaultIntensity;
                if (liveInfo != null) liveSettings = true;
                if (liveInfo == null || !liveInfo.OffersSolid) current.Solid = false;
                if (liveInfo != null && liveInfo.Direction && before != id) current.Direction = LiveEffects.DefaultDirection(id);
                if (!was) ScreenOn();
                else if (before != id) SoftwareSwitched();
                Changed(true);
                return;
            }
            // A live effect's period and intensity mean something else (Aurora's 20 s turn, Rain's density), so after one, even through
            // the Screen effect, the keyboard's effect starts at its own defaults. A keyboard without the slider always sends 100.
            bool fromLive = liveSettings;
            liveSettings = false;
            if (Screening) ScreenOff();
            var effect = G515Profile.EffectById(id);
            var target = Target();
            target.Effect = effect.Id;
            if (effect.Speed && (fromLive || target.Period < effect.MinPeriod || target.Period > effect.MaxPeriod))
                target.Period = effect.DefaultPeriod;
            if (fromLive || !(kind.Intensity && effect.Intensity)) target.Intensity = 100;
            target.Solid = false;
            if (fromLive && Array.IndexOf(directionValues, target.Direction) < 0) target.Direction = 1;
            Changed(true);
        };
        colorHead = Ui.Text(light, "Color", 14, 98, () => Theme.Dim, 9f);
        colors = new ColorRow(light, 115);
        colors.Picked += color => { Target().Color = color; Changed(true); };

        directionHead = Ui.Text(light, "Direction", 14, 156, () => Theme.Dim, 9f);
        directionValues = kind.VerticalWave ? VerticalDirections : Directions;
        directions = Ui.Add(light, new SegmentedPicker { Items = kind.VerticalWave ? VerticalDirText : DirText, AccessibleName = "Direction" },
            14, 174, 352, 32);
        directions.Picked += i => { Target().Direction = directionValues[i]; Changed(true); };

        Ui.Text(light, kind.Bar != null ? "Brightness (all)" : "Brightness", 14, 214, () => Theme.Dim, 9f);
        brightValue = Value(light, 14, 214, 172);
        bright = Ui.Add(light, new Slider { AccessibleName = "Brightness", Stops = kind.BrightnessLevels }, 14, 234, 172, 24);
        bright.Changed += (s, e) => BrightnessChanged(bright.Value);
        speedHead = Ui.Text(light, "Speed", 194, 214, () => Theme.Dim, 9f);
        speedValue = Value(light, 194, 214, 172);
        speed = Ui.Add(light, new Slider { AccessibleName = "Speed" }, 194, 234, 172, 24);
        speed.Changed += (s, e) =>
        {
            var target = Target();
            var effect = Info(target.Effect);
            target.Period = effect.MaxPeriod - (effect.MaxPeriod - effect.MinPeriod) * speed.Value / 100;
            Changed(false);
        };
        // Also for the live effects.
        if (kind.Intensity || canScreen)
        {
            intensityHead = Ui.Text(light, "Intensity", 14, 266, () => Theme.Dim, 9f);
            intensityValue = Value(light, 14, 266, 172);
            intensity = Ui.Add(light, new Slider { AccessibleName = "Intensity" }, 14, 286, 172, 24);
            intensity.Changed += (s, e) => { Target().Intensity = intensity.Value; Changed(false); };
            light.Height += 52;
        }

        foreach (Control control in light.Controls)
            if (control != lightHead && control != effectHead && control != effects) plainLight.Add(control);

        // A live effect has its own directions (Audio, Rain, Fire) in the Direction row, and the Screen box's two pause switches, the
        // same settings, since it has no box of its own: right of Intensity, the first in line with its slider, the second below it
        // in the narrow box and beside it in the wide one (Widen).
        liveDirections = Ui.Add(light,
            new SegmentedPicker { Items = LiveEffects.ShortDirectionNames, AccessibleName = "Live direction", Visible = false },
            14, 174, 352, 32);
        liveDirections.Picked += i => { Target().Direction = LiveEffects.Directions[i]; Changed(true); };
        var pauseHead = Ui.Text(light, "Pause", 194, 266, () => Theme.Dim, 9f);
        livePause = SideOption(light, "Screen off or locked", "Pause when screen off or locked", 194, 289, 132,
            on => { setup.PauseWhenLocked = on; ScreenChanged(); });
        liveBattery = SideOption(light, "Low battery", "Pause on low battery", 194, 310, 132,
            on => { setup.PauseOnBattery = on; ScreenChanged(); });
        liveControls.Add(pauseHead);
        // Room for Low battery below, ending above the footer's line at 673.
        if (intensity != null) light.Height += 6;
        // Beside the colours, for Audio: the colour as it is, not turning with height.
        // Placed after the colour row's + in RefreshControls, which moves right once a custom colour is kept.
        solidToggle = SideOption(light, "Solid", "Solid color", 272, 123, 34, on => { Target().Solid = on; Changed(true); });

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
            // In the title row, so the box keeps the others' rows (Intensity and the pause switches) above the footer.
            perZone = Ui.Add(light, new Toggle { AccessibleName = "Lighting per zone" }, 96, 13, 34, 18);
            perZone.Changed += () => PerZone(perZone.Checked);
            var perZoneLabel = Ui.Text(light, "Per zone", 136, 12, () => Theme.Text, 9f);
            perZone.Follow(perZoneLabel);
            partPicker = Ui.Add(light, new SegmentedPicker { Items = new[] { "Keys", "Light bar" }, AccessibleName = "Part" },
                226, 7, 140, 28);
            partPicker.Picked += i => { part = i + 1; RefreshControls(); };
        }
        foreach (var control in screenLight) control.Visible = false;
        picture.KeysPicked += picked =>
        {
            if (!Sampling) return;
            foreach (int key in picked) if (picture.IdOf(key) >= 0) setup.Keys[picture.IdOf(key)] = selectedZone;
            ScreenChanged();
        };

        screenBox = Ui.Add(this, new RoundedBox { Visible = false }, 412, 339, 224, light.Height);
        Ui.Text(screenBox, "Screen", 14, 12, () => Theme.Dim, 9f, Ui.Semibold);
        Ui.Text(screenBox, "Intensity", 14, 36, () => Theme.Dim, 9f);
        boostValue = Value(screenBox, 14, 36, 196);
        boostSlider = Ui.Add(screenBox, new Slider { AccessibleName = "Intensity" }, 14, 54, 196, 20);
        boostSlider.Changed += (s, e) =>
        {
            setup.Boost = boostSlider.Value; boostValue.Text = setup.Boost + "%"; force = true;
            Edited(); debounce.Stop(); debounce.Start();
        };
        Ui.Text(screenBox, "Brightness", 14, 78, () => Theme.Dim, 9f);
        screenBrightValue = Value(screenBox, 14, 78, 196);
        screenBright = Ui.Add(screenBox, new Slider { AccessibleName = "Brightness", Stops = kind.BrightnessLevels }, 14, 96, 196, 20);
        screenBright.Changed += (s, e) => BrightnessChanged(screenBright.Value);
        Ui.Text(screenBox, "Update rate", 14, 120, () => Theme.Dim, 9f);
        rateValue = Value(screenBox, 14, 120, 196);
        rateSlider = Ui.Add(screenBox, new Slider { AccessibleName = "Update rate", Max = 9, Step = 1 }, 14, 138, 196, 20);
        rateSlider.Changed += (s, e) =>
        {
            setup.Interval = 1000 - rateSlider.Value * 100;
            rateValue.Text = setup.Interval + " ms";
            screenTimer.Interval = FrameInterval;
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

        var idle = Ui.Add(this, new RoundedBox(), 412, 339, 224, light.Height);
        idleArea = idle;
        bool note = live && !kind.Profile;
        Ui.Text(idle, note ? "Saving" : "When idle", 14, 12, () => Theme.Dim, 9f, Ui.Semibold);
        var noteText = Ui.Text(idle,
            "G-Lite can't save to this keyboard yet. It keeps your lighting and sets it again when G-Lite starts and when you open this screen. Picking – as the G-Lite preset gives the keyboard its own lighting back."
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

        gameMode = (kind.GameMode == GameModeSupport.Tested || kind.GameMode == GameModeSupport.Experimental) && found.Has(0x4522);
        bool gameShown = gameMode || kind.GameMode == GameModeSupport.NotImplemented;
        if (kind.GKeys != null) views.Add(ButtonsView);
        if (gameShown) views.Add(GameView);
        if (views.Count > 1)
        {
            // Right of the preset row, which reaches 436 while a preset is named: three views get 64 px each, so Game mode is "Game".
            var viewNames = views.Select(shown => shown == SettingsView ? "Settings" : shown == ButtonsView ? "Bindings" : "Game");
            int pickWidth = views.Count > 2 ? 192 : 196;
            viewPick = Ui.Add(this, new SegmentedPicker { Items = viewNames.ToArray(), AccessibleName = "View", Visible = false },
                636 - pickWidth, 72, pickWidth, 32);
            viewPick.Picked += index => { if (index >= 0 && index < views.Count) ShowView(views[index]); };
        }
        if (gameShown)
        {
            if (gameMode) gameKeys = GameModeKeys.FromText(Settings.Get(GameKeysSetting));
            string gameText = "Select the keys you want to disable while gaming. Windows and Menu keys are always disabled in Game mode. "
                + "Changes apply immediately and can be saved in your G-Lite presets, but not in the keyboard's on-board memory.";
            if (kind.GameMode == GameModeSupport.Experimental)
                gameText = "Experimental, not tried on a G915 yet. " + gameText;
            if (!gameMode)
                gameText = "Not implemented yet: this keyboard sets its Game mode keys through a feature G-Lite doesn't know, "
                    + "so keys can't be selected here.";
            gameBox = Ui.Add(this, new GameModeBox(kind.Keys, gameMode, gameText), 20, 118, 616, 543);
            gameBox.Picked += usage =>
            {
                var keys = gameKeys.ToList();
                if (!keys.Remove(usage)) keys.Add(usage);
                SetGameKeys(keys);
            };
            gameBox.Cleared += () => SetGameKeys(new int[0]);
        }
        if (kind.GKeys != null)
        {
            buttonsBox = Ui.Add(this, new RoundedBox { Visible = false }, 20, 118, 616, 543);
            Ui.Text(buttonsBox, "G keys", 14, 12, () => Theme.Dim, 9f, Ui.Semibold);
            buttonRows = new ButtonRows(buttonsBox, Bindings.KeyboardActions, Value(buttonsBox, 200, 12, 402));
            buttonRows.Assigned += (row, binding) =>
            {
                if (gKeys == null || Screening) return;
                gKeys[row] = binding;
                gKeyEdits++;
                Edited();
                RefreshControls();
                ClearStatus();
            };
        }

        saveButton = Ui.Add(this, new FlatButton { Style = ButtonStyle.Primary, Text = "Save to keyboard", Visible = !note },
            496, 685, 140, 32);
        saveButton.Click += (s, e) => Save();

        string active = canScreen ? Settings.Get(Active) : null;
        Lighting kept = null;
        if (active != null && active != "1")
            try { int ignored; kept = Lighting.FromText(active, out ignored); }
            catch (Exception) { }
        if (active == "1") { current.Effect = ScreenEffect; ScreenOn(); }
        else if (kept != null && LiveEffects.Is(kept.Effect)) { current = kept; liveSettings = true; ScreenOn(); }
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

    // A switch with its label to the right, hidden until RefreshControls shows it; the label goes with it (Visible on both).
    Toggle SideOption(Control parent, string text, string name, int x, int y, int width, Action<bool> set)
    {
        var toggle = Ui.Add(parent, new Toggle { AccessibleName = name, Visible = false }, x, y, 34, 18);
        var label = Ui.Text(parent, text, x + 40, y - 1, () => Theme.Text, 9f);
        label.AutoSize = false;
        label.Size = new Size(width, 20);
        label.Visible = false;
        toggle.Follow(label);
        toggle.Changed += () => set(toggle.Checked);
        toggle.Tag = label;
        return toggle;
    }

    static void ShowOption(Toggle toggle, bool on)
    {
        toggle.Visible = on;
        ((Control)toggle.Tag).Visible = on;
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

    // A preset's brightness on a keyboard with fixed levels: the level below, as the keyboard itself would take it.
    int Allowed(int level)
    {
        if (kind.BrightnessLevels == null) return level;
        return kind.BrightnessLevels.Where(stop => stop <= level).DefaultIfEmpty(0).Max();
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
        saveButton.Enabled = !broken && !Screening && !takenOver;
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
        Settings.Set(Active, ActiveText);
        Settings.Set(Saved, setup.ToText());
        dirty = true;
        force = true;
        released = false;
        shown = sent = null;
        nextTry = DateTime.MinValue;
        runningName = EffectName;
        LogUserAction(runningName + " effect on");
        screenTimer.Interval = FrameInterval;
        if (Manual) return;
        screenTimer.Start();
        if (Sampling) { snapTimer.Start(); Snap(); }
    }

    // From the Screen effect or a live effect to another, while they run.
    void SoftwareSwitched()
    {
        StopAnimation();
        Settings.Set(Active, ActiveText);
        force = true;
        shown = sent = null;
        runningName = EffectName;
        LogUserAction(runningName + " effect on");
        screenTimer.Interval = FrameInterval;
        if (Sampling && !Manual) { snapTimer.Start(); Snap(); } else snapTimer.Stop();
    }

    void StopAnimation()
    {
        if (animation != null) animation.Dispose();
        animation = null;
        animationProblem = null;
        sentColors = null;
    }

    void ScreenOff()
    {
        screenTimer.Stop();
        snapTimer.Stop();
        StopAnimation();
        Settings.Remove(Active);
        shown = sent = null;
        released = false;
        LogUserAction(runningName + " effect off");
        leftScreen = true;
        if (claimed) RunOnDevice(hidpp => { ReleaseLights(hidpp); claimed = false; });
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
        if (setup.PauseOnBattery && battery >= 0 && !charging && battery < ScreenSettings.BatteryLimit)
            return "Paused: battery below " + ScreenSettings.BatteryLimit + "%.";
        return null;
    }

    int ZoneOfKey(int key) { return setup.ZoneOf(picture.IdOf(key), picture.XOf(key)); }

    void ShowLive()
    {
        if (!Sampling) return;
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
        if (releaseFirst)
        {
            ReleaseLights(hidpp);
            releaseFirst = false;
        }
        if (!quiet) { ClaimLights(hidpp); return; }
        if (found.Has(0x8101)) hidpp.CallUnlogged(hidpp.FeatureIndex(0x8101), 6, 0x05);
        hidpp.CallUnlogged(hidpp.FeatureIndex(0x8071), 5, 0x01, 0x03, 0x04);
        if (!found.Has(0x8101)) try { hidpp.CallUnlogged(hidpp.FeatureIndex(0x8071), 8, 0x01, 0x01, 0x00); } catch (HidppError) { }
    }

    void ReleaseLights(Hidpp hidpp)
    {
        if (live) G915Lighting.Release(hidpp); else G515Profile.Release(hidpp);
    }

    void ClaimLights(Hidpp hidpp)
    {
        if (found.Has(0x8101)) G515Profile.Claim(hidpp);
        else G915Lighting.Claim(hidpp);
    }

    void Failed(Exception error)
    {
        var refused = error as HidppError;
        if (refused != null && refused.Code == 8) releaseFirst = true;
        claimed = false;
        sent = null;
        sentColors = null;
        failing = true;
        nextTry = DateTime.UtcNow.AddSeconds(5);
        problem = "The " + runningName + " effect couldn't reach the keyboard: " + error.Message;
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
                    if (release && claimed) { ReleaseLights(hidpp); claimed = false; }
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
        if (!Sampling) { Animate(readBattery); return; }
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
                    if (sampled != null && !IsDisposed && Sampling && !sending && sampled.Length == setup.Zones.Count)
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

    // A frame of the live effect: drawn on the picture, and the keys whose colour changed sent to the keyboard (all of them every 2 s,
    // with the claim again, as the Screen effect does).
    void Animate(bool readBattery)
    {
        if (animation == null || animationId != current.Effect)
        {
            StopAnimation();
            animation = LiveEffects.Make(current.Effect);
            animationId = current.Effect;
            animationClock = Stopwatch.StartNew();
            lastFrame = 0;
            if (animation == null) return;
        }
        if (keyField == null) keyField = KeyField.From(picture);
        animation.Color = current.Color;
        animation.Period = current.Period;
        animation.Intensity = current.Intensity;
        animation.Solid = current.Solid;
        animation.Direction = current.Direction;
        double seconds = animationClock.Elapsed.TotalSeconds, elapsed = Math.Min(0.25, seconds - lastFrame);
        lastFrame = seconds;
        var frameColors = new int[keyField.Count];
        animation.Frame(keyField, seconds, elapsed, frameColors);
        if (animation.Problem != animationProblem) { animationProblem = animation.Problem; statusDirty = true; }
        ShowAnimation(frameColors);
        // Lost and Failed, on the device's thread, drop the map to force a full frame; this frame keeps the one it took.
        var lastSent = sentColors;
        bool full = force || lastSent == null || DateTime.UtcNow >= nextRefresh;
        if (full) nextRefresh = DateTime.UtcNow.AddSeconds(2);
        if (lastSent == null) sentColors = lastSent = new Dictionary<int, int>();
        var changes = new Dictionary<int, int>();
        for (int i = 0; i < keyField.Count; i++)
        {
            int id = keyField.Ids[i], color = frameColors[i], before;
            bool known = lastSent.TryGetValue(id, out before);
            if (full || !known || ScreenCapture.Distance(before, color) > 9 || (color == 0 && before != 0)) changes[id] = color;
        }
        // Without a cable a frame takes at most WirelessKeysPerFrame keys, the unsent and most changed first. One left out keeps its
        // old colour in lastSent, so it comes back further off next frame; on a full frame it's forgotten, so it goes first.
        if (found.Link != "USB" && changes.Count > WirelessKeysPerFrame)
        {
            var left = changes.OrderByDescending(pair =>
            {
                int before;
                return lastSent.TryGetValue(pair.Key, out before) ? ScreenCapture.Distance(before, pair.Value) : int.MaxValue;
            }).Skip(WirelessKeysPerFrame).Select(pair => pair.Key).ToList();
            foreach (int id in left)
            {
                changes.Remove(id);
                if (full) lastSent.Remove(id);
            }
        }
        if (changes.Count == 0 && !readBattery) return;
        force = false;
        foreach (var change in changes) lastSent[change.Key] = change.Value;
        sending = true;
        bool claimAgain = full;
        RunOnDevice(hidpp =>
        {
            try
            {
                if (changes.Count > 0)
                {
                    if (present == null) present = KeyLights.Present(hidpp);
                    if (!claimed || claimAgain) { Claim(hidpp, claimed); claimed = true; }
                    var lit = changes.Where(pair => present.Contains(pair.Key)).ToDictionary(pair => pair.Key, pair => pair.Value);
                    KeyLights.SendColors(hidpp, lit, ref individualColors);
                }
                if (readBattery) { ReadBattery(hidpp); statusDirty = true; }
                Worked();
            }
            catch (Exception ex) { Failed(ex); throw; }
            finally { sending = false; }
        }, null, message => { });
    }

    void ShowAnimation(int[] frameColors)
    {
        var byId = new Dictionary<int, int>();
        for (int i = 0; i < keyField.Count; i++) byId[keyField.Ids[i]] = frameColors[i];
        var keyColors = new Color[picture.Count];
        for (int key = 0; key < picture.Count; key++)
        {
            int color;
            byId.TryGetValue(picture.IdOf(key), out color);
            keyColors[key] = Color.FromArgb(color >> 16 & 0xFF, color >> 8 & 0xFF, color & 0xFF);
        }
        picture.ShowLive(keyColors, new bool[picture.Count]);
    }

    void ReadBattery(Hidpp hidpp)
    {
        var reply = hidpp.CallUnlogged(hidpp.FeatureIndex(0x1004), 1);
        battery = reply[0];
        charging = reply[2] != 0;
    }

    // Up and Down in the Direction row once the keyboard's Wave says it has them. Not All (0): no capability bit lists it, and it
    // was only tried on the G815, whose kind.VerticalWave gives all of them from the start.
    void ApplyWaveDirections()
    {
        if (verticalWave != 1 || kind.VerticalWave || directionValues == UpDownDirections) return;
        directionValues = UpDownDirections;
        directions.Items = UpDownDirText;
        directions.Invalidate();
    }

    void ApplyLayout()
    {
        if (iso || !KeyLayout.IsIso(layoutCode)) return;
        var model = kind.Iso;
        if (model == null) return;
        iso = true;
        picture.SetModel(model);
        if (gameBox != null) gameBox.SetModel(model);
        keyField = null;
        force = true;
        if (Sampling) SyncScreen();
    }

    void Snap()
    {
        if (!Sampling || !Visible || snapping) return;
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
        addZone.Enabled = setup.Zones.Count < ScreenSettings.MaxZones;
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
        screenTimer.Interval = FrameInterval;
        ShowLive();
    }

    internal void Exiting()
    {
        if (Screening)
        {
            if (!claimed) return;
            RunOnDevice(hidpp => { ReleaseLights(hidpp); claimed = false; });
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
            StopAnimation();
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

    protected override void Lost() { claimed = false; clusters = null; present = null; sentColors = null; }

    protected override void Opened(Hidpp hidpp) { SendGameKeys(hidpp); }

    protected override bool ReopensWhenFound { get { return gameMode && gameKeys.Count > 0; } }

    void SendGameKeys(Hidpp hidpp)
    {
        var keys = gameKeys;
        if (!gameMode || keys.Count == 0) return;
        try { GameModeKeys.Send(hidpp, keys); }
        catch (HidppError ex) { Log.Add(DeviceLabel, "error", "Game mode keys: " + ex.Message); }
    }

    protected override bool Damaged { get { return broken; } }

    protected override bool Unsaved
    {
        get
        {
            if (current == null) return false;
            if (live && kind.Profile) return dirty || Screening || GKeysChanged || gameKeys.Count > 0;
            if (live) return Known();
            return stored != null && (dirty || Screening);
        }
    }

    string LightingText() { return current.ToText(brightness) + BarText(brightness) + (Screening ? "|" + setup.ToText() : ""); }

    protected override string Current() { return LightingText() + (gameMode ? "!" + GameModeKeys.ToText(gameKeys) : ""); }

    // Presets hold the lighting and the Game mode keys, not the G keys: those belong to an onboard profile. A ~ and G keys after it,
    // from a preset saved while presets held them (2026-11-20), are left out. A lighting that's already the one shown isn't sent again,
    // so Revert after a Game mode key change, or a preset saved before presets held them, leaves the keyboard's lighting alone.
    protected override void ApplyPreset(string text)
    {
        string game;
        text = GameModeKeys.Split(text, out game).Split('~')[0];
        int level;
        var parts = text.Split('|');
        var pieces = parts[0].Split('#');
        var lighting = Lighting.FromText(pieces[0], out level);
        var newSetup = parts.Length > 1 ? ScreenSettings.FromText(parts[1]) : setup;
        if (game != null) SetGameKeys(GameModeKeys.FromText(game));
        if (text == LightingText()) { RefreshControls(); ClearStatus(); return; }
        bool software = lighting.Effect == ScreenEffect || LiveEffects.Is(lighting.Effect);
        if (software && !canScreen) { lighting.Effect = 0x01; software = false; }
        bar = software ? null : BarFrom(pieces);
        part = bar == null ? 0 : 1;
        bool was = Screening;
        byte before = current.Effect;
        setup = newSetup;
        current = lighting;
        liveSettings = LiveEffects.Is(lighting.Effect);
        brightness = Allowed(Math.Max(0, Math.Min(100, level)));
        if (was && !Screening) ScreenOff();
        if (!was && Screening) ScreenOn();
        else if (was && Screening && before != current.Effect) SoftwareSwitched();
        if (Sampling) ScreenChanged();
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
        if (gKeys == null && openView == ButtonsView) openView = SettingsView;
        bool screening = Screening, sampling = Sampling, settings = openView == SettingsView;
        foreach (var control in plainLight) control.Visible = !sampling;
        foreach (var control in screenLight) control.Visible = sampling;
        screenBox.Visible = sampling && settings;
        idleArea.Visible = !sampling && !kind.NoIdleTimes && settings;
        pictureBox.Visible = lightBox.Visible = settings;
        Widen(!sampling && kind.NoIdleTimes && !Parts);
        if (picture.Live != screening) picture.Live = screening;
        var view = Viewed;
        var effect = Info(view.Effect);
        bool known = Known();
        bool color = known && effect != null && effect.Color, hasDirection = known && effect != null && effect.Direction,
            hasSpeed = known && effect != null && effect.Speed,
            hasIntensity = known && effect != null && effect.Intensity && (kind.Intensity || LiveEffects.Is(effect.Id));
        // The G512 X's title row also holds Per zone, so it keeps the short heading.
        lightHead.Text = known || Parts ? "Lighting"
            : broken ? "Lighting (half written on the keyboard)"
            : live ? "Lighting (as on the keyboard)"
            : "Lighting (factory animation)";
        effects.Index = known ? effectIds.IndexOf(view.Effect) : -1;
        effects.Off = part == 2 ? new[] { effectIds.IndexOf(Ripple) } : new int[0];
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
        directions.Index = hasDirection ? Array.IndexOf(directionValues, view.Direction) : -1;
        SetDimmed(speedHead, hasSpeed);
        speed.Enabled = hasSpeed;
        if (hasSpeed) speed.Value = (effect.MaxPeriod - view.Period) * 100 / (effect.MaxPeriod - effect.MinPeriod);
        speedValue.Text = hasSpeed ? speed.Value + "%" : "";
        speed.Invalidate();
        if (intensity != null)
        {
            SetDimmed(intensityHead, hasIntensity);
            intensity.Enabled = hasIntensity;
            intensity.Value = view.Intensity;
            intensityValue.Text = hasIntensity ? view.Intensity + "%" : "";
            intensity.Invalidate();
        }
        bright.Value = brightness;
        brightValue.Text = brightness + "%";
        bright.Invalidate();
        idleBox.Index = Nearest(IdleSecs.Skip(never).ToArray(), current.Idle);
        dimBox.Index = Nearest(DimPcts, current.Dim);
        sleepBox.Index = Nearest(SleepSecs.Skip(never).ToArray(), current.Sleep);
        dimBox.Enabled = current.Idle > 0;
        saveButton.Enabled = (dirty || GKeysChanged) && !broken && !takenOver;
        ShowSlots();
        ShowViews();
        picture.Display(known ? current : null, known ? bar : null, brightness);
        bool liveEffect = screening && !sampling;
        if (liveEffect) directions.Visible = false;
        liveDirections.Visible = liveEffect;
        liveDirections.Enabled = hasDirection;
        liveDirections.Index = liveEffect && hasDirection ? Array.IndexOf(LiveEffects.Directions, view.Direction) : -1;
        liveDirections.Invalidate();
        foreach (var control in liveControls) control.Visible = liveEffect;
        ShowOption(livePause, liveEffect);
        // Paused() reads the battery from 0x1004 only, so it's greyed out on a wired keyboard.
        ShowOption(liveBattery, liveEffect);
        liveBattery.Enabled = found.Has(0x1004);
        ShowOption(solidToggle, liveEffect && effect != null && effect.OffersSolid);
        solidToggle.Left = colors.Right + Scaled(12);
        ((Control)solidToggle.Tag).Left = solidToggle.Left + Scaled(40);
        livePause.Checked = setup.PauseWhenLocked;
        liveBattery.Checked = setup.PauseOnBattery && found.Has(0x1004);
        solidToggle.Checked = view.Solid;
        if (screening)
        {
            effects.Index = effectIds.IndexOf(current.Effect);
            effects.Invalidate();
            lightHead.Text = "Lighting";
            saveButton.Enabled = false;
            if (sampling)
            {
                foreach (var control in plainLight) control.Visible = false;
                SyncScreen();
            }
        }
        loading = false;
    }

    // Without idle settings, the Lighting box takes the whole width, back to its half while the Screen effect shows its own box.
    // Sizes come from the picture box above, which is already scaled to the screen's DPI.
    void Widen(bool on)
    {
        if (on == wide) return;
        wide = on;
        int gap = lightBox.Top - pictureBox.Bottom;
        int extra = pictureBox.Right - idleArea.Left + gap;
        int change = on ? extra : -extra, half = on ? extra / 2 : -(extra / 2);
        lightBox.Width += change;
        directions.Width += change;
        bright.Width += half;
        brightValue.Width += half;
        if (intensity != null) { intensity.Width += half; intensityValue.Width += half; }
        liveDirections.Width += change;
        // "Center out" fits only the wide box's cells; the narrow one says Out and In.
        liveDirections.Items = on ? LiveEffects.DirectionNames : LiveEffects.ShortDirectionNames;
        var rightHalf = new List<Control> { speedHead, speedValue, speed, livePause, (Control)livePause.Tag };
        rightHalf.AddRange(liveControls);
        foreach (Control control in rightHalf) control.Left += half;
        // Low battery beside Screen off or locked where there's room, below it in the narrow box.
        var batteryLabel = (Control)liveBattery.Tag;
        liveBattery.Left = livePause.Left + (on ? Scaled(170) : 0);
        liveBattery.Top = livePause.Top + (on ? 0 : Scaled(21));
        batteryLabel.Left = liveBattery.Left + Scaled(40);
        batteryLabel.Top = liveBattery.Top - Scaled(1);
        speed.Width += half;
        speedValue.Width += half;
    }

    // Layout units in the page's current pixels: the page is built at 100% and scaled as a whole once placed (MainWindow.Place),
    // which may come after RefreshControls, so the picture box's width tells the scale, not Ui.DpiScale.
    int Scaled(int units) { return (int)Math.Round(units * pictureBox.Width / 616f); }

    void ShowView(int shown)
    {
        openView = shown;
        if (buttonRows != null) buttonRows.Recording = -1;
        RefreshControls();
    }

    void ShowViews()
    {
        if (viewPick == null) return;
        viewPick.Visible = gKeys != null || views.Contains(GameView);
        viewPick.Index = views.IndexOf(openView);
        viewPick.Off = gKeys == null && views.Contains(ButtonsView) ? new[] { views.IndexOf(ButtonsView) } : new int[0];
        viewPick.Invalidate();
        if (buttonsBox != null)
        {
            buttonsBox.Visible = openView == ButtonsView;
            // The Screen effect stops Save, and its box is hidden here: the G keys wait until it's off.
            bool screening = Screening;
            buttonRows.Show(kind.GKeys, gKeys, !broken && !screening,
                screening ? "Turn off the " + EffectName + " effect to change the G keys."
                    : kind.GKeysExperimental ? "Experimental: changes take effect once saved to the keyboard."
                    : "Changes take effect once saved to the keyboard.");
        }
        if (gameBox != null)
        {
            gameBox.Visible = openView == GameView;
            gameBox.ShowKeys(gameKeys);
        }
    }

    void SetGameKeys(IEnumerable<int> keys)
    {
        var list = GameModeKeys.Clean(keys);
        if (!gameMode || list.SequenceEqual(gameKeys)) return;
        gameKeys = list;
        var text = GameModeKeys.ToText(list);
        if (text == "") Settings.Remove(GameKeysSetting); else Settings.Set(GameKeysSetting, text);
        LogUserAction(text == "" ? "Game mode keys cleared" : "Game mode keys " + string.Join(" ", list.Select(usage => usage.ToString("X2"))));
        RunOnDevice(hidpp => GameModeKeys.Send(hidpp, list));
        Edited();
        gameBox.ShowKeys(gameKeys);
    }

    // The G keys aren't in presets, so Revert with a preset picked brings them back too.
    protected override bool UnsavedBesidesPreset { get { return GKeysChanged; } }

    protected override void RevertBesidesPreset() { DropGKeyEdits(); RefreshControls(); ClearStatus(); }

    void DropGKeyEdits()
    {
        if (buttonRows != null) buttonRows.Recording = -1;
        gKeys = Bindings.Copy(storedGKeys);
        gKeyEdits++;
    }

    internal void RecordedKeys(Keys keys) { if (buttonRows != null) buttonRows.Recorded(keys); }

    void ShowSlots()
    {
        if (slotPicker == null) return;
        slotPicker.Items = slots.Select(sector => "M" + sector).ToArray();
        slotPicker.Visible = slots.Count > 1;
        FooterPickerShown = slotPicker.Visible;
        slotPicker.Index = slots.IndexOf(running);
        slotPicker.Invalidate();
        saveButton.Text = slotPicker.Visible && slots.Contains(running) ? "Save to M" + running : "Save to keyboard";
        saveButton.Invalidate();
    }

    // 0x8100 event fn 0 is a new running profile on the mice (MousePage.Heard); 0x8040 event fn 0 a new brightness, from the
    // keyboard's brightness key. The G815 sends both (2026-11-20); the window's activation checks too (MainWindow), for an event missed
    // while G-Lite was closed or a keyboard that sends none.
    public void Heard(byte feature, int fn)
    {
        if (fn != 0) return;
        if (profilesFeature != 0 && feature == profilesFeature) CheckSlot();
        if (brightnessFeature != 0 && feature == brightnessFeature) CheckBrightness();
    }

    // On activation. An asleep keyboard takes about 3 s per read, which would hold edits made right after.
    public void CheckKeyboard()
    {
        if (found.Asleep) return;
        CheckSlot();
        CheckBrightness();
    }

    void CheckBrightness()
    {
        if (brightnessFeature == 0 || checkingBrightness) return;
        checkingBrightness = true;
        int level = -1, startedAt = previewSequence;
        RunOnDevice(hidpp => level = G515Profile.Brightness(hidpp), () =>
        {
            checkingBrightness = false;
            if (level < 0 || level > 100 || level == brightness || startedAt != previewSequence || debounce.Enabled) return;
            Log.Add(DeviceLabel, "found", "brightness " + level + "% set on the keyboard");
            brightness = level;
            storedBrightness = level;
            if (!Screening && Settings.Get(Remembered) != null) Settings.Set(Remembered, current.ToText(level) + BarText(level));
            Edited();
            RefreshControls();
        }, message => { checkingBrightness = false; });
    }

    void CheckSlot()
    {
        if (slotPicker == null || (slots.Count < 2 && !takenOver) || checkingSlot) return;
        checkingSlot = true;
        int now = 0;
        bool host = false;
        RunOnDevice(hidpp =>
        {
            byte feature = hidpp.FeatureIndex(0x8100);
            var reply = hidpp.CallUnlogged(feature, 4);
            now = reply[0] << 8 | reply[1];
            if (now == 0) host = hidpp.CallUnlogged(feature, 2)[0] != 1;
        }, () =>
        {
            checkingSlot = false;
            if (host && !takenOver)
            {
                Log.Add(DeviceLabel, "found", "the keyboard left its onboard profiles");
                takenOver = true;
                slots = new List<int>();
                RefreshControls();
                ClearStatus();
                return;
            }
            if (now != 0 && takenOver)
            {
                Log.Add(DeviceLabel, "found", "the keyboard runs its onboard profiles again");
                takenOver = false;
                RunOnDevice(SendGameKeys);
                // Back on another profile than before is an M-key switch: the live lighting and the preset go.
                if (running != 0 && now != running) SlotSwitched(now, false); else Load();
                return;
            }
            if (now == 0 || now == running) return;
            Log.Add(DeviceLabel, "found", "M" + now + " picked on the keyboard");
            SlotSwitched(now, false);
        }, message => { checkingSlot = false; });
    }

    // Another onboard profile runs, picked here or with an M key. The keyboard shows that profile's saved lighting, so a live lighting
    // not saved goes, as it does on the keyboard alone, and the preset goes back to – (docs/G815-plan.md, step 2). Save is off until
    // the new slot is read, or it would write the old slot's lighting into it.
    void SlotSwitched(int sector, bool picked, bool again = false)
    {
        if (sector == running && !again) return;
        if (picked) LogUserAction("Switch to M" + sector);
        running = sector;
        // A live effect or the Screen effect stops too: the keyboard shows the new profile's lighting (the owner's choice, 2026-11-21).
        if (Screening)
        {
            ScreenOff();
            current = stored != null ? stored.Clone() : new Lighting();
            liveSettings = false;
        }
        DropPreset();
        Settings.Remove(Remembered);
        bar = null;
        part = 0;
        dirty = false;
        edits++;
        DropGKeyEdits();
        previewSequence++;
        debounce.Stop();
        RefreshControls();
        RunOnDevice(hidpp =>
        {
            G915Lighting.Release(hidpp);
            claimed = false;
            if (picked) hidpp.Call(hidpp.FeatureIndex(0x8100), 3, (byte)(sector >> 8), (byte)sector);
        });
        Load();
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
        if (takenOver)
            ShowError("G HUB or another app has taken over the keyboard. Close it to save.");
        else
        if (DynamicLightingOn())
            ShowStatus("Windows Dynamic Lighting is on and may override this.", true,
                () => Process.Start("ms-settings:personalization-lighting"), "Open Windows settings");
        else if (Screening)
        {
            var why = Paused();
            if (why != null) ShowStatus(why, true);
            else if (!Sampling && animationProblem != null) ShowStatus(animationProblem, true);
            else if (Sampling && ScreenCapture.Missing(setup.Monitor))
                ShowStatus("The chosen monitor isn't connected. Using the main monitor.", true);
            else ShowStatus(GKeysChanged ? "Live only. G keys not saved: turn off the " + EffectName + " effect to save them." : "Live only.");
        }
        else if (live && kind.Profile)
            ShowStatus(dirty ? "Set by G-Lite, not saved to the keyboard"
                : GKeysChanged ? "G keys not saved to " + (slots.Contains(running) && slots.Count > 1 ? "M" + running : "the keyboard")
                : Known() ? "Matches the keyboard" : "");
        else if (live) ShowStatus(Known() ? "Set by G-Lite, not saved to the keyboard" : "");
        else ShowStatus(dirty ? "Not saved to the keyboard" : stored == null ? "" : "Matches the keyboard");
    }

    public static int TestDynamicLighting = -1;

    public static bool DynamicLightingOn()
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
            if (!Sampling) Settings.Set(Active, ActiveText);
            int screenLevel = brightness;
            previewSequence++;
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
        // Only G keys changed: the lighting stays as saved, which also covers one G-Lite can't read.
        bool lightingToo = dirty;
        var keys = GKeysChanged ? Bindings.Copy(gKeys) : null;
        var keysWere = Bindings.Copy(storedGKeys);
        int keysSector = gKeysSector, editsAtStart = edits, keyEditsAtStart = gKeyEdits;
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
            var built = lightingToo ? G915Lighting.Build(profile, clusters, lighting, others, !kind.NoIdleTimes) : profile;
            if (keys != null && sector != keysSector)
                throw new RefusedException("The keyboard switched profiles: read it again before saving the G keys");
            if (keys != null) built = G915Lighting.WithButtons(built, keys, keysWere);
            Backups.Save(ProfileBackups, new Dictionary<string, byte[]> { { sector.ToString("X4"), profile } }, DeviceLabel);
            G915Lighting.Store(hidpp, sector, built, kind.ReloadThroughHostMode);
            claimed = false;
        }, () =>
        {
            if (lightingToo)
            {
                Settings.Remove(Remembered);
                stored = lighting;
                unknownStored = false;
            }
            if (keys != null) storedGKeys = keys;
            if (keys != null && keyEditsAtStart == gKeyEdits) gKeys = Bindings.Copy(keys);
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
        BackupMenu(backupsButton, ProfileBackups, kind.GKeys != null ? "Restore the factory profile" : "Restore the factory lighting",
            () => RestoreProfile(null),
            entry =>
            {
                if (BackupSector(entry) < 0) return "damaged, can't be restored";
                var lighting = G915Lighting.Decode(entry.Data.Values.First(), clusters == null ? 1 : G915Lighting.Keys(clusters));
                return lighting == null ? "an effect G-Lite doesn't know" : G515Profile.EffectById(lighting.Effect).Name;
            },
            entry => BackupSector(entry) > 0, RestoreProfile);
    }

    // A backup holds the whole profile, so it brings back its G keys with its lighting. A null backup restores the factory lighting,
    // and the factory G keys on a keyboard whose G keys G-Lite edits; the rest of the profile (M1-M3, G-shift) stays.
    void RestoreProfile(Backups.Entry entry)
    {
        int sector = entry == null ? -1 : BackupSector(entry);
        var backup = entry == null ? null : entry.Data.Values.First();
        int keyCount = kind.GKeys == null ? 0 : kind.GKeys.Length;
        string what = entry != null ? "the backup from " + When(entry) : keyCount > 0 ? "the factory profile" : "the factory lighting";
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
            if (backup == null && keyCount > 0) profile = G915Lighting.FactoryButtons(hidpp, now, profile, keyCount);
            Backups.Save(ProfileBackups, new Dictionary<string, byte[]> { { now.ToString("X4"), current } }, DeviceLabel);
            G915Lighting.Store(hidpp, now, profile, kind.ReloadThroughHostMode);
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
        DropGKeyEdits();
        SetGameKeys(new int[0]);
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
        var slotsRead = new List<int>();
        int runningRead = 0, keyEditsAtStart = gKeyEdits;
        bool hostRead = false;
        byte[][] keysRead = null;
        RunOnDevice(hidpp =>
        {
            if (readLayout) layoutCode = hidpp.Call(hidpp.FeatureIndex(0x4540), 0)[0];
            bool vertical;
            clusters = G915Lighting.Clusters(hidpp, out vertical);
            verticalWave = vertical ? 1 : 0;
            if (slotPicker != null)
                try
                {
                    bool onboard;
                    slotsRead = G915Lighting.Slots(hidpp, out runningRead, out onboard);
                    profilesFeature = hidpp.FeatureIndex(0x8100);
                    hostRead = !onboard;
                }
                catch (Exception) { slotsRead = new List<int>(); }
            brightnessFeature = hidpp.FeatureIndex(0x8040);
            // The saved lighting only when nothing is remembered, since a remembered lighting replaces it; the G keys always. Optional,
            // so any failure leaves the screen on live lighting as before.
            if (kind.Profile && (lighting == null || kind.GKeys != null))
                try
                {
                    int sector, buttons;
                    var profile = G915Lighting.ReadProfile(hidpp, out sector, out buttons);
                    if (lighting == null) saved = G915Lighting.Decode(profile, G915Lighting.Keys(clusters), kind.Intensity);
                    if (kind.GKeys != null && (!kind.GKeysExperimental || buttons == kind.GKeys.Length + 3))
                        keysRead = G915Lighting.ReadButtons(profile, kind.GKeys.Length);
                }
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
            slots = slotsRead;
            running = runningRead;
            takenOver = hostRead;
            // Edits made during the read are kept, unless the keys are another slot's now.
            bool sameSlot = keysRead != null && runningRead == gKeysSector;
            storedGKeys = keysRead;
            if (keyEditsAtStart == gKeyEdits || gKeys == null || !sameSlot)
            {
                if (buttonRows != null) buttonRows.Recording = -1;
                gKeys = Bindings.Copy(keysRead);
            }
            gKeysSector = runningRead;
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
            ApplyWaveDirections();
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
        bool readWave = verticalWave < 0 && !kind.VerticalWave && found.Has(0x8071);
        RunOnDevice(hidpp =>
        {
            if (readLayout) layoutCode = hidpp.Call(hidpp.FeatureIndex(0x4540), 0)[0];
            if (readWave)
                try { bool vertical; G915Lighting.Clusters(hidpp, out vertical); verticalWave = vertical ? 1 : 0; }
                catch (NotSupportedException) { verticalWave = 0; }
            brightnessFeature = hidpp.FeatureIndex(0x8040);
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
            ApplyWaveDirections();
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
