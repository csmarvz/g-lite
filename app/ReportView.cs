using System;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

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
        Size = new Size(Ui.PageSize.Width, Ui.PageSize.Height - 72);
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
        body.Anchor = Ui.Tall;
        Ui.Text(body, "Report", 14, 12, () => Theme.Dim, 9f, Ui.Semibold);
        size = Ui.Text(body, "", 300, 12, () => Theme.Dim, 9f);
        size.AutoSize = false;
        size.Size = new Size(302, 18);
        size.TextAlign = ContentAlignment.TopRight;
        var inset = Ui.Add(body, new Control(), 14, 38, 588, 433);
        inset.Anchor = Ui.Tall;
        Ui.Style(inset, () => inset.BackColor = Theme.Background);
        box = Ui.Add(inset,
            new TextBox
            {
                Multiline = true, ReadOnly = true, WordWrap = false, ScrollBars = ScrollBars.Both,
                BorderStyle = BorderStyle.None, AccessibleName = "Device report"
            },
            8, 6, 576, 423);
        box.Anchor = Ui.Tall;
        Ui.Font(box, 9f, "Consolas");
        Ui.Style(box, () => { box.BackColor = Theme.Background; box.ForeColor = Theme.Text; Dark(); });
        box.HandleCreated += (s, e) => Dark();

        status = Ui.Add(this, new StatusLine(), 20, 611, 256, 36);
        status.Anchor = Ui.Footer;
        var back = Ui.Add(this, new FlatButton { Text = "Back" }, 284, 614, 76, 32);
        back.Anchor = Ui.Footer;
        back.Click += (s, e) => Close();
        issue = Ui.Add(this, new FlatButton { Text = "Open GitHub issue" }, 368, 614, 160, 32);
        issue.Anchor = Ui.Footer;
        issue.Click += (s, e) => OpenIssue();
        copy = Ui.Add(this, new FlatButton { Style = ButtonStyle.Primary, Text = "Copy" }, 536, 614, 100, 32);
        copy.Anchor = Ui.Footer;
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
