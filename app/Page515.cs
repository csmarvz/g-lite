using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Microsoft.Win32;

class KeyPicture : Ctl
{
    readonly List<RectangleF> keys = new List<RectangleF>();
    readonly List<Tuple<PointF, double>> ripples = new List<Tuple<PointF, double>>();
    readonly Stopwatch clock = Stopwatch.StartNew();
    readonly Timer timer = new Timer { Interval = 33 };
    readonly Random rnd = new Random();
    float uw, uh, unit, ox, oy;
    byte id = 0x01;
    byte[] prm = new byte[10];
    int bright = 100;
    double lastRipple;

    public KeyPicture(Model m)
    {
        Cursor = Cursors.Default;
        TabStop = false;
        AccessibleRole = AccessibleRole.Graphic;
        AccessibleName = m.Name + " lighting";
        foreach (var row in m.Rows)
        {
            float x = 0;
            int before = keys.Count;
            foreach (var t in row.Split('|'))
            {
                if (t.StartsWith("_")) { x += float.Parse(t.Substring(1), CultureInfo.InvariantCulture); continue; }
                var parts = t.Split(':');
                float w = parts.Length > 1 ? float.Parse(parts[1], CultureInfo.InvariantCulture) : 1;
                keys.Add(new RectangleF(x, uh, w, 1));
                x += w;
                uw = Math.Max(uw, x);
            }
            uh += keys.Count > before ? 1 : x;
        }
        timer.Tick += (s, e) => { if (Visible) Invalidate(); };
        VisibleChanged += (s, e) => timer.Enabled = Visible;
    }

    public void Display(Lighting l, int b)
    {
        id = l == null ? (byte)0xFF : l.Effect;
        prm = l == null ? new byte[10] : G515.Encode(l, 100);
        bright = b;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (unit > 0) ripples.Add(Tuple.Create(new PointF((e.X - ox) / unit, (e.Y - oy) / unit), clock.Elapsed.TotalSeconds));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = Begin(e);
        float pad = 8 * K;
        unit = Math.Min((Width - 2 * pad) / uw, (Height - 2 * pad) / uh);
        ox = (Width - uw * unit) / 2;
        oy = (Height - uh * unit) / 2;
        Ui.Fill(g, new RectangleF(ox - pad, oy - pad, uw * unit + 2 * pad, uh * unit + 2 * pad), 8 * K, Theme.Bed, Theme.Bed);
        double t = clock.Elapsed.TotalSeconds, period = KeyboardView.PeriodOf(id, prm);
        if (id == 0x17 && t - lastRipple > 1.8)
        {
            var k = keys[rnd.Next(keys.Count)];
            ripples.Add(Tuple.Create(new PointF(k.X + k.Width / 2, k.Y + 0.5f), t));
            lastRipple = t;
        }
        ripples.RemoveAll(r => t - r.Item2 > 3);
        foreach (var r in keys)
        {
            double glow = 0;
            if (id == 0x17)
                foreach (var rp in ripples)
                {
                    double d = Math.Sqrt(Math.Pow(r.X + r.Width / 2 - rp.Item1.X, 2) + Math.Pow(r.Y + 0.5 - rp.Item1.Y, 2));
                    double front = (t - rp.Item2) * 8 / period;
                    glow = Math.Max(glow, Math.Max(0, 1 - Math.Abs(d - front)) * Math.Max(0, 1 - (t - rp.Item2) / 3));
                }
            var c = KeyboardView.Lit(id, prm, (r.X + r.Width / 2) / uw, r.Y / uh, t, glow);
            int max = Math.Max(c.R, Math.Max(c.G, c.B));
            var fill = Theme.KeyOff;
            if (max > 0)
            {
                var full = Color.FromArgb(c.R * 255 / max, c.G * 255 / max, c.B * 255 / max);
                fill = Ui.Mix(Theme.KeyOff, full, max / 255.0 * bright / 100.0);
            }
            Ui.Fill(g, new RectangleF(ox + r.X * unit + 1.5f * K, oy + r.Y * unit + 1.5f * K, r.Width * unit - 3 * K, unit - 3 * K), 3 * K, fill, fill);
        }
    }
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
    static readonly Model Board = Model.Load("g515-tkl.json");

    Lighting cur = new Lighting(), stored;
    int brightness = 100, storedBrightness = -1;
    bool loading, dirty, claimed, unknownStored, broken;
    int previewSeq, edits;
    readonly Timer debounce = new Timer { Interval = 120 };

    readonly Label lightHead, colorHead, dirHead, speedHead, speedValue, brightValue;
    readonly Seg effects, dirs;
    readonly ColorRow colors;
    readonly Slider speed, bright;
    readonly Drop idleBox, dimBox, sleepBox;
    readonly Btn saveBtn, backupsBtn;
    readonly KeyPicture picture;
    Backups.Entry fix;

    public Page515(Found found, Hidpp fake, MouseEventHandler drag) : base(found, fake, drag, Board.Name, "keyboard", "profile.g515.")
    {
        debounce.Tick += (s, e) => { debounce.Stop(); Preview(); };
        if (found.Wpid != 0) Backups.Move(found.Wpid.ToString("X4"), BackupId);

        backupsBtn = BackupsButton("keyboard's lighting");
        backupsBtn.Click += (s, e) => ShowBackups();

        var pic = Ui.Add(this, new Box(), 20, 118, 616, 209);
        Ui.Text(pic, "Live on the keyboard", 14, 12, () => Theme.Dim, 9f, Ui.Semi);
        picture = Ui.Add(pic, new KeyPicture(Board), 14, 36, 588, 161);

        var light = Ui.Add(this, new Box(), 20, 339, 380, 272);
        lightHead = Ui.Text(light, "Lighting", 14, 12, () => Theme.Dim, 9f, Ui.Semi);
        Ui.Text(light, "Effect", 14, 40, () => Theme.Dim, 9f);
        effects = Ui.Add(light, new Seg { Items = G515.Effects.Select(d => d.Name).ToArray(), AccessibleName = "Effect" }, 14, 58, 352, 32);
        effects.Picked += i =>
        {
            var d = G515.Effects[i];
            cur.Effect = d.Id;
            if (d.Speed && (cur.Period < d.MinP || cur.Period > d.MaxP)) cur.Period = d.DefP;
            Changed(true);
        };
        colorHead = Ui.Text(light, "Color", 14, 98, () => Theme.Dim, 9f);
        colors = new ColorRow(light, 115);
        colors.Picked += c => { cur.Color = c; Changed(true); };

        dirHead = Ui.Text(light, "Direction", 14, 156, () => Theme.Dim, 9f);
        dirs = Ui.Add(light, new Seg { Items = DirText, AccessibleName = "Direction" }, 14, 174, 352, 32);
        dirs.Picked += i => { cur.Direction = Directions[i]; Changed(true); };

        Ui.Text(light, "Brightness", 14, 214, () => Theme.Dim, 9f);
        brightValue = Value(light, 14, 214, 172);
        bright = Ui.Add(light, new Slider { AccessibleName = "Brightness" }, 14, 234, 172, 24);
        bright.Changed += (s, e) =>
        {
            brightness = bright.Value;
            brightValue.Text = brightness + "%";
            dirty = true;
            edits++;
            saveBtn.Enabled = !broken;
            picture.Display(Known() ? cur : null, brightness);
            Calm();
            debounce.Stop();
            debounce.Start();
        };
        speedHead = Ui.Text(light, "Speed", 194, 214, () => Theme.Dim, 9f);
        speedValue = Value(light, 194, 214, 172);
        speed = Ui.Add(light, new Slider { AccessibleName = "Speed" }, 194, 234, 172, 24);
        speed.Changed += (s, e) =>
        {
            var d = G515.Def(cur.Effect);
            cur.Period = d.MaxP - (d.MaxP - d.MinP) * speed.Value / 100;
            Changed(false);
        };

        var idle = Ui.Add(this, new Box(), 412, 339, 224, 272);
        Ui.Text(idle, "When idle", 14, 12, () => Theme.Dim, 9f, Ui.Semi);
        idleBox = Choice(idle, "Dim after", IdleText, 40, i => cur.Idle = IdleSecs[i]);
        dimBox = Choice(idle, "Dim to", DimText, 98, i => cur.Dim = DimPcts[i]);
        sleepBox = Choice(idle, "Turn off after", SleepText, 156, i => cur.Sleep = SleepSecs[i]);

        saveBtn = Ui.Add(this, new Btn { Look = Look.Primary, Text = "Save to keyboard" }, 496, 635, 140, 32);
        saveBtn.Click += (s, e) => Save();

        Sync();
    }

    static string BackupId { get { return "G515"; } }

    public override void Restyle() { Sync(); }

    public override void Hidden() { Release(); }

    protected override void Lost() { claimed = false; }

    protected override string Current() { return cur.ToText(brightness); }

    protected override void Use(string text)
    {
        int b;
        cur = Lighting.FromText(text, out b);
        brightness = Math.Max(0, Math.Min(100, b));
        Changed(true);
    }

    Drop Choice(Control parent, string label, string[] items, int y, Action<int> set)
    {
        Ui.Text(parent, label, 14, y, () => Theme.Dim, 9f);
        var d = Ui.Add(parent, new Drop { AccessibleName = label }, 14, y + 18, 196, 32);
        d.Items.AddRange(items);
        d.Picked += i => { set(i); Changed(false); };
        return d;
    }

    bool Known() { return !(unknownStored && !dirty); }

    static int Near(int[] values, int v)
    {
        int best = 0;
        for (int i = 0; i < values.Length; i++) if (Math.Abs(values[i] - v) < Math.Abs(values[best] - v)) best = i;
        return best;
    }

    void Sync()
    {
        loading = true;
        var d = G515.Def(cur.Effect);
        bool known = Known();
        bool color = known && d != null && d.Color, dir = known && d != null && d.Direction, spd = known && d != null && d.Speed;
        lightHead.Text = known ? "Lighting" : broken ? "Lighting · half written on the keyboard" : "Lighting · factory animation";
        effects.Index = known ? Array.FindIndex(G515.Effects, e => e.Id == cur.Effect) : -1;
        Shade(colorHead, color);
        colors.Sync(cur.Color, color);
        Shade(dirHead, dir);
        dirs.Enabled = dir;
        dirs.Index = dir ? Array.IndexOf(Directions, cur.Direction) : -1;
        Shade(speedHead, spd);
        speed.Enabled = spd;
        if (spd) speed.Value = (d.MaxP - cur.Period) * 100 / (d.MaxP - d.MinP);
        speedValue.Text = spd ? speed.Value + "%" : "";
        speed.Invalidate();
        bright.Value = brightness;
        brightValue.Text = brightness + "%";
        bright.Invalidate();
        idleBox.Index = Near(IdleSecs, cur.Idle);
        dimBox.Index = Near(DimPcts, cur.Dim);
        sleepBox.Index = Near(SleepSecs, cur.Sleep);
        dimBox.Enabled = cur.Idle > 0;
        saveBtn.Enabled = dirty && !broken;
        picture.Display(known ? cur : null, brightness);
        loading = false;
    }

    void Changed(bool now)
    {
        if (loading) return;
        dirty = true;
        edits++;
        Sync();
        Calm();
        debounce.Stop();
        if (now) Preview(); else debounce.Start();
    }

    protected override void Calm()
    {
        if (broken) { ShowBroken(); return; }
        if (DynamicLightingOn())
            Say("Windows Dynamic Lighting is on and may override this.", true, () => Process.Start("ms-settings:personalization-lighting"), "Open Windows settings");
        else Say(dirty ? "Previewing · not saved to the keyboard" : stored == null ? "" : "Matches the keyboard");
    }

    static bool DynamicLightingOn()
    {
        using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Lighting"))
            return k != null && Equals(k.GetValue("AmbientLightingEnabled"), 1);
    }

    void ShowBroken()
    {
        const string half = "The lighting saved on the keyboard is half written.";
        var last = fix;
        if (last != null) Say(half + " Last backup: " + When(last) + ".", true, () => Restore(last.Files, "the backup from " + When(last)), "Restore backup");
        else Say(half, true, () => Restore(new G515.Files(), "the factory lighting"), "Restore factory lighting");
    }

    void ShowBackups()
    {
        BackupMenu(backupsBtn, BackupId, "Restore the factory lighting", () => Restore(new G515.Files(), "the factory lighting"),
            e => Backups.Describe(e.Files), e => G515.Good(e.Files), e => Restore(e.Files, "the backup from " + When(e)));
    }

    void Restore(G515.Files backup, string what)
    {
        debounce.Stop();
        previewSeq++;
        saveBtn.Enabled = false;
        Say("Restoring " + what + "…");
        Did("Restore " + what);
        int b = storedBrightness;
        Do(h =>
        {
            Backups.Save(BackupId, G515.Read(h), Who);
            G515.Restore(h, backup);
            claimed = false;
            if (b >= 0) G515.SetBrightness(h, b);
        }, () =>
        {
            Note = "Restored " + what;
            Load();
        });
    }

    protected override void Revert()
    {
        if (stored == null) return;
        cur = stored.Clone();
        brightness = storedBrightness;
        Release();
        Sync();
        Calm();
    }

    void Preview()
    {
        var l = cur.Clone();
        int b = brightness, seq = ++previewSeq;
        Do(h =>
        {
            if (seq != previewSeq) return;
            if (b != G515.Brightness(h)) G515.SetBrightness(h, b);
            if (claimed || !l.Same(stored)) { if (!claimed) { G515.Claim(h); claimed = true; } G515.Preview(h, l); }
        }, () => { if (seq == previewSeq) Calm(); });
    }

    void Save()
    {
        var l = cur.Clone();
        int b = brightness;
        saveBtn.Enabled = false;
        Say("Saving…");
        Did("Save to keyboard");
        Do(h =>
        {
            var f = G515.Snapshot(h);
            Backups.Save(BackupId, f, Who);
            G515.Store(h, f, l);
            claimed = false;
        }, () =>
        {
            stored = l;
            storedBrightness = b;
            dirty = false;
            unknownStored = false;
            Sync();
            Say("Saved to the keyboard");
        });
    }

    public override void Load()
    {
        dirty = false;
        debounce.Stop();
        Say("Reading the keyboard…");
        int n = edits;
        Lighting l = null;
        int b = 100;
        bool unknown = false, half = false;
        Do(h =>
        {
            G515.Files f;
            try { f = G515.Snapshot(h); }
            catch (HalfWritten) { half = true; b = G515.Brightness(h); return; }
            l = G515.Decode(f.UserProfile ?? f.FactoryProfile);
            unknown = l == null;
            if (l == null) l = new Lighting();
            b = G515.Brightness(h);
        }, () =>
        {
            broken = half;
            fix = half ? Backups.List(BackupId).FirstOrDefault(e => G515.Good(e.Files)) : null;
            storedBrightness = b;
            stored = half ? null : l.Clone();
            unknownStored = half || unknown;
            if (n == edits)
            {
                brightness = b;
                if (!half) cur = l;
            }
            Sync();
            Loaded();
        });
    }

    public void Release()
    {
        debounce.Stop();
        if (!dirty) return;
        dirty = false;
        int b = storedBrightness;
        var back = unknownStored || stored == null ? null : stored.Clone();
        previewSeq++;
        Do(h =>
        {
            if (b >= 0) G515.SetBrightness(h, b);
            if (!claimed) return;
            if (back != null) G515.Preview(h, back);
            G515.Release(h);
            claimed = false;
        });
    }
}
