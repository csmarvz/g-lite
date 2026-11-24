using System;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

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

    public LogsPage()
    {
        Visible = false;
        Size = Ui.PageSize;
        Ui.Style(this, () => BackColor = Theme.Background);
        Ui.Text(this, "Logs", 20, 14, () => Theme.Text, 13.5f, Ui.Semibold);

        var body = Ui.Add(this, new RoundedBox(), 20, 56, 616, 605);
        body.Anchor = Ui.Tall;
        Ui.Text(body, "log.txt, newest last", 14, 12, () => Theme.Dim, 9f, Ui.Semibold);
        count = Ui.Text(body, "", 300, 12, () => Theme.Dim, 9f);
        count.AutoSize = false;
        count.Size = new Size(302, 18);
        count.TextAlign = ContentAlignment.TopRight;
        var inset = Ui.Add(body, new Control(), 14, 38, 588, 553);
        inset.Anchor = Ui.Tall;
        Ui.Style(inset, () => inset.BackColor = Theme.Background);
        box = Ui.Add(inset,
            new RichTextBox
            {
                ReadOnly = true, WordWrap = false, ScrollBars = RichTextBoxScrollBars.Both,
                BorderStyle = BorderStyle.None, DetectUrls = false, AccessibleName = "Log"
            },
            8, 6, 576, 543);
        box.Anchor = Ui.Tall;
        Ui.Font(box, 9f, "Consolas");
        Ui.Style(box, () => { box.BackColor = Theme.Background; Dark(); if (Visible) Render(); });
        box.HandleCreated += (s, e) => Dark();

        status = Ui.Add(this, new StatusLine(), 20, 683, 280, 36);
        status.Anchor = Ui.Footer;
        var clear = Ui.Add(this, new FlatButton { Text = "Clear" }, 314, 686, 76, 32);
        clear.Anchor = Ui.Footer;
        clear.Click += (s, e) => { Log.Clear(); Render(); status.Set("Log cleared", false, null); };
        var folder = Ui.Add(this, new FlatButton { Text = "Open folder" }, 398, 686, 130, 32);
        folder.Anchor = Ui.Footer;
        folder.Click += (s, e) => OpenFolder();
        var copy = Ui.Add(this, new FlatButton { Style = ButtonStyle.Primary, Text = "Copy" }, 536, 686, 100, 32);
        copy.Anchor = Ui.Footer;
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
