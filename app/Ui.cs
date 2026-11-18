using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

static class Theme
{
    public static Color Background, Sidebar, Card, Button, ButtonHover, ButtonPressed, Border, Text, Dim, Accent, AccentBackground, OnAccent, Warn, KeyOff, PictureBackground, Track;
    public static bool Light;
    public static event Action Changed;
    public static readonly string[] Modes = { "system", "dark", "light" };

    static Theme() { Set(false); }

    public static string Mode
    {
        get { var mode = Settings.Get("theme"); return Array.IndexOf(Modes, mode) > 0 ? mode : "system"; }
        set { Settings.Set("theme", value); Apply(); }
    }

    public static bool SystemLight() { return Personalize("AppsUseLightTheme"); }

    public static bool TaskbarLight() { return Personalize("SystemUsesLightTheme"); }

    static bool Personalize(string name)
    {
        try
        {
            using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                return key != null && Equals(key.GetValue(name), 1);
        }
        catch { return false; }
    }

    public static void Apply()
    {
        var mode = Mode;
        bool light = mode == "light" || (mode == "system" && SystemLight());
        if (light == Light) return;
        Set(light);
        if (Changed != null) Changed();
    }

    public static Color FromHex(int value) { return Color.FromArgb(value >> 16 & 0xFF, value >> 8 & 0xFF, value & 0xFF); }

    static void Set(bool light)
    {
        Light = light;
        if (light)
        {
            Background = FromHex(0xF7F7F7); Sidebar = FromHex(0xEDEDED); Card = FromHex(0xFFFFFF); Button = FromHex(0xECECEC); ButtonHover = FromHex(0xE0E0E0); ButtonPressed = FromHex(0xDADADA); Border = FromHex(0xD4D4D4);
            Text = FromHex(0x1A1A1A); Dim = FromHex(0x5A5A5A); Accent = FromHex(0x1D7A60); AccentBackground = FromHex(0xD9EDE6); OnAccent = FromHex(0xFFFFFF);
            Warn = FromHex(0x9A5B00); KeyOff = FromHex(0xD6D6D6); PictureBackground = FromHex(0xBDBDBD); Track = FromHex(0xC8C8C8);
        }
        else
        {
            Background = FromHex(0x1C1C1C); Sidebar = FromHex(0x161616); Card = FromHex(0x232323); Button = FromHex(0x2B2B2B); ButtonHover = FromHex(0x353535); ButtonPressed = FromHex(0x242424); Border = FromHex(0x3A3A3A);
            Text = FromHex(0xE8E8E8); Dim = FromHex(0xA3A3A3); Accent = FromHex(0x3FA98A); AccentBackground = FromHex(0x26302D); OnAccent = FromHex(0x0C1A15);
            Warn = FromHex(0xE0A458); KeyOff = FromHex(0x2E2E2E); PictureBackground = FromHex(0x151515); Track = FromHex(0x444444);
        }
    }
}

static class Ui
{
    public const string Regular = "Segoe UI", Semibold = "Segoe UI Semibold";
    public static float DpiScale = 1;
    public static Action<ContextMenuStrip> Popped;
    static readonly List<KeyValuePair<Control, Action>> styles = new List<KeyValuePair<Control, Action>>();
    static readonly List<Tuple<Control, float, string>> fonts = new List<Tuple<Control, float, string>>();
    static readonly Dictionary<string, Font> cache = new Dictionary<string, Font>();
    public static readonly ToolStripRenderer Menus = new MenuRenderer();

    public static T Style<T>(T control, Action apply) where T : Control
    {
        apply();
        styles.Add(new KeyValuePair<Control, Action>(control, apply));
        return control;
    }

    public static T Font<T>(T control, float points, string face) where T : Control
    {
        control.Font = WindowHelpers.PixelFont(points, DpiScale, face);
        fonts.Add(Tuple.Create((Control)control, points, face));
        return control;
    }

    public static void Restyle() { styles.RemoveAll(s => s.Key.IsDisposed); foreach (var s in styles) s.Value(); }

    public static void Refont() { fonts.RemoveAll(entry => entry.Item1.IsDisposed); foreach (var entry in fonts) entry.Item1.Font = WindowHelpers.PixelFont(entry.Item2, DpiScale, entry.Item3); }

    public static Font Get(float points, string face)
    {
        var key = points + "|" + face + "|" + DpiScale;
        Font font;
        if (!cache.TryGetValue(key, out font)) cache[key] = font = WindowHelpers.PixelFont(points, DpiScale, face);
        return font;
    }

    public static Label Text(Control parent, string text, int x, int y, Func<Color> color, float points = 9.75f, string face = Regular)
    {
        var label = new Label { Text = text, Location = new Point(x, y), AutoSize = true, BackColor = Color.Transparent, UseMnemonic = false };
        Style(label, () => label.ForeColor = color());
        Font(label, points, face);
        parent.Controls.Add(label);
        return label;
    }

    public const int Charging = 1000, Asleep = -2, Usb = -3;

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

    public static int BatteryWidth(float scale) { return (int)Math.Round(16 * scale) + (int)Math.Round(2 * scale); }
    public static int BatteryHeight(float scale) { return (int)Math.Round(9 * scale); }

    // The USB symbol in the battery's 18 x 9 box, antialiased: whole pixels made it look rough.
    static void UsbMark(Graphics graphics, int x, int y, float scale, Color ink)
    {
        var offset = graphics.PixelOffsetMode;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        Func<float, float, PointF> at = (pointX, pointY) => new PointF(x + pointX * scale, y + pointY * scale);
        using (var pen = new Pen(ink, 1.1f * scale) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
        using (var brush = new SolidBrush(ink))
        {
            graphics.FillEllipse(brush, x + 0.2f * scale, y + 3.1f * scale, 2.8f * scale, 2.8f * scale);
            graphics.DrawLine(pen, at(2.5f, 4.5f), at(14, 4.5f));
            graphics.FillPolygon(brush, new[] { at(13.3f, 2.4f), at(18, 4.5f), at(13.3f, 6.6f) });
            graphics.DrawLines(pen, new[] { at(4.8f, 4.5f), at(7.4f, 1.6f), at(10, 1.6f) });
            graphics.FillEllipse(brush, x + 9.6f * scale, y + 0.3f * scale, 2.6f * scale, 2.6f * scale);
            graphics.DrawLines(pen, new[] { at(6.6f, 4.5f), at(9.4f, 7.4f), at(11, 7.4f) });
            graphics.FillRectangle(brush, x + 10.6f * scale, y + 6.1f * scale, 2.6f * scale, 2.6f * scale);
        }
        graphics.PixelOffsetMode = offset;
    }

    public static void Battery(Graphics graphics, float left, float top, int percent, float scale, Color? color = null)
    {
        int x = (int)Math.Round(left), y = (int)Math.Round(top);
        int line = Math.Max(1, (int)Math.Round(scale)), width = (int)Math.Round(16 * scale), height = BatteryHeight(scale), nub = (int)Math.Round(3 * scale);
        var ink = color ?? Theme.Text;
        var smoothing = graphics.SmoothingMode;
        graphics.SmoothingMode = SmoothingMode.None;
        if (percent == Usb)
        {
            UsbMark(graphics, x, y, scale, ink);
            graphics.SmoothingMode = smoothing;
            return;
        }
        if (percent == Asleep)
        {
            using (var brush = new SolidBrush(ink))
                for (int row = 0; row < Moon.Length; row++)
                    for (int from = 0; from < Moon[row].Length; from++)
                    {
                        if (Moon[row][from] != '#') continue;
                        int to = from;
                        while (to + 1 < Moon[row].Length && Moon[row][to + 1] == '#') to++;
                        int cell = Math.Max(1, (int)Math.Round(scale)), cellLeft = (int)Math.Round(from * scale);
                        graphics.FillRectangle(brush, x + cellLeft, y + (int)Math.Round(row * scale), (int)Math.Round(to * scale) + cell - cellLeft, cell);
                        from = to;
                    }
            graphics.SmoothingMode = smoothing;
            return;
        }
        using (var outline = new SolidBrush(ink))
        {
            graphics.FillRectangle(outline, x, y, width, line);
            graphics.FillRectangle(outline, x, y + height - line, width, line);
            graphics.FillRectangle(outline, x, y, line, height);
            graphics.FillRectangle(outline, x + width - line, y, line, height);
            graphics.FillRectangle(outline, x + width, y + nub, BatteryWidth(scale) - width, height - nub * 2);
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
                    int upper = y + (int)Math.Round((row + 1) * scale), lower = y + (int)Math.Round((row + 2) * scale);
                    int from = x + (int)Math.Round((5 + Spark[row * 2]) * scale), to = x + (int)Math.Round((6 + Spark[row * 2 + 1]) * scale);
                    spark.Union(new Rectangle(from, upper, to - from, lower - upper));
                }
            var clip = graphics.Clip;
            graphics.ExcludeClip(spark);
            using (var brush = new SolidBrush(percent <= 20 ? Theme.Warn : ink)) graphics.FillRectangle(brush, level);
            graphics.Clip = clip;
            if (charging)
            {
                graphics.ExcludeClip(level);
                using (var brush = new SolidBrush(ink)) graphics.FillRegion(brush, spark);
                graphics.Clip = clip;
            }
            clip.Dispose();
        }
        graphics.SmoothingMode = smoothing;
    }

    public static T Add<T>(Control parent, T control, int x, int y, int width, int height) where T : Control
    {
        control.Bounds = new Rectangle(x, y, width, height);
        control.TabIndex = parent.Controls.Count;
        if (!(control is DrawnControl) && !(control is TextBoxBase)) control.TabStop = false;
        parent.Controls.Add(control);
        control.BringToFront();
        return control;
    }

    public static GraphicsPath Round(RectangleF bounds, float radius)
    {
        var path = new GraphicsPath();
        float diameter = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
        if (diameter <= 0) { path.AddRectangle(bounds); return path; }
        path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    public static void Fill(Graphics graphics, RectangleF bounds, float radius, Color fill, Color edge)
    {
        using (var path = Round(bounds, radius))
        using (var brush = new SolidBrush(fill))
        using (var pen = new Pen(edge))
        {
            graphics.FillPath(brush, path);
            if (edge != fill) graphics.DrawPath(pen, path);
        }
    }

    public static Color Mix(Color from, Color to, double amount)
    {
        return Color.FromArgb((int)(from.R + (to.R - from.R) * amount), (int)(from.G + (to.G - from.G) * amount), (int)(from.B + (to.B - from.B) * amount));
    }

    // The text of something that can't be changed now.
    public static Color Off { get { return Mix(Theme.Card, Theme.Dim, 0.45); } }

    public static void Draw(Graphics graphics, string text, Font font, RectangleF bounds, Color color, TextFormatFlags flags)
    {
        TextRenderer.DrawText(graphics, text, font, Rectangle.Round(bounds), color, flags | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
    }
}

static class Icons
{
    public static void Draw(Graphics graphics, string name, RectangleF bounds, Color color)
    {
        var state = graphics.Save();
        graphics.TranslateTransform(bounds.X, bounds.Y);
        graphics.ScaleTransform(bounds.Width / 24f, bounds.Height / 24f);
        using (var pen = new Pen(color, 1.7f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
        using (var brush = new SolidBrush(color))
            switch (name)
            {
                case "refresh":
                    graphics.DrawArc(pen, 4, 4, 16, 16, -10, -305);
                    graphics.DrawLines(pen, new[] { new PointF(20, 4), new PointF(20, 10.5f), new PointF(13.5f, 10.5f) });
                    break;
                case "minus": graphics.DrawLine(pen, 6, 12, 18, 12); break;
                case "lines": graphics.DrawLine(pen, 5, 7, 19, 7); graphics.DrawLine(pen, 5, 12, 19, 12); graphics.DrawLine(pen, 5, 17, 13, 17); break;
                case "power": graphics.DrawArc(pen, 5, 5.5f, 14, 14, -60, 300); graphics.DrawLine(pen, 12, 3.5f, 12, 11); break;
                case "close": graphics.DrawLine(pen, 6.5f, 6.5f, 17.5f, 17.5f); graphics.DrawLine(pen, 17.5f, 6.5f, 6.5f, 17.5f); break;
                case "download": graphics.DrawLine(pen, 12, 4, 12, 15); graphics.DrawLines(pen, new[] { new PointF(7.5f, 10.5f), new PointF(12, 15), new PointF(16.5f, 10.5f) }); graphics.DrawLine(pen, 5, 19.5f, 19, 19.5f); break;
                case "plus": graphics.DrawLine(pen, 12, 5, 12, 19); graphics.DrawLine(pen, 5, 12, 19, 12); break;
                case "chevron": graphics.DrawLines(pen, new[] { new PointF(7, 10), new PointF(12, 15), new PointF(17, 10) }); break;
                case "check": graphics.DrawLines(pen, new[] { new PointF(5, 12.5f), new PointF(10, 17.5f), new PointF(19, 7) }); break;
                case "gear":
                    {
                        var cog = new List<PointF>();
                        for (int i = 0; i < 8; i++)
                            foreach (var tooth in new[] { new[] { 7f, -15f }, new[] { 9.8f, -9f }, new[] { 9.8f, 9f }, new[] { 7f, 15f } })
                            {
                                double angle = (i * 45 + tooth[1]) * Math.PI / 180;
                                cog.Add(new PointF(12 + (float)Math.Cos(angle) * tooth[0], 12 + (float)Math.Sin(angle) * tooth[0]));
                            }
                        graphics.DrawPolygon(pen, cog.ToArray());
                        graphics.DrawEllipse(pen, 9, 9, 6, 6);
                    }
                    break;
                case "mouse":
                    using (var path = Ui.Round(new RectangleF(6, 3, 12, 18), 6)) graphics.DrawPath(pen, path);
                    graphics.DrawLine(pen, 12, 7, 12, 10);
                    break;
                case "headset":
                    graphics.DrawLine(pen, 4, 15, 4, 12);
                    graphics.DrawArc(pen, 4, 4, 16, 16, 180, 180);
                    graphics.DrawLine(pen, 20, 12, 20, 15);
                    using (var path = Ui.Round(new RectangleF(3, 14, 4, 6), 1.5f)) graphics.DrawPath(pen, path);
                    using (var path = Ui.Round(new RectangleF(17, 14, 4, 6), 1.5f)) graphics.DrawPath(pen, path);
                    break;
                case "speaker":
                    using (var path = Ui.Round(new RectangleF(6, 3, 12, 18), 2.5f)) graphics.DrawPath(pen, path);
                    graphics.DrawEllipse(pen, 9, 11.5f, 6, 6);
                    graphics.FillEllipse(brush, 11, 6, 2, 2);
                    break;
                case "light":
                    using (var path = Ui.Round(new RectangleF(3, 4, 18, 6), 2)) graphics.DrawPath(pen, path);
                    graphics.DrawLine(pen, 12, 10, 12, 20);
                    graphics.DrawLine(pen, 8, 20, 16, 20);
                    break;
                case "device":
                    using (var path = Ui.Round(new RectangleF(4, 4, 16, 16), 3)) graphics.DrawPath(pen, path);
                    graphics.FillEllipse(brush, 10.5f, 10.5f, 3, 3);
                    break;
                case "keyboard":
                    using (var path = Ui.Round(new RectangleF(2, 6, 20, 12), 2)) graphics.DrawPath(pen, path);
                    foreach (var x in new[] { 6, 10, 14, 18 }) graphics.FillEllipse(brush, x - 1, 9, 2, 2);
                    graphics.DrawLine(pen, 7, 14, 17, 14);
                    break;
            }
        graphics.Restore(state);
    }
}

class DrawnControl : Control
{
    protected bool hover, down, key;
    Timer anim;
    float from, to, now;
    DateTime start;
    int length;

    public DrawnControl()
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

    protected void SetValueWithoutAnimation(float value)
    {
        from = to = now = value;
        if (anim != null) anim.Stop();
        Invalidate();
    }

    protected void Glide(float value, int milliseconds)
    {
        if (!WindowHelpers.Animations || !Visible || !IsHandleCreated) { SetValueWithoutAnimation(value); return; }
        from = now;
        to = value;
        length = milliseconds;
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
        double progress = Math.Min(1, (DateTime.UtcNow - start).TotalMilliseconds / length);
        now = (float)(from + (to - from) * (1 - Math.Pow(1 - progress, 3)));
        if (progress >= 1) anim.Stop();
        Invalidate();
    }

    protected void DrawFocusRing(Graphics graphics, float radius, Color color)
    {
        if (!Focused || !ShowFocusCues || !Enabled) return;
        using (var pen = new Pen(color, 1.5f * DpiScale))
        using (var path = Ui.Round(new RectangleF(DpiScale, DpiScale, Width - 2 * DpiScale - 1, Height - 2 * DpiScale - 1), radius))
            graphics.DrawPath(pen, path);
    }

    protected void DrawFocusRing(Graphics graphics, float radius) { DrawFocusRing(graphics, radius, Theme.Text); }

    protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { down = true; Invalidate(); } base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { if (down) { down = false; Invalidate(); } base.OnMouseUp(e); }
    protected override void OnMouseCaptureChanged(EventArgs e) { if (down) { down = false; Invalidate(); } base.OnMouseCaptureChanged(e); }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        bool inside = ClientRectangle.Contains(e.Location);
        if (inside != hover) { hover = inside; Invalidate(); }
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

    protected override bool IsInputKey(Keys keyData)
    {
        if (!Clicks && (keyData == Keys.Left || keyData == Keys.Right || keyData == Keys.Up || keyData == Keys.Down)) return true;
        return base.IsInputKey(keyData);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && anim != null) anim.Dispose();
        base.Dispose(disposing);
    }

    protected static float DpiScale { get { return Ui.DpiScale; } }
    protected Color Under { get { return Parent != null ? Parent.BackColor : Theme.Background; } }

    protected Graphics Begin(PaintEventArgs e)
    {
        var graphics = e.Graphics;
        graphics.Clear(Under);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        return graphics;
    }

    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
    protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }
}

class RoundedBox : Control
{
    public RoundedBox()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Ui.Style(this, () => BackColor = Theme.Card);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var graphics = e.Graphics;
        graphics.Clear(Parent != null ? Parent.BackColor : Theme.Background);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        Ui.Fill(graphics, new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), 8 * Ui.DpiScale, Theme.Card, Theme.Border);
        base.OnPaint(e);
    }
}

enum ButtonStyle { Normal, Primary, Ghost, Solid }

class FlatButton : DrawnControl
{
    public ButtonStyle Style;
    public string Icon;
    public bool On;

    public FlatButton() { AccessibleRole = AccessibleRole.PushButton; }

    protected override void OnPaint(PaintEventArgs e)
    {
        var graphics = Begin(e);
        Color fill, edge, fore;
        bool lit = Enabled && hover;
        if (Style == ButtonStyle.Primary || Style == ButtonStyle.Solid) { fill = edge = Pressed ? Ui.Mix(Theme.Accent, Color.Black, 0.18) : lit ? Ui.Mix(Theme.Accent, Theme.Text, 0.12) : Theme.Accent; fore = Theme.OnAccent; }
        else if (Style == ButtonStyle.Ghost) { fill = edge = Pressed ? Theme.ButtonPressed : On ? Theme.AccentBackground : lit ? Theme.Button : Under; fore = On || lit ? Theme.Text : Theme.Dim; }
        else { fill = Pressed ? Theme.ButtonPressed : lit ? Theme.ButtonHover : Theme.Button; edge = Theme.Border; fore = Theme.Text; }
        if (!Enabled) { fill = Ui.Mix(Under, fill, 0.5); edge = Ui.Mix(Under, edge, 0.5); fore = Ui.Mix(Under, fore, 0.45); }
        Ui.Fill(graphics, new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), 6 * DpiScale, fill, edge);
        if (Style == ButtonStyle.Solid && On)
            using (var pen = new Pen(Theme.OnAccent, 1.5f * DpiScale))
            using (var path = Ui.Round(new RectangleF(2.5f * DpiScale, 2.5f * DpiScale, Width - 5 * DpiScale - 1, Height - 5 * DpiScale - 1), 4.5f * DpiScale))
                graphics.DrawPath(pen, path);
        DrawFocusRing(graphics, 6 * DpiScale);
        var font = Ui.Get(9.75f, Style == ButtonStyle.Primary || Style == ButtonStyle.Solid ? Ui.Semibold : Ui.Regular);
        float icon = 16 * DpiScale;
        if (Text == "")
        {
            if (Icon != null) Icons.Draw(graphics, Icon, new RectangleF((Width - icon) / 2, (Height - icon) / 2, icon, icon), fore);
            return;
        }
        float textWidth = TextRenderer.MeasureText(graphics, Text, font, Size.Empty, TextFormatFlags.NoPadding).Width;
        float x = (Style == ButtonStyle.Ghost || Style == ButtonStyle.Solid) && Icon != null ? 10 * DpiScale : (Width - textWidth - (Icon != null ? icon + 8 * DpiScale : 0)) / 2;
        if (Icon != null) { Icons.Draw(graphics, Icon, new RectangleF(x, (Height - icon) / 2, icon, icon), fore); x += icon + 8 * DpiScale; }
        Ui.Draw(graphics, Text, font, new RectangleF(x, 0, Width - x - 4 * DpiScale, Height), fore, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }
}

class SegmentedPicker : DrawnControl
{
    public string[] Items = new string[0];
    public int[] Off = new int[0];
    public event Action<int> Picked;
    int index = -1, hot = -1;


    readonly Timer later = new Timer { Interval = 250 };

    public SegmentedPicker()
    {
        AccessibleRole = AccessibleRole.PageTabList;
        later.Tick += (s, e) => { later.Stop(); if (Picked != null) Picked(index); };
    }

    public int Index { get { return index; } set { if (value == index) return; index = value; SetValueWithoutAnimation(value); } }

    public void Pick(int i)
    {
        if (!Enabled || !Visible || Off.Contains(i)) throw new InvalidOperationException(AccessibleName + " can't be changed now");
        Slide(i);
        if (Picked != null) Picked(i);
    }

    protected override bool Clicks { get { return false; } }

    RectangleF Cell(float i)
    {
        float pad = 2 * DpiScale, cellWidth = (Width - 2 * pad - (Items.Length - 1) * pad) / Items.Length;
        return new RectangleF(pad + i * (cellWidth + pad), pad, cellWidth, Height - 2 * pad);
    }

    void Slide(int i)
    {
        if (i == index) return;
        bool had = index >= 0;
        index = i;
        if (had) Glide(i, 160); else SetValueWithoutAnimation(i);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!Enabled || Items.Length == 0) return;
        int step = e.KeyCode == Keys.Left || e.KeyCode == Keys.Up ? -1 : e.KeyCode == Keys.Right || e.KeyCode == Keys.Down ? 1 : 0;
        if (step == 0) return;
        e.Handled = true;
        int to = Math.Max(0, Math.Min(Items.Length - 1, index < 0 ? 0 : index + step));
        while (Off.Contains(to) && to + step >= 0 && to + step < Items.Length) to += step;
        if (to == index || Off.Contains(to)) return;
        Slide(to);
        later.Stop();
        later.Start();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) later.Dispose();
        base.Dispose(disposing);
    }

    int Hit(Point point)
    {
        for (int i = 0; i < Items.Length; i++) if (!Off.Contains(i) && Cell(i).Contains(point)) return i;
        return -1;
    }

    protected override void OnMouseMove(MouseEventArgs e) { int hit = Hit(e.Location); if (hit != hot) { hot = hit; Invalidate(); } base.OnMouseMove(e); }
    protected override void OnMouseLeave(EventArgs e) { hot = -1; base.OnMouseLeave(e); }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        int hit = Hit(e.Location);
        if (!Enabled || e.Button != MouseButtons.Left || hit < 0) return;
        later.Stop();
        Slide(hit);
        if (Picked != null) Picked(hit);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var graphics = Begin(e);
        Ui.Fill(graphics, new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), 6 * DpiScale, Theme.Button, Theme.Border);
        if (hot >= 0 && hot != index && Enabled) Ui.Fill(graphics, Cell(hot), 4 * DpiScale, Theme.ButtonHover, Theme.ButtonHover);
        var fill = Enabled ? Theme.Accent : Ui.Mix(Theme.Button, Theme.Accent, 0.35);
        if (index >= 0 && index < Items.Length) Ui.Fill(graphics, Cell(Now), 4 * DpiScale, fill, fill);
        DrawFocusRing(graphics, 6 * DpiScale);
        for (int i = 0; i < Items.Length; i++)
        {
            var cell = Cell(i);
            bool lit = Math.Abs(Now - i) < 0.5f && index >= 0;
            Color fore = lit ? Theme.OnAccent : Theme.Text;
            if (!Enabled || Off.Contains(i)) fore = Ui.Mix(lit ? fill : Theme.Button, fore, lit ? 0.7 : 0.45);
            Ui.Draw(graphics, Items[i], Ui.Get(9.4f, lit ? Ui.Semibold : Ui.Regular), cell, fore,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
    }
}

class Swatch : DrawnControl
{
    public Color? Fill;
    public bool On;

    public Swatch() { AccessibleRole = AccessibleRole.PushButton; }

    protected override void OnPaint(PaintEventArgs e)
    {
        var graphics = Begin(e);
        double fade = Enabled ? 1 : 0.35;
        if (On) using (var path = Ui.Round(new RectangleF(DpiScale, DpiScale, Width - 2 * DpiScale, Height - 2 * DpiScale), 7 * DpiScale)) using (var pen = new Pen(Ui.Mix(Under, Theme.Text, fade), 2 * DpiScale)) graphics.DrawPath(pen, path);
        float inset = Pressed ? 5 * DpiScale : 4 * DpiScale;
        var box = new RectangleF(inset, inset, Width - 2 * inset - 1, Height - 2 * inset - 1);
        var fill = Fill ?? (Pressed ? Theme.ButtonPressed : hover && Enabled ? Theme.ButtonHover : Theme.Button);
        bool ring = Focused && ShowFocusCues && Enabled;
        Ui.Fill(graphics, box, 4 * DpiScale, Ui.Mix(Under, fill, fade), ring ? Theme.Text : Ui.Mix(Under, Theme.Border, fade));
        if (Fill == null) Icons.Draw(graphics, "plus", new RectangleF(Width / 2f - 7 * DpiScale, Height / 2f - 7 * DpiScale, 14 * DpiScale, 14 * DpiScale), Ui.Mix(Under, Theme.Dim, fade));
    }
}

class Toggle : DrawnControl
{
    bool on;
    Control follower;
    public event Action Changed;

    public Toggle()
    {
        AccessibleRole = AccessibleRole.CheckButton;
        SetStyle(ControlStyles.StandardDoubleClick, false);
    }

    public void Follow(Control label)
    {
        follower = label;
        label.Click += (s, e) => { if (Enabled) OnClick(EventArgs.Empty); };
        label.DoubleClick += (s, e) => { if (Enabled) OnClick(EventArgs.Empty); };
    }

    public bool Checked { get { return on; } set { on = value; SetValueWithoutAnimation(value ? 1 : 0); } }

    protected override void OnClick(EventArgs e)
    {
        base.OnClick(e);
        on = !on;
        Glide(on ? 1 : 0, 140);
        if (Changed != null) Changed();
    }

    // The label fades with the switch. Checked at each paint too, since a theme change sets the label's colour back.
    void FadeLabel()
    {
        if (follower == null) return;
        var color = Enabled ? Theme.Text : Ui.Off;
        if (follower.ForeColor != color) follower.ForeColor = color;
    }

    protected override void OnEnabledChanged(EventArgs e) { FadeLabel(); base.OnEnabledChanged(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        FadeLabel();
        var graphics = Begin(e);
        var box = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
        float position = Now;
        float faded = Enabled ? 0 : 0.6f;
        Ui.Fill(graphics, box, box.Height / 2, Ui.Mix(Ui.Mix(Theme.Button, Theme.Accent, position), Theme.Card, faded), Ui.Mix(Ui.Mix(Theme.Border, Theme.Accent, position), Theme.Card, faded));
        DrawFocusRing(graphics, box.Height / 2);
        float knob = Height - 8 * DpiScale;
        float stretch = Pressed ? 4 * DpiScale : 0;
        using (var brush = new SolidBrush(Ui.Mix(Ui.Mix(Theme.Dim, Theme.OnAccent, position), Theme.Card, faded)))
        using (var path = Ui.Round(new RectangleF(4 * DpiScale + position * (Width - 8 * DpiScale - knob - stretch), 4 * DpiScale, knob + stretch, knob), knob / 2))
            graphics.FillPath(brush, path);
    }
}

class Dropdown : DrawnControl
{
    public readonly List<string> Items = new List<string>();
    public event Action<int> Picked;
    public Func<string, string> Label;
    int index = -1;

    public Dropdown() { AccessibleRole = AccessibleRole.ComboBox; }

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
        var menu = new ContextMenuStrip { Renderer = Ui.Menus, Font = Ui.Get(9.75f, Ui.Regular), ShowImageMargin = false, ShowCheckMargin = true };
        menu.BackColor = Theme.Card;
        menu.ForeColor = Theme.Text;
        for (int i = 0; i < Items.Count; i++)
        {
            int picked = i;
            var item = new ToolStripMenuItem(Label != null ? Label(Items[i]) : Items[i]) { Checked = i == index };
            item.Click += (s, args) => { Index = picked; if (Picked != null) Picked(picked); };
            menu.Items.Add(item);
        }
        menu.MinimumSize = new Size(Width, 0);
        menu.Closed += (s, args) => BeginInvoke((Action)menu.Dispose);
        menu.Show(this, new Point(0, Height + (int)(2 * DpiScale)));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var graphics = Begin(e);
        Color fore = Enabled ? Theme.Text : Ui.Mix(Theme.Button, Theme.Text, 0.45);
        Ui.Fill(graphics, new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), 6 * DpiScale, Pressed ? Theme.ButtonPressed : Enabled && hover ? Theme.ButtonHover : Theme.Button, Theme.Border);
        DrawFocusRing(graphics, 6 * DpiScale);
        Ui.Draw(graphics, Text, Ui.Get(9.75f, Ui.Regular), new RectangleF(10 * DpiScale, 0, Width - 38 * DpiScale, Height), fore, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        Icons.Draw(graphics, "chevron", new RectangleF(Width - 26 * DpiScale, (Height - 16 * DpiScale) / 2, 16 * DpiScale, 16 * DpiScale), Enabled ? Theme.Dim : fore);
    }
}

class TextField : DrawnControl
{
    public readonly TextBox Box = new TextBox { BorderStyle = BorderStyle.None };

    public TextField()
    {
        Cursor = Cursors.IBeam;
        TabStop = false;
        Controls.Add(Box);
        Ui.Style(Box, () => { Box.BackColor = Theme.Button; Box.ForeColor = Theme.Text; });
        Box.GotFocus += (s, e) => Invalidate();
        Box.LostFocus += (s, e) => Invalidate();
    }

    protected override void OnClick(EventArgs e) { base.OnClick(e); Box.Focus(); }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        Box.Font = Ui.Get(9.75f, Ui.Regular);
        Box.Width = Width - (int)(20 * DpiScale);
        Box.Location = new Point((int)(10 * DpiScale), (Height - Box.Height) / 2);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var graphics = Begin(e);
        Ui.Fill(graphics, new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), 6 * DpiScale, Theme.Button, Box.Focused ? Theme.Accent : Theme.Border);
    }
}

class Slider : DrawnControl
{
    public int Max = 100, Value, Step = 5;
    public event EventHandler Changed;
    int wheel, grab;

    protected override bool Clicks { get { return false; } }

    protected override bool IsInputKey(Keys keyData)
    {
        return keyData == Keys.PageUp || keyData == Keys.PageDown || keyData == Keys.Home || keyData == Keys.End || base.IsInputKey(keyData);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!Enabled) return;
        int value;
        switch (e.KeyCode)
        {
            case Keys.Left: case Keys.Down: value = Value - 1; break;
            case Keys.Right: case Keys.Up: value = Value + 1; break;
            case Keys.PageDown: value = Value - 10; break;
            case Keys.PageUp: value = Value + 10; break;
            case Keys.Home: value = 0; break;
            case Keys.End: value = Max; break;
            default: return;
        }
        e.Handled = true;
        SetValue(value);
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
        SetValue(Value + notches * Step);
    }

    public void SetValue(int value)
    {
        value = Math.Max(0, Math.Min(Max, value));
        if (value == Value) return;
        Value = value;
        Invalidate();
        if (Changed != null) Changed(this, EventArgs.Empty);
    }

    float X(int value) { return 7 * DpiScale + (float)((long)value * (Width - 14 * DpiScale) / Max); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var graphics = Begin(e);
        var color = Enabled ? Theme.Accent : Theme.Border;
        float mid = Height / 2f;
        using (var track = new SolidBrush(Theme.Track)) graphics.FillRectangle(track, 7 * DpiScale, mid - 2 * DpiScale, Width - 14 * DpiScale, 4 * DpiScale);
        using (var brush = new SolidBrush(color))
        {
            graphics.FillRectangle(brush, 7 * DpiScale, mid - 2 * DpiScale, X(Value) - 7 * DpiScale, 4 * DpiScale);
            graphics.FillEllipse(brush, X(Value) - 7 * DpiScale, mid - 7 * DpiScale, 14 * DpiScale, 14 * DpiScale);
        }
        if (Focused && ShowFocusCues && Enabled)
            using (var pen = new Pen(Theme.Text, 2 * DpiScale)) graphics.DrawEllipse(pen, X(Value) - 6 * DpiScale, mid - 6 * DpiScale, 12 * DpiScale, 12 * DpiScale);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        float at = X(Value);
        grab = Math.Abs(e.X - at) <= 9 * DpiScale ? (int)(e.X - at) : 0;
        Set(e.X - grab);
    }

    protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); if (e.Button == MouseButtons.Left) Set(e.X - grab); }

    void Set(int pixelX)
    {
        if (Max > 0) SetValue((int)Math.Round((pixelX - 7 * DpiScale) * Max / (Width - 14 * DpiScale)));
    }
}

class StatusLine : DrawnControl
{
    string text = "";
    bool warn;
    Action click;

    public StatusLine() { Cursor = Cursors.Default; TabStop = false; }

    public string Shown { get { return text; } }

    public void Set(string message, bool isWarning, Action action)
    {
        text = message ?? "";
        warn = isWarning;
        click = action;
        Cursor = action != null ? Cursors.Hand : Cursors.Default;
        AccessibleName = text;
        Invalidate();
    }

    protected override void OnClick(EventArgs e) { base.OnClick(e); if (click != null) click(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var graphics = Begin(e);
        if (text == "") return;
        using (var brush = new SolidBrush(warn ? Theme.Warn : Theme.Accent)) graphics.FillEllipse(brush, 0, Height / 2f - 4 * DpiScale, 8 * DpiScale, 8 * DpiScale);
        var font = Ui.Get(9f, Ui.Regular);
        var flags = TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;
        int width = Width - (int)(16 * DpiScale), height = Math.Min(Height, TextRenderer.MeasureText(graphics, text, font, new Size(width, Height), flags).Height);
        TextRenderer.DrawText(graphics, text, font, new Rectangle((int)(16 * DpiScale), (Height - height) / 2, width, height), warn ? Theme.Text : Theme.Dim, flags);
    }
}

class DeviceList : Control
{
    int offset, content, target;
    readonly Timer glide = new Timer { Interval = 15 };

    public DeviceList()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Ui.Style(this, () => BackColor = Theme.Sidebar);
        glide.Tick += (s, e) => Glide();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) glide.Dispose();
        base.Dispose(disposing);
    }

    public int Offset { get { return offset; } }

    public void Watch(Control item)
    {
        item.GotFocus += (s, e) => Reveal(item);
    }

    public void Fit(int height)
    {
        content = height;
        ScrollTo(offset);
    }

    public void Reveal(Control item)
    {
        if (item.Top < 0) ScrollTo(offset + item.Top);
        else if (item.Bottom > Height) ScrollTo(offset + item.Bottom - Height);
    }

    void Wheel(int delta)
    {
        target = Clamp(target - delta * (int)Math.Round(50 * Ui.DpiScale) / 120);
        if (target != offset) glide.Start();
    }

    void Glide()
    {
        int gap = target - offset;
        if (gap == 0) { glide.Stop(); return; }
        int step = (int)Math.Round(gap * 0.25);
        if (step == 0) step = Math.Sign(gap);
        Shift(offset + step);
    }

    int Clamp(int to) { return Math.Max(0, Math.Min(to, content - Height)); }

    protected override void OnMouseWheel(MouseEventArgs e) { Wheel(e.Delta); base.OnMouseWheel(e); }

    protected override void OnResize(EventArgs e) { base.OnResize(e); ScrollTo(offset); }

    protected override void ScaleControl(SizeF factor, BoundsSpecified specified)
    {
        offset = (int)Math.Round(offset * factor.Height);
        target = (int)Math.Round(target * factor.Height);
        content = (int)Math.Round(content * factor.Height);
        base.ScaleControl(factor, specified);
    }

    void ScrollTo(int to)
    {
        glide.Stop();
        target = Clamp(to);
        Shift(target);
    }

    void Shift(int to)
    {
        to = Clamp(to);
        if (to != offset)
        {
            SuspendLayout();
            foreach (Control control in Controls) control.Top += offset - to;
            ResumeLayout();
            offset = to;
        }
        foreach (Control control in Controls) control.Invalidate();
    }

    public void Fade(Graphics graphics, Control item)
    {
        float zone = 32 * Ui.DpiScale, ramp = 46 * Ui.DpiScale;
        DrawEdgeFade(graphics, item, Height - zone, zone, false, (content - Height - offset) / ramp);
        DrawEdgeFade(graphics, item, 0, zone, true, offset / ramp);
    }

    static void DrawEdgeFade(Graphics graphics, Control item, float top, float height, bool fromTop, float strength)
    {
        strength = Math.Min(1, strength);
        var area = new RectangleF(0, top - item.Top, item.Width, height);
        if (strength <= 0 || area.Bottom <= 0 || area.Top >= item.Height) return;
        Color clear = Color.FromArgb(0, Theme.Sidebar), solid = Color.FromArgb((int)Math.Round(255 * strength), Theme.Sidebar);
        using (var brush = new LinearGradientBrush(new RectangleF(0, area.Top - 1, 1, area.Height + 2), fromTop ? solid : clear, fromTop ? clear : solid, LinearGradientMode.Vertical))
            graphics.FillRectangle(brush, area);
    }
}

class DeviceListItem : DrawnControl
{
    public string Title = "", Subtitle = "", Icon = "keyboard", Value = "";
    public int Level = -1;
    public bool On;

    public DeviceListItem() { AccessibleRole = AccessibleRole.PushButton; }

    protected override void OnPaint(PaintEventArgs e)
    {
        var graphics = Begin(e);
        if (On || hover) { var color = On ? Theme.AccentBackground : Pressed ? Theme.ButtonPressed : Theme.Button; Ui.Fill(graphics, new RectangleF(0, 0, Width, Height), 6 * DpiScale, color, color); }
        DrawFocusRing(graphics, 6 * DpiScale);
        float s = 32 * DpiScale, y = (Height - s) / 2;
        Ui.Fill(graphics, new RectangleF(8 * DpiScale, y, s, s), 6 * DpiScale, Theme.Button, Theme.Button);
        Icons.Draw(graphics, Icon, new RectangleF(8 * DpiScale + 7 * DpiScale, y + 7 * DpiScale, 18 * DpiScale, 18 * DpiScale), On ? Theme.Accent : Theme.Dim);
        float x = 8 * DpiScale + s + 10 * DpiScale;
        Ui.Draw(graphics, Title, Ui.Get(9.75f, Ui.Semibold), new RectangleF(x, 0, Width - x - 4 * DpiScale, Height / 2f + 1), Theme.Text, TextFormatFlags.Bottom | TextFormatFlags.EndEllipsis);
        var font = Ui.Get(8.6f, Ui.Regular);
        float top = Height / 2f + 1;
        float right = Width - 10 * DpiScale;
        if (Level != -1)
        {
            right -= Ui.BatteryWidth(DpiScale);
            Ui.Battery(graphics, right, top + (font.Height - Ui.BatteryHeight(DpiScale)) / 2f, Level, DpiScale, Theme.Dim);
            if (Value != "")
            {
                float valueWidth = TextRenderer.MeasureText(graphics, Value, font, Size.Empty, TextFormatFlags.NoPadding).Width;
                right -= 5 * DpiScale + valueWidth;
                Ui.Draw(graphics, Value, font, new RectangleF(right, top, valueWidth + 4 * DpiScale, Height / 2f), Theme.Dim, TextFormatFlags.Top);
            }
            right -= 6 * DpiScale;
        }
        Ui.Draw(graphics, Subtitle, font, new RectangleF(x, top, right - x, Height / 2f), Theme.Dim, TextFormatFlags.Top | TextFormatFlags.EndEllipsis);
        var list = Parent as DeviceList;
        if (list != null) list.Fade(graphics, this);
    }
}

class DeviceSubtitle : Control
{
    string before = "", value = "", after = "";
    int level = -1;

    public DeviceSubtitle()
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
        AccessibleName = before + (level == Ui.Asleep ? ", asleep" : level >= 0 ? ", " + value : "") + after;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var graphics = e.Graphics;
        var font = Ui.Get(9f, Ui.Regular);
        var flags = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix;
        float scale = Ui.DpiScale, x = 0;
        int y = (Height - font.Height) / 2;
        Action<string> text = s =>
        {
            TextRenderer.DrawText(graphics, s, font, new Point((int)x, y), Theme.Dim, flags);
            x += TextRenderer.MeasureText(graphics, s, font, Size.Empty, flags).Width;
        };
        text(before);
        if (level != -1)
        {
            x += 6 * scale;
            if (value != "")
            {
                text(value);
                x += 5 * scale;
            }
            Ui.Battery(graphics, (int)x, y + (font.Height - Ui.BatteryHeight(scale)) / 2, level, scale, Theme.Dim);
            x += Ui.BatteryWidth(scale);
        }
        text(after);
    }
}

class TitleLogo : DrawnControl
{
    public TitleLogo() { Cursor = Cursors.Default; TabStop = false; }

    protected override void OnPaint(PaintEventArgs e)
    {
        var graphics = Begin(e);
        float x = 8 * DpiScale;
        Ui.Draw(graphics, "G-Lite", Ui.Get(10.5f, Ui.Semibold), new RectangleF(x, 0, Width - x, Height), Theme.Text, TextFormatFlags.VerticalCenter);
    }
}

class MenuColorTable : ProfessionalColorTable
{
    public override Color MenuBorder { get { return Theme.Border; } }
    public override Color MenuItemBorder { get { return Theme.ButtonHover; } }
    public override Color MenuItemSelected { get { return Theme.ButtonHover; } }
    public override Color ToolStripDropDownBackground { get { return Theme.Card; } }
    public override Color ImageMarginGradientBegin { get { return Theme.Card; } }
    public override Color ImageMarginGradientMiddle { get { return Theme.Card; } }
    public override Color ImageMarginGradientEnd { get { return Theme.Card; } }
    public override Color CheckBackground { get { return Theme.Card; } }
    public override Color CheckSelectedBackground { get { return Theme.ButtonHover; } }
    public override Color CheckPressedBackground { get { return Theme.ButtonHover; } }
    public override Color SeparatorDark { get { return Theme.Border; } }
    public override Color SeparatorLight { get { return Theme.Border; } }
}

class MenuRenderer : ToolStripProfessionalRenderer
{
    public MenuRenderer() : base(new MenuColorTable()) { RoundedEdges = false; }

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
        if (Equals(e.ToolStrip.Tag, "round") && WindowHelpers.Rounded) return;
        base.OnRenderToolStripBorder(e);
    }

    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var box = e.ImageRectangle;
        float s = Math.Min(box.Width, box.Height);
        Icons.Draw(e.Graphics, "check", new RectangleF(box.X + (box.Width - s) / 2, box.Y + (box.Height - s) / 2, s, s), Theme.Accent);
    }
}

class TrayTip : Form
{
    public Func<List<string[]>> Rows;
    public NotifyIcon Tray;
    List<string[]> rows = new List<string[]>();
    Point anchor, shownAt;
    DateTime firstAt, movedAt, filledAt;
    bool hovering;
    float scale = 1;
    Font font;
    int nameWidth, rowHeight;
    Color back, edge, nameColor, valueColor;
    readonly Timer watch = new Timer { Interval = 100 };

    struct IconId { public int Size; public IntPtr Window; public int Id; public Guid Item; }
    struct IconRect { public int Left, Top, Right, Bottom; }
    [DllImport("shell32.dll")] static extern int Shell_NotifyIconGetRect(ref IconId id, out IconRect rect);

    public TrayTip()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        DoubleBuffered = true;
        WindowHelpers.Round(this, 3);
        watch.Tick += (s, e) => Tick();
    }

    // NotifyIcon keeps its window and id private; .NET Framework 4.8 doesn't change any more, so the field names hold.
    Rectangle IconBounds()
    {
        try
        {
            var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            var window = (NativeWindow)typeof(NotifyIcon).GetField("window", flags).GetValue(Tray);
            var id = new IconId { Window = window.Handle, Id = (int)typeof(NotifyIcon).GetField("id", flags).GetValue(Tray) };
            id.Size = Marshal.SizeOf(id);
            IconRect rect;
            if (Shell_NotifyIconGetRect(ref id, out rect) == 0) return Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
        }
        catch (Exception) { }
        return new Rectangle(shownAt, Size.Empty);
    }

    protected override bool ShowWithoutActivation { get { return true; } }

    protected override CreateParams CreateParams
    {
        get
        {
            var createParams = base.CreateParams;
            createParams.ExStyle |= 0x08000000 | 0x80 | 0x20 | 0x08;
            return createParams;
        }
    }

    public void PointerMoved()
    {
        var now = DateTime.UtcNow;
        if (!hovering) { hovering = true; firstAt = now; watch.Start(); }
        anchor = Cursor.Position;
        movedAt = now;
    }

    public void Dismiss()
    {
        hovering = false;
        watch.Stop();
        Hide();
    }

    void Tick()
    {
        var now = DateTime.UtcNow;
        if (Cursor.Position != anchor && (now - movedAt).TotalMilliseconds > 250) { Dismiss(); return; }
        if (!Visible && (now - firstAt).TotalMilliseconds < 400) return;
        if (Visible && (now - filledAt).TotalMilliseconds < 1000) return;
        filledAt = now;
        if (!Visible) shownAt = anchor;
        Fill();
    }

    void Fill()
    {
        rows = Rows();
        var icon = IconBounds();
        var center = new Point(icon.X + icon.Width / 2, icon.Y + icon.Height / 2);
        scale = WindowHelpers.At(center);
        if (font != null) font.Dispose();
        font = WindowHelpers.PixelFont(9, scale);
        int pad = S(10), gap = S(24), valueWidth = 0;
        nameWidth = 0;
        rowHeight = font.Height + S(4);
        var flags = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;
        using (var surface = new Bitmap(1, 1))
        using (var graphics = Graphics.FromImage(surface))
            foreach (var row in rows)
            {
                nameWidth = Math.Max(nameWidth, TextRenderer.MeasureText(graphics, row[0], font, Size.Empty, flags).Width);
                valueWidth = Math.Max(valueWidth, TextRenderer.MeasureText(graphics, row[1], font, Size.Empty, flags).Width + (HasIcon(row) ? S(18) + (row[1] == "" ? 0 : S(5)) : 0));
            }
        var size = new Size(pad * 2 + nameWidth + gap + valueWidth, S(8) * 2 + rowHeight * rows.Count);
        // Placed from the icon, not the cursor, so it stays put while the mouse moves over the icon, like the system's tray tooltips.
        var area = Screen.FromPoint(center).WorkingArea;
        int margin = S(8), x = center.X - size.Width / 2, y;
        if (center.Y >= area.Bottom) y = area.Bottom - size.Height - margin;
        else if (center.Y < area.Top) y = area.Top + margin;
        else if (center.X < area.Left) { x = area.Left + margin; y = center.Y - size.Height / 2; }
        else if (center.X >= area.Right) { x = area.Right - size.Width - margin; y = center.Y - size.Height / 2; }
        else y = icon.Top - size.Height - margin;
        x = Math.Max(area.Left, Math.Min(area.Right - size.Width, x));
        y = Math.Max(area.Top, Math.Min(area.Bottom - size.Height, y));
        Bounds = new Rectangle(new Point(x, y), size);
        // The system's tray tooltips follow the taskbar's theme; colours from the Tooltip and DarkMode_Explorer::Tooltip theme parts.
        bool light = Theme.TaskbarLight();
        back = Theme.FromHex(light ? 0xF9F9F9 : 0x2B2B2B);
        edge = Theme.FromHex(light ? 0xE5E5E5 : 0x767676);
        nameColor = Theme.FromHex(light ? 0x575757 : 0xABABAB);
        valueColor = Theme.FromHex(light ? 0x1A1A1A : 0xFFFFFF);
        BackColor = back;
        if (!Visible) Show();
        WindowHelpers.Border(this, edge);
        Invalidate();
    }

    int S(int value) { return (int)Math.Round(value * scale); }

    static bool HasIcon(string[] row) { return row.Length > 2 && row[2] != ""; }

    void DrawBattery(Graphics graphics, int x, int y, string level)
    {
        int percent;
        if (int.TryParse(level, out percent)) Ui.Battery(graphics, x, y, percent, scale, valueColor);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var graphics = e.Graphics;
        graphics.Clear(back);
        if (font == null) return;
        int pad = S(10), top = S(8) + S(2);
        var flags = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;
        for (int i = 0; i < rows.Count; i++)
        {
            int y = top + i * rowHeight;
            TextRenderer.DrawText(graphics, rows[i][0], font, new Point(pad, y), nameColor, flags);
            var width = TextRenderer.MeasureText(graphics, rows[i][1], font, Size.Empty, flags).Width;
            int right = Width - pad;
            if (HasIcon(rows[i]))
            {
                DrawBattery(graphics, right - S(18), y + (font.Height - S(9)) / 2, rows[i][2]);
                right -= S(18) + S(5);
            }
            TextRenderer.DrawText(graphics, rows[i][1], font, new Point(right - width, y), valueColor, flags);
        }
        if (!WindowHelpers.Rounded) using (var pen = new Pen(edge)) graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
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

    public void Draw(Graphics graphics, Rectangle area)
    {
        float scale = Owner != null ? Owner.DeviceDpi / 96f : 1;
        var flags = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix;
        var size = TextRenderer.MeasureText(graphics, value, Font, Size.Empty, flags);
        int right = (Owner != null ? Owner.Width - 2 * Bounds.X : Width) - TextLeft - (int)(2 * scale), y = area.Top + (area.Height - size.Height) / 2;
        var color = Enabled ? Theme.Text : Theme.Dim;
        if (Level != -1)
        {
            Ui.Battery(graphics, right - Ui.BatteryWidth(scale), y + (size.Height - Ui.BatteryHeight(scale)) / 2, Level, scale, color);
            right -= Ui.BatteryWidth(scale) + (int)(5 * scale);
        }
        TextRenderer.DrawText(graphics, value, Font, new Point(right - size.Width, y), color, flags);
    }
}
