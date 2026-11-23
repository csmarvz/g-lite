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
    ScreenSettings setup;
    readonly Timer debounce = new Timer { Interval = 120 }, screenTimer = new Timer { Interval = 100 },
        snapTimer = new Timer { Interval = 1000 }, frameTimer = new Timer { Interval = 50 };
    readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
    readonly KeyPicture picture;
    readonly KeyLayout iso;
    bool isoShown;
    volatile bool wholeAgain;
    // One colour only (ZoneLighting.Zones): no colour row, and every light goes out white, so brightness alone sets the level.
    volatile bool monochrome;
    // The picture's ids are zones ("kind": "zones", the G213) or lighting ids, every key then in zone 0 (the G413).
    readonly bool zonesModel;
    readonly RoundedBox pictureBox;
    readonly Label effectHead;
    // One zone: the zone row is hidden and what's below it moved up, once its height in layout units.
    bool zoneRowHidden;
    const int ZoneRowHeight = 58;
    // Game mode keys through 0x4522, as on KeyboardPage (GameModeKeys): with a lighting-id picture. Replaced, never changed in place,
    // since Opened reads it on the queue.
    readonly bool gameMode;
    readonly GameModeBox gameBox;
    readonly SegmentedPicker viewPick;
    bool gameShown;
    volatile List<int> gameKeys = new List<int>();
    string GameKeysSetting { get { return "gamemode." + found.SettingsKey; } }
    // The Audio effect (ZoneLighting.Audio): the capture, opened again after it fails, and the level, rising at once and falling at
    // 1.6 a second, sent in steps of 2% when it moves and every 2 s.
    readonly Timer audioTimer = new Timer { Interval = 50 };
    AudioLoopback audio;
    DateTime audioRetry, audioRefresh;
    double audioLevel, audioClock;
    int audioSent = -1;
    string audioProblem;
    // A keyboard's effects go in a dropdown (effectList) holding only those the picked zones list (shownEffects, out of effectIds).
    // Per-key colours through 0x8080 (the G512 and G513, experimental) add G-Lite's live effects (LiveEffects), drawn key by key every
    // 40 ms like on KeyboardPage, with the keyboard's own effect set to Off first, since a running 0x8070 effect would draw over them
    // (OpenLogi). Intensity and direction stay at each effect's default.
    readonly bool perKey;
    readonly Dropdown effectList;
    readonly List<byte> effectIds = new List<byte>(), shownEffects = new List<byte>();
    readonly Timer liveTimer = new Timer { Interval = 40 };
    LiveEffect animation;
    byte animationId;
    KeyField keyField;
    System.Diagnostics.Stopwatch liveClock;
    double lastLiveFrame;
    volatile Dictionary<int, int> sentKeys;
    volatile bool liveClaimed;
    DateTime nextLiveRefresh;
    string liveProblem;
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
        debounce.Tick += (s, e) => { debounce.Stop(); RefreshControls(); Send(); };
        setup = ZoneLighting.Screen(Settings.Get(Saved));

        var model = ZoneLighting.Picture(found);
        if (model != null)
        {
            pictureBox = Ui.Add(this, new RoundedBox(), 20, 118, 616, 209);
            Ui.Text(pictureBox, "Live on the keyboard", 14, 12, () => Theme.Dim, 9f, Ui.Semibold);
            var ansi = KeyLayout.Load(model);
            zonesModel = ansi.Zones;
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
        effectHead = Ui.Text(light, "Effect", 14, 98, () => Theme.Dim, 9f);
        // After the effects 0x8070 knows, those G-Lite draws: Screen on the speakers; on a keyboard Audio, or the live effects with 0x8080.
        perKey = keyboard && picture != null && !zonesModel && found.Has(0x8080);
        effects = Ui.Add(light,
            new SegmentedPicker
            {
                Items = ZoneLighting.Effects.Select(effect => effect.Name).Concat(new[] { "Screen" }).ToArray(),
                AccessibleName = "Effect", Visible = !keyboard
            },
            14, 116, 440, 32);
        if (keyboard)
        {
            effectIds.AddRange(ZoneLighting.Effects.Select(effect => effect.Id));
            if (perKey) effectIds.AddRange(LiveEffects.All.Select(effect => effect.Id));
            else effectIds.Add(ZoneLighting.Audio.Id);
            effectList = Ui.Add(light, new Dropdown { AccessibleName = "Effect" }, 14, 116, 196, 32);
            effectList.Badge = i => Live(shownEffects[i]) ? "Live" : null;
            effectList.Picked += i =>
            {
                byte id = shownEffects[i];
                var info = InfoOf(id);
                // A live effect's period means something else (Rain's fall, Aurora's 20 s turn), so crossing between a live effect and
                // the keyboard's own starts at the new one's default, as on KeyboardPage.
                Edit(edited =>
                {
                    bool crossed = Live(edited.Effect) != Live(id);
                    edited.Effect = id;
                    // Without a speed, 0, so the next effect with one starts at its default too.
                    if (crossed) edited.Period = info.Speed ? info.DefaultPeriod : 0;
                    if (info.Speed && (edited.Period < info.MinPeriod || edited.Period > info.MaxPeriod)) edited.Period = info.DefaultPeriod;
                    if (Live(id)) edited.Direction = LiveEffects.DefaultDirection(id);
                }, true);
            };
        }
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
        bright.MouseUp += (s, e) => RefreshControls();
        speedHead = Ui.Text(light, "Speed", 194, 214, () => Theme.Dim, 9f);
        speedValue = Value(light, 194, 214, 172);
        speed = Ui.Add(light, new Slider { AccessibleName = "Speed" }, 194, 234, 172, 24);
        speed.Changed += (s, e) =>
        {
            var effect = InfoOf(Displayed().Effect);
            int period = SpeedScale.Period(speed.Value, effect.MinPeriod, effect.MaxPeriod);
            Edit(edited => edited.Period = period, false);
        };
        speed.MouseUp += (s, e) => RefreshControls();
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
        Ui.Text(settingsBox, "Intensity", 14, 126, () => Theme.Dim, 9f);
        boostValue = Value(settingsBox, 14, 126, 218);
        boost = Ui.Add(settingsBox, new Slider { AccessibleName = "Intensity" }, 14, 144, 218, 24);
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

        gameMode = keyboard && picture != null && !zonesModel && found.Has(0x4522);
        if (gameMode)
        {
            gameKeys = GameModeKeys.FromText(Settings.Get(GameKeysSetting), false);
            viewPick = Ui.Add(this, new SegmentedPicker { Items = new[] { "Settings", "Game" }, AccessibleName = "View" }, 440, 72, 196, 32);
            viewPick.Picked += index => { gameShown = index == 1; RefreshControls(); };
            // Tried on the G413 (2026-11-22); the G512 and G513 aren't.
            string gameText = "Select the keys you want to disable while gaming (Fn+F8 turns Game mode on). Changes apply "
                + "immediately and can be saved in your G-Lite presets, but not in the keyboard's memory.";
            if (!found.ShortName.ToUpper().StartsWith("G413")) gameText = "Experimental, not tried on this keyboard yet. " + gameText;
            gameBox = Ui.Add(this, new GameModeBox(KeyLayout.Load(model), gameText, false), 20, 118, 616, 543);
            gameBox.Picked += usage =>
            {
                var keys = gameKeys.ToList();
                if (!keys.Remove(usage)) keys.Add(usage);
                SetGameKeys(keys);
            };
            gameBox.Cleared += () => SetGameKeys(new int[0]);
        }
        audioTimer.Tick += (s, e) => AudioTick();
        liveTimer.Tick += (s, e) => LiveTick();

        screenTimer.Tick += (s, e) => Tick();
        if (picture != null)
        {
            keyZones = KeyZones();
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
            audioTimer.Dispose();
            if (audio != null) audio.Dispose();
            liveTimer.Dispose();
            if (animation != null) animation.Dispose();
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
        if (now) RefreshControls(); else Values();
        // While a slider moves, sent at most every 120 ms rather than once it stops: restarting the timer on each move sent nothing
        // during a drag.
        if (now) { debounce.Stop(); Send(); }
        else if (!debounce.Enabled) debounce.Start();
    }

    public override void Restyle() { RefreshControls(); }

    protected override string Current()
    {
        return (current == null ? "" : ZoneLighting.ToText(current)) + (screening ? "|" + setup.ToText() : "")
            + (gameMode ? "!" + GameModeKeys.ToText(gameKeys) : "");
    }

    protected override void ApplyPreset(string text)
    {
        string game;
        text = GameModeKeys.Split(text, out game);
        if (game != null) SetGameKeys(GameModeKeys.FromText(game, false));
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

    protected override void Lost()
    {
        zones = null;
        sent = new MouseLight[Count];
        framed = null;
        audioSent = -1;
        sentKeys = null;
        liveClaimed = false;
    }

    // On a per-key keyboard Audio is the per-key one (LiveEffects), which shares its id with ZoneLighting.Audio.
    EffectInfo InfoOf(byte id) { return perKey && LiveEffects.Is(id) ? LiveEffects.Info(id) : ZoneLighting.EffectById(id); }

    bool Live(byte id) { return perKey && LiveEffects.Is(id); }

    bool IsLive(MouseLight light) { return light != null && Live(light.Effect); }

    void LiveTick()
    {
        var listed = zones;
        var lights = current;
        if (IsDisposed || listed == null || lights == null || framing || !IsLive(lights[0])) return;
        var light = lights[0];
        if (animation == null || animationId != light.Effect)
        {
            StopLive();
            animation = LiveEffects.Make(light.Effect);
            animationId = light.Effect;
            liveClock = System.Diagnostics.Stopwatch.StartNew();
            lastLiveFrame = 0;
        }
        if (keyField == null) keyField = KeyField.From(picture);
        animation.Color = light.Color;
        animation.Period = light.Period;
        animation.Intensity = LiveEffects.DefaultIntensity;
        animation.Direction = light.Direction;
        animation.Solid = false;
        double seconds = liveClock.Elapsed.TotalSeconds, elapsed = Math.Min(0.25, seconds - lastLiveFrame);
        lastLiveFrame = seconds;
        var frameColors = new int[keyField.Count];
        animation.Frame(keyField, seconds, elapsed, frameColors);
        if (animation.Problem != liveProblem) { liveProblem = animation.Problem; ClearStatus(); }
        for (int i = 0; i < frameColors.Length; i++) frameColors[i] = Scale(frameColors[i], light.Bright / 100.0);
        ShowLiveFrame(frameColors);
        // The keys whose colour moved, all of them every 2 s, as on KeyboardPage.Animate.
        var lastSent = sentKeys;
        bool full = lastSent == null || DateTime.UtcNow >= nextLiveRefresh;
        if (full) nextLiveRefresh = DateTime.UtcNow.AddSeconds(2);
        if (lastSent == null) sentKeys = lastSent = new Dictionary<int, int>();
        var changes = new Dictionary<int, int>();
        for (int i = 0; i < keyField.Count; i++)
        {
            int id = keyField.Ids[i], color = frameColors[i], before;
            if (full || !lastSent.TryGetValue(id, out before) || ScreenCapture.Distance(before, color) > 9 || (color == 0 && before != 0))
                changes[id] = color;
        }
        if (changes.Count == 0) return;
        foreach (var change in changes) lastSent[change.Key] = change.Value;
        // The keyboard's effect is set to Off again with every full frame, unlogged after the first: a keyboard turned off and on
        // comes back running its own, as the G515 does (KeyboardPage).
        bool claim = !liveClaimed || full, firstClaim = !liveClaimed;
        framing = true;
        RunOnDevice(hidpp =>
        {
            try
            {
                if (claim)
                {
                    for (int zone = 0; zone < listed.Count && zone < Count; zone++)
                        ZoneLighting.SetLight(hidpp, zone, listed[zone], new MouseLight { Effect = 0x00 }, firstClaim);
                    sent = new MouseLight[Count];
                    liveClaimed = true;
                }
                ZoneLighting.SendKeys(hidpp, changes);
            }
            finally { framing = false; }
        }, null, message => { sentKeys = null; liveClaimed = false; });
    }

    void ShowLiveFrame(int[] frameColors)
    {
        if (picture == null || !Visible) return;
        var byId = new Dictionary<int, int>();
        for (int i = 0; i < keyField.Count; i++) byId[keyField.Ids[i]] = frameColors[i];
        var lit = new Color[picture.Count];
        for (int key = 0; key < picture.Count; key++)
        {
            int color;
            byId.TryGetValue(picture.IdOf(key), out color);
            int max = Math.Max(color >> 16 & 0xFF, Math.Max(color >> 8 & 0xFF, color & 0xFF));
            lit[key] = max == 0 ? Theme.KeyOff
                : Ui.Mix(Theme.KeyOff, Color.FromArgb((color >> 16 & 0xFF) * 255 / max, (color >> 8 & 0xFF) * 255 / max, (color & 0xFF) * 255 / max),
                    max / 255.0);
        }
        picture.ShowLive(lit, new bool[picture.Count]);
    }

    void StopLive()
    {
        if (animation != null) { animation.Dispose(); animation = null; }
        sentKeys = null;
        liveClaimed = false;
        if (liveProblem != null) { liveProblem = null; ClearStatus(); }
    }

    void SyncLive()
    {
        var lights = current;
        bool wanted = perKey && !screening && zones != null && lights != null && IsLive(lights[0]);
        if (wanted && !liveTimer.Enabled && !Manual) liveTimer.Start();
        if (wanted || !liveTimer.Enabled && animation == null) return;
        liveTimer.Stop();
        StopLive();
    }

    // The keyboard forgets its Game mode list when it loses power: sent again whenever a connection opens.
    protected override void Opened(Hidpp hidpp)
    {
        var keys = gameKeys;
        if (!gameMode || keys.Count == 0) return;
        try { GameModeKeys.Send(hidpp, keys, false); }
        catch (HidppError ex) { Log.Add(DeviceLabel, "error", "Game mode keys: " + ex.Message); }
    }

    protected override bool ReopensWhenFound { get { return gameMode && gameKeys.Count > 0; } }

    void SetGameKeys(IEnumerable<int> keys)
    {
        var list = GameModeKeys.Clean(keys, false);
        if (!gameMode || list.SequenceEqual(gameKeys)) return;
        gameKeys = list;
        var text = GameModeKeys.ToText(list);
        if (text == "") Settings.Remove(GameKeysSetting); else Settings.Set(GameKeysSetting, text);
        LogUserAction(text == "" ? "Game mode keys cleared" : "Game mode keys " + string.Join(" ", list.Select(usage => usage.ToString("X2"))));
        RunOnDevice(hidpp => GameModeKeys.Send(hidpp, list, false));
        Edited();
        gameBox.ShowKeys(gameKeys);
    }

    List<int> KeyZones()
    {
        return Enumerable.Range(0, picture.Count).Select(key => zonesModel ? picture.IdOf(key) : picture.IdOf(key) >= 0 ? 0 : -1).ToList();
    }

    void HideZoneRow()
    {
        zoneRowHidden = true;
        zoneHead.Visible = zonePicker.Visible = false;
        int shift = effectHead.Top - zoneHead.Top;
        foreach (Control control in light.Controls)
            if (control.Top > zoneHead.Top && control != zonePicker) control.Top -= shift;
        RefreshControls();
    }

    // The loudest of eight bands, 30% to 90% of AudioLoopback's range spread over the whole level.
    void AudioTick()
    {
        var listed = zones;
        var lights = current;
        if (IsDisposed || listed == null || lights == null || framing) return;
        if (audio == null)
        {
            if (DateTime.UtcNow < audioRetry) return;
            try { audio = new AudioLoopback(); ShowAudioProblem(null); }
            catch (Exception) { audioRetry = DateTime.UtcNow.AddSeconds(3); ShowAudioProblem("Audio can't hear the PC's sound."); return; }
        }
        var heard = audio.Bands(8);
        if (heard == null) { audio.Dispose(); audio = null; audioRetry = DateTime.UtcNow.AddSeconds(1); return; }
        double seconds = clock.Elapsed.TotalSeconds, elapsed = Math.Min(0.25, seconds - audioClock);
        audioClock = seconds;
        double target = Math.Max(0, Math.Min(1, (heard.Max() - 0.3) / 0.6));
        audioLevel = target > audioLevel ? target : Math.Max(target, audioLevel - elapsed * 1.6);
        int step = (int)Math.Round(audioLevel * 50);
        bool refresh = DateTime.UtcNow >= audioRefresh;
        if (step == audioSent && !refresh) return;
        if (refresh) audioRefresh = DateTime.UtcNow.AddSeconds(2);
        audioSent = step;
        double level = step / 50.0;
        bool white = monochrome;
        var frame = lights.ToArray();
        framing = true;
        RunOnDevice(hidpp =>
        {
            try
            {
                bool wholeAudio = frame[0] != null && frame[0].Effect == ZoneLighting.Audio.Id;
                for (int zone = 0; zone < frame.Length && zone < listed.Count; zone++)
                {
                    var zoneLight = frame[zone];
                    if (zoneLight == null || zoneLight.Effect != ZoneLighting.Audio.Id) continue;
                    // On the G213 zone 0 covers the areas, and an area write would end it (docs/G213.md).
                    if (zone > 0 && wholeAudio && ZoneLighting.IsArea(listed[zone][ZoneLighting.Place])) continue;
                    ZoneLighting.SetColor(hidpp, zone, listed[zone], Scale(white ? 0xFFFFFF : zoneLight.Color, level), zoneLight.Bright);
                }
            }
            finally { framing = false; }
        }, null, message => { audioSent = -1; });
    }

    static int Scale(int color, double factor)
    {
        return (int)((color >> 16 & 0xFF) * factor) << 16 | (int)((color >> 8 & 0xFF) * factor) << 8 | (int)((color & 0xFF) * factor);
    }

    void ShowAudioProblem(string problem)
    {
        if (problem == audioProblem) return;
        audioProblem = problem;
        ClearStatus();
    }

    void SyncAudio()
    {
        var lights = current;
        bool wanted = keyboard && !perKey && !screening && zones != null && lights != null
            && lights.Any(zoneLight => zoneLight != null && zoneLight.Effect == ZoneLighting.Audio.Id);
        if (wanted && !audioTimer.Enabled && !Manual) audioTimer.Start();
        if (wanted || !audioTimer.Enabled && audio == null) return;
        audioTimer.Stop();
        if (audio != null) { audio.Dispose(); audio = null; }
        audioSent = -1;
        audioLevel = 0;
        if (audioProblem != null) ShowAudioProblem(null);
    }

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
        var effect = known ? InfoOf(displayed.Effect) : null;
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
        if (effectList != null)
        {
            // Audio is sent as Static, so it's listed where Static is; the live effects cover the whole keyboard, so only on All.
            shownEffects.Clear();
            shownEffects.AddRange(offered == null ? effectIds
                : effectIds.Where(id => Live(id) ? picked == 0
                    : offered.Any(zone => zone.ContainsKey(id == ZoneLighting.Audio.Id ? 0x01 : id))));
            effectList.Items.Clear();
            effectList.Items.AddRange(shownEffects.Select(id => InfoOf(id).Name));
            effectList.Enabled = ready;
            effectList.Index = known ? shownEffects.IndexOf(displayed.Effect) : -1;
            effectList.Invalidate();
        }
        bool color = known && effect.Color && !monochrome, hasSpeed = known && effect.Speed, hasBrightness = known && displayed.Effect != 0;
        SetDimmed(brightHead, hasBrightness);
        bright.Enabled = hasBrightness;
        // Not while they're dragged (the refresh at each send runs then): Brightness at 0 is kept as 1. MouseUp refreshes them.
        if (!bright.Capture) bright.Value = known ? displayed.Bright : 100;
        brightValue.Text = hasBrightness ? displayed.Bright + "%" : "";
        bright.Invalidate();
        SetDimmed(speedHead, hasSpeed);
        speed.Enabled = hasSpeed;
        if (hasSpeed && !speed.Capture) speed.Value = SpeedScale.Percent(displayed.Period, effect.MinPeriod, effect.MaxPeriod);
        speedValue.Text = hasSpeed ? speed.Value + "%" : "";
        speed.Invalidate();
        foreach (var control in plainLight) control.Visible = !screening;
        if (!screening) colors.RefreshControls(known ? displayed.Color : 0xFFFFFF, color);
        bool wave = known && effect.Direction && !IsLive(displayed);
        // G HUB gives a one-colour keyboard a slider from black to white; G-Lite sends white, scaled by Brightness, which is the same.
        colorHead.Text = wave ? "Direction" : monochrome ? "Color: one only; Brightness works within the keyboard's own level (F7)" : "Color";
        SetDimmed(colorHead, color || wave);
        if (wave || monochrome) foreach (var control in light.Controls.OfType<Swatch>()) control.Visible = false;
        directions.Visible = !screening && wave;
        directions.Index = wave ? Array.IndexOf(ZoneLighting.Directions, displayed.Direction) : -1;
        directions.Invalidate();
        if (picture != null) ShowFrame();
        areas.Visible = settingsBox.Visible = screening && !gameShown;
        light.Visible = !gameShown;
        if (pictureBox != null) pictureBox.Visible = !gameShown;
        if (gameBox != null)
        {
            gameBox.Visible = gameShown;
            gameBox.ShowKeys(gameKeys);
            viewPick.Index = gameShown ? 1 : 0;
            viewPick.Invalidate();
        }
        light.Height = light.Width * ((screening ? 162 : picture != null ? 310 : 330) - (zoneRowHidden ? ZoneRowHeight : 0)) / 616;
        SyncAudio();
        SyncLive();
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

    // While a slider moves, only the values beside the sliders: the whole refresh redraws the key picture, which made the slider
    // stutter on a large screen. The rest follows at each send (the debounce tick).
    void Values()
    {
        if (bright.Enabled) brightValue.Text = Math.Max(1, bright.Value) + "%";
        if (speed.Enabled) speedValue.Text = speed.Value + "%";
    }

    void ShowFrame()
    {
        if (picture == null || !Visible) return;
        var listed = zones;
        if (!isoShown && iso != null && listed != null && KeyLayout.IsIso(layout))
        {
            isoShown = true;
            picture.SetModel(iso);
            keyZones = KeyZones();
            keyField = null;
            if (gameBox != null) gameBox.SetModel(iso);
        }
        // A live effect draws the picture itself (ShowLiveFrame).
        if (current != null && IsLive(current[0]) && !screening) return;
        double seconds = clock.Elapsed.TotalSeconds;
        var lit = new Color[picture.Count];
        var marked = new bool[picture.Count];
        bool any = current != null && listed != null && !screening;
        for (int key = 0; key < picture.Count; key++)
        {
            int zone = keyZones[key];
            var zoneLight = LightOfZone(zone);
            if (monochrome && zoneLight != null && zoneLight.Color != 0xFFFFFF)
            {
                zoneLight = zoneLight.Clone();
                zoneLight.Color = 0xFFFFFF;
            }
            if (zoneLight != null && zoneLight.Effect == ZoneLighting.Audio.Id)
            {
                zoneLight = zoneLight.Clone();
                zoneLight.Effect = 0x01;
                zoneLight.Bright = Math.Max(1, (int)Math.Round(zoneLight.Bright * audioLevel));
                if (audioLevel <= 0) zoneLight.Effect = 0x00;
            }
            var color = any ? ZoneLighting.ColorAt(zoneLight, picture.XOf(key), seconds) : Color.Black;
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
        if (audioProblem != null) { ShowStatus(audioProblem, true); return; }
        if (liveProblem != null) { ShowStatus(liveProblem, true); return; }
        if (current != null && IsLive(current[0]))
        {
            ShowStatus("Experimental: G-Lite draws this effect key by key, not tried on this keyboard yet");
            return;
        }
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
            var zoneLight = want[zone];
            // Static scales its colour by brightness (MouseProtocol.Encode); the G413 ignores Breathe's intensity byte, so its white
            // is scaled here instead.
            int white = monochrome && zoneLight.Effect == 0x0A ? Scale(0xFFFFFF, zoneLight.Bright / 100.0) : 0xFFFFFF;
            if (monochrome && zoneLight.Color != white)
            {
                zoneLight = zoneLight.Clone();
                zoneLight.Color = white;
            }
            ZoneLighting.SetLight(hidpp, zone, listed[zone], zoneLight);
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
            bool oneColor;
            read = ZoneLighting.Zones(hidpp, out oneColor);
            monochrome = oneColor;
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
                if (segZones.Count <= 1 && !zoneRowHidden) HideZoneRow();
            }
            RefreshControls();
            Loaded();
        });
    }
}
