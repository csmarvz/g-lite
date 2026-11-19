using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;

static class Logo
{
    public static readonly int[] Sizes = { 16, 20, 24, 32, 40, 48, 64, 256 };

    public static Bitmap Draw(int size)
    {
        float scale = size / 100f;
        var bitmap = new Bitmap(size, size);
        using (var disc = new Bitmap(size, size))
        using (var letters = new Bitmap(size, size))
        {
            using (var graphics = Graphics.FromImage(disc))
            using (var white = new SolidBrush(Color.White))
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.FillEllipse(white, 2 * scale, 2 * scale, 96 * scale, 96 * scale);
            }
            using (var graphics = Graphics.FromImage(letters))
            using (var matrix = new Matrix())
            using (var pen = new Pen(Color.White, size <= 16 ? 10.5f : 9) { LineJoin = LineJoin.Miter })
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                matrix.Scale(scale, scale);
                matrix.Translate(50, 50);
                matrix.Scale(0.9f, 0.9f);
                matrix.Translate(-50, -50);
                matrix.Translate(14.8f, 20);
                matrix.Multiply(new Matrix(1, 0, -0.2493f, 1, 0, 0));
                graphics.Transform = matrix;
                graphics.DrawLines(pen, new[]
                {
                    new PointF(46, 10), new PointF(14, 10), new PointF(14, 50), new PointF(46, 50), new PointF(46, 30), new PointF(32, 30)
                });
                graphics.DrawLines(pen, new[] { new PointF(60, 10), new PointF(60, 50), new PointF(88, 50) });
            }
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    bitmap.SetPixel(x, y, Color.FromArgb(disc.GetPixel(x, y).A * (255 - letters.GetPixel(x, y).A) / 255, 255, 255, 255));
        }
        return bitmap;
    }

    public static Icon MakeIcon(int size)
    {
        using (var bitmap = Draw(size)) return Icon.FromHandle(bitmap.GetHicon());
    }

    public static Bitmap Slanted(int size, Color color)
    {
        int height = (int)Math.Round(size * 0.54f);
        int stroke = Math.Max(2, (int)Math.Round(height * 0.21f));
        if ((height - stroke) % 2 != 0) height++;
        int gWidth = (int)Math.Round(height * 0.84f), lWidth = (int)Math.Round(height * 0.75f),
            gap = Math.Max(1, (int)Math.Round(stroke * 0.33f));
        int slant = (int)Math.Ceiling(height * 0.25f);
        while (gWidth + gap + lWidth + slant > size) { if (lWidth >= gWidth) lWidth--; else gWidth--; }
        int left = (size - (gWidth + gap + lWidth + slant)) / 2, top = (size - height) / 2;
        int middle = (height - stroke) / 2, barStart = (int)Math.Round(gWidth * 0.55f), lLeft = gWidth + gap;
        var bitmap = new Bitmap(size, size);
        using (var graphics = Graphics.FromImage(bitmap))
        using (var path = new GraphicsPath(FillMode.Winding))
        using (var brush = new SolidBrush(color))
        using (var shear = new Matrix(1, 0, -0.25f, 1, left + 0.25f * (top + height), top))
        {
            path.AddRectangle(new RectangleF(0, 0, gWidth, stroke));
            path.AddRectangle(new RectangleF(0, height - stroke, gWidth, stroke));
            path.AddRectangle(new RectangleF(0, 0, stroke, height));
            path.AddRectangle(new RectangleF(gWidth - stroke, middle, stroke, height - middle));
            path.AddRectangle(new RectangleF(barStart, middle, gWidth - barStart, stroke));
            path.AddRectangle(new RectangleF(lLeft, 0, stroke, height));
            path.AddRectangle(new RectangleF(lLeft, height - stroke, lWidth, stroke));
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.Transform = shear;
            graphics.FillPath(brush, path);
        }
        return bitmap;
    }

    public static Icon MakeTray(int size, bool light)
    {
        using (var bitmap = Slanted(size, light ? Color.FromArgb(28, 28, 28) : Color.White)) return Icon.FromHandle(bitmap.GetHicon());
    }

    public static void WriteIco(string path)
    {
        var images = Sizes.Select(size =>
        {
            using (var bitmap = Draw(size))
            using (var stream = new MemoryStream())
            {
                bitmap.Save(stream, ImageFormat.Png);
                return stream.ToArray();
            }
        }).ToArray();
        using (var writer = new BinaryWriter(File.Create(path)))
        {
            writer.Write((short)0);
            writer.Write((short)1);
            writer.Write((short)Sizes.Length);
            int offset = 6 + 16 * Sizes.Length;
            for (int i = 0; i < Sizes.Length; i++)
            {
                byte side = (byte)(Sizes[i] >= 256 ? 0 : Sizes[i]);
                writer.Write(side);
                writer.Write(side);
                writer.Write((byte)0);
                writer.Write((byte)0);
                writer.Write((short)1);
                writer.Write((short)32);
                writer.Write(images[i].Length);
                writer.Write(offset);
                offset += images[i].Length;
            }
            foreach (var image in images) writer.Write(image);
        }
    }
}
