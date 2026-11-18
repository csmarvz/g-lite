using System;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using Timer = System.Windows.Forms.Timer;

class G510Set
{
    public const int Static = 0, Breathe = 1, Cycle = 2, MinPeriod = 1000, MaxPeriod = 20000;
    public static readonly string[] Effects = { "Off", "Static", "Breathe", "Cycle" };

    public bool On = true;
    public int Color = 0xFFFFFF, Bright = 100, Effect = Static, Period = 5000;

    public G510Set Clone() { return (G510Set)MemberwiseClone(); }

    public bool Same(G510Set o) { return o != null && On == o.On && Color == o.Color && Bright == o.Bright && Effect == o.Effect && (Effect == Static || Period == o.Period); }

    public string ToText() { return (On ? 1 : 0) + "," + Color.ToString("X6") + "," + Bright + (Effect == Static ? "" : "," + Effect + "," + Period); }

    public static G510Set FromText(string text)
    {
        var v = (text ?? "").Split(',');
        int color, bright, effect = Static, period = 5000;
        if (v.Length < 3 || !int.TryParse(v[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out color) || !int.TryParse(v[2], out bright)) return null;
        if (v.Length >= 5 && (!int.TryParse(v[3], out effect) || effect < Static || effect > Cycle || !int.TryParse(v[4], out period))) { effect = Static; period = 5000; }
        return new G510Set { On = v[0] != "0", Color = color & 0xFFFFFF, Bright = Math.Max(1, Math.Min(100, bright)), Effect = effect, Period = Math.Max(MinPeriod, Math.Min(MaxPeriod, period)) };
    }
}

// The G510 and G510s backlight: one colour for the whole keyboard in feature report 0x05 (`05 R G B`), with no brightness or effects of its own,
// so brightness scales the colour and G-Lite runs Breathe and Cycle by sending frames. From the Linux driver hid-lg-g15 (facts only) and an owner's report (docs/G510.md).
static class G510Lighting
{
    public const byte Backlight = 0x05;

    public static int Read(Hidpp h)
    {
        var r = h.GetFeature(Backlight);
        return r[1] << 16 | r[2] << 8 | r[3];
    }

    static int Scale(int color, double k)
    {
        return (int)((color >> 16 & 0xFF) * k) << 16 | (int)((color >> 8 & 0xFF) * k) << 8 | (int)((color & 0xFF) * k);
    }

    public static int Frame(G510Set s, double ms)
    {
        if (!s.On) return 0;
        double k = s.Bright / 100.0, phase = ms % s.Period / s.Period;
        if (s.Effect == G510Set.Breathe) return Scale(s.Color, k * (0.5 - 0.5 * Math.Cos(2 * Math.PI * phase)));
        if (s.Effect == G510Set.Cycle) return Scale(LitraProtocol.Hue(phase), k);
        return Scale(s.Color, k);
    }

    public static void Set(Hidpp h, int color, bool quiet = false)
    {
        var report = new[] { Backlight, (byte)(color >> 16), (byte)(color >> 8), (byte)color };
        if (quiet) h.SetFeatureUnlogged(report); else h.SetFeature(report);
    }
}

class PageG510 : DevicePage
{
    G510Set cur, read, sent;
    bool loading, ready, framing;
    int seq;
    volatile int framed = -1;
    DateTime pausedUntil;
    readonly Stopwatch clock = Stopwatch.StartNew();
    readonly Timer debounce = new Timer { Interval = 120 }, animTimer = new Timer { Interval = 50 };

    internal override bool Busy { get { return debounce.Enabled; } }

    readonly Label lightHead, effectHead, colorHead, brightHead, brightValue, speedHead, speedValue;
    readonly SegmentedPicker effects;
    readonly ColorRow colors;
    readonly Slider bright, speed;

    string Remembered { get { return "lighting." + found.SettingsKey; } }

    public PageG510(FoundDevice found, Hidpp fake, MouseEventHandler drag) : base(found, fake, drag, found.ShortName, "keyboard", "profile." + found.SettingsKey + ".")
    {
        debounce.Tick += (s, e) => { debounce.Stop(); Send(); };
        animTimer.Tick += (s, e) => Animate();

        var light = Ui.Add(this, new RoundedBox(), 20, 118, 616, 256);
        lightHead = Ui.Text(light, "Backlight", 14, 12, () => Theme.Dim, 9f, Ui.Semibold);
        effectHead = Ui.Text(light, "Effect", 14, 40, () => Theme.Dim, 9f);
        effects = Ui.Add(light, new SegmentedPicker { Items = G510Set.Effects, AccessibleName = "Effect" }, 14, 58, 440, 32);
        effects.Picked += i => Edit(s => { s.On = i > 0; if (i > 0) s.Effect = i - 1; }, true);
        colorHead = Ui.Text(light, "Color", 14, 100, () => Theme.Dim, 9f);
        colors = new ColorRow(light, 117);
        colors.Picked += c => Edit(s => s.Color = c, true);
        brightHead = Ui.Text(light, "Brightness", 14, 160, () => Theme.Dim, 9f);
        brightValue = Value(light, 14, 160, 260);
        bright = Ui.Add(light, new Slider { AccessibleName = "Brightness" }, 14, 180, 260, 24);
        bright.Changed += (s, e) => { int b = Math.Max(1, bright.Value); Edit(x => x.Bright = b, false); };
        speedHead = Ui.Text(light, "Speed", 290, 160, () => Theme.Dim, 9f);
        speedValue = Value(light, 290, 160, 260);
        speed = Ui.Add(light, new Slider { AccessibleName = "Speed" }, 290, 180, 260, 24);
        speed.Changed += (s, e) => { int period = G510Set.MaxPeriod - (G510Set.MaxPeriod - G510Set.MinPeriod) * speed.Value / 100; Edit(x => x.Period = period, false); };
        var note = Ui.Text(light, "One color for the whole keyboard. G-Lite sets it again when it starts and when you open the keyboard, and runs Breathe and Cycle itself: they stop when you quit G-Lite.", 14, 214, () => Theme.Dim, 9f);
        note.AutoSize = false;
        note.Size = new Size(588, 36);

        cur = G510Set.FromText(Settings.Get(Remembered));
        RefreshControls();
        if (cur != null) Load();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { debounce.Dispose(); animTimer.Dispose(); }
        base.Dispose(disposing);
    }

    public override void Restyle() { RefreshControls(); }

    protected override string Current() { return cur == null ? "" : cur.ToText(); }

    protected override void ApplyPreset(string text)
    {
        var s = G510Set.FromText(text);
        if (s == null) return;
        cur = s;
        Changed(true);
    }

    protected override bool Unsaved { get { return ready && cur != null && read != null && !cur.Same(read); } }

    protected override void Lost() { sent = null; framed = -1; }

    protected override void Revert()
    {
        if (read == null) return;
        Settings.Remove(Remembered);
        cur = null;
        RefreshControls();
        Send(read.Clone());
    }

    void Edit(Action<G510Set> change, bool now)
    {
        if (loading || !ready) return;
        var s = (cur ?? read ?? new G510Set()).Clone();
        change(s);
        cur = s;
        Changed(now);
    }

    void Changed(bool now)
    {
        seq++;
        Edited();
        RefreshControls();
        debounce.Stop();
        if (now) Send(); else debounce.Start();
    }

    void RefreshControls()
    {
        loading = true;
        var s = cur ?? read;
        bool known = ready && s != null, lit = known && s.On, color = lit && s.Effect != G510Set.Cycle, moving = lit && s.Effect != G510Set.Static;
        lightHead.Text = ready && read == null ? "Backlight (couldn't be read)" : "Backlight";
        SetDimmed(effectHead, ready);
        effects.Enabled = ready;
        effects.Index = known ? (s.On ? s.Effect + 1 : 0) : -1;
        effects.Invalidate();
        SetDimmed(colorHead, color);
        colors.RefreshControls(known ? s.Color : 0xFFFFFF, color);
        SetDimmed(brightHead, lit);
        bright.Enabled = lit;
        bright.Value = known ? s.Bright : 100;
        brightValue.Text = lit ? s.Bright + "%" : "";
        bright.Invalidate();
        SetDimmed(speedHead, moving);
        speed.Enabled = moving;
        speed.Value = known ? (G510Set.MaxPeriod - s.Period) * 100 / (G510Set.MaxPeriod - G510Set.MinPeriod) : 0;
        speedValue.Text = moving ? speed.Value + "%" : "";
        speed.Invalidate();
        loading = false;
        Animation();
    }

    bool Animated { get { return ready && cur != null && cur.On && cur.Effect != G510Set.Static; } }

    void Animation()
    {
        bool on = Animated && !Manual;
        if (on && !animTimer.Enabled) animTimer.Start();
        if (!on && animTimer.Enabled) animTimer.Stop();
    }

    void Animate()
    {
        if (IsDisposed || !Animated || framing || DateTime.UtcNow < pausedUntil) return;
        int frame = G510Lighting.Frame(cur, clock.Elapsed.TotalMilliseconds);
        if (frame == framed) return;
        framing = true;
        RunOnDevice(h =>
        {
            try { G510Lighting.Set(h, frame, true); framed = frame; }
            finally { framing = false; }
        }, null, msg =>
        {
            // A refused frame would otherwise fail, and be logged, 20 times a second.
            framing = false;
            framed = -1;
            pausedUntil = DateTime.UtcNow.AddSeconds(10);
            ShowStatus("The keyboard refused the effect's colors: " + msg, true);
        });
    }

    protected override void ClearStatus()
    {
        if (ready && read == null) { ShowStatus("The keyboard didn't answer. Is it plugged in?", true, Load, "Try again"); return; }
        ShowStatus(cur == null ? "" : "Set by G-Lite, not saved to the keyboard");
    }

    void Send()
    {
        if (cur == null) return;
        Settings.Set(Remembered, cur.ToText());
        Send(cur.Clone());
    }

    // Breathe and Cycle are sent by Animate, frame by frame.
    void Send(G510Set want)
    {
        int n = ++seq;
        framed = -1;
        pausedUntil = DateTime.MinValue;
        if (want.On && want.Effect != G510Set.Static) { RunOnDevice(h => sent = null, () => { if (n == seq) ClearStatus(); }); return; }
        RunOnDevice(h =>
        {
            if (n != seq || want.Same(sent)) return;
            G510Lighting.Set(h, G510Lighting.Frame(want, 0));
            sent = want;
        }, () => { if (n == seq) ClearStatus(); });
    }

    public override void Load()
    {
        debounce.Stop();
        ShowStatus("Reading the keyboard…");
        var want = cur == null ? null : cur.Clone();
        int color = -1;
        RunOnDevice(h =>
        {
            sent = null;
            framed = -1;
            try { color = G510Lighting.Read(h); }
            catch (System.IO.IOException) { color = -1; }
            if (want != null && color >= 0 && (!want.On || want.Effect == G510Set.Static)) { G510Lighting.Set(h, G510Lighting.Frame(want, 0)); sent = want; }
        }, () =>
        {
            ready = true;
            read = color < 0 ? null : new G510Set { On = color != 0, Color = color == 0 ? 0xFFFFFF : color, Bright = 100 };
            RefreshControls();
            Loaded();
        });
    }
}
