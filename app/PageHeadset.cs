using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

class HeadsetPicture : Ctl
{
    int battery = -1, millivolts;
    bool approx, charging;
    string line = "";

    public HeadsetPicture()
    {
        Cursor = Cursors.Default;
        TabStop = false;
        AccessibleRole = AccessibleRole.Graphic;
        AccessibleName = "Headset";
    }

    public void Display(int pct, bool estimated, int mv, bool charge, string text)
    {
        battery = pct;
        approx = estimated;
        millivolts = mv;
        charging = charge;
        line = text;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = Begin(e);
        float h = Height - 16 * K, w = h * 0.9f, x = 62 * K, y = 8 * K;
        float cupW = w * 0.26f, cupH = h * 0.42f, cupY = y + h * 0.5f;
        using (var band = new Pen(Theme.KeyOff, 9 * K) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            g.DrawArc(band, x + cupW / 2, y + 4 * K, w - cupW, h * 1.05f, 180, 180);
        using (var boom = new Pen(Theme.KeyOff, 5 * K) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            g.DrawBezier(boom, x + cupW * 0.6f, cupY + cupH * 0.8f, x + cupW * 0.4f, y + h * 1.02f, x + w * 0.28f, y + h * 0.98f, x + w * 0.4f, y + h * 0.96f);
        foreach (var cx in new[] { x, x + w - cupW })
        {
            Ui.Fill(g, new RectangleF(cx, cupY, cupW, cupH), cupW * 0.42f, Theme.KeyOff, Theme.Border);
            Ui.Fill(g, new RectangleF(cx + cupW * 0.22f, cupY + cupH * 0.16f, cupW * 0.56f, cupH * 0.68f), cupW * 0.26f, Theme.Bed, Theme.Bed);
        }
        using (var b = new SolidBrush(Theme.Accent)) g.FillEllipse(b, x + w * 0.4f - 4 * K, y + h * 0.96f - 4 * K, 8 * K, 8 * K);

        float tx = x + w + 70 * K, top = Height / 2f - 46 * K;
        var big = Ui.Get(27f, Ui.Semi);
        var text = battery < 0 ? "–" : battery + "%";
        Ui.Draw(g, text, big, new RectangleF(tx, top, Width - tx, 48 * K), Theme.Text, TextFormatFlags.Left | TextFormatFlags.Bottom);
        float bw = TextRenderer.MeasureText(g, text, big, Size.Empty, TextFormatFlags.NoPadding).Width;
        Ui.Draw(g, charging ? "charging" : "battery", Ui.Get(11f, Ui.Semi), new RectangleF(tx + bw + 8 * K, top, 120 * K, 44 * K), Theme.Dim, TextFormatFlags.Left | TextFormatFlags.Bottom);
        var small = Ui.Get(9.75f, Ui.Regular);
        var volts = millivolts > 0 ? (millivolts / 1000.0).ToString("0.00", CultureInfo.InvariantCulture) + " V" + (approx ? ", estimated from the voltage" : "") : "";
        Ui.Draw(g, volts, small, new RectangleF(tx, top + 58 * K, Width - tx, 20 * K), Theme.Dim, TextFormatFlags.Left);
        Ui.Draw(g, line, small, new RectangleF(tx, top + 80 * K, Width - tx, 20 * K), Theme.Dim, TextFormatFlags.Left);
    }
}

class PageHeadset : DevicePage
{
    HeadSet cur = new HeadSet(), stored, sent;
    GHeadset.State state;
    bool loading, dirty;
    int seq;
    int[] offs = GHeadset.OffMinutes;
    readonly Timer debounce = new Timer { Interval = 120 }, testing = new Timer { Interval = 100 };

    internal override bool Busy { get { return debounce.Enabled; } }
    Mic mic;
    DateTime testEnd;

    readonly Label sideHead, sideValue, offHead, firmware;
    readonly Btn testBtn;
    readonly Slider sidetone;
    readonly Drop offBox;
    readonly HeadsetPicture picture;

    public PageHeadset(Found found, Hidpp fake, MouseEventHandler drag) : base(found, fake, drag, found.Short, "headset", "profile." + found.Slug + ".")
    {
        debounce.Tick += (s, e) => { debounce.Stop(); Send(); };

        var pic = Ui.Add(this, new Box(), 20, 118, 616, 209);
        Ui.Text(pic, "Headset", 14, 12, () => Theme.Dim, 9f, Ui.Semi);
        picture = Ui.Add(pic, new HeadsetPicture(), 14, 36, 588, 161);

        var sound = Ui.Add(this, new Box(), 20, 339, 380, 272);
        Ui.Text(sound, "Sound", 14, 12, () => Theme.Dim, 9f, Ui.Semi);
        sideHead = Ui.Text(sound, "Sidetone", 14, 40, () => Theme.Dim, 9f);
        sideValue = Value(sound, 14, 40, 352);
        sidetone = Ui.Add(sound, new Slider { AccessibleName = "Sidetone" }, 14, 60, 352, 24);
        sidetone.Changed += (s, e) => { cur.Sidetone = sidetone.Value; Changed(false); };
        var hint = Ui.Text(sound, "How much of your own voice you hear while you talk. It works while an app is using the microphone, like a call or a voice chat.", 14, 92, () => Theme.Dim, 9f);
        hint.AutoSize = false;
        hint.Size = new Size(352, 54);
        testBtn = Ui.Add(sound, new Btn { Text = "Test sidetone", Icon = "headset" }, 14, 150, 150, 32);
        testBtn.Click += (s, e) => { if (mic == null) StartTest(); else StopTest(); };
        testing.Tick += (s, e) =>
        {
            if (mic == null) return;
            if (DateTime.UtcNow > testEnd) { StopTest(); return; }
            mic.Keep();
        };

        var power = Ui.Add(this, new Box(), 412, 339, 224, 272);
        Ui.Text(power, "Power", 14, 12, () => Theme.Dim, 9f, Ui.Semi);
        offHead = Ui.Text(power, "Turn off after", 14, 40, () => Theme.Dim, 9f);
        offBox = Ui.Add(power, new Drop { AccessibleName = "Turn off after" }, 14, 58, 196, 32);
        offBox.Picked += i => { cur.Off = offs[i]; Changed(true); };
        Ui.Text(power, "Firmware", 14, 98, () => Theme.Dim, 9f);
        firmware = Ui.Text(power, "", 14, 116, () => Theme.Text, 9.75f);
        firmware.AutoSize = false;
        firmware.AutoEllipsis = true;
        firmware.Size = new Size(196, 22);
        var note = Ui.Text(power, "Changes apply right away.", 14, 156, () => Theme.Dim, 9f);
        note.AutoSize = false;
        note.Size = new Size(196, 48);

        Sync();
    }

    static string OffText(int m) { return m == 0 ? "Never" : m + " min"; }

    void StartTest()
    {
        int dev = Mic.Find(found.Short);
        if (dev < 0) { Say("Couldn't find the headset's microphone in Windows", true); return; }
        try { mic = new Mic(dev); }
        catch (Exception ex) { Say(ex.Message, true); return; }
        testEnd = DateTime.UtcNow.AddSeconds(30);
        testing.Start();
        testBtn.Text = "Stop the test";
        testBtn.Look = Look.Primary;
        testBtn.Invalidate();
        Say("Talk now. You hear yourself at the sidetone level. The test stops after 30 s.");
    }

    void StopTest()
    {
        testing.Stop();
        if (mic != null) { mic.Dispose(); mic = null; }
        testBtn.Text = "Test sidetone";
        testBtn.Look = Look.Normal;
        testBtn.Invalidate();
        Calm();
    }

    public override void Hidden() { if (mic != null) StopTest(); }

    protected override void Dispose(bool disposing)
    {
        if (disposing && mic != null) StopTest();
        base.Dispose(disposing);
    }

    public override void Restyle() { Sync(); }

    protected override bool Unsaved { get { return dirty; } }

    protected override string Current() { return cur.ToText(); }

    protected override void Use(string text)
    {
        var s = HeadSet.FromText(text);
        s.Sidetone = Math.Max(0, Math.Min(100, s.Sidetone));
        cur = s;
        Changed(true);
    }

    protected override void Revert()
    {
        if (stored == null) return;
        cur = stored.Clone();
        Changed(true);
        dirty = false;
        Calm();
    }

    void Sync()
    {
        loading = true;
        bool ready = state != null, hasSide = ready && state.HasSidetone, hasOff = ready && state.HasOff;
        Shade(sideHead, hasSide);
        sidetone.Enabled = hasSide;
        testBtn.Enabled = hasSide;
        sidetone.Value = cur.Sidetone;
        sideValue.Text = hasSide ? cur.Sidetone + "%" : "";
        sidetone.Invalidate();
        offs = cur.Off >= 0 && !GHeadset.OffMinutes.Contains(cur.Off) ? GHeadset.OffMinutes.Concat(new[] { cur.Off }).OrderBy(m => m).ToArray() : GHeadset.OffMinutes;
        offBox.Items.Clear();
        offBox.Items.AddRange(offs.Select(OffText));
        offBox.Index = hasOff ? Array.IndexOf(offs, cur.Off) : -1;
        offBox.Enabled = hasOff;
        offBox.Invalidate();
        Shade(offHead, hasOff);
        firmware.Text = found.Firmware == "" ? "–" : found.Firmware;
        int mv = ready && state.Millivolts > 0 ? state.Millivolts : found.Millivolts;
        bool charging = ready && state.Charging;
        int pct = !charging ? (mv > 0 ? Found.FromVolts(mv) : found.Battery) : found.Battery >= 0 ? found.Battery : Found.FromChargingVolts(mv);
        picture.Display(pct, mv > 0 || found.Approx, mv, ready && state.Charging,
            hasOff ? (cur.Off == 0 ? "Stays on when idle" : "Turns off after " + cur.Off + " min idle") : "");
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
        if (mic != null) return;
        if (state != null && !state.HasSidetone && !state.HasOff && state.Millivolts == 0) { Say("The headset didn't answer. Is it on?", true, Load, "Try again"); return; }
        Say(state == null ? "" : dirty ? "Applied to the headset" : "Matches the headset");
    }

    void Send()
    {
        var want = cur.Clone();
        var s = state;
        int n = ++seq;
        Do(h =>
        {
            if (n != seq) return;
            if (s.HasSidetone && (sent == null || want.Sidetone != sent.Sidetone)) GHeadset.SetSidetone(h, want.Sidetone);
            if (s.HasOff && want.Off >= 0 && (sent == null || want.Off != sent.Off)) GHeadset.SetOff(h, want.Off);
            sent = want;
        }, () => { if (n == seq) Calm(); });
    }

    public override void Load()
    {
        debounce.Stop();
        Say("Reading the headset…");
        GHeadset.State s = null;
        int n = seq;
        bool autoOff = Demo || found.HasAutoOff;
        Do(h => s = GHeadset.Read(h, autoOff), () =>
        {
            state = s;
            stored = s.Set.Clone();
            if (n == seq)
            {
                cur = s.Set.Clone();
                sent = s.Set.Clone();
                dirty = false;
            }
            Sync();
            Loaded();
        });
    }
}
