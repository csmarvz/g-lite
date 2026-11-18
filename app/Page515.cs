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

class KeyPicture : DrawnControl
{
    readonly List<RectangleF> keys = new List<RectangleF>();
    readonly List<Tuple<PointF, double>> ripples = new List<Tuple<PointF, double>>();
    readonly Stopwatch clock = Stopwatch.StartNew();
    readonly Timer timer = new Timer { Interval = 33 };
    readonly Random random = new Random();
    List<int> ids = new List<int>();
    float widthInKeys, heightInKeys, keySize, offsetX, offsetY;
    byte id = 0x01, barId = 0x01;
    byte[] parameters = new byte[10], barParameters = new byte[10];
    public HashSet<int> Bar = new HashSet<int>();
    public int Part;
    public event Action<bool> PartPicked;
    int bright = 100;
    double lastRipple;
    bool live, dragging;
    Color[] liveColors;
    bool[] marked;
    Point dragFrom, dragTo;
    public event Action<List<int>> KeysPicked;

    public KeyPicture(KeyLayout m)
    {
        Cursor = Cursors.Default;
        TabStop = false;
        AccessibleRole = AccessibleRole.Graphic;
        SetModel(m);
        timer.Tick += (s, e) => { if (Visible) Invalidate(); };
        VisibleChanged += (s, e) => timer.Enabled = Visible;
    }

    public void SetModel(KeyLayout m)
    {
        keys.Clear();
        lips.Clear();
        widthInKeys = heightInKeys = 0;
        AccessibleName = m.Name + " lighting";
        foreach (var row in m.Rows)
        {
            float x = 0, tall = 0;
            foreach (var t in row.Split('|'))
            {
                if (t.StartsWith("_")) { x += float.Parse(t.Substring(1), CultureInfo.InvariantCulture); continue; }
                var parts = t.Split(':');
                float w = parts.Length > 1 ? float.Parse(parts[1], CultureInfo.InvariantCulture) : 1;
                float h = parts.Length > 2 ? float.Parse(parts[2], CultureInfo.InvariantCulture) : 1;
                if (parts.Length > 3) lips[keys.Count] = float.Parse(parts[3], CultureInfo.InvariantCulture);
                keys.Add(new RectangleF(x, heightInKeys, w, h));
                x += w;
                // A key taller than a row (the numpad's + and Enter) hangs into the next one, which leaves a gap for it.
                tall = Math.Max(tall, Math.Min(h, 1));
                widthInKeys = Math.Max(widthInKeys, x);
            }
            heightInKeys += tall > 0 ? tall : x;
        }
        ids = m.KeyIds();
        liveColors = null;
        Invalidate();
    }

    public int Count { get { return keys.Count; } }
    public int IdOf(int key) { return ids[key]; }
    public float XOf(int key) { return (keys[key].X + keys[key].Width / 2) / widthInKeys; }

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

    public void Display(Lighting l, int b) { Display(l, l, b); }

    public void Display(Lighting l, Lighting bar, int b)
    {
        id = l == null ? (byte)0xFF : l.Effect;
        parameters = l == null ? new byte[10] : G515Profile.Encode(l, 100);
        var other = bar ?? l;
        barId = other == null ? (byte)0xFF : other.Effect;
        barParameters = other == null ? new byte[10] : G515Profile.Encode(other, 100);
        bright = b;
        Invalidate();
    }

    bool OnBar(int key) { return Bar.Contains(ids[key]); }

    RectangleF Cap(RectangleF r) { return new RectangleF(offsetX + r.X * keySize + 1.5f * DpiScale, offsetY + r.Y * keySize + 1.5f * DpiScale, r.Width * keySize - 3 * DpiScale, r.Height * keySize - 3 * DpiScale); }

    // A key with a lip ("Enter:1.25:2:.25", the ISO Enter) is an L: its own rectangle plus its first row widened to the left by the lip.
    readonly Dictionary<int, float> lips = new Dictionary<int, float>();

    RectangleF? TopRow(int k)
    {
        float lip;
        if (!lips.TryGetValue(k, out lip)) return null;
        var r = keys[k];
        return Cap(new RectangleF(r.X - lip, r.Y, r.Width + lip, 1));
    }

    bool Hit(int k, Func<RectangleF, bool> test) { var top = TopRow(k); return test(Cap(keys[k])) || top != null && test(top.Value); }

    void FillKey(Graphics g, int k, Color fill)
    {
        Ui.Fill(g, Cap(keys[k]), 3 * DpiScale, fill, fill);
        var top = TopRow(k);
        if (top != null) Ui.Fill(g, top.Value, 3 * DpiScale, fill, fill);
    }

    void Outline(Graphics g, int k, Pen pen)
    {
        var r = Cap(keys[k]);
        var top = TopRow(k);
        if (top == null)
        {
            using (var path = Ui.Round(new RectangleF(r.X + DpiScale, r.Y + DpiScale, r.Width - 2 * DpiScale, r.Height - 2 * DpiScale), 3 * DpiScale)) g.DrawPath(pen, path);
            return;
        }
        var t = top.Value;
        pen.LineJoin = System.Drawing.Drawing2D.LineJoin.Round;
        g.DrawPolygon(pen, new[] { new PointF(t.Left + DpiScale, t.Top + DpiScale), new PointF(r.Right - DpiScale, t.Top + DpiScale), new PointF(r.Right - DpiScale, r.Bottom - DpiScale), new PointF(r.Left + DpiScale, r.Bottom - DpiScale), new PointF(r.Left + DpiScale, t.Bottom - DpiScale), new PointF(t.Left + DpiScale, t.Bottom - DpiScale) });
    }

    Rectangle Marquee { get { return Rectangle.FromLTRB(Math.Min(dragFrom.X, dragTo.X), Math.Min(dragFrom.Y, dragTo.Y), Math.Max(dragFrom.X, dragTo.X), Math.Max(dragFrom.Y, dragTo.Y)); } }

    bool Dragged { get { return Math.Abs(dragTo.X - dragFrom.X) > 4 * DpiScale || Math.Abs(dragTo.Y - dragFrom.Y) > 4 * DpiScale; } }

    List<int> Touched()
    {
        var hits = new List<int>();
        for (int k = 0; k < keys.Count; k++)
            if (Hit(k, r => Dragged ? r.IntersectsWith(Marquee) : r.Contains(dragTo))) hits.Add(k);
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
        if (keySize > 0) ripples.Add(Tuple.Create(new PointF((e.X - offsetX) / keySize, (e.Y - offsetY) / keySize), clock.Elapsed.TotalSeconds));
        if (Bar.Count == 0 || PartPicked == null || e.Button != MouseButtons.Left) return;
        for (int k = 0; k < keys.Count; k++)
            if (Hit(k, r => { r.Inflate(0, 2 * DpiScale); return r.Contains(e.Location); })) { PartPicked(OnBar(k)); return; }
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
        float pad = 8 * DpiScale;
        keySize = Math.Min((Width - 2 * pad) / widthInKeys, (Height - 2 * pad) / heightInKeys);
        offsetX = (Width - widthInKeys * keySize) / 2;
        offsetY = (Height - heightInKeys * keySize) / 2;
        Ui.Fill(g, new RectangleF(offsetX - pad, offsetY - pad, widthInKeys * keySize + 2 * pad, heightInKeys * keySize + 2 * pad), 8 * DpiScale, Theme.PictureBackground, Theme.PictureBackground);
        if (live) { PaintLive(g); return; }
        double t = clock.Elapsed.TotalSeconds, period = KeyboardView.PeriodOf(id, parameters);
        if (id == 0x17 && t - lastRipple > 1.8)
        {
            var k = keys[random.Next(keys.Count)];
            ripples.Add(Tuple.Create(new PointF(k.X + k.Width / 2, k.Y + 0.5f), t));
            lastRipple = t;
        }
        ripples.RemoveAll(r => t - r.Item2 > 3);
        for (int k = 0; k < keys.Count; k++)
        {
            var r = keys[k];
            bool onBar = OnBar(k);
            byte kid = onBar ? barId : id;
            var kprm = onBar ? barParameters : parameters;
            double glow = 0;
            if (kid == 0x17)
                foreach (var rp in ripples)
                {
                    double d = Math.Sqrt(Math.Pow(r.X + r.Width / 2 - rp.Item1.X, 2) + Math.Pow(r.Y + 0.5 - rp.Item1.Y, 2));
                    double front = (t - rp.Item2) * 8 / period;
                    glow = Math.Max(glow, Math.Max(0, 1 - Math.Abs(d - front)) * Math.Max(0, 1 - (t - rp.Item2) / 3));
                }
            var c = KeyboardView.EffectColorAt(kid, kprm, (r.X + r.Width / 2) / widthInKeys, r.Y / heightInKeys, t, glow);
            int max = Math.Max(c.R, Math.Max(c.G, c.B));
            var fill = Theme.KeyOff;
            if (max > 0)
            {
                var full = Color.FromArgb(c.R * 255 / max, c.G * 255 / max, c.B * 255 / max);
                fill = Ui.Mix(Theme.KeyOff, full, max / 255.0 * bright / 100.0);
            }
            if (Part != 0 && Part != (onBar ? 2 : 1)) fill = Ui.Mix(Theme.KeyOff, fill, 0.3);
            FillKey(g, k, fill);
        }
    }

    void PaintLive(Graphics g)
    {
        var touched = dragging ? new HashSet<int>(Touched()) : new HashSet<int>();
        for (int k = 0; k < keys.Count; k++)
        {
            var fill = liveColors != null && k < liveColors.Length ? Ui.Mix(Theme.KeyOff, liveColors[k], bright / 100.0) : Theme.KeyOff;
            FillKey(g, k, fill);
            if (touched.Contains(k) || marked != null && k < marked.Length && marked[k])
                using (var pen = new Pen(Theme.Accent, 2 * DpiScale)) Outline(g, k, pen);
        }
        if (!dragging || !Dragged) return;
        using (var b = new SolidBrush(Color.FromArgb(46, Theme.Accent))) g.FillRectangle(b, Marquee);
        using (var pen = new Pen(Theme.Accent, DpiScale)) g.DrawRectangle(pen, Marquee);
    }
}

class KeyboardKinds
{
    public KeyLayout Keys;
    KeyLayout iso;
    bool isoBuilt;
    public KeyLayout Iso { get { if (!isoBuilt) { iso = Keys.Iso(); isoBuilt = true; } return iso; } }
    public string Prefix;
    public bool Saves, Profile, Guessed;
    public int[] Bar;

    public static string File(string name)
    {
        name = name.ToUpper();
        bool small = name.Contains("TKL");
        return name.Contains("G915 X") ? (small ? "g915-tkl.json" : "g915.json") : name.Contains(" 60") ? "g-60.json" : name.Contains(" 75") ? "g512-x-75.json" : name.Contains(" 98") ? "g-full.json" : "g515-tkl.json";
    }

    public static KeyboardKinds For(FoundDevice found)
    {
        string file = File(found.Name);
        var keys = LoadLayout(file, found.ShortName);
        if (file == "g512-x-75.json")
        {
            keys.Rows = keys.Rows.Take(7).ToArray();
            keys.Ids = keys.Ids.Take(7).ToArray();
        }
        return new KeyboardKinds { Keys = keys, Prefix = "profile." + found.SettingsKey + ".", Guessed = true };
    }

    static KeyLayout LoadLayout(string file, string name)
    {
        var model = KeyLayout.Load(file);
        model.Name = name;
        return model;
    }

    public static readonly KeyboardKinds
        G515 = new KeyboardKinds { Keys = KeyLayout.Load("g515-tkl.json"), Prefix = "profile.g515.", Saves = true },
        G915Tkl = new KeyboardKinds { Keys = KeyLayout.Load("g915-tkl.json"), Prefix = "profile.g915.", Profile = true },
        G915 = new KeyboardKinds { Keys = KeyLayout.Load("g915.json"), Prefix = "profile.g915.", Profile = true },
        G815 = new KeyboardKinds { Keys = LoadLayout("g915.json", "G815"), Prefix = "profile.g815.", Profile = true },
        G512X = new KeyboardKinds { Keys = KeyLayout.Load("g512-x-75.json"), Prefix = "profile.g512x.", Bar = Enumerable.Range(0xA0, 33).ToArray() };
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
    const byte ScreenEffect = 0xFE;
    string Remembered { get { return "lighting." + found.SettingsKey; } }
    string Saved { get { return "screen." + found.SettingsKey; } }
    string Active { get { return "screen.on." + found.SettingsKey; } }
    bool Screening { get { return cur.Effect == ScreenEffect; } }
    bool leftScreen;

    readonly KeyboardKinds kind;
    readonly bool live, canScreen;
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
    readonly Dropdown monitorDrop;
    readonly ScreenPreview preview;
    readonly ZoneList zoneList;
    readonly FlatButton addZone;
    readonly Slider boostSlider, screenBright, rateSlider;
    readonly Label boostValue, screenBrightValue, rateValue;
    readonly Toggle smoothToggle, batteryToggle, lockToggle;
    readonly Action presenceChanged;
    readonly EventHandler exiting;

    Lighting cur = new Lighting(), stored, bar;
    int part;
    readonly SegmentedPicker partSeg;
    readonly Toggle perZone;
    const byte Ripple = 0x17;
    bool Parts { get { return kind.Bar != null; } }
    Lighting Viewed { get { return part == 2 && bar != null ? bar : cur; } }
    int brightness = 100, storedBrightness = -1;
    bool loading, dirty, unknownStored, broken;
    int previewSeq, edits;
    readonly Timer debounce = new Timer { Interval = 120 };

    internal override bool Busy { get { return debounce.Enabled; } }

    readonly Label lightHead, colorHead, dirHead, speedHead, speedValue, brightValue;
    readonly SegmentedPicker effects, dirs;
    readonly ColorRow colors;
    readonly Slider speed, bright;
    readonly Dropdown idleBox, dimBox, sleepBox;
    readonly int never;
    readonly FlatButton saveBtn, backupsBtn;
    readonly KeyPicture picture;
    Backups.Entry fix;

    public Page515(FoundDevice found, Hidpp fake, MouseEventHandler drag, KeyboardKinds kind) : base(found, fake, drag, kind.Keys.Name, "keyboard", kind.Prefix)
    {
        this.kind = kind;
        live = !kind.Saves;
        debounce.Tick += (s, e) => { debounce.Stop(); Preview(); };
        if (!live && found.WirelessId != 0) Backups.Move(found.WirelessId.ToString("X4"), BackupId);

        if (!live)
        {
            backupsBtn = BackupsButton("keyboard's lighting");
            backupsBtn.Click += (s, e) => ShowBackups();
        }
        else if (kind.Profile)
        {
            backupsBtn = BackupsButton("keyboard's profile");
            backupsBtn.Click += (s, e) => ShowProfileBackups();
        }

        var pic = Ui.Add(this, new RoundedBox(), 20, 118, 616, 209);
        Ui.Text(pic, "Live on the keyboard", 14, 12, () => Theme.Dim, 9f, Ui.Semibold);
        picture = Ui.Add(pic, new KeyPicture(kind.Keys), 14, 36, 588, 161);
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
        var names = G515Profile.Effects.Select(d => d.Name).ToList();
        if (canScreen) names.Add("Screen");
        effects = Ui.Add(light, new SegmentedPicker { Items = names.ToArray(), AccessibleName = "Effect" }, 14, 58, 352, 32);
        effects.Picked += i =>
        {
            if (i == G515Profile.Effects.Length)
            {
                part = 0;
                bar = null;
                if (!Screening) { cur.Effect = ScreenEffect; ScreenOn(); }
                Changed(true);
                return;
            }
            if (Screening) ScreenOff();
            var d = G515Profile.Effects[i];
            var target = Target();
            target.Effect = d.Id;
            if (d.Speed && (target.Period < d.MinPeriod || target.Period > d.MaxPeriod)) target.Period = d.DefaultPeriod;
            Changed(true);
        };
        colorHead = Ui.Text(light, "Color", 14, 98, () => Theme.Dim, 9f);
        colors = new ColorRow(light, 115);
        colors.Picked += c => { Target().Color = c; Changed(true); };

        dirHead = Ui.Text(light, "Direction", 14, 156, () => Theme.Dim, 9f);
        dirs = Ui.Add(light, new SegmentedPicker { Items = DirText, AccessibleName = "Direction" }, 14, 174, 352, 32);
        dirs.Picked += i => { Target().Direction = Directions[i]; Changed(true); };

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
            var d = G515Profile.EffectById(target.Effect);
            target.Period = d.MaxPeriod - (d.MaxPeriod - d.MinPeriod) * speed.Value / 100;
            Changed(false);
        };

        foreach (Control c in light.Controls) if (c != lightHead && c != effectHead && c != effects) plainLight.Add(c);

        screenLight.Add(Ui.Text(light, "Monitor", 14, 98, () => Theme.Dim, 9f));
        monitorDrop = Ui.Add(light, new Dropdown { AccessibleName = "Monitor" }, 14, 116, 200, 30);
        monitorDrop.Picked += i =>
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
        screenLight.Add(monitorDrop);
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
            foreach (Control c in light.Controls) if (c != lightHead) c.Top += shift;
            light.Height += shift;
            perZone = Ui.Add(light, new Toggle { AccessibleName = "Lighting per zone" }, 14, 46, 34, 18);
            perZone.Changed += () => PerZone(perZone.Checked);
            var perZoneLabel = Ui.Text(light, "Lighting per zone", 56, 44, () => Theme.Text, 9f);
            perZone.Follow(perZoneLabel);
            partSeg = Ui.Add(light, new SegmentedPicker { Items = new[] { "Keys", "Light bar" }, AccessibleName = "Part" }, 190, 38, 176, 30);
            partSeg.Picked += i => { part = i + 1; RefreshControls(); };
        }
        foreach (var c in screenLight) c.Visible = false;
        picture.KeysPicked += picked =>
        {
            foreach (int k in picked) if (picture.IdOf(k) >= 0) setup.Keys[picture.IdOf(k)] = selectedZone;
            ScreenChanged();
        };

        screenBox = Ui.Add(this, new RoundedBox { Visible = false }, 412, 339, 224, Parts ? 316 : 272);
        Ui.Text(screenBox, "Screen", 14, 12, () => Theme.Dim, 9f, Ui.Semibold);
        Ui.Text(screenBox, "Color boost", 14, 36, () => Theme.Dim, 9f);
        boostValue = Value(screenBox, 14, 36, 196);
        boostSlider = Ui.Add(screenBox, new Slider { AccessibleName = "Color boost" }, 14, 54, 196, 20);
        boostSlider.Changed += (s, e) => { setup.Boost = boostSlider.Value; boostValue.Text = setup.Boost + "%"; force = true; Edited(); debounce.Stop(); debounce.Start(); };
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
        var noteText = Ui.Text(idle, "G-Lite can't save to this keyboard yet. It keeps your lighting and sets it again when G-Lite starts and when you open this screen. Picking None gives the keyboard its own lighting back." + (kind.Guessed ? " The key picture is a guess for this model." : ""), 14, 40, () => Theme.Dim, 9f);
        noteText.AutoSize = false;
        noteText.Size = new Size(196, 200);
        noteText.Visible = note;
        // The G915 TKL reads 0 as at once, not never (tried on an owner's keyboard), so its profile keyboards don't offer Never.
        never = kind.Profile ? 1 : 0;
        idleBox = Choice(idle, "Dim after", IdleText.Skip(never).ToArray(), 40, i => cur.Idle = IdleSecs[i + never]);
        dimBox = Choice(idle, "Dim to", DimText, 98, i => cur.Dim = DimPcts[i]);
        sleepBox = Choice(idle, "Turn off after", SleepText.Skip(never).ToArray(), 156, i => cur.Sleep = SleepSecs[i + never]);
        if (note) foreach (Control c in idle.Controls) if (c != noteText && c.Top > 30) c.Visible = false;

        saveBtn = Ui.Add(this, new FlatButton { Style = ButtonStyle.Primary, Text = "Save to keyboard", Visible = !note }, 496, 685, 140, 32);
        saveBtn.Click += (s, e) => Save();

        if (canScreen && Settings.Get(Active) == "1")
        {
            cur.Effect = ScreenEffect;
            ScreenOn();
        }
        RefreshControls();
        if (live) Load();
    }

    Lighting Target() { return part == 2 && bar != null ? bar : cur; }

    void PerZone(bool on)
    {
        if (on == (bar != null) || Screening) { RefreshControls(); return; }
        if (on)
        {
            bar = cur.Clone();
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

    string BarText(int b) { return bar == null ? "" : "#" + bar.ToText(b); }

    Lighting BarFrom(string[] pieces)
    {
        if (!Parts || pieces.Length < 2) return null;
        int ignored;
        var l = Lighting.FromText(pieces[1], out ignored);
        if (l.Effect == Ripple || l.Effect == ScreenEffect || G515Profile.EffectById(l.Effect) == null) l.Effect = 0x01;
        return l;
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
        saveBtn.Enabled = !broken && !Screening;
        picture.Display(Known() ? cur : null, Known() ? bar : null, brightness);
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
        if (claimed) RunOnDevice(h => { if (live) G915Lighting.Release(h); else G515Profile.Release(h); claimed = false; });
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
        if (!quiet) { ClaimLights(h); return; }
        if (found.Has(0x8101)) h.CallUnlogged(h.FeatureIndex(0x8101), 6, 0x05);
        h.CallUnlogged(h.FeatureIndex(0x8071), 5, 0x01, 0x03, 0x04);
        if (!found.Has(0x8101)) try { h.CallUnlogged(h.FeatureIndex(0x8071), 8, 0x01, 0x01, 0x00); } catch (HidppError) { }
    }

    void ClaimLights(Hidpp h)
    {
        if (found.Has(0x8101)) G515Profile.Claim(h);
        else G915Lighting.Claim(h);
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
            RunOnDevice(h =>
            {
                try
                {
                    if (release && claimed) { if (live) G915Lighting.Release(h); else G515Profile.Release(h); claimed = false; }
                    if (readBattery) ReadBattery(h);
                    statusDirty = true;
                    Worked();
                }
                catch (Exception e) { Failed(e); throw; }
                finally { sending = false; }
            }, null, msg => { });
            return;
        }
        if (released) { released = false; force = true; ClearStatus(); }
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
        bool send = changed, readLayout = layoutCode < 0 && !live && found.Has(0x4540);
        RunOnDevice(h =>
        {
            try
            {
                if (readLayout) { layoutCode = h.Call(h.FeatureIndex(0x4540), 0)[0]; layoutDirty = true; }
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
        var r = h.CallUnlogged(h.FeatureIndex(0x1004), 1);
        battery = r[0];
        charging = r[2] != 0;
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

    internal void Exiting()
    {
        if (Screening)
        {
            if (!claimed) return;
            RunOnDevice(h => { if (live) G915Lighting.Release(h); else G515Profile.Release(h); claimed = false; });
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

    protected override bool Unsaved { get { return cur != null && (live && kind.Profile ? dirty || Screening : live ? Known() : stored != null && (dirty || Screening)); } }

    protected override string Current() { return cur.ToText(brightness) + BarText(brightness) + (Screening ? "|" + setup.ToText() : ""); }

    protected override void ApplyPreset(string text)
    {
        int b;
        var parts = text.Split('|');
        var pieces = parts[0].Split('#');
        var l = Lighting.FromText(pieces[0], out b);
        if (l.Effect == ScreenEffect && !canScreen) l.Effect = 0x01;
        bar = l.Effect == ScreenEffect ? null : BarFrom(pieces);
        part = bar == null ? 0 : 1;
        bool was = Screening;
        if (parts.Length > 1) setup = ScreenSetup.FromText(parts[1]);
        cur = l;
        brightness = Math.Max(0, Math.Min(100, b));
        if (was && !Screening) ScreenOff();
        if (!was && Screening) ScreenOn();
        if (Screening) ScreenChanged();
        Changed(true);
    }

    Dropdown Choice(Control parent, string label, string[] items, int y, Action<int> set)
    {
        Ui.Text(parent, label, 14, y, () => Theme.Dim, 9f);
        var d = Ui.Add(parent, new Dropdown { AccessibleName = label }, 14, y + 18, 196, 32);
        d.Items.AddRange(items);
        d.Picked += i => { set(i); Changed(false); };
        return d;
    }

    bool Known() { return live ? !unknownStored : !(unknownStored && !dirty); }

    static int Nearest(int[] values, int v)
    {
        int best = 0;
        for (int i = 0; i < values.Length; i++) if (Math.Abs(values[i] - v) < Math.Abs(values[best] - v)) best = i;
        return best;
    }

    void RefreshControls()
    {
        loading = true;
        bool screening = Screening;
        foreach (var c in plainLight) c.Visible = !screening;
        foreach (var c in screenLight) c.Visible = screening;
        screenBox.Visible = screening;
        idleArea.Visible = !screening;
        if (picture.Live != screening) picture.Live = screening;
        var view = Viewed;
        var d = G515Profile.EffectById(view.Effect);
        bool known = Known();
        bool color = known && d != null && d.Color, dir = known && d != null && d.Direction, spd = known && d != null && d.Speed;
        lightHead.Text = known ? "Lighting" : broken ? "Lighting (half written on the keyboard)" : live ? "Lighting (as on the keyboard)" : "Lighting (factory animation)";
        effects.Index = known ? Array.FindIndex(G515Profile.Effects, e => e.Id == view.Effect) : -1;
        effects.Off = part == 2 ? new[] { Array.FindIndex(G515Profile.Effects, e => e.Id == Ripple) } : new int[0];
        effects.Invalidate();
        if (partSeg != null)
        {
            perZone.Checked = bar != null && !screening;
            perZone.Enabled = known && !screening;
            partSeg.Enabled = known && !screening && bar != null;
            partSeg.Index = bar != null && !screening ? part - 1 : -1;
            partSeg.Invalidate();
            picture.Part = screening || !known || bar == null ? 0 : part;
        }
        SetDimmed(colorHead, color);
        colors.RefreshControls(view.Color, color);
        SetDimmed(dirHead, dir);
        dirs.Enabled = dir;
        dirs.Index = dir ? Array.IndexOf(Directions, view.Direction) : -1;
        SetDimmed(speedHead, spd);
        speed.Enabled = spd;
        if (spd) speed.Value = (d.MaxPeriod - view.Period) * 100 / (d.MaxPeriod - d.MinPeriod);
        speedValue.Text = spd ? speed.Value + "%" : "";
        speed.Invalidate();
        bright.Value = brightness;
        brightValue.Text = brightness + "%";
        bright.Invalidate();
        idleBox.Index = Nearest(IdleSecs.Skip(never).ToArray(), cur.Idle);
        dimBox.Index = Nearest(DimPcts, cur.Dim);
        sleepBox.Index = Nearest(SleepSecs.Skip(never).ToArray(), cur.Sleep);
        dimBox.Enabled = cur.Idle > 0;
        saveBtn.Enabled = dirty && !broken;
        picture.Display(known ? cur : null, known ? bar : null, brightness);
        if (screening)
        {
            foreach (var c in plainLight) c.Visible = false;
            effects.Index = G515Profile.Effects.Length;
            lightHead.Text = "Lighting";
            saveBtn.Enabled = false;
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
            ShowStatus("Windows Dynamic Lighting is on and may override this.", true, () => Process.Start("ms-settings:personalization-lighting"), "Open Windows settings");
        else if (Screening)
        {
            var why = Paused();
            if (why != null) ShowStatus(why, true);
            else if (ScreenCapture.Missing(setup.Monitor)) ShowStatus("The chosen monitor isn't connected. Using the main monitor.", true);
            else ShowStatus("Live only.");
        }
        else if (live && kind.Profile) ShowStatus(dirty ? "Set by G-Lite, not saved to the keyboard" : Known() ? "Matches the keyboard" : "");
        else if (live) ShowStatus(Known() ? "Set by G-Lite, not saved to the keyboard" : "");
        else ShowStatus(dirty ? "Not saved to the keyboard" : stored == null ? "" : "Matches the keyboard");
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
        if (last != null) ShowStatus(half + " Last backup: " + When(last) + ".", true, () => Restore(last.Files, "the backup from " + When(last)), "Restore backup");
        else ShowStatus(half, true, () => Restore(new G515Profile.Files(), "the factory lighting"), "Restore factory lighting");
    }

    void ShowBackups()
    {
        BackupMenu(backupsBtn, BackupId, "Restore the factory lighting", () => Restore(new G515Profile.Files(), "the factory lighting"),
            e => Backups.Describe(e.Files), e => G515Profile.FilesAreValid(e.Files), e => Restore(e.Files, "the backup from " + When(e)));
    }

    void Restore(G515Profile.Files backup, string what)
    {
        debounce.Stop();
        previewSeq++;
        saveBtn.Enabled = false;
        ShowStatus("Restoring " + what + "…");
        LogUserAction("Restore " + what);
        int b = storedBrightness;
        RunOnDevice(h =>
        {
            Backups.Save(BackupId, G515Profile.Read(h), DeviceLabel);
            G515Profile.Restore(h, backup);
            claimed = false;
            if (b >= 0) G515Profile.SetBrightness(h, b);
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
            cur = stored != null ? stored.Clone() : new Lighting();
        }
        if (live) { Forget(); if (kind.Profile) Load(); return; }
        if (stored == null) { RefreshControls(); ClearStatus(); return; }
        cur = stored.Clone();
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
            int level = brightness;
            RunOnDevice(h => { if (level != G515Profile.Brightness(h)) G515Profile.SetBrightness(h, level); }, ClearStatus);
            return;
        }
        idleLevel = 0;
        var l = cur.Clone();
        int b = brightness, seq = ++previewSeq;
        bool repaint = leftScreen;
        leftScreen = false;
        if (live) { Apply(l, bar == null ? null : bar.Clone(), b, seq); return; }
        RunOnDevice(h =>
        {
            if (seq != previewSeq) return;
            if (b != G515Profile.Brightness(h)) G515Profile.SetBrightness(h, b);
            if (!claimed && !repaint && l.Same(stored)) return;
            if (!claimed) { G515Profile.Claim(h); claimed = true; }
            try { G515Profile.Preview(h, l); }
            catch (HidppError e) { if (e.Code != 5) throw; G515Profile.Claim(h); G515Profile.Preview(h, l); }
        }, () => { if (seq == previewSeq) ClearStatus(); });
    }

    void Save()
    {
        if (live) { SaveProfile(); return; }
        var l = cur.Clone();
        int b = brightness;
        saveBtn.Enabled = false;
        ShowStatus("Saving…");
        LogUserAction("Save to keyboard");
        RunOnDevice(h =>
        {
            var f = G515Profile.Snapshot(h);
            Backups.Save(BackupId, f, DeviceLabel);
            G515Profile.Store(h, f, l);
            claimed = false;
        }, () =>
        {
            stored = l;
            storedBrightness = b;
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
        var l = cur.Clone();
        var others = bar == null ? null : bar.Clone();
        int n = edits;
        debounce.Stop();
        previewSeq++;
        saveBtn.Enabled = false;
        ShowStatus("Saving…");
        LogUserAction("Save to keyboard");
        RunOnDevice(h =>
        {
            if (clusters == null) clusters = G915Lighting.Clusters(h);
            int sector;
            var p = G915Lighting.ReadProfile(h, out sector);
            var built = G915Lighting.Build(p, clusters, l, others);
            Backups.Save(ProfileBackups, new Dictionary<string, byte[]> { { sector.ToString("X4"), p } }, DeviceLabel);
            G915Lighting.Store(h, sector, built);
            claimed = false;
        }, () =>
        {
            Settings.Remove(Remembered);
            stored = l;
            unknownStored = false;
            if (n == edits) dirty = false;
            RefreshControls();
            ShowStatus("Saved to the keyboard");
        });
    }

    static int BackupSector(Backups.Entry e)
    {
        int sector;
        return e.Data.Count == 1 && int.TryParse(e.Data.Keys.First(), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out sector) && sector > 0 && sector < 0x100
            && MouseProtocol.Valid(e.Data.Values.First()) ? sector : -1;
    }

    void ShowProfileBackups()
    {
        BackupMenu(backupsBtn, ProfileBackups, "Restore the factory lighting", () => RestoreProfile(null),
            e =>
            {
                if (BackupSector(e) < 0) return "damaged, can't be restored";
                var l = G915Lighting.Decode(e.Data.Values.First(), clusters == null ? 1 : G915Lighting.Keys(clusters));
                return l == null ? "an effect G-Lite doesn't know" : G515Profile.EffectById(l.Effect).Name;
            },
            e => BackupSector(e) > 0, RestoreProfile);
    }

    // A null backup restores the factory lighting.
    void RestoreProfile(Backups.Entry e)
    {
        int sector = e == null ? -1 : BackupSector(e);
        var backup = e == null ? null : e.Data.Values.First();
        string what = e == null ? "the factory lighting" : "the backup from " + When(e);
        debounce.Stop();
        previewSeq++;
        saveBtn.Enabled = false;
        ShowStatus("Restoring " + what + "…");
        LogUserAction("Restore " + what);
        RunOnDevice(h =>
        {
            int now;
            var current = G915Lighting.ReadProfile(h, out now);
            if (backup != null && now != sector) throw new RefusedException("The keyboard runs another profile than the one in this backup");
            var p = backup ?? G915Lighting.FactoryLighting(h, now, current);
            Backups.Save(ProfileBackups, new Dictionary<string, byte[]> { { now.ToString("X4"), current } }, DeviceLabel);
            G915Lighting.Store(h, now, p);
            claimed = false;
        }, () =>
        {
            Settings.Remove(Remembered);
            Note = "Restored " + what;
            dirty = false;
            Load();
        });
    }

    void Apply(Lighting l, Lighting others, int b, int seq)
    {
        bool known = !unknownStored, sent = false;
        RunOnDevice(h =>
        {
            if (seq != previewSeq) return;
            if (clusters == null) clusters = G915Lighting.Clusters(h);
            if (b != G515Profile.Brightness(h)) G515Profile.SetBrightness(h, b);
            if (!known) return;
            ClaimLights(h);
            G915Lighting.Preview(h, clusters, l, others);
            sent = true;
        }, () =>
        {
            if (sent && seq == previewSeq) Settings.Set(Remembered, l.ToText(b) + (others == null ? "" : "#" + others.ToText(b)));
            if (seq == previewSeq) ClearStatus();
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
        int seq = ++previewSeq;
        debounce.Stop();
        RefreshControls();
        ClearStatus();
        int b = -1;
        RunOnDevice(h => { G915Lighting.Release(h); b = G515Profile.Brightness(h); }, () => { if (seq == previewSeq && b >= 0) { brightness = b; RefreshControls(); } });
    }

    void LoadLive()
    {
        dirty = false;
        debounce.Stop();
        ShowStatus("Reading the keyboard…");
        int n = edits, seq = previewSeq;
        int b = 100;
        string kept = Settings.Get(Remembered);
        Lighting l = null, lb = null;
        int kb = -1;
        if (kept != null) try { var pieces = kept.Split('#'); l = Lighting.FromText(pieces[0], out kb); lb = BarFrom(pieces); } catch (Exception) { l = null; lb = null; }
        bool dropped = false;
        bool screening = Screening, readLayout = layoutCode < 0 && found.Has(0x4540);
        Lighting saved = null;
        RunOnDevice(h =>
        {
            if (readLayout) layoutCode = h.Call(h.FeatureIndex(0x4540), 0)[0];
            clusters = G915Lighting.Clusters(h);
            // Only when nothing is remembered: a remembered lighting replaces what's saved. Optional, so any failure leaves the
            // screen on live lighting as before.
            if (kind.Profile && l == null)
                try { int sector; saved = G915Lighting.Decode(G915Lighting.ReadProfile(h, out sector), G915Lighting.Keys(clusters)); }
                catch (Exception) { }
            if (l != null && seq == previewSeq && !screening)
            {
                if (kb >= 0 && kb <= 100 && kb != G515Profile.Brightness(h)) G515Profile.SetBrightness(h, kb);
                ClaimLights(h);
                try { G915Lighting.Preview(h, clusters, l, lb); }
                catch (RefusedException) { G915Lighting.Release(h); dropped = true; }
            }
            b = G515Profile.Brightness(h);
        }, () =>
        {
            if (dropped) { Settings.Remove(Remembered); l = null; }
            storedBrightness = b;
            if (n == edits)
            {
                dirty = l != null && kind.Profile;
                if (l == null && saved != null) l = saved;
                stored = l == null ? null : l.Clone();
                unknownStored = l == null;
                brightness = b;
                if (l != null && !Screening) { cur = l; bar = lb; part = lb == null ? 0 : 1; }
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
        int n = edits;
        Lighting l = null;
        int b = 100;
        bool unknown = false, half = false, readLayout = layoutCode < 0 && found.Has(0x4540);
        RunOnDevice(h =>
        {
            if (readLayout) layoutCode = h.Call(h.FeatureIndex(0x4540), 0)[0];
            G515Profile.Files f;
            try { f = G515Profile.Snapshot(h); }
            catch (HalfWrittenException) { half = true; b = G515Profile.Brightness(h); return; }
            l = G515Profile.Decode(f.UserProfile ?? f.FactoryProfile);
            unknown = l == null;
            if (l == null) l = new Lighting();
            b = G515Profile.Brightness(h);
        }, () =>
        {
            ApplyLayout();
            broken = half;
            fix = half ? Backups.List(BackupId).FirstOrDefault(e => G515Profile.FilesAreValid(e.Files)) : null;
            storedBrightness = b;
            stored = half ? null : l.Clone();
            unknownStored = half || unknown;
            if (n == edits && !editing)
            {
                brightness = b;
                if (!half && !Screening) cur = l;
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
        var l = cur.Clone();
        int b = brightness;
        RunOnDevice(h =>
        {
            try
            {
                if (!claimed || h.CallUnlogged(h.FeatureIndex(0x8071), 5, 0x00)[1] != 0) return;
                G515Profile.Claim(h);
                claimed = true;
                if (b != G515Profile.Brightness(h)) G515Profile.SetBrightness(h, b);
                Show(h, l, idleLevel);
            }
            catch (HidppError) { }
            catch (TimeoutException) { }
            finally { watching = false; }
        }, null, message => { });
    }

    void Idle()
    {
        int idle = WindowHelpers.IdleSeconds();
        int level = cur.Sleep > 0 && idle >= cur.Sleep ? 2 : cur.Idle > 0 && idle >= cur.Idle ? 1 : 0;
        if (level == idleLevel) return;
        idleLevel = level;
        var l = cur.Clone();
        RunOnDevice(h =>
        {
            if (!claimed) return;
            try { Show(h, l, level); }
            catch (HidppError) { }
            catch (TimeoutException) { }
        }, null, message => { });
    }

    static void Show(Hidpp h, Lighting l, int level)
    {
        if (level == 2) G515Profile.Preview(h, new Lighting { Effect = 0 });
        else G515Profile.Preview(h, l, level == 1 ? l.Dim : 100);
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
        bool repaint = leftScreen && back != null;
        leftScreen = false;
        previewSeq++;
        RunOnDevice(h =>
        {
            if (b >= 0) G515Profile.SetBrightness(h, b);
            if (!claimed && !repaint) return;
            if (!claimed) G515Profile.Claim(h);
            try { if (back != null) G515Profile.Preview(h, back); }
            catch (HidppError e) { if (e.Code != 5) throw; }
            G515Profile.Release(h);
            claimed = false;
        });
    }
}
