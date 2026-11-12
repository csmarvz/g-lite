using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

class PageUpdate : Control
{
    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] static extern int SetWindowTheme(IntPtr h, string app, string id);
    readonly Label big, meta, link, whatsNew;
    readonly Box notesBox;
    readonly RichTextBox notes;
    readonly StatusLine status;
    readonly Btn back, skip, primary;
    readonly Dictionary<string, List<Release>> changelogs = new Dictionary<string, List<Release>>();
    readonly Dictionary<string, string> changelogErrors = new Dictionary<string, string>();
    readonly string installed = Application.ProductVersion;
    string state = "", version;
    bool fromSettings, showSkip, showPrimary = true;
    int renderedWidth;
    public Action BackClicked, SkipClicked, CheckClicked, RestartClicked;
    public Action<string> InstallClicked;
    public Func<string, string> Fetch = Updater.Changelog;

    public PageUpdate(MouseEventHandler drag)
    {
        Visible = false;
        Ui.Style(this, () => BackColor = Theme.Bg);
        MouseDown += drag;
        Ui.Text(this, "Update", 20, 14, () => Theme.Text, 13.5f, Ui.Semi).MouseDown += drag;

        var hero = Ui.Add(this, new Box(), 20, 64, 616, 92);
        big = Ui.Text(hero, "", 18, 12, () => Theme.Text, 12.75f, Ui.Semi);
        meta = Ui.Text(hero, "", 18, 42, () => Theme.Dim, 9f);
        meta.AutoSize = false;
        meta.AutoEllipsis = true;
        meta.Size = new Size(390, 18);
        link = Ui.Text(hero, "See on GitHub", 18, 62, () => Theme.Accent, 9f);
        link.Cursor = Cursors.Hand;
        link.Click += (s, e) => OpenRelease();
        var installedLabel = Ui.Text(hero, "Currently installed", 398, 26, () => Theme.Dim, 9f);
        installedLabel.AutoSize = false;
        installedLabel.Size = new Size(200, 18);
        installedLabel.TextAlign = ContentAlignment.TopRight;
        var installedText = Ui.Text(hero, installed, 398, 46, () => Theme.Text, 9.75f, Ui.Semi);
        installedText.AutoSize = false;
        installedText.Size = new Size(200, 20);
        installedText.TextAlign = ContentAlignment.TopRight;

        whatsNew = Ui.Text(this, "What's new", 20, 172, () => Theme.Dim, 9f, Ui.Semi);
        notesBox = Ui.Add(this, new Box(), 20, 194, 616, 463);
        notes = Ui.Add(notesBox, new RichTextBox { ReadOnly = true, WordWrap = true, ScrollBars = RichTextBoxScrollBars.Vertical, BorderStyle = BorderStyle.None, DetectUrls = false, AccessibleName = "Changelog" }, 18, 10, 594, 443);
        Ui.Style(notes, () => { notes.BackColor = Theme.Card; Dark(); Render(); });
        notes.HandleCreated += (s, e) => { Dark(); Later(Render); };
        notes.Resize += (s, e) => { if (notes.Width != renderedWidth) { renderedWidth = notes.Width; Later(Render); } };
        VisibleChanged += (s, e) => { if (Visible) { Arrange(); Render(); } };

        var line = Ui.Add(this, new Control(), 0, 673, 656, 1);
        Ui.Style(line, () => line.BackColor = Theme.Border);
        status = Ui.Add(this, new StatusLine(), 20, 683, 330, 36);
        back = Ui.Add(this, new Btn { Text = "Back" }, 284, 685, 76, 32);
        back.Click += (s, e) => { if (BackClicked != null) BackClicked(); };
        skip = Ui.Add(this, new Btn { Text = "Skip this version" }, 368, 685, 140, 32);
        skip.Click += (s, e) => { if (SkipClicked != null) SkipClicked(); };
        primary = Ui.Add(this, new Btn { Look = Look.Primary, Text = "Update now" }, 516, 685, 120, 32);
        primary.Click += (s, e) => Primary();
    }

    public bool FromSettings { get { return fromSettings; } set { fromSettings = value; Arrange(); } }

    public bool Busy { get { return state == "downloading"; } }

    public string State { get { return state; } }

    void Later(Action action)
    {
        var context = SynchronizationContext.Current;
        if (context == null) { action(); return; }
        context.Post(x => { if (!IsDisposed) action(); }, null);
    }

    void Primary()
    {
        if (state == "ready") { if (RestartClicked != null) RestartClicked(); }
        else if (state == "available" || state == "failed") { if (InstallClicked != null) InstallClicked(version); }
        else if (CheckClicked != null) CheckClicked();
    }

    void OpenRelease()
    {
        try { Process.Start(Updater.Repo + "/releases" + (version != null ? "/tag/v" + version : "")); }
        catch (Exception ex) { status.Set("Couldn't open the browser: " + ex.Message, true, null); }
    }

    public void Checking()
    {
        if (Busy) return;
        Set("checking", null, "Checking for updates…", "Asking GitHub for the latest release");
        status.Set("Checking…", false, null);
    }

    public void CheckFailed(string error)
    {
        if (Busy) return;
        Set("error", null, "Couldn't check for updates", error);
        status.Set("GitHub didn't answer. Check your connection and try again.", true, null);
    }

    public void Current()
    {
        if (Busy) return;
        Set("current", installed, "G-Lite is up to date", installed + " is the latest release");
        status.Set("Checked just now", false, null);
        Load();
    }

    public void Available(string v)
    {
        if (Busy) return;
        Set("available", v, "Version " + v, "");
        status.Set("Downloads from GitHub and checks it against checksums.txt", false, null);
        Load();
    }

    public void Progress(string v, int percent)
    {
        if (state != "downloading") Set("downloading", v, "Version " + v, "");
        meta.Text = percent < 0 ? "Downloading GLite.exe…" : "Downloading GLite.exe… " + percent + "%";
        status.Set("G-Lite restarts when it's done", false, null);
    }

    public void Failed(string v, string error)
    {
        Set("failed", v, "Version " + v, "The update failed");
        status.Set("Update failed: " + error, true, null);
        Load();
    }

    public void Ready(string v)
    {
        Set("ready", v, "Version " + v, "Installed. Restart G-Lite to use it.");
        status.Set("G-Lite runs " + installed + " until it restarts", false, null);
        Load();
    }

    public void Note(string text, bool warn) { status.Set(text, warn, null); }

    void Set(string newState, string v, string title, string detail)
    {
        state = newState;
        version = v;
        big.Text = title;
        meta.Text = detail;
        link.Visible = v != null;
        whatsNew.Visible = notesBox.Visible = v != null;
        whatsNew.Text = newState == "current" ? "What's new in " + installed : "What's new";
        showSkip = newState == "available" || newState == "failed" || newState == "downloading";
        skip.Enabled = primary.Enabled = newState != "downloading";
        showPrimary = newState != "checking";
        primary.Text = newState == "ready" ? "Restart G-Lite" : newState == "available" ? "Update now" : newState == "failed" ? "Try again" : newState == "downloading" ? "Updating…" : "Check again";
        Arrange();
        Render();
    }

    void Arrange()
    {
        back.Visible = fromSettings;
        skip.Visible = showSkip;
        primary.Visible = showPrimary;
        float k = Width / 656f;
        int x = (int)Math.Round(636 * k);
        foreach (var b in new[] { primary, skip, back })
        {
            if (b == back ? !fromSettings : b == skip ? !showSkip : !showPrimary) continue;
            x -= b.Width;
            b.Left = x;
            x -= (int)Math.Round(8 * k);
        }
        status.Width = Math.Max(0, x + (int)Math.Round(8 * k) - status.Left - (int)Math.Round(12 * k));
    }

    void Load()
    {
        var tag = version;
        if (tag == null || changelogs.ContainsKey(tag)) { Render(); return; }
        changelogErrors.Remove(tag);
        var context = SynchronizationContext.Current;
        Task.Run(() =>
        {
            List<Release> list = null;
            string error = null;
            try { list = Updater.Releases(Fetch(tag)); } catch (Exception ex) { error = ex.Message; }
            context.Post(x =>
            {
                if (IsDisposed) return;
                if (list != null) changelogs[tag] = list; else changelogErrors[tag] = error;
                Render();
            }, null);
        });
    }

    List<Release> Listed()
    {
        List<Release> all;
        if (version == null || !changelogs.TryGetValue(version, out all)) return null;
        if (state == "current") return all.Where(r => r.Version == installed).ToList();
        return Updater.Between(all, installed, version);
    }

    void Dark()
    {
        if (notes.IsHandleCreated) SetWindowTheme(notes.Handle, Theme.Light ? "Explorer" : "DarkMode_Explorer", null);
    }

    void Add(string text, float size, string face, Color color, bool bullet)
    {
        notes.SelectionStart = notes.TextLength;
        notes.SelectionLength = 0;
        notes.SelectionFont = Ui.Get(size, face);
        notes.SelectionColor = color;
        notes.SelectionBullet = bullet;
        notes.BulletIndent = (int)Math.Round(12 * Ui.K);
        notes.AppendText(text);
    }

    void Render()
    {
        if (version == null || !notes.IsHandleCreated) return;
        var list = Listed();
        if (state != "current" && list != null)
        {
            var newest = list.FirstOrDefault(r => r.Version == version);
            if (state == "available" && newest != null && newest.Date != "") meta.Text = "Released " + newest.Date + (list.Count > 1 ? " · " + list.Count + " versions newer than yours" : "");
            if (list.Count > 1) whatsNew.Text = "What's new since " + installed;
        }
        notes.Clear();
        string error;
        if (list == null && version != null && changelogErrors.TryGetValue(version, out error)) Add("Couldn't load the changelog: " + error + "\nSee on GitHub shows it too.", 9.75f, Ui.Regular, Theme.Dim, false);
        else if (list == null) Add("Loading the changelog…", 9.75f, Ui.Regular, Theme.Dim, false);
        else if (list.Count == 0) Add("The changelog doesn't list this version.", 9.75f, Ui.Regular, Theme.Dim, false);
        else
        {
            bool first = true;
            foreach (var release in list)
            {
                if (!first) Add("\n", 9.75f, Ui.Regular, Theme.Text, false);
                first = false;
                Add(release.Version, 9.75f, Ui.Semi, Theme.Text, false);
                Add((release.Date != "" ? "   " + release.Date : "") + "\n", 9f, Ui.Regular, Theme.Dim, false);
                foreach (var text in release.Lines)
                {
                    var plain = text.Replace("`", "").Replace("**", "");
                    if (plain.StartsWith("#"))
                    {
                        Add("\n", 4.5f, Ui.Regular, Theme.Text, false);
                        Add(plain.TrimStart('#', ' ').ToUpperInvariant() + "\n", 8.25f, Ui.Semi, Theme.Accent, false);
                    }
                    else if (plain.StartsWith("- ") || plain.StartsWith("* ")) Add(plain.Substring(2) + "\n", 9.75f, Ui.Regular, Theme.Text, true);
                    else Add(plain + "\n", 9.75f, Ui.Regular, Theme.Text, false);
                }
            }
        }
        Add("", 9.75f, Ui.Regular, Theme.Text, false);
        notes.SelectionStart = 0;
        notes.SelectionLength = 0;
        notes.ScrollToCaret();
    }
}
