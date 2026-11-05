using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;

static class Logo
{
    public static readonly int[] Sizes = { 16, 20, 24, 32, 40, 48, 64, 256 };

    public static Bitmap Draw(int n)
    {
        float k = n / 100f;
        var bmp = new Bitmap(n, n);
        using (var disc = new Bitmap(n, n))
        using (var gl = new Bitmap(n, n))
        {
            using (var g = Graphics.FromImage(disc))
            using (var white = new SolidBrush(Color.White))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.FillEllipse(white, 2 * k, 2 * k, 96 * k, 96 * k);
            }
            using (var g = Graphics.FromImage(gl))
            using (var m = new Matrix())
            using (var pen = new Pen(Color.White, n <= 16 ? 10.5f : 9) { LineJoin = LineJoin.Miter })
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                m.Scale(k, k);
                m.Translate(50, 50);
                m.Scale(0.9f, 0.9f);
                m.Translate(-50, -50);
                m.Translate(14.8f, 20);
                m.Multiply(new Matrix(1, 0, -0.2493f, 1, 0, 0));
                g.Transform = m;
                g.DrawLines(pen, new[] { new PointF(46, 10), new PointF(14, 10), new PointF(14, 50), new PointF(46, 50), new PointF(46, 30), new PointF(32, 30) });
                g.DrawLines(pen, new[] { new PointF(60, 10), new PointF(60, 50), new PointF(88, 50) });
            }
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                    bmp.SetPixel(x, y, Color.FromArgb(disc.GetPixel(x, y).A * (255 - gl.GetPixel(x, y).A) / 255, 255, 255, 255));
        }
        return bmp;
    }

    public static Icon Make(int n)
    {
        using (var b = Draw(n)) return Icon.FromHandle(b.GetHicon());
    }

    public static void WriteIco(string path)
    {
        var images = Sizes.Select(n =>
        {
            using (var b = Draw(n))
            using (var ms = new MemoryStream())
            {
                b.Save(ms, ImageFormat.Png);
                return ms.ToArray();
            }
        }).ToArray();
        using (var w = new BinaryWriter(File.Create(path)))
        {
            w.Write((short)0);
            w.Write((short)1);
            w.Write((short)Sizes.Length);
            int offset = 6 + 16 * Sizes.Length;
            for (int i = 0; i < Sizes.Length; i++)
            {
                byte side = (byte)(Sizes[i] >= 256 ? 0 : Sizes[i]);
                w.Write(side);
                w.Write(side);
                w.Write((byte)0);
                w.Write((byte)0);
                w.Write((short)1);
                w.Write((short)32);
                w.Write(images[i].Length);
                w.Write(offset);
                offset += images[i].Length;
            }
            foreach (var img in images) w.Write(img);
        }
    }
}
