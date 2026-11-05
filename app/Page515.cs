using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
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

interface IPage
{
    void Shown();
    void Hidden();
    Task Queue { get; }
}

class Page515 : Control, IPage
{
    public const string Prefix = "profile.g515.";
    static readonly int[] Swatches = { 0xFFFFFF, 0xFF2020, 0xFF8000, 0xFFD000, 0x30D050, 0x3080FF };
    static readonly int[] IdleSecs = { 0, 15, 30, 60, 120, 300, 600 };
    static readonly string[] IdleText = { "Never", "15 s", "30 s", "1 min", "2 min", "5 min", "10 min" };
    static readonly int[] DimPcts = { 0, 10, 25, 50, 75 };
    static readonly string[] DimText = { "Dark", "10%", "25%", "50%", "75%" };
    static readonly int[] SleepSecs = { 0, 60, 120, 300, 600, 900, 1800 };
    static readonly string[] SleepText = { "Never", "1 min", "2 min", "5 min", "10 min", "15 min", "30 min" };
    static readonly int[] Directions = { 6, 1, 3, 8 };
    static readonly string[] DirText = { "Left", "Right", "Center out", "Center in" };
    const string OnKeyboard = "As on the keyboard";

    Lighting cur = new Lighting(), stored;
    int brightness = 100, storedBrightness = -1;
    bool loading, dirty, claimed, unknownStored, naming;
    Hidpp dev;
    readonly FakeHidpp fake;
    readonly bool demo;
    Found found;
    string readAt = "";
    Task queue = Task.FromResult(0);
    int previewSeq;
    readonly Timer debounce = new Timer { Interval = 120 };

    readonly Label sub, lightHead, colorHead, dirHead, speedHead, speedValue, brightValue;
    readonly Seg effects, dirs;
    readonly Swatch[] swatches = new Swatch[Swatches.Length];
    readonly Swatch custom, more;
    int lastCustom = -1;
    readonly Slider speed, bright;
    readonly Drop profiles, idleBox, dimBox, sleepBox;
    readonly Field nameField;
    readonly Btn newBtn, delBtn, saveBtn;
    readonly StatusLine status;
    readonly KeyPicture picture;
    public string Note;

    public Page515(Found found, FakeHidpp fake, MouseEventHandler drag)
    {
        this.found = found;
        this.fake = fake;
        demo = fake != null;
        Ui.Style(this, () => BackColor = Theme.Bg);
        MouseDown += drag;
        debounce.Tick += (s, e) => { debounce.Stop(); Preview(); };
        var model = Model.Load("g515-tkl.json");

        Ui.Text(this, model.Name, 20, 14, () => Theme.Text, 13.5f, Ui.Semi).MouseDown += drag;
        sub = Ui.Text(this, "", 21, 44, () => Theme.Dim, 9f);
        sub.MouseDown += drag;
        var reread = Ui.Add(this, new Btn { Look = Look.Ghost, Icon = "refresh", AccessibleName = "Read again from the keyboard" }, 656 - 12 - 108, 10, 36, 28);
        reread.Click += (s, e) => Load();

        Ui.Text(this, "Profile", 20, 79, () => Theme.Dim, 9.75f);
        profiles = Ui.Add(this, new Drop { AccessibleName = "Profile" }, 74, 72, 210, 32);
        profiles.Picked += PickSet;
        nameField = Ui.Add(this, new Field { Visible = false }, 74, 72, 210, 32);
        nameField.Box.KeyDown += (s, e) =>
        {
            if (e.KeyCode == Keys.Enter) { SaveSet(nameField.Box.Text.Trim()); e.SuppressKeyPress = true; }
            if (e.KeyCode == Keys.Escape) { EndName(); e.SuppressKeyPress = true; }
        };
        newBtn = Ui.Add(this, new Btn { Text = "New" }, 292, 72, 68, 32);
        newBtn.Click += (s, e) => { if (naming) SaveSet(nameField.Box.Text.Trim()); else StartName(); };
        delBtn = Ui.Add(this, new Btn { Text = "Delete" }, 368, 72, 76, 32);
        delBtn.Click += (s, e) =>
        {
            if (naming) EndName();
            else if (profiles.Index > 0) { Settings.Remove(Prefix + profiles.Items[profiles.Index]); LoadSets(null); }
        };

        var pic = Ui.Add(this, new Box(), 20, 118, 616, 209);
        Ui.Text(pic, "Live on the keyboard", 14, 12, () => Theme.Dim, 9f, Ui.Semi);
        picture = Ui.Add(pic, new KeyPicture(model), 14, 36, 588, 161);

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
        for (int i = 0; i < Swatches.Length; i++)
        {
            int c = Swatches[i];
            swatches[i] = Ui.Add(light, new Swatch { Fill = Color.FromArgb(c | unchecked((int)0xFF000000)), AccessibleName = "Color " + c.ToString("X6") }, 10 + i * 36, 115, 34, 34);
            swatches[i].Click += (s, e) => { cur.Color = c; Changed(true); };
        }
        custom = Ui.Add(light, new Swatch { AccessibleName = "Your color" }, 10 + Swatches.Length * 36, 115, 34, 34);
        custom.Click += (s, e) => { if (lastCustom >= 0) { cur.Color = lastCustom; Changed(true); } };
        more = Ui.Add(light, new Swatch { AccessibleName = "Pick a color" }, 10 + Swatches.Length * 36, 115, 34, 34);
        more.Click += (s, e) => PickColor();

        dirHead = Ui.Text(light, "Direction", 14, 156, () => Theme.Dim, 9f);
        dirs = Ui.Add(light, new Seg { Items = DirText, AccessibleName = "Direction" }, 14, 174, 352, 32);
        dirs.Picked += i => { cur.Direction = Directions[i]; Changed(true); };

        Ui.Text(light, "Brightness", 14, 214, () => Theme.Dim, 9f);
        brightValue = Value(light, 14, 214);
        bright = Ui.Add(light, new Slider { AccessibleName = "Brightness" }, 14, 234, 172, 24);
        bright.Changed += (s, e) =>
        {
            brightness = bright.Value;
            brightValue.Text = brightness + "%";
            dirty = true;
            saveBtn.Enabled = true;
            picture.Display(Known() ? cur : null, brightness);
            Calm();
            debounce.Stop();
            debounce.Start();
        };
        speedHead = Ui.Text(light, "Speed", 194, 214, () => Theme.Dim, 9f);
        speedValue = Value(light, 194, 214);
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

        var line = Ui.Add(this, new Control(), 0, 623, 656, 1);
        Ui.Style(line, () => line.BackColor = Theme.Border);
        status = Ui.Add(this, new StatusLine(), 20, 633, 460, 36);
        saveBtn = Ui.Add(this, new Btn { Look = Look.Primary, Text = "Save to keyboard" }, 496, 635, 140, 32);
        saveBtn.Click += (s, e) => Save();

        LoadSets(null);
        Sync();
        SetFound(found);
    }

    public void Restyle() { Sync(); }

    public void Shown() { Load(); }

    public void Hidden() { Release(); }

    public void SetFound(Found f)
    {
        found = f;
        sub.Text = f.Link + (f.Battery >= 0 ? " · " + f.BatteryText : "") + (readAt != "" ? " · read at " + readAt : "");
    }

    public Task Queue { get { return queue; } }

    static Label Value(Control parent, int x, int y)
    {
        var l = Ui.Text(parent, "", x, y, () => Theme.Dim, 9f);
        l.AutoSize = false;
        l.Size = new Size(172, 18);
        l.TextAlign = ContentAlignment.TopRight;
        return l;
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
        lightHead.Text = known ? "Lighting" : "Lighting · factory animation";
        effects.Index = known ? Array.FindIndex(G515.Effects, e => e.Id == cur.Effect) : -1;
        bool isCustom = !Swatches.Contains(cur.Color);
        if (isCustom) lastCustom = cur.Color;
        Shade(colorHead, color);
        for (int i = 0; i < swatches.Length; i++) { swatches[i].Enabled = color; swatches[i].On = color && Swatches[i] == cur.Color; swatches[i].Invalidate(); }
        custom.Visible = lastCustom >= 0;
        custom.Fill = Color.FromArgb(lastCustom | unchecked((int)0xFF000000));
        custom.Enabled = color;
        custom.On = color && isCustom;
        custom.Invalidate();
        more.Left = custom.Left + (custom.Visible ? (int)Math.Round(36 * Ui.K) : 0);
        more.Enabled = color;
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
        saveBtn.Enabled = dirty;
        picture.Display(known ? cur : null, brightness);
        loading = false;
    }

    static void Shade(Label l, bool on) { l.ForeColor = on ? Theme.Dim : Ui.Mix(Theme.Card, Theme.Dim, 0.45); }

    void Changed(bool now)
    {
        if (loading) return;
        dirty = true;
        Sync();
        Calm();
        debounce.Stop();
        if (now) Preview(); else debounce.Start();
    }

    public void Say(string text, bool warn = false, Action click = null) { status.Set(text, warn, click); }

    void Post(string text, bool warn) { if (IsHandleCreated) BeginInvoke((Action)(() => Say(text, warn))); }

    void Calm()
    {
        if (DynamicLightingOn())
            Say("Windows Dynamic Lighting is on and may override this. Click to open its settings.", true, () => Process.Start("ms-settings:personalization-lighting"));
        else Say(dirty ? "Previewing · not saved to the keyboard" : stored == null ? "" : "Matches the keyboard");
    }

    static bool DynamicLightingOn()
    {
        using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Lighting"))
            return k != null && Equals(k.GetValue("AmbientLightingEnabled"), 1);
    }

    void PickColor()
    {
        using (var dlg = new ColorDialog { FullOpen = true, Color = Color.FromArgb(cur.Color | unchecked((int)0xFF000000)) })
            if (dlg.ShowDialog(FindForm()) == DialogResult.OK) { cur.Color = dlg.Color.ToArgb() & 0xFFFFFF; Changed(true); }
        FindForm().Activate();
    }

    void LoadSets(string select)
    {
        profiles.Items.Clear();
        profiles.Items.Add(OnKeyboard);
        foreach (var k in Settings.Keys().Where(k => k.StartsWith(Prefix)).Select(k => k.Substring(Prefix.Length)).OrderBy(k => k)) profiles.Items.Add(k);
        profiles.Index = select == null ? 0 : Math.Max(0, profiles.Items.IndexOf(select));
        ShowSets();
    }

    void ShowSets()
    {
        profiles.Visible = !naming;
        nameField.Visible = naming;
        newBtn.Text = naming ? "Save" : "New";
        delBtn.Text = naming ? "Cancel" : "Delete";
        delBtn.Enabled = naming || profiles.Index > 0;
    }

    void StartName()
    {
        naming = true;
        nameField.Box.Text = "";
        ShowSets();
        nameField.Box.Focus();
    }

    void EndName()
    {
        naming = false;
        ShowSets();
    }

    void PickSet(int i)
    {
        ShowSets();
        if (i == 0) { Revert(); return; }
        try
        {
            int b;
            cur = Lighting.FromText(Settings.Get(Prefix + profiles.Items[i]), out b);
            brightness = Math.Max(0, Math.Min(100, b));
            Changed(true);
        }
        catch (Exception) { Say("This profile couldn't be read from settings.txt", true); }
    }

    void SaveSet(string name)
    {
        if (name == "") { Say("Type a name for the profile first", true); nameField.Box.Focus(); return; }
        if (name.Contains("=")) { Say("A profile name can't contain =", true); nameField.Box.Focus(); return; }
        if (name == OnKeyboard) { Say("Pick another name for the profile", true); nameField.Box.Focus(); return; }
        Settings.Set(Prefix + name, cur.ToText(brightness));
        EndName();
        LoadSets(name);
        Say("Saved the profile " + name);
    }

    void Revert()
    {
        if (stored == null) return;
        cur = stored.Clone();
        brightness = storedBrightness;
        Release();
        Sync();
        Calm();
    }

    void Do(Action<Hidpp> a, Action done = null)
    {
        queue = queue.ContinueWith(_ =>
        {
            try
            {
                if (dev == null) dev = demo ? fake : new Hidpp(found.Path, found.Index);
                a(dev);
                if (done != null && IsHandleCreated) BeginInvoke(done);
            }
            catch (Exception ex)
            {
                if (dev != null && !(ex is HidppError)) { dev.Dispose(); dev = null; claimed = false; }
                Post(ex.Message, true);
            }
        });
    }

    void Preview()
    {
        var l = cur.Clone();
        int b = brightness, seq = ++previewSeq;
        Do(h =>
        {
            if (seq != previewSeq) return;
            if (b != G515.Brightness(h)) G515.SetBrightness(h, b);
            if (!l.Same(stored)) { if (!claimed) { G515.Claim(h); claimed = true; } G515.Preview(h, l); }
        }, () => { if (seq == previewSeq) Calm(); });
    }

    void Save()
    {
        var l = cur.Clone();
        int b = brightness;
        saveBtn.Enabled = false;
        Say("Saving…");
        Do(h => { G515.Store(h, l); claimed = false; }, () =>
        {
            stored = l;
            storedBrightness = b;
            dirty = false;
            unknownStored = false;
            Sync();
            Say("Saved to the keyboard");
        });
    }

    public void Load()
    {
        dirty = false;
        debounce.Stop();
        Say("Reading the keyboard…");
        Lighting l = null;
        int b = 100;
        bool unknown = false;
        Do(h =>
        {
            var f = G515.Snapshot(h);
            l = G515.Decode(f.UserProfile ?? f.FactoryProfile);
            unknown = l == null;
            if (l == null) l = new Lighting();
            b = G515.Brightness(h);
        }, () =>
        {
            stored = l.Clone();
            cur = l;
            brightness = b;
            storedBrightness = b;
            unknownStored = unknown;
            if (!naming) profiles.Index = 0;
            ShowSets();
            Sync();
            readAt = DateTime.Now.ToString("HH:mm");
            SetFound(found);
            Calm();
            if (Note == null) return;
            var n = Note;
            Note = null;
            Say(n);
            var t = new Timer { Interval = 8000 };
            t.Tick += (s, e) => { t.Dispose(); if (status.Shown == n) Calm(); };
            t.Start();
        });
    }

    public void Release()
    {
        debounce.Stop();
        if (!dirty) return;
        dirty = false;
        int b = storedBrightness;
        previewSeq++;
        Do(h =>
        {
            if (b >= 0) G515.SetBrightness(h, b);
            if (claimed) { G515.Release(h); claimed = false; }
        });
    }
}
