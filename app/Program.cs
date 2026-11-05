using System;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: System.Reflection.AssemblyVersion("0.0.3")]
[assembly: System.Reflection.AssemblyFileVersion("0.0.3")]
[assembly: System.Reflection.AssemblyInformationalVersion("0.0.3-alpha")]
[assembly: System.Reflection.AssemblyTitle("G-Lite")]
[assembly: System.Reflection.AssemblyProduct("G-Lite")]
[assembly: System.Reflection.AssemblyCompany("com.csmarvz")]

class MainWindow : Form
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    const int W = 840, H = 680, SideW = 184, SideIn = SideW - 20;

    public NotifyIcon Tray;
    readonly Page515 page;
    readonly Control settingsPage;
    readonly DevItem devItem;
    readonly Btn settingsBtn;
    readonly Box updBox;
    readonly Label updText, checkText, themeHint;
    readonly Seg themeSeg;
    readonly Toggle runToggle, checkToggle, autoToggle;
    readonly KeyboardView view;
    readonly bool demo;
    string available;
    bool wantView, moved;
    int traySize, bigSize;
    float scale = 1;

    [DllImport("user32.dll")] static extern bool ReleaseCapture();
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, IntPtr l);

    public MainWindow(FakeHidpp fake)
    {
        demo = fake != null;
        Win.Round(this);
        HandleCreated += (s, e) => Win.Border(this, Theme.Border);
        FormBorderStyle = FormBorderStyle.None;
        Text = "G-Lite";
        StartPosition = FormStartPosition.Manual;
        ClientSize = new Size(W, H);
        Ui.Style(this, () => BackColor = Theme.Bg);
        Ui.Font(this, 9.75f, Ui.Regular);
        FormClosing += (s, e) => { if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; ToTray(); } };
        MouseDown += Drag;

        var side = Ui.Add(this, new Control(), 0, 0, SideW, H);
        Ui.Style(side, () => side.BackColor = Theme.Side);
        side.Paint += (s, e) => { using (var p = new Pen(Theme.Border)) e.Graphics.DrawLine(p, side.Width - 1, 0, side.Width - 1, side.Height); };
        side.MouseDown += Drag;
        Ui.Add(side, new Brand { Tag2 = demo ? "demo" : "" }, 10, 12, SideIn, 34).MouseDown += Drag;
        Ui.Text(side, "DEVICES", 18, 58, () => Theme.Dim, 8.25f, Ui.Semi).MouseDown += Drag;
        devItem = Ui.Add(side, new DevItem { Title = "G515 TKL", Sub = demo ? "Demo" : "LIGHTSPEED", On = true, AccessibleName = "G515 TKL" }, 10, 78, SideIn, 46);
        devItem.Click += (s, e) => ShowDevice();

        int bottom = H - 14 - 36;
        var quit = Ui.Add(side, new Btn { Look = Look.Ghost, Icon = "power", Text = "Quit" }, 10, bottom, SideIn, 36);
        quit.Click += (s, e) => { HidePanel(); page.Queue.Wait(5000); Application.Exit(); };
        bottom -= 40;
        settingsBtn = Ui.Add(side, new Btn { Look = Look.Ghost, Icon = "gear", Text = "Settings" }, 10, bottom, SideIn, 36);
        settingsBtn.Click += (s, e) => ShowSettings();
        if (demo)
        {
            bottom -= 40;
            view = new KeyboardView(fake, Model.Load("g515-tkl.json"));
            var show = Ui.Add(side, new Btn { Look = Look.Ghost, Icon = "keyboard", Text = "Show keyboard" }, 10, bottom, SideIn, 36);
            show.Click += (s, e) => { if (view.Visible) view.Hide(); else ShowView(); };
            view.VisibleChanged += (s, e) => { show.Text = view.Visible ? "Hide keyboard" : "Show keyboard"; wantView = view.Visible; };
        }
        updBox = Ui.Add(side, new Box { Visible = false }, 10, bottom - 8 - 92, SideIn, 92);
        updText = Ui.Text(updBox, "", 12, 10, () => Theme.Text, 9f);
        updText.AutoSize = false;
        updText.Size = new Size(SideIn - 24, 34);
        var updGo = Ui.Add(updBox, new Btn { Look = Look.Primary, Text = "Update" }, 12, 48, 76, 32);
        updGo.Click += (s, e) => Install(available, true);
        var updSkip = Ui.Add(updBox, new Btn { Look = Look.Ghost, Text = "Skip" }, 94, 48, SideIn - 106, 32);
        updSkip.Click += (s, e) => { Updater.Skipped = available; ShowUpdate(null); };

        page = Ui.Add(this, new Page515(fake, Drag), SideW, 0, W - SideW, H);

        settingsPage = Ui.Add(this, new Control { Visible = false }, SideW, 0, W - SideW, H);
        Ui.Style(settingsPage, () => settingsPage.BackColor = Theme.Bg);
        settingsPage.MouseDown += Drag;
        var sp = settingsPage;
        Ui.Text(sp, "Settings", 20, 14, () => Theme.Text, 13.5f, Ui.Semi).MouseDown += Drag;

        Ui.Text(sp, "Appearance", 20, 64, () => Theme.Dim, 9f, Ui.Semi);
        var look = Ui.Add(sp, new Box(), 20, 86, 616, 64);
        themeHint = Row(look, 0, "Theme", "");
        themeSeg = Ui.Add(look, new Seg { Items = new[] { "System", "Dark", "Light" }, AccessibleName = "Theme" }, 616 - 14 - 240, 16, 240, 32);
        themeSeg.Picked += i => { Theme.Mode = Theme.Modes[i]; ShowThemeHint(); };

        Ui.Text(sp, "Updates", 20, 170, () => Theme.Dim, 9f, Ui.Semi);
        var upd = Ui.Add(sp, new Box(), 20, 192, 616, 192);
        Row(upd, 0, "Check for updates", "Looks for a new release on GitHub when G-Lite starts");
        checkToggle = Ui.Add(upd, new Toggle { Checked = Updater.Check, AccessibleName = "Check for updates" }, 616 - 14 - 40, 21, 40, 22);
        checkToggle.Changed += () => Updater.Check = checkToggle.Checked;
        Rule(upd, 64);
        Row(upd, 64, "Install updates automatically", "Otherwise G-Lite asks first");
        autoToggle = Ui.Add(upd, new Toggle { Checked = Updater.Auto, AccessibleName = "Install updates automatically" }, 616 - 14 - 40, 64 + 21, 40, 22);
        autoToggle.Changed += () => Updater.Auto = autoToggle.Checked;
        Rule(upd, 128);
        checkText = Row(upd, 128, "Version " + Application.ProductVersion, "");
        checkText.AutoSize = false;
        checkText.AutoEllipsis = true;
        checkText.Size = new Size(616 - 14 - 180 - 14 - 14, 18);
        var now = Ui.Add(upd, new Btn { Text = "Check for updates now" }, 616 - 14 - 180, 128 + 16, 180, 32);
        now.Click += (s, e) => CheckUpdates(true);

        Ui.Text(sp, "Startup", 20, 404, () => Theme.Dim, 9f, Ui.Semi);
        var start = Ui.Add(sp, new Box(), 20, 426, 616, 64);
        Row(start, 0, "Run on startup", "Starts in the tray when you sign in to Windows");
        runToggle = Ui.Add(start, new Toggle { AccessibleName = "Run on startup" }, 616 - 14 - 40, 21, 40, 22);
        runToggle.Changed += () => SetStartup(runToggle.Checked);

        var minimize = Ui.Add(this, new Btn { Look = Look.Ghost, Icon = "minus", AccessibleName = "Minimize" }, W - 12 - 72, 10, 36, 28);
        minimize.Click += (s, e) => WindowState = FormWindowState.Minimized;
        var close = Ui.Add(this, new Btn { Look = Look.Ghost, Icon = "close", AccessibleName = "Close to tray" }, W - 12 - 36, 10, 36, 28);
        close.Click += (s, e) => ToTray();
        minimize.BringToFront();
        close.BringToFront();

        Theme.Changed += Restyle;
        ShowThemeHint();
        ShowUpdate(null);
        Rescale(Win.At(Point.Empty));
    }

    static Label Row(Control box, int y, string title, string hint)
    {
        Ui.Text(box, title, 14, y + 12, () => Theme.Text, 9.75f, Ui.Semi);
        return Ui.Text(box, hint, 14, y + 33, () => Theme.Dim, 9f);
    }

    static void Rule(Control box, int y)
    {
        var l = Ui.Add(box, new Control(), 14, y, box.Width - 28, 1);
        Ui.Style(l, () => l.BackColor = Theme.Border);
    }

    void Restyle()
    {
        Ui.Restyle();
        page.Restyle();
        Win.Border(this, Theme.Border);
        ShowThemeHint();
        Invalidate(true);
        if (Tray != null && Tray.ContextMenuStrip != null) Tray.ContextMenuStrip.BackColor = Theme.Card;
    }

    void ShowThemeHint()
    {
        var m = Theme.Mode;
        themeSeg.Index = Array.IndexOf(Theme.Modes, m);
        themeHint.Text = m == "system" ? "Follows Windows, which uses " + (Theme.SystemLight() ? "light" : "dark") + " mode right now" : "Always " + m;
    }

    void ShowDevice()
    {
        settingsPage.Visible = false;
        page.Visible = true;
        devItem.On = true;
        settingsBtn.On = false;
        devItem.Invalidate();
        settingsBtn.Invalidate();
    }

    void ShowSettings()
    {
        runToggle.Checked = StartupOn();
        checkToggle.Checked = Updater.Check;
        autoToggle.Checked = Updater.Auto;
        ShowThemeHint();
        page.Visible = false;
        settingsPage.Visible = true;
        devItem.On = false;
        settingsBtn.On = true;
        devItem.Invalidate();
        settingsBtn.Invalidate();
    }

    int S(float v) { return (int)Math.Round(v * scale); }

    void Rescale(float ns)
    {
        if (ns == scale) return;
        SuspendLayout();
        Scale(new SizeF(ns / scale, ns / scale));
        scale = ns;
        Ui.K = ns;
        Ui.Refont();
        ResumeLayout();
        Invalidate(true);
        RefreshIcons();
    }

    public void RefreshIcons()
    {
        int t = (int)Math.Round(16 * Win.At(Point.Empty)), b = S(32);
        if (Tray != null && t != traySize) { traySize = t; Tray.Icon = Logo.Make(t); }
        if (b != bigSize) { bigSize = b; Icon = Logo.Make(b); }
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == Win.DpiChanged) Rescale(Win.FromMessage(m));
        base.WndProc(ref m);
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
        if (!Win.Rounded) using (var pen = new Pen(Theme.Border)) e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
    }

    void SetStartup(bool on)
    {
        using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
            if (on) k.SetValue("GLite", "\"" + Application.ExecutablePath + "\" --tray");
            else k.DeleteValue("GLite", false);
        using (var k = Registry.CurrentUser.CreateSubKey(ApprovedKey))
            if (on) k.SetValue("GLite", new byte[] { 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, RegistryValueKind.Binary);
            else k.DeleteValue("GLite", false);
    }

    static bool StartupOn()
    {
        using (var k = Registry.CurrentUser.OpenSubKey(RunKey))
            if (k == null || k.GetValue("GLite") == null) return false;
        using (var k = Registry.CurrentUser.OpenSubKey(ApprovedKey))
        {
            var v = k == null ? null : k.GetValue("GLite") as byte[];
            return v == null || v.Length == 0 || (v[0] & 1) == 0;
        }
    }

    void ShowView()
    {
        var wa = Screen.FromControl(this).WorkingArea;
        view.StartPosition = FormStartPosition.Manual;
        view.Rescale(scale);
        view.Location = new Point(Math.Max(wa.Left, Left - view.Width - S(8)), Math.Max(wa.Top, Top));
        view.Show();
    }

    void ShowUpdate(string v)
    {
        available = v;
        updText.Text = v == null ? "" : "Version " + v + " is available";
        updBox.Visible = v != null;
        if (Tray != null) Tray.Text = "G-Lite " + Application.ProductVersion + (demo ? " (demo)" : "") + (v == null ? "" : " · " + v + " available");
    }

    public void CheckUpdates(bool manual)
    {
        if (manual) checkText.Text = "Checking for updates…";
        Task.Run(() =>
        {
            string v = null, err = null;
            try { v = Updater.Latest(); } catch (Exception ex) { err = ex.Message; }
            BeginInvoke((Action)(() => Found(v, err, manual)));
        });
    }

    void Found(string v, string err, bool manual)
    {
        if (err != null) { if (manual) checkText.Text = "Couldn't check for updates: " + err; return; }
        if (v == null || !Updater.Newer(v, Application.ProductVersion)) { if (manual) checkText.Text = "G-Lite is up to date"; return; }
        if (!manual && v == Updater.Skipped) return;
        if (!manual && Updater.Auto) { Install(v, Visible); return; }
        if (manual) checkText.Text = "Version " + v + " is available";
        ShowUpdate(v);
    }

    void Install(string v, bool show)
    {
        ShowUpdate(null);
        page.Say("Downloading " + v + "…");
        Task.Run(() =>
        {
            try
            {
                Updater.Install(v, show);
                BeginInvoke((Action)(() => { HidePanel(); page.Queue.Wait(5000); if (Tray != null) Tray.Visible = false; Application.Exit(); }));
            }
            catch (Exception ex) { BeginInvoke((Action)(() => page.Say("Update failed: " + ex.Message, true))); }
        });
    }

    public void SetNote(string note) { page.Note = note; }

    public void ShowPanel()
    {
        var wa = Screen.FromPoint(Cursor.Position).WorkingArea;
        Rescale(Win.At(moved ? Location : Cursor.Position));
        if (!moved) Location = new Point(Math.Max(wa.Left, wa.Right - Width - S(12)), Math.Max(wa.Top, wa.Bottom - Height - S(12)));
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        Show();
        if (view != null && wantView) ShowView();
        Activate();
        ShowDevice();
        page.Load();
    }

    void ToTray()
    {
        HidePanel();
        if (Tray == null || Settings.Get("tray-tip-shown") == "1") return;
        Tray.ShowBalloonTip(3000, "G-Lite", "G-Lite is still running in the tray.", ToolTipIcon.None);
        Settings.Set("tray-tip-shown", "1");
    }

    public void HidePanel()
    {
        Hide();
        page.Release();
    }

    [STAThread]
    static void Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "--swap") { Environment.ExitCode = Updater.Swap(args[1]); return; }
        Win.Aware();
        bool fresh, demo = args.Contains("--demo");
        using (var mutex = new Mutex(true, demo ? "GLiteDemo" : "GLite", out fresh))
        {
            if (!fresh && args.Contains("--restart"))
                try { fresh = mutex.WaitOne(15000); } catch (AbandonedMutexException) { fresh = true; }
            if (!fresh) return;
            Updater.Cleanup();
            Settings.Init();
            Theme.Apply();
            Application.EnableVisualStyles();
            var fake = demo ? new FakeHidpp() : null;
            var panel = new MainWindow(fake);
            var tray = new NotifyIcon { Text = "G-Lite " + Application.ProductVersion + (demo ? " (demo)" : ""), Visible = true };
            tray.ContextMenuStrip = new ContextMenuStrip { Renderer = Ui.Menus, ShowImageMargin = false, BackColor = Theme.Card };
            tray.ContextMenuStrip.Items.Add("Open", null, (s, e) => panel.ShowPanel());
            tray.ContextMenuStrip.Items.Add("Quit", null, (s, e) => { tray.Visible = false; Application.Exit(); });
            tray.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) { if (panel.Visible) panel.HidePanel(); else panel.ShowPanel(); } };
            panel.CreateControl();
            panel.Handle.ToString();
            panel.Tray = tray;
            panel.RefreshIcons();
            SystemEvents.DisplaySettingsChanged += (o, e) => panel.RefreshIcons();
            SystemEvents.UserPreferenceChanged += (o, e) => { panel.RefreshIcons(); Theme.Apply(); };
            if (args.Contains("--restart")) panel.SetNote("Successfully updated to v" + Application.ProductVersion);
            using (var k = Registry.CurrentUser.OpenSubKey(RunKey, true))
            {
                var run = k == null ? null : k.GetValue("GLite") as string;
                if (run != null && !run.Contains("--tray")) k.SetValue("GLite", run + " --tray");
            }
            if (!args.Contains("--tray")) panel.ShowPanel();
            if (Updater.Check) panel.CheckUpdates(false);
            Application.Run();
            tray.Visible = false;
        }
    }
}
