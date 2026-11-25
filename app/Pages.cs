using System;
using System.Collections.Generic;
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
    protected FoundDevice found;
    readonly Hidpp fake;
    Hidpp connection;
    // Whether the work running on the queue opened the connection, so Opened has just run.
    bool openedNow;
    // Set when a call failed and the connection was dropped, until a scan finds the device again (SetFound).
    volatile bool lost;
    Task queue = Task.FromResult(0);
    const string NoPreset = "–";
    readonly string prefix;
    readonly DeviceSubtitle subtitle;
    readonly Dropdown profiles;
    readonly TextField nameField;
    readonly FlatButton updateButton, moreButton, undoButton, reportButton;
    FlatButton headerBackupsButton;
    readonly Label heading;
    readonly StatusLine status;
    readonly FlatButton actionButton;
    Action actDo;
    readonly ReportView report;
    bool naming, touched, ready, queuedWork, restoring = true;
    string baseline, renaming, loaded, remembered;
    readonly Dictionary<string, string[]> drafts = new Dictionary<string, string[]>();
    public string Note;
    // A control a screen puts in the footer, left of Revert (the keyboard's M1-M3).
    protected Control FooterPicker;
    // Whether the screen shows FooterPicker: its Visible reads false while the window is hidden in the tray.
    protected bool FooterPickerShown;
    // A screen laid out in rows (UseBody) puts its settings on Body.Sheet, below the header. Body is as tall as they are and the
    // footer follows it; when they don't fit the window, Body scrolls and the footer stays at the bottom.
    protected ScrollPanel Body;
    readonly List<Control> footer = new List<Control>();
    // Design units: Body starts under the preset row; the footer's status line sits 10 below Body, the footer 57 high in all.
    protected const int BodyTop = 118;
    const int FooterGap = 10, FooterHeight = 730 - 673;

    protected DevicePage(FoundDevice device, Hidpp fake, string title, string noun, string prefix)
    {
        found = device;
        this.fake = fake;
        this.prefix = prefix;
        Size = Ui.PageSize;
        Ui.Style(this, () => BackColor = Theme.Background);
        heading = Ui.Text(this, title, 20, 14, () => Theme.Text, 13.5f, Ui.Semibold);
        heading.SizeChanged += (s, e) => PlaceHeaderButtons();
        subtitle = Ui.Add(this, new DeviceSubtitle(), 21, 42, 540, 20);
        reportButton = Ui.Add(this, new FlatButton { Style = ButtonStyle.Ghost, Text = "Report", AccessibleName = "Report of the " + noun },
            20, 10, 80, 28);

        Ui.Text(this, "G-Lite preset", 20, 79, () => Theme.Dim, 9.75f);
        profiles = Ui.Add(this, new Dropdown { AccessibleName = "G-Lite preset" }, 108, 72, 160, 32);
        profiles.Picked += PickSet;
        profiles.Label = name => name + (drafts.ContainsKey(name) || (name == loaded && Changed()) ? " •" : "");
        nameField = Ui.Add(this, new TextField { Visible = false }, 108, 72, 160, 32);
        nameField.Box.KeyDown += (s, e) =>
        {
            if (e.KeyCode == Keys.Enter) { SaveSet(nameField.Box.Text.Trim()); e.SuppressKeyPress = true; }
            if (e.KeyCode == Keys.Escape) { EndName(); e.SuppressKeyPress = true; }
        };
        updateButton = Ui.Add(this, new FlatButton { Text = "Update" }, 276, 72, 76, 32);
        updateButton.Click += (s, e) =>
        {
            if (naming) SaveSet(nameField.Box.Text.Trim());
            else if (profiles.Index <= 0) StartName(null);
            else UpdateSet();
        };
        moreButton = Ui.Add(this, new FlatButton { Text = "…", AccessibleName = "More preset actions" }, 360, 72, 36, 32);
        moreButton.Click += (s, e) => { if (naming) EndName(); else PresetMenu(); };

        status = Ui.Add(this, new StatusLine(), 20, 683, 460, 36);
        undoButton = Ui.Add(this, new FlatButton { Text = "Revert", Enabled = false, AccessibleName = "Revert changes" }, 416, 685, 72, 32);
        undoButton.Click += (s, e) => Undo();
        actionButton = Ui.Add(this, new FlatButton { Visible = false }, 328, 685, 160, 32);
        actionButton.TabIndex = 1000;
        actionButton.Click += (s, e) => { if (actDo != null) actDo(); };
        report = Ui.Add(this, new ReportView(noun, () => ReportView.TitleFor(found), Collect), 0, 72, 656, 658);
        report.Anchor = Ui.Tall;
        report.Closed += () => { reportButton.On = false; reportButton.Invalidate(); reportButton.Focus(); };
        reportButton.Click += (s, e) =>
        {
            if (report.Visible) { report.Close(); return; }
            reportButton.On = true;
            reportButton.Invalidate();
            report.Open();
        };
        LoadSets(null);
        ShowSub();
    }

    public Task Queue { get { return queue; } }

    protected bool Demo { get { return fake != null; } }

    protected FlatButton BackupsButton(string noun)
    {
        headerBackupsButton = Ui.Add(this,
            new FlatButton { Style = ButtonStyle.Ghost, Text = "Backups", AccessibleName = "Backups of the " + noun }, 104, 10, 80, 28);
        PlaceHeaderButtons();
        return headerBackupsButton;
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        PlaceHeaderButtons();
        PlaceFooter();
    }

    // top: where Body starts, in design units, lower when the screen keeps something above it (the keyboard's picture).
    protected ScrollPanel UseBody(int top = BodyTop)
    {
        Body = Ui.Add(this, new ScrollPanel(), 0, top, Ui.PageSize.Width, Ui.PageSize.Height - FooterHeight - top);
        Ui.Style(Body, () => Body.BackColor = Theme.Background);
        footer.AddRange(new Control[] { status, undoButton, actionButton });
        return Body;
    }

    // A screen's own control in the footer row, like Save, made at the footer's design height (the status line at 683): it joins the
    // footer where it is now.
    protected void InFooter(Control control)
    {
        control.Top += status.Top - 683;
        footer.Add(control);
    }

    // How tall the rows are, in design units.
    protected void FitBody(int height)
    {
        Body.Fit(Scaled(height));
        PlaceFooter();
    }

    // Design units in the page's pixels: the page is built at 100% and scaled as a whole once placed, at its width's ratio.
    protected int Scaled(int units) { return (int)Math.Round(units * Width / (float)Ui.PageSize.Width); }

    void PlaceFooter()
    {
        if (Body == null) return;
        int bottom = Height - Scaled(FooterHeight);
        Body.Height = Math.Max(0, Math.Min(Body.Content, bottom - Body.Top));
        int top = Body.Bottom + Scaled(FooterGap);
        int shift = top - status.Top;
        if (shift != 0) foreach (var control in footer) control.Top += shift;
    }

    // Report and Backups follow the device's name, as far as the page's width lets them.
    void PlaceHeaderButtons()
    {
        if (reportButton == null) return;
        int gap = (int)Math.Round(4 * Ui.DpiScale);
        int width = reportButton.Width + (headerBackupsButton != null ? gap + headerBackupsButton.Width : 0);
        reportButton.Left = Math.Min(heading.Right + 3 * gap, Width - 5 * gap - width);
        if (headerBackupsButton != null) headerBackupsButton.Left = reportButton.Right + gap;
    }

    public static string When(Backups.Entry entry)
    {
        return entry.When.ToLocalTime().ToString("d MMM HH:mm", CultureInfo.InvariantCulture);
    }

    protected void BackupMenu(FlatButton anchor, string device, string factory, Action restoreFactory,
        Func<Backups.Entry, string> describe, Func<Backups.Entry, bool> good, Action<Backups.Entry> restore)
    {
        var menu = new ContextMenuStrip { Renderer = Ui.Menus, Font = Ui.Get(9.75f, Ui.Regular), ShowImageMargin = false };
        menu.BackColor = Theme.Card;
        menu.ForeColor = Theme.Text;
        if (factory != null)
        {
            menu.Items.Add(new ToolStripMenuItem(factory, null, (s, e) => restoreFactory()));
            menu.Items.Add(new ToolStripSeparator());
        }
        var list = Backups.List(device);
        menu.Items.Add(new ToolStripMenuItem(list.Count == 0 ? "No backups yet. G-Lite makes one before each save." : "Restore a backup")
            { Enabled = false });
        foreach (var backup in list)
        {
            var entry = backup;
            var item = new ToolStripMenuItem(When(backup) + ", " + describe(backup) + (backup.First ? ", first backup" : ""), null,
                (s, e) => restore(entry));
            item.Enabled = good(backup);
            menu.Items.Add(item);
        }
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Open the backups folder", null, (s, e) =>
        {
            var folder = Backups.Folder(device);
            Directory.CreateDirectory(folder);
            Process.Start(folder);
        }));
        menu.Closed += (s, e) => BeginInvoke((Action)menu.Dispose);
        if (Ui.Popped != null) { Ui.Popped(menu); return; }
        menu.Show(anchor, new Point(anchor.Width, anchor.Height + (int)(2 * Ui.DpiScale)), ToolStripDropDownDirection.BelowLeft);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) queue = queue.ContinueWith(_ => { if (connection != null) connection.Dispose(); connection = null; });
        base.Dispose(disposing);
    }

    public virtual void Shown() { Load(); }

    internal virtual bool Busy { get { return false; } }

    public static bool Manual;

    public virtual void Hidden() { }

    public virtual void Restyle() { }

    public abstract void Load();

    protected abstract string Current();

    protected abstract void ApplyPreset(string text);

    protected abstract void Revert();

    protected virtual void ClearStatus() { ShowStatus(""); }

    protected virtual bool Unsaved { get { return false; } }

    // Changes a preset doesn't hold (the G815's G keys): with a preset picked, Revert is on for them too and brings them back.
    protected virtual bool UnsavedBesidesPreset { get { return false; } }

    protected virtual void RevertBesidesPreset() { }

    protected virtual bool Keepable { get { return true; } }

    protected void Edited() { touched = true; ShowSets(); }

    protected virtual void Lost() { }

    // Runs on the device's queue each time a connection is opened, the first one and after one was lost: for what the device forgets
    // when it's unplugged. ReopensWhenFound makes a scan that finds the device again open one at once, not at the next call.
    protected virtual void Opened(Hidpp hidpp) { }
    protected virtual bool ReopensWhenFound { get { return false; } }

    public void SetFound(FoundDevice device)
    {
        found = device;
        ShowSub();
        if (lost && ReopensWhenFound) RunOnDevice(hidpp => { });
    }

    // The receiver says the device connected again (0x41), which a scan misses when it was off only for a moment. A device just
    // connected may not answer yet: the failure stays off the status line, since the lost connection reopens at the next scan.
    public void Reconnected()
    {
        if (!ReopensWhenFound) return;
        Log.Add(DeviceLabel, "found", "connected again");
        RunOnDevice(hidpp => { if (!openedNow) Opened(hidpp); }, null, message => { });
    }

    void ShowSub()
    {
        subtitle.Set(found.Subtitle, found.Level, found.BatteryText, "");
    }

    static int ActionWidth(string action, bool primary)
    {
        return TextRenderer.MeasureText(action, Ui.Get(9.75f, primary ? Ui.Semibold : Ui.Regular)).Width + (int)(28 * Ui.DpiScale);
    }

    public void ShowStatus(string text, bool warn = false, Action click = null, string action = null)
    {
        ShowSets();
        int footerLeft = FooterPicker != null && FooterPickerShown ? FooterPicker.Left : undoButton.Left;
        // With too little room left for the text beside a button, the text itself takes the click and names the action.
        if (action != null && footerLeft - status.Left - ActionWidth(action, true) < (int)(220 * Ui.DpiScale))
        {
            text = text + " " + action + ".";
            action = null;
        }
        actDo = action != null ? click : null;
        actionButton.Visible = action != null;
        status.Set(text, warn, action != null ? null : click);
        if (action == null) { status.Width = footerLeft - status.Left - (int)(12 * Ui.DpiScale); return; }
        var main = MainButton();
        actionButton.Style = main == null || !main.Enabled ? ButtonStyle.Primary : ButtonStyle.Normal;
        actionButton.Text = action;
        int width = ActionWidth(action, actionButton.Style == ButtonStyle.Primary);
        int right = footerLeft - (int)(8 * Ui.DpiScale);
        actionButton.Bounds = new Rectangle(right - width, actionButton.Top, width, actionButton.Height);
        status.Width = actionButton.Left - status.Left - (int)(12 * Ui.DpiScale);
    }

    protected void ShowError(string text)
    {
        ShowStatus(text, true);
        status.Error = true;
    }

    protected void ShowStatusLater(string text, bool warn) { if (IsHandleCreated) BeginInvoke((Action)(() => ShowStatus(text, warn))); }

    protected static void SetDimmed(Label label, bool on) { label.ForeColor = on ? Theme.Dim : Ui.Off; }

    protected static Label Value(Control parent, int x, int y, int width)
    {
        var label = Ui.Text(parent, "", x, y, () => Theme.Dim, 9f);
        label.AutoSize = false;
        label.Size = new Size(width, 18);
        label.TextAlign = ContentAlignment.TopRight;
        return label;
    }

    void Collect(Action<string, string> done)
    {
        if (found.PlainHid)
        {
            var device = found;
            var screen = GetType().Name;
            Task.Run(() =>
            {
                string plain = null, error = null;
                try { plain = Report.PlainHidReport(device, screen); }
                catch (Exception ex) { error = ex.Message; }
                if (IsHandleCreated) BeginInvoke((Action)(() => done(plain, error)));
            });
            return;
        }
        string text = null;
        RunOnDevice(
            hidpp => text = found.Hidpp10 ? Report.Registers(hidpp, found, GetType().Name) : Report.Build(hidpp, found, GetType().Name),
            () => done(text, null),
            message => done(null, message));
    }

    protected string DeviceLabel { get { return found.ShortName == "" ? found.Name : found.ShortName; } }

    protected void LogUserAction(string what) { Log.Add(DeviceLabel, "you", what); }

    // Whether the screen has something to send to its device after a scan: its preset, by default.
    protected virtual bool SendsAgain { get { return Settings.Get(PresetKey) != ""; } }

    // A screen that didn't read its device when made, and isn't shown, reads it now, so what it keeps is sent again without opening it.
    public void LoadToSendAgain()
    {
        if (queuedWork || !SendsAgain) return;
        // Started in the tray, a hidden screen has no handle yet, and Do's callbacks need one.
        if (!IsHandleCreated) CreateHandle();
        Load();
    }

    protected void RunOnDevice(Action<Hidpp> work, Action done = null, Action<string> failed = null)
    {
        queuedWork = true;
        queue = queue.ContinueWith(_ =>
        {
            try
            {
                openedNow = connection == null;
                if (connection == null)
                {
                    connection = fake ?? new Hidpp(found.Path, found.Index, found.ShortPath);
                    connection.DeviceLabel = DeviceLabel;
                    lost = false;
                    Opened(connection);
                }
                work(connection);
                if (done != null && IsHandleCreated) BeginInvoke(done);
            }
            catch (Exception ex)
            {
                Log.Add(DeviceLabel, "error", ex.Message);
                if (connection != null && !(ex is HidppError) && !(ex is RefusedException))
                {
                    connection.Dispose();
                    connection = null;
                    lost = true;
                    Lost();
                }
                if (failed == null) ShowStatusLater(ex.Message, true);
                else if (IsHandleCreated) BeginInvoke((Action)(() => failed(ex.Message)));
            }
        });
    }

    string PresetKey { get { return "preset." + found.SettingsKey; } }

    // The picked preset's name, with "=edited" (a name can't hold "=") once the settings moved away from it:
    // then the next start keeps what the device or lighting.<device> has and shows the dot, instead of sending the preset over it.
    void Remember()
    {
        if (restoring) return;
        var value = loaded == null ? "" : loaded + (Changed() ? "=edited" : "");
        if (value == remembered) return;
        remembered = value;
        if (value == "") Settings.Remove(PresetKey); else Settings.Set(PresetKey, value);
    }

    protected virtual bool Damaged { get { return false; } }

    // Once per run, after the first load: a device that saves (G515, mice) comes back on its onboard lighting, not the unsaved preview.
    void RestorePreset()
    {
        if (!restoring) return;
        restoring = false;
        remembered = Settings.Get(PresetKey);
        var parts = remembered.Split('=');
        var name = parts[0];
        bool edited = parts.Length > 1;
        int index = name == "" ? -1 : profiles.Items.IndexOf(name);
        if (index <= 0) { Remember(); return; }
        var text = Settings.Get(prefix + name);
        if (!edited && !Damaged && Current() != text)
        {
            try { ApplyPreset(text); }
            catch (Exception) { ShowStatus("The preset " + name + " couldn't be read from settings.txt", true); Remember(); return; }
        }
        profiles.Index = index;
        loaded = name;
        baseline = edited ? text : Current();
        touched = edited;
        ShowSets();
    }

    protected void Loaded()
    {
        ready = true;
        ShowSets();
        ShowSub();
        ClearStatus();
        RestorePreset();
        if (Note == null || !Visible) return;
        var note = Note;
        Note = null;
        Briefly(note);
    }

    void Briefly(string text)
    {
        ShowStatus(text);
        var timer = new Timer { Interval = 8000 };
        timer.Tick += (s, e) => { timer.Dispose(); if (status.Shown == text) ClearStatus(); };
        timer.Start();
    }

    void LoadSets(string select)
    {
        profiles.Items.Clear();
        profiles.Items.Add(NoPreset);
        var presetNames = Settings.Keys()
            .Where(key => key.StartsWith(prefix))
            .Select(key => key.Substring(prefix.Length))
            .OrderBy(key => key);
        foreach (var presetName in presetNames) profiles.Items.Add(presetName);
        profiles.Index = select == null ? 0 : Math.Max(0, profiles.Items.IndexOf(select));
        ShowSets();
    }

    FlatButton MainButton()
    {
        return Controls.OfType<FlatButton>().FirstOrDefault(button => button != actionButton && button != undoButton
            && button.Style == ButtonStyle.Primary && button.Visible && button.Top >= actionButton.Top - 4);
    }

    void ShowSets()
    {
        profiles.Visible = !naming;
        nameField.Visible = naming;
        bool picked = profiles.Index > 0;
        updateButton.Text = naming ? "Save" : picked ? "Update" : "New…";
        moreButton.Text = naming ? "Cancel" : "…";
        bool changed = picked && Changed();
        updateButton.Enabled = (naming || (picked ? changed : ready)) && Keepable;
        if (picked) profiles.Text = profiles.Items[profiles.Index] + (changed ? " •" : "");
        profiles.Invalidate();
        undoButton.Enabled = picked ? changed || UnsavedBesidesPreset : Unsaved;
        Remember();
        if (!IsHandleCreated || !Visible) return;
        moreButton.Width = (int)((naming ? 76 : 36) * Ui.DpiScale);
        var main = MainButton();
        undoButton.Left = (main != null ? main.Left - (int)(8 * Ui.DpiScale) : (int)(636 * Ui.DpiScale)) - undoButton.Width;
        if (FooterPicker != null)
            FooterPicker.Bounds = new Rectangle(undoButton.Left - (int)(8 * Ui.DpiScale) - FooterPicker.Width, undoButton.Top,
                FooterPicker.Width, undoButton.Height);
    }

    bool Changed() { return touched && baseline != null && Current() != baseline; }

    void Undo()
    {
        if (profiles.Index <= 0 || baseline == null) Revert();
        else
        {
            if (Changed())
                try { ApplyPreset(baseline); }
                catch (Exception) { ShowStatus("This preset couldn't be read from settings.txt", true); }
            if (UnsavedBesidesPreset) RevertBesidesPreset();
        }
        touched = false;
        ShowSets();
    }

    void PresetMenu()
    {
        bool picked = profiles.Index > 0;
        var name = picked ? profiles.Items[profiles.Index] : null;
        var menu = new ContextMenuStrip { Renderer = Ui.Menus, Font = Ui.Get(9.75f, Ui.Regular), ShowImageMargin = false };
        menu.BackColor = Theme.Card;
        menu.ForeColor = Theme.Text;
        menu.Items.Add(new ToolStripMenuItem("Save as new preset…", null, (s, e) => StartName(null)));
        menu.Items.Add(new ToolStripMenuItem("Rename…", null, (s, e) => StartName(name)) { Enabled = picked });
        menu.Items.Add(new ToolStripMenuItem("Delete", null, (s, e) => DeleteSet(name)) { Enabled = picked });
        menu.Closed += (s, e) => BeginInvoke((Action)menu.Dispose);
        if (Ui.Popped != null) { Ui.Popped(menu); return; }
        menu.Show(moreButton, new Point(moreButton.Width, moreButton.Height + (int)(2 * Ui.DpiScale)),
            ToolStripDropDownDirection.BelowLeft);
    }

    void StartName(string old)
    {
        naming = true;
        renaming = old;
        nameField.Box.Text = old ?? "";
        ShowSets();
        nameField.Box.Focus();
        nameField.Box.SelectAll();
    }

    void EndName()
    {
        naming = false;
        renaming = null;
        ShowSets();
    }

    void PickSet(int i)
    {
        if (loaded != null)
        {
            if (Changed()) drafts[loaded] = new[] { baseline, Current() };
            else drafts.Remove(loaded);
        }
        baseline = null;
        loaded = null;
        touched = false;
        if (i == 0) Revert();
        else
        {
            var name = profiles.Items[i];
            try
            {
                string[] draft;
                if (drafts.TryGetValue(name, out draft)) { ApplyPreset(draft[1]); baseline = draft[0]; drafts.Remove(name); }
                else { ApplyPreset(Settings.Get(prefix + name)); baseline = Current(); }
                loaded = name;
                touched = draft != null;
            }
            catch (Exception) { ShowStatus("This preset couldn't be read from settings.txt", true); }
        }
        restoring = false;
        ShowSets();
    }

    // The device switched to another onboard profile (an M key): the preset no longer describes what runs, so it goes back to –
    // without sending anything. Changes not saved to it stay as a draft, as when another preset is picked.
    protected void DropPreset()
    {
        if (loaded != null)
        {
            if (Changed()) drafts[loaded] = new[] { baseline, Current() };
            else drafts.Remove(loaded);
        }
        baseline = null;
        loaded = null;
        touched = false;
        restoring = false;
        profiles.Index = 0;
        ShowSets();
    }

    void UpdateSet()
    {
        if (profiles.Index <= 0) return;
        var name = profiles.Items[profiles.Index];
        baseline = Current();
        touched = false;
        Settings.Set(prefix + name, baseline);
        ShowSets();
        Briefly("Updated the preset " + name);
    }

    void DeleteSet(string name)
    {
        if (name == null) return;
        Settings.Remove(prefix + name);
        drafts.Remove(name);
        baseline = null;
        loaded = null;
        LoadSets(null);
        Revert();
        ShowSets();
        Briefly("Deleted the preset " + name);
    }

    void SaveSet(string name)
    {
        if (name == "") { ShowStatus("Type a name for the preset first", true); nameField.Box.Focus(); return; }
        if (name.Contains("=")) { ShowStatus("A preset name can't contain =", true); nameField.Box.Focus(); return; }
        if (name == renaming) { EndName(); return; }
        if (profiles.Items.Any(item => string.Equals(item, name, StringComparison.OrdinalIgnoreCase)))
        {
            ShowStatus("There's already a preset called " + name + ". Pick it and press Update to change it.", true);
            nameField.Box.Focus();
            return;
        }
        if (renaming != null)
        {
            var old = renaming;
            Settings.Set(prefix + name, Settings.Get(prefix + old));
            Settings.Remove(prefix + old);
            if (loaded == old) loaded = name;
            EndName();
            LoadSets(name);
            Briefly("Renamed the preset to " + name);
            return;
        }
        baseline = Current();
        touched = false;
        loaded = name;
        restoring = false;
        Settings.Set(prefix + name, baseline);
        EndName();
        LoadSets(name);
        Briefly("Saved the preset " + name);
    }
}

class ColorRow
{
    public static readonly int[] Swatches = { 0xFFFFFF, 0xFF2020, 0xFF8000, 0xFFD000, 0x30D050, 0x3080FF };
    // A swatch's size, and the row's width once a custom colour is kept (the swatches, yours and +), 36 apart.
    public const int Size = 34;
    public static readonly int Widest = (Swatches.Length + 2) * 36 - 2;
    readonly Swatch[] swatches = new Swatch[Swatches.Length];
    readonly Swatch custom, more;
    readonly Control parent;
    // In a row of Settings' layout, + stays at the right edge and the swatches move instead.
    readonly bool rightAligned;
    int lastCustom = -1, color;
    public event Action<int> Picked;

    public ColorRow(Control parent, int y) : this(parent, 10, y, false) { }

    // x is where the row starts at its widest; a right-aligned row ends at x + Widest.
    public ColorRow(Control parent, int x, int y, bool alignRight)
    {
        this.parent = parent;
        rightAligned = alignRight;
        for (int i = 0; i < Swatches.Length; i++)
        {
            int color = Swatches[i];
            swatches[i] = Ui.Add(parent,
                new Swatch { Fill = Color.FromArgb(color | unchecked((int)0xFF000000)), AccessibleName = "Color " + color.ToString("X6") },
                x + i * 36, y, Size, Size);
            swatches[i].Click += (s, e) => Pick(color);
        }
        custom = Ui.Add(parent, new Swatch { AccessibleName = "Your color" }, x + Swatches.Length * 36, y, Size, Size);
        custom.Click += (s, e) => { if (lastCustom >= 0) Pick(lastCustom); };
        more = Ui.Add(parent, new Swatch { AccessibleName = "Pick a color" }, x + (Swatches.Length + (alignRight ? 1 : 0)) * 36, y,
            Size, Size);
        more.Click += (s, e) => Choose();
    }

    void Pick(int color) { if (Picked != null) Picked(color); }

    void Choose()
    {
        var form = parent.FindForm();
        using (var dialog = new ColorDialog { FullOpen = true, Color = Color.FromArgb(color | unchecked((int)0xFF000000)) })
            if (dialog.ShowDialog(form) == DialogResult.OK) Pick(dialog.Color.ToArgb() & 0xFFFFFF);
        form.Activate();
    }

    // A row that shows a colour it can't change (a read-only mouse) passes editable false: the colour stays marked.
    public void RefreshControls(int newColor, bool on, bool editable = true)
    {
        color = newColor;
        bool isCustom = !Swatches.Contains(newColor);
        if (isCustom) lastCustom = newColor;
        for (int i = 0; i < swatches.Length; i++)
        {
            swatches[i].Enabled = on && editable;
            swatches[i].On = on && Swatches[i] == newColor;
            swatches[i].Invalidate();
        }
        // Kept in a local: Visible reads false while the page is hidden.
        bool customShown = lastCustom >= 0;
        custom.Visible = customShown;
        custom.Fill = Color.FromArgb(lastCustom | unchecked((int)0xFF000000));
        custom.Enabled = on && editable;
        custom.On = on && isCustom;
        custom.Invalidate();
        // From the swatch's own size, as the page may not be scaled yet (RefreshControls from a constructor), where Ui.DpiScale already is.
        int step = (int)Math.Round(custom.Width * 36f / Size);
        if (rightAligned)
            for (int i = 0; i < swatches.Length; i++) swatches[i].Left = custom.Left - (swatches.Length - i - (customShown ? 0 : 1)) * step;
        else more.Left = custom.Left + (customShown ? step : 0);
        more.Enabled = on && editable;
    }

    // The right edge of the last swatch (+), which moves right once a custom colour is kept.
    public int Right { get { return more.Right; } }
}

class InfoPage : Control
{
    readonly DeviceSubtitle subtitle;
    readonly Label title, head, text, link, battery, firmware, features, mediaLabel, mediaNote;
    readonly FlatButton again, create, reportButton;
    readonly Toggle media;
    readonly ReportView report;
    FoundDevice found, applied;

    string MediaKey { get { return "fnswap." + found.SettingsKey; } }

    public InfoPage(FoundDevice device, Action rescan)
    {
        Size = Ui.PageSize;
        Ui.Style(this, () => BackColor = Theme.Background);
        title = Ui.Text(this, "", 20, 14, () => Theme.Text, 13.5f, Ui.Semibold);
        title.SizeChanged += (s, e) => PlaceReportButton();
        subtitle = Ui.Add(this, new DeviceSubtitle(), 21, 42, 540, 20);

        var state = Ui.Add(this, new RoundedBox(), 20, 80, 616, 104);
        head = Ui.Text(state, "", 14, 12, () => Theme.Dim, 9f, Ui.Semibold);
        text = Ui.Text(state, "", 14, 38, () => Theme.Text, 9.75f);
        text.AutoSize = false;
        text.Size = new Size(440, 60);
        again = Ui.Add(state, new FlatButton { Text = "Scan again", Icon = "refresh" }, 616 - 14 - 130, 36, 130, 32);
        again.Click += (s, e) => rescan();
        create = Ui.Add(state, new FlatButton { Style = ButtonStyle.Primary, Text = "Create report" }, 616 - 14 - 130, 36, 130, 32);
        media = Ui.Add(state, new Toggle { AccessibleName = "Use F1–F12 as media keys" }, 14, 42, 34, 18);
        media.Changed += () => SendMedia(media.Checked, true);
        mediaLabel = Ui.Text(state, "Use F1–F12 as media keys", 60, 40, () => Theme.Text, 9.75f);
        media.Follow(mediaLabel);
        mediaNote = Ui.Text(state, "When on, F1–F12 send media actions, and can't be typed.", 60, 62, () => Theme.Dim, 9f);

        var details = Ui.Add(this, new RoundedBox(), 20, 196, 616, 196);
        Ui.Text(details, "Details", 14, 12, () => Theme.Dim, 9f, Ui.Semibold);
        link = Row(details, "Connection", 40);
        battery = Row(details, "Battery", 68);
        firmware = Row(details, "Firmware", 96);
        features = Row(details, "Supports", 124);
        features.AutoSize = false;
        features.Size = new Size(470, 60);
        reportButton = Ui.Add(this, new FlatButton { Style = ButtonStyle.Ghost, Text = "Report", AccessibleName = "Report of this device" },
            20, 10, 80, 28);
        report = Ui.Add(this, new ReportView("device", () => ReportView.TitleFor(found), Collect), 0, 72, 656, 658);
        report.Anchor = Ui.Tall;
        report.Closed += () => { reportButton.On = false; reportButton.Invalidate(); reportButton.Focus(); };
        reportButton.Click += (s, e) => { if (report.Visible) report.Close(); else Open(); };
        create.Click += (s, e) => Open();
        SetFound(device);
    }

    void Open()
    {
        reportButton.On = true;
        reportButton.Invalidate();
        report.Open();
    }

    void Collect(Action<string, string> done)
    {
        var device = found;
        Task.Run(() =>
        {
            string text = null, error = null;
            try
            {
                if (device.PlainHid) text = Report.PlainHidReport(device, "none, not supported yet");
                else using (var hidpp = new Hidpp(device.Path, device.Index, device.ShortPath))
                    text = device.Hidpp10
                        ? Report.Registers(hidpp, device, "none, not supported yet")
                        : Report.Build(hidpp, device, "none, not supported yet");
            }
            catch (Exception ex) { error = ex.Message; }
            if (IsHandleCreated) BeginInvoke((Action)(() => done(text, error)));
        });
    }

    void SendMedia(bool on, bool chosen)
    {
        var device = found;
        if (chosen) Settings.Set(MediaKey, on ? "1" : "0");
        Task.Run(() =>
        {
            try
            {
                using (var hidpp = new Hidpp(device.Path, device.Index, device.ShortPath))
                    hidpp.Call(hidpp.FeatureIndex(0x40A0), 1, (byte)(on ? 1 : 0));
            }
            catch (Exception) { }
        });
    }

    static Label Row(Control box, string name, int y)
    {
        Ui.Text(box, name, 14, y, () => Theme.Dim, 9.75f);
        return Ui.Text(box, "", 120, y, () => Theme.Text, 9.75f);
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        PlaceReportButton();
    }

    // Report follows the device's name, as far as the page's width lets it.
    void PlaceReportButton()
    {
        if (reportButton == null) return;
        reportButton.Left = Math.Min(title.Right + (int)Math.Round(12 * Ui.DpiScale), Width - (int)Math.Round(20 * Ui.DpiScale) - reportButton.Width);
    }

    public void SetFound(FoundDevice device)
    {
        found = device;
        title.Text = device.ShortName == "" ? "Logitech device" : device.ShortName;
        subtitle.Set(device.Subtitle, device.Level, device.BatteryText, "");
        bool keys = device.MediaKeys && !device.Asleep;
        head.Text = device.Asleep ? "Asleep" : keys ? "Keys" : "Not supported yet";
        text.Visible = !keys;
        media.Visible = mediaLabel.Visible = mediaNote.Visible = keys;
        media.Checked = keys && Settings.Get(MediaKey) == "1";
        if (keys && media.Checked && applied != device) SendMedia(true, false);
        if (keys) applied = device;
        text.Text = device.Asleep
            ? "It's paired but didn't answer. Wake it with a key press or a click, and G-Lite picks it up."
            : "G-Lite can't change this device yet. A report of what it supports helps add it. G-Lite only reads the device, and nothing is sent until you paste the report somewhere.";
        again.Visible = device.Asleep;
        reportButton.Visible = !device.Asleep;
        create.Visible = !device.Asleep && !keys;
        if (device.Asleep && report.Visible) report.Close();
        link.Text = device.Link + device.Slot + (device.WirelessId != 0 ? ", ID " + device.WirelessId.ToString("X4") : "");
        battery.Text = device.Battery < 0 ? "–"
            : device.BatteryWords + (device.Charging ? ", charging" : "")
                + (device.BatteryFromVoltage ? " (estimated from the voltage)" : "");
        firmware.Text = device.Firmware == "" ? "–" : device.Firmware;
        var known = device.Features.Where(x => x >= 0x0100 && x != 0x1D4B && (x < 0x1000 || x > 0x1004) && Report.Name(x) != "").ToList();
        var named = known.Select(x => Report.Name(x)).Select(name => char.ToUpper(name[0]) + name.Substring(1)).Distinct().ToList();
        int rest = device.Features.Length - known.Count;
        features.Text = device.Features.Length == 0 ? "–"
            : string.Join(", ", named) + (rest > 0 ? (named.Count > 0 ? ", and " : "") + rest + " more in the report" : "");
    }
}

class EmptyPage : Control
{
    readonly Label title, text, tips;
    readonly FlatButton scan, report;

    public EmptyPage(Action rescan)
    {
        Size = Ui.PageSize;
        Ui.Style(this, () => BackColor = Theme.Background);
        title = Centered(Ui.Text(this, "", 0, 150, () => Theme.Text, 15f, Ui.Semibold), 36);
        text = Centered(Ui.Text(this, "", 0, 192, () => Theme.Dim, 9.75f), 44);
        scan = Ui.Add(this, new FlatButton { Style = ButtonStyle.Primary, Text = "Scan again" }, 328 - 8 - 150, 252, 150, 32);
        scan.Click += (s, e) => rescan();
        report = Ui.Add(this, new FlatButton { Text = "Report your device" }, 328 + 8, 252, 150, 32);
        report.Click += (s, e) => Process.Start("https://github.com/csmarvz/g-lite/issues/new");
        tips = Centered(Ui.Text(this,
            "Turn the device on and wake it with a key press or a click.\nQuit G HUB, which can hold on to the device.\nPair Bluetooth devices in Windows first.",
            0, 320, () => Theme.Dim, 9f), 64);
        Searching(true);
    }

    Label Centered(Label label, int height)
    {
        label.AutoSize = false;
        label.TextAlign = ContentAlignment.TopCenter;
        label.Bounds = new Rectangle(56, label.Top, 656 - 112, height);
        return label;
    }

    public void Searching(bool on)
    {
        title.Text = on ? "Looking for devices…" : "No supported device found";
        text.Text = on ? "" : "G-Lite looked over USB, LIGHTSPEED, Unifying and Bolt receivers, and Bluetooth.";
        scan.Visible = report.Visible = tips.Visible = !on;
    }
}
