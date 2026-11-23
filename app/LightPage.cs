using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Drawing.Drawing2D;
using System.Threading;
using System.Windows.Forms;
using Timer = System.Windows.Forms.Timer;

class LightPicture : DrawnControl
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

    public void Display(bool hasBack, bool frontLit, double frontLevel, int frontKelvin,
        bool backLit, double backLevel, int[] zones, int pick)
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

    RectangleF Bar
    {
        get
        {
            float width = Width * 0.64f, height = width / 12;
            return new RectangleF((Width - width) / 2, Height - height - 4 * DpiScale, width, height);
        }
    }

    static Color Warmth(int kelvin)
    {
        double fraction = Math.Max(0, Math.Min(1, (kelvin - 2700) / 3800.0));
        return Color.FromArgb((int)(255 - 10 * fraction), (int)(214 + 36 * fraction), (int)(170 + 85 * fraction));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var graphics = Begin(e);
        var bar = Bar;
        var zones = colors;
        var housing = new RectangleF(bar.X + bar.Height * 0.3f, bar.Y - bar.Height * 0.3f, bar.Width - bar.Height * 0.6f, bar.Height);
        if (back && backOn && zones != null)
        {
            float zoneWidth = housing.Width / zones.Length;
            for (int zone = 0; zone < zones.Length; zone++)
            {
                if (zones[zone] < 0) continue;
                var glow = new RectangleF(housing.X + zone * zoneWidth - zoneWidth * 0.35f, 0,
                    zoneWidth * 1.7f, housing.Y * 2 + bar.Height * 0.4f);
                using (var path = new GraphicsPath())
                {
                    path.AddEllipse(glow);
                    using (var brush = new PathGradientBrush(path))
                    {
                        int alpha = (int)(255 * Math.Max(0.35, level));
                        brush.CenterColor = Color.FromArgb(alpha, zones[zone] >> 16 & 0xFF, zones[zone] >> 8 & 0xFF, zones[zone] & 0xFF);
                        brush.SurroundColors = new[]
                            { Color.FromArgb(0, zones[zone] >> 16 & 0xFF, zones[zone] >> 8 & 0xFF, zones[zone] & 0xFF) };
                        graphics.FillPath(brush, path);
                    }
                }
            }
        }
        Ui.Fill(graphics, housing, bar.Height / 2, Color.FromArgb(24, 24, 26), Color.FromArgb(24, 24, 26));
        var face = frontOn ? Ui.Mix(Color.FromArgb(110, 110, 110), Warmth(kelvin), 0.55 + 0.45 * front) : Theme.KeyOff;
        Ui.Fill(graphics, bar, bar.Height / 2, face, Theme.Border);
        if (back && selected > 0 && zones != null)
        {
            float zoneWidth = bar.Width / zones.Length;
            using (var pen = new Pen(Theme.Accent, 2 * DpiScale))
            using (var path = Ui.Round(
                new RectangleF(bar.X + (selected - 1) * zoneWidth + 2 * DpiScale, bar.Bottom + 5 * DpiScale,
                    zoneWidth - 4 * DpiScale, 3 * DpiScale),
                1.5f * DpiScale))
                graphics.DrawPath(pen, path);
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        var zones = colors;
        if (!back || selected < 0 || zones == null || e.Button != MouseButtons.Left || Picked == null) return;
        var bar = Bar;
        int zone = (int)((e.X - bar.X) / (bar.Width / zones.Length));
        if (zone >= 0 && zone < zones.Length) Picked(zone + 1);
    }
}

class LightPage : DevicePage
{
    LightState current;
    volatile LightState sent;
    LightRange lumens = new LightRange { Min = 30, Max = 400, Step = 1 }, kelvins = new LightRange { Min = 2700, Max = 6500, Step = 100 };
    bool loading, ready, colorsChosen, screening, capturing, framing, snapping, trying;
    List<byte[]> groups;
    readonly FlatButton tryButton;
    readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
    int sequence, picked, selectedZone;
    int[] shown;
    volatile int[] framed;
    DateTime nextRefresh;
    ScreenSettings setup;
    readonly bool back;
    readonly Timer debounce = new Timer { Interval = 120 }, screenTimer = new Timer { Interval = 100 },
        animationTimer = new Timer { Interval = 50 }, snapTimer = new Timer { Interval = 1000 };

    internal override bool Busy { get { return debounce.Enabled; } }

    readonly Label frontHead, lumenValue, kelvinValue, levelValue, speedHead, speedValue, zoneHead, colorHead, boostValue, rateValue;
    readonly Toggle frontPower, backPower, smoothToggle, lockToggle;
    readonly Slider lumen, kelvin, level, speed, boost, rate;
    readonly SegmentedPicker zonePicker, effects;
    readonly ColorRow colors;
    readonly RoundedBox liveBox, frontBox, backBox, areas, settingsBox;
    readonly Label liveHead;
    readonly LightPicture picture;
    string tried;
    readonly Dropdown monitorDropdown;
    readonly ScreenPreview preview;

    string Remembered { get { return "lighting." + found.SettingsKey; } }
    string Saved { get { return "screen." + found.SettingsKey; } }
    string Active { get { return "screen.on." + found.SettingsKey; } }

    public LightPage(FoundDevice found, Hidpp fake, MouseEventHandler drag)
        : base(found, fake, drag, found.ShortName, "light", "profile." + found.SettingsKey + ".")
    {
        back = found.Has(0x8081);
        debounce.Tick += (s, e) => { debounce.Stop(); Send(); };
        setup = LitraProtocol.Screen(Settings.Get(Saved));

        liveBox = Ui.Add(this, new RoundedBox(), 20, 118, 616, 96);
        liveHead = Ui.Text(liveBox, "Live on the light", 14, 12, () => Theme.Dim, 9f, Ui.Semibold);
        picture = Ui.Add(liveBox, new LightPicture(), 14, 30, 588, 58);
        picture.Picked += index => { if (zonePicker.Enabled) { picked = index; RefreshControls(); } };
        var front = frontBox = Ui.Add(this, new RoundedBox(), 20, 224, 616, 86);
        frontHead = Ui.Text(front, "Front light", 14, 12, () => Theme.Dim, 9f, Ui.Semibold);
        frontPower = Power(front, 14, 36, "Front light on", on => Edit(edited => edited.FrontOn = on, true));
        Ui.Text(front, "Brightness", 110, 36, () => Theme.Dim, 9f);
        lumenValue = Value(front, 110, 36, 230);
        lumen = Ui.Add(front, new Slider { AccessibleName = "Front brightness" }, 110, 54, 230, 24);
        lumen.Changed += (s, e) =>
        {
            int snapped = lumens.Snap(lumens.Min + (lumens.Max - lumens.Min) * lumen.Value / 100);
            Edit(edited => edited.Lumen = snapped, false);
        };
        Ui.Text(front, "Color temperature", 366, 36, () => Theme.Dim, 9f);
        kelvinValue = Value(front, 366, 36, 236);
        kelvin = Ui.Add(front, new Slider { AccessibleName = "Color temperature", Step = 1 }, 366, 54, 236, 24);
        kelvin.Changed += (s, e) =>
        {
            int snapped = kelvins.Snap(kelvins.Min + kelvin.Value * kelvins.Step);
            Edit(edited => edited.Kelvin = snapped, false);
        };

        backBox = Ui.Add(this, new RoundedBox { Visible = back }, 20, 320, 616, 268);
        Ui.Text(backBox, "Back light", 14, 12, () => Theme.Dim, 9f, Ui.Semibold);
        if (back && found.Has(0x8071))
        {
            tryButton = Ui.Add(backBox, new FlatButton { Text = "Try effect", AccessibleName = "Try the light's own effects" },
                490, 6, 112, 28);
            tryButton.Click += (s, e) => TryMenu();
        }
        backPower = Power(backBox, 14, 36, "Back light on", on => Edit(edited => edited.BackOn = on, true));
        Ui.Text(backBox, "Brightness", 110, 36, () => Theme.Dim, 9f);
        levelValue = Value(backBox, 110, 36, 230);
        level = Ui.Add(backBox, new Slider { AccessibleName = "Back brightness" }, 110, 54, 230, 24);
        level.Changed += (s, e) => { int brightness = Math.Max(1, level.Value); Edit(edited => edited.BackLevel = brightness, false); };
        speedHead = Ui.Text(backBox, "Speed", 366, 36, () => Theme.Dim, 9f);
        speedValue = Value(backBox, 366, 36, 236);
        speed = Ui.Add(backBox, new Slider { AccessibleName = "Speed" }, 366, 54, 236, 24);
        speed.Changed += (s, e) =>
        {
            int period = SpeedScale.Period(speed.Value, LightState.MinPeriod, LightState.MaxPeriod);
            Edit(edited => edited.Period = period, false);
        };
        Ui.Text(backBox, "Effect", 14, 88, () => Theme.Dim, 9f);
        effects = Ui.Add(backBox,
            new SegmentedPicker { Items = LightState.Effects.Concat(new[] { "Screen" }).ToArray(), AccessibleName = "Effect" },
            14, 106, 588, 30);
        effects.Picked += index =>
        {
            if (index == LightState.Effects.Length) { if (!screening) { ScreenOn(); Edited(); } return; }
            if (screening) ScreenOff();
            Edit(edited => { colorsChosen = true; edited.Effect = index; }, true);
        };
        zoneHead = Ui.Text(backBox, "Zone", 14, 146, () => Theme.Dim, 9f);
        zonePicker = Ui.Add(backBox,
            new SegmentedPicker
            {
                Items = new[] { "All" }.Concat(Enumerable.Range(1, LightState.Zones).Select(i => i.ToString())).ToArray(),
                AccessibleName = "Zone", Index = 0
            },
            14, 164, 588, 30);
        zonePicker.Picked += index => { picked = index; RefreshControls(); };
        colorHead = Ui.Text(backBox, "Color", 14, 204, () => Theme.Dim, 9f);
        colors = new ColorRow(backBox, 221);
        colors.Picked += color => Edit(edited =>
        {
            colorsChosen = true;
            for (int zone = 0; zone < LightState.Zones; zone++) if (picked == 0 || picked == zone + 1) edited.Colors[zone] = color;
        }, true);

        areas = Ui.Add(this, new RoundedBox { Visible = false }, 20, 372, 358, 240);
        settingsBox = Ui.Add(this, new RoundedBox { Visible = false }, 390, 372, 246, 240);
        Ui.Text(areas, "Screen areas", 14, 12, () => Theme.Dim, 9f);
        var frame = Ui.Add(areas, new RoundedBox(), 14, 34, 330, 158);
        preview = Ui.Add(frame, new ScreenPreview(), 8, 8, 314, 142);
        preview.Picked += index => { selectedZone = index; preview.Selected = index; preview.Invalidate(); };
        preview.Done += ScreenChanged;
        var resetAreas = Ui.Add(areas, new FlatButton { Text = "Reset areas", AccessibleName = "Reset the screen areas" },
            14, 200, 110, 30);
        resetAreas.Click += (s, e) => { setup.Zones = LitraProtocol.ScreenZones(); selectedZone = 0; ScreenChanged(); };
        Ui.Text(settingsBox, "Monitor", 14, 12, () => Theme.Dim, 9f);
        monitorDropdown = Ui.Add(settingsBox, new Dropdown { AccessibleName = "Monitor" }, 14, 34, 218, 30);
        monitorDropdown.Picked += index =>
        {
            var all = Screen.AllScreens;
            if (index < 0 || index >= all.Length) return;
            setup.Monitor = all[index].Primary ? "" : all[index].DeviceName;
            ScreenChanged();
            Snap();
        };
        Ui.Text(settingsBox, "Intensity", 14, 72, () => Theme.Dim, 9f);
        boostValue = Value(settingsBox, 14, 72, 218);
        boost = Ui.Add(settingsBox, new Slider { AccessibleName = "Intensity" }, 14, 90, 218, 24);
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
        lockToggle = Option(settingsBox, "Pause when screen off or locked", 14, 194, 36,
            () => setup.PauseWhenLocked = !setup.PauseWhenLocked);

        screenTimer.Tick += (s, e) => Tick();
        snapTimer.Tick += (s, e) => Snap();
        animationTimer.Tick += (s, e) => Animate();

        if (back && Settings.Get(Active) == "1") ScreenOn();
        RefreshControls();
        if (back && (screening || Settings.Get(Remembered) != "")) Load();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            debounce.Dispose();
            screenTimer.Dispose();
            snapTimer.Dispose();
            animationTimer.Dispose();
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
        var menu = new ContextMenuStrip { Renderer = Ui.Menus, Font = Ui.Get(9.75f, Ui.Regular), ShowImageMargin = false };
        menu.BackColor = Theme.Card;
        menu.ForeColor = Theme.Text;
        for (int groupIndex = 0; groupIndex < list.Count; groupIndex++)
        {
            for (int i = 0; i < list[groupIndex].Length; i++)
            {
                int group = groupIndex, index = i;
                byte id = list[groupIndex][i];
                menu.Items.Add(new ToolStripMenuItem("Group " + groupIndex + ": " + LitraProtocol.EffectName(id), null,
                    (s, e) => TryEffect(group, index, id)));
            }
            menu.Items.Add(new ToolStripSeparator());
        }
        menu.Items.Add(new ToolStripMenuItem("Back to G-Lite's colours", null, (s, e) => StopTrying()));
        menu.Closed += (s, e) => BeginInvoke((Action)menu.Dispose);
        if (Ui.Popped != null) { Ui.Popped(menu); return; }
        menu.Show(tryButton, new Point(tryButton.Width, tryButton.Height + (int)(2 * Ui.DpiScale)), ToolStripDropDownDirection.BelowLeft);
    }

    void TryEffect(int group, int index, byte id)
    {
        if (!ready || current == null) return;
        string name = "Group " + group + ": " + LitraProtocol.EffectName(id);
        trying = true;
        tried = name;
        RefreshControls();
        LogUserAction("Try effect " + name);
        var state = current.Clone();
        RunOnDevice(hidpp => LitraProtocol.TryEffect(hidpp, group, index, id, state),
            () => ShowStatus("Sent " + name + ". What lit up? Please say in your issue."));
    }

    void StopTrying()
    {
        if (!ready) return;
        trying = false;
        colorsChosen = true;
        framed = null;
        LogUserAction("Back to G-Lite's colours");
        RunOnDevice(hidpp =>
        {
            LitraProtocol.Release(hidpp);
            var was = sent;
            if (was != null) was.Colors = Enumerable.Repeat(-1, LightState.Zones).ToArray();
        });
        RefreshControls();
        Send();
    }

    // Every way out of Try effect gives back the software control it took, so G-Lite's own colours show again.
    void EndTrying()
    {
        if (!trying) return;
        trying = false;
        framed = null;
        RunOnDevice(hidpp =>
        {
            LitraProtocol.Release(hidpp);
            var was = sent;
            if (was != null) was.Colors = Enumerable.Repeat(-1, LightState.Zones).ToArray();
        });
    }

    void Edit(Action<LightState> change, bool now)
    {
        if (loading || !ready) return;
        EndTrying();
        change(current);
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
        return current == null ? ""
            : "front:" + current.FrontText() + (back ? ";back:" + current.BackText() : "") + (screening ? "|" + setup.ToText() : "");
    }

    protected override void ApplyPreset(string text)
    {
        if (current == null) return;
        var pieces = text.Split('|');
        var next = current.Clone();
        foreach (var part in pieces[0].Split(';'))
        {
            if (part.StartsWith("front:")) next.ReadFront(part.Substring(6));
            if (part.StartsWith("back:") && back) next.ReadBack(part.Substring(5));
        }
        next.Lumen = lumens.Snap(next.Lumen);
        next.Kelvin = kelvins.Snap(next.Kelvin);
        bool screen = back && pieces.Length > 1;
        if (screen) setup = LitraProtocol.Screen(pieces[1]);
        if (screening && !screen) ScreenOff();
        EndTrying();
        colorsChosen = true;
        current = next;
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

    protected override void Lost() { ready = false; sent = null; framed = null; }

    void ScreenOn()
    {
        EndTrying();
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
        colorsChosen = true;
        if (ready)
            RunOnDevice(hidpp => { var was = sent; if (was != null) was.Colors = Enumerable.Repeat(-1, LightState.Zones).ToArray(); });
        LogUserAction("Screen effect off");
        RefreshControls();
    }

    void ScreenChanged()
    {
        if (loading) return;
        Settings.Set(Saved, setup.ToText());
        framed = null;
        preview.Zones = setup.Zones;
        preview.Selected = selectedZone;
        preview.Invalidate();
        Edited();
    }

    bool Animated { get { return back && ready && !screening && !trying && current != null && current.Effect != LightState.Static; } }

    void Animation()
    {
        bool on = Animated && !Manual;
        if (on && !animationTimer.Enabled) animationTimer.Start();
        if (!on && animationTimer.Enabled) animationTimer.Stop();
    }

    void Animate()
    {
        if (IsDisposed || !Animated) return;
        var frame = LitraProtocol.Frame(current, clock.Elapsed.TotalMilliseconds);
        Show(frame);
        if (framing) return;
        var last = framed;
        if (last != null && frame.SequenceEqual(last)) return;
        framing = true;
        RunOnDevice(hidpp =>
        {
            try { LitraProtocol.Colors(hidpp, frame, true); framed = frame; }
            finally { framing = false; }
        }, null, message => { framed = null; });
    }

    void Tick()
    {
        if (IsDisposed || !screening || !ready || capturing || framing) return;
        if (setup.PauseWhenLocked && (Presence.Locked || Presence.DisplayOff)) return;
        capturing = true;
        var monitor = ScreenCapture.Find(setup.Monitor).Bounds;
        var zones = setup.Zones.ToList();
        var context = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        ThreadPool.QueueUserWorkItem(state =>
        {
            int[] sampled = null;
            try { sampled = ScreenCapture.Sample(monitor, zones); } catch (Exception) { }
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
        for (int zone = 0; zone < sampled.Length; zone++)
        {
            int target = ScreenCapture.Boost(sampled[zone], setup.Boost);
            shown[zone] = setup.Smooth && !fresh ? ScreenCapture.Blend(shown[zone], target, 0.45) : target;
            if (!changed && ScreenCapture.Distance(shown[zone], last[zone]) > 6) changed = true;
        }
        if (!changed) return;
        var frame = Enumerable.Range(0, LightState.Zones).Select(zone => zone < shown.Length ? shown[zone] : 0).ToArray();
        framing = true;
        RunOnDevice(hidpp =>
        {
            try { LitraProtocol.Colors(hidpp, frame, true); framed = frame; }
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
        var light = current;
        frontHead.Text = ready ? "Front light" : "Front light (reading…)";
        frontPower.Enabled = lumen.Enabled = kelvin.Enabled = ready;
        if (light != null)
        {
            frontPower.Checked = light.FrontOn;
            lumen.Value = (light.Lumen - lumens.Min) * 100 / Math.Max(1, lumens.Max - lumens.Min);
            lumenValue.Text = light.Lumen + " lm";
            kelvin.Max = Math.Max(1, (kelvins.Max - kelvins.Min) / kelvins.Step);
            kelvin.Value = (light.Kelvin - kelvins.Min) / kelvins.Step;
            kelvinValue.Text = light.Kelvin + " K";
            backPower.Checked = light.BackOn;
            level.Value = light.BackLevel;
            levelValue.Text = light.BackLevel + "%";
        }
        lumen.Invalidate();
        kelvin.Invalidate();
        level.Invalidate();
        backPower.Enabled = level.Enabled = effects.Enabled = ready;
        if (tryButton != null) tryButton.Enabled = ready && !screening && groups != null;
        zonePicker.Index = picked;
        zonePicker.Invalidate();
        effects.Index = screening ? LightState.Effects.Length : light == null ? -1 : light.Effect;
        effects.Invalidate();
        bool plain = back && !screening;
        bool zoned = ready && light != null && (light.Effect == LightState.Static || light.Effect == LightState.Breathe);
        bool moving = ready && light != null && !screening && light.Effect != LightState.Static;
        zoneHead.Visible = zonePicker.Visible = colorHead.Visible = plain;
        foreach (var control in backBox.Controls.Cast<Control>().Where(control => control is Swatch)) control.Visible = plain;
        zonePicker.Enabled = plain && zoned;
        SetDimmed(zoneHead, zoned);
        SetDimmed(colorHead, zoned);
        if (plain) colors.RefreshControls(light == null ? 0xFFFFFF : light.Colors[picked == 0 ? 0 : picked - 1], zoned);
        SetDimmed(speedHead, moving);
        speed.Enabled = moving;
        if (light != null) speed.Value = SpeedScale.Percent(light.Period, LightState.MinPeriod, LightState.MaxPeriod);
        speedValue.Text = moving ? speed.Value + "%" : "";
        speed.Invalidate();
        backBox.Height = backBox.Width * (screening ? 148 : 268) / 616;
        liveBox.Visible = !(back && screening);
        frontBox.Top = backBox.Width * (liveBox.Visible ? 224 : 118) / 616;
        backBox.Top = backBox.Width * (liveBox.Visible ? 320 : 214) / 616;
        liveHead.Text = trying ? "Live on the light: trying " + tried + ", look at the light" : "Live on the light";
        Show(light == null ? null
            : light.Effect == LightState.Static ? light.Colors
            : LitraProtocol.Frame(light, clock.Elapsed.TotalMilliseconds));
        Animation();
        areas.Visible = settingsBox.Visible = back && screening;
        if (back && screening) Monitors();
        loading = false;
    }

    void Monitors()
    {
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
        lockToggle.Checked = setup.PauseWhenLocked;
        boost.Invalidate();
        rate.Invalidate();
        loading = false;
    }

    void Values()
    {
        var light = current;
        lumenValue.Text = light.Lumen + " lm";
        kelvinValue.Text = light.Kelvin + " K";
        levelValue.Text = light.BackLevel + "%";
        if (speed.Enabled) speedValue.Text = speed.Value + "%";
        Show(light.Effect == LightState.Static ? light.Colors : LitraProtocol.Frame(light, clock.Elapsed.TotalMilliseconds));
    }

    void Show(int[] zones)
    {
        var light = current;
        if (light == null || !ready) { picture.Display(back, false, 0, 4000, false, 0, null, -1); return; }
        double frontLevel = (light.Lumen - lumens.Min) / (double)Math.Max(1, lumens.Max - lumens.Min);
        bool zoned = light.Effect == LightState.Static || light.Effect == LightState.Breathe;
        picture.Display(back, light.FrontOn, frontLevel, light.Kelvin,
            light.BackOn && !trying, light.BackLevel / 100.0, zones, zoned && !screening && !trying ? picked : -1);
    }

    protected override void ClearStatus()
    {
        if (screening) { ShowStatus("Screen effect, not saved to the light"); return; }
        if (back && current != null && current.Effect != LightState.Static)
        {
            ShowStatus(LightState.Effects[current.Effect] + ": G-Lite runs it, so it stops if you quit G-Lite");
            return;
        }
        ShowStatus(back && Settings.Get(Remembered) != "" ? "Back light set by G-Lite, not saved to the light" : "");
    }

    void Send()
    {
        var before = sent;
        if (current == null || !ready || before == null) return;
        var want = current.Clone();
        bool colorsToo = !screening && colorsChosen;
        int startedAt = ++sequence;
        if (back && (Settings.Get(Remembered) != "" || colorsChosen || want.BackOn != before.BackOn || want.BackLevel != before.BackLevel))
            Settings.Set(Remembered, want.BackText());
        RunOnDevice(hidpp =>
        {
            if (startedAt != sequence) return;
            Apply(hidpp, want, colorsToo);
        }, () => { if (startedAt == sequence) ClearStatus(); });
    }

    void Apply(Hidpp hidpp, LightState want, bool colorsToo)
    {
        var was = sent;
        if (was == null) return;
        if (was.FrontOn != want.FrontOn) LitraProtocol.SetFrontOn(hidpp, want.FrontOn);
        if (was.Lumen != want.Lumen) LitraProtocol.Set(hidpp, 4, want.Lumen);
        if (was.Kelvin != want.Kelvin) LitraProtocol.Set(hidpp, 9, want.Kelvin);
        if (back)
        {
            if (was.BackOn != want.BackOn) LitraProtocol.SetBackOn(hidpp, want.BackOn);
            if (was.BackLevel != want.BackLevel) LitraProtocol.SetBackLevel(hidpp, want.BackLevel);
            if (colorsToo && want.Effect == LightState.Static && !was.Colors.SequenceEqual(want.Colors))
                LitraProtocol.Colors(hidpp, want.Colors, false);
        }
        var unsent = colorsToo && want.Effect != LightState.Static ? Enumerable.Repeat(-1, LightState.Zones).ToArray() : was.Colors;
        var now = want.Clone();
        if (!colorsToo || want.Effect != LightState.Static) now.Colors = unsent;
        sent = now;
    }

    public override void Load()
    {
        debounce.Stop();
        ShowStatus("Reading the light…");
        LightState read = null;
        LightRange readLumens = null, readKelvins = null;
        List<byte[]> readGroups = null;
        bool canTry = tryButton != null;
        var remembered = new LightState();
        bool hasRemembered = back && remembered.ReadBack(Settings.Get(Remembered));
        bool screen = screening, wasTrying = trying;
        RunOnDevice(hidpp =>
        {
            if (wasTrying) LitraProtocol.Release(hidpp);
            readLumens = LitraProtocol.Lumens(hidpp);
            readKelvins = LitraProtocol.Kelvins(hidpp);
            read = new LightState
            {
                FrontOn = LitraProtocol.FrontOn(hidpp), Lumen = LitraProtocol.Get(hidpp, 3), Kelvin = LitraProtocol.Get(hidpp, 8)
            };
            if (back)
            {
                read.BackLevel = Math.Max(1, Math.Min(100, LitraProtocol.BackLevel(hidpp)));
                var on = LitraProtocol.BackOn(hidpp);
                read.BackOn = on ?? true;
                read.Colors = Enumerable.Repeat(-1, LightState.Zones).ToArray();
                if (canTry) try { readGroups = G915Lighting.Clusters(hidpp); } catch (NotSupportedException) { } catch (HidppError) { }
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
                Apply(hidpp, want, !screen);
                read = want;
            }
        }, () =>
        {
            if (hasRemembered) colorsChosen = true;
            lumens = readLumens;
            kelvins = readKelvins;
            if (readGroups != null) groups = readGroups;
            trying = false;
            if (read.Colors.Any(color => color < 0)) read.Colors = Enumerable.Repeat(0xFFFFFF, LightState.Zones).ToArray();
            current = read;
            ready = true;
            framed = null;
            RefreshControls();
            Loaded();
        });
    }
}
