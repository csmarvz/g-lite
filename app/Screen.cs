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
    public int MarkColor;
    public RectangleF Area;
}

class ScreenSettings
{
    public static readonly int[] MarkColors = { 0x3B82F6, 0x8B6CF0, 0xE5533D, 0xF29D38, 0x2FB57D, 0x22B8C8, 0xE0559A };
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
        return names
            .Select((name, i) => new Zone { Name = name, MarkColor = MarkColors[i], Area = new RectangleF(i * 0.25f, 0.5f, 0.25f, 0.35f) })
            .ToList();
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
        int mark = MarkColors.First(color => Zones.All(zone => zone.MarkColor != color));
        var right = Zones.Count > 0 ? Zones[Zones.Count - 1].Area : new RectangleF(0, 0.5f, 1, 0.35f);
        Zones.Add(new Zone { Name = "Zone " + (Zones.Count + 1), MarkColor = mark, Area = right });
        // The zones become equal strips left to right, the new one at the right, each keeping its height.
        for (int i = 0; i < Zones.Count; i++)
        {
            var area = Zones[i].Area;
            Zones[i].Area = new RectangleF((float)i / Zones.Count, area.Y, 1f / Zones.Count, area.Height);
        }
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

    static string FormatNumber(float value) { return value.ToString("0.###", CultureInfo.InvariantCulture); }

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
            "zones:" + string.Join("/", Zones.Select(zone => zone.Name + "," + zone.MarkColor.ToString("X6")
                + "," + FormatNumber(zone.Area.X) + "," + FormatNumber(zone.Area.Y)
                + "," + FormatNumber(zone.Area.Width) + "," + FormatNumber(zone.Area.Height))),
            "keys:" + string.Join(",", Keys.OrderBy(pair => pair.Key).Select(pair => pair.Key.ToString("X2") + "." + pair.Value)));
    }

    public static ScreenSettings FromText(string text)
    {
        var setup = new ScreenSettings();
        if (string.IsNullOrEmpty(text)) return setup;
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
                    case "mon": setup.Monitor = value; break;
                    case "boost": setup.Boost = Clamp(int.Parse(value), 0, 100); break;
                    case "bright": setup.Bright = Clamp(int.Parse(value), 1, 100); break;
                    case "ms": setup.Interval = Clamp(int.Parse(value) / 100 * 100, 100, 1000); break;
                    case "smooth": setup.Smooth = value == "1"; break;
                    case "bat": setup.PauseOnBattery = bits[0] == "1"; break;
                    case "lock": setup.PauseWhenLocked = value == "1"; break;
                    case "zones":
                        var zones = new List<Zone>();
                        foreach (var zoneText in value.Split('/'))
                        {
                            var fields = zoneText.Split(',');
                            if (fields.Length < 6) continue;
                            Func<int, float> fraction = i => Math.Max(0, Math.Min(1, float.Parse(fields[i], CultureInfo.InvariantCulture)));
                            zones.Add(new Zone
                            {
                                Name = fields[0], MarkColor = int.Parse(fields[1], NumberStyles.HexNumber),
                                Area = new RectangleF(fraction(2), fraction(3), fraction(4), fraction(5))
                            });
                        }
                        if (zones.Count > 0) setup.Zones = zones.Take(MarkColors.Length).ToList();
                        break;
                    case "keys":
                        foreach (var entry in value.Split(',').Where(entry => entry.Contains('.')))
                        {
                            var parts = entry.Split('.');
                            setup.Keys[int.Parse(parts[0], NumberStyles.HexNumber)] = int.Parse(parts[1]);
                        }
                        break;
                }
            }
        }
        catch (Exception) { return new ScreenSettings(); }
        return setup;
    }

    static int Clamp(int value, int low, int high) { return Math.Max(low, Math.Min(high, value)); }
}

static class ScreenCapture
{
    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr window);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr window, IntPtr context);
    [DllImport("gdi32.dll")] static extern int SetStretchBltMode(IntPtr context, int mode);
    [DllImport("gdi32.dll")] static extern bool SetBrushOrgEx(IntPtr context, int x, int y, IntPtr previous);
    [DllImport("gdi32.dll")]
    static extern bool StretchBlt(IntPtr target, int x, int y, int width, int height,
        IntPtr source, int sourceX, int sourceY, int sourceWidth, int sourceHeight, int operation);

    public static Screen Find(string monitor)
    {
        return Screen.AllScreens.FirstOrDefault(screen => monitor != "" && screen.DeviceName == monitor) ?? Screen.PrimaryScreen;
    }

    public static bool Missing(string monitor) { return monitor != "" && Screen.AllScreens.All(screen => screen.DeviceName != monitor); }

    public static string Label(Screen screen)
    {
        int number = Array.IndexOf(Screen.AllScreens, screen) + 1;
        return (screen.Primary ? "Main" : "Monitor " + number) + ", " + screen.Bounds.Width + " × " + screen.Bounds.Height;
    }

    public static Bitmap Shrink(Rectangle source, int width, int height)
    {
        var bitmap = new Bitmap(Math.Max(1, width), Math.Max(1, height), PixelFormat.Format32bppRgb);
        if (source.Width <= 0 || source.Height <= 0) return bitmap;
        using (var graphics = Graphics.FromImage(bitmap))
        {
            IntPtr target = graphics.GetHdc(), screen = GetDC(IntPtr.Zero);
            SetStretchBltMode(target, 4);
            SetBrushOrgEx(target, 0, 0, IntPtr.Zero);
            StretchBlt(target, 0, 0, bitmap.Width, bitmap.Height, screen, source.X, source.Y, source.Width, source.Height, 0x00CC0020);
            ReleaseDC(IntPtr.Zero, screen);
            graphics.ReleaseHdc(target);
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
        return zones.Select(zone => { using (var shrunk = Shrink(Pixels(monitor, zone.Area), 8, 8)) return Average(shrunk); }).ToArray();
    }

    public static int Boost(int color, int boost)
    {
        double red = color >> 16 & 0xFF, green = color >> 8 & 0xFF, blue = color & 0xFF,
            mean = (red + green + blue) / 3, factor = 1 + boost / 50.0;
        red = Math.Max(0, Math.Min(255, mean + (red - mean) * factor));
        green = Math.Max(0, Math.Min(255, mean + (green - mean) * factor));
        blue = Math.Max(0, Math.Min(255, mean + (blue - mean) * factor));
        double max = Math.Max(red, Math.Max(green, blue));
        if (max < 12) return 0;
        return (int)(red * 255 / max) << 16 | (int)(green * 255 / max) << 8 | (int)(blue * 255 / max);
    }

    public static int Blend(int from, int to, double fraction)
    {
        Func<int, int> mix = shift => (int)Math.Round((from >> shift & 0xFF) + ((to >> shift & 0xFF) - (from >> shift & 0xFF)) * fraction);
        return mix(16) << 16 | mix(8) << 8 | mix(0);
    }

    public static int Distance(int first, int second)
    {
        return Math.Abs((first >> 16 & 0xFF) - (second >> 16 & 0xFF))
            + Math.Abs((first >> 8 & 0xFF) - (second >> 8 & 0xFF))
            + Math.Abs((first & 0xFF) - (second & 0xFF));
    }
}

static class KeyLights
{
    static readonly HashSet<string> waitedOn = new HashSet<string>();

    // fn 7 shows the frame. A real G515 answered it with error 08, busy, during Rain at a frame every 40 ms (a full frame took it
    // 30–39 ms over LIGHTSPEED, docs/screen.md): busy means try again, so it's sent again for up to about 200 ms. The first wait per
    // keyboard is logged; keyboards on their own device threads share the set, hence the lock.
    public static void ShowFrame(Hidpp hidpp, byte feature)
    {
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                hidpp.CallUnlogged(feature, 7, 0, 0, 0, 0, 0);
                if (attempt > 1 && hidpp.DeviceLabel != null)
                {
                    bool first;
                    lock (waitedOn) first = waitedOn.Add(hidpp.DeviceLabel);
                    if (first)
                        Log.Add(hidpp.DeviceLabel, "sent", "Per-key frame shown after the keyboard was busy " + (attempt - 1) + " time"
                            + (attempt == 2 ? "" : "s"), "ok");
                }
                return;
            }
            catch (HidppError ex)
            {
                if (ex.Code != 8 || attempt >= 20) throw;
                System.Threading.Thread.Sleep(10);
            }
        }
    }

    public static HashSet<int> Present(Hidpp hidpp)
    {
        byte feature = hidpp.FeatureIndex(0x8081);
        var present = new HashSet<int>();
        for (int page = 0; page < 3; page++)
        {
            var bits = hidpp.Call(feature, 0, 0, (byte)page, 0);
            for (int i = 0; i < 14; i++)
                for (int bit = 0; bit < 8; bit++)
                    if ((bits[2 + i] >> bit & 1) != 0) present.Add(page * 112 + i * 8 + bit);
        }
        return present;
    }

    public static void Send(Hidpp hidpp, Dictionary<int, List<int>> idsByColor)
    {
        byte feature = hidpp.FeatureIndex(0x8081);
        foreach (var group in idsByColor)
            for (int start = 0; start < group.Value.Count; start += 13)
            {
                var parameters = new List<byte> { (byte)(group.Key >> 16), (byte)(group.Key >> 8), (byte)group.Key };
                parameters.AddRange(group.Value.Skip(start).Take(13).Select(id => (byte)id));
                hidpp.CallUnlogged(feature, 6, parameters.ToArray());
            }
        ShowFrame(hidpp, feature);
    }

    // A frame of colours by lighting id, for the live effects: keys sharing a colour go three or more to a message (fn 6, up to 13),
    // the others four to a message (fn 1: id, red, green, blue each; a short message repeats its last key), then fn 7 shows the frame.
    // individual turns false when the keyboard refuses fn 1, and fn 6 takes every key from then on.
    public static void SendColors(Hidpp hidpp, Dictionary<int, int> colorById, ref bool individual)
    {
        byte feature = hidpp.FeatureIndex(0x8081);
        var singles = new List<KeyValuePair<int, int>>();
        var groups = new Dictionary<int, List<int>>();
        foreach (var group in colorById.GroupBy(pair => pair.Value))
        {
            if (individual && group.Count() < 3) singles.AddRange(group);
            else groups[group.Key] = group.Select(pair => pair.Key).ToList();
        }
        for (int start = 0; start < singles.Count && individual; start += 4)
        {
            var batch = singles.Skip(start).Take(4).ToList();
            while (batch.Count < 4) batch.Add(batch[batch.Count - 1]);
            var parameters = batch.SelectMany(pair => new[] { (byte)pair.Key, (byte)(pair.Value >> 16), (byte)(pair.Value >> 8), (byte)pair.Value });
            try { hidpp.CallUnlogged(feature, 1, parameters.ToArray()); }
            catch (HidppError)
            {
                individual = false;
                foreach (var pair in singles.Skip(start))
                {
                    List<int> list;
                    if (!groups.TryGetValue(pair.Value, out list)) groups[pair.Value] = list = new List<int>();
                    list.Add(pair.Key);
                }
            }
        }
        foreach (var group in groups)
            for (int start = 0; start < group.Value.Count; start += 13)
            {
                var parameters = new List<byte> { (byte)(group.Key >> 16), (byte)(group.Key >> 8), (byte)group.Key };
                parameters.AddRange(group.Value.Skip(start).Take(13).Select(id => (byte)id));
                hidpp.CallUnlogged(feature, 6, parameters.ToArray());
            }
        ShowFrame(hidpp, feature);
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

    public static void Power(Message message)
    {
        if (message.Msg != 0x0218 || (int)message.WParam != 0x8013 || message.LParam == IntPtr.Zero) return;
        var setting = (Guid)Marshal.PtrToStructure(message.LParam, typeof(Guid));
        if (setting != DisplayState) return;
        int state = Marshal.ReadInt32(message.LParam, 20);
        Set(Locked, state == 0);
    }

    public static void Set(bool locked, bool displayOff)
    {
        if (locked == Locked && displayOff == DisplayOff) return;
        Locked = locked;
        DisplayOff = displayOff;
        if (Changed != null) Changed();
    }
}

class ScreenPreview : DrawnControl
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
        float width = Monitor.Width * scale, height = Monitor.Height * scale;
        return new RectangleF((Width - width) / 2, (Height - height) / 2, width, height);
    }

    RectangleF Box(Zone zone)
    {
        var picture = Picture();
        return new RectangleF(picture.X + zone.Area.X * picture.Width, picture.Y + zone.Area.Y * picture.Height,
            zone.Area.Width * picture.Width, zone.Area.Height * picture.Height);
    }

    int Edges(RectangleF box, Point at)
    {
        float near = 6 * DpiScale;
        if (!RectangleF.Inflate(box, near, near).Contains(at)) return -1;
        int hitEdges = 0;
        if (Math.Abs(at.X - box.Left) <= near) hitEdges |= 1;
        if (Math.Abs(at.X - box.Right) <= near) hitEdges |= 2;
        if (Math.Abs(at.Y - box.Top) <= near) hitEdges |= 4;
        if (Math.Abs(at.Y - box.Bottom) <= near) hitEdges |= 8;
        return hitEdges;
    }

    int Hit(Point at, out int found)
    {
        found = 0;
        var order = Enumerable.Range(0, Zones.Count).OrderBy(i => i == Selected ? 0 : 1).ToList();
        foreach (int i in order)
        {
            int hitEdges = Edges(Box(Zones[i]), at);
            if (hitEdges >= 0 && (hitEdges != 0 || Box(Zones[i]).Contains(at))) { found = hitEdges; return i; }
        }
        return -1;
    }

    static Cursor CursorFor(int hitEdges)
    {
        if (hitEdges == 5 || hitEdges == 10) return Cursors.SizeNWSE;
        if (hitEdges == 6 || hitEdges == 9) return Cursors.SizeNESW;
        if ((hitEdges & 3) != 0) return Cursors.SizeWE;
        if ((hitEdges & 12) != 0) return Cursors.SizeNS;
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
        var picture = Picture();
        float dx = (e.X - from.X) / picture.Width, dy = (e.Y - from.Y) / picture.Height, min = 0.03f;
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
        float step = 0.01f,
            dx = e.KeyCode == Keys.Left ? -step : e.KeyCode == Keys.Right ? step : 0,
            dy = e.KeyCode == Keys.Up ? -step : e.KeyCode == Keys.Down ? step : 0;
        if (e.KeyCode == Keys.Tab && e.Control) return;
        if (dx == 0 && dy == 0) return;
        e.Handled = true;
        var area = Zones[Selected].Area;
        area.X = Math.Max(0, Math.Min(1 - area.Width, area.X + dx));
        area.Y = Math.Max(0, Math.Min(1 - area.Height, area.Y + dy));
        Zones[Selected].Area = area;
        Invalidate();
        if (Moved != null) Moved();
        if (Done != null) Done();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var graphics = Begin(e);
        var picture = Picture();
        using (var brush = new SolidBrush(Theme.PictureBackground)) graphics.FillRectangle(brush, picture);
        if (Image != null)
        {
            graphics.InterpolationMode = InterpolationMode.HighQualityBilinear;
            graphics.DrawImage(Image, picture);
        }
        var font = Ui.Get(7.5f, Ui.Semibold);
        for (int i = 0; i < Zones.Count; i++)
        {
            if (i == Selected) continue;
            Draw(graphics, Zones[i], false, font);
        }
        if (Selected >= 0 && Selected < Zones.Count) Draw(graphics, Zones[Selected], true, font);
        if (Focused && ShowFocusCues)
            using (var pen = new Pen(Theme.Text, 1.5f * DpiScale))
                graphics.DrawRectangle(pen, picture.X + DpiScale, picture.Y + DpiScale,
                    picture.Width - 2 * DpiScale, picture.Height - 2 * DpiScale);
    }

    void Draw(Graphics graphics, Zone zone, bool on, Font font)
    {
        var box = Box(zone);
        var mark = Color.FromArgb(zone.MarkColor >> 16 & 0xFF, zone.MarkColor >> 8 & 0xFF, zone.MarkColor & 0xFF);
        using (var fill = new SolidBrush(Color.FromArgb(on ? 40 : 18, 255, 255, 255))) graphics.FillRectangle(fill, box);
        if (on)
            using (var ring = new Pen(Theme.Accent, 2 * DpiScale))
                graphics.DrawRectangle(ring, box.X - 2 * DpiScale, box.Y - 2 * DpiScale,
                    box.Width + 4 * DpiScale, box.Height + 4 * DpiScale);
        using (var pen = new Pen(mark, (on ? 2.5f : 1.5f) * DpiScale))
        {
            if (!on) pen.DashStyle = DashStyle.Dash;
            graphics.DrawRectangle(pen, box.X, box.Y, box.Width, box.Height);
        }
        var size = TextRenderer.MeasureText(graphics, zone.Name, font, Size.Empty, TextFormatFlags.NoPadding);
        var tag = new RectangleF(box.X + 2 * DpiScale, box.Y + 2 * DpiScale,
            Math.Min(box.Width - 4 * DpiScale, size.Width + 6 * DpiScale), size.Height + 2 * DpiScale);
        if (tag.Width < 8 * DpiScale || tag.Height > box.Height) return;
        using (var brush = new SolidBrush(mark)) graphics.FillRectangle(brush, tag);
        Ui.Draw(graphics, zone.Name, font, new RectangleF(tag.X + 3 * DpiScale, tag.Y + DpiScale, tag.Width - 4 * DpiScale, tag.Height),
            Color.FromArgb(16, 16, 16), TextFormatFlags.EndEllipsis);
    }
}

class ZoneList : DrawnControl
{
    public List<Zone> Zones = new List<Zone>();
    public Func<int, int> Count = i => 0;
    public int Selected;
    public event Action<int> Picked, Removed;

    public ZoneList() { AccessibleRole = AccessibleRole.List; AccessibleName = "Zones"; }

    protected override bool Clicks { get { return false; } }

    float Row { get { return 22 * DpiScale; } }

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
        var graphics = Begin(e);
        var font = Ui.Get(9f, Ui.Regular);
        for (int i = 0; i < Zones.Count; i++)
        {
            var rowBox = new RectangleF(0.5f, i * Row + 1, Width - 1, Row - 2);
            bool on = i == Selected;
            Ui.Fill(graphics, rowBox, 4 * DpiScale, on ? Theme.AccentBackground : Theme.Button, on ? Theme.Accent : Theme.Button);
            var zone = Zones[i];
            using (var brush = new SolidBrush(
                Color.FromArgb(zone.MarkColor >> 16 & 0xFF, zone.MarkColor >> 8 & 0xFF, zone.MarkColor & 0xFF)))
                graphics.FillRectangle(brush, 7 * DpiScale, rowBox.Y + (rowBox.Height - 9 * DpiScale) / 2, 9 * DpiScale, 9 * DpiScale);
            float right = on && Zones.Count > 1 ? Row : 6 * DpiScale;
            Ui.Draw(graphics, Count(i).ToString(), Ui.Get(8.25f, Ui.Regular),
                new RectangleF(0, rowBox.Y, Width - right - 2 * DpiScale, rowBox.Height),
                Theme.Dim, TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
            Ui.Draw(graphics, zone.Name, font,
                new RectangleF(22 * DpiScale, rowBox.Y, Width - 22 * DpiScale - right - 26 * DpiScale, rowBox.Height),
                Theme.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            if (on && Zones.Count > 1)
                Icons.Draw(graphics, "close",
                    new RectangleF(Width - Row + 5 * DpiScale, rowBox.Y + (rowBox.Height - 12 * DpiScale) / 2,
                        12 * DpiScale, 12 * DpiScale),
                    Theme.Dim);
        }
        if (Focused && ShowFocusCues && Selected >= 0 && Selected < Zones.Count)
            using (var pen = new Pen(Theme.Text, 1.5f * DpiScale)) graphics.DrawRectangle(pen, 1, Selected * Row + 1, Width - 3, Row - 3);
    }
}
