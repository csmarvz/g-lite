using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

class DeviceList : Control
{
    int offset, content, target;
    readonly Timer glide = new Timer { Interval = 15 };

    public DeviceList()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.ResizeRedraw, true);
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
        using (var brush = new LinearGradientBrush(new RectangleF(0, area.Top - 1, 1, area.Height + 2),
            fromTop ? solid : clear, fromTop ? clear : solid, LinearGradientMode.Vertical))
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
        if (On || hover)
        {
            var color = On ? Theme.AccentBackground : Pressed ? Theme.ButtonPressed : Theme.Button;
            Ui.Fill(graphics, new RectangleF(0, 0, Width, Height), 6 * DpiScale, color, color);
        }
        DrawFocusRing(graphics, 6 * DpiScale);
        float s = 32 * DpiScale, y = (Height - s) / 2;
        Ui.Fill(graphics, new RectangleF(8 * DpiScale, y, s, s), 6 * DpiScale, Theme.Button, Theme.Button);
        Icons.Draw(graphics, Icon, new RectangleF(8 * DpiScale + 7 * DpiScale, y + 7 * DpiScale, 18 * DpiScale, 18 * DpiScale),
            On ? Theme.Accent : Theme.Dim);
        float x = 8 * DpiScale + s + 10 * DpiScale;
        Ui.Draw(graphics, Title, Ui.Get(9.75f, Ui.Semibold), new RectangleF(x, 0, Width - x - 4 * DpiScale, Height / 2f + 1), Theme.Text,
            TextFormatFlags.Bottom | TextFormatFlags.EndEllipsis);
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
                Ui.Draw(graphics, Value, font, new RectangleF(right, top, valueWidth + 4 * DpiScale, Height / 2f), Theme.Dim,
                    TextFormatFlags.Top);
            }
            right -= 6 * DpiScale;
        }
        Ui.Draw(graphics, Subtitle, font, new RectangleF(x, top, right - x, Height / 2f), Theme.Dim,
            TextFormatFlags.Top | TextFormatFlags.EndEllipsis);
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
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
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
        Ui.Draw(graphics, "G-Lite", Ui.Get(10.5f, Ui.Semibold), new RectangleF(x, 0, Width - x, Height), Theme.Text,
            TextFormatFlags.VerticalCenter);
    }
}
