using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

// In-memory G515 that answers the HID++ calls the app makes, for running without the keyboard.
class FakeHidpp : Hidpp
{
    static readonly Dictionary<int, byte> Index = new Dictionary<int, byte> { { 0x8071, 9 }, { 0x8040, 14 }, { 0x8101, 15 } };
    static readonly byte[] EffectIds = { 0x00, 0x01, 0x0A, 0x15, 0x16, 0x17 };

    public static byte[] Hex(string s) { return Enumerable.Range(0, s.Length / 2).Select(i => Convert.ToByte(s.Substring(i * 2, 2), 16)).ToArray(); }

    Dictionary<int, byte[]> files = new Dictionary<int, byte[]>
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
        if (!files.TryGetValue(0x0008, out p)) p = files[0x0101];
        int rec = 30 + ((p[74] & 1) != 0 ? 0 : 11);
        id = p[rec];
        prm = p.Skip(rec + 1).Take(10).ToArray();
    }

    public override void Dispose() { }

    public override byte[] Call(byte feat, int fn, params byte[] p)
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
        }
        else if (feat == Index[0x8101])
        {
            switch (fn)
            {
                case 2: buffer.Clear(); break;
                case 3: buffer.AddRange(p.Take(16)); break;
                case 6: if ((p[0] & 1) != 0) mode = p[0] >> 1 & 1; r[0] = (byte)mode; r[2] = 1; break;
                case 7: files[u16(0)] = buffer.Take(u16(2)).ToArray(); break;
                case 8:
                    if (!files.ContainsKey(u16(0))) throw new HidppError(4, "HID++ error 0x04");
                    buffer = files[u16(0)].ToList();
                    break;
                case 0xC: buffer.Skip(u16(0)).Take(16).ToArray().CopyTo(r, 0); break;
            }
        }
        return r;
    }
}

class KeyboardView : Form
{
    static readonly string[] Rows =
    {
        "Esc|_1|F1|F2|F3|F4|_.5|F5|F6|F7|F8|_.5|F9|F10|F11|F12|_.25|PrtSc|ScrLk|Pause",
        "`|1|2|3|4|5|6|7|8|9|0|-|=|Back:2|_.25|Ins|Home|PgUp",
        "Tab:1.5|Q|W|E|R|T|Y|U|I|O|P|[|]|\\:1.5|_.25|Del|End|PgDn",
        "Caps:1.75|A|S|D|F|G|H|J|K|L|;|'|Enter:2.25",
        "Shift:2.25|Z|X|C|V|B|N|M|,|.|/|Shift:2.75|_1.25|↑",
        "Ctrl:1.25|Win:1.25|Alt:1.25|:6.25|Alt:1.25|Fn:1.25|Menu:1.25|Ctrl:1.25|_.25|←|↓|→",
    };
    const float U = 40, Pad = 16;
    readonly FakeHidpp kb;
    readonly List<Tuple<RectangleF, string>> keys = new List<Tuple<RectangleF, string>>();
    readonly List<Tuple<PointF, double>> ripples = new List<Tuple<PointF, double>>();
    readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
    float width;

    protected override bool ShowWithoutActivation { get { return true; } }

    public KeyboardView(FakeHidpp kb)
    {
        this.kb = kb;
        for (int r = 0; r < Rows.Length; r++)
        {
            float x = 0, y = r * U + (r > 0 ? U / 2 : 0);
            foreach (var t in Rows[r].Split('|'))
            {
                if (t.StartsWith("_")) { x += float.Parse(t.Substring(1), System.Globalization.CultureInfo.InvariantCulture) * U; continue; }
                var parts = t.Split(':');
                float w = parts.Length > 1 ? float.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture) * U : U;
                if (r == 4 && parts[0] == "↑") x = width - 2 * U;
                keys.Add(Tuple.Create(new RectangleF(Pad + x, Pad + y, w - 4, U - 4), parts[0]));
                x += w;
                width = Math.Max(width, x);
            }
        }
        Text = "G515 demo keyboard";
        BackColor = Color.FromArgb(28, 28, 28);
        DoubleBuffered = true;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        ClientSize = new Size((int)(width + 2 * Pad), (int)(6.5f * U + 2 * Pad + 20));
        var timer = new System.Windows.Forms.Timer { Interval = 33 };
        timer.Tick += (s, e) => Invalidate();
        timer.Start();
        FormClosing += (s, e) => { if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); } };
        MouseDown += (s, e) => ripples.Add(Tuple.Create(new PointF(e.X, e.Y), clock.Elapsed.TotalSeconds));
    }

    static Color Hsv(double h, double s, double v)
    {
        h = (h % 1 + 1) % 1 * 6;
        int i = (int)h;
        double f = h - i, p = v * (1 - s), q = v * (1 - s * f), t = v * (1 - s * (1 - f));
        double[] rgb = i == 0 ? new[] { v, t, p } : i == 1 ? new[] { q, v, p } : i == 2 ? new[] { p, v, t } : i == 3 ? new[] { p, q, v } : i == 4 ? new[] { t, p, v } : new[] { v, p, q };
        return Color.FromArgb((int)(rgb[0] * 255), (int)(rgb[1] * 255), (int)(rgb[2] * 255));
    }

    static Color Scale(Color c, double k)
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
        var color = Color.FromArgb(p[0], p[1], p[2]);
        double period = (id == 0x0A ? p[3] << 8 | p[4] : p[6] << 8 | p[7]) / 1000.0;
        if (id == 0x17) period = (p[6] << 8 | p[7]) / 20.0;
        if (period <= 0) period = 5;
        int dir = p[9];
        ripples.RemoveAll(r => t - r.Item2 > 3);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var font = new Font("Segoe UI", 7.5f))
        foreach (var k in keys)
        {
            var r = k.Item1;
            double x = (r.X + r.Width / 2 - Pad) / width, y = (r.Y - Pad) / (6.5 * U);
            Color c;
            if (def == null) c = Hsv(t / 8 - x, 1, 1);
            else switch (id)
            {
                case 0x01: c = color; break;
                case 0x0A: c = Scale(color, 0.5 - 0.5 * Math.Cos(2 * Math.PI * t / period)); break;
                case 0x15: c = Hsv(t / period, p[1] / 255.0, 1); break;
                case 0x16:
                    double off = dir == 6 ? x : dir == 3 ? -Math.Abs(x - 0.5) * 2 : dir == 8 ? Math.Abs(x - 0.5) * 2 : dir == 2 ? -y : dir == 7 ? y : -x;
                    c = Hsv(t / period + off, p[1] / 255.0, 1);
                    break;
                case 0x17:
                    double glow = 0;
                    foreach (var rp in ripples)
                    {
                        double d = Math.Sqrt(Math.Pow(r.X + r.Width / 2 - rp.Item1.X, 2) + Math.Pow(r.Y + r.Height / 2 - rp.Item1.Y, 2)) / U;
                        double front = (t - rp.Item2) * 8 / period;
                        glow = Math.Max(glow, Math.Max(0, 1 - Math.Abs(d - front)) * Math.Max(0, 1 - (t - rp.Item2) / 3));
                    }
                    c = Scale(color, glow);
                    break;
                default: c = Color.Black; break;
            }
            c = Scale(c, bright / 100.0);
            using (var path = new GraphicsPath())
            {
                float rad = 5;
                path.AddArc(r.X, r.Y, rad * 2, rad * 2, 180, 90);
                path.AddArc(r.Right - rad * 2, r.Y, rad * 2, rad * 2, 270, 90);
                path.AddArc(r.Right - rad * 2, r.Bottom - rad * 2, rad * 2, rad * 2, 0, 90);
                path.AddArc(r.X, r.Bottom - rad * 2, rad * 2, rad * 2, 90, 90);
                path.CloseFigure();
                using (var key = new SolidBrush(Color.FromArgb(30, 30, 32))) g.FillPath(key, path);
                using (var pen = new Pen(Color.FromArgb(55, 55, 58))) g.DrawPath(pen, path);
                using (var lit = new SolidBrush(c))
                    g.DrawString(k.Item2, font, lit, r, new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center });
                using (var under = new SolidBrush(Color.FromArgb(90, c))) g.FillRectangle(under, r.X + 6, r.Bottom - 4, r.Width - 12, 2);
            }
        }
        string what = def == null ? "factory animation (approximation)" : def.Name;
        if (id == 0x17) what += " — click the keys";
        using (var f = new Font("Segoe UI", 9f))
        using (var b = new SolidBrush(Color.FromArgb(150, 150, 150)))
            g.DrawString(what + "   ·   brightness " + bright + "%", f, b, Pad, ClientSize.Height - 26);
    }
}
