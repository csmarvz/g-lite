using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

class KeyPicture : DrawnControl
{
    readonly List<RectangleF> keys = new List<RectangleF>();
    readonly List<Tuple<PointF, double>> ripples = new List<Tuple<PointF, double>>();
    readonly Stopwatch clock = Stopwatch.StartNew();
    readonly Timer timer = new Timer { Interval = 33 };
    readonly Random random = new Random();
    List<int> ids = new List<int>();
    float widthInKeys, heightInKeys, keySize, offsetX, offsetY;
    byte id = 0x01, barId = 0x01;
    byte[] parameters = new byte[10], barParameters = new byte[10];
    public HashSet<int> Bar = new HashSet<int>();
    public int Part;
    public event Action<bool> PartPicked;
    int bright = 100;
    double lastRipple;
    bool live, dragging;
    Color[] liveColors;
    bool[] marked;
    Point dragFrom, dragTo;
    public event Action<List<int>> KeysPicked;

    public KeyPicture(KeyLayout layout)
    {
        Cursor = Cursors.Default;
        TabStop = false;
        AccessibleRole = AccessibleRole.Graphic;
        SetModel(layout);
        timer.Tick += (s, e) => { if (Visible) Invalidate(); };
        VisibleChanged += (s, e) => timer.Enabled = Visible;
    }

    public void SetModel(KeyLayout layout)
    {
        keys.Clear();
        lips.Clear();
        widthInKeys = heightInKeys = 0;
        AccessibleName = layout.Name + " lighting";
        foreach (var row in layout.Rows)
        {
            float x = 0, tall = 0;
            foreach (var token in row.Split('|'))
            {
                if (token.StartsWith("_")) { x += float.Parse(token.Substring(1), CultureInfo.InvariantCulture); continue; }
                var parts = token.Split(':');
                float keyWidth = parts.Length > 1 ? float.Parse(parts[1], CultureInfo.InvariantCulture) : 1;
                float keyHeight = parts.Length > 2 ? float.Parse(parts[2], CultureInfo.InvariantCulture) : 1;
                if (parts.Length > 3) lips[keys.Count] = float.Parse(parts[3], CultureInfo.InvariantCulture);
                keys.Add(new RectangleF(x, heightInKeys, keyWidth, keyHeight));
                x += keyWidth;
                // A key taller than a row (the numpad's + and Enter) hangs into the next one, which leaves a gap for it.
                tall = Math.Max(tall, Math.Min(keyHeight, 1));
                widthInKeys = Math.Max(widthInKeys, x);
            }
            heightInKeys += tall > 0 ? tall : x;
        }
        ids = layout.KeyIds();
        liveColors = null;
        Invalidate();
    }

    public int Count { get { return keys.Count; } }
    public int IdOf(int key) { return ids[key]; }
    public float XOf(int key) { return (keys[key].X + keys[key].Width / 2) / widthInKeys; }

    public bool Live
    {
        get { return live; }
        set { live = value; Cursor = value ? Cursors.Cross : Cursors.Default; dragging = false; Invalidate(); }
    }

    public void ShowLive(Color[] colors, bool[] outlined)
    {
        liveColors = colors;
        marked = outlined;
        Invalidate();
    }

    public void Display(Lighting lighting, int level) { Display(lighting, lighting, level); }

    public void Display(Lighting lighting, Lighting bar, int level)
    {
        id = lighting == null ? (byte)0xFF : lighting.Effect;
        parameters = lighting == null ? new byte[10] : G515Profile.Encode(lighting, 100);
        var other = bar ?? lighting;
        barId = other == null ? (byte)0xFF : other.Effect;
        barParameters = other == null ? new byte[10] : G515Profile.Encode(other, 100);
        bright = level;
        Invalidate();
    }

    bool OnBar(int key) { return Bar.Contains(ids[key]); }

    RectangleF Cap(RectangleF box)
    {
        return new RectangleF(offsetX + box.X * keySize + 1.5f * DpiScale, offsetY + box.Y * keySize + 1.5f * DpiScale,
            box.Width * keySize - 3 * DpiScale, box.Height * keySize - 3 * DpiScale);
    }

    // A key with a lip ("Enter:1.25:2:.25", the ISO Enter) is an L: its own rectangle plus its first row widened to the left by the lip.
    readonly Dictionary<int, float> lips = new Dictionary<int, float>();

    RectangleF? TopRow(int key)
    {
        float lip;
        if (!lips.TryGetValue(key, out lip)) return null;
        var box = keys[key];
        return Cap(new RectangleF(box.X - lip, box.Y, box.Width + lip, 1));
    }

    bool Hit(int key, Func<RectangleF, bool> test)
    {
        var top = TopRow(key);
        return test(Cap(keys[key])) || (top != null && test(top.Value));
    }

    void FillKey(Graphics graphics, int key, Color fill)
    {
        Ui.Fill(graphics, Cap(keys[key]), 3 * DpiScale, fill, fill);
        var top = TopRow(key);
        if (top != null) Ui.Fill(graphics, top.Value, 3 * DpiScale, fill, fill);
    }

    void Outline(Graphics graphics, int key, Pen pen)
    {
        var box = Cap(keys[key]);
        var top = TopRow(key);
        if (top == null)
        {
            using (var path = Ui.Round(
                new RectangleF(box.X + DpiScale, box.Y + DpiScale, box.Width - 2 * DpiScale, box.Height - 2 * DpiScale), 3 * DpiScale))
                graphics.DrawPath(pen, path);
            return;
        }
        var topBox = top.Value;
        pen.LineJoin = System.Drawing.Drawing2D.LineJoin.Round;
        graphics.DrawPolygon(pen, new[]
        {
            new PointF(topBox.Left + DpiScale, topBox.Top + DpiScale), new PointF(box.Right - DpiScale, topBox.Top + DpiScale),
            new PointF(box.Right - DpiScale, box.Bottom - DpiScale), new PointF(box.Left + DpiScale, box.Bottom - DpiScale),
            new PointF(box.Left + DpiScale, topBox.Bottom - DpiScale), new PointF(topBox.Left + DpiScale, topBox.Bottom - DpiScale)
        });
    }

    Rectangle Marquee
    {
        get
        {
            return Rectangle.FromLTRB(Math.Min(dragFrom.X, dragTo.X), Math.Min(dragFrom.Y, dragTo.Y),
                Math.Max(dragFrom.X, dragTo.X), Math.Max(dragFrom.Y, dragTo.Y));
        }
    }

    bool Dragged { get { return Math.Abs(dragTo.X - dragFrom.X) > 4 * DpiScale || Math.Abs(dragTo.Y - dragFrom.Y) > 4 * DpiScale; } }

    List<int> Touched()
    {
        var hits = new List<int>();
        for (int key = 0; key < keys.Count; key++)
            if (Hit(key, cap => Dragged ? cap.IntersectsWith(Marquee) : cap.Contains(dragTo))) hits.Add(key);
        return hits;
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (live)
        {
            if (e.Button != MouseButtons.Left) return;
            dragging = true;
            dragFrom = dragTo = e.Location;
            Invalidate();
            return;
        }
        if (keySize > 0)
            ripples.Add(Tuple.Create(new PointF((e.X - offsetX) / keySize, (e.Y - offsetY) / keySize), clock.Elapsed.TotalSeconds));
        if (Bar.Count == 0 || PartPicked == null || e.Button != MouseButtons.Left) return;
        for (int key = 0; key < keys.Count; key++)
            if (Hit(key, cap => { cap.Inflate(0, 2 * DpiScale); return cap.Contains(e.Location); })) { PartPicked(OnBar(key)); return; }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!dragging) return;
        dragTo = new Point(Math.Max(0, Math.Min(Width, e.X)), Math.Max(0, Math.Min(Height, e.Y)));
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (!dragging) return;
        dragTo = new Point(Math.Max(0, Math.Min(Width, e.X)), Math.Max(0, Math.Min(Height, e.Y)));
        var hits = Touched();
        dragging = false;
        Invalidate();
        if (hits.Count > 0 && KeysPicked != null) KeysPicked(hits);
    }

    protected override void OnMouseCaptureChanged(EventArgs e)
    {
        base.OnMouseCaptureChanged(e);
        if (dragging && !Capture) { dragging = false; Invalidate(); }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var graphics = Begin(e);
        float pad = 8 * DpiScale;
        keySize = Math.Min((Width - 2 * pad) / widthInKeys, (Height - 2 * pad) / heightInKeys);
        offsetX = (Width - widthInKeys * keySize) / 2;
        offsetY = (Height - heightInKeys * keySize) / 2;
        Ui.Fill(graphics, new RectangleF(offsetX - pad, offsetY - pad, widthInKeys * keySize + 2 * pad, heightInKeys * keySize + 2 * pad),
            8 * DpiScale, Theme.PictureBackground, Theme.PictureBackground);
        if (live) { PaintLive(graphics); return; }
        double seconds = clock.Elapsed.TotalSeconds, period = KeyboardView.PeriodOf(id, parameters);
        if (id == 0x17 && seconds - lastRipple > 1.8)
        {
            var rippleKey = keys[random.Next(keys.Count)];
            ripples.Add(Tuple.Create(new PointF(rippleKey.X + rippleKey.Width / 2, rippleKey.Y + 0.5f), seconds));
            lastRipple = seconds;
        }
        ripples.RemoveAll(ripple => seconds - ripple.Item2 > 3);
        for (int key = 0; key < keys.Count; key++)
        {
            var box = keys[key];
            bool onBar = OnBar(key);
            byte keyEffect = onBar ? barId : id;
            var keyParameters = onBar ? barParameters : parameters;
            double glow = 0;
            if (keyEffect == 0x17)
                foreach (var ripple in ripples)
                {
                    double distance = Math.Sqrt(Math.Pow(box.X + box.Width / 2 - ripple.Item1.X, 2)
                        + Math.Pow(box.Y + 0.5 - ripple.Item1.Y, 2));
                    double front = (seconds - ripple.Item2) * 8 / period;
                    glow = Math.Max(glow, Math.Max(0, 1 - Math.Abs(distance - front)) * Math.Max(0, 1 - (seconds - ripple.Item2) / 3));
                }
            var color = KeyboardView.EffectColorAt(keyEffect, keyParameters,
                (box.X + box.Width / 2) / widthInKeys, box.Y / heightInKeys, seconds, glow);
            int max = Math.Max(color.R, Math.Max(color.G, color.B));
            var fill = Theme.KeyOff;
            if (max > 0)
            {
                var full = Color.FromArgb(color.R * 255 / max, color.G * 255 / max, color.B * 255 / max);
                fill = Ui.Mix(Theme.KeyOff, full, max / 255.0 * bright / 100.0);
            }
            if (Part != 0 && Part != (onBar ? 2 : 1)) fill = Ui.Mix(Theme.KeyOff, fill, 0.3);
            FillKey(graphics, key, fill);
        }
    }

    void PaintLive(Graphics graphics)
    {
        var touched = dragging ? new HashSet<int>(Touched()) : new HashSet<int>();
        for (int key = 0; key < keys.Count; key++)
        {
            var fill = liveColors != null && key < liveColors.Length ? Ui.Mix(Theme.KeyOff, liveColors[key], bright / 100.0) : Theme.KeyOff;
            FillKey(graphics, key, fill);
            if (touched.Contains(key) || (marked != null && key < marked.Length && marked[key]))
                using (var pen = new Pen(Theme.Accent, 2 * DpiScale)) Outline(graphics, key, pen);
        }
        if (!dragging || !Dragged) return;
        using (var brush = new SolidBrush(Color.FromArgb(46, Theme.Accent))) graphics.FillRectangle(brush, Marquee);
        using (var pen = new Pen(Theme.Accent, DpiScale)) graphics.DrawRectangle(pen, Marquee);
    }
}
