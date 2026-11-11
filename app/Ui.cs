using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using Microsoft.Win32;

static class Theme
{
    public static Color Bg, Side, Card, Btn, BtnHover, BtnDown, Border, Text, Dim, Accent, AccentBg, OnAccent, Warn, KeyOff, Bed, Track;
    public static bool Light;
    public static event Action Changed;
    public static readonly string[] Modes = { "system", "dark", "light" };

    static Theme() { Set(false); }

    public static string Mode
    {
        get { var m = Settings.Get("theme"); return Array.IndexOf(Modes, m) > 0 ? m : "system"; }
        set { Settings.Set("theme", value); Apply(); }
    }

    public static bool SystemLight()
    {
        try
        {
            using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                return k != null && Equals(k.GetValue("AppsUseLightTheme"), 1);
        }
        catch { return false; }
    }

    public static void Apply()
    {
        var m = Mode;
        bool light = m == "light" || (m == "system" && SystemLight());
        if (light == Light) return;
        Set(light);
        if (Changed != null) Changed();
    }

    static Color C(int v) { return Color.FromArgb(v >> 16 & 0xFF, v >> 8 & 0xFF, v & 0xFF); }

    static void Set(bool light)
    {
        Light = light;
        if (light)
        {
            Bg = C(0xF7F7F7); Side = C(0xEDEDED); Card = C(0xFFFFFF); Btn = C(0xECECEC); BtnHover = C(0xE0E0E0); BtnDown = C(0xDADADA); Border = C(0xD4D4D4);
            Text = C(0x1A1A1A); Dim = C(0x5A5A5A); Accent = C(0x1D7A60); AccentBg = C(0xD9EDE6); OnAccent = C(0xFFFFFF);
            Warn = C(0x9A5B00); KeyOff = C(0xD6D6D6); Bed = C(0xBDBDBD); Track = C(0xC8C8C8);
        }
        else
        {
            Bg = C(0x1C1C1C); Side = C(0x161616); Card = C(0x232323); Btn = C(0x2B2B2B); BtnHover = C(0x353535); BtnDown = C(0x242424); Border = C(0x3A3A3A);
            Text = C(0xE8E8E8); Dim = C(0xA3A3A3); Accent = C(0x3FA98A); AccentBg = C(0x26302D); OnAccent = C(0x0C1A15);
            Warn = C(0xE0A458); KeyOff = C(0x2E2E2E); Bed = C(0x151515); Track = C(0x444444);
        }
    }
}

static class Ui
{
    public const string Regular = "Segoe UI", Semi = "Segoe UI Semibold";
    public static float K = 1;
    public static Action<ContextMenuStrip> Popped;
    static readonly List<KeyValuePair<Control, Action>> styles = new List<KeyValuePair<Control, Action>>();
    static readonly List<Tuple<Control, float, string>> fonts = new List<Tuple<Control, float, string>>();
    static readonly Dictionary<string, Font> cache = new Dictionary<string, Font>();
    public static readonly ToolStripRenderer Menus = new MenuLook();

    public static T Style<T>(T c, Action a) where T : Control
    {
        a();
        styles.Add(new KeyValuePair<Control, Action>(c, a));
        return c;
    }

    public static T Font<T>(T c, float pt, string face) where T : Control
    {
        c.Font = Win.Px(pt, K, face);
        fonts.Add(Tuple.Create((Control)c, pt, face));
        return c;
    }

    public static void Restyle() { styles.RemoveAll(s => s.Key.IsDisposed); foreach (var s in styles) s.Value(); }

    public static void Refont() { fonts.RemoveAll(f => f.Item1.IsDisposed); foreach (var f in fonts) f.Item1.Font = Win.Px(f.Item2, K, f.Item3); }

    public static Font Get(float pt, string face)
    {
        var key = pt + "|" + face + "|" + K;
        Font f;
        if (!cache.TryGetValue(key, out f)) cache[key] = f = Win.Px(pt, K, face);
        return f;
    }

    public static Label Text(Control parent, string text, int x, int y, Func<Color> color, float pt = 9.75f, string face = Regular)
    {
        var l = new Label { Text = text, Location = new Point(x, y), AutoSize = true, BackColor = Color.Transparent, UseMnemonic = false };
        Style(l, () => l.ForeColor = color());
        Font(l, pt, face);
        parent.Controls.Add(l);
        return l;
    }

    public const int Charging = 1000, Asleep = -2;
    static readonly string[] Moon =
    {
        "..###......#....",
        ".##.......###...",
        "##.........#....",
        "##..............",
        "##..............",
        "##............#.",
        "###....#........",
        ".######....#....",
        "..####.........."
    };
    static readonly int[] Spark = { 4, 5, 3, 4, 2, 3, 1, 5, 3, 4, 2, 3, 1, 2 };

    public static int BatteryWidth(float k) { return (int)Math.Round(16 * k) + (int)Math.Round(2 * k); }
    public static int BatteryHeight(float k) { return (int)Math.Round(9 * k); }

    public static void Battery(Graphics g, float left, float top, int percent, float k, Color? color = null)
    {
        int x = (int)Math.Round(left), y = (int)Math.Round(top);
        int line = Math.Max(1, (int)Math.Round(k)), width = (int)Math.Round(16 * k), height = BatteryHeight(k), nub = (int)Math.Round(3 * k);
        var ink = color ?? Theme.Text;
        var smoothing = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.None;
        if (percent == Asleep)
        {
            using (var brush = new SolidBrush(ink))
                for (int row = 0; row < Moon.Length; row++)
                    for (int from = 0; from < Moon[row].Length; from++)
                    {
                        if (Moon[row][from] != '#') continue;
                        int to = from;
                        while (to + 1 < Moon[row].Length && Moon[row][to + 1] == '#') to++;
                        int cell = Math.Max(1, (int)Math.Round(k)), cellLeft = (int)Math.Round(from * k);
                        g.FillRectangle(brush, x + cellLeft, y + (int)Math.Round(row * k), (int)Math.Round(to * k) + cell - cellLeft, cell);
                        from = to;
                    }
            g.SmoothingMode = smoothing;
            return;
        }
        using (var outline = new SolidBrush(ink))
        {
            g.FillRectangle(outline, x, y, width, line);
            g.FillRectangle(outline, x, y + height - line, width, line);
            g.FillRectangle(outline, x, y, line, height);
            g.FillRectangle(outline, x + width - line, y, line, height);
            g.FillRectangle(outline, x + width, y + nub, BatteryWidth(k) - width, height - nub * 2);
        }
        bool charging = percent >= Charging;
        if (charging) percent -= Charging;
        int inset = line * 2, room = width - inset * 2;
        int fill = Math.Max(line, (int)Math.Round(room * Math.Max(0, Math.Min(100, percent)) / 100.0));
        var level = new Rectangle(x + inset, y + inset, fill, height - inset * 2);
        using (var spark = new Region(Rectangle.Empty))
        {
            if (charging)
                for (int row = 0; row < Spark.Length / 2; row++)
                {
                    int upper = y + (int)Math.Round((row + 1) * k), lower = y + (int)Math.Round((row + 2) * k);
                    int from = x + (int)Math.Round((5 + Spark[row * 2]) * k), to = x + (int)Math.Round((6 + Spark[row * 2 + 1]) * k);
                    spark.Union(new Rectangle(from, upper, to - from, lower - upper));
                }
            var clip = g.Clip;
            g.ExcludeClip(spark);
            using (var brush = new SolidBrush(percent <= 20 ? Theme.Warn : ink)) g.FillRectangle(brush, level);
            g.Clip = clip;
            if (charging)
            {
                g.ExcludeClip(level);
                using (var brush = new SolidBrush(ink)) g.FillRegion(brush, spark);
                g.Clip = clip;
            }
            clip.Dispose();
        }
        g.SmoothingMode = smoothing;
    }

    public static T Add<T>(Control parent, T c, int x, int y, int w, int h) where T : Control
    {
        c.Bounds = new Rectangle(x, y, w, h);
        c.TabIndex = parent.Controls.Count;
        if (!(c is Ctl) && !(c is TextBoxBase)) c.TabStop = false;
        parent.Controls.Add(c);
        c.BringToFront();
        return c;
    }

    public static GraphicsPath Round(RectangleF r, float rad)
    {
        var p = new GraphicsPath();
        float d = Math.Min(rad * 2, Math.Min(r.Width, r.Height));
        if (d <= 0) { p.AddRectangle(r); return p; }
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    public static void Fill(Graphics g, RectangleF r, float rad, Color fill, Color edge)
    {
        using (var path = Round(r, rad))
        using (var b = new SolidBrush(fill))
        using (var p = new Pen(edge))
        {
            g.FillPath(b, path);
            if (edge != fill) g.DrawPath(p, path);
        }
    }

    public static Color Mix(Color a, Color b, double t)
    {
        return Color.FromArgb((int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
    }

    public static void Draw(Graphics g, string text, Font f, RectangleF r, Color c, TextFormatFlags flags)
    {
        TextRenderer.DrawText(g, text, f, Rectangle.Round(r), c, flags | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
    }
}

static class Icons
{
    public static void Draw(Graphics g, string name, RectangleF r, Color c)
    {
        var state = g.Save();
        g.TranslateTransform(r.X, r.Y);
        g.ScaleTransform(r.Width / 24f, r.Height / 24f);
        using (var p = new Pen(c, 1.7f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
        using (var b = new SolidBrush(c))
            switch (name)
            {
                case "refresh":
                    g.DrawArc(p, 4, 4, 16, 16, -10, -305);
                    g.DrawLines(p, new[] { new PointF(20, 4), new PointF(20, 10.5f), new PointF(13.5f, 10.5f) });
                    break;
                case "minus": g.DrawLine(p, 6, 12, 18, 12); break;
                case "lines": g.DrawLine(p, 5, 7, 19, 7); g.DrawLine(p, 5, 12, 19, 12); g.DrawLine(p, 5, 17, 13, 17); break;
                case "power": g.DrawArc(p, 5, 5.5f, 14, 14, -60, 300); g.DrawLine(p, 12, 3.5f, 12, 11); break;
                case "close": g.DrawLine(p, 6.5f, 6.5f, 17.5f, 17.5f); g.DrawLine(p, 17.5f, 6.5f, 6.5f, 17.5f); break;
                case "plus": g.DrawLine(p, 12, 5, 12, 19); g.DrawLine(p, 5, 12, 19, 12); break;
                case "chevron": g.DrawLines(p, new[] { new PointF(7, 10), new PointF(12, 15), new PointF(17, 10) }); break;
                case "check": g.DrawLines(p, new[] { new PointF(5, 12.5f), new PointF(10, 17.5f), new PointF(19, 7) }); break;
                case "gear":
                    {
                        var cog = new List<PointF>();
                        for (int i = 0; i < 8; i++)
                            foreach (var t in new[] { new[] { 7f, -15f }, new[] { 9.8f, -9f }, new[] { 9.8f, 9f }, new[] { 7f, 15f } })
                            {
                                double a = (i * 45 + t[1]) * Math.PI / 180;
                                cog.Add(new PointF(12 + (float)Math.Cos(a) * t[0], 12 + (float)Math.Sin(a) * t[0]));
                            }
                        g.DrawPolygon(p, cog.ToArray());
                        g.DrawEllipse(p, 9, 9, 6, 6);
                    }
                    break;
                case "mouse":
                    using (var path = Ui.Round(new RectangleF(6, 3, 12, 18), 6)) g.DrawPath(p, path);
                    g.DrawLine(p, 12, 7, 12, 10);
                    break;
                case "headset":
                    g.DrawLine(p, 4, 15, 4, 12);
                    g.DrawArc(p, 4, 4, 16, 16, 180, 180);
                    g.DrawLine(p, 20, 12, 20, 15);
                    using (var path = Ui.Round(new RectangleF(3, 14, 4, 6), 1.5f)) g.DrawPath(p, path);
                    using (var path = Ui.Round(new RectangleF(17, 14, 4, 6), 1.5f)) g.DrawPath(p, path);
                    break;
                case "device":
                    using (var path = Ui.Round(new RectangleF(4, 4, 16, 16), 3)) g.DrawPath(p, path);
                    g.FillEllipse(b, 10.5f, 10.5f, 3, 3);
                    break;
                case "keyboard":
                    using (var path = Ui.Round(new RectangleF(2, 6, 20, 12), 2)) g.DrawPath(p, path);
                    foreach (var x in new[] { 6, 10, 14, 18 }) g.FillEllipse(b, x - 1, 9, 2, 2);
                    g.DrawLine(p, 7, 14, 17, 14);
                    break;
            }
        g.Restore(state);
    }
}

class Ctl : Control
{
    protected bool hover, down, key;
    Timer anim;
    float from, to, now;
    DateTime start;
    int length;

    public Ctl()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Cursor = Cursors.Hand;
    }

    protected bool Pressed { get { return (down && hover || key) && Enabled; } }

    public void Press()
    {
        if (!Enabled || !Visible) throw new InvalidOperationException((AccessibleName ?? Text) + " can't be pressed now");
        OnClick(EventArgs.Empty);
    }
    protected virtual bool Clicks { get { return true; } }
    protected float Now { get { return now; } }

    protected void Jump(float v)
    {
        from = to = now = v;
        if (anim != null) anim.Stop();
        Invalidate();
    }

    protected void Glide(float v, int ms)
    {
        if (!Win.Animations || !Visible || !IsHandleCreated) { Jump(v); return; }
        from = now;
        to = v;
        length = ms;
        start = DateTime.UtcNow;
        if (anim == null)
        {
            anim = new Timer { Interval = 15 };
            anim.Tick += (s, e) => Step();
        }
        anim.Start();
    }

    void Step()
    {
        double t = Math.Min(1, (DateTime.UtcNow - start).TotalMilliseconds / length);
        now = (float)(from + (to - from) * (1 - Math.Pow(1 - t, 3)));
        if (t >= 1) anim.Stop();
        Invalidate();
    }

    protected void Ring(Graphics g, float rad, Color c)
    {
        if (!Focused || !ShowFocusCues || !Enabled) return;
        using (var p = new Pen(c, 1.5f * K))
        using (var path = Ui.Round(new RectangleF(K, K, Width - 2 * K - 1, Height - 2 * K - 1), rad))
            g.DrawPath(p, path);
    }

    protected void Ring(Graphics g, float rad) { Ring(g, rad, Theme.Text); }

    protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { down = true; Invalidate(); } base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { if (down) { down = false; Invalidate(); } base.OnMouseUp(e); }
    protected override void OnMouseCaptureChanged(EventArgs e) { if (down) { down = false; Invalidate(); } base.OnMouseCaptureChanged(e); }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        bool h = ClientRectangle.Contains(e.Location);
        if (h != hover) { hover = h; Invalidate(); }
        base.OnMouseMove(e);
    }

    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { key = false; Invalidate(); base.OnLostFocus(e); }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!Clicks || !Enabled || (e.KeyCode != Keys.Space && e.KeyCode != Keys.Enter)) return;
        e.Handled = true;
        if (!key) { key = true; Invalidate(); }
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        if (!key || (e.KeyCode != Keys.Space && e.KeyCode != Keys.Enter)) return;
        key = false;
        Invalidate();
        if (Enabled) OnClick(EventArgs.Empty);
    }

    protected override bool IsInputKey(Keys k)
    {
        if (!Clicks && (k == Keys.Left || k == Keys.Right || k == Keys.Up || k == Keys.Down)) return true;
        return base.IsInputKey(k);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && anim != null) anim.Dispose();
        base.Dispose(disposing);
    }

    protected static float K { get { return Ui.K; } }
    protected Color Under { get { return Parent != null ? Parent.BackColor : Theme.Bg; } }

    protected Graphics Begin(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Under);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        return g;
    }

    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
    protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }
}

class Box : Control
{
    public Box()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Ui.Style(this, () => BackColor = Theme.Card);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent != null ? Parent.BackColor : Theme.Bg);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        Ui.Fill(g, new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), 8 * Ui.K, Theme.Card, Theme.Border);
        base.OnPaint(e);
    }
}

enum Look { Normal, Primary, Ghost }

class Btn : Ctl
{
    public Look Look;
    public string Icon;
    public bool On;

    public Btn() { AccessibleRole = AccessibleRole.PushButton; }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = Begin(e);
        Color fill, edge, fore;
        bool lit = Enabled && hover;
        if (Look == Look.Primary) { fill = edge = Pressed ? Ui.Mix(Theme.Accent, Color.Black, 0.18) : lit ? Ui.Mix(Theme.Accent, Theme.Text, 0.12) : Theme.Accent; fore = Theme.OnAccent; }
        else if (Look == Look.Ghost) { fill = edge = Pressed ? Theme.BtnDown : On ? Theme.AccentBg : lit ? Theme.Btn : Under; fore = On || lit ? Theme.Text : Theme.Dim; }
        else { fill = Pressed ? Theme.BtnDown : lit ? Theme.BtnHover : Theme.Btn; edge = Theme.Border; fore = Theme.Text; }
        if (!Enabled) { fill = Ui.Mix(Under, fill, 0.5); edge = Ui.Mix(Under, edge, 0.5); fore = Ui.Mix(Under, fore, 0.45); }
        Ui.Fill(g, new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), 6 * K, fill, edge);
        Ring(g, 6 * K);
        var font = Ui.Get(9.75f, Look == Look.Primary ? Ui.Semi : Ui.Regular);
        float icon = 16 * K;
        if (Text == "")
        {
            if (Icon != null) Icons.Draw(g, Icon, new RectangleF((Width - icon) / 2, (Height - icon) / 2, icon, icon), fore);
            return;
        }
        float tw = TextRenderer.MeasureText(g, Text, font, Size.Empty, TextFormatFlags.NoPadding).Width;
        float x = Look == Look.Ghost && Icon != null ? 10 * K : (Width - tw - (Icon != null ? icon + 8 * K : 0)) / 2;
        if (Icon != null) { Icons.Draw(g, Icon, new RectangleF(x, (Height - icon) / 2, icon, icon), fore); x += icon + 8 * K; }
        Ui.Draw(g, Text, font, new RectangleF(x, 0, Width - x - 4 * K, Height), fore, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }
}

class Seg : Ctl
{
    public string[] Items = new string[0];
    public event Action<int> Picked;
    int index = -1, hot = -1;


    readonly Timer later = new Timer { Interval = 250 };

    public Seg()
    {
        AccessibleRole = AccessibleRole.PageTabList;
        later.Tick += (s, e) => { later.Stop(); if (Picked != null) Picked(index); };
    }

    public int Index { get { return index; } set { if (value == index) return; index = value; Jump(value); } }

    public void Pick(int i)
    {
        if (!Enabled || !Visible) throw new InvalidOperationException(AccessibleName + " can't be changed now");
        Slide(i);
        if (Picked != null) Picked(i);
    }

    protected override bool Clicks { get { return false; } }

    RectangleF Cell(float i)
    {
        float pad = 2 * K, w = (Width - 2 * pad - (Items.Length - 1) * pad) / Items.Length;
        return new RectangleF(pad + i * (w + pad), pad, w, Height - 2 * pad);
    }

    void Slide(int i)
    {
        if (i == index) return;
        bool had = index >= 0;
        index = i;
        if (had) Glide(i, 160); else Jump(i);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!Enabled || Items.Length == 0) return;
        int d = e.KeyCode == Keys.Left || e.KeyCode == Keys.Up ? -1 : e.KeyCode == Keys.Right || e.KeyCode == Keys.Down ? 1 : 0;
        if (d == 0) return;
        e.Handled = true;
        int to = Math.Max(0, Math.Min(Items.Length - 1, index < 0 ? 0 : index + d));
        if (to == index) return;
        Slide(to);
        later.Stop();
        later.Start();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) later.Dispose();
        base.Dispose(disposing);
    }

    int Hit(Point p)
    {
        for (int i = 0; i < Items.Length; i++) if (Cell(i).Contains(p)) return i;
        return -1;
    }

    protected override void OnMouseMove(MouseEventArgs e) { int h = Hit(e.Location); if (h != hot) { hot = h; Invalidate(); } base.OnMouseMove(e); }
    protected override void OnMouseLeave(EventArgs e) { hot = -1; base.OnMouseLeave(e); }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        int h = Hit(e.Location);
        if (!Enabled || e.Button != MouseButtons.Left || h < 0) return;
        later.Stop();
        Slide(h);
        if (Picked != null) Picked(h);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = Begin(e);
        Ui.Fill(g, new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), 6 * K, Theme.Btn, Theme.Border);
        if (hot >= 0 && hot != index && Enabled) Ui.Fill(g, Cell(hot), 4 * K, Theme.BtnHover, Theme.BtnHover);
        if (index >= 0 && index < Items.Length)
        {
            var fill = Enabled ? Theme.Accent : Ui.Mix(Theme.Btn, Theme.Accent, 0.35);
            Ui.Fill(g, Cell(Now), 4 * K, fill, fill);
        }
        Ring(g, 6 * K);
        for (int i = 0; i < Items.Length; i++)
        {
            var r = Cell(i);
            bool lit = Math.Abs(Now - i) < 0.5f && index >= 0;
            Color fore = lit ? Theme.OnAccent : Theme.Text;
            if (!Enabled) fore = Ui.Mix(lit ? Theme.Accent : Theme.Btn, fore, 0.45);
            Ui.Draw(g, Items[i], Ui.Get(9.4f, lit ? Ui.Semi : Ui.Regular), r, fore,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
    }
}

class Swatch : Ctl
{
    public Color? Fill;
    public bool On;

    public Swatch() { AccessibleRole = AccessibleRole.PushButton; }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = Begin(e);
        double fade = Enabled ? 1 : 0.35;
        if (On) using (var path = Ui.Round(new RectangleF(K, K, Width - 2 * K, Height - 2 * K), 7 * K)) using (var p = new Pen(Ui.Mix(Under, Theme.Text, fade), 2 * K)) g.DrawPath(p, path);
        float inset = Pressed ? 5 * K : 4 * K;
        var r = new RectangleF(inset, inset, Width - 2 * inset - 1, Height - 2 * inset - 1);
        var fill = Fill ?? (Pressed ? Theme.BtnDown : hover && Enabled ? Theme.BtnHover : Theme.Btn);
        bool ring = Focused && ShowFocusCues && Enabled;
        Ui.Fill(g, r, 4 * K, Ui.Mix(Under, fill, fade), ring ? Theme.Text : Ui.Mix(Under, Theme.Border, fade));
        if (Fill == null) Icons.Draw(g, "plus", new RectangleF(Width / 2f - 7 * K, Height / 2f - 7 * K, 14 * K, 14 * K), Ui.Mix(Under, Theme.Dim, fade));
    }
}

class Toggle : Ctl
{
    bool on;
    public event Action Changed;

    public Toggle() { AccessibleRole = AccessibleRole.CheckButton; }

    public bool Checked { get { return on; } set { on = value; Jump(value ? 1 : 0); } }

    protected override void OnClick(EventArgs e)
    {
        base.OnClick(e);
        on = !on;
        Glide(on ? 1 : 0, 140);
        if (Changed != null) Changed();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = Begin(e);
        var r = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
        float t = Now;
        Ui.Fill(g, r, r.Height / 2, Ui.Mix(Theme.Btn, Theme.Accent, t), Ui.Mix(Theme.Border, Theme.Accent, t));
        Ring(g, r.Height / 2);
        float d = Height - 8 * K;
        float stretch = Pressed ? 4 * K : 0;
        using (var b = new SolidBrush(Ui.Mix(Theme.Dim, Theme.OnAccent, t)))
        using (var path = Ui.Round(new RectangleF(4 * K + t * (Width - 8 * K - d - stretch), 4 * K, d + stretch, d), d / 2))
            g.FillPath(b, path);
    }
}

class Drop : Ctl
{
    public readonly List<string> Items = new List<string>();
    public event Action<int> Picked;
    public Func<string, string> Label;
    int index = -1;

    public Drop() { AccessibleRole = AccessibleRole.ComboBox; }

    public int Index
    {
        get { return index; }
        set { index = value; Text = value >= 0 && value < Items.Count ? Items[value] : ""; }
    }

    public void Pick(int i)
    {
        if (!Enabled || !Visible) throw new InvalidOperationException(AccessibleName + " can't be changed now");
        Index = i;
        if (Picked != null) Picked(i);
    }

    protected override void OnClick(EventArgs e)
    {
        base.OnClick(e);
        if (!Enabled || Items.Count == 0) return;
        var m = new ContextMenuStrip { Renderer = Ui.Menus, Font = Ui.Get(9.75f, Ui.Regular), ShowImageMargin = false, ShowCheckMargin = true };
        m.BackColor = Theme.Card;
        m.ForeColor = Theme.Text;
        for (int i = 0; i < Items.Count; i++)
        {
            int n = i;
            var item = new ToolStripMenuItem(Label != null ? Label(Items[i]) : Items[i]) { Checked = i == index };
            item.Click += (s, a) => { Index = n; if (Picked != null) Picked(n); };
            m.Items.Add(item);
        }
        m.MinimumSize = new Size(Width, 0);
        m.Closed += (s, a) => BeginInvoke((Action)m.Dispose);
        m.Show(this, new Point(0, Height + (int)(2 * K)));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = Begin(e);
        Color fore = Enabled ? Theme.Text : Ui.Mix(Theme.Btn, Theme.Text, 0.45);
        Ui.Fill(g, new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), 6 * K, Pressed ? Theme.BtnDown : Enabled && hover ? Theme.BtnHover : Theme.Btn, Theme.Border);
        Ring(g, 6 * K);
        Ui.Draw(g, Text, Ui.Get(9.75f, Ui.Regular), new RectangleF(10 * K, 0, Width - 38 * K, Height), fore, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        Icons.Draw(g, "chevron", new RectangleF(Width - 26 * K, (Height - 16 * K) / 2, 16 * K, 16 * K), Enabled ? Theme.Dim : fore);
    }
}

class Field : Ctl
{
    public readonly TextBox Box = new TextBox { BorderStyle = BorderStyle.None };

    public Field()
    {
        Cursor = Cursors.IBeam;
        TabStop = false;
        Controls.Add(Box);
        Ui.Style(Box, () => { Box.BackColor = Theme.Btn; Box.ForeColor = Theme.Text; });
        Box.GotFocus += (s, e) => Invalidate();
        Box.LostFocus += (s, e) => Invalidate();
    }

    protected override void OnClick(EventArgs e) { base.OnClick(e); Box.Focus(); }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        Box.Font = Ui.Get(9.75f, Ui.Regular);
        Box.Width = Width - (int)(20 * K);
        Box.Location = new Point((int)(10 * K), (Height - Box.Height) / 2);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = Begin(e);
        Ui.Fill(g, new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), 6 * K, Theme.Btn, Box.Focused ? Theme.Accent : Theme.Border);
    }
}

class Slider : Ctl
{
    public int Max = 100, Value, Step = 5;
    public event EventHandler Changed;
    int wheel, grab;

    protected override bool Clicks { get { return false; } }

    protected override bool IsInputKey(Keys k)
    {
        return k == Keys.PageUp || k == Keys.PageDown || k == Keys.Home || k == Keys.End || base.IsInputKey(k);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!Enabled) return;
        int v;
        switch (e.KeyCode)
        {
            case Keys.Left: case Keys.Down: v = Value - 1; break;
            case Keys.Right: case Keys.Up: v = Value + 1; break;
            case Keys.PageDown: v = Value - 10; break;
            case Keys.PageUp: v = Value + 10; break;
            case Keys.Home: v = 0; break;
            case Keys.End: v = Max; break;
            default: return;
        }
        e.Handled = true;
        Put(v);
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        var handled = e as HandledMouseEventArgs;
        if (handled != null) handled.Handled = true;
        if (!Enabled) return;
        wheel += e.Delta;
        int notches = wheel / 120;
        if (notches == 0) return;
        wheel -= notches * 120;
        Put(Value + notches * Step);
    }

    public void Put(int v)
    {
        v = Math.Max(0, Math.Min(Max, v));
        if (v == Value) return;
        Value = v;
        Invalidate();
        if (Changed != null) Changed(this, EventArgs.Empty);
    }

    float X(int v) { return 7 * K + (float)((long)v * (Width - 14 * K) / Max); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = Begin(e);
        var c = Enabled ? Theme.Accent : Theme.Border;
        float mid = Height / 2f;
        using (var track = new SolidBrush(Theme.Track)) g.FillRectangle(track, 7 * K, mid - 2 * K, Width - 14 * K, 4 * K);
        using (var b = new SolidBrush(c))
        {
            g.FillRectangle(b, 7 * K, mid - 2 * K, X(Value) - 7 * K, 4 * K);
            g.FillEllipse(b, X(Value) - 7 * K, mid - 7 * K, 14 * K, 14 * K);
        }
        if (Focused && ShowFocusCues && Enabled)
            using (var p = new Pen(Theme.Text, 2 * K)) g.DrawEllipse(p, X(Value) - 6 * K, mid - 6 * K, 12 * K, 12 * K);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        float at = X(Value);
        grab = Math.Abs(e.X - at) <= 9 * K ? (int)(e.X - at) : 0;
        Set(e.X - grab);
    }

    protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); if (e.Button == MouseButtons.Left) Set(e.X - grab); }

    void Set(int px)
    {
        if (Max > 0) Put((int)Math.Round((px - 7 * K) * Max / (Width - 14 * K)));
    }
}

class StatusLine : Ctl
{
    string text = "";
    bool warn;
    Action click;

    public StatusLine() { Cursor = Cursors.Default; TabStop = false; }

    public string Shown { get { return text; } }

    public void Set(string t, bool w, Action c)
    {
        text = t ?? "";
        warn = w;
        click = c;
        Cursor = c != null ? Cursors.Hand : Cursors.Default;
        AccessibleName = text;
        Invalidate();
    }

    protected override void OnClick(EventArgs e) { base.OnClick(e); if (click != null) click(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = Begin(e);
        if (text == "") return;
        using (var b = new SolidBrush(warn ? Theme.Warn : Theme.Accent)) g.FillEllipse(b, 0, Height / 2f - 4 * K, 8 * K, 8 * K);
        var font = Ui.Get(9f, Ui.Regular);
        var flags = TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;
        int w = Width - (int)(16 * K), h = Math.Min(Height, TextRenderer.MeasureText(g, text, font, new Size(w, Height), flags).Height);
        TextRenderer.DrawText(g, text, font, new Rectangle((int)(16 * K), (Height - h) / 2, w, h), warn ? Theme.Text : Theme.Dim, flags);
    }
}

class DevItem : Ctl
{
    public string Title = "", Sub = "", Icon = "keyboard", Value = "";
    public int Level = -1;
    public bool On;

    public DevItem() { AccessibleRole = AccessibleRole.PushButton; }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = Begin(e);
        if (On || hover) { var c = On ? Theme.AccentBg : Pressed ? Theme.BtnDown : Theme.Btn; Ui.Fill(g, new RectangleF(0, 0, Width, Height), 6 * K, c, c); }
        Ring(g, 6 * K);
        float s = 32 * K, y = (Height - s) / 2;
        Ui.Fill(g, new RectangleF(8 * K, y, s, s), 6 * K, Theme.Btn, Theme.Btn);
        Icons.Draw(g, Icon, new RectangleF(8 * K + 7 * K, y + 7 * K, 18 * K, 18 * K), On ? Theme.Accent : Theme.Dim);
        float x = 8 * K + s + 10 * K;
        Ui.Draw(g, Title, Ui.Get(9.75f, Ui.Semi), new RectangleF(x, 0, Width - x - 4 * K, Height / 2f + 1), Theme.Text, TextFormatFlags.Bottom | TextFormatFlags.EndEllipsis);
        var font = Ui.Get(8.6f, Ui.Regular);
        float top = Height / 2f + 1;
        Ui.Draw(g, Sub, font, new RectangleF(x, top, Width - x - 4 * K, Height / 2f), Theme.Dim, TextFormatFlags.Top | TextFormatFlags.EndEllipsis);
        if (Level == -1) return;
        x += TextRenderer.MeasureText(g, Sub, font, Size.Empty, TextFormatFlags.NoPadding).Width + 6 * K;
        Ui.Battery(g, x, top + (font.Height - Ui.BatteryHeight(K)) / 2f, Level, K, Theme.Dim);
        x += Ui.BatteryWidth(K) + 5 * K;
        Ui.Draw(g, Value, font, new RectangleF(x, top, Width - x - 4 * K, Height / 2f), Theme.Dim, TextFormatFlags.Top | TextFormatFlags.EndEllipsis);
    }
}

class SubLine : Control
{
    string before = "", value = "", after = "";
    int level = -1;

    public SubLine()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
    }

    public void Set(string before, int level, string value, string after)
    {
        this.before = before;
        this.level = level;
        this.value = value;
        this.after = after;
        AccessibleName = before + (level == Ui.Asleep ? " · asleep" : level >= 0 ? " · " + value : "") + after;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var font = Ui.Get(9f, Ui.Regular);
        var flags = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix;
        float k = Ui.K, x = 0;
        int y = (Height - font.Height) / 2;
        Action<string> text = s =>
        {
            TextRenderer.DrawText(g, s, font, new Point((int)x, y), Theme.Dim, flags);
            x += TextRenderer.MeasureText(g, s, font, Size.Empty, flags).Width;
        };
        text(before);
        if (level != -1)
        {
            x += 6 * k;
            Ui.Battery(g, (int)x, y + (font.Height - Ui.BatteryHeight(k)) / 2, level, k, Theme.Dim);
            x += Ui.BatteryWidth(k) + 5 * k;
            text(value);
        }
        text(after);
    }
}

class Brand : Ctl
{
    public string Tag2 = "";

    public Brand() { Cursor = Cursors.Default; TabStop = false; }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = Begin(e);
        var font = Ui.Get(10.5f, Ui.Semi);
        float x = 8 * K;
        Ui.Draw(g, "G-Lite", font, new RectangleF(x, 0, Width - x, Height), Theme.Text, TextFormatFlags.VerticalCenter);
        if (Tag2 == "") return;
        x += TextRenderer.MeasureText(g, "G-Lite", font, Size.Empty, TextFormatFlags.NoPadding).Width + 6 * K;
        Ui.Draw(g, Tag2, Ui.Get(9f, Ui.Regular), new RectangleF(x, 0, Width - x, Height), Theme.Dim, TextFormatFlags.VerticalCenter);
    }
}

class MenuColors : ProfessionalColorTable
{
    public override Color MenuBorder { get { return Theme.Border; } }
    public override Color MenuItemBorder { get { return Theme.BtnHover; } }
    public override Color MenuItemSelected { get { return Theme.BtnHover; } }
    public override Color ToolStripDropDownBackground { get { return Theme.Card; } }
    public override Color ImageMarginGradientBegin { get { return Theme.Card; } }
    public override Color ImageMarginGradientMiddle { get { return Theme.Card; } }
    public override Color ImageMarginGradientEnd { get { return Theme.Card; } }
    public override Color CheckBackground { get { return Theme.Card; } }
    public override Color CheckSelectedBackground { get { return Theme.BtnHover; } }
    public override Color CheckPressedBackground { get { return Theme.BtnHover; } }
    public override Color SeparatorDark { get { return Theme.Border; } }
    public override Color SeparatorLight { get { return Theme.Border; } }
}

class MenuLook : ToolStripProfessionalRenderer
{
    public MenuLook() : base(new MenuColors()) { RoundedEdges = false; }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        var row = e.Item as MenuRow;
        if (row != null && e.Text == row.ShortcutKeyDisplayString) { row.Draw(e.Graphics, e.TextRectangle); return; }
        if (row != null) row.TextLeft = e.TextRectangle.Left;
        e.TextColor = e.Item.Enabled ? Theme.Text : Theme.Dim;
        base.OnRenderItemText(e);
    }

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        if (Equals(e.ToolStrip.Tag, "round") && Win.Rounded) return;
        base.OnRenderToolStripBorder(e);
    }

    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var r = e.ImageRectangle;
        float s = Math.Min(r.Width, r.Height);
        Icons.Draw(e.Graphics, "check", new RectangleF(r.X + (r.Width - s) / 2, r.Y + (r.Height - s) / 2, s, s), Theme.Accent);
    }
}

class TrayTip : Form
{
    public Func<List<string[]>> Rows;
    List<string[]> rows = new List<string[]>();
    Point anchor;
    DateTime firstAt, movedAt, filledAt;
    bool hovering;
    float scale = 1;
    Font font;
    int nameWidth, rowHeight;
    readonly Timer watch = new Timer { Interval = 100 };

    public TrayTip()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        DoubleBuffered = true;
        Win.Round(this);
        watch.Tick += (s, e) => Tick();
    }

    protected override bool ShowWithoutActivation { get { return true; } }

    protected override CreateParams CreateParams
    {
        get
        {
            var p = base.CreateParams;
            p.ExStyle |= 0x08000000 | 0x80 | 0x20 | 0x08;
            return p;
        }
    }

    public void Moved()
    {
        var now = DateTime.UtcNow;
        if (!hovering) { hovering = true; firstAt = now; watch.Start(); }
        anchor = Cursor.Position;
        movedAt = now;
    }

    public void Gone()
    {
        hovering = false;
        watch.Stop();
        Hide();
    }

    void Tick()
    {
        var now = DateTime.UtcNow;
        if (Cursor.Position != anchor && (now - movedAt).TotalMilliseconds > 250) { Gone(); return; }
        if (!Visible && (now - firstAt).TotalMilliseconds < 400) return;
        if (Visible && (now - filledAt).TotalMilliseconds < 1000) return;
        filledAt = now;
        Fill();
    }

    void Fill()
    {
        rows = Rows();
        scale = Win.At(anchor);
        if (font != null) font.Dispose();
        font = Win.Px(9, scale);
        int pad = S(10), gap = S(24), valueWidth = 0;
        nameWidth = 0;
        rowHeight = font.Height + S(4);
        var flags = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;
        foreach (var r in rows)
        {
            nameWidth = Math.Max(nameWidth, TextRenderer.MeasureText(r[0], font, Size.Empty, flags).Width);
            valueWidth = Math.Max(valueWidth, TextRenderer.MeasureText(r[1], font, Size.Empty, flags).Width + (HasIcon(r) ? S(18) + (r[1] == "" ? 0 : S(6)) : 0));
        }
        var size = new Size(pad * 2 + nameWidth + gap + valueWidth, S(8) * 2 + rowHeight * rows.Count);
        var area = Screen.FromPoint(anchor).WorkingArea;
        int x = Math.Max(area.Left, Math.Min(area.Right - size.Width, anchor.X - size.Width / 2));
        int y = anchor.Y - size.Height - S(16);
        if (y < area.Top) y = anchor.Y + S(24);
        y = Math.Max(area.Top, Math.Min(area.Bottom - size.Height, y));
        Bounds = new Rectangle(new Point(x, y), size);
        BackColor = Theme.Card;
        if (!Visible) { Show(); Win.Border(this, Theme.Border); }
        Invalidate();
    }

    int S(int value) { return (int)Math.Round(value * scale); }

    static bool HasIcon(string[] row) { return row.Length > 2 && row[2] != ""; }

    void DrawBattery(Graphics g, int x, int y, string level)
    {
        int percent;
        if (int.TryParse(level, out percent)) Ui.Battery(g, x, y, percent, scale);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Theme.Card);
        if (font == null) return;
        int pad = S(10), top = S(8) + S(2);
        var flags = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;
        for (int i = 0; i < rows.Count; i++)
        {
            int y = top + i * rowHeight;
            TextRenderer.DrawText(g, rows[i][0], font, new Point(pad, y), Theme.Dim, flags);
            var width = TextRenderer.MeasureText(rows[i][1], font, Size.Empty, flags).Width;
            TextRenderer.DrawText(g, rows[i][1], font, new Point(Width - pad - width, y), Theme.Text, flags);
            if (HasIcon(rows[i])) DrawBattery(g, Width - pad - width - (rows[i][1] == "" ? 0 : S(6)) - S(18), y + (font.Height - S(9)) / 2, rows[i][2]);
        }
        if (!Win.Rounded) using (var pen = new Pen(Theme.Border)) g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
    }
}

class MenuRow : ToolStripMenuItem
{
    string value = "";
    public int Level = -1;
    public int TextLeft;

    public MenuRow(string name) : base(name) { ShowShortcutKeys = true; }

    public string Value
    {
        get { return value; }
        set { this.value = value; ShortcutKeyDisplayString = (Level != -1 ? "  " : "") + value; }
    }

    public void Draw(Graphics g, Rectangle area)
    {
        float k = Owner != null ? Owner.DeviceDpi / 96f : 1;
        var flags = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix;
        var size = TextRenderer.MeasureText(g, value, Font, Size.Empty, flags);
        int right = (Owner != null ? Owner.Width - 2 * Bounds.X : Width) - TextLeft - (int)(2 * k), y = area.Top + (area.Height - size.Height) / 2;
        var color = Enabled ? Theme.Text : Theme.Dim;
        TextRenderer.DrawText(g, value, Font, new Point(right - size.Width, y), color, flags);
        if (Level != -1) Ui.Battery(g, right - size.Width - (value == "" ? 0 : (int)(5 * k)) - Ui.BatteryWidth(k), y + (size.Height - Ui.BatteryHeight(k)) / 2, Level, k, color);
    }
}
