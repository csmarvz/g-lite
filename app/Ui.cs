using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

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
        control.Font = Get(points, face);
        fonts.Add(Tuple.Create((Control)control, points, face));
        return control;
    }

    public static void Restyle() { styles.RemoveAll(s => s.Key.IsDisposed); foreach (var s in styles) s.Value(); }

    public static void Refont()
    {
        fonts.RemoveAll(entry => entry.Item1.IsDisposed);
        foreach (var entry in fonts) entry.Item1.Font = Get(entry.Item2, entry.Item3);
    }

    public static Font Get(float points, string face)
    {
        var key = points + "|" + face + "|" + DpiScale;
        Font font;
        if (!cache.TryGetValue(key, out font)) cache[key] = font = WindowHelpers.PixelFont(points, DpiScale, face);
        return font;
    }

    public static Label Text(Control parent, string text, int x, int y, Func<Color> color, float points = 9.75f, string face = Regular)
    {
        var label = new Label
        {
            Text = text, Location = new Point(x, y), AutoSize = true, BackColor = Color.Transparent, UseMnemonic = false
        };
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
        int line = Math.Max(1, (int)Math.Round(scale)), width = (int)Math.Round(16 * scale), height = BatteryHeight(scale),
            nub = (int)Math.Round(3 * scale);
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
                        graphics.FillRectangle(brush,
                            x + cellLeft, y + (int)Math.Round(row * scale), (int)Math.Round(to * scale) + cell - cellLeft, cell);
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
                    int from = x + (int)Math.Round((5 + Spark[row * 2]) * scale),
                        to = x + (int)Math.Round((6 + Spark[row * 2 + 1]) * scale);
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

    // Pages are laid out at this size before they're scaled and placed, so their anchors measure from it.
    public static readonly Size PageSize = new Size(656, 730);

    // A row of a Settings box: a title at 12 and a hint at 33, or a 32 high control at 16.
    public const int RowHeight = 64;

    // A page's footer stays at the bottom of the window, and a box that ends above it grows down to it.
    public const AnchorStyles Footer = AnchorStyles.Bottom | AnchorStyles.Left;
    public const AnchorStyles Tall = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left;

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
        return Color.FromArgb(
            (int)(from.R + (to.R - from.R) * amount),
            (int)(from.G + (to.G - from.G) * amount),
            (int)(from.B + (to.B - from.B) * amount));
    }

    // The text of something that can't be changed now.
    public static Color Off { get { return Mix(Theme.Card, Theme.Dim, 0.45); } }

    public static void Draw(Graphics graphics, string text, Font font, RectangleF bounds, Color color, TextFormatFlags flags)
    {
        TextRenderer.DrawText(graphics, text, font, Rectangle.Round(bounds), color,
            flags | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
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
                case "lines":
                    graphics.DrawLine(pen, 5, 7, 19, 7); graphics.DrawLine(pen, 5, 12, 19, 12); graphics.DrawLine(pen, 5, 17, 13, 17);
                    break;
                case "power": graphics.DrawArc(pen, 5, 5.5f, 14, 14, -60, 300); graphics.DrawLine(pen, 12, 3.5f, 12, 11); break;
                case "close": graphics.DrawLine(pen, 6.5f, 6.5f, 17.5f, 17.5f); graphics.DrawLine(pen, 17.5f, 6.5f, 6.5f, 17.5f); break;
                case "download":
                    graphics.DrawLine(pen, 12, 4, 12, 15);
                    graphics.DrawLines(pen, new[] { new PointF(7.5f, 10.5f), new PointF(12, 15), new PointF(16.5f, 10.5f) });
                    graphics.DrawLine(pen, 5, 19.5f, 19, 19.5f);
                    break;
                case "plus": graphics.DrawLine(pen, 12, 5, 12, 19); graphics.DrawLine(pen, 5, 12, 19, 12); break;
                case "chevron": graphics.DrawLines(pen, new[] { new PointF(7, 10), new PointF(12, 15), new PointF(17, 10) }); break;
                case "check": graphics.DrawLines(pen, new[] { new PointF(5, 12.5f), new PointF(10, 17.5f), new PointF(19, 7) }); break;
                case "shield":
                    using (var path = new GraphicsPath())
                    {
                        path.AddLine(12, 3.5f, 19, 6);
                        path.AddLine(19, 6, 19, 11.5f);
                        path.AddBezier(19, 11.5f, 19, 15.8f, 16, 19.2f, 12, 20.5f);
                        path.AddBezier(12, 20.5f, 8, 19.2f, 5, 15.8f, 5, 11.5f);
                        path.AddLine(5, 11.5f, 5, 6);
                        path.CloseFigure();
                        graphics.FillPath(brush, path);
                    }
                    break;
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
