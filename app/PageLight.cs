using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
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

class PageLight : DevicePage
{
    LightState cur, sent;
    LightRange lumens = new LightRange { Min = 30, Max = 400, Step = 1 }, kelvins = new LightRange { Min = 2700, Max = 6500, Step = 100 };
    bool loading, ready, colorsChosen, screening, capturing, framing, snapping;
    readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
    int seq, picked, selectedZone;
    int[] shown, framed;
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
    readonly Box backBox, areas, settingsBox;
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

        var front = Ui.Add(this, new Box(), 20, 118, 616, 86);
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

        backBox = Ui.Add(this, new Box { Visible = back }, 20, 214, 616, 268);
        Ui.Text(backBox, "Back light", 14, 12, () => Theme.Dim, 9f, Ui.Semi);
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
        label.Click += (s, e) => { toggle.Checked = !toggle.Checked; flip(); ScreenChanged(); };
        return toggle;
    }

    void Edit(Action<LightState> change, bool now)
    {
        if (loading || !ready) return;
        change(cur);
        seq++;
        Edited();
        Sync();
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
        shown = framed = null;
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
        shown = framed = null;
        colorsChosen = true;
        if (sent != null) sent.Colors = new int[LightState.Zones].Select(c => -1).ToArray();
        Did("Screen effect off");
        Sync();
        Send();
    }

    void ScreenChanged()
    {
        if (loading) return;
        Settings.Set(Saved, setup.ToText());
        framed = null;
        Sync();
    }

    bool Animated { get { return back && ready && !screening && cur != null && cur.Effect != LightState.Static; } }

    void Animation()
    {
        bool on = Animated && !Manual;
        if (on && !animTimer.Enabled) animTimer.Start();
        if (!on && animTimer.Enabled) animTimer.Stop();
    }

    void Animate()
    {
        if (!Animated || framing) return;
        var frame = Litra.Frame(cur, clock.Elapsed.TotalMilliseconds);
        if (framed != null && frame.SequenceEqual(framed)) return;
        framing = true;
        Do(h =>
        {
            try { Litra.Colors(h, frame, true); framed = frame; }
            finally { framing = false; }
        }, null, msg => { framed = null; });
    }

    void Tick()
    {
        if (!screening || !ready || capturing || framing) return;
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
        bool changed = refresh || fresh || framed == null;
        for (int z = 0; z < sampled.Length; z++)
        {
            int target = ScreenCapture.Boost(sampled[z], setup.Boost);
            shown[z] = setup.Smooth && !fresh ? ScreenCapture.Blend(shown[z], target, 0.45) : target;
            if (!changed && ScreenCapture.Distance(shown[z], framed[z]) > 6) changed = true;
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
        backBox.Height = (int)Math.Round((screening ? 148 : 268) * Ui.K);
        Animation();
        areas.Visible = settingsBox.Visible = back && screening;
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

    protected override void Calm()
    {
        if (screening) { Say("Screen effect · not saved to the light"); return; }
        if (back && cur != null && cur.Effect != LightState.Static) { Say(LightState.Effects[cur.Effect] + " · G-Lite runs it, so it stops when G-Lite closes"); return; }
        Say(back && Settings.Get(Remembered) != "" ? "Back light set by G-Lite · not saved to the light" : "");
    }

    void Send()
    {
        if (cur == null || !ready) return;
        var want = cur.Clone();
        bool colorsToo = !screening && colorsChosen;
        int n = ++seq;
        if (back && (Settings.Get(Remembered) != "" || colorsChosen || want.BackOn != sent.BackOn || want.BackLevel != sent.BackLevel)) Settings.Set(Remembered, want.BackText());
        Do(h =>
        {
            if (n != seq) return;
            Apply(h, want, colorsToo);
        }, () => { if (n == seq) Calm(); });
    }

    void Apply(Hidpp h, LightState want, bool colorsToo)
    {
        var was = sent;
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
        sent = want.Clone();
        if (!colorsToo || want.Effect != LightState.Static) sent.Colors = unsent;
    }

    public override void Load()
    {
        debounce.Stop();
        Say("Reading the light…");
        LightState read = null;
        LightRange readLumens = null, readKelvins = null;
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
            if (read.Colors.Any(c => c < 0)) read.Colors = Enumerable.Repeat(0xFFFFFF, LightState.Zones).ToArray();
            cur = read;
            ready = true;
            framed = null;
            Sync();
            Loaded();
        });
    }
}
