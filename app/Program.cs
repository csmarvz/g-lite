using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

static class Theme
{
    public static readonly Color Bg = Color.FromArgb(28, 28, 28), Btn = Color.FromArgb(43, 43, 43),
        Border = Color.FromArgb(58, 58, 58), Text = Color.FromArgb(232, 232, 232), Dim = Color.FromArgb(150, 150, 150),
        Accent = Color.FromArgb(63, 169, 138), AccentBg = Color.FromArgb(38, 48, 45);
}

class Slider : Control
{
    public int Max = 100, Value;
    public event EventHandler Changed;

    public Slider() { DoubleBuffered = true; Height = 20; Cursor = Cursors.Hand; }

    int X(int v) { return 7 + (int)((long)v * (Width - 14) / Max); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var c = Enabled ? Theme.Accent : Theme.Border;
        using (var track = new SolidBrush(Color.FromArgb(68, 68, 68))) g.FillRectangle(track, 7, 8, Width - 14, 4);
        using (var b = new SolidBrush(c))
        {
            g.FillRectangle(b, 7, 8, X(Value) - 7, 4);
            g.FillEllipse(b, X(Value) - 7, 3, 14, 14);
        }
    }

    protected override void OnMouseDown(MouseEventArgs e) { Set(e.X); }
    protected override void OnMouseMove(MouseEventArgs e) { if (e.Button == MouseButtons.Left) Set(e.X); }

    void Set(int px)
    {
        int v = Math.Max(0, Math.Min(Max, (int)Math.Round((double)(px - 7) * Max / (Width - 14))));
        if (v == Value) return;
        Value = v;
        Invalidate();
        if (Changed != null) Changed(this, EventArgs.Empty);
    }
}

class Panel515 : Form
{
    static readonly int[] Swatches = { 0xFFFFFF, 0xFF2020, 0xFF8000, 0xFFD000, 0x30D050, 0x3080FF };
    static readonly int[] IdleSecs = { 0, 15, 30, 60, 120, 300, 600 };
    static readonly string[] IdleText = { "Never dim", "Dim after 15 s", "Dim after 30 s", "Dim after 1 min", "Dim after 2 min", "Dim after 5 min", "Dim after 10 min" };
    static readonly int[] DimPcts = { 0, 10, 25, 50, 75 };
    static readonly int[] SleepSecs = { 0, 60, 120, 300, 600, 900, 1800 };
    static readonly string[] SleepText = { "Never off", "Off after 1 min", "Off after 2 min", "Off after 5 min", "Off after 10 min", "Off after 15 min", "Off after 30 min" };
    static readonly int[] Directions = { 6, 1, 3, 8 };
    static readonly string[] DirText = { "← Left", "Right →", "Center out", "Center in" };
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    Lighting cur = new Lighting(), stored;
    int brightness = 100, storedBrightness = -1;
    bool loading, dirty, claimed, unknownStored;
    Hidpp dev;
    Task queue = Task.FromResult(0);
    int previewSeq;
    System.Windows.Forms.Timer debounce = new System.Windows.Forms.Timer { Interval = 120 };

    Button[] effectBtns, dirBtns;
    Control[] swatchCtl;
    Label effectHead, speedHead, brightHead, status;
    Slider speed, bright;
    ComboBox idleBox, dimBox, sleepBox;
    CheckBox startup;
    Button saveBtn, more;
    bool moved, demo;

    [DllImport("user32.dll")] static extern bool ReleaseCapture();
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, IntPtr l);

    FakeHidpp fake;

    public Panel515(FakeHidpp fake)
    {
        this.fake = fake;
        demo = fake != null;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = Theme.Bg;
        ForeColor = Theme.Text;
        Font = new Font("Segoe UI", 9f);
        ClientSize = new Size(360, 10);
        Padding = new Padding(1);
        debounce.Tick += (s, e) => { debounce.Stop(); Preview(); };
        MouseDown += Drag;

        int y = 12, x0 = 16, w = 328;
        var title = Lbl(demo ? "G515 Lighting — demo" : "G515 Lighting", x0, y, Theme.Dim);
        if (demo)
        {
            var view = new KeyboardView(fake, Model.Load("g515-tkl.json"));
            view.Show();
            var show = new Label { Text = "Show keyboard", ForeColor = Theme.Accent, Location = new Point(x0 + w - 120, y), AutoSize = true, Cursor = Cursors.Hand };
            show.Click += (s, e) => view.Visible = !view.Visible;
            view.VisibleChanged += (s, e) => show.Text = view.Visible ? "Hide keyboard" : "Show keyboard";
            show.Text = "Hide keyboard";
            Controls.Add(show);
        }
        var close = new Label { Text = "✕", ForeColor = Theme.Dim, Location = new Point(x0 + w - 14, y), AutoSize = true, Cursor = Cursors.Hand };
        close.Click += (s, e) => HidePanel();
        Controls.Add(close);
        y += 30;

        effectHead = Head("Effect", x0, ref y);
        effectBtns = Grid(G515.Effects.Select(d => d.Name).ToArray(), 3, x0, ref y, w, i => { cur.Effect = G515.Effects[i].Id; var d = G515.Effects[i]; if (d.Speed && (cur.Period < d.MinP || cur.Period > d.MaxP)) cur.Period = d.DefP; Changed(true); });

        Head("Color", x0, ref y);
        swatchCtl = new Control[Swatches.Length + 1];
        int sw = (w - 6 * 6) / 7;
        for (int i = 0; i < Swatches.Length; i++)
        {
            int c = Swatches[i];
            var p = new Label { BackColor = Color.FromArgb(c | unchecked((int)0xFF000000)), Bounds = new Rectangle(x0 + i * (sw + 6), y, sw, 26), Cursor = Cursors.Hand };
            p.Click += (s, e) => { cur.Color = c; Changed(true); };
            p.Paint += (s, e) => { if (cur.Color == c && ((Control)s).Enabled) using (var pen = new Pen(Theme.Accent, 4)) e.Graphics.DrawRectangle(pen, 1, 1, sw - 3, 23); };
            swatchCtl[i] = p;
            Controls.Add(p);
        }
        more = Btn("…", x0 + 6 * (sw + 6), y, sw, 26);
        more.Click += (s, e) => PickColor();
        swatchCtl[6] = more;
        y += 34;

        speedHead = Head("Speed", x0, ref y);
        speed = new Slider { Location = new Point(x0, y), Width = w };
        speed.Changed += (s, e) => { var d = G515.Def(cur.Effect); cur.Period = d.MaxP - (d.MaxP - d.MinP) * speed.Value / 100; Changed(false); };
        Controls.Add(speed);
        y += 28;

        Head("Direction", x0, ref y);
        dirBtns = Grid(DirText, 4, x0, ref y, w, i => { cur.Direction = Directions[i]; Changed(true); });

        brightHead = Head("Brightness", x0, ref y);
        bright = new Slider { Location = new Point(x0, y), Width = w };
        bright.Changed += (s, e) => { brightness = bright.Value; brightHead.Text = "Brightness  " + brightness + "%"; dirty = true; saveBtn.Enabled = true; Mark(saveBtn, true); debounce.Stop(); debounce.Start(); };
        Controls.Add(bright);
        y += 28;

        Head("When idle", x0, ref y);
        int cw = (w - 12) / 3;
        idleBox = Combo(IdleText, x0, y, cw + 20);
        dimBox = Combo(DimPcts.Select(p => p == 0 ? "to dark" : "to " + p + "%").ToArray(), x0 + cw + 26, y, cw - 26);
        sleepBox = Combo(SleepText, x0 + 2 * cw + 12, y, cw);
        idleBox.SelectedIndexChanged += (s, e) => { if (!loading) { cur.Idle = IdleSecs[idleBox.SelectedIndex]; Changed(false); } };
        dimBox.SelectedIndexChanged += (s, e) => { if (!loading) { cur.Dim = DimPcts[dimBox.SelectedIndex]; Changed(false); } };
        sleepBox.SelectedIndexChanged += (s, e) => { if (!loading) { cur.Sleep = SleepSecs[sleepBox.SelectedIndex]; Changed(false); } };
        y += 40;

        status = new Label { Location = new Point(x0, y), Size = new Size(w, 34), ForeColor = Theme.Dim, Cursor = Cursors.Hand };
        status.Click += (s, e) => { if (status.Text.Contains("Dynamic Lighting")) System.Diagnostics.Process.Start("ms-settings:personalization-lighting"); };
        Controls.Add(status);
        y += 38;

        startup = new CheckBox { Text = "Run on startup", Location = new Point(x0, y + 5), AutoSize = true, ForeColor = Theme.Dim };
        using (var k = Registry.CurrentUser.OpenSubKey(RunKey)) startup.Checked = k != null && k.GetValue("G515Lighting") != null;
        startup.CheckedChanged += (s, e) => SetStartup(startup.Checked);
        Controls.Add(startup);
        var quit = Btn("Quit", x0 + w - 60, y, 60, 30);
        quit.Click += (s, e) => { HidePanel(); queue.Wait(5000); Application.Exit(); };
        saveBtn = Btn("Save to keyboard", x0 + w - 60 - 8 - 130, y, 130, 30);
        saveBtn.Click += (s, e) => Save();
        ClientSize = new Size(360, y + 44);

        Sync();
    }

    void Drag(object s, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        ReleaseCapture();
        SendMessage(Handle, 0xA1, (IntPtr)2, IntPtr.Zero);
        moved = true;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        using (var pen = new Pen(Theme.Border)) e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
    }

    Label Lbl(string t, int x, int y, Color c)
    {
        var l = new Label { Text = t, Location = new Point(x, y), AutoSize = true, ForeColor = c };
        l.MouseDown += Drag;
        Controls.Add(l);
        return l;
    }

    Label Head(string t, int x, ref int y)
    {
        var l = Lbl(t, x, y, Theme.Text);
        l.Font = new Font("Segoe UI Semibold", 9.5f);
        y += 24;
        return l;
    }

    Button Btn(string t, int x, int y, int w, int h)
    {
        var b = new Button { Text = t, Bounds = new Rectangle(x, y, w, h), FlatStyle = FlatStyle.Flat, BackColor = Theme.Btn, ForeColor = Theme.Text, Cursor = Cursors.Hand, TabStop = false };
        b.FlatAppearance.BorderColor = Theme.Border;
        Controls.Add(b);
        return b;
    }

    Button[] Grid(string[] names, int cols, int x0, ref int y, int w, Action<int> click)
    {
        int bw = (w - (cols - 1) * 6) / cols;
        var r = new Button[names.Length];
        for (int i = 0; i < names.Length; i++)
        {
            int n = i;
            r[i] = Btn(names[i], x0 + i % cols * (bw + 6), y + i / cols * 40, bw, 34);
            r[i].Click += (s, e) => click(n);
        }
        y += (names.Length + cols - 1) / cols * 40 + 6;
        return r;
    }

    ComboBox Combo(string[] items, int x, int y, int w)
    {
        var c = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat, BackColor = Theme.Btn, ForeColor = Theme.Text, Location = new Point(x, y), Width = w };
        c.Items.AddRange(items);
        c.SelectedIndex = 0;
        Controls.Add(c);
        return c;
    }

    static void Pick(ComboBox c, int[] values, int v)
    {
        int best = 0;
        for (int i = 0; i < values.Length; i++) if (Math.Abs(values[i] - v) < Math.Abs(values[best] - v)) best = i;
        c.SelectedIndex = best;
    }

    static void Mark(Button b, bool on)
    {
        b.FlatAppearance.BorderColor = on ? Theme.Accent : Theme.Border;
        b.FlatAppearance.BorderSize = on ? 2 : 1;
        b.BackColor = on ? Theme.AccentBg : Theme.Btn;
    }

    void Sync()
    {
        loading = true;
        var d = G515.Def(cur.Effect);
        effectHead.Text = "Effect" + (unknownStored && !dirty ? ":  factory animation" : d != null ? ":  " + d.Name : "");
        for (int i = 0; i < effectBtns.Length; i++) Mark(effectBtns[i], !(unknownStored && !dirty) && G515.Effects[i].Id == cur.Effect);
        foreach (var c in swatchCtl) { c.Enabled = d != null && d.Color; c.Invalidate(); }
        bool custom = !Swatches.Contains(cur.Color);
        int lum = ((cur.Color >> 16 & 0xFF) * 299 + (cur.Color >> 8 & 0xFF) * 587 + (cur.Color & 0xFF) * 114) / 1000;
        more.BackColor = custom ? Color.FromArgb(cur.Color | unchecked((int)0xFF000000)) : Theme.Btn;
        more.ForeColor = custom && lum > 140 ? Color.Black : Theme.Text;
        more.FlatAppearance.BorderColor = custom && more.Enabled ? Theme.Accent : Theme.Border;
        more.FlatAppearance.BorderSize = custom && more.Enabled ? 2 : 1;
        speed.Enabled = d != null && d.Speed;
        if (speed.Enabled) speed.Value = (d.MaxP - cur.Period) * 100 / (d.MaxP - d.MinP);
        speedHead.Text = speed.Enabled ? "Speed  " + speed.Value + "%" : "Speed";
        speed.Invalidate();
        for (int i = 0; i < dirBtns.Length; i++) { dirBtns[i].Enabled = d != null && d.Direction; Mark(dirBtns[i], dirBtns[i].Enabled && Directions[i] == cur.Direction); }
        bright.Value = brightness;
        brightHead.Text = "Brightness  " + brightness + "%";
        bright.Invalidate();
        Pick(idleBox, IdleSecs, cur.Idle);
        Pick(dimBox, DimPcts, cur.Dim);
        Pick(sleepBox, SleepSecs, cur.Sleep);
        dimBox.Enabled = cur.Idle > 0;
        saveBtn.Enabled = dirty;
        Mark(saveBtn, dirty);
        loading = false;
    }

    void Changed(bool now)
    {
        if (loading) return;
        dirty = true;
        Sync();
        debounce.Stop();
        if (now) Preview(); else debounce.Start();
    }

    void PickColor()
    {
        using (var dlg = new ColorDialog { FullOpen = true, Color = Color.FromArgb(cur.Color | unchecked((int)0xFF000000)) })
            if (dlg.ShowDialog(this) == DialogResult.OK) { cur.Color = dlg.Color.ToArgb() & 0xFFFFFF; Changed(true); }
        Activate();
    }

    void SetStartup(bool on)
    {
        using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
            if (on) k.SetValue("G515Lighting", "\"" + Application.ExecutablePath + "\"");
            else k.DeleteValue("G515Lighting", false);
    }

    void Status(string s) { if (IsHandleCreated) BeginInvoke((Action)(() => status.Text = s)); }

    void Do(Action<Hidpp> a, Action done = null)
    {
        queue = queue.ContinueWith(_ =>
        {
            try
            {
                if (dev == null) dev = demo ? fake : new Hidpp();
                a(dev);
                if (done != null) BeginInvoke(done);
            }
            catch (Exception ex)
            {
                if (dev != null && !(ex is HidppError)) { dev.Dispose(); dev = null; claimed = false; }
                Status(ex.Message);
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
            Status("Previewing — Save to keep it");
        });
    }

    void Save()
    {
        var l = cur.Clone();
        saveBtn.Enabled = false;
        dirty = false;
        storedBrightness = brightness;
        Status("Saving…");
        Do(h => { G515.Store(h, l); claimed = false; }, () =>
        {
            stored = l; storedBrightness = brightness; dirty = false; unknownStored = false;
            Sync();
            status.Text = "Saved to keyboard ✓";
        });
    }

    public void ShowPanel()
    {
        var wa = Screen.FromPoint(Cursor.Position).WorkingArea;
        if (!moved) Location = new Point(wa.Right - Width - 12, wa.Bottom - Height - 12);
        Show();
        Activate();
        dirty = false;
        status.Text = DynamicLightingOn() ? "Windows Dynamic Lighting is on and may override this. Click to open its settings." : "Reading keyboard…";
        bool dl = status.Text.Contains("Dynamic");
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
            stored = l.Clone(); cur = l; brightness = b; storedBrightness = b; unknownStored = unknown;
            Sync();
            if (!dl) status.Text = "";
        });
    }


    public void HidePanel()
    {
        Hide();
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

    static bool DynamicLightingOn()
    {
        using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Lighting"))
            return k != null && Equals(k.GetValue("AmbientLightingEnabled"), 1);
    }

    static Icon MakeIcon()
    {
        var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        using (var pen = new Pen(Color.White, 2.5f))
        using (var dot = new SolidBrush(Color.White))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.DrawRectangle(pen, 2, 8, 28, 17);
            for (int r = 0; r < 2; r++) for (int c = 0; c < 5; c++) g.FillRectangle(dot, 6 + c * 4.5f, 12 + r * 4.5f, 2.5f, 2.5f);
            g.FillRectangle(dot, 10, 20.5f, 12, 2.5f);
        }
        return Icon.FromHandle(bmp.GetHicon());
    }

    [STAThread]
    static void Main(string[] args)
    {
        bool fresh, demo = args.Contains("--demo");
        using (new Mutex(true, demo ? "G515LightingDemo" : "G515Lighting", out fresh))
        {
            if (!fresh) return;
            Application.EnableVisualStyles();
            var fake = demo ? new FakeHidpp() : null;
            var panel = new Panel515(fake);
            var tray = new NotifyIcon { Icon = MakeIcon(), Text = demo ? "G515 Lighting (demo)" : "G515 Lighting", Visible = true };
            tray.ContextMenuStrip = new ContextMenuStrip();
            tray.ContextMenuStrip.Items.Add("Open", null, (s, e) => panel.ShowPanel());
            tray.ContextMenuStrip.Items.Add("Quit", null, (s, e) => { tray.Visible = false; Application.Exit(); });
            tray.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) { if (panel.Visible) panel.HidePanel(); else panel.ShowPanel(); } };
            panel.CreateControl();
            panel.Handle.ToString();
            Application.Run();
            tray.Visible = false;
        }
    }
}
