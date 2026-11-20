using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

class G510Page : DevicePage
{
    G510Set current, read, sent;
    bool loading, ready, framing;
    int sequence;
    volatile int framed = -1;
    DateTime pausedUntil;
    readonly Stopwatch clock = Stopwatch.StartNew();
    readonly Timer debounce = new Timer { Interval = 120 }, animationTimer = new Timer { Interval = 50 };

    internal override bool Busy { get { return debounce.Enabled; } }

    readonly Label lightHead, effectHead, colorHead, brightHead, brightValue, speedHead, speedValue;
    readonly SegmentedPicker effects;
    readonly ColorRow colors;
    readonly Slider bright, speed;

    string Remembered { get { return "lighting." + found.SettingsKey; } }

    public G510Page(FoundDevice found, Hidpp fake, MouseEventHandler drag)
        : base(found, fake, drag, found.ShortName, "keyboard", "profile." + found.SettingsKey + ".")
    {
        debounce.Tick += (s, e) => { debounce.Stop(); Send(); };
        animationTimer.Tick += (s, e) => Animate();

        var light = Ui.Add(this, new RoundedBox(), 20, 118, 616, 256);
        lightHead = Ui.Text(light, "Backlight", 14, 12, () => Theme.Dim, 9f, Ui.Semibold);
        effectHead = Ui.Text(light, "Effect", 14, 40, () => Theme.Dim, 9f);
        effects = Ui.Add(light, new SegmentedPicker { Items = G510Set.Effects, AccessibleName = "Effect" }, 14, 58, 440, 32);
        effects.Picked += index => Edit(settings => { settings.On = index > 0; if (index > 0) settings.Effect = index - 1; }, true);
        colorHead = Ui.Text(light, "Color", 14, 100, () => Theme.Dim, 9f);
        colors = new ColorRow(light, 117);
        colors.Picked += color => Edit(settings => settings.Color = color, true);
        brightHead = Ui.Text(light, "Brightness", 14, 160, () => Theme.Dim, 9f);
        brightValue = Value(light, 14, 160, 260);
        bright = Ui.Add(light, new Slider { AccessibleName = "Brightness" }, 14, 180, 260, 24);
        bright.Changed += (s, e) => { int brightness = Math.Max(1, bright.Value); Edit(settings => settings.Bright = brightness, false); };
        speedHead = Ui.Text(light, "Speed", 290, 160, () => Theme.Dim, 9f);
        speedValue = Value(light, 290, 160, 260);
        speed = Ui.Add(light, new Slider { AccessibleName = "Speed" }, 290, 180, 260, 24);
        speed.Changed += (s, e) =>
        {
            int period = G510Set.MaxPeriod - (G510Set.MaxPeriod - G510Set.MinPeriod) * speed.Value / 100;
            Edit(settings => settings.Period = period, false);
        };
        var note = Ui.Text(light,
            "One color for the whole keyboard. G-Lite sets it again when it starts and when you open the keyboard, and runs Breathe and Cycle itself: they stop when you quit G-Lite.",
            14, 214, () => Theme.Dim, 9f);
        note.AutoSize = false;
        note.Size = new Size(588, 36);

        current = G510Set.FromText(Settings.Get(Remembered));
        RefreshControls();
        if (current != null) Load();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { debounce.Dispose(); animationTimer.Dispose(); }
        base.Dispose(disposing);
    }

    public override void Restyle() { RefreshControls(); }

    protected override string Current() { return current == null ? "" : current.ToText(); }

    protected override void ApplyPreset(string text)
    {
        var settings = G510Set.FromText(text);
        if (settings == null) return;
        current = settings;
        Changed(true);
    }

    protected override bool Unsaved { get { return ready && current != null && read != null && !current.Same(read); } }

    protected override void Lost() { sent = null; framed = -1; }

    protected override void Revert()
    {
        if (read == null) return;
        Settings.Remove(Remembered);
        current = null;
        RefreshControls();
        Send(read.Clone());
    }

    void Edit(Action<G510Set> change, bool now)
    {
        if (loading || !ready) return;
        var settings = (current ?? read ?? new G510Set()).Clone();
        change(settings);
        current = settings;
        Changed(now);
    }

    void Changed(bool now)
    {
        sequence++;
        Edited();
        RefreshControls();
        debounce.Stop();
        if (now) Send(); else debounce.Start();
    }

    void RefreshControls()
    {
        loading = true;
        var settings = current ?? read;
        bool known = ready && settings != null, lit = known && settings.On,
            color = lit && settings.Effect != G510Set.Cycle, moving = lit && settings.Effect != G510Set.Static;
        lightHead.Text = ready && read == null ? "Backlight (couldn't be read)" : "Backlight";
        SetDimmed(effectHead, ready);
        effects.Enabled = ready;
        effects.Index = known ? (settings.On ? settings.Effect + 1 : 0) : -1;
        effects.Invalidate();
        SetDimmed(colorHead, color);
        colors.RefreshControls(known ? settings.Color : 0xFFFFFF, color);
        SetDimmed(brightHead, lit);
        bright.Enabled = lit;
        bright.Value = known ? settings.Bright : 100;
        brightValue.Text = lit ? settings.Bright + "%" : "";
        bright.Invalidate();
        SetDimmed(speedHead, moving);
        speed.Enabled = moving;
        speed.Value = known ? (G510Set.MaxPeriod - settings.Period) * 100 / (G510Set.MaxPeriod - G510Set.MinPeriod) : 0;
        speedValue.Text = moving ? speed.Value + "%" : "";
        speed.Invalidate();
        loading = false;
        Animation();
    }

    bool Animated { get { return ready && current != null && current.On && current.Effect != G510Set.Static; } }

    void Animation()
    {
        bool on = Animated && !Manual;
        if (on && !animationTimer.Enabled) animationTimer.Start();
        if (!on && animationTimer.Enabled) animationTimer.Stop();
    }

    void Animate()
    {
        if (IsDisposed || !Animated || framing || DateTime.UtcNow < pausedUntil) return;
        int frame = G510Lighting.Frame(current, clock.Elapsed.TotalMilliseconds);
        if (frame == framed) return;
        framing = true;
        RunOnDevice(hidpp =>
        {
            try { G510Lighting.Set(hidpp, frame, true); framed = frame; }
            finally { framing = false; }
        }, null, message =>
        {
            // A refused frame would otherwise fail, and be logged, 20 times a second.
            framing = false;
            framed = -1;
            pausedUntil = DateTime.UtcNow.AddSeconds(10);
            ShowStatus("The keyboard refused the effect's colors: " + message, true);
        });
    }

    protected override void ClearStatus()
    {
        if (ready && read == null) { ShowStatus("The keyboard didn't answer. Is it plugged in?", true, Load, "Try again"); return; }
        ShowStatus(current == null ? "" : "Set by G-Lite, not saved to the keyboard");
    }

    void Send()
    {
        if (current == null) return;
        Settings.Set(Remembered, current.ToText());
        Send(current.Clone());
    }

    // Breathe and Cycle are sent by Animate, frame by frame.
    void Send(G510Set want)
    {
        int startedAt = ++sequence;
        framed = -1;
        pausedUntil = DateTime.MinValue;
        if (want.On && want.Effect != G510Set.Static)
        {
            RunOnDevice(hidpp => sent = null, () => { if (startedAt == sequence) ClearStatus(); });
            return;
        }
        RunOnDevice(hidpp =>
        {
            if (startedAt != sequence || want.Same(sent)) return;
            G510Lighting.Set(hidpp, G510Lighting.Frame(want, 0));
            sent = want;
        }, () => { if (startedAt == sequence) ClearStatus(); });
    }

    public override void Load()
    {
        debounce.Stop();
        ShowStatus("Reading the keyboard…");
        var want = current == null ? null : current.Clone();
        int color = -1;
        RunOnDevice(hidpp =>
        {
            sent = null;
            framed = -1;
            try { color = G510Lighting.Read(hidpp); }
            catch (System.IO.IOException) { color = -1; }
            if (want != null && color >= 0 && (!want.On || want.Effect == G510Set.Static))
            {
                G510Lighting.Set(hidpp, G510Lighting.Frame(want, 0));
                sent = want;
            }
        }, () =>
        {
            ready = true;
            read = color < 0 ? null : new G510Set { On = color != 0, Color = color == 0 ? 0xFFFFFF : color, Bright = 100 };
            RefreshControls();
            Loaded();
        });
    }
}
