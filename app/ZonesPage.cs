using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using Timer = System.Windows.Forms.Timer;

// The G560 from the front, drawn from its photos: two round speakers, each with a light under its grille on the inner side (the
// front zone: bottom left, bottom right) and a glow behind it on the wall (the back zone: top left, top right). Zones 0 to 3 are
// bottom left, bottom right, top left and top right (an owner's check, docs/G560.md); a click on a light picks its zone.
class SpeakerPicture : DrawnControl
{
    Color[] colors = new Color[4];
    int selected = -1;
    bool pickable;
    public event Action<int> Picked;

    public SpeakerPicture()
    {
        TabStop = false;
        AccessibleRole = AccessibleRole.Graphic;
        AccessibleName = "Speakers";
    }

    // pick: the zone to mark, -1 for none (All); canPick: whether a click picks a zone now.
    public void Display(Color[] zoneColors, int pick, bool canPick)
    {
        colors = zoneColors;
        selected = pick;
        pickable = canPick;
        Cursor = canPick ? Cursors.Hand : Cursors.Default;
        Invalidate();
    }

    float Radius { get { return (Height - 24 * DpiScale) * 0.36f; } }

    PointF Center(int speaker)
    {
        float radius = Radius, gap = radius * 1.6f;
        return new PointF(Width / 2f + (speaker == 0 ? -1 : 1) * (gap + radius), Height / 2f + radius * 0.18f);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var graphics = Begin(e);
        float radius = Radius;
        if (radius <= 1) return;
        for (int speaker = 0; speaker < 2; speaker++)
        {
            var center = Center(speaker);
            Color back = colors[2 + speaker], front = colors[speaker];
            // The glow behind, up the wall, kept inside the picture.
            float reach = Math.Min(center.Y, Height - center.Y) - 1;
            using (var path = new GraphicsPath())
            {
                path.AddEllipse(center.X - radius * 1.7f, center.Y - reach, radius * 3.4f, reach * 2);
                using (var brush = new PathGradientBrush(path))
                {
                    brush.CenterPoint = new PointF(center.X, center.Y - radius * 0.3f);
                    brush.CenterColor = Color.FromArgb(back == Theme.KeyOff ? 0 : 150, back);
                    brush.SurroundColors = new[] { Color.FromArgb(0, back) };
                    graphics.FillPath(brush, path);
                }
            }
            // The housing's curve on the inner side, lit along its bottom.
            float inner = speaker == 0 ? 1 : -1;
            var housing = new RectangleF(center.X - radius * 1.08f + inner * radius * 0.1f, center.Y - radius * 1.02f, radius * 2.16f, radius * 2.12f);
            Ui.Fill(graphics, housing, radius * 1.08f, Color.FromArgb(28, 28, 30), Color.FromArgb(28, 28, 30));
            using (var pen = new Pen(front, radius * 0.11f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                graphics.DrawArc(pen, housing.X + radius * 0.05f, housing.Y + radius * 0.05f, housing.Width - radius * 0.1f,
                    housing.Height - radius * 0.1f, speaker == 0 ? -10 : 90, 100);
            Ui.Fill(graphics, new RectangleF(center.X - radius, center.Y - radius, radius * 2, radius * 2), radius,
                Theme.KeyOff, Theme.Border);
            // The picked zone: a mark under its speaker (front) or above it (back).
            if (selected == speaker || selected == 2 + speaker)
            {
                float markY = selected == speaker ? center.Y + radius * 1.02f + 6 * DpiScale : center.Y - radius * 1.02f - 9 * DpiScale;
                using (var pen = new Pen(Theme.Accent, 2 * DpiScale))
                using (var mark = Ui.Round(new RectangleF(center.X - radius * 0.5f, markY, radius, 3 * DpiScale), 1.5f * DpiScale))
                    graphics.DrawPath(pen, mark);
            }
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (!pickable || e.Button != MouseButtons.Left || Picked == null) return;
        int speaker = e.X < Width / 2 ? 0 : 1;
        Picked(e.Y > Center(speaker).Y ? speaker : 2 + speaker);
    }
}

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
    // The G560's, in the keyboards' picture place.
    readonly SpeakerPicture speakers;
    readonly KeyLayout iso;
    bool isoShown;
    volatile bool wholeAgain;
    // One colour only (ZoneLighting.Zones): no colour row, and every light goes out white, so brightness alone sets the level.
    volatile bool monochrome;
    // The picture's ids are zones ("kind": "zones", the G213) or lighting ids, every key then in zone 0 (the G413).
    readonly bool zonesModel;
    // Game mode keys through 0x4522, as on KeyboardPage (GameModeKeys): with a lighting-id picture. Replaced, never changed in place,
    // since Opened reads it on the queue. The Game view picks them on a picture in the sticky picture's place.
    readonly bool gameMode;
    readonly SegmentedPicker viewPick;
    bool gameShown;
    volatile List<int> gameKeys = new List<int>();
    string GameKeysSetting { get { return "gamemode." + found.SettingsKey; } }
    readonly KeyPicture gamePicture;
    readonly RoundedBox gameBox;
    readonly Label gameHeading, gameKeysCount;
    readonly FlatButton gameClear;
    readonly int gameHeight;
    // The Audio effect (ZoneLighting.Audio): the capture, opened again after it fails, and the level, rising at once and falling at
    // 1.6 a second, sent in steps of 2% when it moves and every 2 s.
    readonly Timer audioTimer = new Timer { Interval = 50 };
    AudioLoopback audio;
    DateTime audioRetry, audioRefresh;
    double audioLevel, audioClock;
    int audioSent = -1;
    string audioProblem;
    // The effects go in a dropdown holding only those the picked zones list (shownEffects, out of effectIds), then Screen on the
    // speakers, Audio on a keyboard. Per-key colours through 0x8080 on a keyboard with a lighting-id picture (the G512, G513, G PRO, G410, G810 and G910, experimental) give G-Lite's live effects
    // (LiveEffects) instead of Audio, drawn key by key every 40 ms like on KeyboardPage, with the keyboard's own effect set to Off
    // first, since a running 0x8070 effect would draw over them (OpenLogi). Intensity and direction stay at each effect's default.
    // Screen and the live effects cover every zone, so they're listed on All only.
    readonly bool perKey;
    readonly List<byte> effectIds = new List<byte>(), shownEffects = new List<byte>();
    const byte ScreenEffect = 0xFE;
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

    readonly Label speedValue, brightValue, boostValue, rateValue;
    readonly SegmentedPicker zonePicker, directions;
    readonly Dropdown effects, monitorDropdown;
    readonly ColorRow colors;
    readonly Slider speed, bright, boost, rate;
    readonly ScreenPreview preview;
    readonly Toggle smoothToggle, lockToggle;
    // Lighting's rows in design units, those a device may not have (no zones to pick, no colour, no Wave, the Screen effect's) left
    // out through rows; then Saving's note.
    readonly RowLayout rows;
    readonly RoundedBox lighting;
    readonly int zoneRow, screenRow, monitorRow, boostRow, rateRow, smoothRow, colorRow, directionRow, pauseRow;
    readonly RowText zoneText, effectText, colorText, directionText, brightText, speedText, pauseText;
    const int PictureHeight = 168, PickerWidth = 336, ZonesHeight = 176, NotePadding = 14;

    string Remembered { get { return "lighting." + found.SettingsKey; } }
    string Saved { get { return "screen." + found.SettingsKey; } }
    string Active { get { return "screen.on." + found.SettingsKey; } }

    public ZonesPage(FoundDevice found, Hidpp fake)
        : base(found, fake, found.ShortName, found.Kind == "keyboard" ? "keyboard" : "device",
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
        // The picture stays above the rows, which scroll under them; without one the rows start below the header.
        if (model != null)
        {
            var ansi = KeyLayout.Load(model);
            zonesModel = ansi.Zones;
            iso = ansi.Iso();
            picture = Ui.Add(this, new KeyPicture(ansi) { Live = true }, 20, BodyTop, Rows.Width, PictureHeight);
        }
        if (!keyboard) speakers = Ui.Add(this, new SpeakerPicture(), 20, BodyTop, Rows.Width, PictureHeight);
        var sheet = (picture != null || speakers != null ? UseBody(BodyTop + PictureHeight + 12) : UseBody()).Sheet;
        rows = new RowLayout(sheet);

        // Before the device is read every row it may have shows; once read, the zone row goes with one zone, the colour row on a
        // one-colour keyboard, Direction without Wave. The Screen effect's rows follow Effect on the speakers while it runs.
        Label heading;
        lighting = rows.Section("Lighting", out heading);
        zoneRow = rows.Row(lighting);
        zoneText = Rows.Row(lighting, zoneRow, "Zone", "");
        zonePicker = Rows.Picker(lighting, zoneRow, "Zone", keyboard ? new[] { "All" } : new[] { "All" }.Concat(ZoneLighting.Names).ToArray(),
            ZonePickerWidth(keyboard ? 1 : Count + 1));
        zonePicker.Index = 0;
        zonePicker.Picked += index => { picked = index; RefreshControls(); };
        int effectRow = rows.Row(lighting);
        effectText = Rows.Row(lighting, effectRow, "Effect", "What the lighting does");
        effects = Rows.Dropdown(lighting, effectRow, "Effect");
        perKey = keyboard && picture != null && !zonesModel && found.Has(0x8080);
        effectIds.AddRange(ZoneLighting.Effects.Select(effect => effect.Id));
        if (!keyboard) effectIds.Add(ScreenEffect);
        else if (perKey) effectIds.AddRange(LiveEffects.All.Select(effect => effect.Id));
        else effectIds.Add(ZoneLighting.Audio.Id);
        effects.Badge = i => Live(shownEffects[i]) || shownEffects[i] == ScreenEffect ? "Live" : null;
        effects.Picked += i =>
        {
            byte id = shownEffects[i];
            if (id == ScreenEffect) { if (!screening) { ScreenOn(); Edited(); } return; }
            if (screening) ScreenOff();
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

        screenRow = rows.Row(lighting, ZonesHeight);
        Rows.Row(lighting, screenRow, "Zones", "");
        // The monitor takes the row's width up to Reset, down to its bottom; the speakers' four areas are fixed, one per zone.
        preview = Ui.Add(lighting, new ScreenPreview(), 130, screenRow + 14, Rows.Width - Rows.Inset - 66 - 12 - 130, 148);
        preview.Picked += index => { selectedZone = index; preview.Selected = index; preview.Invalidate(); };
        preview.Done += ScreenChanged;
        var resetAreas = Ui.Add(lighting, new FlatButton { Text = "Reset", AccessibleName = "Reset the screen areas" },
            Rows.Width - Rows.Inset - 66, screenRow + 132, 66, 30);
        resetAreas.Click += (s, e) => { setup.Zones = ZoneLighting.ScreenZones(); selectedZone = 0; ScreenChanged(); };
        monitorRow = rows.Row(lighting);
        Rows.Row(lighting, monitorRow, "Monitor", "The screen the lights follow");
        monitorDropdown = Rows.Dropdown(lighting, monitorRow, "Monitor");
        monitorDropdown.Picked += index =>
        {
            var all = Screen.AllScreens;
            if (index < 0 || index >= all.Length) return;
            setup.Monitor = all[index].Primary ? "" : all[index].DeviceName;
            ScreenChanged();
            Snap();
        };
        boostRow = rows.Row(lighting);
        Rows.Row(lighting, boostRow, "Color boost", "Stronger screen colors on the lights");
        boost = Rows.Slider(lighting, boostRow, "Color boost", out boostValue);
        boost.Changed += (s, e) => { setup.Boost = boost.Value; boostValue.Text = setup.Boost + "%"; ScreenChanged(); };
        rateRow = rows.Row(lighting);
        Rows.Row(lighting, rateRow, "Update rate", "How often the lights follow the screen");
        rate = Rows.Slider(lighting, rateRow, "Update rate", out rateValue);
        rate.Max = 9;
        rate.Step = 1;
        rate.Changed += (s, e) =>
        {
            setup.Interval = 1000 - rate.Value * 100;
            rateValue.Text = setup.Interval + " ms";
            screenTimer.Interval = setup.Interval;
            ScreenChanged();
        };
        smoothRow = rows.Row(lighting);
        Rows.Row(lighting, smoothRow, "Smooth changes", "Colors fade instead of jumping");
        smoothToggle = Rows.Toggle(lighting, smoothRow, "Smooth changes");
        smoothToggle.Changed += () => { setup.Smooth = smoothToggle.Checked; ScreenChanged(); };

        colorRow = rows.Row(lighting);
        colorText = Rows.Row(lighting, colorRow, "Color", "The effect's color");
        colors = new ColorRow(lighting, Rows.Width - Rows.Inset - ColorRow.Widest, colorRow + (Ui.RowHeight - ColorRow.Size) / 2, true);
        colors.Picked += color => Edit(edited => edited.Color = color, true);
        directionRow = rows.Row(lighting);
        directionText = Rows.Row(lighting, directionRow, "Direction", "Which way the effect moves");
        directions = Rows.Picker(lighting, directionRow, "Direction", ZoneLighting.DirText, PickerWidth);
        directions.Picked += index =>
        {
            int direction = ZoneLighting.Directions[index];
            Edit(edited => edited.Direction = direction, true);
        };
        // Shared with the Screen effect, which has its own level (setup.Bright), as on KeyboardPage.
        int brightRow = rows.Row(lighting);
        brightText = Rows.Row(lighting, brightRow, "Brightness", "");
        bright = Rows.Slider(lighting, brightRow, "Brightness", out brightValue);
        bright.Changed += (s, e) =>
        {
            int brightness = Math.Max(1, bright.Value);
            if (!screening) { Edit(edited => edited.Bright = brightness, false); return; }
            if (loading) return;
            setup.Bright = brightness;
            brightValue.Text = brightness + "%";
            ScreenChanged();
        };
        bright.MouseUp += (s, e) => RefreshControls();
        int speedRow = rows.Row(lighting);
        speedText = Rows.Row(lighting, speedRow, "Speed", "How fast the effect moves");
        speed = Rows.Slider(lighting, speedRow, "Speed", out speedValue);
        speed.Changed += (s, e) =>
        {
            var effect = InfoOf(Displayed().Effect);
            int period = SpeedScale.Period(speed.Value, effect.MinPeriod, effect.MaxPeriod);
            Edit(edited => edited.Period = period, false);
        };
        speed.MouseUp += (s, e) => RefreshControls();
        pauseRow = rows.Row(lighting);
        pauseText = Rows.Row(lighting, pauseRow, "Pause when screen off or locked", "For the Screen effect");
        lockToggle = Rows.Toggle(lighting, pauseRow, "Pause when screen off or locked");
        lockToggle.Changed += () => { setup.PauseWhenLocked = lockToggle.Checked; ScreenChanged(); };

        var saving = rows.Section("Saving", out heading);
        string noteText = "G-Lite can't save to " + (keyboard ? "this keyboard" : "these speakers")
            + " yet. It keeps your lighting and sets it again when G-Lite starts and when you open this screen.";
        int noteRow = rows.Row(saving, Rows.NoteHeight(noteText) + 2 * NotePadding);
        Rows.Note(saving, noteRow + NotePadding, noteText);

        gameMode = keyboard && picture != null && !zonesModel && found.Has(0x4522);
        if (gameMode)
        {
            gameKeys = GameModeKeys.FromText(Settings.Get(GameKeysSetting), false);
            viewPick = Ui.Add(this, new SegmentedPicker { Items = new[] { "Settings", "Game" }, AccessibleName = "View" }, 440, 72, 196, 32);
            viewPick.Picked += index => { gameShown = index == 1; RefreshControls(); };
            // Tried on the G413 (2026-11-22); the G512 and G513 aren't.
            string gameText = "Select the keys you want to disable while gaming (Fn+F8 turns Game mode on). Windows and Menu keys are "
                + "always disabled in Game mode. Changes apply immediately and can be saved in your G-Lite presets, but not in the "
                + "keyboard's memory.";
            if (!found.ShortName.ToUpper().StartsWith("G413")) gameText = "Experimental, not tried on this keyboard yet. " + gameText;
            gamePicture = Ui.Add(this, GameModeKeys.Picture(KeyLayout.Load(model), false), 20, BodyTop, Rows.Width, PictureHeight);
            gamePicture.Visible = false;
            gamePicture.IdPicked += id =>
            {
                var keys = gameKeys.ToList();
                int usage = GameModeKeys.Usage(id);
                if (!keys.Remove(usage)) keys.Add(usage);
                SetGameKeys(keys);
            };
            int gameTop = 0, noteHeight = Rows.NoteHeight(gameText) + 2 * NotePadding;
            gameBox = Rows.Section(sheet, "Game mode", ref gameTop, new RoundedBox(), noteHeight + Ui.RowHeight, out gameHeading);
            gameHeight = gameTop;
            Rows.Note(gameBox, NotePadding, gameText);
            gameKeysCount = Rows.Row(gameBox, noteHeight, "Keys turned off", "").Hint;
            gameClear = Rows.Button(gameBox, noteHeight, "Clear", "Clear the Game mode keys");
            gameClear.Click += (s, e) => SetGameKeys(new int[0]);
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
        }
        if (speakers != null)
            speakers.Picked += zone =>
            {
                if (!zonePicker.Enabled) return;
                picked = segZones.IndexOf(zone) + 1;
                RefreshControls();
            };
        if (picture != null || speakers != null)
        {
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

    // The speakers' zone names need a wider picker than the keyboards' Keys and Logo, or Zone 1 to 5.
    int ZonePickerWidth(int items) { return !keyboard ? 440 : items <= 3 ? Rows.ControlWidth : PickerWidth; }

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
        ShowGameKeys();
    }

    void ShowGameKeys()
    {
        gamePicture.PickedIds = new HashSet<int>(gameKeys.Select(GameModeKeys.LightingId));
        gamePicture.Invalidate();
        gameKeysCount.Text = gameKeys.Count == 0 ? "Only Windows and Menu"
            : gameKeys.Count + (gameKeys.Count == 1 ? " key" : " keys") + ", besides Windows and Menu";
        gameClear.Enabled = gameKeys.Count > 0;
    }

    // On a lighting-id picture every key shows the zone at the keys (0x8070 location 1, else zone 0) but the logo, which has no id: it
    // shows the zone at the logo (location 2, the G512's second zone) once the keyboard is read.
    List<int> KeyZones()
    {
        var listed = zones;
        int logo = listed == null ? -1 : listed.FindIndex(zone => zone[ZoneLighting.Place] == 2),
            keys = listed == null ? 0 : Math.Max(0, listed.FindIndex(zone => zone[ZoneLighting.Place] == 1));
        return Enumerable.Range(0, picture.Count)
            .Select(key => zonesModel ? picture.IdOf(key) : picture.IdOf(key) >= 0 ? keys : picture.NameOf(key) == "Logo" ? logo : -1)
            .ToList();
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

    // Screen covers every zone, so it's on All.
    void ScreenOn()
    {
        screening = true;
        picked = 0;
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

        // Which rows there are first: a row shown again makes its controls visible, and the refresh below hides those it keeps hidden.
        rows.ShowRow(lighting, zoneRow, listed == null || segZones.Count > 1);
        foreach (int row in new[] { screenRow, monitorRow, boostRow, rateRow, smoothRow }) rows.ShowRow(lighting, row, !keyboard && screening);
        rows.ShowRow(lighting, pauseRow, !keyboard);
        rows.ShowRow(lighting, colorRow, !monochrome);
        rows.ShowRow(lighting, directionRow, listed == null
            || ZoneLighting.Effects.Any(info => info.Direction && listed.Any(zone => zone.ContainsKey(info.Id))));
        PlaceView();

        zonePicker.Enabled = ready && !screening;
        zoneText.Enable(ready && !screening);
        zoneText.Hint.Text = mixed && !screening ? "Zones differ" : picture != null ? "Click a key to pick its zone"
            : speakers != null ? "Click a light to pick it" : "The lights to set";
        zonePicker.Index = picked;
        zonePicker.Invalidate();
        // Audio is sent as Static, so it's listed where Static is; Screen and the live effects cover every zone, so only on All.
        var offered = !ready ? null : picked == 0 ? listed : listed.Skip(Zone).Take(1);
        shownEffects.Clear();
        shownEffects.AddRange(offered == null ? effectIds
            : effectIds.Where(id => Live(id) || id == ScreenEffect ? picked == 0
                : offered.Any(zone => zone.ContainsKey(id == ZoneLighting.Audio.Id ? 0x01 : id))));
        effects.Items.Clear();
        effects.Items.AddRange(shownEffects.Select(id => id == ScreenEffect ? "Screen" : InfoOf(id).Name));
        // Screen can be turned off before the speakers answer.
        effects.Enabled = ready || screening;
        effects.Index = screening ? shownEffects.IndexOf(ScreenEffect) : known ? shownEffects.IndexOf(displayed.Effect) : -1;
        effects.Invalidate();
        effectText.Enable(ready);
        effectText.Hint.Text = screening ? "The lights follow the screen"
            : known && IsLive(displayed) ? "Drawn by G-Lite, key by key"
            : known && displayed.Effect == ZoneLighting.Audio.Id ? "Follows the PC's sound"
            : known || !ready ? "What the lighting does"
            : keyboard ? "The keyboard's own lighting, until you pick one"
            : "The speakers' own lighting, until you pick one";

        bool color = known && effect.Color && !monochrome, hasSpeed = known && effect.Speed,
            hasBrightness = screening || known && displayed.Effect != 0;
        colorText.Enable(color);
        // Not on a one-colour keyboard, whose colour row is left out: it would show its custom swatch again.
        if (!monochrome) colors.RefreshControls(known ? displayed.Color : 0xFFFFFF, color);
        bool wave = known && effect.Direction && !IsLive(displayed);
        directionText.Enable(wave);
        directions.Enabled = wave;
        directions.Index = wave ? Array.IndexOf(ZoneLighting.Directions, displayed.Direction) : -1;
        directions.Invalidate();
        int level = screening ? setup.Bright : known ? displayed.Bright : 100;
        brightText.Enable(hasBrightness);
        // G HUB gives a one-colour keyboard a slider from black to white; G-Lite sends white, scaled by Brightness, which is the same.
        brightText.Hint.Text = monochrome ? "One color: within the keyboard's own level (F7)"
            : screening ? "How bright the screen's colors are" : "How bright the lights are";
        bright.Enabled = hasBrightness;
        // Not while they're dragged (the refresh at each send runs then): Brightness at 0 is kept as 1. MouseUp refreshes them.
        if (!bright.Capture) bright.Value = level;
        brightValue.Text = hasBrightness ? level + "%" : "";
        bright.Invalidate();
        speedText.Enable(hasSpeed);
        speed.Enabled = hasSpeed;
        if (hasSpeed && !speed.Capture) speed.Value = SpeedScale.Percent(displayed.Period, effect.MinPeriod, effect.MaxPeriod);
        speedValue.Text = hasSpeed ? speed.Value + "%" : "";
        speed.Invalidate();
        pauseText.Enable(screening);
        lockToggle.Enabled = screening;
        lockToggle.Checked = setup.PauseWhenLocked;
        if (picture != null || speakers != null) ShowFrame();
        if (gameMode)
        {
            ShowGameKeys();
            viewPick.Index = gameShown ? 1 : 0;
            viewPick.Invalidate();
        }
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
        boost.Value = setup.Boost;
        boostValue.Text = setup.Boost + "%";
        rate.Value = (1000 - setup.Interval) / 100;
        rateValue.Text = setup.Interval + " ms";
        smoothToggle.Checked = setup.Smooth;
        boost.Invalidate();
        rate.Invalidate();
        loading = false;
    }

    // Settings' sections with the rows this device has, or the Game view's.
    void PlaceView()
    {
        if (gameMode) gameHeading.Visible = gameBox.Visible = gamePicture.Visible = gameShown;
        if (picture != null) picture.Visible = !gameShown;
        int height = rows.Place(Scaled, !gameShown);
        FitBody(gameShown ? gameHeight : height);
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

    // A light's colour as the pictures draw it: at its brightness over an unlit key.
    static Color Shown(Color color)
    {
        int max = Math.Max(color.R, Math.Max(color.G, color.B));
        return max == 0 ? Theme.KeyOff
            : Ui.Mix(Theme.KeyOff, Color.FromArgb(color.R * 255 / max, color.G * 255 / max, color.B * 255 / max), max / 255.0);
    }

    // The speakers' four lights: what each zone runs, or the screen's colours while the Screen effect does.
    void ShowSpeakers()
    {
        var listed = zones;
        var sampled = framed;
        double seconds = clock.Elapsed.TotalSeconds;
        var lit = new Color[4];
        for (int zone = 0; zone < 4; zone++)
        {
            int frame = screening && sampled != null && zone < sampled.Length ? sampled[zone] : -1;
            lit[zone] = frame >= 0 ? Shown(Color.FromArgb(frame >> 16 & 0xFF, frame >> 8 & 0xFF, frame & 0xFF))
                : current != null && listed != null && !screening ? Shown(ZoneLighting.ColorAt(LightOfZone(zone), 0.5f, seconds))
                : Theme.KeyOff;
        }
        speakers.Display(lit, picked > 0 && zonePicker.Enabled ? Zone : -1, zonePicker.Enabled);
    }

    void ShowFrame()
    {
        if (!Visible) return;
        if (speakers != null) { ShowSpeakers(); return; }
        if (picture == null) return;
        var listed = zones;
        if (!isoShown && iso != null && listed != null && KeyLayout.IsIso(layout))
        {
            isoShown = true;
            picture.SetModel(iso);
            keyZones = KeyZones();
            keyField = null;
            if (gamePicture != null) gamePicture.SetModel(iso);
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
            if (picture != null) keyZones = KeyZones();
            if (keyboard)
            {
                var listed = read.Take(Count).ToList();
                bool areas = listed.Any(zone => ZoneLighting.IsArea(zone[ZoneLighting.Place]));
                // With areas (the G213), the whole-keyboard zone is what All sets.
                segZones = Enumerable.Range(0, listed.Count).Where(i => !(areas && listed[i][ZoneLighting.Place] == 1)).ToList();
                zonePicker.Items = new[] { "All" }
                    .Concat(segZones.Select(i => ZoneLighting.ZoneName(listed[i][ZoneLighting.Place], i)))
                    .ToArray();
                int width = ZonePickerWidth(zonePicker.Items.Length);
                zonePicker.Width = Scaled(width);
                zonePicker.Left = Scaled(Rows.Width - Rows.Inset - width);
                if (picked > segZones.Count) picked = 0;
            }
            RefreshControls();
            Loaded();
        });
    }
}
