using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using Timer = System.Windows.Forms.Timer;

static class GSpeaker
{
    public static List<Dictionary<int, int>> Zones(Hidpp h)
    {
        byte f = h.F(0x8070);
        var zones = new List<Dictionary<int, int>>();
        int count = h.Call(f, 0)[0];
        for (int zone = 0; zone < count; zone++)
        {
            var info = h.Call(f, 1, (byte)zone, 0xFF, 0);
            var map = new Dictionary<int, int>();
            for (int effect = 0; effect < info[3]; effect++)
            {
                var r = h.Call(f, 2, (byte)zone, (byte)effect, 0xFF, 0);
                map[r[2] << 8 | r[3]] = effect;
            }
            zones.Add(map);
        }
        return zones;
    }

    public static void SetLight(Hidpp h, int zone, Dictionary<int, int> effects, MouseLight light)
    {
        int index;
        if (!effects.TryGetValue(light.Effect, out index)) return;
        h.Call(h.F(0x8070), 3, new byte[] { (byte)zone, (byte)index }.Concat(GMouse.Encode(light)).ToArray());
    }

    public static void SetColor(Hidpp h, int zone, Dictionary<int, int> effects, int color, int bright)
    {
        int index;
        if (!effects.TryGetValue(0x01, out index)) return;
        var light = new MouseLight { Effect = 0x01, Color = color, Bright = bright };
        h.Quiet(h.F(0x8070), 3, new byte[] { (byte)zone, (byte)index }.Concat(GMouse.Encode(light)).ToArray());
    }

    public static List<Zone> ScreenZones()
    {
        var areas = new[] { new RectangleF(0.05f, 0.55f, 0.4f, 0.35f), new RectangleF(0.55f, 0.55f, 0.4f, 0.35f), new RectangleF(0.05f, 0.1f, 0.4f, 0.35f), new RectangleF(0.55f, 0.1f, 0.4f, 0.35f) };
        return areas.Select((area, i) => new Zone { Name = "Zone " + (i + 1), Mark = ScreenSetup.Marks[i], Area = area }).ToList();
    }

    public static ScreenSetup Screen(string text)
    {
        var setup = ScreenSetup.FromText(text);
        if (string.IsNullOrEmpty(text) || setup.Zones.Count != 4) setup.Zones = ScreenZones();
        return setup;
    }

    public static string ToText(MouseLight[] lights)
    {
        return string.Join(";", lights.Select(l => l == null ? "" : string.Join(",", l.Effect, l.Color.ToString("X6"), l.Period, l.Bright)));
    }

    public static MouseLight[] FromText(string text, int zones)
    {
        var parts = (text ?? "").Split(';');
        var lights = new MouseLight[zones];
        for (int zone = 0; zone < zones; zone++) lights[zone] = One(parts.Length == 1 ? parts[0] : zone < parts.Length ? parts[zone] : "");
        return lights.Any(l => l != null) ? lights : null;
    }

    static MouseLight One(string text)
    {
        var v = text.Split(',');
        byte effect;
        int color, period, bright;
        if (v.Length < 4 || !byte.TryParse(v[0], out effect) || GMouse.Def(effect) == null
            || !int.TryParse(v[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out color)
            || !int.TryParse(v[2], out period) || !int.TryParse(v[3], out bright)) return null;
        var d = GMouse.Def(effect);
        if (d.Speed) period = Math.Max(d.MinP, Math.Min(d.MaxP, period));
        return new MouseLight { Effect = effect, Color = color & 0xFFFFFF, Period = period, Bright = Math.Max(1, Math.Min(100, bright)) };
    }
}

class PageSpeaker : DevicePage
{
    const int Count = 4;
    MouseLight[] cur, sent = new MouseLight[Count];
    List<Dictionary<int, int>> zones;
    bool loading, screening, capturing, framing, snapping;
    int seq, picked, selectedZone;
    int[] shown, framed;
    DateTime nextRefresh;
    ScreenSetup setup;
    readonly Timer debounce = new Timer { Interval = 120 }, screenTimer = new Timer { Interval = 100 }, snapTimer = new Timer { Interval = 1000 };

    internal override bool Busy { get { return debounce.Enabled; } }

    readonly Label lightHead, zoneHead, colorHead, speedHead, speedValue, brightHead, brightValue, note, boostValue, rateValue, screenBrightValue;
    readonly Seg zoneSeg, effects;
    readonly ColorRow colors;
    readonly Slider speed, bright, boost, rate, screenBright;
    readonly Box light, areas, settingsBox;
    readonly Drop monitorDrop;
    readonly ScreenPreview preview;
    readonly Toggle smoothToggle, lockToggle;
    readonly List<Control> plainLight = new List<Control>();

    string Remembered { get { return "lighting." + found.Slug; } }
    string Saved { get { return "screen." + found.Slug; } }
    string Active { get { return "screen.on." + found.Slug; } }

    public PageSpeaker(Found found, Hidpp fake, MouseEventHandler drag) : base(found, fake, drag, found.Short, "device", "profile." + found.Slug + ".")
    {
        debounce.Tick += (s, e) => { debounce.Stop(); Send(); };
        setup = GSpeaker.Screen(Settings.Get(Saved));

        light = Ui.Add(this, new Box(), 20, 118, 616, 330);
        lightHead = Ui.Text(light, "Lighting", 14, 12, () => Theme.Dim, 9f, Ui.Semi);
        zoneHead = Ui.Text(light, "Zone", 14, 40, () => Theme.Dim, 9f);
        zoneSeg = Ui.Add(light, new Seg { Items = new[] { "All", "Zone 1", "Zone 2", "Zone 3", "Zone 4" }, AccessibleName = "Zone", Index = 0 }, 14, 58, 440, 32);
        zoneSeg.Picked += i => { picked = i; Sync(); };
        Ui.Text(light, "Effect", 14, 98, () => Theme.Dim, 9f);
        var names = GMouse.Effects.Select(d => d.Name).ToList();
        names.Add("Screen");
        effects = Ui.Add(light, new Seg { Items = names.ToArray(), AccessibleName = "Effect" }, 14, 116, 440, 32);
        effects.Picked += i =>
        {
            if (i == GMouse.Effects.Length) { if (!screening) ScreenOn(); return; }
            if (screening) ScreenOff();
            var d = GMouse.Effects[i];
            Edit(l =>
            {
                l.Effect = d.Id;
                if (d.Speed && (l.Period < d.MinP || l.Period > d.MaxP)) l.Period = d.DefP;
            }, true);
        };
        colorHead = Ui.Text(light, "Color", 14, 156, () => Theme.Dim, 9f);
        colors = new ColorRow(light, 173);
        colors.Picked += c => Edit(l => l.Color = c, true);

        brightHead = Ui.Text(light, "Brightness", 14, 214, () => Theme.Dim, 9f);
        brightValue = Value(light, 14, 214, 172);
        bright = Ui.Add(light, new Slider { AccessibleName = "Brightness" }, 14, 234, 172, 24);
        bright.Changed += (s, e) => { int b = Math.Max(1, bright.Value); Edit(l => l.Bright = b, false); };
        speedHead = Ui.Text(light, "Speed", 194, 214, () => Theme.Dim, 9f);
        speedValue = Value(light, 194, 214, 172);
        speed = Ui.Add(light, new Slider { AccessibleName = "Speed" }, 194, 234, 172, 24);
        speed.Changed += (s, e) =>
        {
            var d = GMouse.Def(Displayed().Effect);
            int period = d.MaxP - (d.MaxP - d.MinP) * speed.Value / 100;
            Edit(l => l.Period = period, false);
        };
        note = Ui.Text(light, "G-Lite sets the lighting again when it starts and when you open the speakers. Which zone is which isn't known yet.", 14, 272, () => Theme.Dim, 9f);
        note.AutoSize = false;
        note.Size = new Size(588, 40);
        plainLight.AddRange(new Control[] { colorHead, brightHead, brightValue, bright, speedHead, speedValue, speed, note });
        plainLight.AddRange(light.Controls.Cast<Control>().Where(c => c is Swatch));

        areas = Ui.Add(this, new Box { Visible = false }, 20, 292, 358, 300);
        settingsBox = Ui.Add(this, new Box { Visible = false }, 390, 292, 246, 300);
        Ui.Text(areas, "Screen areas", 14, 12, () => Theme.Dim, 9f);
        var frame = Ui.Add(areas, new Box(), 14, 38, 330, 188);
        preview = Ui.Add(frame, new ScreenPreview(), 8, 8, 314, 172);
        preview.Picked += i => { selectedZone = i; preview.Selected = i; preview.Invalidate(); };
        preview.Done += ScreenChanged;
        var resetAreas = Ui.Add(areas, new Btn { Text = "Reset areas", AccessibleName = "Reset the screen areas" }, 14, 236, 110, 30);
        resetAreas.Click += (s, e) => { setup.Zones = GSpeaker.ScreenZones(); selectedZone = 0; ScreenChanged(); };
        Ui.Text(settingsBox, "Monitor", 14, 12, () => Theme.Dim, 9f);
        monitorDrop = Ui.Add(settingsBox, new Drop { AccessibleName = "Monitor" }, 14, 38, 218, 30);
        monitorDrop.Picked += i =>
        {
            var all = Screen.AllScreens;
            if (i < 0 || i >= all.Length) return;
            setup.Monitor = all[i].Primary ? "" : all[i].DeviceName;
            ScreenChanged();
            Snap();
        };
        Ui.Text(settingsBox, "Brightness", 14, 78, () => Theme.Dim, 9f);
        screenBrightValue = Value(settingsBox, 14, 78, 218);
        screenBright = Ui.Add(settingsBox, new Slider { AccessibleName = "Brightness" }, 14, 96, 218, 24);
        screenBright.Changed += (s, e) => { setup.Bright = Math.Max(1, screenBright.Value); screenBrightValue.Text = setup.Bright + "%"; ScreenChanged(); };
        Ui.Text(settingsBox, "Color boost", 14, 126, () => Theme.Dim, 9f);
        boostValue = Value(settingsBox, 14, 126, 218);
        boost = Ui.Add(settingsBox, new Slider { AccessibleName = "Color boost" }, 14, 144, 218, 24);
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
        lockToggle = Option(settingsBox, "Pause when screen off or locked", 14, 256, 36, () => setup.PauseWhenLocked = !setup.PauseWhenLocked);

        screenTimer.Tick += (s, e) => Tick();
        snapTimer.Tick += (s, e) => Snap();

        cur = GSpeaker.FromText(Settings.Get(Remembered), Count);
        if (Settings.Get(Active) == "1") ScreenOn();
        Sync();
        if (cur != null || screening) Load();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            debounce.Dispose();
            screenTimer.Dispose();
            snapTimer.Dispose();
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

    MouseLight Displayed()
    {
        if (cur == null) return null;
        return cur[picked == 0 ? 0 : picked - 1];
    }

    void Edit(Action<MouseLight> change, bool now)
    {
        if (loading || zones == null) return;
        if (cur == null) cur = Enumerable.Range(0, Count).Select(i => new MouseLight()).ToArray();
        if (picked == 0)
        {
            var all = (cur[0] ?? new MouseLight()).Clone();
            change(all);
            for (int zone = 0; zone < Count; zone++) cur[zone] = all.Clone();
        }
        else
        {
            var one = (cur[picked - 1] ?? new MouseLight()).Clone();
            change(one);
            cur[picked - 1] = one;
        }
        seq++;
        Edited();
        Sync();
        debounce.Stop();
        if (now) Send(); else debounce.Start();
    }

    public override void Restyle() { Sync(); }

    protected override string Current() { return cur == null ? "" : GSpeaker.ToText(cur); }

    protected override void Use(string text)
    {
        var lights = GSpeaker.FromText(text, Count);
        if (lights == null) return;
        if (screening) ScreenOff();
        cur = lights;
        seq++;
        Edited();
        Sync();
        Send();
    }

    protected override void Revert() { }

    protected override void Lost() { zones = null; sent = new MouseLight[Count]; framed = null; }

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
        sent = new MouseLight[Count];
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

    void Tick()
    {
        if (IsDisposed || !screening || zones == null || capturing || framing) return;
        if (setup.PauseWhenLocked && (Presence.Locked || Presence.DisplayOff)) return;
        capturing = true;
        var monitor = ScreenCapture.Find(setup.Monitor).Bounds;
        var areas = setup.Zones.ToList();
        var ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        ThreadPool.QueueUserWorkItem(state =>
        {
            int[] sampled = null;
            try { sampled = ScreenCapture.Sample(monitor, areas); } catch (Exception) { }
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
        var frame = (int[])shown.Clone();
        int level = setup.Bright;
        var z0 = zones;
        framing = true;
        Do(h =>
        {
            try { for (int zone = 0; zone < frame.Length && zone < z0.Count; zone++) GSpeaker.SetColor(h, zone, z0[zone], frame[zone], level); framed = frame; }
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
        bool ready = zones != null && zones.Count > 0;
        var l = Displayed();
        bool known = l != null && !screening;
        var d = known ? GMouse.Def(l.Effect) : null;
        bool mixed = picked == 0 && cur != null && cur.Any(z => z == null || !z.Same(cur[0]));
        lightHead.Text = zones != null && zones.Count == 0 ? "Lighting · not found on these speakers" : screening ? "Lighting" : !known ? "Lighting · as on the speakers" : mixed ? "Lighting · zones differ, a change sets all of them" : "Lighting";
        zoneSeg.Enabled = ready && !screening;
        Shade(zoneHead, !screening);
        zoneSeg.Index = picked;
        zoneSeg.Invalidate();
        effects.Enabled = ready || screening;
        effects.Index = screening ? GMouse.Effects.Length : known ? Array.FindIndex(GMouse.Effects, e => e.Id == l.Effect) : -1;
        effects.Invalidate();
        bool color = known && d.Color, spd = known && d.Speed, br = known && l.Effect != 0;
        Shade(colorHead, color);
        colors.Sync(known ? l.Color : 0xFFFFFF, color);
        Shade(brightHead, br);
        bright.Enabled = br;
        bright.Value = known ? l.Bright : 100;
        brightValue.Text = br ? l.Bright + "%" : "";
        bright.Invalidate();
        Shade(speedHead, spd);
        speed.Enabled = spd;
        if (spd) speed.Value = (d.MaxP - l.Period) * 100 / (d.MaxP - d.MinP);
        speedValue.Text = spd ? speed.Value + "%" : "";
        speed.Invalidate();
        foreach (var c in plainLight) c.Visible = !screening;
        areas.Visible = settingsBox.Visible = screening;
        if (!screening) colors.Sync(known ? l.Color : 0xFFFFFF, color);
        light.Height = light.Width * (screening ? 162 : 330) / 616;
        var all = Screen.AllScreens;
        var chosen = ScreenCapture.Find(setup.Monitor);
        monitorDrop.Items.Clear();
        monitorDrop.Items.AddRange(all.Select(ScreenCapture.Label));
        monitorDrop.Index = Array.IndexOf(all, chosen);
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

    protected override void Calm()
    {
        if (zones != null && zones.Count == 0) { Say("These speakers list no lighting zones", true); return; }
        if (screening) { Say("Screen effect · not saved to the speakers"); return; }
        Say(cur == null ? "" : "Set by G-Lite · not saved to the speakers");
    }

    void Send()
    {
        if (cur == null || screening) return;
        var want = cur.Select(l => l == null ? null : l.Clone()).ToArray();
        var z = zones;
        int n = ++seq;
        Settings.Set(Remembered, GSpeaker.ToText(want));
        Do(h =>
        {
            if (n != seq || z == null) return;
            Apply(h, z, want);
        }, () => { if (n == seq) Calm(); });
    }

    void Apply(Hidpp h, List<Dictionary<int, int>> z, MouseLight[] want)
    {
        for (int zone = 0; zone < z.Count && zone < Count; zone++)
        {
            if (want[zone] == null || (sent[zone] != null && sent[zone].Same(want[zone]))) continue;
            GSpeaker.SetLight(h, zone, z[zone], want[zone]);
            sent[zone] = want[zone];
        }
    }

    public override void Load()
    {
        debounce.Stop();
        Say("Reading the speakers…");
        List<Dictionary<int, int>> read = null;
        var want = cur == null || screening ? null : cur.Select(l => l == null ? null : l.Clone()).ToArray();
        Do(h =>
        {
            read = GSpeaker.Zones(h);
            sent = new MouseLight[Count];
            if (want != null) Apply(h, read, want);
        }, () =>
        {
            zones = read;
            framed = null;
            Sync();
            Loaded();
        });
    }
}
