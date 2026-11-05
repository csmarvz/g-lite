using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using Microsoft.Win32;

static class Theme
{
    public static Color Bg, Side, Card, Btn, BtnHover, Border, Text, Dim, Accent, AccentBg, OnAccent, Warn, KeyOff, Bed, Track;
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
            Bg = C(0xF7F7F7); Side = C(0xEDEDED); Card = C(0xFFFFFF); Btn = C(0xECECEC); BtnHover = C(0xE0E0E0); Border = C(0xD4D4D4);
            Text = C(0x1A1A1A); Dim = C(0x5A5A5A); Accent = C(0x1D7A60); AccentBg = C(0xD9EDE6); OnAccent = C(0xFFFFFF);
            Warn = C(0x9A5B00); KeyOff = C(0xD6D6D6); Bed = C(0xBDBDBD); Track = C(0xC8C8C8);
        }
        else
        {
            Bg = C(0x1C1C1C); Side = C(0x161616); Card = C(0x232323); Btn = C(0x2B2B2B); BtnHover = C(0x353535); Border = C(0x3A3A3A);
            Text = C(0xE8E8E8); Dim = C(0xA3A3A3); Accent = C(0x3FA98A); AccentBg = C(0x26302D); OnAccent = C(0x0C1A15);
            Warn = C(0xE0A458); KeyOff = C(0x2E2E2E); Bed = C(0x151515); Track = C(0x444444);
        }
    }
}

static class Ui
{
    public const string Regular = "Segoe UI", Semi = "Segoe UI Semibold";
    public static float K = 1;
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

    public static T Add<T>(Control parent, T c, int x, int y, int w, int h) where T : Control
    {
        c.Bounds = new Rectangle(x, y, w, h);
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
                case "power": g.DrawArc(p, 5, 5.5f, 14, 14, -60, 300); g.DrawLine(p, 12, 3.5f, 12, 11); break;
                case "close": g.DrawLine(p, 6.5f, 6.5f, 17.5f, 17.5f); g.DrawLine(p, 17.5f, 6.5f, 6.5f, 17.5f); break;
                case "plus": g.DrawLine(p, 12, 5, 12, 19); g.DrawLine(p, 5, 12, 19, 12); break;
                case "chevron": g.DrawLines(p, new[] { new PointF(7, 10), new PointF(12, 15), new PointF(17, 10) }); break;
                case "check": g.DrawLines(p, new[] { new PointF(5, 12.5f), new PointF(10, 17.5f), new PointF(19, 7) }); break;
                case "gear":
                    g.DrawEllipse(p, 9, 9, 6, 6);
                    for (int i = 0; i < 8; i++)
                    {
                        double a = i * Math.PI / 4;
                        g.DrawLine(p, 12 + (float)Math.Cos(a) * 6.5f, 12 + (float)Math.Sin(a) * 6.5f, 12 + (float)Math.Cos(a) * 9.5f, 12 + (float)Math.Sin(a) * 9.5f);
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
    protected bool hover;

    public Ctl()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Cursor = Cursors.Hand;
        TabStop = false;
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
        if (Look == Look.Primary) { fill = edge = lit ? Ui.Mix(Theme.Accent, Theme.Text, 0.12) : Theme.Accent; fore = Theme.OnAccent; }
        else if (Look == Look.Ghost) { fill = edge = On ? Theme.AccentBg : lit ? Theme.Btn : Under; fore = On || lit ? Theme.Text : Theme.Dim; }
        else { fill = lit ? Theme.BtnHover : Theme.Btn; edge = Theme.Border; fore = Theme.Text; }
        if (!Enabled) { fill = Ui.Mix(Under, fill, 0.5); edge = Ui.Mix(Under, edge, 0.5); fore = Ui.Mix(Under, fore, 0.45); }
        Ui.Fill(g, new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), 6 * K, fill, edge);
        var font = Ui.Get(9.75f, Look == Look.Primary ? Ui.Semi : Ui.Regular);
        float icon = 16 * K;
        if (Text == "")
        {
            if (Icon != null) Icons.Draw(g, Icon, new RectangleF((Width - icon) / 2, (Height - icon) / 2, icon, icon), fore);
            return;
        }
        float tw = TextRenderer.MeasureText(g, Text, font, Size.Empty, TextFormatFlags.NoPadding).Width;
        float x = Look == Look.Ghost ? 10 * K : (Width - tw - (Icon != null ? icon + 8 * K : 0)) / 2;
        if (Icon != null) { Icons.Draw(g, Icon, new RectangleF(x, (Height - icon) / 2, icon, icon), fore); x += icon + 8 * K; }
        Ui.Draw(g, Text, font, new RectangleF(x, 0, Width - x - 4 * K, Height), fore, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }
}

class Seg : Ctl
{
    public string[] Items = new string[0];
    public event Action<int> Picked;
    int index = -1, hot = -1;

    public Seg() { AccessibleRole = AccessibleRole.PageTabList; }

    public int Index { get { return index; } set { index = value; Invalidate(); } }

    RectangleF Cell(int i)
    {
        float pad = 2 * K, w = (Width - 2 * pad - (Items.Length - 1) * pad) / Items.Length;
        return new RectangleF(pad + i * (w + pad), pad, w, Height - 2 * pad);
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
        index = h;
        Invalidate();
        if (Picked != null) Picked(h);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = Begin(e);
        Ui.Fill(g, new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), 6 * K, Theme.Btn, Theme.Border);
        for (int i = 0; i < Items.Length; i++)
        {
            var r = Cell(i);
            Color fore = Theme.Text;
            if (i == index)
            {
                var fill = Enabled ? Theme.Accent : Ui.Mix(Theme.Btn, Theme.Accent, 0.35);
                Ui.Fill(g, r, 4 * K, fill, fill);
                fore = Theme.OnAccent;
            }
            else if (i == hot && Enabled) Ui.Fill(g, r, 4 * K, Theme.BtnHover, Theme.BtnHover);
            if (!Enabled) fore = Ui.Mix(i == index ? Theme.Accent : Theme.Btn, fore, 0.45);
            Ui.Draw(g, Items[i], Ui.Get(9.4f, i == index ? Ui.Semi : Ui.Regular), r, fore,
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
        var r = new RectangleF(4 * K, 4 * K, Width - 8 * K - 1, Height - 8 * K - 1);
        var fill = Fill ?? (hover && Enabled ? Theme.BtnHover : Theme.Btn);
        Ui.Fill(g, r, 4 * K, Ui.Mix(Under, fill, fade), Ui.Mix(Under, Theme.Border, fade));
        if (Fill == null) Icons.Draw(g, "plus", new RectangleF(Width / 2f - 7 * K, Height / 2f - 7 * K, 14 * K, 14 * K), Ui.Mix(Under, Theme.Dim, fade));
    }
}

class Toggle : Ctl
{
    bool on;
    public event Action Changed;

    public Toggle() { AccessibleRole = AccessibleRole.CheckButton; }

    public bool Checked { get { return on; } set { on = value; Invalidate(); } }

    protected override void OnClick(EventArgs e)
    {
        base.OnClick(e);
        Checked = !on;
        if (Changed != null) Changed();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = Begin(e);
        var r = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
        Ui.Fill(g, r, r.Height / 2, on ? Theme.Accent : Theme.Btn, on ? Theme.Accent : Theme.Border);
        float d = Height - 8 * K;
        using (var b = new SolidBrush(on ? Theme.OnAccent : Theme.Dim))
            g.FillEllipse(b, on ? Width - 4 * K - d : 4 * K, 4 * K, d, d);
    }
}

class Drop : Ctl
{
    public readonly List<string> Items = new List<string>();
    public event Action<int> Picked;
    int index = -1;

    public Drop() { AccessibleRole = AccessibleRole.ComboBox; }

    public int Index
    {
        get { return index; }
        set { index = value; Text = value >= 0 && value < Items.Count ? Items[value] : ""; }
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
            var item = new ToolStripMenuItem(Items[i]) { Checked = i == index };
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
        Ui.Fill(g, new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), 6 * K, Enabled && hover ? Theme.BtnHover : Theme.Btn, Theme.Border);
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
    public int Max = 100, Value;
    public event EventHandler Changed;

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
    }

    protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); Set(e.X); }
    protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); if (e.Button == MouseButtons.Left) Set(e.X); }

    void Set(int px)
    {
        int v = Math.Max(0, Math.Min(Max, (int)Math.Round((px - 7 * K) * Max / (Width - 14 * K))));
        if (v == Value) return;
        Value = v;
        Invalidate();
        if (Changed != null) Changed(this, EventArgs.Empty);
    }
}

class StatusLine : Ctl
{
    string text = "";
    bool warn;
    Action click;

    public StatusLine() { Cursor = Cursors.Default; }

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
    public string Title = "", Sub = "", Icon = "keyboard";
    public bool On;

    public DevItem() { AccessibleRole = AccessibleRole.PushButton; }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = Begin(e);
        if (On || hover) Ui.Fill(g, new RectangleF(0, 0, Width, Height), 6 * K, On ? Theme.AccentBg : Theme.Btn, On ? Theme.AccentBg : Theme.Btn);
        float s = 32 * K, y = (Height - s) / 2;
        Ui.Fill(g, new RectangleF(8 * K, y, s, s), 6 * K, Theme.Btn, Theme.Btn);
        Icons.Draw(g, Icon, new RectangleF(8 * K + 7 * K, y + 7 * K, 18 * K, 18 * K), On ? Theme.Accent : Theme.Dim);
        float x = 8 * K + s + 10 * K;
        Ui.Draw(g, Title, Ui.Get(9.75f, Ui.Semi), new RectangleF(x, 0, Width - x - 4 * K, Height / 2f + 1), Theme.Text, TextFormatFlags.Bottom | TextFormatFlags.EndEllipsis);
        Ui.Draw(g, Sub, Ui.Get(8.6f, Ui.Regular), new RectangleF(x, Height / 2f + 1, Width - x - 4 * K, Height / 2f), Theme.Dim, TextFormatFlags.Top | TextFormatFlags.EndEllipsis);
    }
}

class Brand : Ctl
{
    public string Tag2 = "";

    public Brand() { Cursor = Cursors.Default; }

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
        e.TextColor = e.Item.Enabled ? Theme.Text : Theme.Dim;
        base.OnRenderItemText(e);
    }

    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var r = e.ImageRectangle;
        float s = Math.Min(r.Width, r.Height);
        Icons.Draw(e.Graphics, "check", new RectangleF(r.X + (r.Width - s) / 2, r.Y + (r.Height - s) / 2, s, s), Theme.Accent);
    }
}
