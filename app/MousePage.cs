using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;

class MousePicture : DrawnControl
{
    const float TextGap = 70, TextWidth = 220;
    readonly Stopwatch clock = Stopwatch.StartNew();
    readonly Timer timer = new Timer { Interval = 33 };
    MouseLight light;
    int dpi;
    string stage = "", rate = "";
    // The PRO family's shape (the G Pro Wireless, the PRO X SUPERLIGHTs), from a photo: a dome at each end, nearly straight sides, the
    // buttons in grooves set in from the sides, the wheel in its slot, side buttons on both sides, the DPI dots and the light on the
    // palm, drawn as G-Lite's logo. Dex: the PRO X 2 DEX, right-handed, from its photo: narrower at the front than at the rear, a slight
    // waist on the left below the side buttons. Other mice get a plain rounded shape.
    public bool Pro, Dex;
    // The G502 family's shape, traced from a schematic of the G502 LIGHTSPEED: the buttons as blades either side of a strip with the
    // wheel, the mode button and G9, G7 and G8 on the left, the thumb rest bulging at the left rear, the palm with the logo and the
    // DPI lights, both lit in the mouse's colour.
    public bool G502;

    public MousePicture()
    {
        Cursor = Cursors.Default;
        TabStop = false;
        AccessibleRole = AccessibleRole.Graphic;
        AccessibleName = "Mouse";
        timer.Tick += (s, e) => { if (Visible) Invalidate(); };
        VisibleChanged += (s, e) => timer.Enabled = Visible;
    }

    public void Display(MouseLight newLight, int newDpi, string newStage, string newRate)
    {
        light = newLight;
        dpi = newDpi;
        stage = newStage;
        rate = newRate;
        Invalidate();
    }

    Color Glow(double seconds)
    {
        if (light == null) return KeyboardView.Hsv(seconds / 8, 1, 1);
        var color = Color.FromArgb(light.Color >> 16 & 0xFF, light.Color >> 8 & 0xFF, light.Color & 0xFF);
        double brightness = light.Bright / 100.0, period = Math.Max(1, light.Period / 1000.0);
        switch (light.Effect)
        {
            case 0x01: return KeyboardView.Scale(color, brightness);
            case 0x0A: return KeyboardView.Scale(color, brightness * (0.5 - 0.5 * Math.Cos(2 * Math.PI * seconds / period)));
            case 0x03: return KeyboardView.Scale(KeyboardView.Hsv(seconds / period, 1, 1), brightness);
            default: return Color.Black;
        }
    }

    // The outline in its box: the G502's, the DEX's, the PRO family's (a little wider at the rear), or a plain rounded one.
    GraphicsPath Body(RectangleF box)
    {
        if (G502) return G502Body(box);
        if (!Pro) return Ui.Round(box, box.Width / 2);
        if (Dex) return DexBody(box);
        Func<float, float, PointF> at = (across, down) => new PointF(box.X + box.Width * across, box.Y + box.Height * down);
        var path = new GraphicsPath();
        path.AddBezier(at(0.5f, 0), at(0.79f, 0), at(0.98f, 0.07f), at(0.985f, 0.22f));
        path.AddLine(at(0.985f, 0.22f), at(1, 0.7f));
        path.AddBezier(at(1, 0.7f), at(1, 0.92f), at(0.78f, 1), at(0.5f, 1));
        path.AddBezier(at(0.5f, 1), at(0.22f, 1), at(0, 0.92f), at(0, 0.7f));
        path.AddLine(at(0, 0.7f), at(0.015f, 0.22f));
        path.AddBezier(at(0.015f, 0.22f), at(0.02f, 0.07f), at(0.21f, 0), at(0.5f, 0));
        path.CloseFigure();
        return path;
    }

    // The two button blades at the front with the strip's notch between them, the right side, the rear, and the left side's thumb rest
    // and side buttons back up to the left blade.
    static GraphicsPath G502Body(RectangleF box)
    {
        Func<float, float, PointF> at = (across, down) => new PointF(box.X + box.Width * across, box.Y + box.Height * down);
        var path = new GraphicsPath();
        path.AddLines(new[] { at(0.21f, 0.095f), at(0.477f, 0.018f), at(0.485f, 0.093f), at(0.64f, 0.095f), at(0.621f, 0.002f), at(0.858f, 0.089f) });
        path.AddBezier(at(0.858f, 0.089f), at(0.9f, 0.12f), at(0.95f, 0.2f), at(0.966f, 0.292f));
        path.AddLines(new[] { at(0.966f, 0.292f), at(0.992f, 0.704f), at(0.975f, 0.745f) });
        path.AddBezier(at(0.975f, 0.745f), at(0.96f, 0.9f), at(0.75f, 1), at(0.533f, 1));
        path.AddBezier(at(0.533f, 1), at(0.35f, 1), at(0.18f, 0.85f), at(0.025f, 0.698f));
        path.AddLine(at(0.025f, 0.698f), at(0.005f, 0.67f));
        path.AddBezier(at(0.005f, 0.67f), at(0, 0.55f), at(0.01f, 0.47f), at(0.08f, 0.453f));
        path.AddLines(new[] { at(0.08f, 0.453f), at(0.1f, 0.45f), at(0.084f, 0.382f), at(0.115f, 0.328f), at(0.098f, 0.28f), at(0.21f, 0.095f) });
        path.CloseFigure();
        return path;
    }

    static GraphicsPath DexBody(RectangleF box)
    {
        // Points across (0 left, 1 right) and down (0 front, 1 rear) the box.
        Func<float, float, PointF> at = (across, down) => new PointF(box.X + box.Width * across, box.Y + box.Height * down);
        var path = new GraphicsPath();
        path.AddBezier(at(0.5f, 0), at(0.78f, 0), at(0.92f, 0.06f), at(0.93f, 0.18f));
        path.AddBezier(at(0.93f, 0.18f), at(0.95f, 0.4f), at(0.99f, 0.55f), at(0.995f, 0.72f));
        path.AddBezier(at(0.995f, 0.72f), at(1, 0.92f), at(0.78f, 1), at(0.5f, 1));
        path.AddBezier(at(0.5f, 1), at(0.2f, 1), at(0, 0.9f), at(0.005f, 0.75f));
        path.AddBezier(at(0.005f, 0.75f), at(0.01f, 0.62f), at(0.07f, 0.58f), at(0.07f, 0.5f));
        path.AddBezier(at(0.07f, 0.5f), at(0.07f, 0.38f), at(0.04f, 0.28f), at(0.05f, 0.2f));
        path.AddBezier(at(0.05f, 0.2f), at(0.06f, 0.06f), at(0.22f, 0), at(0.5f, 0));
        path.CloseFigure();
        return path;
    }

    // The plain shape and the PRO family's: their outline, the buttons' grooves, the wheel and the PRO's side buttons and DPI dots.
    void DrawBody(Graphics graphics, RectangleF body)
    {
        float x = body.X, y = body.Y, width = body.Width, height = body.Height;
        // Where the buttons end.
        float split = height * (Dex ? 0.46f : Pro ? 0.45f : 0.4f);
        // Side buttons: both sides on the G Pro's family (ambidextrous), the left only on the DEX.
        if (Pro)
            foreach (var side in Dex ? new[] { 0f } : new[] { 0f, 1f })
                foreach (var buttonTop in Dex ? new[] { 0.28f, 0.45f } : new[] { 0.44f, 0.53f })
                    Ui.Fill(graphics, new RectangleF(x + (Dex ? width * 0.05f : side * width) - 2.5f * DpiScale, y + height * buttonTop,
                        5 * DpiScale, height * (Dex ? 0.16f : 0.085f)), 2 * DpiScale, Theme.KeyOff, Theme.Border);
        using (var path = Body(body))
        {
            using (var brush = new SolidBrush(Theme.KeyOff)) graphics.FillPath(brush, path);
            graphics.SetClip(path);
            using (var pen = new Pen(Theme.PictureBackground, 1.5f * DpiScale))
            {
                graphics.DrawLine(pen, x + width / 2, y, x + width / 2, y + split);
                if (Pro)
                {
                    // The buttons' outer grooves, from the notch near the front to where they end (the DEX's, from its photo).
                    float notch = Dex ? 0.12f : 0.1f, leftTop = Dex ? 0.12f : 0.07f, leftBottom = Dex ? 0.15f : 0.1f,
                        rightTop = Dex ? 0.91f : 0.93f, rightBottom = Dex ? 0.89f : 0.9f;
                    // The buttons' rear corners are rounded, as on the mouse.
                    float corner = width * 0.06f, left = x + width * leftBottom, right = x + width * rightBottom, bottom = y + split;
                    using (var grooves = new GraphicsPath())
                    {
                        grooves.AddLine(x, y + height * notch, x + width * leftTop, y + height * notch);
                        grooves.AddLine(x + width * leftTop, y + height * notch, left, bottom - corner);
                        grooves.AddBezier(left, bottom - corner, left, bottom - corner * 0.45f, left + corner * 0.45f, bottom, left + corner, bottom);
                        grooves.AddLine(left + corner, bottom, right - corner, bottom);
                        grooves.AddBezier(right - corner, bottom, right - corner * 0.45f, bottom, right, bottom - corner * 0.45f, right, bottom - corner);
                        grooves.AddLine(right, bottom - corner, x + width * rightTop, y + height * notch);
                        grooves.AddLine(x + width * rightTop, y + height * notch, x + width, y + height * notch);
                        graphics.DrawPath(pen, grooves);
                    }
                }
                else graphics.DrawLine(pen, x, y + split, x + width, y + split);
            }
            graphics.ResetClip();
            using (var pen = new Pen(Theme.Border)) graphics.DrawPath(pen, path);
        }
        if (Pro)
        {
            // The wheel in its slot, ridged.
            var slot = Dex ? new RectangleF(x + width * 0.43f, y + height * 0.135f, width * 0.15f, height * 0.165f)
                : new RectangleF(x + width * 0.415f, y + height * 0.11f, width * 0.17f, height * 0.17f);
            Ui.Fill(graphics, slot, slot.Width * 0.35f, Theme.PictureBackground, Theme.PictureBackground);
            var wheel = new RectangleF(slot.X + slot.Width * 0.25f, slot.Y + slot.Height * 0.08f, slot.Width * 0.5f, slot.Height * 0.84f);
            Ui.Fill(graphics, wheel, wheel.Width / 2, Theme.Border, Theme.Border);
            using (var pen = new Pen(Theme.PictureBackground, 1 * DpiScale))
                for (int ridge = 1; ridge < 8; ridge++)
                    graphics.DrawLine(pen, wheel.X + 1 * DpiScale, wheel.Y + wheel.Height * ridge / 8, wheel.Right - 1 * DpiScale, wheel.Y + wheel.Height * ridge / 8);
        }
        if (Pro && !Dex)
            using (var brush = new SolidBrush(Theme.Border))
                for (int dot = 0; dot < 3; dot++)
                    graphics.FillEllipse(brush, x + width / 2 - 1.5f * DpiScale, y + height * (0.485f + dot * 0.03f), 3 * DpiScale, 3 * DpiScale);
        if (!Pro)
            Ui.Fill(graphics, new RectangleF(x + width / 2 - 4 * DpiScale, y + height * 0.12f, 8 * DpiScale, height * 0.17f),
                4 * DpiScale, Theme.PictureBackground, Theme.PictureBackground);
    }

    // The G502, in points across (0 left, 1 right) and down (0 front, 1 rear) its box, traced from the schematic.
    void DrawG502(Graphics graphics, RectangleF body)
    {
        Func<float, float, PointF> at = (across, down) => new PointF(body.X + body.Width * across, body.Y + body.Height * down);
        using (var path = Body(body))
        {
            using (var brush = new SolidBrush(Theme.KeyOff)) graphics.FillPath(brush, path);
            graphics.SetClip(path);
            using (var pen = new Pen(Theme.PictureBackground, 1.5f * DpiScale) { LineJoin = LineJoin.Round })
            {
                // The strip between the buttons, each button's rear edge, and its outer edge along G7 and G8.
                graphics.DrawLine(pen, at(0.485f, 0.093f), at(0.523f, 0.549f));
                graphics.DrawLine(pen, at(0.64f, 0.095f), at(0.658f, 0.52f));
                graphics.DrawLine(pen, at(0.255f, 0.563f), at(0.52f, 0.5f));
                graphics.DrawLine(pen, at(0.66f, 0.47f), at(0.905f, 0.513f));
                graphics.DrawLines(pen, new[] { at(0.234f, 0.095f), at(0.22f, 0.43f), at(0.255f, 0.563f) });
                graphics.DrawLine(pen, at(0.105f, 0.274f), at(0.23f, 0.262f));
                graphics.DrawLine(pen, at(0.109f, 0.289f), at(0.25f, 0.561f));
                // The palm: its front edge across to the right side, its rear edge round the logo, and the rear's split.
                graphics.DrawLines(pen, new[] { at(0.192f, 0.624f), at(0.659f, 0.527f), at(0.88f, 0.546f), at(0.878f, 0.668f) });
                graphics.DrawBezier(pen, at(0.878f, 0.668f), at(0.9f, 0.7f), at(0.95f, 0.71f), at(0.975f, 0.745f));
                graphics.DrawBezier(pen, at(0.209f, 0.626f), at(0.215f, 0.72f), at(0.27f, 0.78f), at(0.523f, 0.833f));
                graphics.DrawLine(pen, at(0.523f, 0.833f), at(0.523f, 1));
                graphics.DrawLine(pen, at(0.586f, 0.835f), at(0.611f, 1));
            }
            graphics.ResetClip();
            using (var pen = new Pen(Theme.Border)) graphics.DrawPath(pen, path);
        }
        // The wheel in its slot, then the mode button and G9 below it; the arrows either side for its tilt.
        var slot = new RectangleF(at(0.505f, 0.16f), new SizeF(body.Width * 0.135f, body.Height * 0.175f));
        Ui.Fill(graphics, slot, slot.Width * 0.3f, Theme.PictureBackground, Theme.PictureBackground);
        var wheel = new RectangleF(slot.X + slot.Width * 0.22f, slot.Y + slot.Height * 0.08f, slot.Width * 0.56f, slot.Height * 0.84f);
        Ui.Fill(graphics, wheel, wheel.Width * 0.3f, Theme.Border, Theme.Border);
        using (var pen = new Pen(Theme.PictureBackground, 1 * DpiScale))
            for (int ridge = 1; ridge < 8; ridge++)
                graphics.DrawLine(pen, wheel.X + 1 * DpiScale, wheel.Y + wheel.Height * ridge / 8, wheel.Right - 1 * DpiScale, wheel.Y + wheel.Height * ridge / 8);
        foreach (var button in new[] { new RectangleF(at(0.515f, 0.357f), new SizeF(body.Width * 0.125f, body.Height * 0.066f)),
            new RectangleF(at(0.52f, 0.435f), new SizeF(body.Width * 0.12f, body.Height * 0.078f)) })
            Ui.Fill(graphics, button, 2 * DpiScale, Theme.KeyOff, Theme.PictureBackground);
        using (var brush = new SolidBrush(Theme.Border))
        {
            graphics.FillPolygon(brush, new[] { at(0.405f, 0.25f), at(0.44f, 0.237f), at(0.44f, 0.263f) });
            graphics.FillPolygon(brush, new[] { at(0.73f, 0.25f), at(0.695f, 0.237f), at(0.695f, 0.263f) });
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var graphics = Begin(e);
        // The mouse and its text centred together, the text counted at a fixed width so the picture doesn't move as the DPI does.
        float height = Height - 16 * DpiScale, width = height * (G502 ? 0.57f : Dex ? 0.54f : Pro ? 0.5f : 0.6f), y = 8 * DpiScale;
        float x = Math.Max(8 * DpiScale, (Width - width - TextGap * DpiScale - TextWidth * DpiScale) / 2);
        var body = new RectangleF(x, y, width, height);
        if (G502) DrawG502(graphics, body);
        else DrawBody(graphics, body);

        var color = Glow(clock.Elapsed.TotalSeconds);
        int max = Math.Max(color.R, Math.Max(color.G, color.B));
        float radius = width * (G502 ? 0.09f : Dex ? 0.1f : Pro ? 0.12f : 0.17f), centerX = x + width * (G502 ? 0.4f : 0.5f),
            centerY = y + height * (G502 ? 0.686f : Dex ? 0.74f : Pro ? 0.75f : 0.68f);
        var fill = Theme.KeyOff;
        if (max > 0)
        {
            var full = Color.FromArgb(color.R * 255 / max, color.G * 255 / max, color.B * 255 / max);
            fill = Ui.Mix(Theme.PictureBackground, full, max / 255.0);
            using (var glowPath = new GraphicsPath())
            {
                glowPath.AddEllipse(centerX - radius * 2.4f, centerY - radius * 2.4f, radius * 4.8f, radius * 4.8f);
                using (var glowBrush = new PathGradientBrush(glowPath)
                {
                    CenterColor = Color.FromArgb((int)(110 * max / 255.0), full),
                    SurroundColors = new[] { Color.FromArgb(0, full) }
                })
                {
                    using (var clip = Body(body)) graphics.SetClip(clip);
                    graphics.FillPath(glowBrush, glowPath);
                    graphics.ResetClip();
                }
            }
        }
        else fill = Pro || G502 ? Theme.Border : Theme.PictureBackground;
        if (G502)
            using (var pen = new Pen(fill, 1.5f * DpiScale) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                for (int dpiLight = 0; dpiLight < 3; dpiLight++)
                    graphics.DrawLine(pen, x + width * 0.183f, y + height * (0.459f + dpiLight * 0.036f), x + width * 0.168f, y + height * (0.483f + dpiLight * 0.036f));
        if (Pro || G502) Logo.Paint(graphics, new RectangleF(centerX - radius, centerY - radius, 2 * radius, 2 * radius), fill, Theme.KeyOff);
        else using (var brush = new SolidBrush(fill)) graphics.FillEllipse(brush, centerX - radius, centerY - radius, 2 * radius, 2 * radius);

        float textX = x + width + TextGap * DpiScale, top = Height / 2f - 46 * DpiScale;
        var bigFont = Ui.Get(27f, Ui.Semibold);
        var text = dpi > 0 ? dpi.ToString() : "–";
        Ui.Draw(graphics, text, bigFont, new RectangleF(textX, top, Width - textX, 48 * DpiScale),
            Theme.Text, TextFormatFlags.Left | TextFormatFlags.Bottom);
        float dpiWidth = TextRenderer.MeasureText(graphics, text, bigFont, Size.Empty, TextFormatFlags.NoPadding).Width;
        Ui.Draw(graphics, "DPI", Ui.Get(11f, Ui.Semibold),
            new RectangleF(textX + dpiWidth + 8 * DpiScale, top, 80 * DpiScale, 44 * DpiScale),
            Theme.Dim, TextFormatFlags.Left | TextFormatFlags.Bottom);
        var small = Ui.Get(9.75f, Ui.Regular);
        Ui.Draw(graphics, stage, small, new RectangleF(textX, top + 58 * DpiScale, Width - textX, 20 * DpiScale),
            Theme.Dim, TextFormatFlags.Left);
        Ui.Draw(graphics, rate, small, new RectangleF(textX, top + 80 * DpiScale, Width - textX, 20 * DpiScale),
            Theme.Dim, TextFormatFlags.Left);
    }
}

class MousePage : DevicePage
{
    MouseSettings current = new MouseSettings(), stored, sent;
    MouseProtocol.State state;
    bool loading, dirty, lightSet;
    int sequence;
    readonly Timer debounce = new Timer { Interval = 120 }, watch = new Timer { Interval = 5000 };
    readonly Toggle invert;
    readonly SegmentedPicker viewPick;
    // Settings' sections and the rows a mouse may not have, in design units; the Bindings view's buttons, one row each.
    readonly RowLayout layout;
    readonly RoundedBox sensitivity, lighting, buttonsBox;
    readonly int stageRow, rateRow, invertRow, noteRow, effectRowY, colorRow, brightRow, speedRow;
    readonly RowText stageText, rateText, effectText, colorText, brightText, speedText;
    readonly Label buttonsHeading;
    const int PictureHeight = 168, PickerWidth = 336, NotePadding = 14;
    const string UnsupportedLight = "G-Lite can't set this mouse's lighting yet.";
    readonly ButtonRows buttonRows;
    readonly Timer pressedTimer = new Timer { Interval = 1500 };
    bool showingButtons, spying;
    ButtonSpy spy;

    internal override bool Busy { get { return debounce.Enabled; } }

    readonly Label speedValue, brightValue, dpiValue;
    readonly SegmentedPicker stages, rates;
    readonly Dropdown effects;
    readonly ColorRow colors;
    readonly Slider speed, bright, dpi;
    readonly MousePicture picture;
    readonly FlatButton saveButton, backupsButton;
    Backups.Entry fix;
    readonly IMouse mouse;

    public MousePage(FoundDevice found, Hidpp fake, IMouse mouse)
        : base(found, fake, found.ShortName, "mouse", "profile." + found.SettingsKey + ".")
    {
        this.mouse = mouse;
        debounce.Tick += (s, e) => { debounce.Stop(); Send(); };
        backupsButton = BackupsButton("mouse's profile");
        backupsButton.Click += (s, e) => ShowBackups();

        // The picture stays above the rows, which scroll under it.
        picture = Ui.Add(this, new MousePicture(), 20, BodyTop, Rows.Width, PictureHeight);
        string upper = found.Name.ToUpperInvariant();
        picture.Pro = Regex.IsMatch(upper, @"\bPRO\b");
        picture.Dex = Regex.IsMatch(upper, @"\bDEX\b");
        picture.G502 = upper.Contains("G502");
        var sheet = UseBody(BodyTop + PictureHeight + 12).Sheet;
        layout = new RowLayout(sheet);

        // Sensitivity first: DPI is what's changed most, and some mice have no lighting. Rows a mouse doesn't have go once it's read.
        Label heading;
        sensitivity = layout.Section("Sensitivity", out heading);
        stageRow = layout.Row(sensitivity);
        stageText = Rows.Row(sensitivity, stageRow, "DPI stage", "The DPI buttons switch between these");
        stages = Rows.Picker(sensitivity, stageRow, "DPI stage", new string[0], PickerWidth);
        stages.Picked += index => { current.Stage = index; current.Dpi = state.Stages[index]; Changed(true); };
        int dpiRow = layout.Row(sensitivity);
        Rows.Row(sensitivity, dpiRow, "DPI", "The picked stage's sensitivity");
        dpi = Rows.Slider(sensitivity, dpiRow, "DPI", out dpiValue);
        dpi.Step = 1;
        // DPI always applies: off only on a read-only mouse, which shows its value.
        dpi.ZeroWhenOff = false;
        dpi.Changed += (s, e) => { if (state != null && state.Dpis.Length > 0) { current.Dpi = state.Dpis[dpi.Value]; Changed(false); } };
        rateRow = layout.Row(sensitivity);
        rateText = Rows.Row(sensitivity, rateRow, "Report rate", "");
        rates = Rows.Picker(sensitivity, rateRow, "Report rate", new string[0], PickerWidth);
        rates.Picked += index =>
        {
            if (!state.Onboard) { current.Rate = state.Rates[index]; Edited(); SendRate(state.Rates[index]); return; }
            current.Rate = state.Rates[index];
            Changed(true);
        };
        invertRow = layout.Row(sensitivity);
        Rows.Row(sensitivity, invertRow, "Invert scrolling", "The wheel scrolls the other way");
        invert = Rows.Toggle(sensitivity, invertRow, "Invert scrolling");
        invert.Changed += () => SetInvert(invert.Checked);

        lighting = layout.Section("Lighting", out heading);
        noteRow = layout.Row(lighting, Rows.NoteHeight(UnsupportedLight) + 2 * NotePadding);
        Rows.Note(lighting, noteRow + NotePadding, UnsupportedLight);
        int effectRow = layout.Row(lighting);
        effectText = Rows.Row(lighting, effectRow, "Effect", "");
        effects = Rows.Dropdown(lighting, effectRow, "Effect");
        effects.Items.AddRange(mouse.Effects.Select(effect => effect.Name));
        effects.Picked += index =>
        {
            var effect = Effects[index];
            var edited = Light();
            edited.Effect = effect.Id;
            if (effect.Speed && (edited.Period < effect.MinPeriod || edited.Period > effect.MaxPeriod))
                edited.Period = effect.DefaultPeriod;
            Changed(true);
        };
        colorRow = layout.Row(lighting);
        colorText = Rows.Row(lighting, colorRow, "Color", "The effect's color");
        colors = new ColorRow(lighting, Rows.Width - Rows.Inset - ColorRow.Widest, colorRow + (Ui.RowHeight - ColorRow.Size) / 2, true);
        colors.Picked += color => { Light().Color = color; Changed(true); };
        brightRow = layout.Row(lighting);
        brightText = Rows.Row(lighting, brightRow, "Brightness", "How bright the light is");
        bright = Rows.Slider(lighting, brightRow, "Brightness", out brightValue);
        bright.Changed += (s, e) => { Light().Bright = Math.Max(1, bright.Value); Changed(false); };
        speedRow = layout.Row(lighting);
        speedText = Rows.Row(lighting, speedRow, "Speed", "How fast the effect moves");
        speed = Rows.Slider(lighting, speedRow, "Speed", out speedValue);
        speed.Changed += (s, e) =>
        {
            var edited = Light();
            var effect = MouseProtocol.EffectById(edited.Effect);
            edited.Period = SpeedScale.Period(speed.Value, effect.MinPeriod, effect.MaxPeriod);
            Changed(false);
        };
        effectRowY = effectRow;
        watch.Tick += (s, e) => Watch();
        if (!Manual) watch.Start();

        viewPick = Ui.Add(this, new SegmentedPicker { Items = new[] { "Settings", "Bindings" }, AccessibleName = "View", Visible = false },
            440, 72, 196, 32);
        viewPick.Picked += index => ShowView(index == 1);
        int buttonsTop = 0;
        buttonsBox = Rows.Section(sheet, "Buttons", ref buttonsTop, new RoundedBox(), 0, out buttonsHeading);
        // The hint (the button to press while finding one) on the title's line, at its right.
        buttonRows = new ButtonRows(buttonsBox, Bindings.MouseActions, Value(sheet, 220, 0, Rows.Width - 200));
        buttonRows.Assigned += (row, binding) => { if (!loading && state != null && current.Buttons != null) Assign(row, binding); };
        pressedTimer.Tick += (s, e) => { pressedTimer.Stop(); buttonRows.Pressed = -1; SyncButtons(); };

        saveButton = Ui.Add(this, new FlatButton { Style = ButtonStyle.Primary, Text = "Save to mouse" }, 496, 685, 140, 32);
        saveButton.Click += (s, e) => Save();
        InFooter(saveButton);

        RefreshControls();
    }

    EffectInfo[] Effects { get { return state != null ? state.Effects : mouse.Effects; } }

    MouseLight Light()
    {
        if (current.Light == null) current.Light = new MouseLight();
        return current.Light;
    }

    public override void Restyle() { RefreshControls(); }

    protected override bool Unsaved { get { return dirty; } }

    protected override bool Damaged { get { return state != null && !state.ProfileOk; } }

    protected override string Current() { return current.ToText(); }

    protected override void ApplyPreset(string text)
    {
        if (state != null && state.ReadOnly) return;
        var settings = MouseSettings.FromText(text);
        var saved = stored != null && stored.Buttons != null ? stored.Buttons : current.Buttons;
        if (current.Buttons == null) settings.Buttons = null;
        else if (settings.Buttons == null) settings.Buttons = Bindings.Copy(saved);
        else settings.Buttons = Enumerable.Range(0, current.Buttons.Length)
            .Select(i => i < settings.Buttons.Length ? settings.Buttons[i] : (byte[])saved[i].Clone())
            .ToArray();
        if (settings.Buttons != null && !Bindings.HasLeft(settings.Buttons, ButtonCount))
            settings.Buttons[0] = (byte[])Bindings.MouseActions[0].Bytes.Clone();
        if (state != null && state.Dpis.Length > 0) settings.Dpi = state.Dpis[Nearest(state.Dpis, settings.Dpi)];
        settings.Stage = state != null && Array.IndexOf(state.Stages, settings.Dpi) >= 0
            ? Array.IndexOf(state.Stages, settings.Dpi)
            : current.Stage;
        current = settings;
        Changed(true);
    }

    protected override void Revert()
    {
        if (stored == null) return;
        buttonRows.Recording = -1;
        current = stored.Clone();
        Changed(true);
        dirty = lightSet && stored.Light == null;
        RefreshControls();
        ClearStatus();
    }

    static int Nearest(int[] values, int value)
    {
        int best = 0;
        for (int i = 0; i < values.Length; i++) if (Math.Abs(values[i] - value) < Math.Abs(values[best] - value)) best = i;
        return best;
    }

    void RefreshControls()
    {
        loading = true;
        bool ready = state != null;
        var light = current.Light;
        var effect = light == null ? null : Effects.FirstOrDefault(info => info.Id == light.Effect);
        bool lit = ready && state.Zones.Count > 0, known = lit && effect != null, live = ready && !state.ReadOnly;
        // The G300 (SaveOnly) has no brightness: each colour is on or off.
        bool color = known && effect.Color, hasSpeed = known && effect.Speed,
            hasBrightness = known && light.Effect != 0x00 && !state.SaveOnly;
        bool hasStages = ready && state.Stages.Length > 0;
        // Before the mouse is read every row it may have shows, but Invert scrolling, which few have.
        bool unsupported = ready && !lit && found.Has(0x8071), lightRows = !ready || lit;
        layout.ShowRow(sensitivity, stageRow, !ready || hasStages);
        layout.ShowRow(sensitivity, rateRow, !ready || state.Rates.Length > 0);
        layout.ShowRow(sensitivity, invertRow, ready && state.CanInvert);
        layout.ShowSection(lighting, lightRows || unsupported);
        layout.ShowRow(lighting, noteRow, unsupported);
        foreach (int row in new[] { effectRowY, colorRow, speedRow }) layout.ShowRow(lighting, row, lightRows);
        // The G300 (SaveOnly) has no brightness at all.
        layout.ShowRow(lighting, brightRow, lightRows && !(ready && state.SaveOnly));
        bool canBind = ready && state.ButtonCount > 0 && state.Buttons != null;
        if (!canBind && showingButtons) ShowView(false);
        PlaceView();
        // The light as saved on the mouse when there's no record of it shows no effect, so the row says why.
        effectText.Hint.Text = found.LightIsLiveOnly ? "Live only: not saved to the mouse"
            : lit && light == null ? "As saved on the mouse"
            : "What the lighting does";
        var effectNames = Effects.Select(info => info.Name).ToList();
        if (!effectNames.SequenceEqual(effects.Items)) { effects.Items.Clear(); effects.Items.AddRange(effectNames); }
        effects.Enabled = lit && live;
        effects.Index = known ? Array.FindIndex(Effects, info => info.Id == light.Effect) : -1;
        effects.Invalidate();
        colorText.Enable(color);
        colors.RefreshControls(light == null ? 0xFFFFFF : light.Color, color, live);
        brightText.Enable(hasBrightness);
        bright.Enabled = hasBrightness && live;
        // At 0 only when it doesn't apply, its value blank then; a read-only mouse's shows its value.
        bright.ZeroWhenOff = !hasBrightness;
        bright.Value = light == null ? 100 : light.Bright;
        brightValue.Text = hasBrightness ? light.Bright + "%" : "";
        bright.Invalidate();
        speedText.Enable(hasSpeed);
        speed.Enabled = hasSpeed && live;
        speed.ZeroWhenOff = !hasSpeed;
        if (hasSpeed) speed.Value = SpeedScale.Percent(light.Period, effect.MinPeriod, effect.MaxPeriod);
        speedValue.Text = hasSpeed ? speed.Value + "%" : "";
        speed.Invalidate();

        int stage = !hasStages ? -1
            : !state.Writable ? Array.IndexOf(state.Stages, current.Dpi)
            : current.Stage >= 0 && current.Stage < state.Stages.Length ? current.Stage
            : -1;
        var view = hasStages ? (int[])state.Stages.Clone() : new int[0];
        if (stage >= 0 && state.Writable) view[stage] = current.Dpi;
        stages.Items = hasStages ? view.Select(stageDpi => stageDpi.ToString()).ToArray() : new[] { "–" };
        stages.Enabled = hasStages && live;
        stages.Index = stage;
        stageText.Enable(hasStages);
        dpi.Enabled = live && state.Dpis.Length > 0;
        dpi.Max = Math.Max(1, ready ? state.Dpis.Length - 1 : 1);
        dpi.Value = ready && state.Dpis.Length > 0 ? Nearest(state.Dpis, current.Dpi) : 0;
        dpi.Invalidate();
        dpiValue.Text = ready ? current.Dpi.ToString() : "";
        bool rateOn = live && state.Rates.Length > 0 && (!state.Onboard || state.Savable);
        int rate = ready ? (current.Rate > 0 ? current.Rate : state.Rate) : 0;
        rates.Items = ready && state.Rates.Length > 0
            ? state.Rates.Select(hz => hz >= 1000 ? hz / 1000 + "k" : hz.ToString()).ToArray()
            : new[] { "–" };
        rates.Index = ready ? Array.IndexOf(state.Rates, rate) : -1;
        rates.Enabled = rateOn;
        rates.Invalidate();
        rateText.Enable(rateOn);
        rateText.Hint.Text = ready && state.Onboard && state.Savable ? "In Hz, applies when you save"
            : ready && state.Onboard && !state.ReadOnly ? "In Hz, set by the saved profile"
            : "How often the mouse reports, in Hz";
        invert.Checked = ready && state.Inverted;
        viewPick.Visible = canBind;
        viewPick.Index = showingButtons ? 1 : 0;
        SyncButtons();
        saveButton.Enabled = ready && state.Savable && dirty && Keepable;
        saveButton.Visible = ready ? state.Writable : mouse.HasFactory;
        backupsButton.Visible = mouse.HasFactory || (ready && state.Writable);

        picture.Display(lit ? light : new MouseLight { Effect = 0 }, ready ? current.Dpi : 0,
            !ready ? ""
                : stage >= 0 ? "Stage " + (stage + 1) + " of " + state.Stages.Length
                    + (current.Dpi != state.Stages[stage] ? ", was " + state.Stages[stage] : "")
                : "",
            rate > 0 ? rate + " Hz report rate" + (ready && rate != state.Rate ? " after saving" : "") : "");
        loading = false;
    }

    void Changed(bool now)
    {
        if (loading || state == null) return;
        dirty = true;
        sequence++;
        Edited();
        RefreshControls();
        ClearStatus();
        // While a slider moves, sent at most every 120 ms rather than once it stops: restarting the timer on each move sent nothing
        // during a drag.
        if (now) { debounce.Stop(); Send(); }
        else if (!debounce.Enabled) debounce.Start();
    }

    protected override void ClearStatus()
    {
        if (state == null) { ShowStatus(""); return; }
        if (!state.ProfileOk) { ShowBroken(); return; }
        if (state.ReadOnly) { ShowStatus("Read only: G-Lite shows this mouse's settings but can't change them yet"); return; }
        if (!Keepable) { ShowStatus("No button is set to Left click. Set one to save.", true); return; }
        bool buttons = stored != null && !Bindings.Same(current.Buttons, stored.Buttons);
        bool rate = stored != null && state.Onboard && current.Rate > 0 && current.Rate != stored.Rate;
        // A format 6 to 8 mouse only takes its stages' DPI, so an edited stage waits for the save (HidppMouse.SendDpi). On a mouse
        // with nothing live (the G300, SaveOnly) everything does, the picked stage and the lighting too.
        bool dpiWaits = (MouseProtocol.State.WideFormat(state.Format) || state.SaveOnly) && state.Savable && current.Stage >= 0
            && current.Stage < state.Stages.Length
            && (current.Dpi != state.Stages[current.Stage] || (state.SaveOnly && stored != null && current.Stage != stored.Stage));
        bool lightingWaits = state.SaveOnly && stored != null && current.Light != null && !current.Light.Same(stored.Light);
        var waiting = new List<string>();
        if (dpiWaits) waiting.Add("DPI");
        if (lightingWaits) waiting.Add("lighting");
        if (buttons) waiting.Add("buttons");
        if (rate) waiting.Add("report rate");
        if (dirty && waiting.Count > 0)
        {
            var text = waiting.Count == 1 ? waiting[0] : string.Join(", ", waiting.Take(waiting.Count - 1)) + " and " + waiting.Last();
            ShowStatus(char.ToUpper(text[0]) + text.Substring(1) + (waiting.Count == 1 && !buttons ? " changes" : " change")
                + " when saved to the mouse");
            return;
        }
        // Only formats 2 and 3 (and the G9x pages) get their LED records written; on formats 4 to 8 a light set stays live.
        bool liveLight = state.Savable && !state.Flash && !state.SaveOnly && state.Zones.Count > 0
            && !MouseProtocol.State.KnownFormat(state.Format)
            && stored != null && current.Light != null && !current.Light.Same(stored.Light);
        ShowStatus(!dirty ? "Matches the mouse"
            : liveLight ? "Lighting applied to the mouse; Save to mouse doesn't keep it on this mouse"
            : state.Savable ? "Applied to the mouse, not saved on it yet"
            : "Applied to the mouse, not saved on it");
    }

    string BackupId { get { return found.SettingsKey; } }

    byte[] Usable(Backups.Entry entry)
    {
        var data = state == null ? null : entry.Get(state.Key);
        return data != null && state.Writable && data.Length == state.SectorSize && Intact(state, data) ? data : null;
    }

    // A profile G-Lite may write over: a sector whose CRC checks, or on a mouse with nothing live (the G300's report, no CRC) one of
    // the right size.
    static bool Intact(MouseProtocol.State profileState, byte[] data)
    {
        return profileState.SaveOnly ? data != null && data.Length == profileState.SectorSize : MouseProtocol.Valid(data);
    }

    void ShowBroken()
    {
        const string bad = "The profile saved on the mouse is damaged.";
        if (fix != null)
            ShowStatus(bad + " Last good backup: " + When(fix) + ".", true,
                () => Restore(Usable(fix), "the backup from " + When(fix)), "Restore backup");
        else if (state.Writable && mouse.HasFactory)
            ShowStatus(bad, true, () => Restore(null, "the factory profile"), "Restore factory profile");
        else ShowStatus(bad + " G-Lite can't repair it on this mouse.", true);
    }

    void ShowBackups()
    {
        bool canRestore = state != null && state.Writable && !state.Fresh && mouse.HasFactory;
        BackupMenu(backupsButton, BackupId, canRestore ? "Restore the factory profile" : null, () => Restore(null, "the factory profile"),
            entry =>
            {
                var data = entry.Get(state == null ? "" : state.Key);
                return data == null ? "another profile" : mouse.Describe(data);
            },
            entry => Usable(entry) != null, entry => Restore(Usable(entry), "the backup from " + When(entry)));
    }

    void Save()
    {
        var want = current.Clone();
        if (found.LightIsLiveOnly) want.Light = null;
        var deviceState = state;
        debounce.Stop();
        saveButton.Enabled = false;
        ShowStatus("Saving to the mouse…");
        LogUserAction("Save to mouse");
        RunOnDevice(hidpp =>
        {
            if (deviceState.Fresh) { mouse.Setup(hidpp, deviceState, want); return; }
            var now = mouse.ReadProfile(hidpp, deviceState);
            if (!Intact(deviceState, now)) throw new RefusedException("The profile on the mouse is damaged, not touching it");
            Backups.Save(BackupId, new Dictionary<string, byte[]> { { deviceState.Key, now } }, DeviceLabel);
            mouse.Save(hidpp, deviceState, now, want);
        }, () =>
        {
            lightSet = false;
            Note = "Saved to the mouse";
            Load();
        });
    }

    void Restore(byte[] data, string what)
    {
        var deviceState = state;
        if (deviceState == null || !deviceState.Writable || deviceState.Fresh) return;
        debounce.Stop();
        sequence++;
        saveButton.Enabled = false;
        ShowStatus("Restoring " + what + "…");
        LogUserAction("Restore " + what);
        RunOnDevice(hidpp =>
        {
            var now = mouse.ReadProfile(hidpp, deviceState);
            Backups.Save(BackupId, new Dictionary<string, byte[]> { { deviceState.Key, now } }, DeviceLabel);
            mouse.Restore(hidpp, deviceState, now, data);
        }, () =>
        {
            lightSet = false;
            Note = "Restored " + what;
            Load();
        });
    }

    void Send()
    {
        var want = current.Clone();
        var deviceState = state;
        int startedAt = ++sequence;
        RunOnDevice(hidpp =>
        {
            if (startedAt != sequence) return;
            mouse.SendDpi(hidpp, deviceState, want, sent);
            if (!deviceState.Onboard && want.Rate > 0 && want.Rate != deviceState.Rate && (sent == null || sent.Rate != want.Rate)
                && deviceState.Rates.Contains(want.Rate))
                mouse.SetRate(hidpp, want.Rate);
            if (want.Light != null && deviceState.Zones.Count > 0 && (sent == null || !want.Light.Same(sent.Light)))
            {
                mouse.SetLight(hidpp, deviceState, want.Light);
                lightSet = !found.LightIsLiveOnly && !want.Light.Same(deviceState.Light);
            }
            sent = want;
        }, () =>
        {
            if (startedAt != sequence) return;
            if (found.LightIsLiveOnly && want.Light != null) Keep(want.Light);
            ClearStatus();
        });
    }

    void ShowView(bool buttons)
    {
        showingButtons = buttons;
        buttonRows.Recording = -1;
        if (buttons) StartSpy(); else StopSpy();
        viewPick.Index = buttons ? 1 : 0;
        SyncButtons();
        PlaceView();
    }

    // Settings' sections with the rows this mouse has, or the Bindings view's buttons, a row each.
    void PlaceView()
    {
        buttonsHeading.Visible = buttonsBox.Visible = buttonRows.Hint.Visible = showingButtons;
        int height = layout.Place(Scaled, !showingButtons);
        if (showingButtons)
        {
            buttonsBox.Height = Scaled(ButtonCount * Ui.RowHeight);
            height = Rows.TitleGap + ButtonCount * Ui.RowHeight + Rows.SectionGap;
        }
        FitBody(height);
    }

    void SyncButtons()
    {
        int count = state == null || state.Buttons == null || current.Buttons == null
            ? 0
            : Math.Min(state.ButtonCount, current.Buttons.Length);
        buttonRows.Show(Bindings.Names(found, count), current.Buttons, state != null && state.Savable,
            spying ? "Press a button on the mouse to find it." : "");
    }

    protected override bool Keepable
    {
        get
        {
            if (state != null && state.ReadOnly) return false;
            return current.Buttons == null || Bindings.HasLeft(current.Buttons, ButtonCount)
                || stored == null || !Bindings.HasLeft(stored.Buttons, ButtonCount);
        }
    }

    int ButtonCount { get { return state == null || current.Buttons == null ? 0 : Math.Min(state.ButtonCount, current.Buttons.Length); } }

    void Assign(int row, byte[] binding)
    {
        current.Buttons[row] = binding;
        ButtonsEdited();
    }

    internal void Recorded(Keys keys) { buttonRows.Recorded(keys); }

    void ButtonsEdited()
    {
        if (state == null) return;
        dirty = true;
        Edited();
        RefreshControls();
        ClearStatus();
    }

    void StartSpy()
    {
        if (spying || found.Path == null || !found.Has(0x8110)) return;
        spying = true;
        byte feature = 0;
        RunOnDevice(hidpp => { feature = hidpp.FeatureIndex(0x8110); hidpp.Call(feature, 1); }, () =>
        {
            if (!spying || spy != null) return;
            try
            {
                spy = new ButtonSpy(found.Path, found.Index, feature, bits =>
                {
                    if (IsHandleCreated) BeginInvoke((Action)(() => Pressed(bits)));
                });
            }
            catch (IOException) { }
            SyncButtons();
        });
    }

    void StopSpy()
    {
        if (!spying) return;
        spying = false;
        if (spy != null) { spy.Dispose(); spy = null; }
        RunOnDevice(hidpp => { try { hidpp.Call(hidpp.FeatureIndex(0x8110), 2); } catch (HidppError) { } });
    }

    internal void Pressed(int bits)
    {
        if (bits == 0) return;
        buttonRows.Pressed = Enumerable.Range(0, 16).First(i => (bits >> i & 1) != 0);
        pressedTimer.Stop();
        pressedTimer.Start();
        SyncButtons();
    }

    public override void Shown()
    {
        // A mouse with nothing live (the G300) has its unsaved changes only here, so coming back to it keeps them.
        if (state != null && state.SaveOnly && dirty) ClearStatus();
        else base.Shown();
        if (showingButtons) StartSpy();
    }

    public override void Hidden()
    {
        StopSpy();
        base.Hidden();
    }

    string InvertKey { get { return "wheel.invert." + found.SettingsKey; } }

    void SetInvert(bool on)
    {
        if (state == null) return;
        Settings.Set(InvertKey, on ? "1" : "0");
        LogUserAction(on ? "Invert scrolling" : "Normal scrolling");
        RunOnDevice(hidpp => mouse.SetInvert(hidpp, on), () => { state.Inverted = on; RefreshControls(); });
    }

    internal void Watch()
    {
        if (Settings.Get(InvertKey) != "1") return;
        RunOnDevice(hidpp =>
        {
            try { mouse.SetInvert(hidpp, true); }
            catch (NotSupportedException) { }
            catch (HidppError) { }
            catch (TimeoutException) { }
            catch (RefusedException) { }
        });
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            watch.Dispose();
            pressedTimer.Dispose();
            if (spy != null) spy.Dispose();
        }
        base.Dispose(disposing);
    }

    // The mouse's own buttons: 0x8100 event fn 0 is a new active profile, fn 1 a new DPI stage (MyHUB, docs/research/more-references.md).
    public void Heard(byte feature, int fn)
    {
        if (state == null) return;
        if (found.LightIsLiveOnly && fn == 0 && feature != state.ProfilesFeature) { Reconnected(feature); return; }
        if (state.ProfilesFeature == 0 || feature != state.ProfilesFeature) return;
        if (fn == 0) { Load(); return; }
        if (fn != 1) return;
        int dpi = 0, stage = -1, startedAt = sequence;
        RunOnDevice(hidpp => MouseProtocol.ReadDpiAndStage(hidpp, state, out dpi, out stage), () =>
        {
            if (startedAt != sequence || dpi == 0) return;
            foreach (var set in new[] { current, sent, stored })
                if (set != null) { set.Dpi = dpi; set.Stage = stage; }
            RefreshControls();
        }, message => { });
    }

    // A wireless device sends 0x1D4B (wireless device status) event 0 when it reconnects, after a sleep, and wants its software
    // settings again; Solaar re-applies its settings on it. The index is looked up only then, so the mouse's read sends nothing more.
    void Reconnected(byte feature)
    {
        var kept = KeptLight();
        var deviceState = state;
        RunOnDevice(hidpp =>
        {
            try { if (hidpp.FeatureIndex(0x1D4B) != feature) return; }
            catch (NotSupportedException) { return; }
            Log.Add(DeviceLabel, "found", "woke up (wireless status event)");
            if (kept != null && deviceState.Zones.Count > 0) mouse.SetLight(hidpp, deviceState, kept);
        }, null, message => { });
    }

    string LightKey { get { return "lighting." + found.SettingsKey; } }

    // A G305 loses its live light when switched off, and a scan remakes its screen: the new one reads the mouse to send it again.
    protected override bool SendsAgain { get { return base.SendsAgain || (found.LightIsLiveOnly && KeptLight() != null); } }

    MouseLight KeptLight()
    {
        var text = Settings.Get(LightKey);
        if (text == "") return null;
        try { return MouseLight.FromText(text); }
        catch (FormatException) { return null; }
        catch (IndexOutOfRangeException) { return null; }
    }

    // A mouse that ignores the profile's lighting: G-Lite keeps it, and it's no change to save.
    void Keep(MouseLight light)
    {
        if (!light.Same(KeptLight())) Settings.Set(LightKey, light.ToText());
        if (stored == null) return;
        stored.Light = light.Clone();
        if (current.Dpi == stored.Dpi && current.Stage == stored.Stage && current.Rate == stored.Rate
            && Bindings.Same(current.Buttons, stored.Buttons))
            dirty = false;
        RefreshControls();
    }

    void SendRate(int hz)
    {
        RunOnDevice(hidpp => mouse.SetRate(hidpp, hz), () => Load());
    }

    public override void Load()
    {
        debounce.Stop();
        ShowStatus("Reading the mouse…");
        MouseProtocol.State read = null;
        int startedAt = sequence;
        RunOnDevice(hidpp => read = mouse.Read(hidpp), () =>
        {
            state = read;
            fix = read.ProfileOk ? null : Backups.List(BackupId).FirstOrDefault(entry => Usable(entry) != null);
            int start = read.Stage >= 0 ? read.Stage
                : read.Profile != null && read.Stages.Length > 0 ? Math.Min(mouse.DefaultStage(read.Profile), read.Stages.Length - 1)
                : -1;
            stored = new MouseSettings
            {
                Dpi = read.Dpi, Rate = read.Rate, Light = read.Light, Stage = start,
                Buttons = Bindings.Copy(read.Buttons)
            };
            if (startedAt == sequence)
            {
                current.Dpi = read.Dpi;
                current.Rate = read.Rate;
                current.Stage = start;
                current.Buttons = Bindings.Copy(read.Buttons);
                buttonRows.Recording = -1;
                if (!lightSet || current.Light == null) current.Light = read.Light == null ? null : read.Light.Clone();
                if (!lightSet) dirty = false;
                var kept = found.LightIsLiveOnly && read.Zones.Count > 0 ? KeptLight() : null;
                if (kept != null)
                {
                    current.Light = kept;
                    stored.Light = kept.Clone();
                }
                sent = new MouseSettings
                {
                    Dpi = read.Dpi, Stage = start,
                    Light = lightSet && current.Light != null ? current.Light.Clone() : read.Light
                };
                if (kept != null) Send();
            }
            RefreshControls();
            Loaded();
        });
    }
}
