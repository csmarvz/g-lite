using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

class MousePicture : Ctl
{
    readonly Stopwatch clock = Stopwatch.StartNew();
    readonly Timer timer = new Timer { Interval = 33 };
    MouseLight light;
    int dpi;
    string stage = "", rate = "";

    public MousePicture()
    {
        Cursor = Cursors.Default;
        AccessibleRole = AccessibleRole.Graphic;
        AccessibleName = "Mouse";
        timer.Tick += (s, e) => { if (Visible) Invalidate(); };
        VisibleChanged += (s, e) => timer.Enabled = Visible;
    }

    public void Display(MouseLight l, int d, string st, string rt)
    {
        light = l;
        dpi = d;
        stage = st;
        rate = rt;
        Invalidate();
    }

    Color Glow(double t)
    {
        if (light == null) return KeyboardView.Hsv(t / 8, 1, 1);
        var c = Color.FromArgb(light.Color >> 16 & 0xFF, light.Color >> 8 & 0xFF, light.Color & 0xFF);
        double k = light.Bright / 100.0, period = Math.Max(1, light.Period / 1000.0);
        switch (light.Effect)
        {
            case 0x01: return KeyboardView.Scale(c, k);
            case 0x0A: return KeyboardView.Scale(c, k * (0.5 - 0.5 * Math.Cos(2 * Math.PI * t / period)));
            case 0x03: return KeyboardView.Scale(KeyboardView.Hsv(t / period, 1, 1), k);
            default: return Color.Black;
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = Begin(e);
        float h = Height - 16 * K, w = h * 0.6f, x = 70 * K, y = 8 * K;
        var body = new RectangleF(x, y, w, h);
        using (var path = Ui.Round(body, w / 2))
        {
            using (var b = new SolidBrush(Theme.KeyOff)) g.FillPath(b, path);
            g.SetClip(path);
            using (var p = new Pen(Theme.Bed, 1.5f * K))
            {
                g.DrawLine(p, x + w / 2, y, x + w / 2, y + h * 0.4f);
                g.DrawLine(p, x, y + h * 0.4f, x + w, y + h * 0.4f);
            }
            g.ResetClip();
            using (var p = new Pen(Theme.Border)) g.DrawPath(p, path);
        }
        Ui.Fill(g, new RectangleF(x + w / 2 - 4 * K, y + h * 0.12f, 8 * K, h * 0.17f), 4 * K, Theme.Bed, Theme.Bed);

        var c = Glow(clock.Elapsed.TotalSeconds);
        int max = Math.Max(c.R, Math.Max(c.G, c.B));
        float r = w * 0.17f, cx = x + w / 2, cy = y + h * 0.68f;
        var fill = Theme.KeyOff;
        if (max > 0)
        {
            var full = Color.FromArgb(c.R * 255 / max, c.G * 255 / max, c.B * 255 / max);
            fill = Ui.Mix(Theme.Bed, full, max / 255.0);
            using (var gp = new GraphicsPath())
            {
                gp.AddEllipse(cx - r * 2.4f, cy - r * 2.4f, r * 4.8f, r * 4.8f);
                using (var pb = new PathGradientBrush(gp) { CenterColor = Color.FromArgb((int)(110 * max / 255.0), full), SurroundColors = new[] { Color.FromArgb(0, full) } })
                {
                    using (var clip = Ui.Round(body, w / 2)) g.SetClip(clip);
                    g.FillPath(pb, gp);
                    g.ResetClip();
                }
            }
        }
        else fill = Theme.Bed;
        using (var b = new SolidBrush(fill)) g.FillEllipse(b, cx - r, cy - r, 2 * r, 2 * r);

        float tx = x + w + 70 * K, top = Height / 2f - 46 * K;
        var big = Ui.Get(27f, Ui.Semi);
        var text = dpi > 0 ? dpi.ToString() : "–";
        Ui.Draw(g, text, big, new RectangleF(tx, top, Width - tx, 48 * K), Theme.Text, TextFormatFlags.Left | TextFormatFlags.Bottom);
        float bw = TextRenderer.MeasureText(g, text, big, Size.Empty, TextFormatFlags.NoPadding).Width;
        Ui.Draw(g, "DPI", Ui.Get(11f, Ui.Semi), new RectangleF(tx + bw + 8 * K, top, 80 * K, 44 * K), Theme.Dim, TextFormatFlags.Left | TextFormatFlags.Bottom);
        var small = Ui.Get(9.75f, Ui.Regular);
        Ui.Draw(g, stage, small, new RectangleF(tx, top + 58 * K, Width - tx, 20 * K), Theme.Dim, TextFormatFlags.Left);
        Ui.Draw(g, rate, small, new RectangleF(tx, top + 80 * K, Width - tx, 20 * K), Theme.Dim, TextFormatFlags.Left);
    }
}

class PageMouse : DevicePage
{
    MouseSet cur = new MouseSet(), stored, sent;
    GMouse.State state;
    bool loading, dirty, lightSet;
    int seq;
    readonly Timer debounce = new Timer { Interval = 120 };

    readonly Label lightHead, colorHead, speedHead, speedValue, brightHead, brightValue, stageHead, dpiValue, rateHead, rateHint;
    readonly Seg effects, stages, rates;
    readonly ColorRow colors;
    readonly Slider speed, bright, dpi;
    readonly MousePicture picture;
    readonly Btn saveBtn, backupsBtn;
    Backups.Entry fix;

    public PageMouse(Found found, Hidpp fake, MouseEventHandler drag) : base(found, fake, drag, found.Short, "mouse", "profile." + found.Slug + ".")
    {
        debounce.Tick += (s, e) => { debounce.Stop(); Send(); };
        backupsBtn = BackupsButton("mouse's profile");
        backupsBtn.Click += (s, e) => ShowBackups();

        var pic = Ui.Add(this, new Box(), 20, 118, 616, 209);
        Ui.Text(pic, "Live on the mouse", 14, 12, () => Theme.Dim, 9f, Ui.Semi);
        picture = Ui.Add(pic, new MousePicture(), 14, 36, 588, 161);

        var light = Ui.Add(this, new Box(), 20, 339, 380, 272);
        lightHead = Ui.Text(light, "Lighting", 14, 12, () => Theme.Dim, 9f, Ui.Semi);
        Ui.Text(light, "Effect", 14, 40, () => Theme.Dim, 9f);
        effects = Ui.Add(light, new Seg { Items = GMouse.Effects.Select(d => d.Name).ToArray(), AccessibleName = "Effect" }, 14, 58, 352, 32);
        effects.Picked += i =>
        {
            var d = GMouse.Effects[i];
            var l = Light();
            l.Effect = d.Id;
            if (d.Speed && (l.Period < d.MinP || l.Period > d.MaxP)) l.Period = d.DefP;
            Changed(true);
        };
        colorHead = Ui.Text(light, "Color", 14, 98, () => Theme.Dim, 9f);
        colors = new ColorRow(light, 115);
        colors.Picked += c => { Light().Color = c; Changed(true); };

        brightHead = Ui.Text(light, "Brightness", 14, 156, () => Theme.Dim, 9f);
        brightValue = Value(light, 14, 156, 172);
        bright = Ui.Add(light, new Slider { AccessibleName = "Brightness" }, 14, 176, 172, 24);
        bright.Changed += (s, e) => { Light().Bright = Math.Max(1, bright.Value); Changed(false); };
        speedHead = Ui.Text(light, "Speed", 194, 156, () => Theme.Dim, 9f);
        speedValue = Value(light, 194, 156, 172);
        speed = Ui.Add(light, new Slider { AccessibleName = "Speed" }, 194, 176, 172, 24);
        speed.Changed += (s, e) =>
        {
            var l = Light();
            var d = GMouse.Def(l.Effect);
            l.Period = d.MaxP - (d.MaxP - d.MinP) * speed.Value / 100;
            Changed(false);
        };

        var sens = Ui.Add(this, new Box(), 412, 339, 224, 272);
        Ui.Text(sens, "Sensitivity", 14, 12, () => Theme.Dim, 9f, Ui.Semi);
        stageHead = Ui.Text(sens, "DPI stage", 14, 40, () => Theme.Dim, 9f);
        stages = Ui.Add(sens, new Seg { AccessibleName = "DPI stage" }, 14, 58, 196, 32);
        stages.Picked += i => { cur.Stage = i; cur.Dpi = state.Stages[i]; Changed(true); };
        Ui.Text(sens, "DPI", 14, 98, () => Theme.Dim, 9f);
        dpiValue = Value(sens, 14, 98, 196);
        dpi = Ui.Add(sens, new Slider { AccessibleName = "DPI", Step = 1 }, 14, 118, 196, 24);
        dpi.Changed += (s, e) => { if (state != null && state.Dpis.Length > 0) { cur.Dpi = state.Dpis[dpi.Value]; Changed(false); } };
        rateHead = Ui.Text(sens, "Report rate", 14, 156, () => Theme.Dim, 9f);
        rates = Ui.Add(sens, new Seg { AccessibleName = "Report rate" }, 14, 174, 196, 32);
        rates.Picked += i =>
        {
            if (!state.Onboard) { SendRate(state.Rates[i]); return; }
            cur.Rate = state.Rates[i];
            Changed(true);
        };
        rateHint = Ui.Text(sens, "", 14, 212, () => Theme.Dim, 9f);
        rateHint.AutoSize = false;
        rateHint.Size = new Size(196, 48);

        saveBtn = Ui.Add(this, new Btn { Look = Look.Primary, Text = "Save to mouse" }, 496, 635, 140, 32);
        saveBtn.Click += (s, e) => Save();

        Sync();
    }

    MouseLight Light()
    {
        if (cur.Light == null) cur.Light = new MouseLight();
        return cur.Light;
    }

    public override void Restyle() { Sync(); }

    protected override string Current() { return cur.ToText(); }

    protected override void Use(string text)
    {
        var s = MouseSet.FromText(text);
        if (state != null && state.Dpis.Length > 0) s.Dpi = state.Dpis[Near(state.Dpis, s.Dpi)];
        s.Stage = state != null && Array.IndexOf(state.Stages, s.Dpi) >= 0 ? Array.IndexOf(state.Stages, s.Dpi) : cur.Stage;
        cur = s;
        Changed(true);
    }

    protected override void Revert()
    {
        if (stored == null) return;
        cur = stored.Clone();
        Changed(true);
        dirty = lightSet && stored.Light == null;
        Calm();
    }

    static int Near(int[] values, int v)
    {
        int best = 0;
        for (int i = 0; i < values.Length; i++) if (Math.Abs(values[i] - v) < Math.Abs(values[best] - v)) best = i;
        return best;
    }

    void Sync()
    {
        loading = true;
        bool ready = state != null;
        var l = cur.Light;
        var d = l == null ? null : GMouse.Def(l.Effect);
        bool lit = ready && state.Zones.Count > 0, known = lit && d != null;
        bool color = known && d.Color, spd = known && d.Speed, br = known && l.Effect != 0x00;
        lightHead.Text = !ready || lit ? (l == null && lit ? "Lighting · as saved on the mouse" : "Lighting") : "Lighting · not supported on this mouse";
        effects.Enabled = lit;
        effects.Index = known ? Array.FindIndex(GMouse.Effects, e => e.Id == l.Effect) : -1;
        Shade(colorHead, color);
        colors.Sync(l == null ? 0xFFFFFF : l.Color, color);
        Shade(brightHead, br);
        bright.Enabled = br;
        bright.Value = l == null ? 100 : l.Bright;
        brightValue.Text = br ? l.Bright + "%" : "";
        bright.Invalidate();
        Shade(speedHead, spd);
        speed.Enabled = spd;
        if (spd) speed.Value = (d.MaxP - l.Period) * 100 / (d.MaxP - d.MinP);
        speedValue.Text = spd ? speed.Value + "%" : "";
        speed.Invalidate();

        bool hasStages = ready && state.Stages.Length > 0;
        int stage = hasStages && cur.Stage >= 0 && cur.Stage < state.Stages.Length ? cur.Stage : -1;
        var view = hasStages ? (int[])state.Stages.Clone() : new int[0];
        if (stage >= 0) view[stage] = cur.Dpi;
        stages.Items = hasStages ? view.Select(x => x.ToString()).ToArray() : new[] { "–" };
        stages.Enabled = hasStages;
        stages.Index = stage;
        Shade(stageHead, hasStages);
        dpi.Enabled = ready && state.Dpis.Length > 0;
        dpi.Max = Math.Max(1, ready ? state.Dpis.Length - 1 : 1);
        dpi.Value = ready && state.Dpis.Length > 0 ? Near(state.Dpis, cur.Dpi) : 0;
        dpi.Invalidate();
        dpiValue.Text = ready ? cur.Dpi.ToString() : "";
        bool rateOn = ready && state.Rates.Length > 0 && (!state.Onboard || state.Savable);
        int rate = ready ? (cur.Rate > 0 ? cur.Rate : state.Rate) : 0;
        rates.Items = ready && state.Rates.Length > 0 ? state.Rates.Select(x => x.ToString()).ToArray() : new[] { "–" };
        rates.Index = ready ? Array.IndexOf(state.Rates, rate) : -1;
        rates.Enabled = rateOn;
        rates.Invalidate();
        Shade(rateHead, rateOn);
        rateHead.Text = "Report rate" + (ready && state.Rates.Length > 0 ? " · Hz" : "");
        rateHint.Text = !ready || !state.Onboard ? "" : state.Savable
            ? "Changes when you save to the mouse. The DPI button switches between the saved stages."
            : "Set by the profile saved on the mouse. The DPI button switches between the saved stages.";
        saveBtn.Enabled = ready && state.Savable && dirty;
        saveBtn.Visible = !ready || state.Writable;

        picture.Display(lit ? l : new MouseLight { Effect = 0 }, ready ? cur.Dpi : 0,
            !ready ? "" : stage >= 0 ? "Stage " + (stage + 1) + " of " + state.Stages.Length + (cur.Dpi != state.Stages[stage] ? " · was " + state.Stages[stage] : "") : "",
            rate > 0 ? rate + " Hz report rate" + (ready && rate != state.Rate ? " after saving" : "") : "");
        loading = false;
    }

    void Changed(bool now)
    {
        if (loading || state == null) return;
        dirty = true;
        seq++;
        Sync();
        Calm();
        debounce.Stop();
        if (now) Send(); else debounce.Start();
    }

    protected override void Calm()
    {
        if (state == null) { Say(""); return; }
        if (!state.ProfileOk) { ShowBroken(); return; }
        Say(!dirty ? "Matches the mouse" : state.Savable ? "Applied to the mouse · not saved on it yet" : "Applied to the mouse · not saved on it");
    }

    string BackupId { get { return found.Slug; } }

    byte[] Usable(Backups.Entry e)
    {
        var d = state == null ? null : e.Get(state.Key);
        return d != null && state.Writable && d.Length == state.SectorSize && GMouse.Valid(d) ? d : null;
    }

    void ShowBroken()
    {
        const string bad = "The profile saved on the mouse is damaged. ";
        if (fix != null) Say(bad + "Click to restore the backup from " + When(fix) + ".", true, () => Restore(Usable(fix), "the backup from " + When(fix)));
        else if (state.Writable) Say(bad + "Click to restore the factory profile.", true, () => Restore(null, "the factory profile"));
        else Say(bad + "G-Lite can't repair it on this mouse.", true);
    }

    void ShowBackups()
    {
        bool can = state != null && state.Writable;
        BackupMenu(backupsBtn, BackupId, can ? "Restore the factory profile" : null, () => Restore(null, "the factory profile"),
            e => { var d = e.Get(state == null ? "" : state.Key); return d == null ? "another profile" : GMouse.Describe(d); },
            e => Usable(e) != null, e => Restore(Usable(e), "the backup from " + When(e)));
    }

    void Save()
    {
        var want = cur.Clone();
        var s = state;
        debounce.Stop();
        saveBtn.Enabled = false;
        Say("Saving to the mouse…");
        Do(h =>
        {
            var now = GMouse.ReadSector(h, s.Sector, s.SectorSize);
            if (!GMouse.Valid(now)) throw new Refused("The profile on the mouse is damaged, not touching it");
            Backups.Save(BackupId, new Dictionary<string, byte[]> { { s.Key, now } });
            GMouse.Store(h, s, GMouse.Build(now, want, s.Zones.Count));
        }, () =>
        {
            lightSet = false;
            Note = "Saved to the mouse";
            Load();
        });
    }

    void Restore(byte[] data, string what)
    {
        var s = state;
        if (s == null || !s.Writable) return;
        debounce.Stop();
        seq++;
        saveBtn.Enabled = false;
        Say("Restoring " + what + "…");
        Do(h =>
        {
            var now = GMouse.ReadSector(h, s.Sector, s.SectorSize);
            Backups.Save(BackupId, new Dictionary<string, byte[]> { { s.Key, now } });
            GMouse.Store(h, s, data ?? GMouse.Factory(GMouse.ReadSector(h, GMouse.RomSector, s.SectorSize), now));
        }, () =>
        {
            lightSet = false;
            Note = "Restored " + what;
            Load();
        });
    }

    void Send()
    {
        var want = cur.Clone();
        var s = state;
        int n = ++seq;
        Do(h =>
        {
            if (n != seq) return;
            if (sent == null || want.Dpi != sent.Dpi || want.Stage != sent.Stage)
            {
                bool saved = s.Onboard && want.Stage >= 0 && want.Stage < s.Stages.Length && s.Stages[want.Stage] == want.Dpi;
                if (saved) GMouse.SetStage(h, want.Stage); else GMouse.SetDpi(h, want.Dpi);
            }
            if (want.Light != null && s.Zones.Count > 0 && (sent == null || !want.Light.Same(sent.Light)))
            {
                GMouse.SetLight(h, s, want.Light);
                lightSet = !want.Light.Same(s.Light);
            }
            sent = want;
        }, () => { if (n == seq) Calm(); });
    }

    void SendRate(int hz)
    {
        Do(h => GMouse.SetRate(h, hz), () => Load());
    }

    public override void Load()
    {
        debounce.Stop();
        Say("Reading the mouse…");
        GMouse.State s = null;
        int n = seq;
        Do(h => s = GMouse.Read(h), () =>
        {
            state = s;
            fix = s.ProfileOk ? null : Backups.List(BackupId).FirstOrDefault(e => Usable(e) != null);
            int start = s.Stage >= 0 ? s.Stage : s.Profile != null && s.Stages.Length > 0 ? Math.Min((int)s.Profile[1], s.Stages.Length - 1) : -1;
            stored = new MouseSet { Dpi = s.Dpi, Light = s.Light, Stage = start };
            if (n == seq)
            {
                cur.Dpi = s.Dpi;
                cur.Rate = 0;
                cur.Stage = start;
                if (!lightSet || cur.Light == null) cur.Light = s.Light == null ? null : s.Light.Clone();
                if (!lightSet) dirty = false;
                sent = new MouseSet { Dpi = s.Dpi, Stage = start, Light = lightSet && cur.Light != null ? cur.Light.Clone() : s.Light };
            }
            Sync();
            Loaded();
        });
    }
}
