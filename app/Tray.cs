using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

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
        var choice = e.Item as DropdownItem;
        e.TextColor = choice != null && choice.Current ? Theme.OnAccent : e.Item.Enabled ? Theme.Text : Theme.Dim;
        base.OnRenderItemText(e);
        if (choice != null) choice.DrawBadge(e.Graphics);
    }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        var choice = e.Item as DropdownItem;
        if (choice == null || !choice.Current) { base.OnRenderMenuItemBackground(e); return; }
        float scale = e.Item.Owner != null ? e.Item.Owner.DeviceDpi / 96f : 1;
        int inset = (int)(2 * scale);
        using (var brush = new SolidBrush(e.Item.Selected ? Ui.Mix(Theme.Accent, Theme.Text, 0.15) : Theme.Accent))
            e.Graphics.FillRectangle(brush, inset, 0, e.Item.Width - 2 * inset, e.Item.Height);
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
                valueWidth = Math.Max(valueWidth,
                    TextRenderer.MeasureText(graphics, row[1], font, Size.Empty, flags).Width
                        + (HasIcon(row) ? S(18) + (row[1] == "" ? 0 : S(5)) : 0));
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
        int right = (Owner != null ? Owner.Width - 2 * Bounds.X : Width) - TextLeft - (int)(2 * scale),
            y = area.Top + (area.Height - size.Height) / 2;
        var color = Enabled ? Theme.Text : Theme.Dim;
        if (Level != -1)
        {
            Ui.Battery(graphics, right - Ui.BatteryWidth(scale), y + (size.Height - Ui.BatteryHeight(scale)) / 2, Level, scale, color);
            right -= Ui.BatteryWidth(scale) + (int)(5 * scale);
        }
        TextRenderer.DrawText(graphics, value, Font, new Point(right - size.Width, y), color, flags);
    }
}
