using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: System.Reflection.AssemblyVersion("0.0.32")]
[assembly: System.Reflection.AssemblyFileVersion("0.0.32")]
[assembly: System.Reflection.AssemblyInformationalVersion("0.0.32-alpha")]
[assembly: System.Reflection.AssemblyTitle("G-Lite")]
[assembly: System.Reflection.AssemblyProduct("G-Lite")]
[assembly: System.Reflection.AssemblyCompany("com.csmarvz")]

class MainWindow : Form
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    const int WindowWidth = 868, WindowHeight = 730, SidebarWidth = 212, SidebarInnerWidth = SidebarWidth - 20;

    public NotifyIcon Tray;
    readonly FakeHidpp fake;
    readonly Dictionary<string, Control> pages = new Dictionary<string, Control>();
    readonly Dictionary<string, DeviceListItem> items = new Dictionary<string, DeviceListItem>();
    readonly Dictionary<string, Hidpp> demoDevs = new Dictionary<string, Hidpp>();
    List<FoundDevice> found = new List<FoundDevice>();
    string selected, note;
    Control shown;
    bool scanning, reading;
    DeviceWatcher watch;
    string watched = "";
    DateTime batteriesRead;
    readonly EmptyPage empty;
    readonly Label noneText, readyText;
    readonly DeviceList deviceList;
    int navTop;
    bool rowShown, labelShown;
    string ready;
    readonly Control side, settingsPage;
    readonly FlatButton settingsBtn, logsBtn, scanBtn, minimize, close;
    readonly LogsPage logsPage;
    readonly System.Windows.Forms.Timer rescanSoon = new System.Windows.Forms.Timer { Interval = 1500 }, sleepers = new System.Windows.Forms.Timer { Interval = 15000 }, batteries = new System.Windows.Forms.Timer { Interval = 60000 };
    readonly FlatButton updateBtn;
    readonly PageUpdate updatePage;
    readonly Label checkText, themeHint;
    readonly SegmentedPicker themeSeg;
    readonly Toggle runToggle, checkToggle, autoToggle;
    KeyboardView view;
    readonly bool demo;
    string available;
    bool wantView, moved;
    int traySize, bigSize;
    bool trayLight;
    float scale = 1;

    [DllImport("user32.dll")] static extern bool ReleaseCapture();
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, IntPtr l);

    public MainWindow(FakeHidpp fake)
    {
        this.fake = fake;
        demo = fake != null;
        WindowHelpers.Round(this);
        HandleCreated += (s, e) => { WindowHelpers.Border(this, Theme.Border); Presence.Watch(Handle); };
        FormBorderStyle = FormBorderStyle.None;
        Text = "G-Lite";
        StartPosition = FormStartPosition.Manual;
        ClientSize = new Size(WindowWidth, WindowHeight);
        Ui.Style(this, () => BackColor = Theme.Background);
        Ui.Font(this, 9.75f, Ui.Regular);
        FormClosing += (s, e) => { if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; ToTray(); } };
        MouseDown += Drag;

        side = Ui.Add(this, new Control(), 0, 0, SidebarWidth, WindowHeight);
        Ui.Style(side, () => side.BackColor = Theme.Sidebar);
        side.Paint += (s, e) => { using (var p = new Pen(Theme.Border)) e.Graphics.DrawLine(p, side.Width - 1, 0, side.Width - 1, side.Height); };
        side.MouseDown += Drag;
        Ui.Add(side, new TitleLogo(), 10, 12, SidebarInnerWidth, 34).MouseDown += (s, e) =>
        {
            if (e.Button != MouseButtons.Left) return;
            var at = Location;
            bool was = moved;
            Drag(s, e);
            if (Location != at) return;
            moved = was;
            if (e.X < 60 * Ui.DpiScale) ToggleView();
        };
        Ui.Text(side, "DEVICES", 18, 58, () => Theme.Dim, 8.25f, Ui.Semibold).MouseDown += Drag;
        deviceList = Ui.Add(side, new DeviceList(), 0, 78, SidebarWidth - 1, 400);
        deviceList.MouseDown += Drag;
        noneText = Ui.Text(deviceList, "Looking…", 18, 4, () => Theme.Dim, 9f);

        int bottom = WindowHeight - 14 - 36;
        var quit = Ui.Add(side, new FlatButton { Style = ButtonStyle.Ghost, Icon = "power", Text = "Quit" }, 10, bottom, SidebarInnerWidth, 36);
        quit.Click += (s, e) => { HidePanel(); WaitAll(); Application.Exit(); };
        quit.TabIndex = 103;
        bottom -= 40;
        settingsBtn = Ui.Add(side, new FlatButton { Style = ButtonStyle.Ghost, Icon = "gear", Text = "Settings" }, 10, bottom, SidebarInnerWidth, 36);
        settingsBtn.Click += (s, e) => ShowSettings();
        settingsBtn.TabIndex = 102;
        bottom -= 40;
        logsBtn = Ui.Add(side, new FlatButton { Style = ButtonStyle.Ghost, Icon = "lines", Text = "Logs" }, 10, bottom, SidebarInnerWidth, 36);
        logsBtn.Click += (s, e) => ShowLogs();
        logsBtn.TabIndex = 101;
        bottom -= 40;
        scanBtn = Ui.Add(side, new FlatButton { Style = ButtonStyle.Ghost, Icon = "refresh", Text = "Scan for devices" }, 10, bottom, SidebarInnerWidth, 36);
        scanBtn.Click += (s, e) => Rescan();
        scanBtn.TabIndex = 100;
        if (demo)
        {
            bottom -= 40;
            view = new KeyboardView(fake, KeyLayout.Load("g515-tkl.json"));
            var show = Ui.Add(side, new FlatButton { Style = ButtonStyle.Ghost, Icon = "keyboard", Text = "Show keyboard" }, 10, bottom, SidebarInnerWidth, 36);
            show.Click += (s, e) => { if (view.Visible) view.Hide(); else ShowView(); };
            view.VisibleChanged += (s, e) => { show.Text = view.Visible ? "Hide keyboard" : "Show keyboard"; wantView = view.Visible; };
        }
        updateBtn = Ui.Add(side, new FlatButton { Style = ButtonStyle.Solid, Icon = "download", Text = "Update available", Visible = false }, 10, bottom - 40, SidebarInnerWidth, 36);
        updateBtn.Click += (s, e) => { if (ready != null) Restart(); else ShowUpdatePage(false); };
        updateBtn.TabIndex = 99;
        navTop = bottom;
        readyText = Ui.Text(side, "", 18, bottom - 40 - 20, () => Theme.Dim, 8.25f);
        readyText.AutoSize = false;
        readyText.AutoEllipsis = true;
        readyText.Size = new Size(SidebarInnerWidth - 8, 16);
        readyText.Cursor = Cursors.Hand;
        readyText.Visible = false;
        readyText.Click += (s, e) => ShowUpdatePage(false);
        FitList();

        empty = Ui.Add(this, new EmptyPage(Drag, Rescan), SidebarWidth, 0, WindowWidth - SidebarWidth, WindowHeight);
        rescanSoon.Tick += (s, e) => { rescanSoon.Stop(); Rescan(); };
        sleepers.Tick += (s, e) => { if (Visible) Rescan(); };
        batteries.Tick += (s, e) => ReadBatteries();
        if (!demo) batteries.Start();

        settingsPage = Ui.Add(this, new Control { Visible = false }, SidebarWidth, 0, WindowWidth - SidebarWidth, WindowHeight);
        Ui.Style(settingsPage, () => settingsPage.BackColor = Theme.Background);
        settingsPage.MouseDown += Drag;
        var sp = settingsPage;
        logsPage = Ui.Add(this, new LogsPage(Drag), SidebarWidth, 0, WindowWidth - SidebarWidth, WindowHeight);
        updatePage = Ui.Add(this, new PageUpdate(Drag), SidebarWidth, 0, WindowWidth - SidebarWidth, WindowHeight);
        if (demo) updatePage.Fetch = DemoChangelog;
        updatePage.BackClicked = ShowSettings;
        updatePage.CheckClicked = () => CheckUpdates(true);
        updatePage.InstallClicked = Install;
        updatePage.RestartClicked = Restart;
        updatePage.SkipClicked = () =>
        {
            if (!demo) Updater.Skipped = available;
            Log.Add("", "you", "Skip version " + available);
            checkText.Text = "Version " + available + " skipped";
            ShowUpdate(null);
            if (updatePage.FromSettings) ShowSettings(); else Pick(selected);
        };
        Ui.Text(sp, "Settings", 20, 14, () => Theme.Text, 13.5f, Ui.Semibold).MouseDown += Drag;

        Ui.Text(sp, "Appearance", 20, 64, () => Theme.Dim, 9f, Ui.Semibold);
        var look = Ui.Add(sp, new RoundedBox(), 20, 86, 616, 64);
        themeHint = Row(look, 0, "Theme", "");
        themeSeg = Ui.Add(look, new SegmentedPicker { Items = new[] { "System", "Dark", "Light" }, AccessibleName = "Theme" }, 616 - 14 - 240, 16, 240, 32);
        themeSeg.Picked += i => { Theme.Mode = Theme.Modes[i]; ShowThemeHint(); };

        Ui.Text(sp, "Updates", 20, 170, () => Theme.Dim, 9f, Ui.Semibold);
        var upd = Ui.Add(sp, new RoundedBox(), 20, 192, 616, 192);
        Row(upd, 0, "Check for updates", "Looks for a new release on GitHub when G-Lite starts");
        checkToggle = Ui.Add(upd, new Toggle { Checked = Updater.CheckForUpdates, AccessibleName = "Check for updates" }, 616 - 14 - 40, 21, 40, 22);
        checkToggle.Changed += () => Updater.CheckForUpdates = checkToggle.Checked;
        Rule(upd, 64);
        Row(upd, 64, "Install updates automatically", "Otherwise G-Lite asks first");
        autoToggle = Ui.Add(upd, new Toggle { Checked = Updater.Auto, AccessibleName = "Install updates automatically" }, 616 - 14 - 40, 64 + 21, 40, 22);
        autoToggle.Changed += () => Updater.Auto = autoToggle.Checked;
        Rule(upd, 128);
        checkText = Row(upd, 128, "Version " + Application.ProductVersion, "");
        checkText.AutoSize = false;
        checkText.AutoEllipsis = true;
        checkText.Size = new Size(616 - 14 - 180 - 14 - 14, 18);
        var now = Ui.Add(upd, new FlatButton { Text = "Check for updates" }, 616 - 14 - 180, 128 + 16, 180, 32);
        now.Click += (s, e) => { ShowUpdatePage(true); CheckUpdates(true); };

        Ui.Text(sp, "Startup", 20, 404, () => Theme.Dim, 9f, Ui.Semibold);
        var start = Ui.Add(sp, new RoundedBox(), 20, 426, 616, 64);
        Row(start, 0, "Run on startup", "Starts in the tray when you sign in to Windows");
        runToggle = Ui.Add(start, new Toggle { AccessibleName = "Run on startup" }, 616 - 14 - 40, 21, 40, 22);
        runToggle.Changed += () => SetStartup(runToggle.Checked);

        minimize = Ui.Add(this, new FlatButton { Style = ButtonStyle.Ghost, Icon = "minus", AccessibleName = "Minimize" }, WindowWidth - 12 - 72, 10, 36, 28);
        minimize.Click += (s, e) => WindowState = FormWindowState.Minimized;
        minimize.TabIndex = 1000;
        close = Ui.Add(this, new FlatButton { Style = ButtonStyle.Ghost, Icon = "close", AccessibleName = "Close to tray" }, WindowWidth - 12 - 36, 10, 36, 28);
        close.Click += (s, e) => ToTray();
        close.TabIndex = 1001;

        Theme.Changed += Restyle;
        ShowThemeHint();
        ShowUpdate(null);
        Rescale(WindowHelpers.At(Point.Empty));
    }

    static Label Row(Control box, int y, string title, string hint)
    {
        Ui.Text(box, title, 14, y + 12, () => Theme.Text, 9.75f, Ui.Semibold);
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
        foreach (var p in pages.Values.OfType<DevicePage>()) p.Restyle();
        WindowHelpers.Border(this, Theme.Border);
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

    Control Current
    {
        get { Control c; return selected != null && pages.TryGetValue(selected, out c) ? c : null; }
    }

    void Pick(string key)
    {
        if (key == null || !pages.ContainsKey(key)) key = found.Count > 0 ? found[0].Key : null;
        bool changed = key != selected;
        selected = key;
        settingsPage.Visible = false;
        settingsBtn.On = false;
        settingsBtn.Invalidate();
        logsPage.Close();
        logsBtn.On = false;
        logsBtn.Invalidate();
        HideUpdatePage();
        foreach (var p in pages) p.Value.Visible = p.Key == key;
        empty.Visible = key == null;
        foreach (var i in items) { i.Value.On = i.Key == key; i.Value.Invalidate(); }
        DeviceListItem picked;
        if (changed && key != null && items.TryGetValue(key, out picked)) deviceList.Reveal(picked);
        if (Visible) ShowPage(Current);
    }

    void ShowPage(Control now)
    {
        if (shown == now) return;
        if (shown is IPage && !shown.IsDisposed) ((IPage)shown).Hidden();
        shown = now;
        if (shown is IPage) ((IPage)shown).Shown();
    }

    void ShowSettings()
    {
        runToggle.Checked = StartupOn();
        checkToggle.Checked = Updater.CheckForUpdates;
        autoToggle.Checked = Updater.Auto;
        ShowThemeHint();
        foreach (var p in pages.Values) p.Visible = false;
        empty.Visible = false;
        logsPage.Close();
        logsBtn.On = false;
        logsBtn.Invalidate();
        HideUpdatePage();
        settingsPage.Visible = true;
        foreach (var i in items.Values) { i.On = false; i.Invalidate(); }
        settingsBtn.On = true;
        settingsBtn.Invalidate();
    }

    void ShowLogs()
    {
        foreach (var p in pages.Values) p.Visible = false;
        empty.Visible = false;
        settingsPage.Visible = false;
        settingsBtn.On = false;
        settingsBtn.Invalidate();
        HideUpdatePage();
        foreach (var i in items.Values) { i.On = false; i.Invalidate(); }
        logsPage.Open();
        logsBtn.On = true;
        logsBtn.Invalidate();
    }

    void ShowUpdatePage(bool fromSettings)
    {
        foreach (var p in pages.Values) p.Visible = false;
        empty.Visible = false;
        settingsPage.Visible = false;
        settingsBtn.On = false;
        settingsBtn.Invalidate();
        logsPage.Close();
        logsBtn.On = false;
        logsBtn.Invalidate();
        foreach (var i in items.Values) { i.On = false; i.Invalidate(); }
        if (!fromSettings && available != null && ready == null && !updatePage.Busy && updatePage.State != "failed") updatePage.Available(available);
        updatePage.FromSettings = fromSettings;
        updatePage.Visible = true;
        updateBtn.On = !fromSettings;
        updateBtn.Invalidate();
    }

    void HideUpdatePage()
    {
        updatePage.Visible = false;
        updateBtn.On = false;
        updateBtn.Invalidate();
    }

    static string DeviceLabel(FoundDevice f) { return f.ShortName == "" ? f.Name : f.ShortName; }

    T Place<T>(Control parent, T c, int x, int y, int w, int h) where T : Control
    {
        Ui.Add(parent, c, x, y, w, h);
        if (scale != 1) c.Scale(new SizeF(scale, scale));
        return c;
    }

    public void Rescan()
    {
        if (scanning || !IsHandleCreated) return;
        scanning = true;
        scanBtn.Text = "Scanning…";
        scanBtn.Enabled = false;
        Task.Run(() =>
        {
            List<FoundDevice> list;
            try { list = demo ? DemoDevices() : Devices.Scan(); }
            catch (Exception) { list = new List<FoundDevice>(); }
            BeginInvoke((Action)(() => Apply(list)));
        });
    }

    public void ReadBatteries()
    {
        if (demo || (DateTime.UtcNow - batteriesRead).TotalSeconds < 5) return;
        var due = found.Where(f => !f.Asleep && f.Battery >= 0 && f.Path != null && !f.Hidpp10 && !f.PlainHid && f.BatteryFeature != 0).ToList();
        if (scanning || reading || due.Count == 0) return;
        reading = true;
        batteriesRead = DateTime.UtcNow;
        Task.Run(() =>
        {
            var read = due.Select(f => new { f, now = Devices.ReadBattery(f) }).ToList();
            BeginInvoke((Action)(() =>
            {
                reading = false;
                foreach (var r in read) if (r.now != null) ShowBattery(r.f, r.now);
                if (read.Any(r => r.now == null)) RescanSoon();
            }));
        });
    }

    void RescanSoon()
    {
        rescanSoon.Stop();
        rescanSoon.Start();
    }

    void ShowBattery(FoundDevice f, FoundDevice now)
    {
        if (!found.Contains(f)) return;
        f.Battery = now.Battery;
        f.Charging = now.Charging;
        f.Millivolts = now.Millivolts;
        DeviceListItem item;
        if (items.TryGetValue(f.Key, out item)) { item.Level = f.Level; item.Value = f.BatteryText; item.Invalidate(); }
        Control page;
        if (pages.TryGetValue(f.Key, out page))
        {
            if (page is DevicePage) ((DevicePage)page).SetFound(f);
            else ((InfoPage)page).SetFound(f);
        }
    }

    void Heard(string path, byte[] report)
    {
        if (report[0] == 0x10 && report[2] == 0x41) { BeginInvoke((Action)RescanSoon); return; }
        if (report[0] != 0x11 || report[3] != 0x00) return;
        BeginInvoke((Action)(() =>
        {
            var f = found.FirstOrDefault(d => d.Path == path && d.Index == report[1] && !d.Asleep && d.BatteryFeature != 0 && d.BatteryFeature == report[2]);
            if (f == null) return;
            var now = new FoundDevice { Name = f.Name, BatteryId = f.BatteryId };
            Devices.Decode(now, report.Skip(4).ToArray());
            if (now.BatteryFromVoltage) Devices.Remember(now, DateTime.UtcNow.Ticks / TimeSpan.TicksPerMinute);
            ShowBattery(f, now);
        }));
    }

    void Listen(List<FoundDevice> list)
    {
        // A HID++ 1.0 device on its cable (index 0) isn't watched; one behind a receiver (the G700) is, for the receiver's 0x41 connect events.
        var paths = list.Where(f => f.Path != null && !f.PlainHid && !(f.Hidpp10 && f.Index == 0)).Select(f => f.Path).Distinct().OrderBy(p => p).ToList();
        var key = string.Join("|", paths);
        if (key == watched) return;
        watched = key;
        if (watch != null) watch.Dispose();
        watch = paths.Count == 0 ? null : new DeviceWatcher(paths, Heard);
    }

    void Apply(List<FoundDevice> list)
    {
        scanning = false;
        scanBtn.Text = "Scan for devices";
        scanBtn.Enabled = true;
        empty.Searching(false);
        var keys = new HashSet<string>(list.Select(f => f.Key));
        var before = new HashSet<string>(found.Select(f => f.Key));
        foreach (var f in list.Where(f => !before.Contains(f.Key)))
            Log.Add(DeviceLabel(f), "found", f.Link + f.Slot + (f.WirelessId != 0 ? ", ID " + f.WirelessId.ToString("X4") : "") + (f.Asleep ? ", asleep" : ""));
        foreach (var f in found.Where(f => !keys.Contains(f.Key))) Log.Add(DeviceLabel(f), "lost", f.Link);
        found = list;
        foreach (var k in pages.Keys.Where(k => !keys.Contains(k)).ToList()) Remove(k);
        int y = 0;
        var made = new List<DevicePage>();
        foreach (var f in list)
        {
            var type = PageFor(f);
            Control page;
            if (pages.TryGetValue(f.Key, out page) && page.GetType() != type) { Remove(f.Key); page = null; }
            if (page == null)
            {
                page = MakePage(f, type);
                Place(this, page, SidebarWidth, 0, WindowWidth - SidebarWidth, WindowHeight);
                page.Visible = false;
                pages[f.Key] = page;
                if (page is Page515 && note != null) { ((Page515)page).Note = note; note = null; }
                if (page is DevicePage) made.Add((DevicePage)page);
            }
            else if (page is DevicePage) ((DevicePage)page).SetFound(f);
            else ((InfoPage)page).SetFound(f);
            DeviceListItem item;
            if (!items.TryGetValue(f.Key, out item))
            {
                var key = f.Key;
                item = Place(deviceList, new DeviceListItem(), 10, y, SidebarInnerWidth, 46);
                deviceList.Watch(item);
                item.Click += (s, e) => Pick(key);
                items[key] = item;
            }
            item.TabIndex = list.IndexOf(f);
            item.Title = f.ShortName;
            item.Subtitle = f.Subtitle;
            item.Level = f.Level;
            item.Value = f.BatteryText;
            item.Icon = f.IsG560 ? "speaker" : f.Kind == "light" || f.Kind == "keyboard" || f.Kind == "mouse" || f.Kind == "headset" ? f.Kind : "device";
            item.AccessibleName = f.ShortName;
            item.Location = new Point(S(10), S(y) - deviceList.Offset);
            item.Invalidate();
            y += 50;
        }
        deviceList.Fit(S(Math.Max(0, y - 4)));
        noneText.Text = "None found";
        noneText.Visible = list.Count == 0;
        minimize.BringToFront();
        close.BringToFront();
        sleepers.Enabled = list.Any(f => f.Asleep && !Devices.Shorts.ContainsKey(f.Path ?? ""));
        if (!demo) Listen(list);
        if (!settingsPage.Visible && !logsPage.Visible && !updatePage.Visible) Pick(selected);
        foreach (var page in made) if (page != shown) page.LoadForPreset();
    }

    static Type PageFor(FoundDevice f)
    {
        var support = DeviceTable.For(f);
        return support == null ? typeof(InfoPage) : support.Screen;
    }

    Control MakePage(FoundDevice f, Type type)
    {
        var support = DeviceTable.For(f);
        if (support == null || support.Screen != type) return new InfoPage(f, Drag, Rescan);
        Hidpp dev = null;
        if (demo && !demoDevs.TryGetValue(f.Key, out dev))
            demoDevs[f.Key] = dev = support.Fake != null ? support.Fake(f) : fake;
        return support.Make(f, dev, Drag);
    }

    void Remove(string key)
    {
        var page = pages[key];
        if (shown == page) { if (page is IPage) ((IPage)page).Hidden(); shown = null; }
        pages.Remove(key);
        page.Dispose();
        DeviceListItem item;
        if (items.TryGetValue(key, out item)) { items.Remove(key); item.Dispose(); }
    }

    void WaitAll() { foreach (var p in pages.Values.OfType<IPage>()) p.Queue.Wait(5000); }

    static List<FoundDevice> DemoDevices()
    {
        Thread.Sleep(400);
        return new List<FoundDevice>
        {
            new FoundDevice { Key = "demo|keyboard", Name = "G515 LIGHTSPEED TKL", Kind = "keyboard", Link = "LIGHTSPEED", WirelessId = 0x40B4, Battery = 82, Firmware = "MPK 25.02, build 0016", Features = new[] { 0x0001, 0x0003, 0x0005, 0x1004, 0x4540, 0x8040, 0x8071, 0x8081, 0x8101 } },
            new FoundDevice { Key = "demo|g915", Name = "G915 TKL LIGHTSPEED Wireless RGB Mechanical Gaming Keyboard", Kind = "keyboard", Link = "LIGHTSPEED", Index = 1, WirelessId = 0x408E, Battery = 70, BatteryFromVoltage = true, Millivolts = 3923, Firmware = "MPK 14.01, build 0021", Features = new[] { 0x0001, 0x0003, 0x0005, 0x1001, 0x8040, 0x8071, 0x8081, 0x8100 } },
            new FoundDevice { Key = "demo|g512x", Name = "G512 X 75", Kind = "keyboard", Link = "USB", Index = 0xFF, Firmware = "U1 81.05, build 0020", Features = new[] { 0x0001, 0x0003, 0x0005, 0x4540, 0x8040, 0x8071, 0x8081, 0x8101 } },
            new FoundDevice { Key = "demo|prox60", Name = "PRO X 60", Kind = "keyboard", Link = "LIGHTSPEED", Index = 5, WirelessId = 0x40C1, Battery = 77, Firmware = "MPK 21.01, build 0012", Features = new[] { 0x0001, 0x0003, 0x0005, 0x1004, 0x4540, 0x8040, 0x8071, 0x8081, 0x8101 } },
            new FoundDevice { Key = "demo|g512", Name = "G512 RGB MECHANICAL GAMING KEYBOARD", Kind = "keyboard", Link = "USB", Index = 0xFF, Firmware = "U1 38.03, build 0015", Features = new[] { 0x0001, 0x0003, 0x0005, 0x4540, 0x8060, 0x8070, 0x8080 } },
            new FoundDevice { Key = "demo|g510", Name = "G510s Gaming Keyboard", Kind = "keyboard", Link = "USB", Index = 0xFF, PlainHid = true, HasVendorCollection = true, Path = @"\\?\hid#vid_046d&pid_c22d&mi_01&col02" },
            new FoundDevice { Key = "demo|mouse", Name = "G Pro Wireless Gaming Mouse", Kind = "mouse", Link = "LIGHTSPEED", Index = 1, WirelessId = 0x4079, Battery = 54, BatteryFromVoltage = true, Millivolts = 3824, Firmware = "MPM 15.02, build 0017", Features = new[] { 0x0001, 0x0003, 0x0005, 0x1001, 0x2201, 0x8060, 0x8070, 0x8100 } },
            new FoundDevice { Key = "demo|superlight2", Name = "PRO X SUPERLIGHT 2", Kind = "mouse", Link = "LIGHTSPEED", Index = 4, WirelessId = 0x40A9, Battery = 63, Firmware = "MPM 31.01, build 0018", Features = new[] { 0x0001, 0x0003, 0x0005, 0x1004, 0x2202, 0x8061, 0x8100 } },
            new FoundDevice { Key = "demo|g9x", Name = "G9x Laser Mouse", Kind = "mouse", Link = "USB", Index = 0, Hidpp10 = true, Path = @"\\?\hid#vid_046d&pid_c066&mi_01&col04", Firmware = "58.02, build 0018" },
            new FoundDevice { Key = "demo|g560", Name = "G560 Gaming Speaker", Kind = "device", Link = "USB", Index = 0xFF, Firmware = "U1 22.03, build 0023", Features = new[] { 0x0001, 0x0003, 0x0005, 0x8070, 0x8010, 0x8310, 0x8320, 0x8040, 0x8305 } },
            new FoundDevice { Key = "demo|litra", Name = "Litra Beam LX", Kind = "light", Link = "USB", Index = 0xFF, Firmware = "U1 03.02, build 0011", Features = new[] { 0x0001, 0x0003, 0x0005, 0x1990, 0x8040, 0x8071, 0x8081 } },
            new FoundDevice { Key = "demo|mxmaster", Name = "MX Master 3S", Kind = "mouse", Link = "Bolt", Index = 2, WirelessId = 0xB034, Battery = 70, Firmware = "RBM 21.00, build 0013", Features = new[] { 0x0001, 0x0003, 0x0005, 0x1004, 0x1814, 0x1815, 0x2110, 0x2121, 0x2150, 0x2201 } },
            new FoundDevice { Key = "demo|mxkeys", Name = "MX Keys S", Kind = "keyboard", Link = "Bolt", Index = 3, WirelessId = 0xB378, Battery = 55, Firmware = "RBK 74.00, build 0024", Features = new[] { 0x0001, 0x0003, 0x0005, 0x1004, 0x1814, 0x1815, 0x1982, 0x40A3 } },
            new FoundDevice { Key = "demo|keypad", Name = "MX Creative Keypad", Kind = "device", Link = "USB", Index = 0xFF, Firmware = "U1 66.00, build 0017", Features = new[] { 0x0001, 0x0003, 0x0005, 0x1B04, 0x8040 } },
            new FoundDevice { Key = "demo|g915full", Name = "G915 LIGHTSPEED Wireless RGB Mechanical Gaming Keyboard", Kind = "keyboard", Link = "LIGHTSPEED", Index = 2, WirelessId = 0x407C, Battery = 46, BatteryFromVoltage = true, Millivolts = 3780, Firmware = "MPK 12.01, build 0019", Features = new[] { 0x0001, 0x0003, 0x0005, 0x1001, 0x8040, 0x8071, 0x8081, 0x8100 } },
            new FoundDevice { Key = "demo|g502", Name = "G502 LIGHTSPEED Wireless Gaming Mouse", Kind = "mouse", Link = "LIGHTSPEED", Index = 3, WirelessId = 0x407F, Battery = 88, BatteryFromVoltage = true, Millivolts = 4020, Firmware = "MPM 16.01, build 0020", Features = new[] { 0x0001, 0x0003, 0x0005, 0x1001, 0x2201, 0x8060, 0x8070, 0x8100 } },
            new FoundDevice { Key = "demo|g9", Name = "G9 Laser Mouse", Kind = "mouse", Link = "USB", Index = 0, Hidpp10 = true, Path = @"\\?\hid#vid_046d&pid_c048&mi_01&col04", Firmware = "31.01, build 0012" },
            new FoundDevice { Key = "demo|g700s", Name = "G700s Gaming Mouse", Kind = "mouse", Link = "Wireless", Index = 1, WirelessId = 0x102A, Hidpp10 = true, Path = @"\\?\hid#vid_046d&pid_c531&mi_01&col02", Models = "Wireless 102A", Firmware = "79.02, build 0016" },
            new FoundDevice { Key = "demo|g500s", Name = "G500s Laser Gaming Mouse", Kind = "mouse", Link = "USB", Index = 0, Hidpp10 = true, Path = @"\\?\hid#vid_046d&pid_c24e&mi_01&col04", Firmware = "86.01, build 0003" },
            new FoundDevice { Key = "demo|g502hero", Name = "G502 HERO Gaming Mouse", Kind = "mouse", Link = "USB", Index = 0xFF, Models = "USB C08B", Firmware = "U1 69.00, build 0009", Features = new[] { 0x0001, 0x0003, 0x0005, 0x2201, 0x8060, 0x8070, 0x8100 } },
            new FoundDevice { Key = "demo|headset", Name = "PRO X Wireless Gaming Headset", Kind = "headset", Link = "LIGHTSPEED", Index = 0xFF, Battery = 71, BatteryFromVoltage = true, Millivolts = 3926, Firmware = "U1 12.01, build 0203", Features = new[] { 0x0001, 0x0003, 0x0005, 0x1F20, 0x8300 } },
        };
    }

    int S(float v) { return (int)Math.Round(v * scale); }

    void Rescale(float ns)
    {
        if (ns == scale) return;
        SuspendLayout();
        Scale(new SizeF(ns / scale, ns / scale));
        scale = ns;
        Ui.DpiScale = ns;
        Ui.Refont();
        ResumeLayout();
        Invalidate(true);
        RefreshIcons();
    }

    public void RefreshIcons()
    {
        int t = (int)Math.Round(16 * WindowHelpers.At(Point.Empty)), b = S(32);
        bool light = Theme.TaskbarLight();
        if (Tray != null && (t != traySize || light != trayLight)) { traySize = t; trayLight = light; Tray.Icon = Logo.MakeTray(t, light); }
        if (b != bigSize) { bigSize = b; Icon = Logo.MakeIcon(b); }
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WindowHelpers.DpiChanged) Rescale(WindowHelpers.FromMessage(m));
        Presence.Power(m);
        if (m.Msg == 0x0219 && !demo) RescanSoon();
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
        if (!WindowHelpers.Rounded) using (var pen = new Pen(Theme.Border)) e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
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

    void ToggleView()
    {
        if (view == null)
        {
            view = new KeyboardView(new FakeHidpp(), KeyLayout.Load("g515-tkl.json"));
            view.VisibleChanged += (s, e) => wantView = view.Visible;
        }
        if (view.Visible) view.Hide(); else ShowView();
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
        if (updatePage.Busy || ready != null) return;
        available = v;
        ShowRow(v != null ? "Update available" : null, "download", null);
        if (v != null) updatePage.Available(v);
    }

    public string Available { get { return available ?? (ready != null ? ready + " installed" : null); } }


    public void CheckUpdates(bool manual)
    {
        if (manual) { checkText.Text = "Checking for updates…"; updatePage.Checking(); }
        Task.Run(() =>
        {
            string v = null, err = null;
            if (demo) { Thread.Sleep(800); v = DemoVersion(); }
            else try { v = Updater.Latest(); } catch (Exception ex) { err = ex.Message; }
            BeginInvoke((Action)(() => GotVersion(v, err, manual)));
        });
    }

    void GotVersion(string v, string err, bool manual)
    {
        if (err != null) Log.Add("", "update", "Couldn't check for updates: " + err);
        else if (v != null) Log.Add("", "update", !Updater.IsNewerVersion(v, Application.ProductVersion) ? "Checked for updates: " + v + " is the latest" : "Version " + v + " is available" + (!manual && !demo && v == Updater.Skipped ? ", skipped" : ""));
        if (ready != null) { if (manual) { checkText.Text = "Version " + ready + " installed. Restart G-Lite to use it."; updatePage.Ready(ready); } return; }
        if (err != null) { if (manual) { checkText.Text = "Couldn't check for updates: " + err; updatePage.CheckFailed(err); } return; }
        if (v == null || !Updater.IsNewerVersion(v, Application.ProductVersion)) { if (manual) { checkText.Text = "G-Lite is up to date"; updatePage.Current(); } return; }
        if (!manual && !demo && v == Updater.Skipped) return;
        if (!manual && Updater.Auto) { Install(v); return; }
        if (manual) checkText.Text = "Version " + v + " is available";
        ShowUpdate(v);
    }

    void ShowRow(string text, string icon, string label)
    {
        rowShown = text != null;
        updateBtn.Visible = rowShown;
        if (rowShown) { updateBtn.Text = text; updateBtn.Icon = icon; updateBtn.Invalidate(); }
        labelShown = label != null;
        readyText.Text = label ?? "";
        readyText.Visible = labelShown;
        FitList();
    }

    void FitList()
    {
        int bottom = labelShown ? navTop - 40 - 24 : rowShown ? navTop - 40 - 8 : navTop - 8;
        deviceList.Height = S(bottom - 78);
    }

    void Ready(string v)
    {
        ready = v;
        available = null;
        ShowRow("Restart G-Lite", "refresh", "Version " + v + " installed");
        updatePage.Ready(v);
    }

    void Restart()
    {
        if (demo) { updatePage.Note("Demo: G-Lite would restart now", false); return; }
        Log.Add("", "you", "Restart G-Lite to use " + ready);
        try { Updater.Restart(); }
        catch (Exception ex) { Log.Add("", "error", "Couldn't restart: " + ex.Message); updatePage.Note("Couldn't restart: " + ex.Message + ". Quit G-Lite and open it again.", true); ShowUpdatePage(false); return; }
        HidePanel();
        WaitAll();
        if (Tray != null) Tray.Visible = false;
        Application.Exit();
    }

    void Install(string v)
    {
        available = v;
        Log.Add("", "update", "Downloading " + v + (demo ? " (demo, nothing is downloaded)" : ""));
        ShowRow("Downloading…", "download", null);
        updatePage.Progress(v, -1);
        if (demo) { DemoInstall(v); return; }
        Task.Run(() =>
        {
            try
            {
                Updater.Install(v, percent => BeginInvoke((Action)(() => { ShowRow(percent < 0 ? "Downloading…" : "Downloading " + percent + "%", "download", null); updatePage.Progress(v, percent); })));
                BeginInvoke((Action)(() => Ready(v)));
            }
            catch (Exception ex) { Log.Add("", "error", "Update to " + v + " failed: " + ex.Message); BeginInvoke((Action)(() => { ShowRow("Update failed", "download", null); updatePage.Failed(v, ex.Message); })); }
        });
    }

    static string DemoVersion()
    {
        var current = Application.ProductVersion;
        int dash = current.IndexOf('-');
        var core = new Version(dash < 0 ? current : current.Substring(0, dash));
        return new Version(core.Major, core.Minor, core.Build + 1) + (dash < 0 ? "" : current.Substring(dash));
    }

    static string DemoChangelog(string version)
    {
        string real = "";
        try
        {
            real = Updater.Changelog(Application.ProductVersion);
            int start = real.IndexOf("\n## ");
            real = start < 0 ? "" : real.Substring(start + 1);
        }
        catch (Exception) { }
        return "## [" + version + "] - " + DateTime.Today.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) + "\n\n### Added\n\n- A pretend version, so the update screen can be tried in the demo. Nothing is downloaded or installed.\n\n### Fixed\n\n- The first try fails on purpose, to show what a failed update looks like. Try again works.\n\n" + real;
    }

    int demoTries;

    void DemoInstall(string v)
    {
        bool fail = demoTries++ == 0;
        int percent = 0;
        var timer = new System.Windows.Forms.Timer { Interval = 50 };
        timer.Tick += (s, e) =>
        {
            percent += 2;
            if (fail && percent >= 60)
            {
                timer.Dispose();
                Log.Add("", "error", "Update to " + v + " failed: the download doesn't match its checksum (demo)");
                ShowRow("Update failed", "download", null);
                updatePage.Failed(v, "the download doesn't match its checksum, so nothing was changed (demo)");
                return;
            }
            if (percent >= 100)
            {
                timer.Dispose();
                Log.Add("", "update", "Installed " + v + " (demo, nothing changed)");
                Ready(v);
                return;
            }
            ShowRow("Downloading " + percent + "%", "download", null);
            updatePage.Progress(v, percent);
        };
        timer.Start();
    }

    public void SetNote(string n) { note = n; }

    public List<FoundDevice> Listed { get { return found; } }

    public void Open(string key)
    {
        selected = key;
        ShowPanel();
    }

    public void OpenSettings()
    {
        ShowPanel();
        ShowSettings();
    }

    public void ShowPanel()
    {
        if (WindowState == FormWindowState.Minimized) { Show(); WindowState = FormWindowState.Normal; }
        var wa = Screen.FromPoint(Cursor.Position).WorkingArea;
        Rescale(WindowHelpers.At(moved ? Location : Cursor.Position));
        if (!moved) Location = new Point(Math.Max(wa.Left, wa.Right - Width - S(12)), Math.Max(wa.Top, wa.Bottom - Height - S(12)));
        Show();
        if (view != null && wantView) ShowView();
        Activate();
        Pick(selected);
        Rescan();
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
        if (shown is IPage) ((IPage)shown).Hidden();
        shown = null;
        BeginInvoke((Action)WindowHelpers.Trim);
    }

    [STAThread]
    static void Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "--swap") { Environment.ExitCode = Updater.Swap(args[1]); return; }
        WindowHelpers.EnableDpiAwareness();
        bool fresh, demo = args.Contains("--demo");
        using (var mutex = new Mutex(true, demo ? "GLiteDemo" : "GLite", out fresh))
        {
            if (!fresh && args.Contains("--restart"))
                try { fresh = mutex.WaitOne(15000); } catch (AbandonedMutexException) { fresh = true; }
            if (!fresh) return;
            Settings.Init();
            Theme.Apply();
            Application.EnableVisualStyles();
            if (demo) Backups.Root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "GLite demo backups");
            if (demo) Log.File = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "GLite demo log.txt");
            Log.Add("", "start", "G-Lite " + Application.ProductVersion + (demo ? " demo" : "") + ", " + Report.Windows());
            if (!demo) Updater.Cleanup();
            var fake = demo ? new FakeHidpp() : null;
            var panel = new MainWindow(fake);
            var tray = new NotifyIcon { Visible = true };
            tray.ContextMenuStrip = new ContextMenuStrip { Renderer = Ui.Menus, ShowImageMargin = false, BackColor = Theme.Card };
            var menu = tray.ContextMenuStrip;
            menu.Tag = "round";
            menu.Opened += (s, e) => WindowHelpers.Corners(menu.Handle, Theme.Border);
            var self = System.Diagnostics.Process.GetCurrentProcess();
            var lastCpu = TimeSpan.Zero;
            var lastAt = self.StartTime.ToUniversalTime();
            Func<double> measure = () =>
            {
                self.Refresh();
                var cpu = self.TotalProcessorTime;
                var now = DateTime.UtcNow;
                double percent = (cpu - lastCpu).TotalMilliseconds / Math.Max(1, (now - lastAt).TotalMilliseconds) / Environment.ProcessorCount * 100;
                lastCpu = cpu;
                lastAt = now;
                return percent;
            };
            Func<FoundDevice, string> state = f => f.Asleep ? "" : f.Battery >= 0 ? f.BatteryText : "";
            var tip = new TrayTip { Tray = tray };
            tip.Rows = () =>
            {
                var rows = new List<string[]>();
                if (demo) rows.Add(new[] { "G-Lite", "demo" });
                rows.Add(new[] { "CPU", measure().ToString("0.0") + "%" });
                rows.Add(new[] { "RAM", (WindowHelpers.PrivateMemory() >> 20) + " MB" });
                rows.AddRange(panel.Listed.Select(f => new[] { f.ShortName, state(f), f.Level != -1 ? f.Level.ToString() : "" }));
                if (panel.Available != null) rows.Add(new[] { "Update", panel.Available });
                return rows;
            };
            tray.MouseMove += (s, e) => { if (menu.Visible) return; tip.PointerMoved(); panel.ReadBatteries(); };
            tray.MouseDown += (s, e) => tip.Dismiss();
            Action fill = () =>
            {
                menu.Items.Clear();
                foreach (var f in panel.Listed)
                {
                    var key = f.Key;
                    var row = new MenuRow(f.ShortName) { Level = f.Level, Value = state(f) };
                    row.Click += (s, e) => panel.Open(key);
                    menu.Items.Add(row);
                }
                if (panel.Listed.Count > 0) menu.Items.Add(new ToolStripSeparator());
                menu.Items.Add("Scan for devices", null, (s, e) => panel.Rescan());
                menu.Items.Add("Settings", null, (s, e) => panel.OpenSettings());
                menu.Items.Add("Quit G-Lite", null, (s, e) => { tray.Visible = false; Application.Exit(); });
                float k = WindowHelpers.At(Cursor.Position);
                menu.Padding = new Padding((int)(4 * k), (int)(6 * k), (int)(4 * k), (int)(6 * k));
                menu.Items.Insert(0, new ToolStripLabel { AutoSize = false, Height = (int)(5 * k), Enabled = false });
                foreach (ToolStripItem item in menu.Items) if (item is ToolStripMenuItem) item.Padding = new Padding((int)(6 * k), (int)(3 * k), (int)(6 * k), (int)(3 * k));
            };
            fill();
            menu.Opening += (s, e) => { tip.Dismiss(); fill(); e.Cancel = false; };
            tray.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) { if (panel.Visible && panel.WindowState != FormWindowState.Minimized) panel.HidePanel(); else panel.ShowPanel(); } };
            panel.CreateControl();
            panel.Handle.ToString();
            panel.Tray = tray;
            panel.RefreshIcons();
            panel.Rescan();
            SystemEvents.DisplaySettingsChanged += (o, e) => panel.RefreshIcons();
            SystemEvents.UserPreferenceChanged += (o, e) => { panel.RefreshIcons(); Theme.Apply(); };
            if (args.Contains("--restart")) panel.SetNote("Successfully updated to v" + Application.ProductVersion);
            using (var k = Registry.CurrentUser.OpenSubKey(RunKey, true))
            {
                var run = k == null ? null : k.GetValue("GLite") as string;
                if (run != null && !run.Contains("--tray")) k.SetValue("GLite", run + " --tray");
            }
            if (!args.Contains("--tray")) panel.ShowPanel();
            else
            {
                var settle = new System.Windows.Forms.Timer { Interval = 15000 };
                settle.Tick += (s, e) => { settle.Dispose(); if (!panel.Visible) WindowHelpers.Trim(); };
                settle.Start();
            }
            if (Updater.CheckForUpdates) panel.CheckUpdates(false);
            Application.Run();
            tray.Visible = false;
        }
    }
}

class DeviceTable
{
    public Func<FoundDevice, bool> Matches;
    public Type Screen;
    public Func<FoundDevice, Hidpp, MouseEventHandler, Control> Make;
    public Func<FoundDevice, Hidpp> Fake;

    public static readonly DeviceTable[] All =
    {
        new DeviceTable { Matches = f => f.IsG515, Screen = typeof(Page515), Make = (f, d, drag) => new Page515(f, d, drag, KeyboardKinds.G515) },
        new DeviceTable { Matches = f => f.IsG915, Screen = typeof(Page515), Make = (f, d, drag) => new Page515(f, d, drag, f.Name.ToUpper().Contains("TKL") ? KeyboardKinds.G915Tkl : f.Name.ToUpper().Contains("G815") ? KeyboardKinds.G815 : KeyboardKinds.G915), Fake = f => new FakeG915() },
        new DeviceTable { Matches = f => f.IsG512X, Screen = typeof(Page515), Make = (f, d, drag) => new Page515(f, d, drag, KeyboardKinds.G512X), Fake = f => FakeG915.G512X() },
        new DeviceTable { Matches = f => f.IsRgbKeyboard, Screen = typeof(Page515), Make = (f, d, drag) => new Page515(f, d, drag, KeyboardKinds.For(f)), Fake = f => FakeG915.G512X() },
        new DeviceTable { Matches = f => f.IsLedKeyboard, Screen = typeof(PageSpeaker), Make = (f, d, drag) => new PageSpeaker(f, d, drag), Fake = f => new FakeSpeaker { Count = 2 } },
        new DeviceTable { Matches = f => f.IsG510, Screen = typeof(PageG510), Make = (f, d, drag) => new PageG510(f, d, drag), Fake = f => new FakeG510() },
        new DeviceTable { Matches = f => f.IsG9x, Screen = typeof(PageMouse), Make = (f, d, drag) => new PageMouse(f, d, drag, new G9xMouse(G9xModel.G9x)), Fake = f => new FakeG9x() },
        new DeviceTable { Matches = f => f.IsG5, Screen = typeof(PageMouse), Make = (f, d, drag) => new PageMouse(f, d, drag, G5Mouse.G5()), Fake = f => FakeG9x.Short(0x82) },
        new DeviceTable { Matches = f => f.IsG9, Screen = typeof(PageMouse), Make = (f, d, drag) => new PageMouse(f, d, drag, G5Mouse.G9()), Fake = f => FakeG9x.Short(0x84) },
        new DeviceTable { Matches = f => f.IsG500, Screen = typeof(PageMouse), Make = (f, d, drag) => new PageMouse(f, d, drag, new G9xMouse(f.G500Model)), Fake = f => FakeG9x.G500s() },
        new DeviceTable { Matches = f => f.IsMouse && f.Has(0x2202), Screen = typeof(PageMouse), Make = (f, d, drag) => new PageMouse(f, d, drag, new HidppMouse()), Fake = f => FakeMouse.ProX2() },
        new DeviceTable { Matches = f => f.IsMouse, Screen = typeof(PageMouse), Make = (f, d, drag) => new PageMouse(f, d, drag, new HidppMouse()), Fake = f => FakeMouse.For(f) },
        new DeviceTable { Matches = f => f.IsG560, Screen = typeof(PageSpeaker), Make = (f, d, drag) => new PageSpeaker(f, d, drag), Fake = f => new FakeSpeaker() },
        new DeviceTable { Matches = f => f.IsLight, Screen = typeof(PageLight), Make = (f, d, drag) => new PageLight(f, d, drag), Fake = f => new FakeLight() },
        new DeviceTable { Matches = f => f.IsHeadset, Screen = typeof(PageHeadset), Make = (f, d, drag) => new PageHeadset(f, d, drag), Fake = f => new FakeHeadset() },
    };

    public static readonly DeviceTable Other = new DeviceTable { Matches = OtherDevice.Supports, Screen = typeof(PageOtherDevice), Make = (f, d, drag) => new PageOtherDevice(f, d, drag), Fake = f => new FakeOther() };

    public static DeviceTable For(FoundDevice f)
    {
        if (f.Asleep) return null;
        if (!f.IsG) return Other.Matches(f) ? Other : null;
        return All.FirstOrDefault(s => s.Matches(f));
    }
}
