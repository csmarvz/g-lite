using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

class Zone
{
    public string Name;
    public int Mark;
    public RectangleF Area;
}

class ScreenSetup
{
    public static readonly int[] Marks = { 0x3B82F6, 0x8B6CF0, 0xE5533D, 0xF29D38, 0x2FB57D, 0x22B8C8, 0xE0559A };
    public const int MaxZones = 5;
    public string Monitor = "";
    public List<Zone> Zones = Defaults();
    public Dictionary<int, int> Keys = new Dictionary<int, int>();
    public const int BatteryLimit = 20;
    public int Boost = 33, Interval = 100, Bright = 100;
    public bool Smooth = true, PauseOnBattery = true, PauseWhenLocked = true;

    public static List<Zone> Defaults()
    {
        var names = new[] { "Left", "Middle left", "Middle right", "Right" };
        return names.Select((name, i) => new Zone { Name = name, Mark = Marks[i], Area = new RectangleF(i * 0.25f, 0.5f, 0.25f, 0.35f) }).ToList();
    }

    public int ZoneOf(int id, float x)
    {
        int zone;
        if (id >= 0 && Keys.TryGetValue(id, out zone) && zone < Zones.Count) return zone;
        return Math.Max(0, Math.Min(Zones.Count - 1, (int)(x * Zones.Count)));
    }

    public void Reset()
    {
        Zones = Defaults();
        Keys.Clear();
    }

    public int Add()
    {
        if (Zones.Count >= MaxZones) return -1;
        int mark = Marks.First(m => Zones.All(z => z.Mark != m));
        Zones.Add(new Zone { Name = "Zone " + (Zones.Count + 1), Mark = mark, Area = new RectangleF(0.375f, 0.3f, 0.25f, 0.3f) });
        return Zones.Count - 1;
    }

    public void Remove(int index)
    {
        if (Zones.Count <= 1 || index < 0 || index >= Zones.Count) return;
        Zones.RemoveAt(index);
        foreach (var id in Keys.Keys.ToList())
        {
            int zone = Keys[id];
            if (zone == index) Keys[id] = Math.Max(0, index - 1);
            else if (zone > index) Keys[id] = zone - 1;
        }
    }

    static string F(float v) { return v.ToString("0.###", CultureInfo.InvariantCulture); }

    public string ToText()
    {
        return string.Join(";",
            "mon:" + Monitor,
            "boost:" + Boost,
            "ms:" + Interval,
            "bright:" + Bright,
            "smooth:" + (Smooth ? 1 : 0),
            "bat:" + (PauseOnBattery ? 1 : 0),
            "lock:" + (PauseWhenLocked ? 1 : 0),
            "zones:" + string.Join("/", Zones.Select(z => z.Name + "," + z.Mark.ToString("X6") + "," + F(z.Area.X) + "," + F(z.Area.Y) + "," + F(z.Area.Width) + "," + F(z.Area.Height))),
            "keys:" + string.Join(",", Keys.OrderBy(k => k.Key).Select(k => k.Key.ToString("X2") + "." + k.Value)));
    }

    public static ScreenSetup FromText(string text)
    {
        var s = new ScreenSetup();
        if (string.IsNullOrEmpty(text)) return s;
        try
        {
            foreach (var part in text.Split(';'))
            {
                int colon = part.IndexOf(':');
                if (colon < 0) continue;
                string key = part.Substring(0, colon), value = part.Substring(colon + 1);
                var bits = value.Split(':');
                switch (key)
                {
                    case "mon": s.Monitor = value; break;
                    case "boost": s.Boost = Clamp(int.Parse(value), 0, 100); break;
                    case "bright": s.Bright = Clamp(int.Parse(value), 1, 100); break;
                    case "ms": s.Interval = Clamp(int.Parse(value) / 100 * 100, 100, 1000); break;
                    case "smooth": s.Smooth = value == "1"; break;
                    case "bat": s.PauseOnBattery = bits[0] == "1"; break;
                    case "lock": s.PauseWhenLocked = value == "1"; break;
                    case "zones":
                        var zones = new List<Zone>();
                        foreach (var z in value.Split('/'))
                        {
                            var f = z.Split(',');
                            if (f.Length < 6) continue;
                            Func<int, float> n = i => Math.Max(0, Math.Min(1, float.Parse(f[i], CultureInfo.InvariantCulture)));
                            zones.Add(new Zone { Name = f[0], Mark = int.Parse(f[1], NumberStyles.HexNumber), Area = new RectangleF(n(2), n(3), n(4), n(5)) });
                        }
                        if (zones.Count > 0) s.Zones = zones.Take(Marks.Length).ToList();
                        break;
                    case "keys":
                        foreach (var k in value.Split(',').Where(k => k.Contains('.')))
                        {
                            var f = k.Split('.');
                            s.Keys[int.Parse(f[0], NumberStyles.HexNumber)] = int.Parse(f[1]);
                        }
                        break;
                }
            }
        }
        catch (Exception) { return new ScreenSetup(); }
        return s;
    }

    static int Clamp(int v, int low, int high) { return Math.Max(low, Math.Min(high, v)); }
}

static class ScreenCapture
{
    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr window);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr window, IntPtr context);
    [DllImport("gdi32.dll")] static extern int SetStretchBltMode(IntPtr context, int mode);
    [DllImport("gdi32.dll")] static extern bool SetBrushOrgEx(IntPtr context, int x, int y, IntPtr previous);
    [DllImport("gdi32.dll")] static extern bool StretchBlt(IntPtr target, int x, int y, int width, int height, IntPtr source, int sourceX, int sourceY, int sourceWidth, int sourceHeight, int operation);

    public static Screen Find(string monitor)
    {
        return Screen.AllScreens.FirstOrDefault(s => monitor != "" && s.DeviceName == monitor) ?? Screen.PrimaryScreen;
    }

    public static bool Missing(string monitor) { return monitor != "" && Screen.AllScreens.All(s => s.DeviceName != monitor); }

    public static string Label(Screen screen)
    {
        int number = Array.IndexOf(Screen.AllScreens, screen) + 1;
        return (screen.Primary ? "Main" : "Monitor " + number) + " · " + screen.Bounds.Width + " × " + screen.Bounds.Height;
    }

    public static Bitmap Shrink(Rectangle source, int width, int height)
    {
        var bitmap = new Bitmap(Math.Max(1, width), Math.Max(1, height), PixelFormat.Format32bppRgb);
        if (source.Width <= 0 || source.Height <= 0) return bitmap;
        using (var g = Graphics.FromImage(bitmap))
        {
            IntPtr target = g.GetHdc(), screen = GetDC(IntPtr.Zero);
            SetStretchBltMode(target, 4);
            SetBrushOrgEx(target, 0, 0, IntPtr.Zero);
            StretchBlt(target, 0, 0, bitmap.Width, bitmap.Height, screen, source.X, source.Y, source.Width, source.Height, 0x00CC0020);
            ReleaseDC(IntPtr.Zero, screen);
            g.ReleaseHdc(target);
        }
        return bitmap;
    }

    public static Rectangle Pixels(Rectangle monitor, RectangleF area)
    {
        return new Rectangle(monitor.X + (int)(area.X * monitor.Width), monitor.Y + (int)(area.Y * monitor.Height),
            Math.Max(1, (int)(area.Width * monitor.Width)), Math.Max(1, (int)(area.Height * monitor.Height)));
    }

    public static int Average(Bitmap bitmap)
    {
        var data = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppRgb);
        var row = new byte[data.Stride];
        long red = 0, green = 0, blue = 0, count = 0;
        for (int y = 0; y < bitmap.Height; y++)
        {
            Marshal.Copy(data.Scan0 + y * data.Stride, row, 0, data.Stride);
            for (int x = 0; x < bitmap.Width; x++) { blue += row[x * 4]; green += row[x * 4 + 1]; red += row[x * 4 + 2]; count++; }
        }
        bitmap.UnlockBits(data);
        return (int)(red / count) << 16 | (int)(green / count) << 8 | (int)(blue / count);
    }

    public static int[] Sample(Rectangle monitor, List<Zone> zones)
    {
        return zones.Select(z => { using (var b = Shrink(Pixels(monitor, z.Area), 8, 8)) return Average(b); }).ToArray();
    }

    public static int Boost(int color, int boost)
    {
        double r = color >> 16 & 0xFF, g = color >> 8 & 0xFF, b = color & 0xFF, mean = (r + g + b) / 3, factor = 1 + boost / 50.0;
        r = Math.Max(0, Math.Min(255, mean + (r - mean) * factor));
        g = Math.Max(0, Math.Min(255, mean + (g - mean) * factor));
        b = Math.Max(0, Math.Min(255, mean + (b - mean) * factor));
        double max = Math.Max(r, Math.Max(g, b));
        if (max < 12) return 0;
        return (int)(r * 255 / max) << 16 | (int)(g * 255 / max) << 8 | (int)(b * 255 / max);
    }

    public static int Blend(int from, int to, double t)
    {
        Func<int, int> mix = shift => (int)Math.Round((from >> shift & 0xFF) + ((to >> shift & 0xFF) - (from >> shift & 0xFF)) * t);
        return mix(16) << 16 | mix(8) << 8 | mix(0);
    }

    public static int Distance(int first, int second)
    {
        return Math.Abs((first >> 16 & 0xFF) - (second >> 16 & 0xFF)) + Math.Abs((first >> 8 & 0xFF) - (second >> 8 & 0xFF)) + Math.Abs((first & 0xFF) - (second & 0xFF));
    }
}

static class KeyLights
{
    public static HashSet<int> Present(Hidpp h)
    {
        byte feature = h.F(0x8081);
        var present = new HashSet<int>();
        for (int page = 0; page < 3; page++)
        {
            var bits = h.Call(feature, 0, 0, (byte)page, 0);
            for (int i = 0; i < 14; i++)
                for (int bit = 0; bit < 8; bit++)
                    if ((bits[2 + i] >> bit & 1) != 0) present.Add(page * 112 + i * 8 + bit);
        }
        return present;
    }

    public static void Send(Hidpp h, Dictionary<int, List<int>> idsByColor)
    {
        byte feature = h.F(0x8081);
        foreach (var group in idsByColor)
            for (int start = 0; start < group.Value.Count; start += 13)
            {
                var parameters = new List<byte> { (byte)(group.Key >> 16), (byte)(group.Key >> 8), (byte)group.Key };
                parameters.AddRange(group.Value.Skip(start).Take(13).Select(id => (byte)id));
                h.Quiet(feature, 6, parameters.ToArray());
            }
        h.Quiet(feature, 7, 0, 0, 0, 0, 0);
    }
}

static class Presence
{
    static readonly Guid DisplayState = new Guid("6fe69556-704a-47a0-8f24-c28d936fda47");
    public static bool Locked, DisplayOff;
    public static event Action Changed;

    [DllImport("user32.dll")] static extern IntPtr RegisterPowerSettingNotification(IntPtr recipient, ref Guid setting, int flags);

    public static void Watch(IntPtr window)
    {
        var guid = DisplayState;
        RegisterPowerSettingNotification(window, ref guid, 0);
        SystemEvents.SessionSwitch += (s, e) =>
        {
            if (e.Reason == SessionSwitchReason.SessionLock) Set(true, DisplayOff);
            if (e.Reason == SessionSwitchReason.SessionUnlock) Set(false, DisplayOff);
        };
    }

    public static void Power(Message m)
    {
        if (m.Msg != 0x0218 || (int)m.WParam != 0x8013 || m.LParam == IntPtr.Zero) return;
        var setting = (Guid)Marshal.PtrToStructure(m.LParam, typeof(Guid));
        if (setting != DisplayState) return;
        int state = Marshal.ReadInt32(m.LParam, 20);
        Set(Locked, state == 0);
    }

    public static void Set(bool locked, bool off)
    {
        if (locked == Locked && off == DisplayOff) return;
        Locked = locked;
        DisplayOff = off;
        if (Changed != null) Changed();
    }
}

class ScreenPreview : Ctl
{
    public Bitmap Image;
    public Size Monitor = new Size(16, 9);
    public List<Zone> Zones = new List<Zone>();
    public int Selected;
    public event Action<int> Picked;
    public event Action Moved, Done;
    int grabbed = -1, edges;
    RectangleF start;
    Point from;

    public ScreenPreview() { AccessibleRole = AccessibleRole.Graphic; AccessibleName = "Screen zones"; }

    protected override bool Clicks { get { return false; } }

    RectangleF Picture()
    {
        float scale = Math.Min((float)Width / Monitor.Width, (float)Height / Monitor.Height);
        float w = Monitor.Width * scale, h = Monitor.Height * scale;
        return new RectangleF((Width - w) / 2, (Height - h) / 2, w, h);
    }

    RectangleF Box(Zone z)
    {
        var p = Picture();
        return new RectangleF(p.X + z.Area.X * p.Width, p.Y + z.Area.Y * p.Height, z.Area.Width * p.Width, z.Area.Height * p.Height);
    }

    int Edges(RectangleF r, Point at)
    {
        float near = 6 * K;
        if (!RectangleF.Inflate(r, near, near).Contains(at)) return -1;
        int e = 0;
        if (Math.Abs(at.X - r.Left) <= near) e |= 1;
        if (Math.Abs(at.X - r.Right) <= near) e |= 2;
        if (Math.Abs(at.Y - r.Top) <= near) e |= 4;
        if (Math.Abs(at.Y - r.Bottom) <= near) e |= 8;
        return e;
    }

    int Hit(Point at, out int found)
    {
        found = 0;
        var order = Enumerable.Range(0, Zones.Count).OrderBy(i => i == Selected ? 0 : 1).ToList();
        foreach (int i in order)
        {
            int e = Edges(Box(Zones[i]), at);
            if (e >= 0 && (e != 0 || Box(Zones[i]).Contains(at))) { found = e; return i; }
        }
        return -1;
    }

    static Cursor CursorFor(int e)
    {
        if (e == 5 || e == 10) return Cursors.SizeNWSE;
        if (e == 6 || e == 9) return Cursors.SizeNESW;
        if ((e & 3) != 0) return Cursors.SizeWE;
        if ((e & 12) != 0) return Cursors.SizeNS;
        return Cursors.SizeAll;
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        if (e.Button != MouseButtons.Left) return;
        int found, i = Hit(e.Location, out found);
        if (i < 0) return;
        grabbed = i;
        edges = found;
        start = Zones[i].Area;
        from = e.Location;
        if (i != Selected) { Selected = i; if (Picked != null) Picked(i); }
        Invalidate();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (grabbed < 0)
        {
            int found, i = Hit(e.Location, out found);
            Cursor = i < 0 ? Cursors.Default : CursorFor(found);
            return;
        }
        var p = Picture();
        float dx = (e.X - from.X) / p.Width, dy = (e.Y - from.Y) / p.Height, min = 0.03f;
        float left = start.Left, top = start.Top, right = start.Right, bottom = start.Bottom;
        if (edges == 0)
        {
            dx = Math.Max(-left, Math.Min(1 - right, dx));
            dy = Math.Max(-top, Math.Min(1 - bottom, dy));
            left += dx; right += dx; top += dy; bottom += dy;
        }
        else
        {
            if ((edges & 1) != 0) left = Math.Max(0, Math.Min(right - min, left + dx));
            if ((edges & 2) != 0) right = Math.Min(1, Math.Max(left + min, right + dx));
            if ((edges & 4) != 0) top = Math.Max(0, Math.Min(bottom - min, top + dy));
            if ((edges & 8) != 0) bottom = Math.Min(1, Math.Max(top + min, bottom + dy));
        }
        Zones[grabbed].Area = RectangleF.FromLTRB(left, top, right, bottom);
        Invalidate();
        if (Moved != null) Moved();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (grabbed < 0) return;
        grabbed = -1;
        if (Done != null) Done();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (Selected < 0 || Selected >= Zones.Count) return;
        float step = 0.01f, dx = e.KeyCode == Keys.Left ? -step : e.KeyCode == Keys.Right ? step : 0, dy = e.KeyCode == Keys.Up ? -step : e.KeyCode == Keys.Down ? step : 0;
        if (e.KeyCode == Keys.Tab && e.Control) return;
        if (dx == 0 && dy == 0) return;
        e.Handled = true;
        var a = Zones[Selected].Area;
        a.X = Math.Max(0, Math.Min(1 - a.Width, a.X + dx));
        a.Y = Math.Max(0, Math.Min(1 - a.Height, a.Y + dy));
        Zones[Selected].Area = a;
        Invalidate();
        if (Moved != null) Moved();
        if (Done != null) Done();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = Begin(e);
        var p = Picture();
        using (var b = new SolidBrush(Theme.Bed)) g.FillRectangle(b, p);
        if (Image != null)
        {
            g.InterpolationMode = InterpolationMode.HighQualityBilinear;
            g.DrawImage(Image, p);
        }
        var font = Ui.Get(7.5f, Ui.Semi);
        for (int i = 0; i < Zones.Count; i++)
        {
            if (i == Selected) continue;
            Draw(g, Zones[i], false, font);
        }
        if (Selected >= 0 && Selected < Zones.Count) Draw(g, Zones[Selected], true, font);
        if (Focused && ShowFocusCues) using (var pen = new Pen(Theme.Text, 1.5f * K)) g.DrawRectangle(pen, p.X + K, p.Y + K, p.Width - 2 * K, p.Height - 2 * K);
    }

    void Draw(Graphics g, Zone z, bool on, Font font)
    {
        var r = Box(z);
        var mark = Color.FromArgb(z.Mark >> 16 & 0xFF, z.Mark >> 8 & 0xFF, z.Mark & 0xFF);
        using (var fill = new SolidBrush(Color.FromArgb(on ? 40 : 18, 255, 255, 255))) g.FillRectangle(fill, r);
        if (on) using (var ring = new Pen(Theme.Accent, 2 * K)) g.DrawRectangle(ring, r.X - 2 * K, r.Y - 2 * K, r.Width + 4 * K, r.Height + 4 * K);
        using (var pen = new Pen(mark, (on ? 2.5f : 1.5f) * K))
        {
            if (!on) pen.DashStyle = DashStyle.Dash;
            g.DrawRectangle(pen, r.X, r.Y, r.Width, r.Height);
        }
        var size = TextRenderer.MeasureText(g, z.Name, font, Size.Empty, TextFormatFlags.NoPadding);
        var tag = new RectangleF(r.X + 2 * K, r.Y + 2 * K, Math.Min(r.Width - 4 * K, size.Width + 6 * K), size.Height + 2 * K);
        if (tag.Width < 8 * K || tag.Height > r.Height) return;
        using (var b = new SolidBrush(mark)) g.FillRectangle(b, tag);
        Ui.Draw(g, z.Name, font, new RectangleF(tag.X + 3 * K, tag.Y + K, tag.Width - 4 * K, tag.Height), Color.FromArgb(16, 16, 16), TextFormatFlags.EndEllipsis);
    }
}

class ZoneList : Ctl
{
    public List<Zone> Zones = new List<Zone>();
    public Func<int, int> Count = i => 0;
    public int Selected;
    public event Action<int> Picked, Removed;

    public ZoneList() { AccessibleRole = AccessibleRole.List; AccessibleName = "Zones"; }

    protected override bool Clicks { get { return false; } }

    float Row { get { return 22 * K; } }

    RectangleF Cross(int i) { return new RectangleF(Width - Row, i * Row, Row, Row); }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        int i = (int)(e.Y / Row);
        if (i < 0 || i >= Zones.Count) return;
        if (i == Selected && Zones.Count > 1 && Cross(i).Contains(e.Location)) { if (Removed != null) Removed(i); return; }
        Selected = i;
        Invalidate();
        if (Picked != null) Picked(i);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        int to = Selected + (e.KeyCode == Keys.Down ? 1 : e.KeyCode == Keys.Up ? -1 : 0);
        if (e.KeyCode == Keys.Delete && Zones.Count > 1) { e.Handled = true; if (Removed != null) Removed(Selected); return; }
        if (to == Selected || to < 0 || to >= Zones.Count) return;
        e.Handled = true;
        Selected = to;
        Invalidate();
        if (Picked != null) Picked(to);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = Begin(e);
        var font = Ui.Get(9f, Ui.Regular);
        for (int i = 0; i < Zones.Count; i++)
        {
            var r = new RectangleF(0.5f, i * Row + 1, Width - 1, Row - 2);
            bool on = i == Selected;
            Ui.Fill(g, r, 4 * K, on ? Theme.AccentBg : Theme.Btn, on ? Theme.Accent : Theme.Btn);
            var z = Zones[i];
            using (var b = new SolidBrush(Color.FromArgb(z.Mark >> 16 & 0xFF, z.Mark >> 8 & 0xFF, z.Mark & 0xFF)))
                g.FillRectangle(b, 7 * K, r.Y + (r.Height - 9 * K) / 2, 9 * K, 9 * K);
            float right = on && Zones.Count > 1 ? Row : 6 * K;
            Ui.Draw(g, Count(i).ToString(), Ui.Get(8.25f, Ui.Regular), new RectangleF(0, r.Y, Width - right - 2 * K, r.Height), Theme.Dim, TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
            Ui.Draw(g, z.Name, font, new RectangleF(22 * K, r.Y, Width - 22 * K - right - 26 * K, r.Height), Theme.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            if (on && Zones.Count > 1) Icons.Draw(g, "close", new RectangleF(Width - Row + 5 * K, r.Y + (r.Height - 12 * K) / 2, 12 * K, 12 * K), Theme.Dim);
        }
        if (Focused && ShowFocusCues && Selected >= 0 && Selected < Zones.Count)
            using (var pen = new Pen(Theme.Text, 1.5f * K)) g.DrawRectangle(pen, 1, Selected * Row + 1, Width - 3, Row - 3);
    }
}
