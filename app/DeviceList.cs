using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

// The sidebar's devices: a ScrollPanel without its bar.
class DeviceList : ScrollPanel
{
    public DeviceList()
    {
        ShowBar = false;
        Ui.Style(this, () => BackColor = Theme.Sidebar);
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
