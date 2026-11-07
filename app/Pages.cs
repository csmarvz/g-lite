using System;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

interface IPage
{
    void Shown();
    void Hidden();
    Task Queue { get; }
}

abstract class DevicePage : Control, IPage
{
    protected Found found;
    readonly Hidpp fake;
    Hidpp dev;
    Task queue = Task.FromResult(0);
    readonly string prefix, onDevice;
    readonly Label sub;
    readonly Drop profiles;
    readonly Field nameField;
    readonly Btn newBtn, delBtn, reportBtn;
    readonly StatusLine status;
    readonly ReportView report;
    bool naming;
    string readAt = "";
    public string Note;

    protected DevicePage(Found f, Hidpp fake, MouseEventHandler drag, string title, string noun, string prefix)
    {
        found = f;
        this.fake = fake;
        this.prefix = prefix;
        onDevice = "As on the " + noun;
        Ui.Style(this, () => BackColor = Theme.Bg);
        MouseDown += drag;
        Ui.Text(this, title, 20, 14, () => Theme.Text, 13.5f, Ui.Semi).MouseDown += drag;
        sub = Ui.Text(this, "", 21, 44, () => Theme.Dim, 9f);
        sub.MouseDown += drag;
        var reread = Ui.Add(this, new Btn { Look = Look.Ghost, Icon = "refresh", AccessibleName = "Read again from the " + noun }, 536, 10, 36, 28);
        reread.Click += (s, e) => Load();
        reportBtn = Ui.Add(this, new Btn { Look = Look.Ghost, Text = "Report", AccessibleName = "Report of the " + noun }, 536 - 4 - 80, 10, 80, 28);

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
            else if (profiles.Index > 0) { Settings.Remove(prefix + profiles.Items[profiles.Index]); LoadSets(null); }
        };

        var line = Ui.Add(this, new Control(), 0, 623, 656, 1);
        Ui.Style(line, () => line.BackColor = Theme.Border);
        status = Ui.Add(this, new StatusLine(), 20, 633, 460, 36);
        report = Ui.Add(this, new ReportView(noun, () => ReportView.TitleFor(found), Collect), 0, 72, 656, 608);
        report.Closed += () => { reportBtn.On = false; reportBtn.Invalidate(); };
        reportBtn.Click += (s, e) =>
        {
            if (report.Visible) { report.Close(); return; }
            reportBtn.On = true;
            reportBtn.Invalidate();
            report.Open();
        };
        LoadSets(null);
        ShowSub();
    }

    public Task Queue { get { return queue; } }

    protected bool Demo { get { return fake != null; } }

    protected Btn BackupsButton(string noun)
    {
        reportBtn.Left = 536 - 4 - 80 - 4 - 80;
        return Ui.Add(this, new Btn { Look = Look.Ghost, Text = "Backups", AccessibleName = "Backups of the " + noun }, 536 - 4 - 80, 10, 80, 28);
    }

    public static string When(Backups.Entry e) { return e.When.ToLocalTime().ToString("d MMM HH:mm", CultureInfo.InvariantCulture); }

    protected void BackupMenu(Btn anchor, string device, string factory, Action restoreFactory, Func<Backups.Entry, string> describe, Func<Backups.Entry, bool> good, Action<Backups.Entry> restore)
    {
        var m = new ContextMenuStrip { Renderer = Ui.Menus, Font = Ui.Get(9.75f, Ui.Regular), ShowImageMargin = false };
        m.BackColor = Theme.Card;
        m.ForeColor = Theme.Text;
        if (factory != null)
        {
            m.Items.Add(new ToolStripMenuItem(factory, null, (s, e) => restoreFactory()));
            m.Items.Add(new ToolStripSeparator());
        }
        var list = Backups.List(device);
        m.Items.Add(new ToolStripMenuItem(list.Count == 0 ? "No backups yet. G-Lite makes one before each save." : "Restore a backup") { Enabled = false });
        foreach (var b in list)
        {
            var entry = b;
            var item = new ToolStripMenuItem(When(b) + " · " + describe(b) + (b.First ? " · first backup" : ""), null, (s, e) => restore(entry));
            item.Enabled = good(b);
            m.Items.Add(item);
        }
        m.Items.Add(new ToolStripSeparator());
        m.Items.Add(new ToolStripMenuItem("Open the backups folder", null, (s, e) =>
        {
            var dir = Backups.Folder(device);
            Directory.CreateDirectory(dir);
            Process.Start(dir);
        }));
        m.Closed += (s, a) => BeginInvoke((Action)m.Dispose);
        m.Show(anchor, new Point(anchor.Width, anchor.Height + (int)(2 * Ui.K)), ToolStripDropDownDirection.BelowLeft);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) queue = queue.ContinueWith(_ => { if (dev != null) dev.Dispose(); dev = null; });
        base.Dispose(disposing);
    }

    public virtual void Shown() { Load(); }

    public virtual void Hidden() { }

    public virtual void Restyle() { }

    public abstract void Load();

    protected abstract string Current();

    protected abstract void Use(string text);

    protected abstract void Revert();

    protected virtual void Calm() { Say(""); }

    protected virtual void Lost() { }

    public void SetFound(Found f)
    {
        found = f;
        ShowSub();
    }

    void ShowSub()
    {
        sub.Text = found.Link + (found.Battery >= 0 ? " · " + found.BatteryText : "") + (readAt != "" ? " · read at " + readAt : "");
    }

    public void Say(string text, bool warn = false, Action click = null) { status.Set(text, warn, click); }

    protected void Post(string text, bool warn) { if (IsHandleCreated) BeginInvoke((Action)(() => Say(text, warn))); }

    protected static void Shade(Label l, bool on) { l.ForeColor = on ? Theme.Dim : Ui.Mix(Theme.Card, Theme.Dim, 0.45); }

    protected static Label Value(Control parent, int x, int y, int w)
    {
        var l = Ui.Text(parent, "", x, y, () => Theme.Dim, 9f);
        l.AutoSize = false;
        l.Size = new Size(w, 18);
        l.TextAlign = ContentAlignment.TopRight;
        return l;
    }

    void Collect(Action<string, string> done)
    {
        string text = null;
        Do(h => text = Report.Build(h, found, GetType().Name), () => done(text, null), msg => done(null, msg));
    }

    protected string Who { get { return found.Short == "" ? found.Name : found.Short; } }

    protected void Did(string what) { Log.Add(Who, "you", what); }

    protected void Do(Action<Hidpp> a, Action done = null, Action<string> failed = null)
    {
        queue = queue.ContinueWith(_ =>
        {
            try
            {
                if (dev == null)
                {
                    dev = fake ?? new Hidpp(found.Path, found.Index);
                    dev.Who = Who;
                }
                a(dev);
                if (done != null && IsHandleCreated) BeginInvoke(done);
            }
            catch (Exception ex)
            {
                Log.Add(Who, "error", ex.Message);
                if (dev != null && !(ex is HidppError) && !(ex is Refused)) { dev.Dispose(); dev = null; Lost(); }
                if (failed != null && IsHandleCreated) BeginInvoke((Action)(() => failed(ex.Message)));
                else Post(ex.Message, true);
            }
        });
    }

    protected void Loaded()
    {
        readAt = DateTime.Now.ToString("HH:mm");
        if (!naming) profiles.Index = 0;
        ShowSets();
        ShowSub();
        Calm();
        if (Note == null) return;
        var n = Note;
        Note = null;
        Say(n);
        var t = new Timer { Interval = 8000 };
        t.Tick += (s, e) => { t.Dispose(); if (status.Shown == n) Calm(); };
        t.Start();
    }

    void LoadSets(string select)
    {
        profiles.Items.Clear();
        profiles.Items.Add(onDevice);
        foreach (var k in Settings.Keys().Where(k => k.StartsWith(prefix)).Select(k => k.Substring(prefix.Length)).OrderBy(k => k)) profiles.Items.Add(k);
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
        try { Use(Settings.Get(prefix + profiles.Items[i])); }
        catch (Exception) { Say("This profile couldn't be read from settings.txt", true); }
    }

    void SaveSet(string name)
    {
        if (name == "") { Say("Type a name for the profile first", true); nameField.Box.Focus(); return; }
        if (name.Contains("=")) { Say("A profile name can't contain =", true); nameField.Box.Focus(); return; }
        if (name == onDevice) { Say("Pick another name for the profile", true); nameField.Box.Focus(); return; }
        Settings.Set(prefix + name, Current());
        EndName();
        LoadSets(name);
        Say("Saved the profile " + name);
    }
}

class ColorRow
{
    public static readonly int[] Swatches = { 0xFFFFFF, 0xFF2020, 0xFF8000, 0xFFD000, 0x30D050, 0x3080FF };
    readonly Swatch[] swatches = new Swatch[Swatches.Length];
    readonly Swatch custom, more;
    readonly Control parent;
    int lastCustom = -1, color;
    public event Action<int> Picked;

    public ColorRow(Control parent, int y)
    {
        this.parent = parent;
        for (int i = 0; i < Swatches.Length; i++)
        {
            int c = Swatches[i];
            swatches[i] = Ui.Add(parent, new Swatch { Fill = Color.FromArgb(c | unchecked((int)0xFF000000)), AccessibleName = "Color " + c.ToString("X6") }, 10 + i * 36, y, 34, 34);
            swatches[i].Click += (s, e) => Pick(c);
        }
        custom = Ui.Add(parent, new Swatch { AccessibleName = "Your color" }, 10 + Swatches.Length * 36, y, 34, 34);
        custom.Click += (s, e) => { if (lastCustom >= 0) Pick(lastCustom); };
        more = Ui.Add(parent, new Swatch { AccessibleName = "Pick a color" }, 10 + Swatches.Length * 36, y, 34, 34);
        more.Click += (s, e) => Choose();
    }

    void Pick(int c) { if (Picked != null) Picked(c); }

    void Choose()
    {
        var form = parent.FindForm();
        using (var dlg = new ColorDialog { FullOpen = true, Color = Color.FromArgb(color | unchecked((int)0xFF000000)) })
            if (dlg.ShowDialog(form) == DialogResult.OK) Pick(dlg.Color.ToArgb() & 0xFFFFFF);
        form.Activate();
    }

    public void Sync(int c, bool on)
    {
        color = c;
        bool isCustom = !Swatches.Contains(c);
        if (isCustom) lastCustom = c;
        for (int i = 0; i < swatches.Length; i++) { swatches[i].Enabled = on; swatches[i].On = on && Swatches[i] == c; swatches[i].Invalidate(); }
        custom.Visible = lastCustom >= 0;
        custom.Fill = Color.FromArgb(lastCustom | unchecked((int)0xFF000000));
        custom.Enabled = on;
        custom.On = on && isCustom;
        custom.Invalidate();
        more.Left = custom.Left + (custom.Visible ? (int)Math.Round(36 * Ui.K) : 0);
        more.Enabled = on;
    }
}

class InfoPage : Control
{
    readonly Label title, sub, head, text, link, battery, firmware, features;
    readonly Btn again, create, reportBtn;
    readonly ReportView report;
    Found found;

    public InfoPage(Found f, MouseEventHandler drag, Action rescan)
    {
        Ui.Style(this, () => BackColor = Theme.Bg);
        MouseDown += drag;
        title = Ui.Text(this, "", 20, 14, () => Theme.Text, 13.5f, Ui.Semi);
        title.MouseDown += drag;
        sub = Ui.Text(this, "", 21, 44, () => Theme.Dim, 9f);
        sub.MouseDown += drag;

        var state = Ui.Add(this, new Box(), 20, 80, 616, 104);
        head = Ui.Text(state, "", 14, 12, () => Theme.Dim, 9f, Ui.Semi);
        text = Ui.Text(state, "", 14, 38, () => Theme.Text, 9.75f);
        text.AutoSize = false;
        text.Size = new Size(440, 60);
        again = Ui.Add(state, new Btn { Text = "Scan again", Icon = "refresh" }, 616 - 14 - 130, 36, 130, 32);
        again.Click += (s, e) => rescan();
        create = Ui.Add(state, new Btn { Look = Look.Primary, Text = "Create report" }, 616 - 14 - 130, 36, 130, 32);

        var details = Ui.Add(this, new Box(), 20, 196, 616, 196);
        Ui.Text(details, "Details", 14, 12, () => Theme.Dim, 9f, Ui.Semi);
        link = Row(details, "Connection", 40);
        battery = Row(details, "Battery", 68);
        firmware = Row(details, "Firmware", 96);
        features = Row(details, "Features", 124);
        features.AutoSize = false;
        features.Size = new Size(470, 60);
        reportBtn = Ui.Add(this, new Btn { Look = Look.Ghost, Text = "Report", AccessibleName = "Report of this device" }, 536 - 4 - 80, 10, 80, 28);
        report = Ui.Add(this, new ReportView("device", () => ReportView.TitleFor(found), Collect), 0, 72, 656, 608);
        report.Closed += () => { reportBtn.On = false; reportBtn.Invalidate(); };
        reportBtn.Click += (s, e) => { if (report.Visible) report.Close(); else Open(); };
        create.Click += (s, e) => Open();
        SetFound(f);
    }

    void Open()
    {
        reportBtn.On = true;
        reportBtn.Invalidate();
        report.Open();
    }

    void Collect(Action<string, string> done)
    {
        var f = found;
        Task.Run(() =>
        {
            string t = null, err = null;
            try { using (var h = new Hidpp(f.Path, f.Index)) t = Report.Build(h, f, "none, not supported yet"); }
            catch (Exception ex) { err = ex.Message; }
            if (IsHandleCreated) BeginInvoke((Action)(() => done(t, err)));
        });
    }

    static Label Row(Control box, string name, int y)
    {
        Ui.Text(box, name, 14, y, () => Theme.Dim, 9.75f);
        return Ui.Text(box, "", 120, y, () => Theme.Text, 9.75f);
    }

    public void SetFound(Found f)
    {
        found = f;
        title.Text = f.Short == "" ? "Logitech device" : f.Short;
        sub.Text = f.Sub;
        head.Text = f.Asleep ? "Asleep" : "Not supported yet";
        text.Text = f.Asleep
            ? "It's paired but didn't answer. Wake it with a key press or a click, and G-Lite picks it up."
            : "G-Lite can't change this device yet. A report of what it supports helps add it. G-Lite only reads the device, and nothing is sent until you paste the report somewhere.";
        again.Visible = f.Asleep;
        create.Visible = reportBtn.Visible = !f.Asleep;
        if (f.Asleep && report.Visible) report.Close();
        link.Text = f.Link + (f.Index == 0xFF ? "" : " · slot " + f.Index) + (f.Wpid != 0 ? " · ID " + f.Wpid.ToString("X4") : "");
        battery.Text = f.Battery < 0 ? "–" : f.BatteryText + (f.Approx ? " (estimated from the voltage)" : "");
        firmware.Text = f.Firmware == "" ? "–" : f.Firmware;
        features.Text = f.Features.Length == 0 ? "–" : string.Join("  ", f.Features.Select(x => x.ToString("X4")));
    }
}

class EmptyPage : Control
{
    readonly Label title, text, tips;
    readonly Btn scan, report;

    public EmptyPage(MouseEventHandler drag, Action rescan)
    {
        Ui.Style(this, () => BackColor = Theme.Bg);
        MouseDown += drag;
        title = Centered(Ui.Text(this, "", 0, 150, () => Theme.Text, 15f, Ui.Semi), 36);
        title.MouseDown += drag;
        text = Centered(Ui.Text(this, "", 0, 192, () => Theme.Dim, 9.75f), 44);
        text.MouseDown += drag;
        scan = Ui.Add(this, new Btn { Look = Look.Primary, Text = "Scan again" }, 328 - 8 - 150, 252, 150, 32);
        scan.Click += (s, e) => rescan();
        report = Ui.Add(this, new Btn { Text = "Report your device" }, 328 + 8, 252, 150, 32);
        report.Click += (s, e) => Process.Start("https://github.com/csmarvz/g-lite/issues/new");
        tips = Centered(Ui.Text(this, "Turn the device on and wake it with a key press or a click.\nQuit G HUB, which can hold on to the device.\nPair Bluetooth devices in Windows first.", 0, 320, () => Theme.Dim, 9f), 64);
        tips.MouseDown += drag;
        Searching(true);
    }

    Label Centered(Label l, int h)
    {
        l.AutoSize = false;
        l.TextAlign = ContentAlignment.TopCenter;
        l.Bounds = new Rectangle(56, l.Top, 656 - 112, h);
        return l;
    }

    public void Searching(bool on)
    {
        title.Text = on ? "Looking for devices…" : "No supported device found";
        text.Text = on ? "" : "G-Lite looked over USB, LIGHTSPEED, Unifying and Bolt receivers, and Bluetooth.";
        scan.Visible = report.Visible = tips.Visible = !on;
    }
}

class ReportView : Control
{
    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] static extern int SetWindowTheme(IntPtr h, string app, string id);
    const string Issues = "https://github.com/csmarvz/g-lite/issues/new";
    readonly TextBox box;
    readonly Label size;
    readonly Btn again, issue, copy;
    readonly StatusLine status;
    readonly Func<string> title;
    readonly Action<Action<string, string>> read;
    string text = "";
    public event Action Closed;

    public ReportView(string noun, Func<string> title, Action<Action<string, string>> read)
    {
        this.title = title;
        this.read = read;
        Visible = false;
        Ui.Style(this, () => BackColor = Theme.Bg);

        var head = Ui.Add(this, new Box(), 20, 8, 616, 84);
        Ui.Text(head, "Device report", 14, 12, () => Theme.Dim, 9f, Ui.Semi);
        var intro = Ui.Text(head, "What G-Lite reads from this " + noun + ". It has no serial number, no name you gave the device and no Windows path.", 14, 34, () => Theme.Text, 9.75f);
        intro.AutoSize = false;
        intro.Size = new Size(440, 40);
        again = Ui.Add(head, new Btn { Text = "Read again", Icon = "refresh" }, 616 - 14 - 130, 26, 130, 32);
        again.Click += (s, e) => Read();

        var body = Ui.Add(this, new Box(), 20, 104, 616, 435);
        Ui.Text(body, "Report", 14, 12, () => Theme.Dim, 9f, Ui.Semi);
        size = Ui.Text(body, "", 300, 12, () => Theme.Dim, 9f);
        size.AutoSize = false;
        size.Size = new Size(302, 18);
        size.TextAlign = ContentAlignment.TopRight;
        var inset = Ui.Add(body, new Control(), 14, 38, 588, 383);
        Ui.Style(inset, () => inset.BackColor = Theme.Bg);
        box = Ui.Add(inset, new TextBox { Multiline = true, ReadOnly = true, WordWrap = false, ScrollBars = ScrollBars.Both, BorderStyle = BorderStyle.None, AccessibleName = "Device report" }, 8, 6, 576, 373);
        Ui.Font(box, 9f, "Consolas");
        Ui.Style(box, () => { box.BackColor = Theme.Bg; box.ForeColor = Theme.Text; Dark(); });
        box.HandleCreated += (s, e) => Dark();

        var line = Ui.Add(this, new Control(), 0, 551, 656, 1);
        Ui.Style(line, () => line.BackColor = Theme.Border);
        status = Ui.Add(this, new StatusLine(), 20, 561, 216, 36);
        var back = Ui.Add(this, new Btn { Text = "Back" }, 244, 564, 76, 32);
        back.Click += (s, e) => Close();
        issue = Ui.Add(this, new Btn { Text = "Open GitHub issue" }, 328, 564, 160, 32);
        issue.Click += (s, e) => OpenIssue();
        copy = Ui.Add(this, new Btn { Look = Look.Primary, Text = "Copy report" }, 496, 564, 140, 32);
        copy.Click += (s, e) => { if (Put(text)) status.Set("Copied to the clipboard", false, null); };
    }

    public static string TitleFor(Found f)
    {
        return "Device report: " + (f.Short == "" ? "Logitech device" : f.Short) + (f.Wpid != 0 ? " (" + f.Wpid.ToString("X4") + ")" : "");
    }

    public void Open()
    {
        Visible = true;
        BringToFront();
        if (text == "") Read();
    }

    public void Close()
    {
        Visible = false;
        if (Closed != null) Closed();
    }

    void Dark()
    {
        if (box.IsHandleCreated) SetWindowTheme(box.Handle, Theme.Light ? "Explorer" : "DarkMode_Explorer", null);
    }

    void Read()
    {
        again.Enabled = issue.Enabled = copy.Enabled = false;
        box.Text = "Reading the device. This takes a few seconds and changes nothing on it.";
        size.Text = "";
        status.Set("Reading…", false, null);
        read((t, err) =>
        {
            again.Enabled = true;
            if (t == null)
            {
                box.Text = text != "" ? text : "G-Lite couldn't read the device: " + err;
                issue.Enabled = copy.Enabled = text != "";
                status.Set(err, true, null);
                return;
            }
            text = t;
            box.Text = t;
            size.Text = (Encoding.UTF8.GetByteCount(t) / 1024.0).ToString("0.0", CultureInfo.InvariantCulture) + " KB · " + t.TrimEnd().Split('\n').Length + " lines";
            issue.Enabled = copy.Enabled = true;
            status.Set("Read only. Nothing is sent.", false, null);
        });
    }

    bool Put(string t)
    {
        try { Clipboard.SetText(t); return true; }
        catch (Exception) { status.Set("The clipboard is busy. Try again.", true, null); return false; }
    }

    void OpenIssue()
    {
        var fenced = "```\r\n" + text + "```\r\n";
        if (!Put(fenced)) return;
        var url = Issues + "?title=" + Uri.EscapeDataString(title()) + "&body=";
        bool whole = fenced.Length < 4000;
        try
        {
            Process.Start(url + Uri.EscapeDataString(whole ? fenced : "Paste the report from G-Lite here. It's on your clipboard.\r\n\r\n"));
            status.Set(whole ? "Opened a new issue with the report" : "Copied. Paste it into the new issue.", false, null);
        }
        catch (Exception ex) { status.Set("Couldn't open the browser: " + ex.Message, true, null); }
    }
}

class LogsPage : Control
{
    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] static extern int SetWindowTheme(IntPtr h, string app, string id);
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
    readonly RichTextBox box;
    readonly Label count;
    readonly StatusLine status;
    readonly Timer timer = new Timer { Interval = 1000 };
    System.Collections.Generic.List<Log.Line> shown = new System.Collections.Generic.List<Log.Line>();
    int version = -1;

    public LogsPage(MouseEventHandler drag)
    {
        Visible = false;
        Ui.Style(this, () => BackColor = Theme.Bg);
        MouseDown += drag;
        Ui.Text(this, "Logs", 20, 14, () => Theme.Text, 13.5f, Ui.Semi).MouseDown += drag;

        var body = Ui.Add(this, new Box(), 20, 56, 616, 555);
        Ui.Text(body, "log.txt, newest last", 14, 12, () => Theme.Dim, 9f, Ui.Semi);
        count = Ui.Text(body, "", 300, 12, () => Theme.Dim, 9f);
        count.AutoSize = false;
        count.Size = new Size(302, 18);
        count.TextAlign = ContentAlignment.TopRight;
        var inset = Ui.Add(body, new Control(), 14, 38, 588, 503);
        Ui.Style(inset, () => inset.BackColor = Theme.Bg);
        box = Ui.Add(inset, new RichTextBox { ReadOnly = true, WordWrap = false, ScrollBars = RichTextBoxScrollBars.Both, BorderStyle = BorderStyle.None, DetectUrls = false, AccessibleName = "Log" }, 8, 6, 576, 493);
        Ui.Font(box, 9f, "Consolas");
        Ui.Style(box, () => { box.BackColor = Theme.Bg; Dark(); if (Visible) Render(); });
        box.HandleCreated += (s, e) => Dark();

        var line = Ui.Add(this, new Control(), 0, 623, 656, 1);
        Ui.Style(line, () => line.BackColor = Theme.Border);
        status = Ui.Add(this, new StatusLine(), 20, 633, 280, 36);
        var clear = Ui.Add(this, new Btn { Text = "Clear" }, 314, 636, 76, 32);
        clear.Click += (s, e) => { Log.Clear(); Render(); status.Set("Log cleared", false, null); };
        var folder = Ui.Add(this, new Btn { Text = "Open folder" }, 398, 636, 130, 32);
        folder.Click += (s, e) => OpenFolder();
        var copy = Ui.Add(this, new Btn { Look = Look.Primary, Text = "Copy" }, 536, 636, 100, 32);
        copy.Click += (s, e) => Copy();
        timer.Tick += (s, e) => { if (Visible && Log.Version != version) Render(); };
    }

    public void Open()
    {
        status.Set("", false, null);
        Visible = true;
        timer.Start();
        Render();
    }

    public void Close()
    {
        Visible = false;
        timer.Stop();
    }

    void Dark()
    {
        if (box.IsHandleCreated) SetWindowTheme(box.Handle, Theme.Light ? "Explorer" : "DarkMode_Explorer", null);
    }

    void Render()
    {
        version = Log.Version;
        var lines = Log.Read();
        shown = Log.Fold(lines);
        count.Text = lines.Count + " lines · " + (Log.Size() / 1024.0).ToString("0.0", CultureInfo.InvariantCulture) + " KB";
        var rtf = new StringBuilder(@"{\rtf1\ansi\deff0{\fonttbl{\f0 Consolas;}}{\colortbl;" + Rgb(Theme.Text) + Rgb(Theme.Dim) + Rgb(Theme.Accent) + Rgb(Theme.Warn) + "}");
        foreach (var l in shown)
        {
            var t = Log.Format(l);
            int split = Math.Min(t.Length, l.Time.Length + 2 + 15);
            int c = l.Bad ? 4 : l.Kind == "you" ? 3 : l.Kind == "sent" ? 2 : 1;
            rtf.Append(@"\cf2 ").Append(Esc(t.Substring(0, split))).Append(@"\cf" + c + " ").Append(Esc(t.Substring(split))).Append(@"\par ");
        }
        if (shown.Count == 0) rtf.Append(@"\cf2 Nothing logged yet.");
        rtf.Append("}");
        box.Rtf = rtf.ToString();
        box.SelectAll();
        box.SelectionFont = box.Font;
        box.Select(box.TextLength, 0);
        box.ScrollToCaret();
        SendMessage(box.Handle, 0x114, (IntPtr)6, IntPtr.Zero);
    }

    static string Rgb(Color c) { return @"\red" + c.R + @"\green" + c.G + @"\blue" + c.B + ";"; }

    static string Esc(string s)
    {
        var o = new StringBuilder();
        foreach (var ch in s)
        {
            if (ch == '\\' || ch == '{' || ch == '}') o.Append('\\').Append(ch);
            else if (ch > 127) o.Append(@"\u").Append((int)(short)ch).Append('?');
            else o.Append(ch);
        }
        return o.ToString();
    }

    void Copy()
    {
        if (shown.Count == 0) { status.Set("Nothing to copy", false, null); return; }
        try
        {
            Clipboard.SetText(string.Join("\r\n", shown.Select(Log.Format)) + "\r\n");
            status.Set("Copied " + shown.Count + " lines", false, null);
        }
        catch (Exception) { status.Set("The clipboard is busy. Try again.", true, null); }
    }

    void OpenFolder()
    {
        try
        {
            if (File.Exists(Log.File)) Process.Start("explorer.exe", "/select,\"" + Log.File + "\"");
            else Process.Start(Path.GetDirectoryName(Log.File));
        }
        catch (Exception ex) { status.Set("Couldn't open the folder: " + ex.Message, true, null); }
    }
}
