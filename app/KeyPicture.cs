using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

class KeyPicture : DrawnControl
{
    readonly List<RectangleF> keys = new List<RectangleF>();
    readonly List<Tuple<PointF, double>> ripples = new List<Tuple<PointF, double>>();
    readonly Stopwatch clock = Stopwatch.StartNew();
    readonly Timer timer = new Timer { Interval = 33 };
    readonly Random random = new Random();
    List<int> ids = new List<int>();
    List<string> names = new List<string>();
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

    // Picking keys by lighting id (the Game mode keys): no animation, each key with its name, picked keys filled with the accent and
    // fixed ones hatched. Label gives a key's name from its id and its name in the layout. GameModeKey is the layout's name of the Game
    // mode key, drawn with its joystick icon and outlined, so the note's "Game mode" can be found on the picture.
    public bool Picking;
    public HashSet<int> PickedIds = new HashSet<int>(), FixedIds = new HashSet<int>();
    public Func<int, bool> CanPick;
    public Func<int, string, string> Label;
    public string GameModeKey;
    public event Action<int> IdPicked;
    int hovered = -1;

    public KeyPicture(KeyLayout layout)
    {
        Cursor = Cursors.Default;
        TabStop = false;
        AccessibleRole = AccessibleRole.Graphic;
        SetModel(layout);
        timer.Tick += (s, e) => { if (Visible && !Picking) Invalidate(); };
        VisibleChanged += (s, e) => timer.Enabled = Visible;
    }

    public void SetModel(KeyLayout layout)
    {
        keys.Clear();
        lips.Clear();
        names.Clear();
        hovered = -1;
        widthInKeys = heightInKeys = 0;
        if (!Picking) AccessibleName = layout.Name + " lighting";
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
                names.Add(parts[0]);
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

    bool Pickable(int key) { return ids[key] >= 0 && CanPick != null && CanPick(ids[key]); }

    // The layout's names of the picked keys, in the picture's order (for the traces).
    public string PickedText
    {
        get
        {
            return string.Join(" ", Enumerable.Range(0, keys.Count)
                .Where(key => ids[key] >= 0 && PickedIds.Contains(ids[key])).Select(key => names[key]));
        }
    }

    // A click on the key with this name in the layout (for the traces).
    public void PressKey(string name)
    {
        int key = names.IndexOf(name);
        if (key < 0 || !Pickable(key)) throw new InvalidOperationException(name + " can't be picked");
        if (IdPicked != null) IdPicked(ids[key]);
    }

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
        if (Picking)
        {
            if (e.Button != MouseButtons.Left) return;
            for (int key = 0; key < keys.Count; key++)
                if (Hit(key, cap => cap.Contains(e.Location)))
                {
                    if (Pickable(key) && IdPicked != null) IdPicked(ids[key]);
                    return;
                }
            return;
        }
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
        if (Picking)
        {
            int over = -1;
            for (int key = 0; key < keys.Count; key++)
                if (Pickable(key) && Hit(key, cap => cap.Contains(e.Location))) { over = key; break; }
            Cursor = over >= 0 ? Cursors.Hand : Cursors.Default;
            if (over != hovered) { hovered = over; Invalidate(); }
            return;
        }
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

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (hovered >= 0) { hovered = -1; Invalidate(); }
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
        if (Picking) { PaintPicking(graphics); return; }
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
            if (ids[key] < 0) fill = Theme.KeyOff;
            FillKey(graphics, key, fill);
        }
    }

    void PaintPicking(Graphics graphics)
    {
        using (var format = new StringFormat(StringFormatFlags.NoWrap) { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            for (int key = 0; key < keys.Count; key++)
            {
                int keyId = ids[key];
                bool fixedOff = keyId >= 0 && FixedIds.Contains(keyId), picked = keyId >= 0 && PickedIds.Contains(keyId),
                    pickable = Pickable(key), gameModeKey = names[key] == GameModeKey;
                var fill = picked ? Theme.Accent : pickable || fixedOff || gameModeKey ? Theme.KeyOff
                    : Ui.Mix(Theme.PictureBackground, Theme.KeyOff, 0.4);
                if (key == hovered) fill = Ui.Mix(fill, Theme.Text, 0.2);
                FillKey(graphics, key, fill);
                var cap = Cap(keys[key]);
                if (gameModeKey)
                {
                    using (var pen = new Pen(Theme.Accent, 2 * DpiScale)) Outline(graphics, key, pen);
                    DrawJoystick(graphics, cap);
                    continue;
                }
                if (fixedOff)
                    using (var hatch = new HatchBrush(HatchStyle.WideUpwardDiagonal, Color.FromArgb(120, Theme.Dim), Color.Transparent))
                    using (var path = Ui.Round(cap, 3 * DpiScale))
                        graphics.FillPath(hatch, path);
                var label = Label != null ? Label(keyId, names[key]) : names[key];
                if (string.IsNullOrEmpty(label)) continue;
                var color = picked ? Theme.OnAccent : pickable ? Theme.Text : Theme.Dim;
                var font = Ui.Get(6.5f, Ui.Regular);
                foreach (var points in new[] { 5.5f, 4.5f })
                    if (graphics.MeasureString(label, font, PointF.Empty, format).Width > cap.Width) font = Ui.Get(points, Ui.Regular);
                using (var brush = new SolidBrush(color)) graphics.DrawString(label, font, brush, cap, format);
            }
    }

    // The icon printed on the G815's Game mode key: a ball on a stick, standing on a base.
    void DrawJoystick(Graphics graphics, RectangleF cap)
    {
        float size = Math.Min(cap.Width, cap.Height) * 0.62f, left = cap.X + (cap.Width - size) / 2, top = cap.Y + (cap.Height - size) / 2;
        float middle = left + size / 2, ball = size * 0.3f;
        using (var pen = new Pen(Theme.Text, 1.2f * DpiScale))
        {
            graphics.DrawEllipse(pen, middle - ball / 2, top, ball, ball);
            graphics.DrawLine(pen, middle, top + ball, middle, top + size * 0.62f);
            using (var path = Ui.Round(new RectangleF(left + size * 0.1f, top + size * 0.62f, size * 0.8f, size * 0.32f), size * 0.08f))
                graphics.DrawPath(pen, path);
        }
    }

    void PaintLive(Graphics graphics)
    {
        var touched = dragging ? new HashSet<int>(Touched()) : new HashSet<int>();
        for (int key = 0; key < keys.Count; key++)
        {
            var fill = liveColors != null && key < liveColors.Length && ids[key] >= 0
                ? Ui.Mix(Theme.KeyOff, liveColors[key], bright / 100.0) : Theme.KeyOff;
            FillKey(graphics, key, fill);
            if (touched.Contains(key) || (marked != null && key < marked.Length && marked[key]))
                using (var pen = new Pen(Theme.Accent, 2 * DpiScale)) Outline(graphics, key, pen);
        }
        if (!dragging || !Dragged) return;
        using (var brush = new SolidBrush(Color.FromArgb(46, Theme.Accent))) graphics.FillRectangle(brush, Marquee);
        using (var pen = new Pen(Theme.Accent, DpiScale)) graphics.DrawRectangle(pen, Marquee);
    }
}
