using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Drawing.Drawing2D;
using System.Threading;
using System.Windows.Forms;
using Timer = System.Windows.Forms.Timer;

class LightRange
{
    public int Min, Max, Step;

    public int Snap(int value) { return Math.Max(Min, Math.Min(Max, Min + (int)Math.Round((value - Min) / (double)Step) * Step)); }
}

class LightState
{
    public const int Zones = 7, Static = 0, Breathe = 1, Cycle = 2, Wave = 3;
    public static readonly string[] Effects = { "Static", "Breathe", "Cycle", "Wave" };
    public const int MinPeriod = 1000, MaxPeriod = 20000;
    public bool FrontOn, BackOn = true;
    public int Lumen, Kelvin, BackLevel = 100, Effect, Period = 5000;
    public int[] Colors = Enumerable.Repeat(0xFFFFFF, Zones).ToArray();

    public LightState Clone()
    {
        var copy = (LightState)MemberwiseClone();
        copy.Colors = (int[])Colors.Clone();
        return copy;
    }

    public string FrontText() { return (FrontOn ? 1 : 0) + "," + Lumen + "," + Kelvin; }

    public string BackText() { return (BackOn ? 1 : 0) + "," + BackLevel + "," + string.Join("/", Colors.Select(c => c.ToString("X6"))) + "," + Effect + "," + Period; }

    public bool ReadFront(string text)
    {
        var v = (text ?? "").Split(',');
        int lumen, kelvin;
        if (v.Length != 3 || !int.TryParse(v[1], out lumen) || !int.TryParse(v[2], out kelvin)) return false;
        FrontOn = v[0] == "1";
        Lumen = lumen;
        Kelvin = kelvin;
        return true;
    }

    public bool ReadBack(string text)
    {
        var v = (text ?? "").Split(',');
        int level, effect = Static, period = 5000;
        if ((v.Length != 3 && v.Length != 5) || !int.TryParse(v[1], out level)) return false;
        if (v.Length == 5 && (!int.TryParse(v[3], out effect) || !int.TryParse(v[4], out period) || effect < 0 || effect >= Effects.Length)) return false;
        var colors = v[2].Split('/');
        if (colors.Length != Zones) return false;
        var parsed = new int[Zones];
        for (int i = 0; i < Zones; i++)
            if (!int.TryParse(colors[i], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out parsed[i])) return false;
        BackOn = v[0] == "1";
        BackLevel = Math.Max(1, Math.Min(100, level));
        Colors = parsed.Select(c => c & 0xFFFFFF).ToArray();
        Effect = effect;
        Period = Math.Max(MinPeriod, Math.Min(MaxPeriod, period));
        return true;
    }
}

static class Litra
{
    static byte Front(Hidpp h) { return h.F(0x1990); }

    public static bool FrontOn(Hidpp h) { return (h.Call(Front(h), 0)[0] & 1) != 0; }

    public static void SetFrontOn(Hidpp h, bool on) { h.Call(Front(h), 1, (byte)(on ? 1 : 0)); }

    public static LightRange Range(Hidpp h, int fn, int low, int high, int step, int lowest, int highest)
    {
        var r = h.Call(Front(h), fn);
        int min = r[1] << 8 | r[2], max = r[3] << 8 | r[4], res = r[5] << 8 | r[6];
        if (min < lowest || max > highest || max <= min) return new LightRange { Min = low, Max = high, Step = step };
        return new LightRange { Min = min, Max = max, Step = res > 0 && res <= (max - min) ? res : 1 };
    }

    public static LightRange Lumens(Hidpp h) { return Range(h, 2, 30, 400, 1, 1, 5000); }

    public static LightRange Kelvins(Hidpp h) { return Range(h, 7, 2700, 6500, 100, 1000, 12000); }

    public static int Get(Hidpp h, int fn) { var r = h.Call(Front(h), fn); return r[0] << 8 | r[1]; }

    public static void Set(Hidpp h, int fn, int value) { h.Call(Front(h), fn, (byte)(value >> 8), (byte)value); }

    public static int BackLevel(Hidpp h) { var r = h.Call(h.F(0x8040), 1); return r[0] << 8 | r[1]; }

    public static void SetBackLevel(Hidpp h, int level) { h.Call(h.F(0x8040), 2, 0, (byte)level); }

    public static bool? BackOn(Hidpp h)
    {
        try { return (h.Call(h.F(0x8040), 3)[0] & 1) != 0; }
        catch (HidppError) { return null; }
    }

    public static void SetBackOn(Hidpp h, bool on)
    {
        try { h.Call(h.F(0x8040), 4, (byte)(on ? 1 : 0)); }
        catch (HidppError) { }
    }

    public static void Colors(Hidpp h, int[] colors, bool quiet)
    {
        byte feature = h.F(0x8081);
        for (int start = 0; start < colors.Length; start += 4)
        {
            var parameters = new List<byte>();
            for (int i = start; i < start + 4; i++)
            {
                if (i >= colors.Length) { parameters.AddRange(new byte[] { 0xFF, 0, 0, 0 }); continue; }
                int c = colors[i];
                parameters.Add((byte)(i + 1));
                parameters.AddRange(new[] { c >> 16 & 0xFF, c >> 8 & 0xFF, c & 0xFF }.Select(v => (byte)Math.Max(1, v)));
            }
            if (quiet) h.Quiet(feature, 1, parameters.ToArray()); else h.Call(feature, 1, parameters.ToArray());
        }
        if (quiet) h.Quiet(feature, 7, 0, 0, 1, 0, 0); else h.Call(feature, 7, 0, 0, 1, 0, 0);
    }

    public static string EffectName(byte id)
    {
        switch (id)
        {
            case 0x00: return "Off";
            case 0x01: return "Static";
            case 0x03: return "Colour cycle";
            case 0x04: return "Colour wave";
            case 0x0A: return "Breathe";
            case 0x13: return "Streaming";
            case 0x15: return "Smooth colour cycle";
            case 0x16: return "Smooth colour wave";
            case 0x18: return "Two colours";
        }
        return "Effect " + id.ToString("X2");
    }

    public static byte[] EffectBytes(int group, int index, byte id, LightState state)
    {
        var b = new byte[13];
        b[0] = (byte)group;
        b[1] = (byte)index;
        int color = state.Colors[0], other = state.Colors[LightState.Zones - 1], period = state.Period;
        if (id != 0x03 && id != 0x04 && id != 0x15 && id != 0x16) { b[2] = (byte)(color >> 16); b[3] = (byte)(color >> 8); b[4] = (byte)color; }
        switch (id)
        {
            case 0x01: b[5] = 2; break;
            case 0x0A: b[5] = (byte)(period >> 8); b[6] = (byte)period; b[8] = 100; break;
            case 0x03: case 0x15: b[7] = (byte)(period >> 8); b[8] = (byte)period; b[9] = 100; break;
            case 0x04: case 0x16: b[8] = (byte)period; b[9] = 1; b[10] = 100; b[11] = (byte)(period >> 8); break;
            case 0x18: b[5] = (byte)(other >> 16); b[6] = (byte)(other >> 8); b[7] = (byte)other; b[9] = 50; b[10] = 100; break;
        }
        b[12] = 1;
        return b;
    }

    public static void TryEffect(Hidpp h, int group, int index, byte id, LightState state) { h.Call(h.F(0x8071), 1, EffectBytes(group, index, id, state)); }

    public static int[] Frame(LightState state, double ms)
    {
        double phase = ms % state.Period / state.Period;
        var frame = new int[LightState.Zones];
        for (int z = 0; z < frame.Length; z++)
        {
            if (state.Effect == LightState.Breathe) frame[z] = Scale(state.Colors[z], 0.5 - 0.5 * Math.Cos(2 * Math.PI * phase));
            else if (state.Effect == LightState.Cycle) frame[z] = Hue(phase);
            else if (state.Effect == LightState.Wave) frame[z] = Hue(phase - z / (double)frame.Length);
            else frame[z] = state.Colors[z];
        }
        return frame;
    }

    static int Scale(int color, double k)
    {
        return (int)Math.Round((color >> 16 & 0xFF) * k) << 16 | (int)Math.Round((color >> 8 & 0xFF) * k) << 8 | (int)Math.Round((color & 0xFF) * k);
    }

    public static int Hue(double turn)
    {
        double h = (turn % 1 + 1) % 1 * 6;
        int sector = (int)h % 6;
        int rise = (int)Math.Round((h - Math.Floor(h)) * 255), fall = 255 - rise;
        var rgb = new[] { new[] { 255, rise, 0 }, new[] { fall, 255, 0 }, new[] { 0, 255, rise }, new[] { 0, fall, 255 }, new[] { rise, 0, 255 }, new[] { 255, 0, fall } }[sector];
        return rgb[0] << 16 | rgb[1] << 8 | rgb[2];
    }

    public static List<Zone> ScreenZones()
    {
        return Enumerable.Range(0, LightState.Zones).Select(i => new Zone { Name = (i + 1).ToString(), Mark = ScreenSetup.Marks[i], Area = new RectangleF((float)Math.Round(i / 7.0 + 0.01, 3), 0.1f, (float)Math.Round(1 / 7.0 - 0.02, 3), 0.8f) }).ToList();
    }

    public static ScreenSetup Screen(string text)
    {
        var setup = ScreenSetup.FromText(text);
        if (string.IsNullOrEmpty(text) || setup.Zones.Count != LightState.Zones) setup.Zones = ScreenZones();
        return setup;
    }
}

class LightPicture : Ctl
{
    public event Action<int> Picked;
    bool frontOn, backOn, back;
    double front, level;
    int kelvin = 4000, selected;
    int[] colors;

    public LightPicture()
    {
        TabStop = false;
        AccessibleRole = AccessibleRole.Graphic;
        AccessibleName = "Light";
    }

    public void Display(bool hasBack, bool frontLit, double frontLevel, int frontKelvin, bool backLit, double backLevel, int[] zones, int pick)
    {
        back = hasBack;
        frontOn = frontLit;
        front = frontLevel;
        kelvin = frontKelvin;
        backOn = backLit;
        level = backLevel;
        colors = zones;
        selected = pick;
        Cursor = selected >= 0 ? Cursors.Hand : Cursors.Default;
        Invalidate();
    }

    RectangleF Bar { get { float w = Width * 0.64f, h = w / 12; return new RectangleF((Width - w) / 2, Height - h - 4 * K, w, h); } }

    static Color Warmth(int k)
    {
        double t = Math.Max(0, Math.Min(1, (k - 2700) / 3800.0));
        return Color.FromArgb((int)(255 - 10 * t), (int)(214 + 36 * t), (int)(170 + 85 * t));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = Begin(e);
        var bar = Bar;
        var zones = colors;
        var housing = new RectangleF(bar.X + bar.Height * 0.3f, bar.Y - bar.Height * 0.3f, bar.Width - bar.Height * 0.6f, bar.Height);
        if (back && backOn && zones != null)
        {
            float w = housing.Width / zones.Length;
            for (int z = 0; z < zones.Length; z++)
            {
                if (zones[z] < 0) continue;
                var glow = new RectangleF(housing.X + z * w - w * 0.35f, 0, w * 1.7f, housing.Y * 2 + bar.Height * 0.4f);
                using (var path = new GraphicsPath())
                {
                    path.AddEllipse(glow);
                    using (var brush = new PathGradientBrush(path))
                    {
                        int alpha = (int)(255 * Math.Max(0.35, level));
                        brush.CenterColor = Color.FromArgb(alpha, zones[z] >> 16 & 0xFF, zones[z] >> 8 & 0xFF, zones[z] & 0xFF);
                        brush.SurroundColors = new[] { Color.FromArgb(0, zones[z] >> 16 & 0xFF, zones[z] >> 8 & 0xFF, zones[z] & 0xFF) };
                        g.FillPath(brush, path);
                    }
                }
            }
        }
        Ui.Fill(g, housing, bar.Height / 2, Color.FromArgb(24, 24, 26), Color.FromArgb(24, 24, 26));
        var face = frontOn ? Ui.Mix(Color.FromArgb(110, 110, 110), Warmth(kelvin), 0.55 + 0.45 * front) : Theme.KeyOff;
        Ui.Fill(g, bar, bar.Height / 2, face, Theme.Border);
        if (back && selected > 0 && zones != null)
        {
            float w = bar.Width / zones.Length;
            using (var p = new Pen(Theme.Accent, 2 * K))
            using (var path = Ui.Round(new RectangleF(bar.X + (selected - 1) * w + 2 * K, bar.Bottom + 5 * K, w - 4 * K, 3 * K), 1.5f * K))
                g.DrawPath(p, path);
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        var zones = colors;
        if (!back || selected < 0 || zones == null || e.Button != MouseButtons.Left || Picked == null) return;
        var bar = Bar;
        int z = (int)((e.X - bar.X) / (bar.Width / zones.Length));
        if (z >= 0 && z < zones.Length) Picked(z + 1);
    }
}

class PageLight : DevicePage
{
    LightState cur;
    volatile LightState sent;
    LightRange lumens = new LightRange { Min = 30, Max = 400, Step = 1 }, kelvins = new LightRange { Min = 2700, Max = 6500, Step = 100 };
    bool loading, ready, colorsChosen, screening, capturing, framing, snapping, trying;
    List<byte[]> groups;
    readonly Btn tryBtn;
    readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
    int seq, picked, selectedZone;
    int[] shown;
    volatile int[] framed;
    DateTime nextRefresh;
    ScreenSetup setup;
    readonly bool back;
    readonly Timer debounce = new Timer { Interval = 120 }, screenTimer = new Timer { Interval = 100 }, animTimer = new Timer { Interval = 50 }, snapTimer = new Timer { Interval = 1000 };

    internal override bool Busy { get { return debounce.Enabled; } }

    readonly Label frontHead, lumenValue, kelvinValue, levelValue, speedHead, speedValue, zoneHead, colorHead, boostValue, rateValue;
    readonly Toggle frontPower, backPower, smoothToggle, lockToggle;
    readonly Slider lumen, kelvin, level, speed, boost, rate;
    readonly Seg zoneSeg, effects;
    readonly ColorRow colors;
    readonly Box liveBox, frontBox, backBox, areas, settingsBox;
    readonly Label liveHead;
    readonly LightPicture picture;
    string tried;
    readonly Drop monitorDrop;
    readonly ScreenPreview preview;

    string Remembered { get { return "lighting." + found.Slug; } }
    string Saved { get { return "screen." + found.Slug; } }
    string Active { get { return "screen.on." + found.Slug; } }

    public PageLight(Found found, Hidpp fake, MouseEventHandler drag) : base(found, fake, drag, found.Short, "light", "profile." + found.Slug + ".")
    {
        back = found.Has(0x8081);
        debounce.Tick += (s, e) => { debounce.Stop(); Send(); };
        setup = Litra.Screen(Settings.Get(Saved));

        liveBox = Ui.Add(this, new Box(), 20, 118, 616, 96);
        liveHead = Ui.Text(liveBox, "Live on the light", 14, 12, () => Theme.Dim, 9f, Ui.Semi);
        picture = Ui.Add(liveBox, new LightPicture(), 14, 30, 588, 58);
        picture.Picked += i => { if (zoneSeg.Enabled) { picked = i; Sync(); } };
        var front = frontBox = Ui.Add(this, new Box(), 20, 224, 616, 86);
        frontHead = Ui.Text(front, "Front light", 14, 12, () => Theme.Dim, 9f, Ui.Semi);
        frontPower = Power(front, 14, 36, "Front light on", on => Edit(l => l.FrontOn = on, true));
        Ui.Text(front, "Brightness", 110, 36, () => Theme.Dim, 9f);
        lumenValue = Value(front, 110, 36, 230);
        lumen = Ui.Add(front, new Slider { AccessibleName = "Front brightness" }, 110, 54, 230, 24);
        lumen.Changed += (s, e) => { int lm = lumens.Snap(lumens.Min + (lumens.Max - lumens.Min) * lumen.Value / 100); Edit(l => l.Lumen = lm, false); };
        Ui.Text(front, "Color temperature", 366, 36, () => Theme.Dim, 9f);
        kelvinValue = Value(front, 366, 36, 236);
        kelvin = Ui.Add(front, new Slider { AccessibleName = "Color temperature", Step = 1 }, 366, 54, 236, 24);
        kelvin.Changed += (s, e) => { int k = kelvins.Snap(kelvins.Min + kelvin.Value * kelvins.Step); Edit(l => l.Kelvin = k, false); };

        backBox = Ui.Add(this, new Box { Visible = back }, 20, 320, 616, 268);
        Ui.Text(backBox, "Back light", 14, 12, () => Theme.Dim, 9f, Ui.Semi);
        if (back && found.Has(0x8071))
        {
            tryBtn = Ui.Add(backBox, new Btn { Text = "Try effect", AccessibleName = "Try the light's own effects" }, 490, 6, 112, 28);
            tryBtn.Click += (s, e) => TryMenu();
        }
        backPower = Power(backBox, 14, 36, "Back light on", on => Edit(l => l.BackOn = on, true));
        Ui.Text(backBox, "Brightness", 110, 36, () => Theme.Dim, 9f);
        levelValue = Value(backBox, 110, 36, 230);
        level = Ui.Add(backBox, new Slider { AccessibleName = "Back brightness" }, 110, 54, 230, 24);
        level.Changed += (s, e) => { int b = Math.Max(1, level.Value); Edit(l => l.BackLevel = b, false); };
        speedHead = Ui.Text(backBox, "Speed", 366, 36, () => Theme.Dim, 9f);
        speedValue = Value(backBox, 366, 36, 236);
        speed = Ui.Add(backBox, new Slider { AccessibleName = "Speed" }, 366, 54, 236, 24);
        speed.Changed += (s, e) => { int period = LightState.MaxPeriod - (LightState.MaxPeriod - LightState.MinPeriod) * speed.Value / 100; Edit(l => l.Period = period, false); };
        Ui.Text(backBox, "Effect", 14, 88, () => Theme.Dim, 9f);
        effects = Ui.Add(backBox, new Seg { Items = LightState.Effects.Concat(new[] { "Screen" }).ToArray(), AccessibleName = "Effect" }, 14, 106, 588, 30);
        effects.Picked += i =>
        {
            if (i == LightState.Effects.Length) { if (!screening) ScreenOn(); return; }
            if (screening) ScreenOff();
            Edit(l => { colorsChosen = true; l.Effect = i; }, true);
        };
        zoneHead = Ui.Text(backBox, "Zone", 14, 146, () => Theme.Dim, 9f);
        zoneSeg = Ui.Add(backBox, new Seg { Items = new[] { "All" }.Concat(Enumerable.Range(1, LightState.Zones).Select(i => i.ToString())).ToArray(), AccessibleName = "Zone", Index = 0 }, 14, 164, 588, 30);
        zoneSeg.Picked += i => { picked = i; Sync(); };
        colorHead = Ui.Text(backBox, "Color", 14, 204, () => Theme.Dim, 9f);
        colors = new ColorRow(backBox, 221);
        colors.Picked += c => Edit(l =>
        {
            colorsChosen = true;
            for (int z = 0; z < LightState.Zones; z++) if (picked == 0 || picked == z + 1) l.Colors[z] = c;
        }, true);

        areas = Ui.Add(this, new Box { Visible = false }, 20, 372, 358, 240);
        settingsBox = Ui.Add(this, new Box { Visible = false }, 390, 372, 246, 240);
        Ui.Text(areas, "Screen areas", 14, 12, () => Theme.Dim, 9f);
        var frame = Ui.Add(areas, new Box(), 14, 34, 330, 158);
        preview = Ui.Add(frame, new ScreenPreview(), 8, 8, 314, 142);
        preview.Picked += i => { selectedZone = i; preview.Selected = i; preview.Invalidate(); };
        preview.Done += ScreenChanged;
        var resetAreas = Ui.Add(areas, new Btn { Text = "Reset areas", AccessibleName = "Reset the screen areas" }, 14, 200, 110, 30);
        resetAreas.Click += (s, e) => { setup.Zones = Litra.ScreenZones(); selectedZone = 0; ScreenChanged(); };
        Ui.Text(settingsBox, "Monitor", 14, 12, () => Theme.Dim, 9f);
        monitorDrop = Ui.Add(settingsBox, new Drop { AccessibleName = "Monitor" }, 14, 34, 218, 30);
        monitorDrop.Picked += i =>
        {
            var all = Screen.AllScreens;
            if (i < 0 || i >= all.Length) return;
            setup.Monitor = all[i].Primary ? "" : all[i].DeviceName;
            ScreenChanged();
            Snap();
        };
        Ui.Text(settingsBox, "Color boost", 14, 72, () => Theme.Dim, 9f);
        boostValue = Value(settingsBox, 14, 72, 218);
        boost = Ui.Add(settingsBox, new Slider { AccessibleName = "Color boost" }, 14, 90, 218, 24);
        boost.Changed += (s, e) => { setup.Boost = boost.Value; boostValue.Text = setup.Boost + "%"; ScreenChanged(); };
        Ui.Text(settingsBox, "Update rate", 14, 118, () => Theme.Dim, 9f);
        rateValue = Value(settingsBox, 14, 118, 218);
        rate = Ui.Add(settingsBox, new Slider { AccessibleName = "Update rate", Max = 9, Step = 1 }, 14, 136, 218, 24);
        rate.Changed += (s, e) =>
        {
            setup.Interval = 1000 - rate.Value * 100;
            rateValue.Text = setup.Interval + " ms";
            screenTimer.Interval = setup.Interval;
            ScreenChanged();
        };
        smoothToggle = Option(settingsBox, "Smooth changes", 14, 168, 20, () => setup.Smooth = !setup.Smooth);
        lockToggle = Option(settingsBox, "Pause when screen off or locked", 14, 194, 36, () => setup.PauseWhenLocked = !setup.PauseWhenLocked);

        screenTimer.Tick += (s, e) => Tick();
        snapTimer.Tick += (s, e) => Snap();
        animTimer.Tick += (s, e) => Animate();

        if (back && Settings.Get(Active) == "1") ScreenOn();
        Sync();
        if (back && (screening || Settings.Get(Remembered) != "")) Load();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            debounce.Dispose();
            screenTimer.Dispose();
            snapTimer.Dispose();
            animTimer.Dispose();
        }
        base.Dispose(disposing);
    }

    Toggle Power(Control parent, int x, int y, string name, Action<bool> changed)
    {
        Ui.Text(parent, "Power", x, y, () => Theme.Dim, 9f);
        var toggle = Ui.Add(parent, new Toggle { AccessibleName = name }, x, y + 21, 34, 18);
        toggle.Changed += () => changed(toggle.Checked);
        return toggle;
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

    void TryMenu()
    {
        var list = groups;
        if (list == null) return;
        var m = new ContextMenuStrip { Renderer = Ui.Menus, Font = Ui.Get(9.75f, Ui.Regular), ShowImageMargin = false };
        m.BackColor = Theme.Card;
        m.ForeColor = Theme.Text;
        for (int g = 0; g < list.Count; g++)
        {
            for (int i = 0; i < list[g].Length; i++)
            {
                int group = g, index = i;
                byte id = list[g][i];
                m.Items.Add(new ToolStripMenuItem("Group " + g + " · " + Litra.EffectName(id), null, (s, e) => TryEffect(group, index, id)));
            }
            m.Items.Add(new ToolStripSeparator());
        }
        m.Items.Add(new ToolStripMenuItem("Back to G-Lite's colours", null, (s, e) => StopTrying()));
        m.Closed += (s, a) => BeginInvoke((Action)m.Dispose);
        if (Ui.Popped != null) { Ui.Popped(m); return; }
        m.Show(tryBtn, new Point(tryBtn.Width, tryBtn.Height + (int)(2 * Ui.K)), ToolStripDropDownDirection.BelowLeft);
    }

    void TryEffect(int group, int index, byte id)
    {
        if (!ready || cur == null) return;
        string name = "Group " + group + " · " + Litra.EffectName(id);
        trying = true;
        tried = name;
        Sync();
        Did("Try effect " + name);
        var state = cur.Clone();
        Do(h => Litra.TryEffect(h, group, index, id, state), () => Say("Sent " + name + ". What lit up? Please say in your issue."));
    }

    void StopTrying()
    {
        if (!ready) return;
        trying = false;
        colorsChosen = true;
        framed = null;
        Did("Back to G-Lite's colours");
        Do(h => { var was = sent; if (was != null) was.Colors = Enumerable.Repeat(-1, LightState.Zones).ToArray(); });
        Sync();
        Send();
    }

    void Edit(Action<LightState> change, bool now)
    {
        if (loading || !ready) return;
        if (trying) { trying = false; framed = null; Do(h => { var was = sent; if (was != null) was.Colors = Enumerable.Repeat(-1, LightState.Zones).ToArray(); }); }
        change(cur);
        seq++;
        Edited();
        if (now) Sync(); else Values();
        debounce.Stop();
        if (now) Send(); else debounce.Start();
    }

    public override void Restyle() { Sync(); }

    protected override string Current() { return cur == null ? "" : "front:" + cur.FrontText() + (back ? ";back:" + cur.BackText() : ""); }

    protected override void Use(string text)
    {
        if (cur == null) return;
        var next = cur.Clone();
        foreach (var part in text.Split(';'))
        {
            if (part.StartsWith("front:")) next.ReadFront(part.Substring(6));
            if (part.StartsWith("back:") && back) next.ReadBack(part.Substring(5));
        }
        next.Lumen = lumens.Snap(next.Lumen);
        next.Kelvin = kelvins.Snap(next.Kelvin);
        if (screening) ScreenOff();
        colorsChosen = true;
        cur = next;
        seq++;
        Edited();
        Sync();
        Send();
    }

    protected override void Revert() { }

    protected override void Lost() { ready = false; sent = null; framed = null; }

    void ScreenOn()
    {
        screening = true;
        Settings.Set(Active, "1");
        Settings.Set(Saved, setup.ToText());
        shown = null;
        framed = null;
        Did("Screen effect on");
        screenTimer.Interval = setup.Interval;
        screenTimer.Start();
        snapTimer.Start();
        Snap();
        Sync();
        Calm();
    }

    void ScreenOff()
    {
        screening = false;
        screenTimer.Stop();
        snapTimer.Stop();
        Settings.Remove(Active);
        shown = null;
        framed = null;
        colorsChosen = true;
        if (ready) Do(h => { var was = sent; if (was != null) was.Colors = Enumerable.Repeat(-1, LightState.Zones).ToArray(); });
        Did("Screen effect off");
        Sync();
        Send();
    }

    void ScreenChanged()
    {
        if (loading) return;
        Settings.Set(Saved, setup.ToText());
        framed = null;
        preview.Zones = setup.Zones;
        preview.Selected = selectedZone;
        preview.Invalidate();
    }

    bool Animated { get { return back && ready && !screening && !trying && cur != null && cur.Effect != LightState.Static; } }

    void Animation()
    {
        bool on = Animated && !Manual;
        if (on && !animTimer.Enabled) animTimer.Start();
        if (!on && animTimer.Enabled) animTimer.Stop();
    }

    void Animate()
    {
        if (IsDisposed || !Animated) return;
        var frame = Litra.Frame(cur, clock.Elapsed.TotalMilliseconds);
        Show(frame);
        if (framing) return;
        var last = framed;
        if (last != null && frame.SequenceEqual(last)) return;
        framing = true;
        Do(h =>
        {
            try { Litra.Colors(h, frame, true); framed = frame; }
            finally { framing = false; }
        }, null, msg => { framed = null; });
    }

    void Tick()
    {
        if (IsDisposed || !screening || !ready || capturing || framing) return;
        if (setup.PauseWhenLocked && (Presence.Locked || Presence.DisplayOff)) return;
        capturing = true;
        var monitor = ScreenCapture.Find(setup.Monitor).Bounds;
        var zones = setup.Zones.ToList();
        var ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        ThreadPool.QueueUserWorkItem(state =>
        {
            int[] sampled = null;
            try { sampled = ScreenCapture.Sample(monitor, zones); } catch (Exception) { }
            try { ui.Post(posted => { capturing = false; if (sampled != null && !IsDisposed && screening) Sampled(sampled); }, null); }
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
        for (int z = 0; z < sampled.Length; z++)
        {
            int target = ScreenCapture.Boost(sampled[z], setup.Boost);
            shown[z] = setup.Smooth && !fresh ? ScreenCapture.Blend(shown[z], target, 0.45) : target;
            if (!changed && ScreenCapture.Distance(shown[z], last[z]) > 6) changed = true;
        }
        if (!changed) return;
        var frame = Enumerable.Range(0, LightState.Zones).Select(z => z < shown.Length ? shown[z] : 0).ToArray();
        framing = true;
        Do(h =>
        {
            try { Litra.Colors(h, frame, true); framed = frame; }
            finally { framing = false; }
        }, null, msg => { framed = null; });
    }

    void Snap()
    {
        if (!screening || !Visible || snapping) return;
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

    void Sync()
    {
        loading = true;
        var l = cur;
        frontHead.Text = ready ? "Front light" : "Front light · reading…";
        frontPower.Enabled = lumen.Enabled = kelvin.Enabled = ready;
        if (l != null)
        {
            frontPower.Checked = l.FrontOn;
            lumen.Value = (l.Lumen - lumens.Min) * 100 / Math.Max(1, lumens.Max - lumens.Min);
            lumenValue.Text = l.Lumen + " lm";
            kelvin.Max = Math.Max(1, (kelvins.Max - kelvins.Min) / kelvins.Step);
            kelvin.Value = (l.Kelvin - kelvins.Min) / kelvins.Step;
            kelvinValue.Text = l.Kelvin + " K";
            backPower.Checked = l.BackOn;
            level.Value = l.BackLevel;
            levelValue.Text = l.BackLevel + "%";
        }
        lumen.Invalidate();
        kelvin.Invalidate();
        level.Invalidate();
        backPower.Enabled = level.Enabled = effects.Enabled = ready;
        if (tryBtn != null) tryBtn.Enabled = ready && !screening && groups != null;
        zoneSeg.Index = picked;
        zoneSeg.Invalidate();
        effects.Index = screening ? LightState.Effects.Length : l == null ? -1 : l.Effect;
        effects.Invalidate();
        bool plain = back && !screening;
        bool zoned = ready && l != null && (l.Effect == LightState.Static || l.Effect == LightState.Breathe);
        bool moving = ready && l != null && !screening && l.Effect != LightState.Static;
        zoneHead.Visible = zoneSeg.Visible = colorHead.Visible = plain;
        foreach (var c in backBox.Controls.Cast<Control>().Where(c => c is Swatch)) c.Visible = plain;
        zoneSeg.Enabled = plain && zoned;
        Shade(zoneHead, zoned);
        Shade(colorHead, zoned);
        if (plain) colors.Sync(l == null ? 0xFFFFFF : l.Colors[picked == 0 ? 0 : picked - 1], zoned);
        Shade(speedHead, moving);
        speed.Enabled = moving;
        if (l != null) speed.Value = (LightState.MaxPeriod - l.Period) * 100 / (LightState.MaxPeriod - LightState.MinPeriod);
        speedValue.Text = moving ? speed.Value + "%" : "";
        speed.Invalidate();
        backBox.Height = backBox.Width * (screening ? 148 : 268) / 616;
        liveBox.Visible = !(back && screening);
        frontBox.Top = backBox.Width * (liveBox.Visible ? 224 : 118) / 616;
        backBox.Top = backBox.Width * (liveBox.Visible ? 320 : 214) / 616;
        liveHead.Text = trying ? "Live on the light · trying " + tried + ", look at the light" : "Live on the light";
        Show(l == null ? null : l.Effect == LightState.Static ? l.Colors : Litra.Frame(l, clock.Elapsed.TotalMilliseconds));
        Animation();
        areas.Visible = settingsBox.Visible = back && screening;
        if (back && screening) Monitors();
        loading = false;
    }

    void Monitors()
    {
        var all = Screen.AllScreens;
        var chosen = ScreenCapture.Find(setup.Monitor);
        monitorDrop.Items.Clear();
        monitorDrop.Items.AddRange(all.Select(ScreenCapture.Label));
        monitorDrop.Index = Array.IndexOf(all, chosen);
        preview.Zones = setup.Zones;
        preview.Selected = selectedZone;
        preview.Monitor = chosen.Bounds.Size;
        preview.Invalidate();
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

    void Values()
    {
        var l = cur;
        lumenValue.Text = l.Lumen + " lm";
        kelvinValue.Text = l.Kelvin + " K";
        levelValue.Text = l.BackLevel + "%";
        if (speed.Enabled) speedValue.Text = speed.Value + "%";
        Show(l.Effect == LightState.Static ? l.Colors : Litra.Frame(l, clock.Elapsed.TotalMilliseconds));
    }

    void Show(int[] zones)
    {
        var l = cur;
        if (l == null || !ready) { picture.Display(back, false, 0, 4000, false, 0, null, -1); return; }
        double frontLevel = (l.Lumen - lumens.Min) / (double)Math.Max(1, lumens.Max - lumens.Min);
        bool zoned = l.Effect == LightState.Static || l.Effect == LightState.Breathe;
        picture.Display(back, l.FrontOn, frontLevel, l.Kelvin, l.BackOn && !trying, l.BackLevel / 100.0, zones, zoned && !screening && !trying ? picked : -1);
    }

    protected override void Calm()
    {
        if (screening) { Say("Screen effect · not saved to the light"); return; }
        if (back && cur != null && cur.Effect != LightState.Static) { Say(LightState.Effects[cur.Effect] + " · G-Lite runs it, so it stops if you quit G-Lite"); return; }
        Say(back && Settings.Get(Remembered) != "" ? "Back light set by G-Lite · not saved to the light" : "");
    }

    void Send()
    {
        var before = sent;
        if (cur == null || !ready || before == null) return;
        var want = cur.Clone();
        bool colorsToo = !screening && colorsChosen;
        int n = ++seq;
        if (back && (Settings.Get(Remembered) != "" || colorsChosen || want.BackOn != before.BackOn || want.BackLevel != before.BackLevel)) Settings.Set(Remembered, want.BackText());
        Do(h =>
        {
            if (n != seq) return;
            Apply(h, want, colorsToo);
        }, () => { if (n == seq) Calm(); });
    }

    void Apply(Hidpp h, LightState want, bool colorsToo)
    {
        var was = sent;
        if (was == null) return;
        if (was.FrontOn != want.FrontOn) Litra.SetFrontOn(h, want.FrontOn);
        if (was.Lumen != want.Lumen) Litra.Set(h, 4, want.Lumen);
        if (was.Kelvin != want.Kelvin) Litra.Set(h, 9, want.Kelvin);
        if (back)
        {
            if (was.BackOn != want.BackOn) Litra.SetBackOn(h, want.BackOn);
            if (was.BackLevel != want.BackLevel) Litra.SetBackLevel(h, want.BackLevel);
            if (colorsToo && want.Effect == LightState.Static && !was.Colors.SequenceEqual(want.Colors)) Litra.Colors(h, want.Colors, false);
        }
        var unsent = colorsToo && want.Effect != LightState.Static ? Enumerable.Repeat(-1, LightState.Zones).ToArray() : was.Colors;
        var now = want.Clone();
        if (!colorsToo || want.Effect != LightState.Static) now.Colors = unsent;
        sent = now;
    }

    public override void Load()
    {
        debounce.Stop();
        Say("Reading the light…");
        LightState read = null;
        LightRange readLumens = null, readKelvins = null;
        List<byte[]> readGroups = null;
        bool canTry = tryBtn != null;
        var remembered = new LightState();
        bool hasRemembered = back && remembered.ReadBack(Settings.Get(Remembered));
        bool screen = screening;
        Do(h =>
        {
            readLumens = Litra.Lumens(h);
            readKelvins = Litra.Kelvins(h);
            read = new LightState { FrontOn = Litra.FrontOn(h), Lumen = Litra.Get(h, 3), Kelvin = Litra.Get(h, 8) };
            if (back)
            {
                read.BackLevel = Math.Max(1, Math.Min(100, Litra.BackLevel(h)));
                var on = Litra.BackOn(h);
                read.BackOn = on ?? true;
                read.Colors = Enumerable.Repeat(-1, LightState.Zones).ToArray();
                if (canTry) try { readGroups = G915.Clusters(h); } catch (NotSupportedException) { } catch (HidppError) { }
            }
            sent = read.Clone();
            if (hasRemembered)
            {
                var want = read.Clone();
                want.BackOn = remembered.BackOn;
                want.BackLevel = remembered.BackLevel;
                want.Colors = remembered.Colors;
                want.Effect = remembered.Effect;
                want.Period = remembered.Period;
                Apply(h, want, !screen);
                read = want;
            }
        }, () =>
        {
            if (hasRemembered) colorsChosen = true;
            lumens = readLumens;
            kelvins = readKelvins;
            if (readGroups != null) groups = readGroups;
            trying = false;
            if (read.Colors.Any(c => c < 0)) read.Colors = Enumerable.Repeat(0xFFFFFF, LightState.Zones).ToArray();
            cur = read;
            ready = true;
            framed = null;
            Sync();
            Loaded();
        });
    }
}
