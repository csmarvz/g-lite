using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

class TraceHost : Form
{
    protected override bool ShowWithoutActivation { get { return true; } }

    protected override CreateParams CreateParams
    {
        get
        {
            var createParams = base.CreateParams;
            createParams.ExStyle |= 0x08000000 | 0x80;
            return createParams;
        }
    }
}

class Script : IDisposable
{
    readonly List<string> lines = new List<string>();
    readonly object gate = new object();
    readonly TraceHost host;
    public readonly Control Page;

    public Script(Func<Control> make)
    {
        lines.Add("open");
        Hidpp.Tap = text => { lock (gate) lines.Add("  " + text); };
        host = new TraceHost
        {
            ShowInTaskbar = false, StartPosition = FormStartPosition.Manual, Location = new Point(-32000, -32000),
            FormBorderStyle = FormBorderStyle.None, Opacity = 0, ClientSize = new Size(656, 730)
        };
        try
        {
            Page = make();
            Page.Bounds = new Rectangle(0, 0, 656, 730);
            host.Controls.Add(Page);
            host.Show();
            ((IPage)Page).Shown();
            Wait();
            Add("  = " + State());
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public string Text
    {
        get { lock (gate) return string.Join("\n", lines) + "\n"; }
    }

    void Add(string text) { lock (gate) lines.Add(text); }

    public static bool Timing;

    public void Do(string what, Action action)
    {
        Add(what);
        var started = DateTime.UtcNow;
        action();
        Wait();
        if (Timing) Console.WriteLine("  " + what + " " + (int)(DateTime.UtcNow - started).TotalMilliseconds + " ms");
        Add("  = " + State());
    }

    public void Press(string name) { Do("press " + name, () => Find<DrawnControl>(name).Press()); }

    public void Pick(string name, string item)
    {
        Do("pick " + name + " " + item, () =>
        {
            var picker = All<SegmentedPicker>().FirstOrDefault(control => control.Visible && control.AccessibleName == name);
            if (picker != null) { picker.Pick(Index(picker.Items.ToList(), item, name)); return; }
            var drop = Find<Dropdown>(name);
            drop.Pick(Index(drop.Items, item, name));
        });
    }

    public void Slide(string name, int value)
    {
        Do("slide " + name + " " + value, () =>
        {
            var slider = Find<Slider>(name);
            if (!slider.Enabled) throw new InvalidOperationException(name + " can't be changed now");
            slider.SetValue(value);
        });
    }

    public void Type(string text) { Do("type " + text, () => All<TextField>().First(field => field.Visible).Box.Text = text); }

    public void Menu(string button, string item)
    {
        Do("menu " + button + " > " + item, () =>
        {
            ContextMenuStrip menu = null;
            Ui.Popped = popped => menu = popped;
            try { Find<DrawnControl>(button).Press(); }
            finally { Ui.Popped = null; }
            if (menu == null) throw new InvalidOperationException(button + " didn't open a menu");
            var items = menu.Items.OfType<ToolStripMenuItem>().ToList();
            Add("  menu: " + NormalizeForTrace(string.Join(" | ", items.Select(i => i.Text + (i.Enabled ? "" : " (off)")))));
            var picked = items.FirstOrDefault(i => i.Text == item);
            if (picked == null) throw new InvalidOperationException(button + " has no " + item);
            if (!picked.Enabled) throw new InvalidOperationException(item + " is off");
            picked.PerformClick();
            menu.Dispose();
        });
    }

    static int Index(List<string> items, string item, string name)
    {
        int i = items.IndexOf(item);
        if (i < 0) throw new InvalidOperationException(name + " has no " + item + " (" + string.Join(", ", items) + ")");
        return i;
    }

    IEnumerable<T> All<T>() where T : Control
    {
        var stack = new Stack<Control>();
        stack.Push(Page);
        while (stack.Count > 0)
        {
            var control = stack.Pop();
            if (control is T) yield return (T)control;
            foreach (Control child in control.Controls) stack.Push(child);
        }
    }

    T Find<T>(string name) where T : Control
    {
        var found = All<T>().Where(control => control.Visible && (control.AccessibleName == name || control.Text == name)).ToList();
        if (found.Count == 0) throw new InvalidOperationException("no " + name + " on the screen");
        if (found.Count > 1) throw new InvalidOperationException(found.Count + " controls named " + name + " on the screen");
        return found[0];
    }

    public void Wait()
    {
        var page = Page as DevicePage;
        var queue = ((IPage)Page).Queue;
        var end = DateTime.UtcNow.AddSeconds(20);
        int count = -1;
        var still = DateTime.UtcNow;
        while (true)
        {
            Application.DoEvents();
            Thread.Sleep(5);
            queue = ((IPage)Page).Queue;
            int lineCount;
            lock (gate) lineCount = lines.Count;
            if (lineCount != count || !queue.IsCompleted || page != null && page.Busy) { count = lineCount; still = DateTime.UtcNow; }
            else if ((DateTime.UtcNow - still).TotalMilliseconds > 150) return;
            if (DateTime.UtcNow > end) throw new TimeoutException("the screen didn't settle");
        }
    }

    Point Where(Control control)
    {
        var offset = Point.Empty;
        for (var ancestor = control; ancestor != null && ancestor != Page; ancestor = ancestor.Parent)
            offset.Offset(ancestor.Left, ancestor.Top);
        return offset;
    }

    string State()
    {
        var parts = new List<string>();
        var status = All<StatusLine>().FirstOrDefault(line => line.Visible);
        parts.Add("status " + (status == null ? "-" : "\"" + status.Shown + "\""));
        foreach (var control in All<Control>()
            .Where(control => control.Visible)
            .OrderBy(control => Where(control).Y)
            .ThenBy(control => Where(control).X))
        {
            string disabled = control.Enabled ? "" : " (off)";
            if (control is FlatButton && control.Text != "") parts.Add("[" + control.Text + disabled + "]");
            else if (control is SegmentedPicker)
            {
                var picker = (SegmentedPicker)control;
                parts.Add(control.AccessibleName + "="
                    + (picker.Index >= 0 && picker.Index < picker.Items.Length ? picker.Items[picker.Index] : "-") + disabled
                    + (picker.Off.Length == 0
                        ? ""
                        : " (no "
                            + string.Join(", ", picker.Off.Where(i => i >= 0 && i < picker.Items.Length).Select(i => picker.Items[i]))
                            + ")"));
            }
            else if (control is Dropdown)
                parts.Add(control.AccessibleName + "="
                    + (control.AccessibleName == "Monitor" ? "<monitor>" : control.Text == "" ? "-" : control.Text) + disabled);
            else if (control is Slider) parts.Add(control.AccessibleName + "=" + ((Slider)control).Value + disabled);
            else if (control is Toggle) parts.Add(control.AccessibleName + "=" + (((Toggle)control).Checked ? "on" : "off") + disabled);
            else if (control is Swatch && ((Swatch)control).On) parts.Add("<" + control.AccessibleName + ">");
        }
        return NormalizeForTrace(string.Join(" ", parts));
    }

    static string NormalizeForTrace(string text)
    {
        text = Regex.Replace(text, @"\d{1,2} [A-Z][a-z]{2} \d{2}:\d{2}", "<when>");
        return Regex.Replace(text, @"\b\d{2}:\d{2}\b", "<time>");
    }

    public void Dispose()
    {
        Hidpp.Tap = null;
        Ui.Popped = null;
        host.Close();
        if (Page != null) Page.Dispose();
        host.Dispose();
    }
}

static class Traces
{
    static readonly int[] Keyboard = { 0x0001, 0x0003, 0x0005, 0x1004, 0x4540, 0x8040, 0x8071, 0x8081, 0x8101 };

    static Script Begin(Func<Control> make)
    {
        return last = new Script(make);
    }

    static void Fresh()
    {
        last = null;
        Page515.TestDynamicLighting = 0;
        Settings.Location = Path.GetTempFileName();
        Settings.Init();
        Backups.Root = Path.Combine(Path.GetTempPath(), "glite-traces-" + Guid.NewGuid().ToString("N"));
        Log.File = Path.GetTempFileName();
        WindowHelpers.TestIdle = 0;
        Presence.Set(false, false);
        DevicePage.Manual = true;
    }

    static Script last;

    static string G515()
    {
        var device = new FakeHidpp();
        var found = new FoundDevice
        {
            Key = "trace|g515", Name = "G515 LIGHTSPEED TKL", Kind = "keyboard",
            Link = "LIGHTSPEED", Index = 1, WirelessId = 0x40B4, Features = Keyboard
        };
        Page515 page = null;
        using (var script = Begin(() => page = (Page515)DeviceTable.For(found).Make(found, device, null)))
        {
            script.Pick("Effect", "Static");
            script.Press("Color FF2020");
            script.Slide("Brightness", 40);
            script.Pick("Effect", "Breathe");
            script.Slide("Speed", 30);
            script.Press("Revert changes");
            script.Pick("Effect", "Static");
            script.Press("Color 3080FF");
            script.Pick("Dim after", "1 min");
            script.Press("Save to keyboard");
            script.Menu("More preset actions", "Save as new preset…");
            script.Type("Night");
            script.Press("Save");
            script.Press("Color FFD000");
            script.Pick("Preset", "None");
            script.Pick("Preset", "Night");
            script.Press("Update");
            script.Pick("Effect", "Wave");
            script.Do("idle 70 s", () => { WindowHelpers.TestIdle = 70; page.Watch(); });
            script.Do("idle 4000 s", () => { WindowHelpers.TestIdle = 4000; page.Watch(); });
            script.Do("back", () => { WindowHelpers.TestIdle = 0; page.Watch(); });
            script.Do("lock", () => Presence.Set(true, false));
            script.Do("unlock", () => Presence.Set(false, false));
            script.Do("power cycle", () => { device.PowerCycle(); for (int i = 0; i < 6; i++) page.Watch(); });
            script.Menu("Backups", "Restore the factory lighting");
            script.Pick("Effect", "Cycle");
            script.Do("quit", () => page.Exiting());
            return script.Text;
        }
    }

    static string G915()
    {
        var found = new FoundDevice
        {
            Key = "trace|g915", Name = "G915 TKL LIGHTSPEED Wireless RGB Mechanical Gaming Keyboard", Kind = "keyboard",
            Link = "LIGHTSPEED", Index = 1, WirelessId = 0x408E,
            Features = new[] { 0x0001, 0x0003, 0x0005, 0x1001, 0x8040, 0x8071, 0x8081, 0x8100 }
        };
        Page515 page = null;
        using (var script = Begin(() => page = (Page515)DeviceTable.For(found).Make(found, new FakeG915(), null)))
        {
            script.Pick("Effect", "Static");
            script.Press("Color 30D050");
            script.Slide("Brightness", 50);
            script.Menu("More preset actions", "Save as new preset…");
            script.Type("Green");
            script.Press("Save");
            script.Pick("Effect", "Breathe");
            script.Pick("Preset", "None");
            script.Pick("Preset", "Green");
            script.Pick("Dim after", "15 s");
            script.Pick("Turn off after", "10 min");
            script.Press("Save to keyboard");
            script.Pick("Effect", "Wave");
            script.Press("Revert");
            script.Menu("Backups", "Restore the factory lighting");
            script.Do("quit", () => page.Exiting());
            return script.Text;
        }
    }

    static string G512X()
    {
        var found = new FoundDevice
        {
            Key = "trace|g512x", Name = "G512 X 75", Kind = "keyboard", Link = "USB", Index = 0xFF,
            Features = new[] { 0x0001, 0x0003, 0x0005, 0x8071, 0x8081, 0x8040, 0x8101, 0x4540 }
        };
        Page515 page = null;
        using (var script = Begin(() => page = (Page515)DeviceTable.For(found).Make(found, FakeG915.G512X(), null)))
        {
            script.Pick("Effect", "Static");
            script.Press("Color 30D050");
            script.Pick("Effect", "Wave");
            script.Pick("Effect", "Ripple");
            script.Slide("Brightness", 50);
            script.Press("Lighting per zone");
            script.Pick("Part", "Light bar");
            script.Pick("Effect", "Breathe");
            script.Press("Color FF2020");
            script.Pick("Part", "Keys");
            script.Pick("Effect", "Cycle");
            script.Menu("More preset actions", "Save as new preset…");
            script.Type("Split");
            script.Press("Save");
            script.Slide("Brightness", 70);
            script.Press("Revert changes");
            script.Press("Lighting per zone");
            script.Press("Revert changes");
            script.Do("quit", () => page.Exiting());
            return script.Text;
        }
    }

    static string Generic()
    {
        var found = new FoundDevice
        {
            Key = "trace|prox60", Name = "PRO X 60", Kind = "keyboard", Link = "LIGHTSPEED", Index = 1,
            Features = new[] { 0x0001, 0x0003, 0x0005, 0x1004, 0x4540, 0x8040, 0x8071, 0x8081, 0x8101 }
        };
        Page515 page = null;
        using (var script = Begin(() => page = (Page515)DeviceTable.For(found).Make(found, FakeG915.G512X(), null)))
        {
            script.Pick("Effect", "Static");
            script.Press("Color 30D050");
            script.Pick("Effect", "Wave");
            script.Slide("Brightness", 50);
            script.Do("quit", () => page.Exiting());
            return script.Text;
        }
    }

    static string G512()
    {
        var found = new FoundDevice
        {
            Key = "trace|g512", Name = "G512 RGB MECHANICAL GAMING KEYBOARD", Kind = "keyboard", Link = "USB", Index = 0xFF,
            Features = new[] { 0x0001, 0x0003, 0x0005, 0x4540, 0x8060, 0x8070, 0x8080 }
        };
        using (var script = Begin(() => DeviceTable.For(found).Make(found, new FakeSpeaker { Count = 2 }, null)))
        {
            script.Pick("Effect", "Static");
            script.Press("Color 30D050");
            script.Slide("Brightness", 50);
            script.Pick("Zone", "Logo");
            script.Pick("Effect", "Breathe");
            script.Slide("Speed", 80);
            script.Pick("Zone", "All");
            script.Pick("Effect", "Cycle");
            script.Pick("Effect", "Off");
            return script.Text;
        }
    }

    static string G213()
    {
        var found = new FoundDevice
        {
            Key = "trace|g213", Name = "Gaming Keyboard G213", Kind = "keyboard", Link = "USB", Index = 0xFF,
            Features = new[]
            {
                0x0001, 0x0003, 0x4522, 0x0005, 0x1E00, 0x4540, 0x1EB0, 0x8060, 0x00C2, 0x1801, 0x1802, 0x8070, 0x1821, 0x1BC0, 0x1806
            }
        };
        using (var script = Begin(() => DeviceTable.For(found).Make(found, new FakeSpeaker { Count = 6, G213 = true }, null)))
        {
            script.Pick("Effect", "Static");
            script.Press("Color 30D050");
            script.Pick("Zone", "Zone 3");
            script.Press("Color 3080FF");
            script.Pick("Effect", "Off");
            script.Pick("Zone", "All");
            script.Pick("Effect", "Breathe");
            script.Pick("Effect", "Cycle");
            script.Pick("Effect", "Wave");
            script.Pick("Direction", "Center out");
            script.Slide("Speed", 50);
            script.Pick("Zone", "Zone 2");
            script.Pick("Effect", "Static");
            script.Press("Color FF2020");
            script.Pick("Zone", "All");
            script.Pick("Effect", "Breathe");
            script.Pick("Zone", "Zone 4");
            script.Pick("Effect", "Off");
            script.Pick("Zone", "All");
            script.Pick("Effect", "Breathe");
            script.Pick("Zone", "Zone 2");
            script.Press("Color 3080FF");
            script.Menu("More preset actions", "Save as new preset…");
            script.Type("Wave");
            script.Press("Save");
            return script.Text;
        }
    }

    static string G510()
    {
        var found = new FoundDevice
        {
            Key = "trace|g510", Name = "G510s Gaming Keyboard", Kind = "keyboard", Link = "USB", Index = 0xFF,
            PlainHid = true, HasVendorCollection = true, Path = @"\\?\hid#vid_046d&pid_c22d&mi_01&col02"
        };
        using (var script = Begin(() => DeviceTable.For(found).Make(found, new FakeG510(), null)))
        {
            script.Press("Color FF2020");
            script.Slide("Brightness", 50);
            script.Pick("Effect", "Off");
            script.Pick("Effect", "Static");
            script.Menu("More preset actions", "Save as new preset…");
            script.Type("Red");
            script.Press("Save");
            script.Press("Color 3080FF");
            script.Pick("Effect", "Breathe");
            script.Slide("Speed", 50);
            script.Pick("Effect", "Cycle");
            script.Pick("Effect", "Static");
            script.Pick("Preset", "None");
            return script.Text;
        }
    }

    static string G560()
    {
        var found = new FoundDevice
        {
            Key = "trace|g560", Name = "G560 Gaming Speaker", Kind = "device", Link = "USB", Index = 0xFF,
            Features = new[] { 0x0001, 0x0003, 0x0005, 0x8070, 0x8010, 0x8310, 0x8320, 0x8040, 0x8305 }
        };
        string text;
        using (var script = Begin(() => DeviceTable.For(found).Make(found, new FakeSpeaker(), null)))
        {
            script.Pick("Effect", "Static");
            script.Press("Color 30D050");
            script.Slide("Brightness", 50);
            script.Pick("Effect", "Breathe");
            script.Slide("Speed", 80);
            script.Menu("More preset actions", "Save as new preset…");
            script.Type("Green");
            script.Press("Save");
            script.Pick("Zone", "Bottom right");
            script.Pick("Effect", "Static");
            script.Press("Color 3080FF");
            script.Pick("Zone", "All");
            script.Pick("Effect", "Cycle");
            script.Pick("Effect", "Off");
            script.Pick("Preset", "None");
            script.Pick("Preset", "Green");
            script.Pick("Effect", "Screen");
            script.Press("Update");
            script.Slide("Color boost", 80);
            script.Press("Revert changes");
            script.Pick("Preset", "None");
            script.Pick("Preset", "Green");
            script.Pick("Effect", "Static");
            script.Press("Revert changes");
            text = script.Text;
        }
        return text + Restart(found, () => new FakeSpeaker());
    }

    // G-Lite starts again on a device that lost its lighting: the preset picked last is picked and sent again,
    // first one with the Screen effect, then (after Update) one without it.
    static string Restart(FoundDevice found, Func<Hidpp> device)
    {
        string text;
        using (var script = Begin(() => DeviceTable.For(found).Make(found, device(), null)))
        {
            script.Pick("Effect", "Static");
            script.Press("Update");
            text = "restart\n" + script.Text;
        }
        using (var script = Begin(() => DeviceTable.For(found).Make(found, device(), null)))
            return text + "restart\n" + script.Text;
    }

    static string Litra()
    {
        var found = new FoundDevice
        {
            Key = "trace|litra", Name = "Litra Beam LX", Kind = "light", Link = "USB", Index = 0xFF,
            Features = new[] { 0x0001, 0x0003, 0x0005, 0x1990, 0x8040, 0x8071, 0x8081 }
        };
        string text;
        using (var script = Begin(() => DeviceTable.For(found).Make(found, new FakeLight(), null)))
        {
            script.Press("Front light on");
            script.Slide("Front brightness", 50);
            script.Slide("Color temperature", 10);
            script.Slide("Back brightness", 40);
            script.Press("Color FF2020");
            script.Pick("Zone", "3");
            script.Press("Color 3080FF");
            script.Menu("More preset actions", "Save as new preset…");
            script.Type("Desk");
            script.Press("Save");
            script.Press("Back light on");
            script.Pick("Zone", "All");
            script.Press("Color FFFFFF");
            script.Press("Revert changes");
            script.Pick("Effect", "Breathe");
            script.Slide("Speed", 80);
            script.Pick("Effect", "Wave");
            script.Menu("Try the light's own effects", "Group 0: Breathe");
            script.Menu("Try the light's own effects", "Group 1: Smooth colour wave");
            script.Menu("Try the light's own effects", "Back to G-Lite's colours");
            script.Pick("Effect", "Static");
            script.Pick("Effect", "Screen");
            script.Press("Update");
            script.Slide("Color boost", 80);
            script.Press("Revert changes");
            script.Pick("Preset", "None");
            script.Pick("Preset", "Desk");
            script.Pick("Effect", "Static");
            script.Press("Revert changes");
            text = script.Text;
        }
        return text + Restart(found, () => new FakeLight());
    }

    static string MxMaster()
    {
        var found = new FoundDevice
        {
            Key = "trace|mxmaster", Name = "MX Master 3S", Kind = "mouse", Link = "Bolt receiver", Index = 2,
            Features = new[] { 0x0001, 0x0003, 0x0005, 0x1004, 0x1814, 0x1815, 0x2110, 0x2121, 0x2150, 0x2201 }
        };
        string first;
        using (var script = Begin(() => DeviceTable.For(found).Make(found, new FakeOther(), null)))
        {
            script.Slide("Speed", 36);
            script.Slide("Switch to free spin at", 19);
            script.Press("SmartShift");
            script.Press("Invert scrolling");
            script.Press("Invert the thumb wheel");
            script.Menu("More preset actions", "Save as new preset…");
            script.Type("Work");
            script.Press("Save");
            script.Press("SmartShift");
            script.Press("Revert changes");
            script.Pick("Computer", "3: –");
            script.Pick("Computer", "2: MacBook");
            script.Press("Switch");
            first = script.Text;
        }
        using (var script = Begin(() => DeviceTable.For(found).Make(found, new FakeOther(), null)))
            return first + "reopen on a device that forgot\n" + script.Text;
    }

    static string MxKeys()
    {
        var found = new FoundDevice
        {
            Key = "trace|mxkeys", Name = "MX Keys S", Kind = "keyboard", Link = "Bolt receiver", Index = 3,
            Features = new[] { 0x0001, 0x0003, 0x0005, 0x1004, 0x1814, 0x1815, 0x1982, 0x40A3 }
        };
        using (var script = Begin(() => DeviceTable.For(found).Make(found, new FakeOther(), null)))
        {
            script.Slide("Level", 5);
            script.Press("Automatic");
            script.Press("Backlight");
            script.Press("Backlight");
            script.Press("Automatic");
            script.Pick("Fades after, hands away", "2 min");
            script.Pick("Fades after, plugged in", "1 h");
            script.Press("Revert changes");
            return script.Text;
        }
    }

    static string Keypad()
    {
        var found = new FoundDevice
        {
            Key = "trace|keypad", Name = "MX Creative Keypad", Kind = "device", Link = "USB", Index = 0xFF,
            Features = new[] { 0x0001, 0x0003, 0x0005, 0x1B04, 0x8040 }
        };
        using (var script = Begin(() => DeviceTable.For(found).Make(found, new FakeOther(), null)))
        {
            script.Slide("Brightness", 40);
            script.Press("Revert changes");
            return script.Text;
        }
    }

    static string Mouse(string name, FakeMouse device, int[] features)
    {
        var found = new FoundDevice
        {
            Key = "trace|" + name, Name = name, Kind = "mouse",
            Link = "LIGHTSPEED", Index = 1, WirelessId = 0x4079, Features = features
        };
        using (var script = Begin(() => DeviceTable.For(found).Make(found, device, null)))
        {
            script.Slide("DPI", 20);
            script.Pick("DPI stage", "400");
            script.Pick("Effect", "Static");
            script.Press("Color FFD000");
            script.Slide("Brightness", 60);
            script.Pick("Report rate", "500");
            script.Press("Save to mouse");
            script.Pick("Effect", "Breathe");
            script.Press("Revert changes");
            script.Menu("More preset actions", "Save as new preset…");
            script.Type("Work");
            script.Press("Save");
            script.Pick("DPI stage", "3200");
            script.Press("Update");
            script.Pick("Preset", "None");
            script.Menu("Backups", "Restore the factory profile");
            script.Do("the mouse's DPI button", () =>
            {
                device.PressDpiButton();
                ((PageMouse)script.Page).Heard(device.FeatureIndex(0x8100), 1);
            });
            return script.Text;
        }
    }

    static string TraceWideDpiMouse()
    {
        var found = new FoundDevice
        {
            Key = "trace|superlight2", Name = "PRO X SUPERLIGHT 2", Kind = "mouse", Link = "LIGHTSPEED", Index = 1, WirelessId = 0x40A9,
            Features = new[] { 0x0001, 0x0003, 0x0005, 0x1004, 0x2202, 0x8061, 0x8100 }
        };
        using (var script = Begin(() => DeviceTable.For(found).Make(found, FakeMouse.ProX2(), null)))
        {
            script.Slide("DPI", 20);
            script.Pick("DPI stage", "1600");
            script.Pick("DPI stage", "400");
            return script.Text;
        }
    }

    static string Wheel()
    {
        var device = new FakeMouse { Wheel = true };
        var found = new FoundDevice
        {
            Key = "trace|wheel", Name = "G502 LIGHTSPEED Wireless Gaming Mouse", Kind = "mouse",
            Link = "LIGHTSPEED", Index = 1, WirelessId = 0x407F,
            Features = new[] { 0x0001, 0x0003, 0x0005, 0x1001, 0x2121, 0x2201, 0x8060, 0x8070, 0x8100 }
        };
        PageMouse page = null;
        using (var script = Begin(() => page = (PageMouse)DeviceTable.For(found).Make(found, device, null)))
        {
            script.Do("watch, off", () => page.Watch());
            script.Press("Invert scrolling");
            script.Do("watch, on", () => page.Watch());
            script.Do("power cycle, then watch", () => { device.WheelMode = 0; page.Watch(); });
            script.Press("Invert scrolling");
            script.Do("watch, off", () => page.Watch());
            return script.Text;
        }
    }

    static string Buttons()
    {
        var device = FakeMouse.G502();
        var found = new FoundDevice
        {
            Key = "trace|buttons", Name = "G502 LIGHTSPEED Wireless Gaming Mouse", Kind = "mouse",
            Link = "LIGHTSPEED", Index = 1, WirelessId = 0x407F,
            Features = new[] { 0x0001, 0x0003, 0x0005, 0x1001, 0x2121, 0x2201, 0x8060, 0x8070, 0x8100, 0x8110 }
        };
        PageMouse page = null;
        using (var script = Begin(() => page = (PageMouse)DeviceTable.For(found).Make(found, device, null)))
        {
            script.Pick("View", "Buttons");
            script.Do("G7 pressed on the mouse", () => page.Pressed(0x0040));
            script.Pick("G7", "Next profile");
            script.Pick("Thumb, front", "Keystroke…");
            script.Do("keys Ctrl + Shift + M", () => page.Recorded(Keys.Control | Keys.Shift | Keys.M));
            script.Press("Save to mouse");
            script.Pick("G8", "Disabled");
            script.Press("Revert changes");
            script.Menu("More preset actions", "Save as new preset…");
            script.Type("Keys");
            script.Press("Save");
            script.Pick("G9", "Mute");
            script.Pick("View", "Settings");
            script.Pick("Report rate", "250");
            script.Press("Revert changes");
            script.Pick("Preset", "None");
            script.Pick("Report rate", "500");
            script.Press("Save to mouse");
            script.Pick("Preset", "Keys");
            script.Pick("View", "Buttons");
            script.Pick("Left button", "Back");
            script.Pick("Thumb, back", "Left click");
            script.Press("Save to mouse");
            return script.Text;
        }
    }

    static string G9x()
    {
        var found = new FoundDevice
        {
            Key = "trace|g9x", Name = "G9x Laser Mouse", Kind = "mouse",
            Link = "USB", Index = 0, Hidpp10 = true, Path = @"\\?\hid#vid_046d&pid_c066&mi_01&col04"
        };
        using (var script = Begin(() => DeviceTable.For(found).Make(found, new FakeG9x(), null)))
        {
            script.Slide("DPI", 30);
            script.Pick("Effect", "Static");
            script.Press("Color 3080FF");
            script.Pick("Report rate", "500");
            script.Press("Save to mouse");
            script.Pick("Effect", "Off");
            script.Press("Revert changes");
            return script.Text;
        }
    }

    static string Headset()
    {
        var found = new FoundDevice
        {
            Key = "trace|headset", Name = "PRO X Wireless Gaming Headset", Kind = "headset",
            Link = "LIGHTSPEED", Index = 0xFF, Path = @"\\?\hid#vid_046d&pid_0aba&mi_03&col01",
            Features = new[] { 0x0001, 0x0003, 0x0005, 0x1F20, 0x8300 }
        };
        using (var script = Begin(() => DeviceTable.For(found).Make(found, new FakeHeadset(), null)))
        {
            script.Slide("Sidetone", 40);
            script.Pick("Turn off after", "30 min");
            script.Menu("More preset actions", "Save as new preset…");
            script.Type("Calls");
            script.Press("Save");
            script.Slide("Sidetone", 10);
            script.Press("Revert changes");
            script.Pick("Preset", "None");
            return script.Text;
        }
    }

    static IEnumerable<KeyValuePair<string, Func<string>>> All()
    {
        yield return new KeyValuePair<string, Func<string>>("g515", G515);
        yield return new KeyValuePair<string, Func<string>>("g915", G915);
        yield return new KeyValuePair<string, Func<string>>("g512x", G512X);
        yield return new KeyValuePair<string, Func<string>>("g560", G560);
        yield return new KeyValuePair<string, Func<string>>("g512", G512);
        yield return new KeyValuePair<string, Func<string>>("g213", G213);
        yield return new KeyValuePair<string, Func<string>>("g510", G510);
        yield return new KeyValuePair<string, Func<string>>("prox60", Generic);
        yield return new KeyValuePair<string, Func<string>>("litra", Litra);
        yield return new KeyValuePair<string, Func<string>>("superlight2", TraceWideDpiMouse);
        yield return new KeyValuePair<string, Func<string>>("gpro", () => Mouse("G Pro Wireless Gaming Mouse", new FakeMouse(),
            new[] { 0x0001, 0x0003, 0x0005, 0x1001, 0x2201, 0x8060, 0x8070, 0x8100 }));
        yield return new KeyValuePair<string, Func<string>>("g305", () => Mouse("G305 Lightspeed Wireless Gaming Mouse", FakeMouse.G305(),
            new[] { 0x0001, 0x0003, 0x0005, 0x1000, 0x2201, 0x8060, 0x8070, 0x8100 }));
        yield return new KeyValuePair<string, Func<string>>("wheel", Wheel);
        yield return new KeyValuePair<string, Func<string>>("buttons", Buttons);
        yield return new KeyValuePair<string, Func<string>>("g9x", G9x);
        yield return new KeyValuePair<string, Func<string>>("headset", Headset);
        yield return new KeyValuePair<string, Func<string>>("mxmaster", MxMaster);
        yield return new KeyValuePair<string, Func<string>>("mxkeys", MxKeys);
        yield return new KeyValuePair<string, Func<string>>("keypad", Keypad);
    }

    static void DeleteTestFiles()
    {
        try { File.Delete(Settings.Location); } catch (Exception) { }
        try { File.Delete(Log.File); } catch (Exception) { }
        try { if (Directory.Exists(Backups.Root)) Directory.Delete(Backups.Root, true); } catch (Exception) { }
    }

    public static List<string> Run(string folder, bool record)
    {
        Script.Timing = Environment.GetEnvironmentVariable("GLITE_TIMING") == "1";
        var failures = new List<string>();
        Directory.CreateDirectory(folder);
        foreach (var trace in All())
        {
            Fresh();
            var began = DateTime.UtcNow;
            string text;
            try { text = trace.Value(); }
            catch (Exception ex)
            {
                failures.Add(trace.Key + ": " + ex.GetType().Name + ": " + ex.Message);
                if (last != null) File.WriteAllText(Path.Combine(folder, trace.Key + ".fail.txt"), last.Text);
                continue;
            }
            finally { DeleteTestFiles(); }
            if (Script.Timing) Console.WriteLine(trace.Key + " " + (int)(DateTime.UtcNow - began).TotalMilliseconds + " ms");
            var file = Path.Combine(folder, trace.Key + ".txt");
            if (record) { File.WriteAllText(file, text); continue; }
            if (!File.Exists(file)) { failures.Add(trace.Key + ": nothing recorded yet, run build\\selftest.exe --record"); continue; }
            var want = File.ReadAllText(file).Replace("\r\n", "\n").Split('\n');
            var have = text.Split('\n');
            for (int i = 0; i < Math.Max(want.Length, have.Length); i++)
            {
                var was = i < want.Length ? want[i] : "(end)";
                var now = i < have.Length ? have[i] : "(end)";
                if (was == now) continue;
                failures.Add(trace.Key + " differs at line " + (i + 1) + "\n  was: " + was + "\n  now: " + now);
                break;
            }
        }
        return failures;
    }
}
