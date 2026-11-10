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
            var p = base.CreateParams;
            p.ExStyle |= 0x08000000 | 0x80;
            return p;
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
        host = new TraceHost { ShowInTaskbar = false, StartPosition = FormStartPosition.Manual, Location = new Point(-32000, -32000), FormBorderStyle = FormBorderStyle.None, Opacity = 0, ClientSize = new Size(656, 680) };
        try
        {
            Page = make();
            Page.Bounds = new Rectangle(0, 0, 656, 680);
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

    public void Press(string name) { Do("press " + name, () => Find<Ctl>(name).Press()); }

    public void Pick(string name, string item)
    {
        Do("pick " + name + " " + item, () =>
        {
            var seg = All<Seg>().FirstOrDefault(c => c.Visible && c.AccessibleName == name);
            if (seg != null) { seg.Pick(Index(seg.Items.ToList(), item, name)); return; }
            var drop = Find<Drop>(name);
            drop.Pick(Index(drop.Items, item, name));
        });
    }

    public void Slide(string name, int value)
    {
        Do("slide " + name + " " + value, () =>
        {
            var slider = Find<Slider>(name);
            if (!slider.Enabled) throw new InvalidOperationException(name + " can't be changed now");
            slider.Put(value);
        });
    }

    public void Type(string text) { Do("type " + text, () => All<Field>().First(f => f.Visible).Box.Text = text); }

    public void Menu(string button, string item)
    {
        Do("menu " + button + " > " + item, () =>
        {
            ContextMenuStrip menu = null;
            Ui.Popped = m => menu = m;
            try { Find<Ctl>(button).Press(); }
            finally { Ui.Popped = null; }
            if (menu == null) throw new InvalidOperationException(button + " didn't open a menu");
            var items = menu.Items.OfType<ToolStripMenuItem>().ToList();
            Add("  menu: " + Clean(string.Join(" | ", items.Select(i => i.Text + (i.Enabled ? "" : " (off)")))));
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
            var c = stack.Pop();
            if (c is T) yield return (T)c;
            foreach (Control child in c.Controls) stack.Push(child);
        }
    }

    T Find<T>(string name) where T : Control
    {
        var found = All<T>().Where(c => c.Visible && (c.AccessibleName == name || c.Text == name)).ToList();
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
            int n;
            lock (gate) n = lines.Count;
            if (n != count || !queue.IsCompleted || page != null && page.Busy) { count = n; still = DateTime.UtcNow; }
            else if ((DateTime.UtcNow - still).TotalMilliseconds > 150) return;
            if (DateTime.UtcNow > end) throw new TimeoutException("the screen didn't settle");
        }
    }

    Point Where(Control c)
    {
        var p = Point.Empty;
        for (var x = c; x != null && x != Page; x = x.Parent) p.Offset(x.Left, x.Top);
        return p;
    }

    string State()
    {
        var parts = new List<string>();
        var status = All<StatusLine>().FirstOrDefault(s => s.Visible);
        parts.Add("status " + (status == null ? "-" : "\"" + status.Shown + "\""));
        foreach (var c in All<Control>().Where(c => c.Visible).OrderBy(c => Where(c).Y).ThenBy(c => Where(c).X))
        {
            string off = c.Enabled ? "" : " (off)";
            if (c is Btn && c.Text != "") parts.Add("[" + c.Text + off + "]");
            else if (c is Seg) { var s = (Seg)c; parts.Add(c.AccessibleName + "=" + (s.Index >= 0 && s.Index < s.Items.Length ? s.Items[s.Index] : "-") + off); }
            else if (c is Drop) parts.Add(c.AccessibleName + "=" + (c.Text == "" ? "-" : c.Text) + off);
            else if (c is Slider) parts.Add(c.AccessibleName + "=" + ((Slider)c).Value + off);
            else if (c is Toggle) parts.Add(c.AccessibleName + "=" + (((Toggle)c).Checked ? "on" : "off") + off);
            else if (c is Swatch && ((Swatch)c).On) parts.Add("<" + c.AccessibleName + ">");
        }
        return Clean(string.Join(" ", parts));
    }

    static string Clean(string text)
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
        Win.TestIdle = 0;
        Presence.Set(false, false);
        Page515.Manual = true;
    }

    static Script last;

    static string G515()
    {
        var device = new FakeHidpp();
        var found = new Found { Key = "trace|g515", Name = "G515 LIGHTSPEED TKL", Kind = "keyboard", Link = "LIGHTSPEED", Index = 1, Wpid = 0x40B4, Features = Keyboard };
        Page515 page = null;
        using (var s = Begin(() => page = new Page515(found, device, null)))
        {
            s.Pick("Effect", "Static");
            s.Press("Color FF2020");
            s.Slide("Brightness", 40);
            s.Pick("Effect", "Breathe");
            s.Slide("Speed", 30);
            s.Press("Revert changes");
            s.Pick("Effect", "Static");
            s.Press("Color 3080FF");
            s.Pick("Dim after", "1 min");
            s.Press("Save to keyboard");
            s.Menu("More preset actions", "Save as new preset…");
            s.Type("Night");
            s.Press("Save");
            s.Press("Color FFD000");
            s.Pick("Preset", "None");
            s.Pick("Preset", "Night");
            s.Press("Update");
            s.Pick("Effect", "Wave");
            s.Do("idle 70 s", () => { Win.TestIdle = 70; page.Watch(); });
            s.Do("idle 4000 s", () => { Win.TestIdle = 4000; page.Watch(); });
            s.Do("back", () => { Win.TestIdle = 0; page.Watch(); });
            s.Do("lock", () => Presence.Set(true, false));
            s.Do("unlock", () => Presence.Set(false, false));
            s.Do("power cycle", () => { device.PowerCycle(); for (int i = 0; i < 6; i++) page.Watch(); });
            s.Menu("Backups", "Restore the factory lighting");
            s.Pick("Effect", "Cycle");
            s.Do("quit", () => page.Exiting());
            return s.Text;
        }
    }

    static string G915()
    {
        var found = new Found { Key = "trace|g915", Name = "G915 TKL LIGHTSPEED Wireless RGB Mechanical Gaming Keyboard", Kind = "keyboard", Link = "LIGHTSPEED", Index = 1, Wpid = 0x408E, Features = new[] { 0x0001, 0x0003, 0x0005, 0x1001, 0x8040, 0x8071, 0x8081, 0x8100 } };
        Page515 page = null;
        using (var s = Begin(() => page = new Page515(found, new FakeG915(), null)))
        {
            s.Pick("Effect", "Static");
            s.Press("Color 30D050");
            s.Slide("Brightness", 50);
            s.Menu("More preset actions", "Save as new preset…");
            s.Type("Green");
            s.Press("Save");
            s.Pick("Effect", "Breathe");
            s.Pick("Preset", "None");
            s.Pick("Preset", "Green");
            s.Do("quit", () => page.Exiting());
            return s.Text;
        }
    }

    static string Mouse(string name, FakeMouse device, int[] features)
    {
        var found = new Found { Key = "trace|" + name, Name = name, Kind = "mouse", Link = "LIGHTSPEED", Index = 1, Wpid = 0x4079, Features = features };
        using (var s = Begin(() => new PageMouse(found, device, null)))
        {
            s.Slide("DPI", 20);
            s.Pick("DPI stage", "400");
            s.Pick("Effect", "Static");
            s.Press("Color FFD000");
            s.Slide("Brightness", 60);
            s.Pick("Report rate", "500");
            s.Press("Save to mouse");
            s.Pick("Effect", "Breathe");
            s.Press("Revert changes");
            s.Menu("More preset actions", "Save as new preset…");
            s.Type("Work");
            s.Press("Save");
            s.Pick("DPI stage", "3200");
            s.Press("Update");
            s.Pick("Preset", "None");
            s.Menu("Backups", "Restore the factory profile");
            return s.Text;
        }
    }

    static string G9x()
    {
        var found = new Found { Key = "trace|g9x", Name = "G9x Laser Mouse", Kind = "mouse", Link = "USB", Index = 0, Hidpp10 = true, Path = @"\\?\hid#vid_046d&pid_c066&mi_01&col04" };
        using (var s = Begin(() => new PageMouse(found, new FakeG9x(), null)))
        {
            s.Slide("DPI", 30);
            s.Pick("Effect", "Static");
            s.Press("Color 3080FF");
            s.Pick("Report rate", "500");
            s.Press("Save to mouse");
            s.Pick("Effect", "Off");
            s.Press("Revert changes");
            return s.Text;
        }
    }

    static string Headset()
    {
        var found = new Found { Key = "trace|headset", Name = "PRO X Wireless Gaming Headset", Kind = "headset", Link = "LIGHTSPEED", Index = 0xFF, Path = @"\\?\hid#vid_046d&pid_0aba&mi_03&col01", Features = new[] { 0x0001, 0x0003, 0x0005, 0x1F20, 0x8300 } };
        using (var s = Begin(() => new PageHeadset(found, new FakeHeadset(), null)))
        {
            s.Slide("Sidetone", 40);
            s.Pick("Turn off after", "30 min");
            s.Menu("More preset actions", "Save as new preset…");
            s.Type("Calls");
            s.Press("Save");
            s.Slide("Sidetone", 10);
            s.Press("Revert changes");
            s.Pick("Preset", "None");
            return s.Text;
        }
    }

    static IEnumerable<KeyValuePair<string, Func<string>>> All()
    {
        yield return new KeyValuePair<string, Func<string>>("g515", G515);
        yield return new KeyValuePair<string, Func<string>>("g915", G915);
        yield return new KeyValuePair<string, Func<string>>("gpro", () => Mouse("G Pro Wireless Gaming Mouse", new FakeMouse(), new[] { 0x0001, 0x0003, 0x0005, 0x1001, 0x2201, 0x8060, 0x8070, 0x8100 }));
        yield return new KeyValuePair<string, Func<string>>("g305", () => Mouse("G305 Lightspeed Wireless Gaming Mouse", FakeMouse.G305(), new[] { 0x0001, 0x0003, 0x0005, 0x1000, 0x2201, 0x8060, 0x8070, 0x8100 }));
        yield return new KeyValuePair<string, Func<string>>("g9x", G9x);
        yield return new KeyValuePair<string, Func<string>>("headset", Headset);
    }

    static void Clean()
    {
        try { File.Delete(Settings.Location); } catch (Exception) { }
        try { File.Delete(Log.File); } catch (Exception) { }
        try { if (Directory.Exists(Backups.Root)) Directory.Delete(Backups.Root, true); } catch (Exception) { }
    }

    public static List<string> Run(string dir, bool record)
    {
        Script.Timing = Environment.GetEnvironmentVariable("GLITE_TIMING") == "1";
        var failures = new List<string>();
        Directory.CreateDirectory(dir);
        foreach (var trace in All())
        {
            Fresh();
            var began = DateTime.UtcNow;
            string text;
            try { text = trace.Value(); }
            catch (Exception e)
            {
                failures.Add(trace.Key + ": " + e.GetType().Name + ": " + e.Message);
                if (last != null) File.WriteAllText(Path.Combine(dir, trace.Key + ".fail.txt"), last.Text);
                continue;
            }
            finally { Clean(); }
            if (Script.Timing) Console.WriteLine(trace.Key + " " + (int)(DateTime.UtcNow - began).TotalMilliseconds + " ms");
            var file = Path.Combine(dir, trace.Key + ".txt");
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
