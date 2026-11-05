using System;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

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
            : "G-Lite found this device but can't change its settings yet. Mice and headsets come next.";
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
