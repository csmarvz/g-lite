using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
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
        TabStop = false;
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
    readonly Timer debounce = new Timer { Interval = 120 }, watch = new Timer { Interval = 5000 };
    readonly Toggle invert;
    readonly Label invertLabel;
    readonly Seg viewPick;
    readonly Box buttonsBox;
    readonly Control[] settingsArea;
    readonly Label[] rowNames = new Label[16];
    readonly Drop[] rowDrops = new Drop[16];
    readonly Label buttonsHint;
    readonly Field keyField;
    readonly Timer pressedTimer = new Timer { Interval = 1500 };
    bool showingButtons, spying;
    int recording = -1, pressedRow = -1;
    ButtonSpy spy;

    internal override bool Busy { get { return debounce.Enabled; } }

    readonly Label lightHead, colorHead, speedHead, speedValue, brightHead, brightValue, stageHead, dpiValue, rateHead, rateHint;
    readonly Seg effects, stages, rates;
    readonly ColorRow colors;
    readonly Slider speed, bright, dpi;
    readonly MousePicture picture;
    readonly Btn saveBtn, backupsBtn;
    Backups.Entry fix;
    readonly IMouse mouse;

    public PageMouse(Found found, Hidpp fake, MouseEventHandler drag, IMouse mouse) : base(found, fake, drag, found.Short, "mouse", "profile." + found.Slug + ".")
    {
        this.mouse = mouse;
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
            var d = Fx[i];
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
            if (!state.Onboard) { cur.Rate = state.Rates[i]; Edited(); SendRate(state.Rates[i]); return; }
            cur.Rate = state.Rates[i];
            Changed(true);
        };
        rateHint = Ui.Text(sens, "", 14, 212, () => Theme.Dim, 9f);
        rateHint.AutoSize = false;
        rateHint.Size = new Size(196, 24);
        invert = Ui.Add(sens, new Toggle { AccessibleName = "Invert scrolling", Visible = false }, 14, 242, 34, 18);
        invert.Changed += () => SetInvert(invert.Checked);
        invertLabel = Ui.Text(sens, "Invert scrolling", 56, 242, () => Theme.Text, 9f);
        invertLabel.Visible = false;
        invertLabel.Click += (s, e) => { invert.Checked = !invert.Checked; SetInvert(invert.Checked); };
        watch.Tick += (s, e) => Watch();
        if (!Manual) watch.Start();

        settingsArea = new Control[] { pic, light, sens };
        viewPick = Ui.Add(this, new Seg { Items = new[] { "Settings", "Buttons" }, AccessibleName = "View", Visible = false }, 440, 72, 196, 32);
        viewPick.Picked += i => ShowView(i == 1);
        buttonsBox = Ui.Add(this, new Box { Visible = false }, 20, 118, 616, 493);
        Ui.Text(buttonsBox, "Buttons", 14, 12, () => Theme.Dim, 9f, Ui.Semi);
        buttonsHint = Value(buttonsBox, 200, 12, 402);
        var actionNames = Bindings.Actions.Select(a => a.Name).Concat(new[] { "Keystroke…" }).ToList();
        for (int i = 0; i < 16; i++)
        {
            int row = i;
            rowNames[i] = Ui.Text(buttonsBox, "", 14, 45 + 28 * i, () => Theme.Text, 9.75f);
            rowNames[i].Visible = false;
            rowDrops[i] = Ui.Add(buttonsBox, new Drop { Visible = false }, 380, 40 + 28 * i, 222, 26);
            rowDrops[i].Items.AddRange(actionNames);
            rowDrops[i].Picked += k => PickAction(row, k);
        }
        keyField = Ui.Add(buttonsBox, new Field { Visible = false }, 380, 40, 222, 26);
        keyField.Box.KeyDown += (s, e) => { e.Handled = e.SuppressKeyPress = true; Recorded(e.KeyData); };
        pressedTimer.Tick += (s, e) => { pressedTimer.Stop(); pressedRow = -1; SyncButtons(); };

        saveBtn = Ui.Add(this, new Btn { Look = Look.Primary, Text = "Save to mouse" }, 496, 635, 140, 32);
        saveBtn.Click += (s, e) => Save();

        Sync();
    }

    EffectDef[] Fx { get { return state != null ? state.Effects : mouse.Effects; } }

    MouseLight Light()
    {
        if (cur.Light == null) cur.Light = new MouseLight();
        return cur.Light;
    }

    public override void Restyle() { Sync(); }

    protected override bool Unsaved { get { return dirty; } }

    protected override string Current() { return cur.ToText(); }

    protected override void Use(string text)
    {
        var s = MouseSet.FromText(text);
        var saved = stored != null && stored.Buttons != null ? stored.Buttons : cur.Buttons;
        if (cur.Buttons == null) s.Buttons = null;
        else if (s.Buttons == null) s.Buttons = Bindings.Copy(saved);
        else s.Buttons = Enumerable.Range(0, cur.Buttons.Length).Select(i => i > 0 && i < s.Buttons.Length ? s.Buttons[i] : (byte[])saved[i].Clone()).ToArray();
        if (state != null && state.Dpis.Length > 0) s.Dpi = state.Dpis[Near(state.Dpis, s.Dpi)];
        s.Stage = state != null && Array.IndexOf(state.Stages, s.Dpi) >= 0 ? Array.IndexOf(state.Stages, s.Dpi) : cur.Stage;
        cur = s;
        Changed(true);
    }

    protected override void Revert()
    {
        if (stored == null) return;
        recording = -1;
        cur = stored.Clone();
        Changed(true);
        dirty = lightSet && stored.Light == null;
        Sync();
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
        var d = l == null ? null : Fx.FirstOrDefault(e => e.Id == l.Effect);
        bool lit = ready && state.Zones.Count > 0, known = lit && d != null;
        bool color = known && d.Color, spd = known && d.Speed, br = known && l.Effect != 0x00;
        lightHead.Text = !ready || lit ? (l == null && lit ? "Lighting · as saved on the mouse" : "Lighting") : "Lighting · not supported on this mouse";
        effects.Items = Fx.Select(e => e.Name).ToArray();
        effects.Enabled = lit;
        effects.Index = known ? Array.FindIndex(Fx, e => e.Id == l.Effect) : -1;
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
        rateHead.Text = "Report rate" + (ready && state.Rates.Length > 0 ? " (Hz)" : "") + (ready && state.Onboard && state.Savable ? " · on save" : "");
        rateHint.Text = !ready || !state.Onboard || state.Savable ? "" : "Set by the saved profile.";
        invert.Visible = invertLabel.Visible = ready && state.CanInvert;
        invert.Checked = ready && state.Inverted;
        viewPick.Visible = ready && state.ButtonCount > 0 && state.Buttons != null;
        if (!viewPick.Visible && showingButtons) ShowView(false);
        viewPick.Index = showingButtons ? 1 : 0;
        SyncButtons();
        saveBtn.Enabled = ready && state.Savable && dirty;
        saveBtn.Visible = ready ? state.Writable : mouse.HasFactory;
        backupsBtn.Visible = mouse.HasFactory || ready && state.Writable;

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
        Edited();
        Sync();
        Calm();
        debounce.Stop();
        if (now) Send(); else debounce.Start();
    }

    protected override void Calm()
    {
        if (state == null) { Say(""); return; }
        if (!state.ProfileOk) { ShowBroken(); return; }
        bool buttons = stored != null && !Bindings.Same(cur.Buttons, stored.Buttons);
        bool rate = stored != null && state.Onboard && cur.Rate > 0 && cur.Rate != stored.Rate;
        if (dirty && (buttons || rate)) { Say((buttons && rate ? "Buttons and report rate change" : buttons ? "Buttons change" : "Report rate changes") + " when saved to the mouse"); return; }
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
        const string bad = "The profile saved on the mouse is damaged.";
        if (fix != null) Say(bad + " Last good backup: " + When(fix) + ".", true, () => Restore(Usable(fix), "the backup from " + When(fix)), "Restore backup");
        else if (state.Writable && mouse.HasFactory) Say(bad, true, () => Restore(null, "the factory profile"), "Restore factory profile");
        else Say(bad + " G-Lite can't repair it on this mouse.", true);
    }

    void ShowBackups()
    {
        bool can = state != null && state.Writable && !state.Fresh && mouse.HasFactory;
        BackupMenu(backupsBtn, BackupId, can ? "Restore the factory profile" : null, () => Restore(null, "the factory profile"),
            e => { var d = e.Get(state == null ? "" : state.Key); return d == null ? "another profile" : mouse.Describe(d); },
            e => Usable(e) != null, e => Restore(Usable(e), "the backup from " + When(e)));
    }

    void Save()
    {
        var want = cur.Clone();
        var s = state;
        debounce.Stop();
        saveBtn.Enabled = false;
        Say("Saving to the mouse…");
        Did("Save to mouse");
        Do(h =>
        {
            if (s.Fresh) { mouse.Setup(h, s, want); return; }
            var now = mouse.ReadProfile(h, s);
            if (!GMouse.Valid(now)) throw new Refused("The profile on the mouse is damaged, not touching it");
            Backups.Save(BackupId, new Dictionary<string, byte[]> { { s.Key, now } }, Who);
            mouse.Save(h, s, now, want);
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
        if (s == null || !s.Writable || s.Fresh) return;
        debounce.Stop();
        seq++;
        saveBtn.Enabled = false;
        Say("Restoring " + what + "…");
        Did("Restore " + what);
        Do(h =>
        {
            var now = mouse.ReadProfile(h, s);
            Backups.Save(BackupId, new Dictionary<string, byte[]> { { s.Key, now } }, Who);
            mouse.Restore(h, s, now, data);
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
            mouse.SendDpi(h, s, want, sent);
            if (!s.Onboard && want.Rate > 0 && want.Rate != s.Rate && (sent == null || sent.Rate != want.Rate) && s.Rates.Contains(want.Rate)) mouse.SetRate(h, want.Rate);
            if (want.Light != null && s.Zones.Count > 0 && (sent == null || !want.Light.Same(sent.Light)))
            {
                mouse.SetLight(h, s, want.Light);
                lightSet = !want.Light.Same(s.Light);
            }
            sent = want;
        }, () => { if (n == seq) Calm(); });
    }

    void ShowView(bool buttons)
    {
        showingButtons = buttons;
        recording = -1;
        foreach (var c in settingsArea) c.Visible = !buttons;
        buttonsBox.Visible = buttons;
        if (buttons) StartSpy(); else StopSpy();
        viewPick.Index = buttons ? 1 : 0;
        SyncButtons();
    }

    void SyncButtons()
    {
        int count = state == null || state.Buttons == null || cur.Buttons == null ? 0 : Math.Min(state.ButtonCount, cur.Buttons.Length);
        var names = Bindings.Names(found.Wpid, count);
        bool can = state != null && state.Savable;
        for (int i = 0; i < 16; i++)
        {
            bool on = i < count;
            rowNames[i].Visible = on;
            rowDrops[i].Visible = on && i != recording;
            if (!on) continue;
            rowNames[i].Text = names[i] + (i == pressedRow ? "  ·  pressed" : "");
            rowNames[i].ForeColor = i == pressedRow ? Theme.Accent : Theme.Text;
            rowDrops[i].AccessibleName = names[i];
            int k = Bindings.Find(cur.Buttons[i]);
            rowDrops[i].Index = k;
            if (k < 0) rowDrops[i].Text = Bindings.Describe(cur.Buttons[i]);
            rowDrops[i].Enabled = can && i != 0 && recording < 0;
        }
        keyField.Visible = recording >= 0 && recording < count;
        if (keyField.Visible) keyField.Top = rowDrops[recording].Top;
        buttonsHint.Text = recording >= 0 ? "Press the keys, or Esc to cancel." : spying ? "Press a button on the mouse to find it." : "";
    }

    void PickAction(int row, int k)
    {
        if (loading || state == null || cur.Buttons == null) return;
        if (k >= Bindings.Actions.Length)
        {
            recording = row;
            SyncButtons();
            keyField.Box.Text = "";
            keyField.Box.Focus();
            return;
        }
        cur.Buttons[row] = (byte[])Bindings.Actions[k].Bytes.Clone();
        ButtonsEdited();
    }

    internal void Recorded(Keys keys)
    {
        if (recording < 0 || cur.Buttons == null) return;
        var key = keys & Keys.KeyCode;
        if (key == Keys.Escape) { recording = -1; SyncButtons(); return; }
        int usage = Usage(key);
        if (usage == 0) return;
        int modifiers = ((keys & Keys.Control) != 0 ? 1 : 0) | ((keys & Keys.Shift) != 0 ? 2 : 0) | ((keys & Keys.Alt) != 0 ? 4 : 0);
        cur.Buttons[recording] = Bindings.Key(modifiers, usage);
        recording = -1;
        ButtonsEdited();
    }

    static int Usage(Keys k)
    {
        if (k >= Keys.A && k <= Keys.Z) return 0x04 + (int)k - (int)Keys.A;
        if (k >= Keys.D1 && k <= Keys.D9) return 0x1E + (int)k - (int)Keys.D1;
        if (k >= Keys.F1 && k <= Keys.F12) return 0x3A + (int)k - (int)Keys.F1;
        switch (k)
        {
            case Keys.D0: return 0x27;
            case Keys.Enter: return 0x28;
            case Keys.Back: return 0x2A;
            case Keys.Tab: return 0x2B;
            case Keys.Space: return 0x2C;
            case Keys.OemMinus: return 0x2D;
            case Keys.Oemplus: return 0x2E;
            case Keys.OemOpenBrackets: return 0x2F;
            case Keys.OemCloseBrackets: return 0x30;
            case Keys.OemPipe: return 0x31;
            case Keys.OemSemicolon: return 0x33;
            case Keys.OemQuotes: return 0x34;
            case Keys.Oemtilde: return 0x35;
            case Keys.Oemcomma: return 0x36;
            case Keys.OemPeriod: return 0x37;
            case Keys.OemQuestion: return 0x38;
            case Keys.PrintScreen: return 0x46;
            case Keys.Scroll: return 0x47;
            case Keys.Pause: return 0x48;
            case Keys.Insert: return 0x49;
            case Keys.Home: return 0x4A;
            case Keys.PageUp: return 0x4B;
            case Keys.Delete: return 0x4C;
            case Keys.End: return 0x4D;
            case Keys.PageDown: return 0x4E;
            case Keys.Right: return 0x4F;
            case Keys.Left: return 0x50;
            case Keys.Down: return 0x51;
            case Keys.Up: return 0x52;
        }
        return 0;
    }

    void ButtonsEdited()
    {
        if (state == null) return;
        dirty = true;
        Edited();
        Sync();
        Calm();
    }

    void StartSpy()
    {
        if (spying || found.Path == null || !found.Has(0x8110)) return;
        spying = true;
        byte feature = 0;
        Do(h => { feature = h.F(0x8110); h.Call(feature, 1); }, () =>
        {
            if (!spying || spy != null) return;
            try { spy = new ButtonSpy(found.Path, found.Index, feature, bits => { if (IsHandleCreated) BeginInvoke((Action)(() => Pressed(bits))); }); }
            catch (IOException) { }
            SyncButtons();
        });
    }

    void StopSpy()
    {
        if (!spying) return;
        spying = false;
        if (spy != null) { spy.Dispose(); spy = null; }
        Do(h => { try { h.Call(h.F(0x8110), 2); } catch (HidppError) { } });
    }

    internal void Pressed(int bits)
    {
        if (bits == 0) return;
        pressedRow = Enumerable.Range(0, 16).First(i => (bits >> i & 1) != 0);
        pressedTimer.Stop();
        pressedTimer.Start();
        SyncButtons();
    }

    public override void Shown()
    {
        base.Shown();
        if (showingButtons) StartSpy();
    }

    public override void Hidden()
    {
        StopSpy();
        base.Hidden();
    }

    string InvertKey { get { return "wheel.invert." + found.Slug; } }

    void SetInvert(bool on)
    {
        if (state == null) return;
        Settings.Set(InvertKey, on ? "1" : "0");
        Did(on ? "Invert scrolling" : "Normal scrolling");
        Do(h => mouse.SetInvert(h, on), () => { state.Inverted = on; Sync(); });
    }

    internal void Watch()
    {
        if (Settings.Get(InvertKey) != "1") return;
        Do(h =>
        {
            try { mouse.SetInvert(h, true); }
            catch (NotSupportedException) { }
            catch (HidppError) { }
            catch (TimeoutException) { }
            catch (Refused) { }
        });
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            watch.Dispose();
            pressedTimer.Dispose();
            if (spy != null) spy.Dispose();
        }
        base.Dispose(disposing);
    }

    void SendRate(int hz)
    {
        Do(h => mouse.SetRate(h, hz), () => Load());
    }

    public override void Load()
    {
        debounce.Stop();
        Say("Reading the mouse…");
        GMouse.State s = null;
        int n = seq;
        Do(h => s = mouse.Read(h), () =>
        {
            state = s;
            fix = s.ProfileOk ? null : Backups.List(BackupId).FirstOrDefault(e => Usable(e) != null);
            int start = s.Stage >= 0 ? s.Stage : s.Profile != null && s.Stages.Length > 0 ? Math.Min(mouse.DefaultStage(s.Profile), s.Stages.Length - 1) : -1;
            stored = new MouseSet { Dpi = s.Dpi, Rate = s.Rate, Light = s.Light, Stage = start, Buttons = Bindings.Copy(s.Buttons) };
            if (n == seq)
            {
                cur.Dpi = s.Dpi;
                cur.Rate = s.Rate;
                cur.Stage = start;
                cur.Buttons = Bindings.Copy(s.Buttons);
                recording = -1;
                if (!lightSet || cur.Light == null) cur.Light = s.Light == null ? null : s.Light.Clone();
                if (!lightSet) dirty = false;
                sent = new MouseSet { Dpi = s.Dpi, Stage = start, Light = lightSet && cur.Light != null ? cur.Light.Clone() : s.Light };
            }
            Sync();
            Loaded();
        });
    }
}
