using System;
using System.Collections.Generic;
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
    protected FoundDevice found;
    readonly Hidpp fake;
    Hidpp connection;
    Task queue = Task.FromResult(0);
    const string NoPreset = "None";
    readonly string prefix;
    readonly DeviceSubtitle subtitle;
    readonly Dropdown profiles;
    readonly TextField nameField;
    readonly FlatButton updateButton, moreButton, undoButton, reportButton;
    readonly StatusLine status;
    readonly FlatButton actionButton;
    Action actDo;
    readonly ReportView report;
    bool naming, touched, ready, queuedWork, restoring = true;
    string baseline, renaming, loaded, remembered;
    readonly Dictionary<string, string[]> drafts = new Dictionary<string, string[]>();
    public string Note;

    protected DevicePage(FoundDevice device, Hidpp fake, MouseEventHandler drag, string title, string noun, string prefix)
    {
        found = device;
        this.fake = fake;
        this.prefix = prefix;
        Ui.Style(this, () => BackColor = Theme.Background);
        MouseDown += drag;
        Ui.Text(this, title, 20, 14, () => Theme.Text, 13.5f, Ui.Semibold).MouseDown += drag;
        subtitle = Ui.Add(this, new DeviceSubtitle(), 21, 42, 540, 20);
        subtitle.MouseDown += drag;
        reportButton = Ui.Add(this, new FlatButton { Style = ButtonStyle.Ghost, Text = "Report", AccessibleName = "Report of the " + noun },
            572 - 80, 10, 80, 28);

        Ui.Text(this, "Preset", 20, 79, () => Theme.Dim, 9.75f);
        profiles = Ui.Add(this, new Dropdown { AccessibleName = "Preset" }, 74, 72, 180, 32);
        profiles.Picked += PickSet;
        profiles.Label = name => name + (drafts.ContainsKey(name) || (name == loaded && Changed()) ? " •" : "");
        nameField = Ui.Add(this, new TextField { Visible = false }, 74, 72, 180, 32);
        nameField.Box.KeyDown += (s, e) =>
        {
            if (e.KeyCode == Keys.Enter) { SaveSet(nameField.Box.Text.Trim()); e.SuppressKeyPress = true; }
            if (e.KeyCode == Keys.Escape) { EndName(); e.SuppressKeyPress = true; }
        };
        updateButton = Ui.Add(this, new FlatButton { Text = "Update" }, 262, 72, 76, 32);
        updateButton.Click += (s, e) =>
        {
            if (naming) SaveSet(nameField.Box.Text.Trim());
            else if (profiles.Index <= 0) StartName(null);
            else UpdateSet();
        };
        moreButton = Ui.Add(this, new FlatButton { Text = "…", AccessibleName = "More preset actions" }, 346, 72, 36, 32);
        moreButton.Click += (s, e) => { if (naming) EndName(); else PresetMenu(); };

        var line = Ui.Add(this, new Control(), 0, 673, 656, 1);
        Ui.Style(line, () => line.BackColor = Theme.Border);
        status = Ui.Add(this, new StatusLine(), 20, 683, 460, 36);
        undoButton = Ui.Add(this, new FlatButton { Text = "Revert", Enabled = false, AccessibleName = "Revert changes" }, 416, 685, 72, 32);
        undoButton.Click += (s, e) => Undo();
        actionButton = Ui.Add(this, new FlatButton { Visible = false }, 328, 685, 160, 32);
        actionButton.TabIndex = 1000;
        actionButton.Click += (s, e) => { if (actDo != null) actDo(); };
        report = Ui.Add(this, new ReportView(noun, () => ReportView.TitleFor(found), Collect), 0, 72, 656, 658);
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
        reportButton.Left = 572 - 80 - 4 - 80;
        return Ui.Add(this, new FlatButton { Style = ButtonStyle.Ghost, Text = "Backups", AccessibleName = "Backups of the " + noun },
            572 - 80, 10, 80, 28);
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

    protected virtual bool Keepable { get { return true; } }

    protected void Edited() { touched = true; ShowSets(); }

    protected virtual void Lost() { }

    public void SetFound(FoundDevice device)
    {
        found = device;
        ShowSub();
    }

    void ShowSub()
    {
        subtitle.Set(found.Subtitle, found.Level, found.BatteryText, "");
    }

    public void ShowStatus(string text, bool warn = false, Action click = null, string action = null)
    {
        actDo = action != null ? click : null;
        actionButton.Visible = action != null;
        status.Set(text, warn, action != null ? null : click);
        ShowSets();
        if (action == null) { status.Width = undoButton.Left - status.Left - (int)(12 * Ui.DpiScale); return; }
        var main = MainButton();
        actionButton.Style = main == null || !main.Enabled ? ButtonStyle.Primary : ButtonStyle.Normal;
        actionButton.Text = action;
        int width = TextRenderer.MeasureText(action,
                Ui.Get(9.75f, actionButton.Style == ButtonStyle.Primary ? Ui.Semibold : Ui.Regular)).Width
            + (int)(28 * Ui.DpiScale);
        int right = undoButton.Left - (int)(8 * Ui.DpiScale);
        actionButton.Bounds = new Rectangle(right - width, actionButton.Top, width, actionButton.Height);
        status.Width = actionButton.Left - status.Left - (int)(12 * Ui.DpiScale);
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

    // A screen that didn't read its device when made, and isn't shown, reads it now, so its preset is sent again without opening it.
    public void LoadForPreset()
    {
        if (queuedWork || Settings.Get(PresetKey) == "") return;
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
                if (connection == null)
                {
                    connection = fake ?? new Hidpp(found.Path, found.Index, found.ShortPath);
                    connection.DeviceLabel = DeviceLabel;
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
                    Lost();
                }
                if (failed != null && IsHandleCreated) BeginInvoke((Action)(() => failed(ex.Message)));
                else ShowStatusLater(ex.Message, true);
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
        undoButton.Enabled = picked ? changed : Unsaved;
        Remember();
        if (!IsHandleCreated || !Visible) return;
        moreButton.Width = (int)((naming ? 76 : 36) * Ui.DpiScale);
        var main = MainButton();
        undoButton.Left = (main != null ? main.Left - (int)(8 * Ui.DpiScale) : (int)(636 * Ui.DpiScale)) - undoButton.Width;
    }

    bool Changed() { return touched && baseline != null && Current() != baseline; }

    void Undo()
    {
        if (profiles.Index <= 0 || baseline == null) Revert();
        else
        {
            try { ApplyPreset(baseline); }
            catch (Exception) { ShowStatus("This preset couldn't be read from settings.txt", true); }
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
            int color = Swatches[i];
            swatches[i] = Ui.Add(parent,
                new Swatch { Fill = Color.FromArgb(color | unchecked((int)0xFF000000)), AccessibleName = "Color " + color.ToString("X6") },
                10 + i * 36, y, 34, 34);
            swatches[i].Click += (s, e) => Pick(color);
        }
        custom = Ui.Add(parent, new Swatch { AccessibleName = "Your color" }, 10 + Swatches.Length * 36, y, 34, 34);
        custom.Click += (s, e) => { if (lastCustom >= 0) Pick(lastCustom); };
        more = Ui.Add(parent, new Swatch { AccessibleName = "Pick a color" }, 10 + Swatches.Length * 36, y, 34, 34);
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

    public void RefreshControls(int newColor, bool on)
    {
        color = newColor;
        bool isCustom = !Swatches.Contains(newColor);
        if (isCustom) lastCustom = newColor;
        for (int i = 0; i < swatches.Length; i++)
        {
            swatches[i].Enabled = on;
            swatches[i].On = on && Swatches[i] == newColor;
            swatches[i].Invalidate();
        }
        custom.Visible = lastCustom >= 0;
        custom.Fill = Color.FromArgb(lastCustom | unchecked((int)0xFF000000));
        custom.Enabled = on;
        custom.On = on && isCustom;
        custom.Invalidate();
        more.Left = custom.Left + (custom.Visible ? (int)Math.Round(36 * Ui.DpiScale) : 0);
        more.Enabled = on;
    }
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

    public InfoPage(FoundDevice device, MouseEventHandler drag, Action rescan)
    {
        Ui.Style(this, () => BackColor = Theme.Background);
        MouseDown += drag;
        title = Ui.Text(this, "", 20, 14, () => Theme.Text, 13.5f, Ui.Semibold);
        title.MouseDown += drag;
        subtitle = Ui.Add(this, new DeviceSubtitle(), 21, 42, 540, 20);
        subtitle.MouseDown += drag;

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
            536 - 4 - 80, 10, 80, 28);
        report = Ui.Add(this, new ReportView("device", () => ReportView.TitleFor(found), Collect), 0, 72, 656, 658);
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

    public EmptyPage(MouseEventHandler drag, Action rescan)
    {
        Ui.Style(this, () => BackColor = Theme.Background);
        MouseDown += drag;
        title = Centered(Ui.Text(this, "", 0, 150, () => Theme.Text, 15f, Ui.Semibold), 36);
        title.MouseDown += drag;
        text = Centered(Ui.Text(this, "", 0, 192, () => Theme.Dim, 9.75f), 44);
        text.MouseDown += drag;
        scan = Ui.Add(this, new FlatButton { Style = ButtonStyle.Primary, Text = "Scan again" }, 328 - 8 - 150, 252, 150, 32);
        scan.Click += (s, e) => rescan();
        report = Ui.Add(this, new FlatButton { Text = "Report your device" }, 328 + 8, 252, 150, 32);
        report.Click += (s, e) => Process.Start("https://github.com/csmarvz/g-lite/issues/new");
        tips = Centered(Ui.Text(this,
            "Turn the device on and wake it with a key press or a click.\nQuit G HUB, which can hold on to the device.\nPair Bluetooth devices in Windows first.",
            0, 320, () => Theme.Dim, 9f), 64);
        tips.MouseDown += drag;
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

class ReportView : Control
{
    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    static extern int SetWindowTheme(IntPtr window, string subAppName, string subIdList);
    const string Issues = "https://github.com/csmarvz/g-lite/issues/new";
    readonly TextBox box;
    readonly Label size;
    readonly FlatButton again, issue, copy;
    readonly StatusLine status;
    readonly Func<string> title;
    readonly Action<Action<string, string>> read;
    string text = "";
    System.Collections.Generic.List<Control> covered = new System.Collections.Generic.List<Control>();
    public event Action Closed;

    protected override bool ProcessCmdKey(ref Message message, Keys keyData)
    {
        if (keyData == Keys.Escape && Visible) { Close(); return true; }
        return base.ProcessCmdKey(ref message, keyData);
    }

    void Cover(bool on)
    {
        foreach (var control in covered) control.TabStop = true;
        covered.Clear();
        if (!on || Parent == null) return;
        covered.AddRange(Parent.Controls.Cast<Control>().Where(control => control != this && control.Bottom > Top && control.TabStop));
        foreach (var control in covered) control.TabStop = false;
    }

    public ReportView(string noun, Func<string> title, Action<Action<string, string>> read)
    {
        this.title = title;
        this.read = read;
        Visible = false;
        Ui.Style(this, () => BackColor = Theme.Background);

        var head = Ui.Add(this, new RoundedBox(), 20, 8, 616, 84);
        Ui.Text(head, "Device report", 14, 12, () => Theme.Dim, 9f, Ui.Semibold);
        var intro = Ui.Text(head,
            "What G-Lite reads from this " + noun + ". It has no serial number, no name you gave the device and no Windows path.",
            14, 34, () => Theme.Text, 9.75f);
        intro.AutoSize = false;
        intro.Size = new Size(440, 40);
        again = Ui.Add(head, new FlatButton { Text = "Read again", Icon = "refresh" }, 616 - 14 - 130, 26, 130, 32);
        again.Click += (s, e) => Read();

        var body = Ui.Add(this, new RoundedBox(), 20, 104, 616, 485);
        Ui.Text(body, "Report", 14, 12, () => Theme.Dim, 9f, Ui.Semibold);
        size = Ui.Text(body, "", 300, 12, () => Theme.Dim, 9f);
        size.AutoSize = false;
        size.Size = new Size(302, 18);
        size.TextAlign = ContentAlignment.TopRight;
        var inset = Ui.Add(body, new Control(), 14, 38, 588, 433);
        Ui.Style(inset, () => inset.BackColor = Theme.Background);
        box = Ui.Add(inset,
            new TextBox
            {
                Multiline = true, ReadOnly = true, WordWrap = false, ScrollBars = ScrollBars.Both,
                BorderStyle = BorderStyle.None, AccessibleName = "Device report"
            },
            8, 6, 576, 423);
        Ui.Font(box, 9f, "Consolas");
        Ui.Style(box, () => { box.BackColor = Theme.Background; box.ForeColor = Theme.Text; Dark(); });
        box.HandleCreated += (s, e) => Dark();

        var line = Ui.Add(this, new Control(), 0, 601, 656, 1);
        Ui.Style(line, () => line.BackColor = Theme.Border);
        status = Ui.Add(this, new StatusLine(), 20, 611, 256, 36);
        var back = Ui.Add(this, new FlatButton { Text = "Back" }, 284, 614, 76, 32);
        back.Click += (s, e) => Close();
        issue = Ui.Add(this, new FlatButton { Text = "Open GitHub issue" }, 368, 614, 160, 32);
        issue.Click += (s, e) => OpenIssue();
        copy = Ui.Add(this, new FlatButton { Style = ButtonStyle.Primary, Text = "Copy" }, 536, 614, 100, 32);
        copy.Click += (s, e) => { if (CopyToClipboard(text)) status.Set("Copied to the clipboard", false, null); };
    }

    public static string TitleFor(FoundDevice found)
    {
        return "Device report: " + (found.ShortName == "" ? "Logitech device" : found.ShortName)
            + (found.WirelessId != 0 ? " (" + found.WirelessId.ToString("X4") + ")" : "");
    }

    public void Open()
    {
        Visible = true;
        BringToFront();
        Cover(true);
        copy.Focus();
        if (text == "") Read();
    }

    public void Close()
    {
        Cover(false);
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
        read((report, error) =>
        {
            again.Enabled = true;
            if (report == null)
            {
                box.Text = text != "" ? text : "G-Lite couldn't read the device: " + error;
                issue.Enabled = copy.Enabled = text != "";
                status.Set(error, true, null);
                return;
            }
            report = new string(report
                .Where(character => character >= ' ' || character == '\r' || character == '\n' || character == '\t')
                .ToArray());
            text = report;
            box.Text = report;
            size.Text = (Encoding.UTF8.GetByteCount(report) / 1024.0).ToString("0.0", CultureInfo.InvariantCulture) + " KB, "
                + report.TrimEnd().Split('\n').Length + " lines";
            issue.Enabled = copy.Enabled = true;
            status.Set("Read only. Nothing is sent.", false, null);
        });
    }

    bool CopyToClipboard(string text)
    {
        try { Clipboard.SetText(text); return true; }
        catch (Exception) { status.Set("The clipboard is busy. Try again.", true, null); return false; }
    }

    void OpenIssue()
    {
        var fenced = "```\r\n" + text + "```\r\n";
        if (!CopyToClipboard(fenced)) return;
        var url = Issues + "?title=" + Uri.EscapeDataString(title()) + "&body=";
        bool whole = fenced.Length < 4000;
        try
        {
            Process.Start(url
                + Uri.EscapeDataString(whole ? fenced : "Paste the report from G-Lite here. It's on your clipboard.\r\n\r\n"));
            status.Set(whole ? "Opened a new issue with the report" : "Copied. Paste it into the new issue.", false, null);
        }
        catch (Exception ex) { status.Set("Couldn't open the browser: " + ex.Message, true, null); }
    }
}

class LogsPage : Control
{
    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    static extern int SetWindowTheme(IntPtr window, string subAppName, string subIdList);
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
    readonly RichTextBox box;
    readonly Label count;
    readonly StatusLine status;
    readonly Timer timer = new Timer { Interval = 1000 };
    System.Collections.Generic.List<Log.Line> shown = new System.Collections.Generic.List<Log.Line>();
    int version = -1;

    public LogsPage(MouseEventHandler drag)
    {
        Visible = false;
        Ui.Style(this, () => BackColor = Theme.Background);
        MouseDown += drag;
        Ui.Text(this, "Logs", 20, 14, () => Theme.Text, 13.5f, Ui.Semibold).MouseDown += drag;

        var body = Ui.Add(this, new RoundedBox(), 20, 56, 616, 605);
        Ui.Text(body, "log.txt, newest last", 14, 12, () => Theme.Dim, 9f, Ui.Semibold);
        count = Ui.Text(body, "", 300, 12, () => Theme.Dim, 9f);
        count.AutoSize = false;
        count.Size = new Size(302, 18);
        count.TextAlign = ContentAlignment.TopRight;
        var inset = Ui.Add(body, new Control(), 14, 38, 588, 553);
        Ui.Style(inset, () => inset.BackColor = Theme.Background);
        box = Ui.Add(inset,
            new RichTextBox
            {
                ReadOnly = true, WordWrap = false, ScrollBars = RichTextBoxScrollBars.Both,
                BorderStyle = BorderStyle.None, DetectUrls = false, AccessibleName = "Log"
            },
            8, 6, 576, 543);
        Ui.Font(box, 9f, "Consolas");
        Ui.Style(box, () => { box.BackColor = Theme.Background; Dark(); if (Visible) Render(); });
        box.HandleCreated += (s, e) => Dark();

        var line = Ui.Add(this, new Control(), 0, 673, 656, 1);
        Ui.Style(line, () => line.BackColor = Theme.Border);
        status = Ui.Add(this, new StatusLine(), 20, 683, 280, 36);
        var clear = Ui.Add(this, new FlatButton { Text = "Clear" }, 314, 686, 76, 32);
        clear.Click += (s, e) => { Log.Clear(); Render(); status.Set("Log cleared", false, null); };
        var folder = Ui.Add(this, new FlatButton { Text = "Open folder" }, 398, 686, 130, 32);
        folder.Click += (s, e) => OpenFolder();
        var copy = Ui.Add(this, new FlatButton { Style = ButtonStyle.Primary, Text = "Copy" }, 536, 686, 100, 32);
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
        shown = Log.MergeRepeatedSends(lines);
        count.Text = lines.Count + " lines, " + (Log.Size() / 1024.0).ToString("0.0", CultureInfo.InvariantCulture) + " KB";
        var rtf = new StringBuilder(@"{\rtf1\ansi\deff0{\fonttbl{\f0 Consolas;}}{\colortbl;"
            + Rgb(Theme.Text) + Rgb(Theme.Dim) + Rgb(Theme.Accent) + Rgb(Theme.Warn) + "}");
        foreach (var line in shown)
        {
            var text = Log.Format(line);
            int split = Math.Min(text.Length, line.Time.Length + 2 + 15);
            int colorIndex = line.Bad ? 4 : line.Kind == "you" ? 3 : line.Kind == "sent" ? 2 : 1;
            rtf.Append(@"\cf2 ").Append(EscapeRtf(text.Substring(0, split)))
                .Append(@"\cf" + colorIndex + " ").Append(EscapeRtf(text.Substring(split)))
                .Append(@"\par ");
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

    static string Rgb(Color color) { return @"\red" + color.R + @"\green" + color.G + @"\blue" + color.B + ";"; }

    static string EscapeRtf(string text)
    {
        var escaped = new StringBuilder();
        foreach (var character in text)
        {
            if (character == '\\' || character == '{' || character == '}') escaped.Append('\\').Append(character);
            else if (character > 127) escaped.Append(@"\u").Append((int)(short)character).Append('?');
            else escaped.Append(character);
        }
        return escaped.ToString();
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
