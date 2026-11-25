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

    // Whether a key of the visible picking picture can be picked, without picking it.
    public bool CanClick(string key)
    {
        return All<KeyPicture>().Single(picture => picture.Visible && picture.Picking).CanPress(key);
    }

    public void Click(string key)
    {
        Do("click " + key, () => All<KeyPicture>().Single(picture => picture.Visible && picture.Picking).PressKey(key));
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
            if (lineCount != count || !queue.IsCompleted || (page != null && page.Busy)) { count = lineCount; still = DateTime.UtcNow; }
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
        parts.Add("status " + (status == null ? "-" : "\"" + status.Shown + "\"" + (status.Cut ? " (cut)" : "") + (status.Error ? " (red)" : "")));
        var visibleControls = All<Control>()
            .Where(control => control.Visible)
            .OrderBy(control => Where(control).Y)
            .ThenBy(control => Where(control).X);
        foreach (var control in visibleControls)
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
            {
                var dropdown = (Dropdown)control;
                parts.Add(control.AccessibleName + "="
                    + (control.AccessibleName == "Monitor" ? "<monitor>" : control.Text == "" ? "-" : control.Text) + disabled
                    + (dropdown.Off.Length == 0
                        ? ""
                        : " (no " + string.Join(", ", dropdown.Off.Where(i => i >= 0 && i < dropdown.Items.Count).Select(i => dropdown.Items[i]))
                            + ")"));
            }
            else if (control is Slider) parts.Add(control.AccessibleName + "=" + ((Slider)control).Value + disabled);
            else if (control is Toggle) parts.Add(control.AccessibleName + "=" + (((Toggle)control).Checked ? "on" : "off") + disabled);
            else if (control is Swatch && ((Swatch)control).On) parts.Add("<" + control.AccessibleName + ">");
            else if (control is KeyPicture && ((KeyPicture)control).Picking)
                parts.Add(control.AccessibleName + "=" + (((KeyPicture)control).PickedText == "" ? "-" : ((KeyPicture)control).PickedText));
        }
        return NormalizeForTrace(string.Join(" ", parts));
    }

    static string NormalizeForTrace(string text)
    {
        text = Regex.Replace(text, @"\d{1,2} [A-Z][a-z]{2} \d{2}:\d{2}", "<when>");
        return Regex.Replace(text, @"\b\d{2}:\d{2}\b", "<time>");
    }

    // GLITE_SHOTS names a folder for a picture of each script's screen as it ends, tall enough for every row: to look at a layout
    // without the window (ShotName, set by Traces.Run, then a count).
    public static string ShotFolder, ShotName;
    static int shotCount;

    void SaveShot()
    {
        if (ShotFolder == null || Page == null) return;
        host.ClientSize = new Size(656, 1500);
        Page.Bounds = new Rectangle(0, 0, 656, 1500);
        Application.DoEvents();
        using (var image = new Bitmap(Page.Width, Page.Height))
        {
            Page.DrawToBitmap(image, new Rectangle(Point.Empty, Page.Size));
            image.Save(Path.Combine(ShotFolder, ShotName + "-" + ++shotCount + ".png"));
        }
    }

    public void Dispose()
    {
        try { SaveShot(); } catch (Exception ex) { Console.WriteLine("shot " + ShotName + ": " + ex.Message); }
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
        KeyboardPage.TestDynamicLighting = 0;
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
        KeyboardPage page = null;
        using (var script = Begin(() => page = (KeyboardPage)DeviceTable.For(found).Make(found, device)))
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
            script.Pick("G-Lite preset", "–");
            script.Pick("G-Lite preset", "Night");
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
            // A live effect sends nothing until its timer draws a frame; Cycle after it gives the keyboard its lighting back.
            script.Pick("Effect", "Rain");
            script.Pick("Effect", "Aurora");
            script.Pick("Effect", "Audio");
            script.Press("Solid color");
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
        KeyboardPage page = null;
        var keyboard = new FakeG915();
        using (var script = Begin(() => page = (KeyboardPage)DeviceTable.For(found).Make(found, keyboard)))
        {
            script.Pick("Effect", "Static");
            script.Press("Color 30D050");
            script.Slide("Brightness", 50);
            script.Menu("More preset actions", "Save as new preset…");
            script.Type("Green");
            script.Press("Save");
            script.Pick("Effect", "Breathe");
            script.Pick("G-Lite preset", "–");
            script.Pick("G-Lite preset", "Green");
            script.Pick("Dim after", "15 s");
            script.Pick("Turn off after", "10 min");
            script.Press("Save to M1");
            script.Pick("Effect", "Wave");
            script.Press("Revert");
            script.Menu("Backups", "Restore the factory profile");
            script.Pick("G-Lite preset", "Green");
            script.Pick("Onboard profile", "M2");
            script.Pick("Effect", "Wave");
            script.Do("M3 pressed on the keyboard", () => { keyboard.Running = 3; page.Heard(21, 0); });
            script.Do("the window comes to the front", () => page.CheckKeyboard());
            script.Do("the brightness key pressed", () => { keyboard.Brightness = 30; page.Heard(20, 0); });
            script.Do("quit", () => page.Exiting());
            return script.Text;
        }
    }

    // The G815's G keys: edited in the Bindings view, saved with Save to M1 (bytes 0x20-0x33 of profile 1), left out of presets.
    // Its Game mode keys: sent at once (0x4522 fn 3, then fn 1), kept in presets, cleared by –, and sent again by a new page, as after
    // the keyboard is plugged in again. Off its onboard profiles (G HUB), Save is off and the status line red, until it runs them again.
    // Its Wave also goes up (direction 7).
    static string G815()
    {
        var found = new FoundDevice
        {
            Key = "trace|g815", Name = "G815 RGB MECHANICAL GAMING KEYBOARD", Kind = "keyboard", Link = "USB", Index = 0xFF,
            Features = new[] { 0x0001, 0x0003, 0x0005, 0x4522, 0x4540, 0x8040, 0x8071, 0x8100 }
        };
        KeyboardPage page = null;
        var keyboard = new FakeG915();
        string text;
        using (var script = Begin(() => page = (KeyboardPage)DeviceTable.For(found).Make(found, keyboard)))
        {
            script.Pick("View", "Bindings");
            script.Pick("G1", "Mute");
            script.Pick("G2", "Keystroke…");
            script.Do("keys Ctrl + C", () => page.RecordedKeys(Keys.Control | Keys.C));
            script.Press("Save to M1");
            script.Do("check G1 and G2 in profile 1", () =>
            {
                var saved = keyboard.Sectors[1].Skip(0x20).Take(8).ToArray();
                if (!saved.SequenceEqual(new byte[] { 0x80, 0x03, 0x00, 0xE2, 0x80, 0x02, 0x01, 0x06 }))
                    throw new InvalidOperationException("profile 1 holds " + BitConverter.ToString(saved));
            });
            script.Pick("G3", "Left click");
            script.Menu("More preset actions", "Save as new preset…");
            script.Type("Click");
            script.Press("Save");
            script.Press("Revert");
            script.Pick("G-Lite preset", "–");
            script.Pick("G-Lite preset", "Click");
            script.Pick("G-Lite preset", "–");
            script.Menu("Backups", "Restore the factory profile");
            script.Pick("G1", "Disabled");
            script.Do("M2 pressed on the keyboard", () => { keyboard.Running = 2; page.Heard(21, 0); });
            script.Pick("View", "Game");
            script.Click("K");
            script.Click("W");
            script.Do("check the keyboard's Game mode list", () => CheckGameKeys(keyboard, 0x0E, 0x1A));
            script.Menu("More preset actions", "Save as new preset…");
            script.Type("Shooter");
            script.Press("Save");
            script.Click("K");
            script.Press("Revert");
            script.Pick("G-Lite preset", "–");
            script.Do("check the list is cleared", () => CheckGameKeys(keyboard));
            script.Pick("G-Lite preset", "Shooter");
            script.Press("Clear");
            script.Click("K");
            script.Pick("View", "Settings");
            script.Do("G HUB takes the keyboard off its onboard profiles", () => { keyboard.Modes.Add(2); page.CheckKeyboard(); });
            script.Pick("Effect", "Breathe");
            script.Do("G HUB closes", () => { keyboard.Modes.Add(1); page.CheckKeyboard(); });
            script.Pick("Effect", "Wave");
            script.Pick("Direction", "Up");
            script.Do("quit", () => page.Exiting());
            text = script.Text;
        }
        var pluggedAgain = new FakeG915();
        using (var script = Begin(() => (KeyboardPage)DeviceTable.For(found).Make(found, pluggedAgain)))
        {
            script.Do("check the Game mode list is sent again", () => CheckGameKeys(pluggedAgain, 0x0E));
            return text + script.Text;
        }
    }

    static void CheckGameKeys(FakeG915 keyboard, params int[] usages) { CheckGameKeys(keyboard.GameKeys, usages); }

    static void CheckBands(FakeHeadset headset, params int[] bands)
    {
        if (headset.Persistence != 1 || !headset.Bands.Select(band => (int)band).SequenceEqual(bands))
            throw new InvalidOperationException("the headset holds " + string.Join(" ", headset.Bands) + ", persistence " + headset.Persistence);
    }

    static void CheckGameKeys(IEnumerable<int> listed, params int[] usages)
    {
        if (!listed.SequenceEqual(usages))
            throw new InvalidOperationException("the Game mode list holds " + string.Join(" ", listed.Select(usage => usage.ToString("X2"))));
    }

    static string G512X()
    {
        var found = new FoundDevice
        {
            Key = "trace|g512x", Name = "G512 X 75", Kind = "keyboard", Link = "USB", Index = 0xFF,
            Features = new[] { 0x0001, 0x0003, 0x0005, 0x8071, 0x8081, 0x8040, 0x8101, 0x4540 }
        };
        KeyboardPage page = null;
        using (var script = Begin(() => page = (KeyboardPage)DeviceTable.For(found).Make(found, FakeG915.G512X())))
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
        KeyboardPage page = null;
        using (var script = Begin(() => page = (KeyboardPage)DeviceTable.For(found).Make(found, FakeG915.G512X())))
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
            Features = new[] { 0x0001, 0x0003, 0x4522, 0x0005, 0x4540, 0x8060, 0x8080, 0x8070 }
        };
        var keyboard = FakeSpeaker.G512();
        using (var script = Begin(() => DeviceTable.For(found).Make(found, keyboard)))
        {
            script.Pick("Effect", "Rain");
            script.Pick("Effect", "Static");
            script.Press("Color 30D050");
            script.Slide("Brightness", 50);
            script.Pick("Zone", "Logo");
            script.Pick("Effect", "Breathe");
            script.Slide("Speed", 80);
            script.Pick("Zone", "All");
            script.Pick("Effect", "Cycle");
            script.Pick("Effect", "Off");
            script.Pick("View", "Game");
            script.Click("W");
            script.Do("check the keyboard's Game mode list", () => CheckGameKeys(keyboard.GameKeys, 0x1A));
            script.Pick("View", "Settings");
            return script.Text;
        }
    }

    // The G413 Silver's white backlight: 0x8070 says monochrome, so there's no colour row, every light goes out white, and Breathe's
    // white is scaled by brightness. One zone, so no zone row. Audio sends nothing here (timers don't run in traces). Game mode keys
    // through 0x4522, as on the G815: picked, cleared, and sent again by a new page.
    static string G413()
    {
        var found = new FoundDevice
        {
            Key = "trace|g413", Name = "G413 Silver Mechanical Gaming Keyboard", Kind = "keyboard", Link = "USB", Index = 0xFF,
            Features = new[] { 0x0001, 0x0003, 0x4522, 0x0005, 0x4540, 0x8060, 0x8070, 0x40A2 }
        };
        var keyboard = FakeSpeaker.G413Silver();
        string text;
        using (var script = Begin(() => DeviceTable.For(found).Make(found, keyboard)))
        {
            script.Pick("Effect", "Static");
            script.Slide("Brightness", 50);
            script.Pick("Effect", "Breathe");
            script.Slide("Speed", 80);
            script.Pick("Effect", "Audio");
            script.Pick("Effect", "Off");
            script.Pick("View", "Game");
            script.Click("K");
            script.Click("W");
            script.Do("check the keyboard's Game mode list", () => CheckGameKeys(keyboard.GameKeys, 0x0E, 0x1A));
            script.Click("K");
            script.Pick("View", "Settings");
            text = script.Text;
        }
        var pluggedAgain = FakeSpeaker.G413Silver();
        using (var script = Begin(() => DeviceTable.For(found).Make(found, pluggedAgain)))
        {
            script.Do("check the Game mode list is sent again", () => CheckGameKeys(pluggedAgain.GameKeys, 0x1A));
            script.Pick("View", "Game");
            script.Press("Clear");
            script.Do("check the list is cleared", () => CheckGameKeys(pluggedAgain.GameKeys));
            return text + script.Text;
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
        using (var script = Begin(() => DeviceTable.For(found).Make(found, new FakeSpeaker { Count = 6, G213 = true })))
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
        using (var script = Begin(() => DeviceTable.For(found).Make(found, new FakeG510())))
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
            script.Pick("G-Lite preset", "–");
            script.Pick("View", "Bindings");
            script.Pick("G5", "Volume up");
            script.Pick("M key", "M2");
            script.Pick("G1", "Play or pause");
            script.Pick("M key", "M1");
            script.Pick("View", "Game");
            script.Pick("View", "Settings");
            script.Pick("M key", "M2");
            script.Press("Color 30D050");
            script.Pick("M key", "M1");
            script.Pick("M key", "M2");
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
        using (var script = Begin(() => DeviceTable.For(found).Make(found, new FakeSpeaker())))
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
            script.Pick("G-Lite preset", "–");
            script.Pick("G-Lite preset", "Green");
            script.Pick("Effect", "Screen");
            script.Press("Update");
            script.Slide("Color boost", 80);
            script.Press("Revert changes");
            script.Pick("G-Lite preset", "–");
            script.Pick("G-Lite preset", "Green");
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
        using (var script = Begin(() => DeviceTable.For(found).Make(found, device())))
        {
            script.Pick("Effect", "Static");
            script.Press("Update");
            text = "restart\n" + script.Text;
        }
        using (var script = Begin(() => DeviceTable.For(found).Make(found, device())))
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
        using (var script = Begin(() => DeviceTable.For(found).Make(found, new FakeLight())))
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
            script.Pick("G-Lite preset", "–");
            script.Pick("G-Lite preset", "Desk");
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
        using (var script = Begin(() => DeviceTable.For(found).Make(found, new FakeOther())))
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
        using (var script = Begin(() => DeviceTable.For(found).Make(found, new FakeOther())))
            return first + "reopen on a device that forgot\n" + script.Text;
    }

    static string MxKeys()
    {
        var found = new FoundDevice
        {
            Key = "trace|mxkeys", Name = "MX Keys S", Kind = "keyboard", Link = "Bolt receiver", Index = 3,
            Features = new[] { 0x0001, 0x0003, 0x0005, 0x1004, 0x1814, 0x1815, 0x1982, 0x40A3 }
        };
        using (var script = Begin(() => DeviceTable.For(found).Make(found, new FakeOther())))
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
        using (var script = Begin(() => DeviceTable.For(found).Make(found, new FakeOther())))
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
        using (var script = Begin(() => DeviceTable.For(found).Make(found, device)))
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
            script.Pick("G-Lite preset", "–");
            script.Menu("Backups", "Restore the factory profile");
            script.Do("the mouse's DPI button", () =>
            {
                device.PressDpiButton();
                ((MousePage)script.Page).Heard(device.FeatureIndex(0x8100), 1);
            });
            return script.Text;
        }
    }

    // The G305's lighting is live only: Save leaves the profile's lighting record alone, and G-Lite sends it again when the mouse wakes
    // (0x1D4B event 0) and when the screen loads.
    static string LiveLightMouse()
    {
        var found = new FoundDevice
        {
            Key = "trace|g305live", Name = "G305 Lightspeed Wireless Gaming Mouse", Kind = "mouse",
            Link = "LIGHTSPEED", Index = 1, WirelessId = 0x4074,
            Features = new[] { 0x0001, 0x0003, 0x0005, 0x1000, 0x1D4B, 0x2201, 0x8060, 0x8070, 0x8100 }
        };
        var device = FakeMouse.G305();
        var factoryLight = device.Sectors[MouseProtocol.RomSector]
            .Skip(MouseProtocol.LightRecordsAt).Take(MouseProtocol.LightRecordLength).ToArray();
        using (var script = Begin(() => DeviceTable.For(found).Make(found, device)))
        {
            script.Pick("Effect", "Static");
            script.Press("Color FFD000");
            script.Pick("DPI stage", "400");
            script.Press("Save to mouse");
            script.Do("the mouse reconnects, switched off and on", () => ((MousePage)script.Page).Heard(device.FeatureIndex(0x1D4B), 0));
            script.Do("the screen loads again", () => ((MousePage)script.Page).Load());
            script.Do("the mouse's DPI button", () =>
            {
                device.PressDpiButton();
                ((MousePage)script.Page).Heard(device.FeatureIndex(0x8100), 1);
            });
            var savedLight = device.Sectors[1].Skip(MouseProtocol.LightRecordsAt).Take(MouseProtocol.LightRecordLength).ToArray();
            return script.Text
                + "lighting record after the save: " + (savedLight.SequenceEqual(factoryLight) ? "unchanged" : "changed") + "\n";
        }
    }

    static string TraceWideDpiMouse()
    {
        var found = new FoundDevice
        {
            Key = "trace|superlight2", Name = "PRO X SUPERLIGHT 2", Kind = "mouse", Link = "LIGHTSPEED", Index = 1, WirelessId = 0x40A9,
            Features = new[] { 0x0001, 0x0003, 0x0005, 0x1004, 0x2202, 0x8061, 0x8100 }
        };
        var device = FakeMouse.ProX2();
        using (var script = Begin(() => DeviceTable.For(found).Make(found, device)))
        {
            script.Slide("DPI", 20);
            script.Pick("DPI stage", "1600");
            script.Pick("DPI stage", "400");
            script.Slide("DPI", 20);
            script.Pick("Report rate", "2k");
            script.Pick("View", "Bindings");
            script.Pick("Button 4", "DPI cycle");
            script.Press("Save to mouse");
            var saved = BitConverter.ToString(device.Sectors[1], 0, 0x44).Replace("-", "");
            script.Menu("Backups", "Restore the factory profile");
            return script.Text + "profile after the save: " + saved + "\n"
                + "profile after the factory restore: " + BitConverter.ToString(device.Sectors[1], 0, 0x44).Replace("-", "") + "\n";
        }
    }

    // Game mode on a G915 TKL, experimental: W picked and sent like on the G815; Fn, where the G815 has its right Windows key, isn't
    // hatched or pickable.
    static string G915Game()
    {
        var found = new FoundDevice
        {
            Key = "trace|g915game", Name = "G915 TKL LIGHTSPEED Wireless RGB Mechanical Gaming Keyboard", Kind = "keyboard",
            Link = "LIGHTSPEED", Index = 1, WirelessId = 0x408E,
            Features = new[] { 0x0001, 0x0003, 0x0005, 0x1001, 0x4522, 0x8040, 0x8071, 0x8081, 0x8100 }
        };
        var keyboard = new FakeG915();
        using (var script = Begin(() => DeviceTable.For(found).Make(found, keyboard)))
        {
            script.Pick("View", "Game");
            script.Click("W");
            script.Do("check the keyboard's Game mode list", () => CheckGameKeys(keyboard, 0x1A));
            return script.Text + "Fn pickable: " + Pickable(script, "Fn") + "\n";
        }
    }

    // The G915 TKL's F keys in the Bindings view, experimental: F1 set to Disabled and saved into slot 1 of the profile (0x20).
    // A G915 whose 0x8100 counts other than G1-G5 and M1-M3 gets no bindings.
    static string G915Keys()
    {
        var found = new FoundDevice
        {
            Key = "trace|g915keys", Name = "G915 TKL LIGHTSPEED Wireless RGB Mechanical Gaming Keyboard", Kind = "keyboard",
            Link = "LIGHTSPEED", Index = 1, WirelessId = 0x408E,
            Features = new[] { 0x0001, 0x0003, 0x0005, 0x1001, 0x8040, 0x8071, 0x8081, 0x8100 }
        };
        var keyboard = new FakeG915();
        string text;
        using (var script = Begin(() => DeviceTable.For(found).Make(found, keyboard)))
        {
            script.Pick("View", "Bindings");
            script.Pick("F1", "Disabled");
            script.Press("Save to M1");
            text = script.Text + "buttons after the save: "
                + BitConverter.ToString(keyboard.Sectors[1], 0x20, 0x3C).Replace("-", "") + "\n";
        }
        var full = new FoundDevice
        {
            Key = "trace|g915full", Name = "G915 LIGHTSPEED Wireless RGB Mechanical Gaming Keyboard", Kind = "keyboard",
            Link = "LIGHTSPEED", Index = 1, WirelessId = 0x407C,
            Features = new[] { 0x0001, 0x0003, 0x0005, 0x1001, 0x8040, 0x8071, 0x8081, 0x8100 }
        };
        using (var script = Begin(() => DeviceTable.For(full).Make(full, new FakeG915 { ButtonSlots = 15 })))
            return text + "a G915 counting 15 slots:\n" + script.Text;
    }

    // The G515's Game mode keys go out as a bitmap of its control list (0x1B10, read once), through 0x4523: A and Enter are bits 0x38
    // and 0x3E, byte 7. Kept in presets, cleared by Clear, back with Revert, and sent again by a new page.
    static string G515Game()
    {
        var found = new FoundDevice
        {
            Key = "trace|g515game", Name = "G515 LIGHTSPEED TKL", Kind = "keyboard", Link = "LIGHTSPEED", Index = 1, WirelessId = 0x40B4,
            Features = Keyboard.Concat(new[] { 0x1B10, 0x4523 }).ToArray()
        };
        var keyboard = new FakeHidpp();
        string text;
        using (var script = Begin(() => DeviceTable.For(found).Make(found, keyboard)))
        {
            script.Pick("View", "Game");
            script.Click("A");
            script.Click("Enter");
            script.Do("check the bitmap", () => CheckBitmap(keyboard.GameBitmap, 7, 0x41));
            script.Menu("More preset actions", "Save as new preset…");
            script.Type("Shooter");
            script.Press("Save");
            script.Press("Clear");
            script.Do("check the bitmap is empty", () => CheckBitmap(keyboard.GameBitmap, 7, 0));
            script.Press("Revert");
            script.Do("check the preset's keys are back", () => CheckBitmap(keyboard.GameBitmap, 7, 0x41));
            script.Do("quit", () => ((KeyboardPage)script.Page).Exiting());
            text = script.Text;
        }
        var pluggedAgain = new FakeHidpp();
        using (var script = Begin(() => DeviceTable.For(found).Make(found, pluggedAgain)))
        {
            script.Do("check the bitmap is sent again", () => CheckBitmap(pluggedAgain.GameBitmap, 7, 0x41));
            return text + script.Text;
        }
    }

    static void CheckBitmap(byte[] bitmap, int at, byte value)
    {
        if (bitmap.Where((bits, index) => index != at).Any(bits => bits != 0) || bitmap[at] != value)
            throw new InvalidOperationException("the Game mode bitmap is " + BitConverter.ToString(bitmap));
    }

    // The G915 X has 0x4523 like the G515, experimental: given the G515's control list here, W is bit 0x21, byte 4.
    static string G915XGame()
    {
        var found = new FoundDevice
        {
            Key = "trace|g915xgame", Name = "G915 X LIGHTSPEED", Kind = "keyboard", Link = "LIGHTSPEED", Index = 1, WirelessId = 0x40B5,
            Features = new[] { 0x0001, 0x0003, 0x0005, 0x1004, 0x1B10, 0x4523, 0x4540, 0x8040, 0x8071, 0x8081, 0x8101 }
        };
        var keyboard = FakeG915.G512X();
        using (var script = Begin(() => DeviceTable.For(found).Make(found, keyboard)))
        {
            script.Pick("View", "Game");
            script.Click("W");
            script.Do("check the bitmap", () => CheckBitmap(keyboard.GameBitmap, 4, 0x02));
            return script.Text;
        }
    }

    static bool Pickable(Script script, string key) { return script.CanClick(key); }

    // Format 4, the original PRO X SUPERLIGHT: no lighting (its 0x8070 is hidden), and a save writes format 3's fields and keeps
    // the LED records.
    static string Superlight()
    {
        var found = new FoundDevice
        {
            Key = "trace|superlight", Name = "PRO X Wireless", Kind = "mouse", Link = "LIGHTSPEED", Index = 1, WirelessId = 0x4093,
            Features = new[] { 0x0001, 0x0003, 0x0005, 0x1004, 0x2201, 0x8060, 0x8070, 0x8100 }
        };
        var device = FakeMouse.Superlight();
        // A preset with a light, as one saved on a mouse that has lighting: nothing to send here, so the status doesn't mention it.
        Settings.Set("profile." + found.SettingsKey + ".Lit", "800,1,FF0000,5000,100,1000");
        var lightRecords = device.Sectors[1].Skip(MouseProtocol.LightRecordsAt).Take(0xFD - MouseProtocol.LightRecordsAt).ToArray();
        using (var script = Begin(() => DeviceTable.For(found).Make(found, device)))
        {
            script.Pick("DPI stage", "800");
            script.Slide("DPI", 20);
            script.Pick("Report rate", "500");
            script.Pick("View", "Bindings");
            script.Pick("Button 4", "DPI cycle");
            script.Press("Save to mouse");
            script.Pick("G-Lite preset", "Lit");
            var savedRecords = device.Sectors[1].Skip(MouseProtocol.LightRecordsAt).Take(0xFD - MouseProtocol.LightRecordsAt).ToArray();
            return script.Text + "profile after the save: " + BitConverter.ToString(device.Sectors[1], 0, 0x34).Replace("-", "") + "\n"
                + "LED records after the save: " + (savedRecords.SequenceEqual(lightRecords) ? "unchanged" : "changed") + "\n";
        }
    }

    // Format 7: the buttons read from 0x30.
    static string Dex()
    {
        var found = new FoundDevice
        {
            Key = "trace|dex", Name = "PRO X 2 DEX", Kind = "mouse", Link = "LIGHTSPEED", Index = 1, WirelessId = 0x40B8,
            Features = new[] { 0x0001, 0x0003, 0x0005, 0x1004, 0x2202, 0x8061, 0x8100 }
        };
        using (var script = Begin(() => DeviceTable.For(found).Make(found, FakeMouse.Dex())))
        {
            script.Pick("View", "Bindings");
            return script.Text;
        }
    }

    // Nothing live: the stage, rate and colour wait for Save to mouse, which writes profile 1's report in one go and reads it back.
    static string G300()
    {
        var device = new FakeG300();
        var found = new FoundDevice
        {
            Key = "trace|g300", Name = "G300 Gaming Mouse", Kind = "mouse", Models = "USB C246", Link = "USB", Index = 0xFF,
            PlainHid = true, HasVendorCollection = true, Path = @"\\?\hid#vid_046d&pid_c246&mi_01&col03"
        };
        using (var script = Begin(() => DeviceTable.For(found).Make(found, device)))
        {
            script.Pick("DPI stage", "1500");
            script.Pick("Report rate", "500");
            script.Press("Color FFD000");
            script.Pick("View", "Bindings");
            script.Pick("View", "Settings");
            script.Press("Save to mouse");
            script.Do("check profile 1 as saved: yellow, 500 Hz, the 1500 DPI level the default, the buttons as they were", () =>
            {
                var want = FakeHidpp.Hex("F303030204860A40" + "010000020000030000" + "0A00000B00000C0000" + "040000050000" + "0D0000");
                if (!device.Profiles[0].SequenceEqual(want))
                    throw new InvalidOperationException("profile 1 holds " + BitConverter.ToString(device.Profiles[0]));
            });
            return script.Text;
        }
    }

    // Read only: feature reports F0 and the active mode's, every control off, 20 buttons in the Bindings view.
    static string G600()
    {
        var device = new FakeG600();
        var found = new FoundDevice
        {
            Key = "trace|g600", Name = "G600 Gaming Mouse", Kind = "mouse", Models = "USB C24A", Link = "USB", Index = 0xFF,
            PlainHid = true, HasVendorCollection = true, Path = @"\\?\hid#vid_046d&pid_c24a&mi_01&col03"
        };
        MousePage page = null;
        using (var script = Begin(() => page = (MousePage)DeviceTable.For(found).Make(found, device)))
        {
            script.Pick("View", "Bindings");
            script.Pick("View", "Settings");
            script.Do("mode 3 picked on the mouse, then the screen loads again", () => { device.Mode = 2; device.Level = 3; page.Load(); });
            script.Pick("View", "Bindings");
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
        MousePage page = null;
        using (var script = Begin(() => page = (MousePage)DeviceTable.For(found).Make(found, device)))
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
        MousePage page = null;
        using (var script = Begin(() => page = (MousePage)DeviceTable.For(found).Make(found, device)))
        {
            script.Pick("View", "Bindings");
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
            script.Pick("G-Lite preset", "–");
            script.Pick("Report rate", "500");
            script.Press("Save to mouse");
            script.Pick("G-Lite preset", "Keys");
            script.Pick("View", "Bindings");
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
        using (var script = Begin(() => DeviceTable.For(found).Make(found, new FakeG9x())))
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
            Features = new[] { 0x0001, 0x0003, 0x0005, 0x8310, 0x8300, 0x1F20 }
        };
        var headset = new FakeHeadset();
        using (var script = Begin(() => DeviceTable.For(found).Make(found, headset)))
        {
            script.Slide("Sidetone", 40);
            // The equalizer (experimental): 4 kHz at +4 dB, written with persistence 1, then every band flat.
            script.Slide("4 kHz", 16);
            script.Do("check the headset's bands", () => CheckBands(headset, 0, 0, 0, 0, 0, 1, 3, 4, 6, 6));
            script.Press("Flat");
            script.Do("check the bands are flat", () => CheckBands(headset, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0));
            script.Pick("Turn off after", "30 min");
            script.Menu("More preset actions", "Save as new preset…");
            script.Type("Calls");
            script.Press("Save");
            script.Slide("Sidetone", 10);
            script.Press("Revert changes");
            script.Pick("G-Lite preset", "–");
            return script.Text;
        }
    }

    static IEnumerable<KeyValuePair<string, Func<string>>> All()
    {
        yield return new KeyValuePair<string, Func<string>>("g515", G515);
        yield return new KeyValuePair<string, Func<string>>("g515game", G515Game);
        yield return new KeyValuePair<string, Func<string>>("g915", G915);
        yield return new KeyValuePair<string, Func<string>>("g815", G815);
        yield return new KeyValuePair<string, Func<string>>("g915game", G915Game);
        yield return new KeyValuePair<string, Func<string>>("g915keys", G915Keys);
        yield return new KeyValuePair<string, Func<string>>("g915xgame", G915XGame);
        yield return new KeyValuePair<string, Func<string>>("g512x", G512X);
        yield return new KeyValuePair<string, Func<string>>("g560", G560);
        yield return new KeyValuePair<string, Func<string>>("g512", G512);
        yield return new KeyValuePair<string, Func<string>>("g213", G213);
        yield return new KeyValuePair<string, Func<string>>("g413", G413);
        yield return new KeyValuePair<string, Func<string>>("g510", G510);
        yield return new KeyValuePair<string, Func<string>>("prox60", Generic);
        yield return new KeyValuePair<string, Func<string>>("litra", Litra);
        yield return new KeyValuePair<string, Func<string>>("superlight", Superlight);
        yield return new KeyValuePair<string, Func<string>>("superlight2", TraceWideDpiMouse);
        yield return new KeyValuePair<string, Func<string>>("dex", Dex);
        yield return new KeyValuePair<string, Func<string>>("g600", G600);
        yield return new KeyValuePair<string, Func<string>>("g300", G300);
        yield return new KeyValuePair<string, Func<string>>("gpro", () => Mouse("G Pro Wireless Gaming Mouse", new FakeMouse(),
            new[] { 0x0001, 0x0003, 0x0005, 0x1001, 0x2201, 0x8060, 0x8070, 0x8100 }));
        yield return new KeyValuePair<string, Func<string>>("g305", () => Mouse("G305 Lightspeed Wireless Gaming Mouse", FakeMouse.G305(),
            new[] { 0x0001, 0x0003, 0x0005, 0x1000, 0x2201, 0x8060, 0x8070, 0x8100 }));
        yield return new KeyValuePair<string, Func<string>>("g305live", LiveLightMouse);
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
        Script.ShotFolder = Environment.GetEnvironmentVariable("GLITE_SHOTS");
        if (Script.ShotFolder != null) Directory.CreateDirectory(Script.ShotFolder);
        var failures = new List<string>();
        Directory.CreateDirectory(folder);
        foreach (var trace in All())
        {
            Fresh();
            Script.ShotName = trace.Key;
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
