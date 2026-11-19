using System;
using System.Collections.Generic;
using System.Linq;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using System.Runtime.InteropServices;

class KeyboardView : Form
{
    const float KeyUnit = 40, Pad = 16, HeaderHeight = 40;
    [DllImport("user32.dll")] static extern bool ReleaseCapture();
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
    readonly FakeHidpp keyboard;
    readonly List<Tuple<RectangleF, string>> keys = new List<Tuple<RectangleF, string>>();
    readonly List<Tuple<PointF, double>> ripples = new List<Tuple<PointF, double>>();
    readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
    float width, height, boardWidth, boardHeight, scale = 1;
    List<Point>[] cells;
    Game game;
    Func<Game> make;
    double nextStep;

    protected override bool ShowWithoutActivation { get { return true; } }

    public KeyboardView(FakeHidpp keyboard, KeyLayout model)
    {
        this.keyboard = keyboard;
        WindowHelpers.Round(this);
        foreach (var row in model.Rows)
        {
            float x = 0, y = height, tall = 0;
            foreach (var token in row.Split('|'))
            {
                if (token.StartsWith("_"))
                {
                    x += float.Parse(token.Substring(1), System.Globalization.CultureInfo.InvariantCulture) * KeyUnit; continue;
                }
                var parts = token.Split(':');
                float keyWidth = parts.Length > 1
                    ? float.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture) * KeyUnit : KeyUnit;
                float keyHeight = parts.Length > 2
                    ? float.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture) * KeyUnit : KeyUnit;
                keys.Add(Tuple.Create(new RectangleF(Pad + x, HeaderHeight + Pad + y, keyWidth - 4, keyHeight - 4), parts[0]));
                x += keyWidth;
                tall = Math.Max(tall, Math.Min(keyHeight, KeyUnit));
                width = Math.Max(width, x);
            }
            height += tall > 0 ? tall : x;
        }
        var rows = keys.Select(key => key.Item1.Y).Distinct().OrderBy(top => top).ToList();
        cells = keys.Select(key =>
        {
            int row = rows.IndexOf(key.Item1.Y) - (rows.Count - Game.Rows);
            if (row < 0) return null;
            var columns = Enumerable.Range(0, Game.Columns)
                .Where(column => Pad + (column + 0.5f) * KeyUnit >= key.Item1.X && Pad + (column + 0.5f) * KeyUnit < key.Item1.Right + 4)
                .Select(column => new Point(column, row))
                .ToList();
            return columns.Count > 0 ? columns : null;
        }).ToArray();
        Text = model.Name;
        mines = Button("Mines", () => Play(() => new Mines(keys.Select(key => key.Item1).ToArray(), cells)));
        blocks = Button("Blocks", () => Play(() => new Blocks()));
        snake = Button("Snake", () => Play(() => new Snake(cells)));
        stop = Button("Stop", () => { game = null; RefreshControls(); });
        close = Button("", Hide);
        close.Icon = "close";
        close.AccessibleName = "Close";
        BackColor = Color.FromArgb(28, 28, 28);
        DoubleBuffered = true;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        boardWidth = width + 2 * Pad;
        boardHeight = HeaderHeight + height + 2 * Pad + 20;
        Rescale(WindowHelpers.At(Point.Empty));
        var timer = new System.Windows.Forms.Timer { Interval = 33 };
        timer.Tick += (s, e) => { Tick(); Invalidate(); };
        timer.Start();
        FormClosing += (s, e) => { if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); } };
        MouseDown += (s, e) =>
        {
            if (e.Button != MouseButtons.Left) return;
            float clickX = e.X / scale, clickY = e.Y / scale;
            if (clickY >= HeaderHeight + Pad / 2)
            {
                ripples.Add(Tuple.Create(new PointF(clickX, clickY), clock.Elapsed.TotalSeconds)); return;
            }
            ReleaseCapture();
            SendMessage(Handle, 0xA1, (IntPtr)2, IntPtr.Zero);
        };
    }

    public void Rescale(float newScale)
    {
        scale = newScale;
        ClientSize = new Size((int)Math.Ceiling(boardWidth * scale), (int)Math.Ceiling(boardHeight * scale));
        float right = boardWidth - 12;
        Place(close, ref right, 36);
        right -= 4;
        float playing = right;
        Place(snake, ref right, 72);
        Place(blocks, ref right, 72);
        Place(mines, ref right, 72);
        Place(stop, ref playing, 72);
        RefreshControls();
        Invalidate();
    }

    readonly FlatButton mines, blocks, snake, stop, close;

    FlatButton Button(string text, Action click)
    {
        var button = new FlatButton { Style = ButtonStyle.Ghost, Text = text, AccessibleName = text };
        button.Click += (s, e) => click();
        Controls.Add(button);
        return button;
    }

    void Place(FlatButton button, ref float right, float width)
    {
        right -= width;
        button.Bounds = new Rectangle((int)Math.Round(right * scale), (int)Math.Round(10 * scale),
            (int)Math.Round(width * scale), (int)Math.Round(28 * scale));
    }

    void RefreshControls()
    {
        if (mines == null) return;
        bool on = game != null;
        foreach (var button in new[] { mines, blocks, snake }) if (button.Visible == on) button.Visible = !on;
        if (stop.Visible != on) stop.Visible = on;
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == WindowHelpers.DpiChanged) Rescale(WindowHelpers.FromMessage(message));
        base.WndProc(ref message);
    }

    void Play(Func<Game> newGame)
    {
        make = newGame;
        game = newGame();
        RefreshControls();
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
        if (key == Keys.Escape) { game = null; RefreshControls(); } else if (game.Over) Play(make); else game.Key(key);
        Invalidate();
        return true;
    }

    public static double PeriodOf(byte id, byte[] parameters)
    {
        double period = (id == 0x0A ? parameters[3] << 8 | parameters[4] : parameters[6] << 8 | parameters[7]) / 1000.0;
        if (id == 0x17) period = (parameters[6] << 8 | parameters[7]) / 20.0;
        return period <= 0 ? 5 : period;
    }

    public static Color EffectColorAt(byte id, byte[] parameters, double x, double y, double seconds, double ripple)
    {
        if (G515Profile.EffectById(id) == null) return Hsv(seconds / 8 - x, 1, 1);
        var color = Color.FromArgb(parameters[0], parameters[1], parameters[2]);
        double period = PeriodOf(id, parameters);
        int direction = parameters[9];
        switch (id)
        {
            case 0x01: return color;
            case 0x0A: return Scale(color, 0.5 - 0.5 * Math.Cos(2 * Math.PI * seconds / period));
            case 0x15: return Hsv(seconds / period, parameters[1] / 255.0, 1);
            case 0x16:
                double offset = direction == 6 ? x
                    : direction == 3 ? -Math.Abs(x - 0.5) * 2
                    : direction == 8 ? Math.Abs(x - 0.5) * 2
                    : direction == 2 ? -y
                    : direction == 7 ? y
                    : -x;
                return Hsv(seconds / period + offset, parameters[1] / 255.0, 1);
            case 0x17: return Scale(color, ripple);
            default: return Color.Black;
        }
    }

    public static Color Hsv(double hue, double saturation, double value)
    {
        hue = (hue % 1 + 1) % 1 * 6;
        int i = (int)hue;
        double fraction = hue - i, low = value * (1 - saturation), falling = value * (1 - saturation * fraction),
            rising = value * (1 - saturation * (1 - fraction));
        double[] rgb = i == 0 ? new[] { value, rising, low }
            : i == 1 ? new[] { falling, value, low }
            : i == 2 ? new[] { low, value, rising }
            : i == 3 ? new[] { low, falling, value }
            : i == 4 ? new[] { rising, low, value }
            : new[] { value, low, falling };
        return Color.FromArgb((int)(rgb[0] * 255), (int)(rgb[1] * 255), (int)(rgb[2] * 255));
    }

    public static Color Scale(Color color, double factor)
    {
        factor = Math.Max(0, Math.Min(1, factor));
        return Color.FromArgb((int)(color.R * factor), (int)(color.G * factor), (int)(color.B * factor));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        byte id; byte[] parameters; int bright;
        keyboard.State(out id, out parameters, out bright);
        var effect = G515Profile.EffectById(id);
        double seconds = clock.Elapsed.TotalSeconds;
        double period = PeriodOf(id, parameters);
        ripples.RemoveAll(ripple => seconds - ripple.Item2 > 3);
        var graphics = e.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.ScaleTransform(scale, scale);
        using (var font = WindowHelpers.PixelFont(7.5f, 1))
        for (int keyIndex = 0; keyIndex < keys.Count; keyIndex++)
        {
            var key = keys[keyIndex];
            var box = key.Item1;
            Color fill = Color.FromArgb(30, 30, 32);
            double x = (box.X + box.Width / 2 - Pad) / width, y = (box.Y - HeaderHeight - Pad) / height;
            double glow = 0;
            if (id == 0x17)
                foreach (var ripple in ripples)
                {
                    double distance = Math.Sqrt(Math.Pow(box.X + box.Width / 2 - ripple.Item1.X, 2)
                        + Math.Pow(box.Y + box.Height / 2 - ripple.Item1.Y, 2)) / KeyUnit;
                    double front = (seconds - ripple.Item2) * 8 / period;
                    glow = Math.Max(glow, Math.Max(0, 1 - Math.Abs(distance - front)) * Math.Max(0, 1 - (seconds - ripple.Item2) / 3));
                }
            Color color = EffectColorAt(id, parameters, x, y, seconds, glow);
            if (game != null)
            {
                int hit = 0;
                if (cells[keyIndex] != null) foreach (var cell in cells[keyIndex]) if (hit == 0) hit = game.At(cell.Y, cell.X);
                bool arrow = "↑↓←→".Contains(key.Item2) && key.Item2.Length == 1;
                color = hit != 0
                    ? (game.Over && !(game is Mines) ? Color.FromArgb(255, 40, 40) : Color.FromArgb(hit | unchecked((int)0xFF000000)))
                    : arrow ? Color.FromArgb(120, 120, 120)
                    : Color.FromArgb(45, 45, 45);
                if (hit != 0) fill = Scale(color, 0.45);
            }
            color = Scale(color, bright / 100.0);
            fill = Color.FromArgb(Math.Max(30, (int)(fill.R * bright / 100.0)), Math.Max(30, (int)(fill.G * bright / 100.0)),
                Math.Max(32, (int)(fill.B * bright / 100.0)));
            using (var path = new GraphicsPath())
            {
                float radius = 5;
                path.AddArc(box.X, box.Y, radius * 2, radius * 2, 180, 90);
                path.AddArc(box.Right - radius * 2, box.Y, radius * 2, radius * 2, 270, 90);
                path.AddArc(box.Right - radius * 2, box.Bottom - radius * 2, radius * 2, radius * 2, 0, 90);
                path.AddArc(box.X, box.Bottom - radius * 2, radius * 2, radius * 2, 90, 90);
                path.CloseFigure();
                using (var keyBrush = new SolidBrush(fill)) graphics.FillPath(keyBrush, path);
                using (var pen = new Pen(Color.FromArgb(55, 55, 58))) graphics.DrawPath(pen, path);
                using (var lit = new SolidBrush(color))
                    graphics.DrawString(key.Item2, font, lit, box,
                        new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center });
                using (var under = new SolidBrush(Color.FromArgb(90, color)))
                    graphics.FillRectangle(under, box.X + 6, box.Bottom - 4, box.Width - 12, 2);
            }
        }
        if (!WindowHelpers.Rounded)
            using (var pen = new Pen(Color.FromArgb(58, 58, 58))) graphics.DrawRectangle(pen, 0, 0, boardWidth - 1, boardHeight - 1);
        using (var font = WindowHelpers.PixelFont(9, 1))
        using (var brush = new SolidBrush(Color.FromArgb(150, 150, 150)))
        {
            graphics.DrawString(Text, font, brush, Pad, 17);
        }
        string what = effect == null ? "factory animation (approximation)" : effect.Name;
        if (id == 0x17) what += " — click the keys";
        if (game != null) what = game.Over ? "game over      score " + game.Score + "      any key to play again" : game.Help;
        using (var font = WindowHelpers.PixelFont(9, 1))
        using (var brush = new SolidBrush(Color.FromArgb(150, 150, 150)))
        {
            graphics.DrawString(what + "      brightness " + bright + "%", font, brush, Pad, boardHeight - 26);
            if (game is Mines && !game.Over)
            {
                float legendX = boardWidth - Pad;
                for (int i = Mines.LegendNames.Length - 1; i >= 0; i--)
                {
                    legendX -= graphics.MeasureString(Mines.LegendNames[i], font).Width;
                    graphics.DrawString(Mines.LegendNames[i], font, brush, legendX, boardHeight - 26);
                    legendX -= 12;
                    using (var swatch = new SolidBrush(Color.FromArgb(Mines.LegendColors[i] | unchecked((int)0xFF000000))))
                        graphics.FillRectangle(swatch, legendX, boardHeight - 23, 10, 10);
                    legendX -= 12;
                }
            }
        }
    }
}
