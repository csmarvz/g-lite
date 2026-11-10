using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using Timer = System.Windows.Forms.Timer;
using System.Windows.Forms;
using Microsoft.Win32;

class KeyPicture : Ctl
{
    readonly List<RectangleF> keys = new List<RectangleF>();
    readonly List<Tuple<PointF, double>> ripples = new List<Tuple<PointF, double>>();
    readonly Stopwatch clock = Stopwatch.StartNew();
    readonly Timer timer = new Timer { Interval = 33 };
    readonly Random rnd = new Random();
    List<int> ids = new List<int>();
    float uw, uh, unit, ox, oy;
    byte id = 0x01;
    byte[] prm = new byte[10];
    int bright = 100;
    double lastRipple;
    bool live, dragging;
    Color[] liveColors;
    bool[] marked;
    Point dragFrom, dragTo;
    public event Action<List<int>> KeysPicked;

    public KeyPicture(Model m)
    {
        Cursor = Cursors.Default;
        TabStop = false;
        AccessibleRole = AccessibleRole.Graphic;
        SetModel(m);
        timer.Tick += (s, e) => { if (Visible) Invalidate(); };
        VisibleChanged += (s, e) => timer.Enabled = Visible;
    }

    public void SetModel(Model m)
    {
        keys.Clear();
        uw = uh = 0;
        AccessibleName = m.Name + " lighting";
        foreach (var row in m.Rows)
        {
            float x = 0;
            int before = keys.Count;
            foreach (var t in row.Split('|'))
            {
                if (t.StartsWith("_")) { x += float.Parse(t.Substring(1), CultureInfo.InvariantCulture); continue; }
                var parts = t.Split(':');
                float w = parts.Length > 1 ? float.Parse(parts[1], CultureInfo.InvariantCulture) : 1;
                keys.Add(new RectangleF(x, uh, w, 1));
                x += w;
                uw = Math.Max(uw, x);
            }
            uh += keys.Count > before ? 1 : x;
        }
        ids = m.KeyIds();
        liveColors = null;
        Invalidate();
    }

    public int Count { get { return keys.Count; } }
    public int IdOf(int key) { return ids[key]; }
    public float XOf(int key) { return (keys[key].X + keys[key].Width / 2) / uw; }

    public bool Live
    {
        get { return live; }
        set { live = value; Cursor = value ? Cursors.Cross : Cursors.Default; dragging = false; Invalidate(); }
    }

    public void ShowLive(Color[] colors, bool[] outlined)
    {
        liveColors = colors;
        marked = outlined;
        Invalidate();
    }

    public void Display(Lighting l, int b)
    {
        id = l == null ? (byte)0xFF : l.Effect;
        prm = l == null ? new byte[10] : G515.Encode(l, 100);
        bright = b;
        Invalidate();
    }

    RectangleF Cap(RectangleF r) { return new RectangleF(ox + r.X * unit + 1.5f * K, oy + r.Y * unit + 1.5f * K, r.Width * unit - 3 * K, unit - 3 * K); }

    Rectangle Marquee { get { return Rectangle.FromLTRB(Math.Min(dragFrom.X, dragTo.X), Math.Min(dragFrom.Y, dragTo.Y), Math.Max(dragFrom.X, dragTo.X), Math.Max(dragFrom.Y, dragTo.Y)); } }

    bool Dragged { get { return Math.Abs(dragTo.X - dragFrom.X) > 4 * K || Math.Abs(dragTo.Y - dragFrom.Y) > 4 * K; } }

    List<int> Touched()
    {
        var hits = new List<int>();
        for (int k = 0; k < keys.Count; k++)
        {
            var r = Cap(keys[k]);
            if (Dragged ? r.IntersectsWith(Marquee) : r.Contains(dragTo)) hits.Add(k);
        }
        return hits;
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (live)
        {
            if (e.Button != MouseButtons.Left) return;
            dragging = true;
            dragFrom = dragTo = e.Location;
            Invalidate();
            return;
        }
        if (unit > 0) ripples.Add(Tuple.Create(new PointF((e.X - ox) / unit, (e.Y - oy) / unit), clock.Elapsed.TotalSeconds));
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!dragging) return;
        dragTo = new Point(Math.Max(0, Math.Min(Width, e.X)), Math.Max(0, Math.Min(Height, e.Y)));
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (!dragging) return;
        dragTo = new Point(Math.Max(0, Math.Min(Width, e.X)), Math.Max(0, Math.Min(Height, e.Y)));
        var hits = Touched();
        dragging = false;
        Invalidate();
        if (hits.Count > 0 && KeysPicked != null) KeysPicked(hits);
    }

    protected override void OnMouseCaptureChanged(EventArgs e)
    {
        base.OnMouseCaptureChanged(e);
        if (dragging && !Capture) { dragging = false; Invalidate(); }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = Begin(e);
        float pad = 8 * K;
        unit = Math.Min((Width - 2 * pad) / uw, (Height - 2 * pad) / uh);
        ox = (Width - uw * unit) / 2;
        oy = (Height - uh * unit) / 2;
        Ui.Fill(g, new RectangleF(ox - pad, oy - pad, uw * unit + 2 * pad, uh * unit + 2 * pad), 8 * K, Theme.Bed, Theme.Bed);
        if (live) { PaintLive(g); return; }
        double t = clock.Elapsed.TotalSeconds, period = KeyboardView.PeriodOf(id, prm);
        if (id == 0x17 && t - lastRipple > 1.8)
        {
            var k = keys[rnd.Next(keys.Count)];
            ripples.Add(Tuple.Create(new PointF(k.X + k.Width / 2, k.Y + 0.5f), t));
            lastRipple = t;
        }
        ripples.RemoveAll(r => t - r.Item2 > 3);
        foreach (var r in keys)
        {
            double glow = 0;
            if (id == 0x17)
                foreach (var rp in ripples)
                {
                    double d = Math.Sqrt(Math.Pow(r.X + r.Width / 2 - rp.Item1.X, 2) + Math.Pow(r.Y + 0.5 - rp.Item1.Y, 2));
                    double front = (t - rp.Item2) * 8 / period;
                    glow = Math.Max(glow, Math.Max(0, 1 - Math.Abs(d - front)) * Math.Max(0, 1 - (t - rp.Item2) / 3));
                }
            var c = KeyboardView.Lit(id, prm, (r.X + r.Width / 2) / uw, r.Y / uh, t, glow);
            int max = Math.Max(c.R, Math.Max(c.G, c.B));
            var fill = Theme.KeyOff;
            if (max > 0)
            {
                var full = Color.FromArgb(c.R * 255 / max, c.G * 255 / max, c.B * 255 / max);
                fill = Ui.Mix(Theme.KeyOff, full, max / 255.0 * bright / 100.0);
            }
            Ui.Fill(g, Cap(r), 3 * K, fill, fill);
        }
    }

    void PaintLive(Graphics g)
    {
        var touched = dragging ? new HashSet<int>(Touched()) : new HashSet<int>();
        for (int k = 0; k < keys.Count; k++)
        {
            var fill = liveColors != null && k < liveColors.Length ? Ui.Mix(Theme.KeyOff, liveColors[k], bright / 100.0) : Theme.KeyOff;
            var r = Cap(keys[k]);
            Ui.Fill(g, r, 3 * K, fill, fill);
            if (touched.Contains(k) || marked != null && k < marked.Length && marked[k])
                using (var pen = new Pen(Theme.Accent, 2 * K))
                using (var path = Ui.Round(new RectangleF(r.X + K, r.Y + K, r.Width - 2 * K, r.Height - 2 * K), 3 * K))
                    g.DrawPath(pen, path);
        }
        if (!dragging || !Dragged) return;
        using (var b = new SolidBrush(Color.FromArgb(46, Theme.Accent))) g.FillRectangle(b, Marquee);
        using (var pen = new Pen(Theme.Accent, K)) g.DrawRectangle(pen, Marquee);
    }
}

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
    static readonly Model G515Board = Model.Load("g515-tkl.json"), G515Iso = Model.Load("g515-tkl-iso.json"), G915Tkl = Model.Load("g915-tkl.json"), G915Full = Model.Load("g915.json");
    const byte ScreenEffect = 0xFE;
    string Remembered { get { return "lighting." + found.Slug; } }
    string Saved { get { return "screen." + found.Slug; } }
    string Active { get { return "screen.on." + found.Slug; } }
    bool Screening { get { return cur.Effect == ScreenEffect; } }

    static Model BoardFor(Found f) { return !f.IsG915 ? G515Board : f.Name.ToUpper().Contains("TKL") ? G915Tkl : G915Full; }

    readonly bool g915, canScreen;
    List<byte[]> clusters;
    ScreenSetup setup;
    readonly Timer screenTimer = new Timer { Interval = 100 }, snapTimer = new Timer { Interval = 1000 }, watchTimer = new Timer { Interval = 500 };
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
    readonly Drop monitorDrop;
    readonly ScreenPreview preview;
    readonly ZoneList zoneList;
    readonly Btn addZone;
    readonly Slider boostSlider, screenBright, rateSlider;
    readonly Label boostValue, screenBrightValue, rateValue;
    readonly Toggle smoothToggle, batteryToggle, lockToggle;
    readonly Action presenceChanged;
    readonly EventHandler exiting;

    Lighting cur = new Lighting(), stored;
    int brightness = 100, storedBrightness = -1;
    bool loading, dirty, unknownStored, broken;
    int previewSeq, edits;
    readonly Timer debounce = new Timer { Interval = 120 };

    internal override bool Busy { get { return debounce.Enabled; } }

    readonly Label lightHead, colorHead, dirHead, speedHead, speedValue, brightValue;
    readonly Seg effects, dirs;
    readonly ColorRow colors;
    readonly Slider speed, bright;
    readonly Drop idleBox, dimBox, sleepBox;
    readonly Btn saveBtn, backupsBtn;
    readonly KeyPicture picture;
    Backups.Entry fix;

    public Page515(Found found, Hidpp fake, MouseEventHandler drag) : base(found, fake, drag, BoardFor(found).Name, "keyboard", found.IsG915 ? "profile.g915." : "profile.g515.")
    {
        g915 = found.IsG915;
        debounce.Tick += (s, e) => { debounce.Stop(); Preview(); };
        if (!g915 && found.Wpid != 0) Backups.Move(found.Wpid.ToString("X4"), BackupId);

        if (!g915)
        {
            backupsBtn = BackupsButton("keyboard's lighting");
            backupsBtn.Click += (s, e) => ShowBackups();
        }

        var pic = Ui.Add(this, new Box(), 20, 118, 616, 209);
        Ui.Text(pic, "Live on the keyboard", 14, 12, () => Theme.Dim, 9f, Ui.Semi);
        picture = Ui.Add(pic, new KeyPicture(BoardFor(found)), 14, 36, 588, 161);

        canScreen = found.Has(0x8081);
        setup = ScreenSetup.FromText(Settings.Get(Saved));
        var light = Ui.Add(this, new Box(), 20, 339, 380, 272);
        lightHead = Ui.Text(light, "Lighting", 14, 12, () => Theme.Dim, 9f, Ui.Semi);
        var effectHead = Ui.Text(light, "Effect", 14, 40, () => Theme.Dim, 9f);
        var names = G515.Effects.Select(d => d.Name).ToList();
        if (canScreen) names.Add("Screen");
        effects = Ui.Add(light, new Seg { Items = names.ToArray(), AccessibleName = "Effect" }, 14, 58, 352, 32);
        effects.Picked += i =>
        {
            if (i == G515.Effects.Length)
            {
                if (!Screening) { cur.Effect = ScreenEffect; ScreenOn(); }
                Changed(true);
                return;
            }
            if (Screening) ScreenOff();
            var d = G515.Effects[i];
            cur.Effect = d.Id;
            if (d.Speed && (cur.Period < d.MinP || cur.Period > d.MaxP)) cur.Period = d.DefP;
            Changed(true);
        };
        colorHead = Ui.Text(light, "Color", 14, 98, () => Theme.Dim, 9f);
        colors = new ColorRow(light, 115);
        colors.Picked += c => { cur.Color = c; Changed(true); };

        dirHead = Ui.Text(light, "Direction", 14, 156, () => Theme.Dim, 9f);
        dirs = Ui.Add(light, new Seg { Items = DirText, AccessibleName = "Direction" }, 14, 174, 352, 32);
        dirs.Picked += i => { cur.Direction = Directions[i]; Changed(true); };

        Ui.Text(light, "Brightness", 14, 214, () => Theme.Dim, 9f);
        brightValue = Value(light, 14, 214, 172);
        bright = Ui.Add(light, new Slider { AccessibleName = "Brightness" }, 14, 234, 172, 24);
        bright.Changed += (s, e) => BrightnessChanged(bright.Value);
        speedHead = Ui.Text(light, "Speed", 194, 214, () => Theme.Dim, 9f);
        speedValue = Value(light, 194, 214, 172);
        speed = Ui.Add(light, new Slider { AccessibleName = "Speed" }, 194, 234, 172, 24);
        speed.Changed += (s, e) =>
        {
            var d = G515.Def(cur.Effect);
            cur.Period = d.MaxP - (d.MaxP - d.MinP) * speed.Value / 100;
            Changed(false);
        };

        foreach (Control c in light.Controls) if (c != lightHead && c != effectHead && c != effects) plainLight.Add(c);

        screenLight.Add(Ui.Text(light, "Monitor", 14, 98, () => Theme.Dim, 9f));
        monitorDrop = Ui.Add(light, new Drop { AccessibleName = "Monitor" }, 14, 116, 200, 30);
        monitorDrop.Picked += i =>
        {
            var all = Screen.AllScreens;
            if (i < 0 || i >= all.Length) return;
            setup.Monitor = all[i].Primary ? "" : all[i].DeviceName;
            ScreenChanged();
            Snap();
        };
        preview = Ui.Add(light, new ScreenPreview(), 14, 152, 200, 112);
        preview.Picked += PickZone;
        preview.Done += ScreenChanged;
        screenLight.Add(monitorDrop);
        screenLight.Add(preview);
        screenLight.Add(Ui.Text(light, "Zones", 226, 98, () => Theme.Dim, 9f));
        zoneList = Ui.Add(light, new ZoneList(), 226, 116, 140, 110);
        zoneList.Picked += PickZone;
        zoneList.Removed += i =>
        {
            setup.Remove(i);
            selectedZone = Math.Max(0, Math.Min(i, setup.Zones.Count - 1));
            ScreenChanged();
        };
        addZone = Ui.Add(light, new Btn { Text = "Add", AccessibleName = "Add zone" }, 226, 232, 66, 30);
        addZone.Click += (s, e) =>
        {
            int i = setup.Add();
            if (i >= 0) selectedZone = i;
            ScreenChanged();
        };
        var resetZones = Ui.Add(light, new Btn { Text = "Reset", AccessibleName = "Reset zones" }, 300, 232, 66, 30);
        resetZones.Click += (s, e) =>
        {
            setup.Reset();
            selectedZone = 1;
            ScreenChanged();
        };
        screenLight.AddRange(new Control[] { zoneList, addZone, resetZones });
        foreach (var c in screenLight) c.Visible = false;
        picture.KeysPicked += picked =>
        {
            foreach (int k in picked) if (picture.IdOf(k) >= 0) setup.Keys[picture.IdOf(k)] = selectedZone;
            ScreenChanged();
        };

        screenBox = Ui.Add(this, new Box { Visible = false }, 412, 339, 224, 272);
        Ui.Text(screenBox, "Screen", 14, 12, () => Theme.Dim, 9f, Ui.Semi);
        Ui.Text(screenBox, "Color boost", 14, 36, () => Theme.Dim, 9f);
        boostValue = Value(screenBox, 14, 36, 196);
        boostSlider = Ui.Add(screenBox, new Slider { AccessibleName = "Color boost" }, 14, 54, 196, 20);
        boostSlider.Changed += (s, e) => { setup.Boost = boostSlider.Value; boostValue.Text = setup.Boost + "%"; force = true; debounce.Stop(); debounce.Start(); };
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
            debounce.Stop();
            debounce.Start();
        };
        smoothToggle = Option(screenBox, "Smooth changes", 166, () => setup.Smooth = !setup.Smooth);
        Ui.Text(screenBox, "Pause", 14, 194, () => Theme.Dim, 9f);
        batteryToggle = Option(screenBox, "On battery below " + ScreenSetup.BatteryLimit + "%", 214, () => setup.PauseOnBattery = !setup.PauseOnBattery);
        lockToggle = Option(screenBox, "Screen off or locked", 242, () => setup.PauseWhenLocked = !setup.PauseWhenLocked);

        screenTimer.Tick += (s, e) => Tick();
        snapTimer.Tick += (s, e) => Snap();
        watchTimer.Tick += (s, e) => Watch();
        if (!Manual) watchTimer.Start();
        presenceChanged = () => { if (IsHandleCreated) BeginInvoke((Action)PresenceChanged); };
        Presence.Changed += presenceChanged;
        exiting = (s, e) => Exiting();
        Application.ApplicationExit += exiting;

        var idle = Ui.Add(this, new Box(), 412, 339, 224, 272);
        idleArea = idle;
        Ui.Text(idle, g915 ? "Saving" : "When idle", 14, 12, () => Theme.Dim, 9f, Ui.Semi);
        var note = Ui.Text(idle, "G-Lite can't save to this keyboard yet. It keeps your lighting and sets it again when G-Lite starts and when you open this screen. Picking None gives the keyboard its own lighting back.", 14, 40, () => Theme.Dim, 9f);
        note.AutoSize = false;
        note.Size = new Size(196, 200);
        note.Visible = g915;
        idleBox = Choice(idle, "Dim after", IdleText, 40, i => cur.Idle = IdleSecs[i]);
        dimBox = Choice(idle, "Dim to", DimText, 98, i => cur.Dim = DimPcts[i]);
        sleepBox = Choice(idle, "Turn off after", SleepText, 156, i => cur.Sleep = SleepSecs[i]);
        if (g915) foreach (Control c in idle.Controls) if (c != note && c.Top > 30) c.Visible = false;

        saveBtn = Ui.Add(this, new Btn { Look = Look.Primary, Text = "Save to keyboard", Visible = !g915 }, 496, 635, 140, 32);
        saveBtn.Click += (s, e) => Save();

        if (canScreen && Settings.Get(Active) == "1")
        {
            cur.Effect = ScreenEffect;
            ScreenOn();
        }
        Sync();
        if (g915) Load();
    }

    Toggle Option(Control parent, string text, int y, Action flip)
    {
        var toggle = Ui.Add(parent, new Toggle { AccessibleName = text }, 14, y, 34, 18);
        toggle.Changed += () => { flip(); ScreenChanged(); };
        var label = Ui.Text(parent, text, 56, y, () => Theme.Text, 9f);
        label.Click += (s, e) => { toggle.Checked = !toggle.Checked; flip(); ScreenChanged(); };
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
        saveBtn.Enabled = !broken && !Screening;
        picture.Display(Known() ? cur : null, brightness);
        Calm();
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
        Did("Screen effect on");
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
        Did("Screen effect off");
        if (claimed) Do(h => { if (g915) G915.Release(h); else G515.Release(h); claimed = false; });
    }

    void ScreenChanged()
    {
        Settings.Set(Saved, setup.ToText());
        force = true;
        SyncScreen();
        Calm();
    }

    string Paused()
    {
        if (setup.PauseWhenLocked && (Presence.Locked || Presence.DisplayOff)) return "Paused while the screen is off or locked.";
        if (setup.PauseOnBattery && battery >= 0 && !charging && battery < ScreenSetup.BatteryLimit) return "Paused: battery below " + ScreenSetup.BatteryLimit + "%.";
        return null;
    }

    int ZoneOfKey(int key) { return setup.ZoneOf(picture.IdOf(key), picture.XOf(key)); }

    void ShowLive()
    {
        if (!Screening) return;
        var colors = new Color[picture.Count];
        var outlined = new bool[picture.Count];
        for (int k = 0; k < picture.Count; k++)
        {
            int zone = ZoneOfKey(k), c = shown != null && zone < shown.Length ? shown[zone] : 0;
            colors[k] = Color.FromArgb(c >> 16 & 0xFF, c >> 8 & 0xFF, c & 0xFF);
            outlined[k] = zone == selectedZone;
        }
        picture.ShowLive(colors, outlined);
    }

    void Claim(Hidpp h, bool quiet)
    {
        if (!quiet) { if (g915) G915.Claim(h); else G515.Claim(h); return; }
        if (!g915) h.Quiet(h.F(0x8101), 6, 0x05);
        h.Quiet(h.F(0x8071), 5, 0x01, 0x03, 0x04);
    }

    void Failed(Exception e)
    {
        claimed = false;
        sent = null;
        failing = true;
        nextTry = DateTime.UtcNow.AddSeconds(5);
        problem = "The screen effect couldn't reach the keyboard: " + e.Message;
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
        if (why != null) { problem = null; Say(why, true); }
        else if (statusDirty) { statusDirty = false; Calm(); }
        if (!Screening || sending || DateTime.UtcNow < nextTry) return;
        bool readBattery = found.Has(0x1004) && DateTime.UtcNow >= nextBattery;
        if (readBattery) nextBattery = DateTime.UtcNow.AddSeconds(60);
        if (Paused() != null)
        {
            if (released && !readBattery) return;
            bool release = !released;
            released = true;
            sending = true;
            Do(h =>
            {
                try
                {
                    if (release && claimed) { if (g915) G915.Release(h); else G515.Release(h); claimed = false; }
                    if (readBattery) ReadBattery(h);
                    statusDirty = true;
                    Worked();
                }
                catch (Exception e) { Failed(e); throw; }
                finally { sending = false; }
            }, null, msg => { });
            return;
        }
        if (released) { released = false; force = true; Calm(); }
        if (capturing) return;
        capturing = true;
        var monitor = ScreenCapture.Find(setup.Monitor).Bounds;
        var zones = setup.Zones.ToList();
        var ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        ThreadPool.QueueUserWorkItem(state =>
        {
            int[] sampled = null;
            try { sampled = ScreenCapture.Sample(monitor, zones); } catch (Exception) { }
            try
            {
                ui.Post(posted =>
                {
                    capturing = false;
                    if (sampled != null && !IsDisposed && Screening && !sending && sampled.Length == setup.Zones.Count) Sampled(sampled, readBattery);
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
        for (int z = 0; z < sampled.Length; z++)
        {
            int target = ScreenCapture.Boost(sampled[z], setup.Boost);
            shown[z] = setup.Smooth && !fresh ? ScreenCapture.Blend(shown[z], target, 0.45) : target;
            if (!changed && ScreenCapture.Distance(shown[z], sent[z]) > 6) changed = true;
        }
        ShowLive();
        if (!changed && !readBattery) return;
        var groups = new Dictionary<int, List<int>>();
        var done = new HashSet<int>();
        for (int k = 0; k < picture.Count; k++)
        {
            int id = picture.IdOf(k);
            if (id < 0 || !done.Add(id)) continue;
            int c = shown[ZoneOfKey(k)];
            List<int> list;
            if (!groups.TryGetValue(c, out list)) groups[c] = list = new List<int>();
            list.Add(id);
        }
        force = false;
        sent = (int[])shown.Clone();
        sending = true;
        bool send = changed, readLayout = layoutCode < 0 && !g915 && found.Has(0x4540);
        Do(h =>
        {
            try
            {
                if (readLayout) { layoutCode = h.Call(h.F(0x4540), 0)[0]; layoutDirty = true; }
                if (send)
                {
                    if (present == null) present = KeyLights.Present(h);
                    if (!claimed || refresh) { Claim(h, claimed); claimed = true; }
                    foreach (var list in groups.Values) list.RemoveAll(id => !present.Contains(id));
                    KeyLights.Send(h, groups);
                }
                if (readBattery) { ReadBattery(h); statusDirty = true; }
                Worked();
            }
            catch (Exception e) { Failed(e); throw; }
            finally { sending = false; }
        }, null, msg => { });
    }

    void ReadBattery(Hidpp h)
    {
        var r = h.Quiet(h.F(0x1004), 1);
        battery = r[0];
        charging = r[2] != 0;
    }

    void ApplyLayout()
    {
        if (g915 || iso || layoutCode <= 2) return;
        iso = true;
        picture.SetModel(G515Iso);
        force = true;
        if (Screening) SyncScreen();
    }

    void Snap()
    {
        if (!Screening || !Visible || snapping) return;
        snapping = true;
        var monitor = ScreenCapture.Find(setup.Monitor).Bounds;
        int w = preview.Width, h = Math.Max(1, w * monitor.Height / Math.Max(1, monitor.Width));
        var ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        ThreadPool.QueueUserWorkItem(state =>
        {
            Bitmap image = null;
            try { image = ScreenCapture.Shrink(monitor, w, h); } catch (Exception) { }
            try
            {
                ui.Post(posted =>
                {
                    snapping = false;
                    if (image == null) return;
                    if (IsDisposed || ScreenCapture.Find(setup.Monitor).Bounds != monitor) { image.Dispose(); if (!IsDisposed) Snap(); return; }
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
        monitorDrop.Items.Clear();
        monitorDrop.Items.AddRange(all.Select(ScreenCapture.Label));
        monitorDrop.Index = Array.IndexOf(all, chosen);
        selectedZone = Math.Max(0, Math.Min(selectedZone, setup.Zones.Count - 1));
        preview.Zones = setup.Zones;
        preview.Selected = selectedZone;
        preview.Monitor = chosen.Bounds.Size;
        preview.Invalidate();
        var counts = new int[setup.Zones.Count];
        var seen = new HashSet<int>();
        for (int k = 0; k < picture.Count; k++) if (picture.IdOf(k) >= 0 && seen.Add(picture.IdOf(k))) counts[ZoneOfKey(k)]++;
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

    public static bool Manual;

    internal void Exiting()
    {
        if (Screening)
        {
            if (!claimed) return;
            Do(h => { if (g915) G915.Release(h); else G515.Release(h); claimed = false; });
        }
        else if (g915 || !dirty) return;
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

    public override void Restyle() { Sync(); }

    protected override void Lost() { claimed = false; clusters = null; present = null; }

    protected override bool Unsaved { get { return cur != null && (g915 ? Known() : stored != null && (dirty || Screening)); } }

    protected override string Current() { return cur.ToText(brightness) + (Screening ? "|" + setup.ToText() : ""); }

    protected override void Use(string text)
    {
        int b;
        var parts = text.Split('|');
        var l = Lighting.FromText(parts[0], out b);
        if (l.Effect == ScreenEffect && !canScreen) l.Effect = 0x01;
        bool was = Screening;
        if (parts.Length > 1) setup = ScreenSetup.FromText(parts[1]);
        cur = l;
        brightness = Math.Max(0, Math.Min(100, b));
        if (was && !Screening) ScreenOff();
        if (!was && Screening) ScreenOn();
        if (Screening) ScreenChanged();
        Changed(true);
    }

    Drop Choice(Control parent, string label, string[] items, int y, Action<int> set)
    {
        Ui.Text(parent, label, 14, y, () => Theme.Dim, 9f);
        var d = Ui.Add(parent, new Drop { AccessibleName = label }, 14, y + 18, 196, 32);
        d.Items.AddRange(items);
        d.Picked += i => { set(i); Changed(false); };
        return d;
    }

    bool Known() { return g915 ? !unknownStored : !(unknownStored && !dirty); }

    static int Near(int[] values, int v)
    {
        int best = 0;
        for (int i = 0; i < values.Length; i++) if (Math.Abs(values[i] - v) < Math.Abs(values[best] - v)) best = i;
        return best;
    }

    void Sync()
    {
        loading = true;
        bool screening = Screening;
        foreach (var c in plainLight) c.Visible = !screening;
        foreach (var c in screenLight) c.Visible = screening;
        screenBox.Visible = screening;
        idleArea.Visible = !screening;
        if (picture.Live != screening) picture.Live = screening;
        var d = G515.Def(cur.Effect);
        bool known = Known();
        bool color = known && d != null && d.Color, dir = known && d != null && d.Direction, spd = known && d != null && d.Speed;
        lightHead.Text = known ? "Lighting" : broken ? "Lighting · half written on the keyboard" : g915 ? "Lighting · as on the keyboard" : "Lighting · factory animation";
        effects.Index = known ? Array.FindIndex(G515.Effects, e => e.Id == cur.Effect) : -1;
        Shade(colorHead, color);
        colors.Sync(cur.Color, color);
        Shade(dirHead, dir);
        dirs.Enabled = dir;
        dirs.Index = dir ? Array.IndexOf(Directions, cur.Direction) : -1;
        Shade(speedHead, spd);
        speed.Enabled = spd;
        if (spd) speed.Value = (d.MaxP - cur.Period) * 100 / (d.MaxP - d.MinP);
        speedValue.Text = spd ? speed.Value + "%" : "";
        speed.Invalidate();
        bright.Value = brightness;
        brightValue.Text = brightness + "%";
        bright.Invalidate();
        idleBox.Index = Near(IdleSecs, cur.Idle);
        dimBox.Index = Near(DimPcts, cur.Dim);
        sleepBox.Index = Near(SleepSecs, cur.Sleep);
        dimBox.Enabled = cur.Idle > 0;
        saveBtn.Enabled = dirty && !broken;
        picture.Display(known ? cur : null, brightness);
        if (screening)
        {
            foreach (var c in plainLight) c.Visible = false;
            effects.Index = G515.Effects.Length;
            lightHead.Text = "Lighting";
            saveBtn.Enabled = false;
            SyncScreen();
        }
        loading = false;
    }

    void Changed(bool now)
    {
        if (loading) return;
        if (g915) unknownStored = false;
        dirty = true;
        edits++;
        Edited();
        Sync();
        Calm();
        debounce.Stop();
        if (now) Preview(); else debounce.Start();
    }

    protected override void Calm()
    {
        if (broken) { ShowBroken(); return; }
        if (DynamicLightingOn())
            Say("Windows Dynamic Lighting is on and may override this.", true, () => Process.Start("ms-settings:personalization-lighting"), "Open Windows settings");
        else if (Screening)
        {
            var why = Paused();
            if (why != null) Say(why, true);
            else if (ScreenCapture.Missing(setup.Monitor)) Say("The chosen monitor isn't connected. Using the main monitor.", true);
            else Say("Live only.");
        }
        else if (g915) Say(Known() ? "Set by G-Lite · not saved to the keyboard" : "");
        else Say(dirty ? "Not saved to the keyboard" : stored == null ? "" : "Matches the keyboard");
    }

    public static int TestDynamicLighting = -1;

    static bool DynamicLightingOn()
    {
        if (TestDynamicLighting >= 0) return TestDynamicLighting == 1;
        using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Lighting"))
            return k != null && Equals(k.GetValue("AmbientLightingEnabled"), 1);
    }

    void ShowBroken()
    {
        const string half = "The lighting saved on the keyboard is half written.";
        var last = fix;
        if (last != null) Say(half + " Last backup: " + When(last) + ".", true, () => Restore(last.Files, "the backup from " + When(last)), "Restore backup");
        else Say(half, true, () => Restore(new G515.Files(), "the factory lighting"), "Restore factory lighting");
    }

    void ShowBackups()
    {
        BackupMenu(backupsBtn, BackupId, "Restore the factory lighting", () => Restore(new G515.Files(), "the factory lighting"),
            e => Backups.Describe(e.Files), e => G515.Good(e.Files), e => Restore(e.Files, "the backup from " + When(e)));
    }

    void Restore(G515.Files backup, string what)
    {
        debounce.Stop();
        previewSeq++;
        saveBtn.Enabled = false;
        Say("Restoring " + what + "…");
        Did("Restore " + what);
        int b = storedBrightness;
        Do(h =>
        {
            Backups.Save(BackupId, G515.Read(h), Who);
            G515.Restore(h, backup);
            claimed = false;
            if (b >= 0) G515.SetBrightness(h, b);
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
            cur = stored != null ? stored.Clone() : new Lighting();
        }
        if (g915) { Forget(); return; }
        if (stored == null) { Sync(); Calm(); return; }
        cur = stored.Clone();
        brightness = storedBrightness;
        Release();
        Sync();
        Calm();
    }

    void Preview()
    {
        if (Screening)
        {
            Settings.Set(Saved, setup.ToText());
            int level = brightness;
            Do(h => { if (level != G515.Brightness(h)) G515.SetBrightness(h, level); }, Calm);
            return;
        }
        idleLevel = 0;
        var l = cur.Clone();
        int b = brightness, seq = ++previewSeq;
        if (g915) { Apply(l, b, seq); return; }
        Do(h =>
        {
            if (seq != previewSeq) return;
            if (b != G515.Brightness(h)) G515.SetBrightness(h, b);
            if (!claimed && l.Same(stored)) return;
            if (!claimed) { G515.Claim(h); claimed = true; }
            try { G515.Preview(h, l); }
            catch (HidppError e) { if (e.Code != 5) throw; G515.Claim(h); G515.Preview(h, l); }
        }, () => { if (seq == previewSeq) Calm(); });
    }

    void Save()
    {
        var l = cur.Clone();
        int b = brightness;
        saveBtn.Enabled = false;
        Say("Saving…");
        Did("Save to keyboard");
        Do(h =>
        {
            var f = G515.Snapshot(h);
            Backups.Save(BackupId, f, Who);
            G515.Store(h, f, l);
            claimed = false;
        }, () =>
        {
            stored = l;
            storedBrightness = b;
            dirty = false;
            unknownStored = false;
            Sync();
            Say("Saved to the keyboard");
        });
    }

    void Apply(Lighting l, int b, int seq)
    {
        bool known = !unknownStored, sent = false;
        Do(h =>
        {
            if (seq != previewSeq) return;
            if (clusters == null) clusters = G915.Clusters(h);
            if (b != G515.Brightness(h)) G515.SetBrightness(h, b);
            if (!known) return;
            G915.Claim(h);
            G915.Preview(h, clusters, l);
            sent = true;
        }, () =>
        {
            if (sent && seq == previewSeq) Settings.Set(Remembered, l.ToText(b));
            if (seq == previewSeq) Calm();
        });
    }

    void Forget()
    {
        Settings.Remove(Remembered);
        unknownStored = true;
        dirty = false;
        edits++;
        int seq = ++previewSeq;
        debounce.Stop();
        Sync();
        Calm();
        int b = -1;
        Do(h => { G915.Release(h); b = G515.Brightness(h); }, () => { if (seq == previewSeq && b >= 0) { brightness = b; Sync(); } });
    }

    void LoadG915()
    {
        dirty = false;
        debounce.Stop();
        Say("Reading the keyboard…");
        int n = edits, seq = previewSeq;
        int b = 100;
        string kept = Settings.Get(Remembered);
        Lighting l = null;
        int kb = -1;
        if (kept != null) try { l = Lighting.FromText(kept, out kb); } catch (Exception) { l = null; }
        bool dropped = false;
        bool screening = Screening;
        Do(h =>
        {
            clusters = G915.Clusters(h);
            if (l != null && seq == previewSeq && !screening)
            {
                if (kb >= 0 && kb <= 100 && kb != G515.Brightness(h)) G515.SetBrightness(h, kb);
                G915.Claim(h);
                try { G915.Preview(h, clusters, l); }
                catch (Refused) { G915.Release(h); dropped = true; }
            }
            b = G515.Brightness(h);
        }, () =>
        {
            if (dropped) { Settings.Remove(Remembered); l = null; }
            storedBrightness = b;
            if (n == edits)
            {
                stored = l == null ? null : l.Clone();
                unknownStored = l == null;
                brightness = b;
                if (l != null && !Screening) cur = l;
            }
            Sync();
            Loaded();
        });
    }

    public override void Load()
    {
        if (g915) { LoadG915(); return; }
        bool live = dirty && !Screening;
        if (!live) { dirty = false; debounce.Stop(); }
        Say("Reading the keyboard…");
        int n = edits;
        Lighting l = null;
        int b = 100;
        bool unknown = false, half = false, readLayout = layoutCode < 0 && found.Has(0x4540);
        Do(h =>
        {
            if (readLayout) layoutCode = h.Call(h.F(0x4540), 0)[0];
            G515.Files f;
            try { f = G515.Snapshot(h); }
            catch (HalfWritten) { half = true; b = G515.Brightness(h); return; }
            l = G515.Decode(f.UserProfile ?? f.FactoryProfile);
            unknown = l == null;
            if (l == null) l = new Lighting();
            b = G515.Brightness(h);
        }, () =>
        {
            ApplyLayout();
            broken = half;
            fix = half ? Backups.List(BackupId).FirstOrDefault(e => G515.Good(e.Files)) : null;
            storedBrightness = b;
            stored = half ? null : l.Clone();
            unknownStored = half || unknown;
            if (n == edits && !live)
            {
                brightness = b;
                if (!half && !Screening) cur = l;
            }
            if (Screening) dirty = true;
            Sync();
            Loaded();
        });
    }

    void PresenceChanged()
    {
        if (Screening) { Tick(); Calm(); return; }
        bool now = Presence.Locked || Presence.DisplayOff;
        if (g915 || now == away) return;
        away = now;
        if (!dirty) return;
        if (away) { if (idleLevel != 2) GiveBack(); }
        else Preview();
    }

    internal void Watch()
    {
        if (g915 || Screening || !dirty || !claimed || away) { idleLevel = 0; return; }
        Idle();
        if (++watchTicks % 6 != 0 || watching || idleLevel == 2) return;
        watching = true;
        var l = cur.Clone();
        int b = brightness;
        Do(h =>
        {
            try
            {
                if (!claimed || h.Quiet(h.F(0x8071), 5, 0x00)[1] != 0) return;
                G515.Claim(h);
                claimed = true;
                if (b != G515.Brightness(h)) G515.SetBrightness(h, b);
                Show(h, l, idleLevel);
            }
            catch (HidppError) { }
            catch (TimeoutException) { }
            finally { watching = false; }
        }, null, message => { });
    }

    void Idle()
    {
        int idle = Win.IdleSeconds();
        int level = cur.Sleep > 0 && idle >= cur.Sleep ? 2 : cur.Idle > 0 && idle >= cur.Idle ? 1 : 0;
        if (level == idleLevel) return;
        idleLevel = level;
        var l = cur.Clone();
        Do(h =>
        {
            if (!claimed) return;
            try { Show(h, l, level); }
            catch (HidppError) { }
            catch (TimeoutException) { }
        }, null, message => { });
    }

    static void Show(Hidpp h, Lighting l, int level)
    {
        if (level == 2) G515.Preview(h, new Lighting { Effect = 0 });
        else G515.Preview(h, l, level == 1 ? l.Dim : 100);
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
        int b = storedBrightness;
        var back = unknownStored || stored == null ? null : stored.Clone();
        previewSeq++;
        Do(h =>
        {
            if (b >= 0) G515.SetBrightness(h, b);
            if (!claimed) return;
            try { if (back != null) G515.Preview(h, back); }
            catch (HidppError e) { if (e.Code != 5) throw; }
            G515.Release(h);
            claimed = false;
        });
    }
}
