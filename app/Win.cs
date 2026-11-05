using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

static class Win
{
    public const int DpiChanged = 0x02E0;
    public static bool Rounded;

    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int value, int size);
    [DllImport("user32.dll")] static extern bool SetProcessDpiAwarenessContext(IntPtr context);
    [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] static extern IntPtr MonitorFromPoint(Point p, int flags);
    [DllImport("shcore.dll")] static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint x, out uint y);

    public static void Aware()
    {
        try { if (SetProcessDpiAwarenessContext((IntPtr)(-4))) return; } catch (EntryPointNotFoundException) { }
        try { SetProcessDPIAware(); } catch (EntryPointNotFoundException) { }
    }

    public static float At(Point p)
    {
        try
        {
            uint x, y;
            if (GetDpiForMonitor(MonitorFromPoint(p, 2), 0, out x, out y) == 0) return x / 96f;
        }
        catch (DllNotFoundException) { }
        catch (EntryPointNotFoundException) { }
        using (var g = Graphics.FromHwnd(IntPtr.Zero)) return g.DpiX / 96f;
    }

    public static float FromMessage(Message m) { return (m.WParam.ToInt64() & 0xFFFF) / 96f; }

    public static Font Px(float points, float scale, string face = "Segoe UI")
    {
        return new Font(face, points * 96f / 72f * scale, FontStyle.Regular, GraphicsUnit.Pixel);
    }

    public static void Round(Form f)
    {
        f.HandleCreated += (s, e) =>
        {
            int round = 2, border = 0x3A3A3A;
            try { Rounded = DwmSetWindowAttribute(f.Handle, 33, ref round, 4) == 0 && DwmSetWindowAttribute(f.Handle, 34, ref border, 4) == 0; }
            catch (DllNotFoundException) { }
        };
    }
}
