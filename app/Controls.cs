using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

class DrawnControl : Control
{
    protected bool hover, down, key;
    Timer anim;
    float from, to, now;
    DateTime start;
    int length;

    public DrawnControl()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.ResizeRedraw, true);
        Cursor = Cursors.Hand;
    }

    protected bool Pressed { get { return ((down && hover) || key) && Enabled; } }

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

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left) { down = true; Invalidate(); }
        base.OnMouseDown(e);
    }
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
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.ResizeRedraw, true);
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
        if (Style == ButtonStyle.Primary || Style == ButtonStyle.Solid)
        {
            fill = edge = Pressed ? Ui.Mix(Theme.Accent, Color.Black, 0.18) : lit ? Ui.Mix(Theme.Accent, Theme.Text, 0.12) : Theme.Accent;
            fore = Theme.OnAccent;
        }
        else if (Style == ButtonStyle.Ghost)
        {
            fill = edge = Pressed ? Theme.ButtonPressed : On ? Theme.AccentBackground : lit ? Theme.Button : Under;
            fore = On || lit ? Theme.Text : Theme.Dim;
        }
        else { fill = Pressed ? Theme.ButtonPressed : lit ? Theme.ButtonHover : Theme.Button; edge = Theme.Border; fore = Theme.Text; }
        if (!Enabled) { fill = Ui.Mix(Under, fill, 0.5); edge = Ui.Mix(Under, edge, 0.5); fore = Ui.Mix(Under, fore, 0.45); }
        Ui.Fill(graphics, new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), 6 * DpiScale, fill, edge);
        if (Style == ButtonStyle.Solid && On)
            using (var pen = new Pen(Theme.OnAccent, 1.5f * DpiScale))
            using (var path = Ui.Round(
                new RectangleF(2.5f * DpiScale, 2.5f * DpiScale, Width - 5 * DpiScale - 1, Height - 5 * DpiScale - 1), 4.5f * DpiScale))
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
        float x = (Style == ButtonStyle.Ghost || Style == ButtonStyle.Solid) && Icon != null
            ? 10 * DpiScale
            : (Width - textWidth - (Icon != null ? icon + 8 * DpiScale : 0)) / 2;
        if (Icon != null)
        {
            Icons.Draw(graphics, Icon, new RectangleF(x, (Height - icon) / 2, icon, icon), fore);
            x += icon + 8 * DpiScale;
        }
        Ui.Draw(graphics, Text, font, new RectangleF(x, 0, Width - x - 4 * DpiScale, Height), fore,
            TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
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

    protected override void OnMouseMove(MouseEventArgs e)
    {
        int hit = Hit(e.Location);
        if (hit != hot) { hot = hit; Invalidate(); }
        base.OnMouseMove(e);
    }
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
        if (On)
            using (var path = Ui.Round(new RectangleF(DpiScale, DpiScale, Width - 2 * DpiScale, Height - 2 * DpiScale), 7 * DpiScale))
            using (var pen = new Pen(Ui.Mix(Under, Theme.Text, fade), 2 * DpiScale))
                graphics.DrawPath(pen, path);
        float inset = Pressed ? 5 * DpiScale : 4 * DpiScale;
        var box = new RectangleF(inset, inset, Width - 2 * inset - 1, Height - 2 * inset - 1);
        var fill = Fill ?? (Pressed ? Theme.ButtonPressed : hover && Enabled ? Theme.ButtonHover : Theme.Button);
        bool ring = Focused && ShowFocusCues && Enabled;
        Ui.Fill(graphics, box, 4 * DpiScale, Ui.Mix(Under, fill, fade), ring ? Theme.Text : Ui.Mix(Under, Theme.Border, fade));
        if (Fill == null)
            Icons.Draw(graphics, "plus",
                new RectangleF(Width / 2f - 7 * DpiScale, Height / 2f - 7 * DpiScale, 14 * DpiScale, 14 * DpiScale),
                Ui.Mix(Under, Theme.Dim, fade));
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
        Ui.Fill(graphics, box, box.Height / 2,
            Ui.Mix(Ui.Mix(Theme.Button, Theme.Accent, position), Theme.Card, faded),
            Ui.Mix(Ui.Mix(Theme.Border, Theme.Accent, position), Theme.Card, faded));
        DrawFocusRing(graphics, box.Height / 2);
        float knob = Height - 8 * DpiScale;
        float stretch = Pressed ? 4 * DpiScale : 0;
        using (var brush = new SolidBrush(Ui.Mix(Ui.Mix(Theme.Dim, Theme.OnAccent, position), Theme.Card, faded)))
        using (var path = Ui.Round(
            new RectangleF(4 * DpiScale + position * (Width - 8 * DpiScale - knob - stretch), 4 * DpiScale, knob + stretch, knob),
            knob / 2))
            graphics.FillPath(brush, path);
    }
}

class Dropdown : DrawnControl
{
    public readonly List<string> Items = new List<string>();
    // Items listed but greyed out.
    public int[] Off = new int[0];
    // A small tag after an item's name (Live, for the effects G-Lite draws itself), or null.
    public Func<int, string> Badge;
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
        if (!Enabled || !Visible || Off.Contains(i)) throw new InvalidOperationException(AccessibleName + " can't be changed now");
        Index = i;
        if (Picked != null) Picked(i);
    }

    protected override void OnClick(EventArgs e)
    {
        base.OnClick(e);
        if (!Enabled || Items.Count == 0) return;
        var menu = new ContextMenuStrip
        {
            Renderer = Ui.Menus, Font = Ui.Get(9.75f, Ui.Regular), ShowImageMargin = false, ShowCheckMargin = false
        };
        menu.BackColor = Theme.Card;
        menu.ForeColor = Theme.Text;
        for (int i = 0; i < Items.Count; i++)
        {
            int picked = i;
            var item = new DropdownItem(Label != null ? Label(Items[i]) : Items[i])
            {
                Current = i == index, Enabled = !Off.Contains(i), Badge = Badge != null ? Badge(i) : null
            };
            item.Click += (s, args) => { Index = picked; if (Picked != null) Picked(picked); };
            menu.Items.Add(item);
        }
        menu.MinimumSize = new Size(Width, 0);
        // Rows as wide as the menu, so the picked row's fill and the badges reach its right end; they'd stop at the longest name.
        int rowWidth = Math.Max(Width, menu.Items.Cast<ToolStripItem>().Max(item => item.Width)) - 2;
        foreach (ToolStripItem item in menu.Items)
        {
            int rowHeight = item.Height;
            item.AutoSize = false;
            item.Size = new Size(rowWidth, rowHeight);
        }
        menu.Closed += (s, args) => BeginInvoke((Action)menu.Dispose);
        menu.Show(this, new Point(0, Height + (int)(2 * DpiScale)));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var graphics = Begin(e);
        Color fore = Enabled ? Theme.Text : Ui.Mix(Theme.Button, Theme.Text, 0.45);
        Ui.Fill(graphics, new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), 6 * DpiScale,
            Pressed ? Theme.ButtonPressed : Enabled && hover ? Theme.ButtonHover : Theme.Button, Theme.Border);
        DrawFocusRing(graphics, 6 * DpiScale);
        var font = Ui.Get(9.75f, Ui.Regular);
        float left = 10 * DpiScale, textWidth = Width - 38 * DpiScale;
        string badge = Badge != null && index >= 0 && index < Items.Count ? Badge(index) : null;
        if (badge != null)
        {
            // Small and dim before the chevron, as the menu's rows draw it (DropdownItem).
            var badgeFont = Ui.Get(7.5f, Ui.Regular);
            float badgeWidth = TextRenderer.MeasureText(graphics, badge, badgeFont, Size.Empty, TextFormatFlags.NoPadding).Width;
            var badgeArea = new RectangleF(left + textWidth - badgeWidth, 0, badgeWidth, Height);
            Ui.Draw(graphics, badge, badgeFont, badgeArea, Enabled ? Theme.Dim : fore, TextFormatFlags.VerticalCenter);
            textWidth -= badgeWidth + 6 * DpiScale;
        }
        Ui.Draw(graphics, Text, font, new RectangleF(left, 0, textWidth, Height), fore, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        Icons.Draw(graphics, "chevron",
            new RectangleF(Width - 26 * DpiScale, (Height - 16 * DpiScale) / 2, 16 * DpiScale, 16 * DpiScale), Enabled ? Theme.Dim : fore);
    }
}

// A Dropdown's row: the picked one is filled with the accent instead of a check mark, and its badge (Live) is drawn small and dim
// at the row's right end (MenuRenderer).
class DropdownItem : ToolStripMenuItem
{
    public bool Current;
    public string Badge;

    public DropdownItem(string text) : base(text) { }

    public void DrawBadge(Graphics graphics)
    {
        if (Badge == null) return;
        float scale = Owner != null ? Owner.DeviceDpi / 96f : 1;
        var font = Ui.Get(7.5f, Ui.Regular);
        var flags = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix;
        var size = TextRenderer.MeasureText(graphics, Badge, font, Size.Empty, flags);
        var color = Current ? Ui.Mix(Theme.Accent, Theme.OnAccent, 0.7) : Theme.Dim;
        TextRenderer.DrawText(graphics, Badge, font, new Point(Width - size.Width - (int)(10 * scale), (Height - size.Height) / 2), color, flags);
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
        Ui.Fill(graphics, new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), 6 * DpiScale,
            Theme.Button, Box.Focused ? Theme.Accent : Theme.Border);
    }
}

class Slider : DrawnControl
{
    public int Max = 100, Value, Step = 5;
    // The only values the slider takes, in order, unmarked (ticks under the track were dropped at the owner's request); null for any.
    public int[] Stops;
    // Off, drawn at 0, for a row whose setting doesn't apply and whose value is blank (Rows.Slider); a read-only slider shows its value.
    public bool ZeroWhenOff;
    public event EventHandler Changed;
    int wheel, grab;

    protected override bool Clicks { get { return false; } }

    protected override bool IsInputKey(Keys keyData)
    {
        return keyData == Keys.PageUp || keyData == Keys.PageDown || keyData == Keys.Home || keyData == Keys.End
            || base.IsInputKey(keyData);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!Enabled) return;
        int value;
        switch (e.KeyCode)
        {
            case Keys.Left: case Keys.Down: value = Stops != null ? Next(-1) : Value - 1; break;
            case Keys.Right: case Keys.Up: value = Stops != null ? Next(1) : Value + 1; break;
            case Keys.PageDown: value = Stops != null ? Next(-1) : Value - 10; break;
            case Keys.PageUp: value = Stops != null ? Next(1) : Value + 10; break;
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
        // Only once picked (clicked or tabbed to), as Windows' own sliders: otherwise the wheel scrolls the screen it's on, and a
        // slider passing under the pointer would change, and send its value.
        if (!Enabled || !Focused) return;
        var handled = e as HandledMouseEventArgs;
        if (handled != null) handled.Handled = true;
        wheel += e.Delta;
        int notches = wheel / 120;
        if (notches == 0) return;
        wheel -= notches * 120;
        SetValue(Stops != null ? Next(Math.Sign(notches)) : Value + notches * Step);
    }

    int Next(int direction)
    {
        int at = Array.IndexOf(Stops, Nearest(Value));
        return Stops[Math.Max(0, Math.Min(Stops.Length - 1, at + direction))];
    }

    int Nearest(int value)
    {
        int best = Stops[0];
        foreach (int stop in Stops) if (Math.Abs(stop - value) < Math.Abs(best - value)) best = stop;
        return best;
    }

    public void SetValue(int value)
    {
        value = Math.Max(0, Math.Min(Max, value));
        if (Stops != null && Stops.Length > 0) value = Nearest(value);
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
        int shown = Enabled || !ZeroWhenOff ? Value : 0;
        using (var track = new SolidBrush(Theme.Track))
            graphics.FillRectangle(track, 7 * DpiScale, mid - 2 * DpiScale, Width - 14 * DpiScale, 4 * DpiScale);
        using (var brush = new SolidBrush(color))
        {
            graphics.FillRectangle(brush, 7 * DpiScale, mid - 2 * DpiScale, X(shown) - 7 * DpiScale, 4 * DpiScale);
            graphics.FillEllipse(brush, X(shown) - 7 * DpiScale, mid - 7 * DpiScale, 14 * DpiScale, 14 * DpiScale);
        }
        if (Focused && ShowFocusCues && Enabled)
            using (var pen = new Pen(Theme.Text, 2 * DpiScale))
                graphics.DrawEllipse(pen, X(Value) - 6 * DpiScale, mid - 6 * DpiScale, 12 * DpiScale, 12 * DpiScale);
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

// A view whose content can be taller than it. The content goes on Sheet, which scrolls with the mouse wheel, easing to where the
// wheel points; a thin bar at the right shows where the view is when there's more than fits (ShowBar; the device list has none),
// its thumb drags, and a click beside it jumps there. Fit says how tall the content is. Wheel turns over the content reach the
// panel through the parent chain (Windows passes an unhandled WM_MOUSEWHEEL up), except over a focused Slider, which takes them.
// Scrolling moves Sheet alone, so Windows copies what's on it in one go and paints only the strip that comes into view: moving each
// control, and repainting it, was slow with a whole page on it.
class ScrollPanel : Control
{
    int offset, content, target;
    readonly Timer glide = new Timer { Interval = 15 };
    public bool ShowBar = true;
    public readonly Control Sheet = new Control();
    // A drag of the bar: the pointer's height and the offset when it started, -1 when there's none.
    int grabbedAt = -1, grabbedOffset;
    bool overBar, scaling;

    public ScrollPanel()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.ResizeRedraw, true);
        glide.Tick += (s, e) => Glide();
        Controls.Add(Sheet);
        Sheet.ControlAdded += (s, e) => WatchFocus(e.Control);
    }

    protected override void OnBackColorChanged(EventArgs e)
    {
        base.OnBackColorChanged(e);
        Sheet.BackColor = BackColor;
    }

    // The bar's strip at the right stays the panel's, out of the sheet.
    int SheetWidth { get { return ShowBar ? Width - (int)Math.Round(14 * Ui.DpiScale) : Width; } }

    void FitSheet() { Sheet.Bounds = new Rectangle(0, -offset, SheetWidth, Math.Max(content, Height)); }

    protected override void Dispose(bool disposing)
    {
        if (disposing) glide.Dispose();
        base.Dispose(disposing);
    }

    public int Offset { get { return offset; } }

    public int Content { get { return content; } }

    public void Fit(int height)
    {
        content = height;
        FitSheet();
        ScrollTo(offset);
    }

    // item is on the sheet.
    public void Reveal(Control item) { Reveal(item.Bounds); }

    // area is in the sheet's coordinates; the view moves only as far as it takes.
    void Reveal(Rectangle area)
    {
        if (area.Top < offset) ScrollTo(area.Top);
        else if (area.Bottom > offset + Height) ScrollTo(area.Bottom - Height);
    }

    // Each control on the sheet, at any depth, scrolls into view when it gets focus (a click or Tab): the control itself, not the box
    // it's in, since a box taller than the view made the first click in it scroll, under a press that then dragged what it was on
    // (a zone of the Screen effect); and Tab moves between controls inside one box.
    void WatchFocus(Control control)
    {
        control.GotFocus += (s, e) => Reveal(Sheet.RectangleToClient(control.RectangleToScreen(control.ClientRectangle)));
        control.ControlAdded += (s, e) => WatchFocus(e.Control);
        foreach (Control child in control.Controls) WatchFocus(child);
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

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (scaling) return;
        FitSheet();
        ScrollTo(offset);
    }

    protected override void ScaleControl(SizeF factor, BoundsSpecified specified)
    {
        offset = (int)Math.Round(offset * factor.Height);
        target = (int)Math.Round(target * factor.Height);
        content = (int)Math.Round(content * factor.Height);
        // The sheet is scaled after this with the panel's other children: sized here too, at the old scale's bar width, it would
        // be scaled twice.
        scaling = true;
        base.ScaleControl(factor, specified);
        scaling = false;
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
        if (to == offset) return;
        offset = to;
        Sheet.Top = -offset;
        if (ShowBar) Invalidate();
    }

    bool HasBar { get { return ShowBar && content > Height; } }

    // The bar takes the panel's right 14 pixels (at 100%), wider than it's drawn, so it's easy to grab.
    bool OnBar(Point point) { return HasBar && point.X >= Width - 14 * Ui.DpiScale; }

    // The thumb's top and height, and how far it can move.
    void Thumb(out float top, out float height, out float travel)
    {
        float margin = 6 * Ui.DpiScale, track = Height - 2 * margin;
        height = Math.Max(24 * Ui.DpiScale, track * Height / content);
        travel = track - height;
        top = margin + travel * offset / (content - Height);
    }

    // A press on the bar is the bar's, not the panel's MouseDown. Off the thumb, the thumb jumps there first.
    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left || !OnBar(e.Location)) { base.OnMouseDown(e); return; }
        float top, height, travel;
        Thumb(out top, out height, out travel);
        if (e.Y < top || e.Y > top + height)
        {
            ScrollTo((int)Math.Round((e.Y - 6 * Ui.DpiScale - height / 2) * (content - Height) / travel));
        }
        grabbedAt = e.Y;
        grabbedOffset = offset;
        Invalidate();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (grabbedAt >= 0)
        {
            float top, height, travel;
            Thumb(out top, out height, out travel);
            if (travel > 0) ScrollTo(grabbedOffset + (int)Math.Round((e.Y - grabbedAt) * (content - Height) / travel));
            // Moves keep coming while dragging, and Windows paints only when none is waiting: what scrolls into view would stay blank
            // until the drag stops.
            WindowHelpers.PaintPending(this);
            return;
        }
        bool over = OnBar(e.Location);
        if (over != overBar) { overBar = over; Invalidate(); }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (grabbedAt >= 0) { grabbedAt = -1; Invalidate(); }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (overBar) { overBar = false; Invalidate(); }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(BackColor);
        if (!HasBar) return;
        float scale = Ui.DpiScale, top, height, travel;
        Thumb(out top, out height, out travel);
        var color = overBar || grabbedAt >= 0 ? Theme.Dim : Theme.Track;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        Ui.Fill(e.Graphics, new RectangleF(Width - 9 * scale, top, 4 * scale, height), 2 * scale, color, color);
    }
}

// A Speed slider's percent and an effect's period, 100% the shortest. Both ways round, so a period read back gives the percent it
// came from: truncating both left the slider a step off (Ripple's 2–200 ms stuck at 50% with the arrow keys).
static class SpeedScale
{
    public static int Period(int percent, int minPeriod, int maxPeriod)
    {
        return maxPeriod - (int)Math.Round((maxPeriod - minPeriod) * percent / 100.0);
    }

    public static int Percent(int period, int minPeriod, int maxPeriod)
    {
        return (int)Math.Round((maxPeriod - period) * 100.0 / (maxPeriod - minPeriod));
    }
}

class StatusLine : DrawnControl
{
    string text = "";
    bool warn, error;
    Action click;
    readonly ToolTip whole = new ToolTip();
    const TextFormatFlags Flags = TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix
        | TextFormatFlags.NoPadding;

    public StatusLine() { Cursor = Cursors.Default; TabStop = false; }

    public string Shown { get { return text; } }

    // A red dot instead of the warning's orange, for what stops the screen from working until something outside G-Lite changes.
    public bool Error
    {
        get { return error; }
        set { error = value; Invalidate(); }
    }

    // True when the text needs more than the two lines there is room for, and ends in an ellipsis. The traces mark it.
    public bool Cut
    {
        get
        {
            if (text == "") return false;
            int width = Math.Max(1, Width - (int)(16 * DpiScale));
            return TextRenderer.MeasureText(text, Ui.Get(9f, Ui.Regular), new Size(width, int.MaxValue), Flags).Height > Height;
        }
    }

    void ShowWhole() { whole.SetToolTip(this, Cut ? text : null); }

    protected override void OnResize(EventArgs e) { base.OnResize(e); ShowWhole(); }

    protected override void Dispose(bool disposing)
    {
        if (disposing) whole.Dispose();
        base.Dispose(disposing);
    }

    public void Set(string message, bool isWarning, Action action)
    {
        text = message ?? "";
        warn = isWarning;
        error = false;
        click = action;
        Cursor = action != null ? Cursors.Hand : Cursors.Default;
        AccessibleName = text;
        ShowWhole();
        Invalidate();
    }

    protected override void OnClick(EventArgs e) { base.OnClick(e); if (click != null) click(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var graphics = Begin(e);
        if (text == "") return;
        using (var brush = new SolidBrush(error ? Theme.Error : warn ? Theme.Warn : Theme.Accent))
            graphics.FillEllipse(brush, 0, Height / 2f - 4 * DpiScale, 8 * DpiScale, 8 * DpiScale);
        var font = Ui.Get(9f, Ui.Regular);
        int width = Width - (int)(16 * DpiScale),
            height = Math.Min(Height, TextRenderer.MeasureText(graphics, text, font, new Size(width, Height), Flags).Height);
        TextRenderer.DrawText(graphics, text, font, new Rectangle((int)(16 * DpiScale), (Height - height) / 2, width, height),
            warn ? Theme.Text : Theme.Dim, Flags);
    }
}
