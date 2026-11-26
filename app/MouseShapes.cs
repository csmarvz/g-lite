using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Text.RegularExpressions;

// The mouse pictures' shapes, one class each: the outline, the buttons, the wheel and where the light is. MousePicture draws the light
// in its colour, its glow and the text. Points are across (0 left, 1 right) and down (0 front, 1 rear) the body's box.
abstract class MouseShape
{
    // Width over length.
    public abstract float Aspect { get; }
    // The light's centre, and its radius as a share of the width.
    public abstract PointF LightAt { get; }
    public abstract float LightRadius { get; }
    // How far the glow reaches, in radiuses.
    public virtual float GlowReach { get { return 2.4f; } }
    // The light's colour while it's off.
    public virtual Color Unlit { get { return Theme.Border; } }

    public abstract GraphicsPath Outline(RectangleF box);
    public abstract void Draw(Graphics graphics, RectangleF body);

    // The light in its colour: G-Lite's logo on most.
    public virtual void DrawLight(Graphics graphics, RectangleF body, RectangleF light, Color fill)
    {
        Logo.Paint(graphics, light, fill, Theme.KeyOff);
    }

    public static MouseShape For(string name)
    {
        string upper = name.ToUpperInvariant();
        if (upper.Contains("G502")) return new G502Shape();
        if (upper.Contains("G305") || upper.Contains("G304")) return new G305Shape();
        if (Regex.IsMatch(upper, @"\bDEX\b")) return new DexShape();
        if (Regex.IsMatch(upper, @"\bPRO\b")) return new ProShape();
        return new PlainShape();
    }

    protected static float Scale { get { return Ui.DpiScale; } }

    protected static Func<float, float, PointF> Points(RectangleF box)
    {
        return (across, down) => new PointF(box.X + box.Width * across, box.Y + box.Height * down);
    }

    // The body filled, the lines drawn inside it (clipped to it), then its edge.
    protected void Body(Graphics graphics, RectangleF body, Action<Pen> lines)
    {
        using (var path = Outline(body))
        {
            using (var brush = new SolidBrush(Theme.KeyOff)) graphics.FillPath(brush, path);
            graphics.SetClip(path);
            using (var pen = new Pen(Theme.PictureBackground, 1.5f * Scale) { LineJoin = LineJoin.Round }) lines(pen);
            graphics.ResetClip();
            using (var pen = new Pen(Theme.Border)) graphics.DrawPath(pen, path);
        }
    }

    // A wheel in its slot, ridged.
    protected static void Wheel(Graphics graphics, RectangleF wheel, float radius)
    {
        Ui.Fill(graphics, wheel, radius, Theme.Border, Theme.Border);
        using (var pen = new Pen(Theme.PictureBackground, 1 * Scale))
            for (int ridge = 1; ridge < 8; ridge++)
                graphics.DrawLine(pen, wheel.X + 1 * Scale, wheel.Y + wheel.Height * ridge / 8, wheel.Right - 1 * Scale, wheel.Y + wheel.Height * ridge / 8);
    }
}

// A mouse without its own shape: rounded, the buttons split down the middle, a slot for the wheel, a round light on the palm.
class PlainShape : MouseShape
{
    public override float Aspect { get { return 0.6f; } }
    public override PointF LightAt { get { return new PointF(0.5f, 0.68f); } }
    public override float LightRadius { get { return 0.17f; } }
    public override Color Unlit { get { return Theme.PictureBackground; } }

    public override GraphicsPath Outline(RectangleF box) { return Ui.Round(box, box.Width / 2); }

    public override void Draw(Graphics graphics, RectangleF body)
    {
        float split = body.Y + body.Height * 0.4f;
        Body(graphics, body, pen =>
        {
            graphics.DrawLine(pen, body.X + body.Width / 2, body.Y, body.X + body.Width / 2, split);
            graphics.DrawLine(pen, body.X, split, body.Right, split);
        });
        Ui.Fill(graphics, new RectangleF(body.X + body.Width / 2 - 4 * Scale, body.Y + body.Height * 0.12f, 8 * Scale, body.Height * 0.17f),
            4 * Scale, Theme.PictureBackground, Theme.PictureBackground);
    }

    public override void DrawLight(Graphics graphics, RectangleF body, RectangleF light, Color fill)
    {
        using (var brush = new SolidBrush(fill)) graphics.FillEllipse(brush, light);
    }
}

// The PRO family (the G Pro Wireless, the PRO X SUPERLIGHTs), from a photo: a dome at each end, nearly straight sides, the buttons in
// grooves set in from the sides with rounded rear corners, the wheel in its slot, side buttons on both sides, the DPI dots and the
// light on the palm.
class ProShape : MouseShape
{
    public override float Aspect { get { return 0.5f; } }
    public override PointF LightAt { get { return new PointF(0.5f, 0.75f); } }
    public override float LightRadius { get { return 0.12f; } }

    // Where the buttons end; the grooves' notch near the front and where they run (left and right, at the notch and at the end).
    protected virtual float Split { get { return 0.45f; } }
    protected virtual float Notch { get { return 0.1f; } }
    protected virtual float[] Grooves { get { return new[] { 0.07f, 0.1f, 0.93f, 0.9f }; } }
    protected virtual RectangleF Slot { get { return new RectangleF(0.415f, 0.11f, 0.17f, 0.17f); } }

    public override GraphicsPath Outline(RectangleF box)
    {
        var at = Points(box);
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

    // Both sides: the PRO family is ambidextrous.
    protected virtual void SideButtons(Graphics graphics, RectangleF body)
    {
        foreach (var side in new[] { 0f, 1f })
            foreach (var buttonTop in new[] { 0.44f, 0.53f })
                Ui.Fill(graphics, new RectangleF(body.X + side * body.Width - 2.5f * Scale, body.Y + body.Height * buttonTop, 5 * Scale,
                    body.Height * 0.085f), 2 * Scale, Theme.KeyOff, Theme.Border);
    }

    public override void Draw(Graphics graphics, RectangleF body)
    {
        float x = body.X, y = body.Y, width = body.Width, height = body.Height;
        SideButtons(graphics, body);
        Body(graphics, body, pen =>
        {
            float bottom = y + height * Split, notch = y + height * Notch;
            graphics.DrawLine(pen, x + width / 2, y, x + width / 2, bottom);
            // The buttons' outer grooves, from the notch near the front to where they end, their rear corners rounded as on the mouse.
            float corner = width * 0.06f, left = x + width * Grooves[1], right = x + width * Grooves[3];
            using (var grooves = new GraphicsPath())
            {
                grooves.AddLine(x, notch, x + width * Grooves[0], notch);
                grooves.AddLine(x + width * Grooves[0], notch, left, bottom - corner);
                grooves.AddBezier(left, bottom - corner, left, bottom - corner * 0.45f, left + corner * 0.45f, bottom, left + corner, bottom);
                grooves.AddLine(left + corner, bottom, right - corner, bottom);
                grooves.AddBezier(right - corner, bottom, right - corner * 0.45f, bottom, right, bottom - corner * 0.45f, right, bottom - corner);
                grooves.AddLine(right, bottom - corner, x + width * Grooves[2], notch);
                grooves.AddLine(x + width * Grooves[2], notch, x + width, notch);
                graphics.DrawPath(pen, grooves);
            }
        });
        var slot = new RectangleF(x + width * Slot.X, y + height * Slot.Y, width * Slot.Width, height * Slot.Height);
        Ui.Fill(graphics, slot, slot.Width * 0.35f, Theme.PictureBackground, Theme.PictureBackground);
        var wheel = new RectangleF(slot.X + slot.Width * 0.25f, slot.Y + slot.Height * 0.08f, slot.Width * 0.5f, slot.Height * 0.84f);
        Wheel(graphics, wheel, wheel.Width / 2);
        DpiDots(graphics, body);
    }

    protected virtual void DpiDots(Graphics graphics, RectangleF body)
    {
        using (var brush = new SolidBrush(Theme.Border))
            for (int dot = 0; dot < 3; dot++)
                graphics.FillEllipse(brush, body.X + body.Width / 2 - 1.5f * Scale, body.Y + body.Height * (0.485f + dot * 0.03f), 3 * Scale, 3 * Scale);
    }
}

// The PRO X 2 DEX, right-handed, from its photo: the PRO's buttons and wheel, narrower at the front than at the rear, a slight waist
// on the left below its two side buttons, no DPI dots.
class DexShape : ProShape
{
    public override float Aspect { get { return 0.54f; } }
    public override PointF LightAt { get { return new PointF(0.5f, 0.74f); } }
    public override float LightRadius { get { return 0.1f; } }
    protected override float Split { get { return 0.46f; } }
    protected override float Notch { get { return 0.12f; } }
    protected override float[] Grooves { get { return new[] { 0.12f, 0.15f, 0.91f, 0.89f }; } }
    protected override RectangleF Slot { get { return new RectangleF(0.43f, 0.135f, 0.15f, 0.165f); } }

    public override GraphicsPath Outline(RectangleF box)
    {
        var at = Points(box);
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

    protected override void SideButtons(Graphics graphics, RectangleF body)
    {
        foreach (var buttonTop in new[] { 0.28f, 0.45f })
            Ui.Fill(graphics, new RectangleF(body.X + body.Width * 0.05f - 2.5f * Scale, body.Y + body.Height * buttonTop, 5 * Scale,
                body.Height * 0.16f), 2 * Scale, Theme.KeyOff, Theme.Border);
    }

    protected override void DpiDots(Graphics graphics, RectangleF body) { }
}

// The G502 family, traced from a schematic of the G502 LIGHTSPEED: the buttons as blades either side of a strip with the wheel, the
// mode button and G9, G7 and G8 on the left, the thumb rest bulging at the left rear, the palm with the logo and the DPI lights, both
// lit in the mouse's colour.
class G502Shape : MouseShape
{
    public override float Aspect { get { return 0.57f; } }
    public override PointF LightAt { get { return new PointF(0.4f, 0.686f); } }
    public override float LightRadius { get { return 0.09f; } }

    // The two button blades at the front with the strip's notch between them, the right side, the rear, and the left side's thumb
    // rest and side buttons back up to the left blade.
    public override GraphicsPath Outline(RectangleF box)
    {
        var at = Points(box);
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

    public override void Draw(Graphics graphics, RectangleF body)
    {
        var at = Points(body);
        Body(graphics, body, pen =>
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
        });
        // The wheel in its slot, then the mode button and G9 below it; the arrows either side for its tilt.
        var slot = new RectangleF(at(0.505f, 0.16f), new SizeF(body.Width * 0.135f, body.Height * 0.175f));
        Ui.Fill(graphics, slot, slot.Width * 0.3f, Theme.PictureBackground, Theme.PictureBackground);
        var wheel = new RectangleF(slot.X + slot.Width * 0.22f, slot.Y + slot.Height * 0.08f, slot.Width * 0.56f, slot.Height * 0.84f);
        Wheel(graphics, wheel, wheel.Width * 0.3f);
        foreach (var button in new[] { new RectangleF(at(0.515f, 0.357f), new SizeF(body.Width * 0.125f, body.Height * 0.066f)),
            new RectangleF(at(0.52f, 0.435f), new SizeF(body.Width * 0.12f, body.Height * 0.078f)) })
            Ui.Fill(graphics, button, 2 * Scale, Theme.KeyOff, Theme.PictureBackground);
        using (var brush = new SolidBrush(Theme.Border))
        {
            graphics.FillPolygon(brush, new[] { at(0.405f, 0.25f), at(0.44f, 0.237f), at(0.44f, 0.263f) });
            graphics.FillPolygon(brush, new[] { at(0.73f, 0.25f), at(0.695f, 0.237f), at(0.695f, 0.263f) });
        }
    }

    // The DPI lights by the side buttons, then the logo.
    public override void DrawLight(Graphics graphics, RectangleF body, RectangleF light, Color fill)
    {
        var at = Points(body);
        using (var pen = new Pen(fill, 1.5f * Scale) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            for (int dpiLight = 0; dpiLight < 3; dpiLight++)
                graphics.DrawLine(pen, at(0.183f, 0.459f + dpiLight * 0.036f), at(0.168f, 0.483f + dpiLight * 0.036f));
        base.DrawLight(graphics, body, light, fill);
    }
}

// The G305 (and G304), traced from its top view (62.1 by 116.6 mm): the buttons' front edges straight down from the strip to sharp
// corners, the sides splaying out straight to the buttons' rear edges, each with a second line inside it, then the palm, an egg
// narrowing to the rear. The dark strip between the buttons holds the wheel, the DPI button and the LED under it, its one light
// (docs/G305.md); two side buttons on the left, two small rings and the logo, unlit, on the palm.
class G305Shape : MouseShape
{
    public override float Aspect { get { return 0.54f; } }
    public override PointF LightAt { get { return new PointF(0.503f, 0.436f); } }
    public override float LightRadius { get { return 0.025f; } }
    // The LED is small: its glow reaches further for its size.
    public override float GlowReach { get { return 5; } }

    // Symmetric, clockwise from the front's middle, the strip within it.
    public override GraphicsPath Outline(RectangleF box)
    {
        var at = Points(box);
        var path = new GraphicsPath();
        path.AddBezier(at(0.5f, 0.004f), at(0.535f, 0.005f), at(0.56f, 0.008f), at(0.575f, 0.012f));
        path.AddBezier(at(0.575f, 0.012f), at(0.7f, 0.028f), at(0.83f, 0.058f), at(0.895f, 0.083f));
        path.AddBezier(at(0.895f, 0.083f), at(0.905f, 0.088f), at(0.91f, 0.095f), at(0.912f, 0.105f));
        path.AddLine(at(0.912f, 0.105f), at(0.998f, 0.48f));
        path.AddBezier(at(0.998f, 0.48f), at(1.01f, 0.82f), at(0.78f, 1), at(0.5f, 1));
        path.AddBezier(at(0.5f, 1), at(0.22f, 1), at(-0.01f, 0.82f), at(0.002f, 0.48f));
        path.AddLine(at(0.002f, 0.48f), at(0.088f, 0.105f));
        path.AddBezier(at(0.088f, 0.105f), at(0.09f, 0.095f), at(0.095f, 0.088f), at(0.105f, 0.083f));
        path.AddBezier(at(0.105f, 0.083f), at(0.17f, 0.058f), at(0.3f, 0.028f), at(0.43f, 0.012f));
        path.AddBezier(at(0.43f, 0.012f), at(0.445f, 0.008f), at(0.47f, 0.005f), at(0.5f, 0.004f));
        path.CloseFigure();
        return path;
    }

    public override void Draw(Graphics graphics, RectangleF body)
    {
        var at = Points(body);
        using (var outline = Outline(body))
        {
            outline.Flatten();
            var points = outline.PathPoints;
            // The side buttons along the left edge, half out of it.
            foreach (var button in new[] { new[] { 0.338f, 0.475f }, new[] { 0.496f, 0.64f } })
            {
                // Each end shortened by the pen's round cap, so the gap between the two shows.
                float top = body.Y + body.Height * button[0] + 3.5f * Scale, bottom = body.Y + body.Height * button[1] - 3.5f * Scale;
                var edge = Enumerable.Range(0, 9).Select(step => top + (bottom - top) * step / 8)
                    .Select(y => new PointF(LeftEdge(points, y) - 1.5f * Scale, y)).ToArray();
                using (var border = new Pen(Theme.Border, 7 * Scale) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                    graphics.DrawLines(border, edge);
                using (var fill = new Pen(Theme.KeyOff, 5 * Scale) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                    graphics.DrawLines(fill, edge);
            }
        }
        Body(graphics, body, pen =>
        {
            // The buttons' rear edges, from the sides up to the strip, and the second line inside each side, which ends at them.
            graphics.DrawLine(pen, at(0, 0.482f), at(0.43f, 0.403f));
            graphics.DrawLine(pen, at(0.575f, 0.403f), at(1, 0.482f));
            graphics.DrawLine(pen, at(0.126f, 0.1f), at(0.06f, 0.471f));
            graphics.DrawLine(pen, at(0.874f, 0.1f), at(0.94f, 0.471f));
        });
        // The strip from the front to below the LED, within the outline, the wheel in it and the DPI button.
        var strip = new RectangleF(at(0.43f, -0.05f), new SizeF(body.Width * 0.145f, body.Height * 0.532f));
        using (var outline = Outline(body))
        {
            graphics.SetClip(outline);
            Ui.Fill(graphics, strip, strip.Width * 0.45f, Theme.PictureBackground, Theme.PictureBackground);
            graphics.ResetClip();
        }
        Wheel(graphics, new RectangleF(at(0.447f, 0.122f), new SizeF(body.Width * 0.113f, body.Height * 0.187f)), body.Width * 0.03f);
        Ui.Fill(graphics, new RectangleF(at(0.455f, 0.338f), new SizeF(body.Width * 0.092f, body.Height * 0.072f)), body.Width * 0.04f,
            Theme.KeyOff, Theme.Border);
        // Two small rings behind the strip, then the logo, not lit.
        using (var pen = new Pen(Theme.Border, 1 * Scale))
            foreach (var down in new[] { 0.532f, 0.561f })
                graphics.DrawEllipse(pen, at(0.503f, down).X - 2 * Scale, at(0.503f, down).Y - 2 * Scale, 4 * Scale, 4 * Scale);
        float logo = body.Width * 0.105f;
        var palm = at(0.503f, 0.791f);
        Logo.Paint(graphics, new RectangleF(palm.X - logo, palm.Y - logo, 2 * logo, 2 * logo), Theme.Border, Theme.KeyOff);
    }

    // The outline's leftmost x at a height, from its flattened points.
    static float LeftEdge(PointF[] points, float y)
    {
        float left = float.MaxValue;
        for (int i = 0; i < points.Length; i++)
        {
            PointF from = points[i], to = points[(i + 1) % points.Length];
            if ((y < from.Y && y < to.Y) || (y > from.Y && y > to.Y)) continue;
            float x = Math.Abs(to.Y - from.Y) < 0.001f ? Math.Min(from.X, to.X) : from.X + (to.X - from.X) * (y - from.Y) / (to.Y - from.Y);
            left = Math.Min(left, x);
        }
        return left;
    }

    public override void DrawLight(Graphics graphics, RectangleF body, RectangleF light, Color fill)
    {
        using (var brush = new SolidBrush(fill)) graphics.FillEllipse(brush, light);
    }
}
