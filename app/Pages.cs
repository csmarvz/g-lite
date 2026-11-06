using System;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
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
    readonly Btn newBtn, delBtn;
    readonly StatusLine status;
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
        LoadSets(null);
        ShowSub();
    }

    public Task Queue { get { return queue; } }

    protected bool Demo { get { return fake != null; } }

    protected Btn BackupsButton(string noun)
    {
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

    protected void Do(Action<Hidpp> a, Action done = null)
    {
        queue = queue.ContinueWith(_ =>
        {
            try
            {
                if (dev == null) dev = fake ?? new Hidpp(found.Path, found.Index);
                a(dev);
                if (done != null && IsHandleCreated) BeginInvoke(done);
            }
            catch (Exception ex)
            {
                if (dev != null && !(ex is HidppError) && !(ex is Refused)) { dev.Dispose(); dev = null; Lost(); }
                Post(ex.Message, true);
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
    readonly Btn again;

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
        text.Size = new Size(420, 54);
        again = Ui.Add(state, new Btn { Text = "Scan again", Icon = "refresh" }, 616 - 14 - 130, 36, 130, 32);
        again.Click += (s, e) => rescan();

        var details = Ui.Add(this, new Box(), 20, 196, 616, 196);
        Ui.Text(details, "Details", 14, 12, () => Theme.Dim, 9f, Ui.Semi);
        link = Row(details, "Connection", 40);
        battery = Row(details, "Battery", 68);
        firmware = Row(details, "Firmware", 96);
        features = Row(details, "Features", 124);
        features.AutoSize = false;
        features.Size = new Size(470, 60);
        SetFound(f);
    }

    static Label Row(Control box, string name, int y)
    {
        Ui.Text(box, name, 14, y, () => Theme.Dim, 9.75f);
        return Ui.Text(box, "", 120, y, () => Theme.Text, 9.75f);
    }

    public void SetFound(Found f)
    {
        title.Text = f.Short == "" ? "Logitech device" : f.Short;
        sub.Text = f.Sub;
        head.Text = f.Asleep ? "Asleep" : "Not supported yet";
        text.Text = f.Asleep
            ? "It's paired but didn't answer. Wake it with a key press or a click, and G-Lite picks it up."
            : "G-Lite found this device but can't change its settings yet.";
        again.Visible = f.Asleep;
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
