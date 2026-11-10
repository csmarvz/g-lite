using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

class Model
{
    public string Name { get; set; }
    public string[] Rows { get; set; }
    public string[] Ids { get; set; }

    public List<int> KeyIds()
    {
        var all = new List<int>();
        for (int r = 0; r < Rows.Length; r++)
        {
            var ids = Ids != null && r < Ids.Length ? Ids[r].Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries) : new string[0];
            int n = 0;
            foreach (var t in Rows[r].Split('|'))
            {
                if (t.StartsWith("_")) continue;
                all.Add(n < ids.Length && ids[n] != "--" ? Convert.ToInt32(ids[n], 16) : -1);
                n++;
            }
        }
        return all;
    }

    public static Model Load(string file)
    {
        string text;
        using (var stream = typeof(Model).Assembly.GetManifestResourceStream(file))
        using (var reader = new System.IO.StreamReader(stream)) text = reader.ReadToEnd();
        return new Model { Name = Strings(text, "name")[0], Rows = Strings(text, "rows"), Ids = Strings(text, "ids") };
    }

    static string[] Strings(string text, string key)
    {
        const string quoted = @"""(?:[^""\\]|\\.)*""";
        var match = Regex.Match(text, @"""" + key + @"""\s*:\s*(" + quoted + @"|\[(?:\s*" + quoted + @"\s*,?)*\s*\])");
        if (!match.Success) return null;
        return Regex.Matches(match.Groups[1].Value, quoted).Cast<Match>().Select(found => Regex.Unescape(found.Value.Substring(1, found.Value.Length - 2))).ToArray();
    }
}

// In-memory G515 that answers the HID++ calls the app makes, for running without the keyboard.
class FakeHidpp : Hidpp
{
    static readonly Dictionary<int, byte> Index = new Dictionary<int, byte> { { 0x8071, 9 }, { 0x8040, 14 }, { 0x8101, 15 }, { 0x8081, 16 }, { 0x4540, 17 }, { 0x1004, 18 } };
    static readonly byte[] EffectIds = { 0x00, 0x01, 0x0A, 0x15, 0x16, 0x17 };
    public readonly Dictionary<int, int> Keys = new Dictionary<int, int>();
    readonly byte[] control = new byte[2];
    public int Frames, Battery = 82;
    public bool Charging;

    public static byte[] Hex(string s) { return Enumerable.Range(0, s.Length / 2).Select(i => Convert.ToByte(s.Substring(i * 2, 2), 16)).ToArray(); }

    public Dictionary<int, byte[]> Flash = new Dictionary<int, byte[]>
    {
        { 0x0101, Hex("0101003c012cffffffffffffffffffffffffffffffffffffffffffffffff18ff008180aaff00006400000000000000000000000019ff008180aaff0000640000000000000000000000000300000000000000000010000104000200ffffffffffffffff") },
        { 0x0100, Hex("5e380925010181014063996dec0101ff") },
    };
    List<byte> buffer = new List<byte>();
    int brightness = 100, mode = 0x03;
    public byte[] Live = new byte[12];

    public FakeHidpp() : base(false) { }

    // What the keys show now: effect ID and its 10 parameter bytes, plus brightness.
    public void State(out byte id, out byte[] prm, out int bright)
    {
        bright = brightness;
        var live = Live;
        if (mode == 0 && live[1] < EffectIds.Length && live.Length == 12) { id = EffectIds[live[1]]; prm = live.Skip(2).ToArray(); return; }
        byte[] p;
        if (!Flash.TryGetValue(0x0008, out p)) p = Flash[0x0101];
        int rec = 30 + ((p[74] & 1) != 0 ? 0 : 11);
        id = p[rec];
        prm = p.Skip(rec + 1).Take(10).ToArray();
    }

    public override void Dispose() { }

    protected override byte[] Send(byte feat, int fn, byte[] p)
    {
        Thread.Sleep(5);
        var r = new byte[16];
        Func<int, int> u16 = o => p[o] << 8 | p[o + 1];
        if (feat == 0) { byte i; r[0] = Index.TryGetValue(u16(0), out i) ? i : (byte)0; }
        else if (feat == Index[0x8040])
        {
            if (fn == 1) { r[0] = (byte)(brightness >> 8); r[1] = (byte)brightness; }
            if (fn == 2) brightness = u16(0);
        }
        else if (feat == Index[0x8071])
        {
            if (fn == 0)
            {
                if (p[1] >= EffectIds.Length) throw new HidppError(2, "invalid argument");
                r[1] = p[1]; r[3] = EffectIds[p[1]];
            }
            if (fn == 1) Live = p.Take(12).ToArray();
            if (fn == 5 && p[0] == 1) { control[0] = p[1]; control[1] = p[2]; }
            if (fn == 5) { r[1] = control[0]; r[2] = control[1]; }
        }
        else if (feat == Index[0x8081])
        {
            if (fn == 0)
            {
                r[0] = 0; r[1] = p[1];
                for (int id = p[1] * 112; id < p[1] * 112 + 112; id++)
                    if (id >= 0x01 && id <= 0x4F || id >= 0x61 && id <= 0x6F) r[2 + (id - p[1] * 112) / 8] |= (byte)(1 << (id % 8));
            }
            if (fn == 6)
            {
                for (int i = 3; i < 16 && p.Length > i; i++)
                {
                    if (p[i] == 0 && i > 3) break;
                    if (p[i] < 0x01 || p[i] > 0x4F && p[i] < 0x61 || p[i] > 0x6F) throw new HidppError(2, "invalid argument");
                    Keys[p[i]] = p[0] << 16 | p[1] << 8 | p[2];
                }
            }
            if (fn == 7) Frames++;
        }
        else if (feat == Index[0x4540]) r[0] = 0x0D;
        else if (feat == Index[0x1004]) { r[0] = (byte)Battery; r[2] = (byte)(Charging ? 1 : 0); }
        else if (feat == Index[0x8101])
        {
            switch (fn)
            {
                case 2: buffer.Clear(); break;
                case 3: buffer.AddRange(p.Take(16)); break;
                case 6: if ((p[0] & 1) != 0) mode = p[0] >> 1 & 1; r[0] = (byte)mode; r[2] = 1; break;
                case 7: Flash[u16(0)] = buffer.Take(u16(2)).ToArray(); break;
                case 8:
                    if (!Flash.ContainsKey(u16(0))) throw new HidppError(4, "HID++ error 0x04");
                    buffer = Flash[u16(0)].ToList();
                    break;
                case 0xC: buffer.Skip(u16(0)).Take(16).ToArray().CopyTo(r, 0); break;
            }
        }
        return r;
    }
}

class FakeG915 : Hidpp
{
    static readonly Dictionary<int, byte> Index = new Dictionary<int, byte> { { 0x8071, 10 }, { 0x8040, 20 }, { 0x8100, 21 } };
    public static readonly byte[][] Ids = { new byte[] { 0x00, 0x01, 0x03, 0x0A }, new byte[] { 0x00, 0x01, 0x0A, 0x03, 0x04, 0x0B } };
    public readonly byte[][] Live = { new byte[13], new byte[13] };
    public int Brightness = 80, Claimed;

    public FakeG915() : base(false) { }

    public override void Dispose() { }

    protected override byte[] Send(byte feat, int fn, byte[] p)
    {
        Thread.Sleep(5);
        var r = new byte[16];
        if (feat == 0) { byte i; r[0] = Index.TryGetValue(p[0] << 8 | p[1], out i) ? i : (byte)0; }
        else if (feat == Index[0x8040])
        {
            if (fn == 1) { r[0] = (byte)(Brightness >> 8); r[1] = (byte)Brightness; }
            if (fn == 2) Brightness = p[0] << 8 | p[1];
        }
        else if (feat == Index[0x8071])
        {
            if (fn == 0 || fn == 1)
            {
                if (p[0] >= Ids.Length || p[1] >= Ids[p[0]].Length) throw new HidppError(2, "invalid argument");
                if (fn == 0) { r[0] = p[0]; r[1] = p[1]; r[3] = Ids[p[0]][p[1]]; }
                else Live[p[0]] = p.Take(13).ToArray();
            }
            if (fn == 5) Claimed = p[1];
        }
        else if (feat != Index[0x8100]) throw new HidppError(2, "invalid argument");
        return r;
    }
}

class FakeMouse : Hidpp
{
    public int Dpi = 1600, Stage = 2, Reloads, Rate = 1;
    public readonly byte[][] Zones = { new byte[12], new byte[12] };
    public readonly Dictionary<int, byte[]> Sectors = new Dictionary<int, byte[]>();
    static readonly byte[] Ids = { 0x00, 0x01, 0x03, 0x0A };
    List<byte> pending = new List<byte>();
    int writeTo, writeLength, current = 1;

    public FakeMouse() : this(false) { }

    public FakeMouse(bool fresh) : base(false)
    {
        var rom = Enumerable.Repeat((byte)0xFF, 255).ToArray();
        rom[0] = 1; rom[1] = 2; rom[2] = 2;
        var stages = new[] { 400, 800, 1600, 3200, 6400 };
        for (int i = 0; i < 5; i++) { rom[3 + 2 * i] = (byte)stages[i]; rom[4 + 2 * i] = (byte)(stages[i] >> 8); }
        var cycle = FakeHidpp.Hex("0300000000001f40000000");
        cycle.CopyTo(rom, 0xD0);
        cycle.CopyTo(rom, 0xDB);
        var user = (byte[])rom.Clone();
        Encoding.Unicode.GetBytes("Default").CopyTo(user, 0xA0);
        int c = GMouse.Crc(user, 253);
        user[253] = (byte)(c >> 8);
        user[254] = (byte)c;
        Sectors[0x0101] = rom;
        Sectors[0x0001] = user;
        Sectors[0x0000] = FakeHidpp.Hex("00010100").Concat(Enumerable.Repeat((byte)0xFF, 251)).ToArray();
        if (!fresh) return;
        Sectors.Remove(0x0000);
        Sectors.Remove(0x0001);
        current = 0x0101;
    }

    int[] Stages { get { return GMouse.StagesOf(Sectors[current]); } }

    public override void Dispose() { }

    protected override byte[] Send(byte feat, int fn, byte[] p)
    {
        Thread.Sleep(5);
        var r = new byte[16];
        Func<int, int> u16 = o => p[o] << 8 | p[o + 1];
        switch (feat)
        {
            case 0:
                int id = u16(0);
                r[0] = (byte)(id == 0x2201 ? 1 : id == 0x8060 ? 2 : id == 0x8070 ? 3 : id == 0x8100 ? 4 : 0);
                break;
            case 1:
                if (fn == 1) FakeHidpp.Hex("000064e0326400").CopyTo(r, 0);
                if (fn == 2) { r[1] = (byte)(Dpi >> 8); r[2] = (byte)Dpi; r[3] = 0x03; r[4] = 0x20; }
                if (fn == 3) { Dpi = u16(1); Stage = Array.IndexOf(Stages, Dpi); }
                break;
            case 2:
                if (fn == 0) r[0] = 0x8B;
                if (fn == 1) r[0] = (byte)Rate;
                if (fn == 2) throw new HidppError(5, "HID++ error 0x05");
                break;
            case 3:
                if (fn == 0) r[0] = 2;
                if (fn == 1) { r[0] = p[0]; r[2] = (byte)(p[0] + 1); r[3] = 4; }
                if (fn == 2) { if (p[1] >= Ids.Length) throw new HidppError(2, "invalid argument"); r[0] = p[0]; r[1] = p[1]; r[3] = Ids[p[1]]; }
                if (fn == 3) Zones[p[0]] = p.Take(12).ToArray();
                break;
            case 4:
                if (fn == 0) FakeHidpp.Hex("0103010501081000ff0a04").CopyTo(r, 0);
                if (fn == 2) r[0] = 1;
                if (fn == 3)
                {
                    var prof = Sectors[u16(0)];
                    current = u16(0);
                    Reloads++;
                    Rate = prof[0];
                    Stage = prof[1];
                    Dpi = Stages[Stage];
                    Zones[0] = new byte[] { 0, 0 }.Concat(prof.Skip(0xD1).Take(10)).ToArray();
                    Zones[1] = new byte[] { 1, 0 }.Concat(prof.Skip(0xDC).Take(10)).ToArray();
                }
                if (fn == 4) { r[0] = (byte)(current >> 8); r[1] = (byte)current; }
                if (fn == 5)
                {
                    if (!Sectors.ContainsKey(u16(0))) throw new HidppError(2, "HID++ error 0x02");
                    Sectors[u16(0)].Skip(u16(2)).Take(16).ToArray().CopyTo(r, 0);
                }
                if (fn == 6) { writeTo = u16(0); writeLength = u16(4); pending.Clear(); }
                if (fn == 7) pending.AddRange(p.Take(16));
                if (fn == 8) Sectors[writeTo] = pending.Take(writeLength).ToArray();
                if (fn == 0xB) r[0] = (byte)Math.Max(0, Stage);
                if (fn == 0xC) { Stage = p[0]; Dpi = Stages[Stage]; }
                break;
        }
        return r;
    }
}

class FakeG9x : Hidpp
{
    public readonly Dictionary<int, byte[]> Registers = new Dictionary<int, byte[]>();
    public readonly Dictionary<int, byte[]> Pages = new Dictionary<int, byte[]>();

    public FakeG9x() : base(false)
    {
        Registers[0x0F] = new byte[3];
        Registers[0x51] = FakeHidpp.Hex("221200");
        Registers[0x57] = FakeHidpp.Hex("FF0000");
        Registers[0x64] = FakeHidpp.Hex("020000");
        Registers[0x63] = FakeHidpp.Hex("22002200800100000000000000000000");
        Registers[0xF1] = FakeHidpp.Hex("015802");
        Pages[0] = new byte[512];
        Pages[1] = Page("020001030003040007");
        Pages[2] = Page("FF00008000110011221100220022221200440044222200880088122200000000122101011010028101008102008104008108008110008F0000830100830200830400830800");
        Pages[3] = Page("00FF00800009000922110022002222120055005522220000000012220000000012210101101001");
        Pages[4] = Page("0000FF800011001122110022002222120044004422220000000012220000000012210101101002");
    }

    public static byte[] Page(string start)
    {
        var page = Enumerable.Repeat((byte)0xFF, 512).ToArray();
        FakeHidpp.Hex(start).CopyTo(page, 0);
        GMouse.Seal(page);
        return page;
    }

    public readonly List<string> Writes = new List<string>();
    int nextSequence = -1, uploadPage, uploadOffset, uploadLeft;

    public override void Dispose() { }

    protected override byte[] Send(byte feat, int fn, byte[] p) { throw new HidppError(1, "HID++ 1.0 only"); }

    protected override void SendUpload(byte subId, byte sequence, byte[] data)
    {
        if (sequence != nextSequence || (subId == 0x92) != (sequence == 0)) throw new HidppError(0x05, "out of sequence");
        nextSequence++;
        int start = 0;
        if (subId == 0x92)
        {
            uploadPage = data[1];
            uploadOffset = data[2] * 2;
            uploadLeft = data[5] << 8 | data[6];
            start = 9;
        }
        if (uploadPage != 0) throw new HidppError(0x07, "uploads go to RAM");
        for (int i = start; i < 16 && uploadLeft > 0; i++, uploadLeft--) Pages[0][uploadOffset++] = data[i];
    }

    protected override byte[] SendRegister(byte subId, byte register, byte[] parameters)
    {
        var reply = new byte[16];
        if (subId == 0x80 && register == 0xA1) { nextSequence = 0; return reply; }
        if (subId == 0x82 && register == 0xA0)
        {
            int target = parameters[6], wordOffset = parameters[7], length = parameters[10] << 8 | parameters[11];
            if (target < 2) throw new HidppError(0x07, "protected page");
            Writes.Add((parameters[0] == 0x02 ? "erase " : "copy ") + target + ":" + wordOffset);
            if (parameters[0] == 0x02) Pages[target] = Enumerable.Repeat((byte)0xFF, 512).ToArray();
            else for (int i = 0; i < length; i++) Pages[target][wordOffset * 2 + i] &= Pages[parameters[2]][parameters[3] * 2 + i];
            return reply;
        }
        if (subId == 0x83 && register == 0xA2)
        {
            byte[] page;
            int pageNumber = parameters.Length > 0 ? parameters[0] : 0, offset = parameters.Length > 1 ? parameters[1] * 2 : 0;
            if (!Pages.TryGetValue(pageNumber, out page)) throw new HidppError(3, "no such page");
            Array.Copy(page, offset, reply, 0, 16);
            return reply;
        }
        byte[] value;
        if (!Registers.TryGetValue(register, out value)) throw new HidppError(2, "no such register");
        if (subId == 0x80 || subId == 0x82)
        {
            var stored = new byte[value.Length];
            Array.Copy(parameters, stored, Math.Min(parameters.Length, stored.Length));
            Registers[register] = stored;
            return reply;
        }
        Array.Copy(value, reply, value.Length);
        return reply;
    }
}

class FakeHeadset : Hidpp
{
    public int Sidetone = 30, Off = 30;

    public FakeHeadset() : base(false) { }

    public override void Dispose() { }

    protected override byte[] Send(byte feat, int fn, byte[] p)
    {
        Thread.Sleep(5);
        var r = new byte[16];
        if (feat == 0) { int id = p[0] << 8 | p[1]; r[0] = (byte)(id == 0x8300 ? 1 : id == 0x1F20 ? 2 : 0); }
        else if (feat == 1) { if (fn == 0) r[0] = (byte)Sidetone; if (fn == 1) Sidetone = p[0]; }
        else if (feat == 2)
        {
            if (fn == 0) { r[0] = 0x0F; r[1] = 0x56; r[2] = 1; }
            if (fn == 1) r[0] = (byte)Off;
            if (fn == 2) Off = p[0];
        }
        return r;
    }
}

class KeyboardView : Form
{
    const float U = 40, Pad = 16, Head = 30;
    [DllImport("user32.dll")] static extern bool ReleaseCapture();
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
    readonly FakeHidpp kb;
    readonly List<Tuple<RectangleF, string>> keys = new List<Tuple<RectangleF, string>>();
    readonly List<Tuple<PointF, double>> ripples = new List<Tuple<PointF, double>>();
    readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
    float width, height, bw, bh, scale = 1;
    List<Point>[] cells;
    Game game;
    Func<Game> make;
    double nextStep;

    protected override bool ShowWithoutActivation { get { return true; } }

    public KeyboardView(FakeHidpp kb, Model model)
    {
        this.kb = kb;
        Win.Round(this);
        foreach (var row in model.Rows)
        {
            float x = 0, y = height;
            int before = keys.Count;
            foreach (var t in row.Split('|'))
            {
                if (t.StartsWith("_")) { x += float.Parse(t.Substring(1), System.Globalization.CultureInfo.InvariantCulture) * U; continue; }
                var parts = t.Split(':');
                float w = parts.Length > 1 ? float.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture) * U : U;
                keys.Add(Tuple.Create(new RectangleF(Pad + x, Head + Pad + y, w - 4, U - 4), parts[0]));
                x += w;
                width = Math.Max(width, x);
            }
            height += keys.Count > before ? U : x;
        }
        var rows = keys.Select(k => k.Item1.Y).Distinct().OrderBy(v => v).ToList();
        cells = keys.Select(k =>
        {
            int r = rows.IndexOf(k.Item1.Y) - (rows.Count - Game.H);
            if (r < 0) return null;
            var l = Enumerable.Range(0, Game.W).Where(c => Pad + (c + 0.5f) * U >= k.Item1.X && Pad + (c + 0.5f) * U < k.Item1.Right + 4).Select(c => new Point(c, r)).ToList();
            return l.Count > 0 ? l : null;
        }).ToArray();
        Text = model.Name + " (demo)";
        BackColor = Color.FromArgb(28, 28, 28);
        DoubleBuffered = true;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        bw = width + 2 * Pad;
        bh = Head + height + 2 * Pad + 20;
        Rescale(Win.At(Point.Empty));
        var timer = new System.Windows.Forms.Timer { Interval = 33 };
        timer.Tick += (s, e) => { Tick(); Invalidate(); };
        timer.Start();
        FormClosing += (s, e) => { if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); } };
        MouseDown += (s, e) =>
        {
            if (e.Button != MouseButtons.Left) return;
            float ex = e.X / scale, ey = e.Y / scale;
            if (ey >= Head + Pad / 2) { ripples.Add(Tuple.Create(new PointF(ex, ey), clock.Elapsed.TotalSeconds)); return; }
            if (ex > bw - Pad - 20) { Hide(); return; }
            if (game != null) { if (ex > bw - Pad - 70) game = null; return; }
            if (ex > bw - Pad - 70) { Play(() => new Snake(cells)); return; }
            if (ex > bw - Pad - 125) { Play(() => new Blocks()); return; }
            if (ex > bw - Pad - 180) { Play(() => new Mines(keys.Select(k => k.Item1).ToArray(), cells)); return; }
            ReleaseCapture();
            SendMessage(Handle, 0xA1, (IntPtr)2, IntPtr.Zero);
        };
    }

    public void Rescale(float ns)
    {
        scale = ns;
        ClientSize = new Size((int)Math.Ceiling(bw * scale), (int)Math.Ceiling(bh * scale));
        Invalidate();
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == Win.DpiChanged) Rescale(Win.FromMessage(m));
        base.WndProc(ref m);
    }

    void Play(Func<Game> m)
    {
        make = m;
        game = m();
        nextStep = clock.Elapsed.TotalMilliseconds + game.Interval;
        Activate();
    }

    void Tick()
    {
        if (game == null) return;
        double now = clock.Elapsed.TotalMilliseconds;
        if (game.Over) return;
        if (now >= nextStep) { game.Step(); nextStep = now + game.Interval; }
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys key)
    {
        if (game == null) return base.ProcessCmdKey(ref msg, key);
        if (key == Keys.Escape) game = null; else if (game.Over) Play(make); else game.Key(key);
        Invalidate();
        return true;
    }

    public static double PeriodOf(byte id, byte[] p)
    {
        double period = (id == 0x0A ? p[3] << 8 | p[4] : p[6] << 8 | p[7]) / 1000.0;
        if (id == 0x17) period = (p[6] << 8 | p[7]) / 20.0;
        return period <= 0 ? 5 : period;
    }

    public static Color Lit(byte id, byte[] p, double x, double y, double t, double ripple)
    {
        if (G515.Def(id) == null) return Hsv(t / 8 - x, 1, 1);
        var color = Color.FromArgb(p[0], p[1], p[2]);
        double period = PeriodOf(id, p);
        int dir = p[9];
        switch (id)
        {
            case 0x01: return color;
            case 0x0A: return Scale(color, 0.5 - 0.5 * Math.Cos(2 * Math.PI * t / period));
            case 0x15: return Hsv(t / period, p[1] / 255.0, 1);
            case 0x16:
                double off = dir == 6 ? x : dir == 3 ? -Math.Abs(x - 0.5) * 2 : dir == 8 ? Math.Abs(x - 0.5) * 2 : dir == 2 ? -y : dir == 7 ? y : -x;
                return Hsv(t / period + off, p[1] / 255.0, 1);
            case 0x17: return Scale(color, ripple);
            default: return Color.Black;
        }
    }

    public static Color Hsv(double h, double s, double v)
    {
        h = (h % 1 + 1) % 1 * 6;
        int i = (int)h;
        double f = h - i, p = v * (1 - s), q = v * (1 - s * f), t = v * (1 - s * (1 - f));
        double[] rgb = i == 0 ? new[] { v, t, p } : i == 1 ? new[] { q, v, p } : i == 2 ? new[] { p, v, t } : i == 3 ? new[] { p, q, v } : i == 4 ? new[] { t, p, v } : new[] { v, p, q };
        return Color.FromArgb((int)(rgb[0] * 255), (int)(rgb[1] * 255), (int)(rgb[2] * 255));
    }

    public static Color Scale(Color c, double k)
    {
        k = Math.Max(0, Math.Min(1, k));
        return Color.FromArgb((int)(c.R * k), (int)(c.G * k), (int)(c.B * k));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        byte id; byte[] p; int bright;
        kb.State(out id, out p, out bright);
        var def = G515.Def(id);
        double t = clock.Elapsed.TotalSeconds;
        double period = PeriodOf(id, p);
        ripples.RemoveAll(r => t - r.Item2 > 3);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.ScaleTransform(scale, scale);
        using (var font = Win.Px(7.5f, 1))
        for (int ki = 0; ki < keys.Count; ki++)
        {
            var k = keys[ki];
            var r = k.Item1;
            Color fill = Color.FromArgb(30, 30, 32);
            double x = (r.X + r.Width / 2 - Pad) / width, y = (r.Y - Head - Pad) / height;
            double glow = 0;
            if (id == 0x17)
                foreach (var rp in ripples)
                {
                    double d = Math.Sqrt(Math.Pow(r.X + r.Width / 2 - rp.Item1.X, 2) + Math.Pow(r.Y + r.Height / 2 - rp.Item1.Y, 2)) / U;
                    double front = (t - rp.Item2) * 8 / period;
                    glow = Math.Max(glow, Math.Max(0, 1 - Math.Abs(d - front)) * Math.Max(0, 1 - (t - rp.Item2) / 3));
                }
            Color c = Lit(id, p, x, y, t, glow);
            if (game != null)
            {
                int hit = 0;
                if (cells[ki] != null) foreach (var cell in cells[ki]) if (hit == 0) hit = game.At(cell.Y, cell.X);
                bool arrow = "↑↓←→".Contains(k.Item2) && k.Item2.Length == 1;
                c = hit != 0 ? (game.Over && !(game is Mines) ? Color.FromArgb(255, 40, 40) : Color.FromArgb(hit | unchecked((int)0xFF000000))) : arrow ? Color.FromArgb(120, 120, 120) : Color.FromArgb(45, 45, 45);
                if (hit != 0) fill = Scale(c, 0.45);
            }
            c = Scale(c, bright / 100.0);
            fill = Color.FromArgb(Math.Max(30, (int)(fill.R * bright / 100.0)), Math.Max(30, (int)(fill.G * bright / 100.0)), Math.Max(32, (int)(fill.B * bright / 100.0)));
            using (var path = new GraphicsPath())
            {
                float rad = 5;
                path.AddArc(r.X, r.Y, rad * 2, rad * 2, 180, 90);
                path.AddArc(r.Right - rad * 2, r.Y, rad * 2, rad * 2, 270, 90);
                path.AddArc(r.Right - rad * 2, r.Bottom - rad * 2, rad * 2, rad * 2, 0, 90);
                path.AddArc(r.X, r.Bottom - rad * 2, rad * 2, rad * 2, 90, 90);
                path.CloseFigure();
                using (var key = new SolidBrush(fill)) g.FillPath(key, path);
                using (var pen = new Pen(Color.FromArgb(55, 55, 58))) g.DrawPath(pen, path);
                using (var lit = new SolidBrush(c))
                    g.DrawString(k.Item2, font, lit, r, new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center });
                using (var under = new SolidBrush(Color.FromArgb(90, c))) g.FillRectangle(under, r.X + 6, r.Bottom - 4, r.Width - 12, 2);
            }
        }
        if (!Win.Rounded) using (var pen = new Pen(Color.FromArgb(58, 58, 58))) g.DrawRectangle(pen, 0, 0, bw - 1, bh - 1);
        using (var f = Win.Px(9, 1))
        using (var b = new SolidBrush(Color.FromArgb(150, 150, 150)))
        {
            g.DrawString(Text, f, b, Pad, 12);
            g.DrawString("✕", f, b, bw - Pad - 14, 12);
            using (var a = new SolidBrush(Color.FromArgb(63, 169, 138))) g.DrawString(game == null ? "Mines      Blocks      Snake" : "Stop", f, a, bw - Pad - (game == null ? 175 : 66), 12);
        }
        string what = def == null ? "factory animation (approximation)" : def.Name;
        if (id == 0x17) what += " — click the keys";
        if (game != null) what = game.Over ? "game over   ·   score " + game.Score + "   ·   any key to play again" : game.Help;
        using (var f = Win.Px(9, 1))
        using (var b = new SolidBrush(Color.FromArgb(150, 150, 150)))
        {
            g.DrawString(what + "   ·   brightness " + bright + "%", f, b, Pad, bh - 26);
            if (game is Mines && !game.Over)
            {
                float lx = bw - Pad;
                for (int i = Mines.LegendNames.Length - 1; i >= 0; i--)
                {
                    lx -= g.MeasureString(Mines.LegendNames[i], f).Width;
                    g.DrawString(Mines.LegendNames[i], f, b, lx, bh - 26);
                    lx -= 12;
                    using (var sw = new SolidBrush(Color.FromArgb(Mines.LegendColors[i] | unchecked((int)0xFF000000)))) g.FillRectangle(sw, lx, bh - 23, 10, 10);
                    lx -= 12;
                }
            }
        }
    }
}
