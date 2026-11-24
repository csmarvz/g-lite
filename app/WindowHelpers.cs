using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

static class WindowHelpers
{
    public const int DpiChanged = 0x02E0;
    public static bool Rounded;

    struct MemoryCounters
    {
        public int Size, PageFaults;
        public IntPtr PeakWorkingSet, WorkingSet, PeakPagedPool, PagedPool, PeakNonPagedPool, NonPagedPool,
            Pagefile, PeakPagefile, Private, PrivateWorkingSet;
        public long SharedCommit;
    }
    [DllImport("psapi.dll")] static extern bool GetProcessMemoryInfo(IntPtr process, ref MemoryCounters counters, int size);
    [DllImport("psapi.dll")] static extern bool EmptyWorkingSet(IntPtr process);
    [DllImport("kernel32.dll")] static extern IntPtr GetCurrentProcess();

    struct LastInput { public int Size, Time; }
    [DllImport("user32.dll")] static extern bool GetLastInputInfo(ref LastInput info);

    public static int TestIdle = -1;

    public static int IdleSeconds()
    {
        if (TestIdle >= 0) return TestIdle;
        var info = new LastInput { Size = 8 };
        if (!GetLastInputInfo(ref info)) return 0;
        return (int)(((uint)Environment.TickCount - (uint)info.Time) / 1000);
    }

    public static long PrivateMemory()
    {
        var counters = new MemoryCounters { Size = Marshal.SizeOf(typeof(MemoryCounters)) };
        if (GetProcessMemoryInfo(GetCurrentProcess(), ref counters, counters.Size) && counters.PrivateWorkingSet != IntPtr.Zero)
            return counters.PrivateWorkingSet.ToInt64();
        return System.Diagnostics.Process.GetCurrentProcess().WorkingSet64;
    }

    public static void Trim()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        EmptyWorkingSet(GetCurrentProcess());
    }

    [DllImport("user32.dll")] static extern bool SystemParametersInfo(int action, int param, ref bool value, int ini);

    public static bool Animations
    {
        get
        {
            bool on = true;
            try { SystemParametersInfo(0x1042, 0, ref on, 0); } catch (EntryPointNotFoundException) { }
            return on;
        }
    }

    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
    [DllImport("user32.dll")] static extern bool SetProcessDpiAwarenessContext(IntPtr context);
    [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] static extern IntPtr MonitorFromPoint(Point point, int flags);
    [DllImport("shcore.dll")] static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint dpiX, out uint dpiY);

    public static void EnableDpiAwareness()
    {
        try { if (SetProcessDpiAwarenessContext((IntPtr)(-4))) return; } catch (EntryPointNotFoundException) { }
        try { SetProcessDPIAware(); } catch (EntryPointNotFoundException) { }
    }

    public static float At(Point point)
    {
        try
        {
            uint dpiX, dpiY;
            if (GetDpiForMonitor(MonitorFromPoint(point, 2), 0, out dpiX, out dpiY) == 0) return dpiX / 96f;
        }
        catch (DllNotFoundException) { }
        catch (EntryPointNotFoundException) { }
        using (var graphics = Graphics.FromHwnd(IntPtr.Zero)) return graphics.DpiX / 96f;
    }

    public static float FromMessage(Message message) { return (message.WParam.ToInt64() & 0xFFFF) / 96f; }

    public static Font PixelFont(float points, float scale, string face = "Segoe UI")
    {
        return new Font(face, points * 96f / 72f * scale, FontStyle.Regular, GraphicsUnit.Pixel);
    }

    [DllImport("user32.dll")] static extern bool RedrawWindow(IntPtr window, IntPtr area, IntPtr region, int flags);

    // Shows a window once it's painted: cloaked (DWMWA_CLOAK), shown, every control painted at once (RDW_INVALIDATE, RDW_ERASE,
    // RDW_ALLCHILDREN, RDW_UPDATENOW), then uncloaked. Shown straight from the tray, its controls flash white while they paint,
    // slower since Trim handed their memory back.
    public static void ShowPainted(Form form)
    {
        int on = 1, off = 0;
        bool cloaked = false;
        try { cloaked = DwmSetWindowAttribute(form.Handle, 13, ref on, 4) == 0; } catch (DllNotFoundException) { }
        form.Show();
        RedrawWindow(form.Handle, IntPtr.Zero, IntPtr.Zero, 0x1 | 0x4 | 0x80 | 0x100);
        if (cloaked) DwmSetWindowAttribute(form.Handle, 13, ref off, 4);
    }

    // Paints what's waiting to be painted in a control and everything in it (RDW_UPDATENOW, RDW_ALLCHILDREN) now, not once the input
    // queue is empty.
    public static void PaintPending(Control control)
    {
        RedrawWindow(control.Handle, IntPtr.Zero, IntPtr.Zero, 0x80 | 0x100);
    }

    public static void Border(Form form, Color color)
    {
        if (!form.IsHandleCreated) return;
        int colorRef = color.R | color.G << 8 | color.B << 16;
        try { DwmSetWindowAttribute(form.Handle, 34, ref colorRef, 4); } catch (DllNotFoundException) { }
    }

    struct ThemeOptions { public int Flags, Mask; }
    [DllImport("uxtheme.dll")] static extern int SetWindowThemeAttribute(IntPtr window, int attribute, ref ThemeOptions options, int size);

    // DWMWA_USE_IMMERSIVE_DARK_MODE is 20, or 19 before Windows 10 20H1; the caption and text colours (35, 36) need Windows 11.
    // WTNCA_NODRAWCAPTION and WTNCA_NODRAWICON leave the title and icon out of the title bar only: the taskbar and Alt+Tab keep
    // them (ShowIcon = false can lose the taskbar's icon too).
    public static void TitleBar(Form form, Color caption, Color text, bool dark)
    {
        if (!form.IsHandleCreated) return;
        var empty = new ThemeOptions { Flags = 3, Mask = 3 };
        try { SetWindowThemeAttribute(form.Handle, 1, ref empty, 8); } catch (DllNotFoundException) { }
        int on = dark ? 1 : 0, captionColor = caption.R | caption.G << 8 | caption.B << 16, textColor = text.R | text.G << 8 | text.B << 16;
        try
        {
            if (DwmSetWindowAttribute(form.Handle, 20, ref on, 4) != 0) DwmSetWindowAttribute(form.Handle, 19, ref on, 4);
            DwmSetWindowAttribute(form.Handle, 35, ref captionColor, 4);
            DwmSetWindowAttribute(form.Handle, 36, ref textColor, 4);
        }
        catch (DllNotFoundException) { }
    }

    public static bool Corners(IntPtr handle, Color border)
    {
        int round = 2, color = border.R | border.G << 8 | border.B << 16;
        try { return DwmSetWindowAttribute(handle, 33, ref round, 4) == 0 && DwmSetWindowAttribute(handle, 34, ref color, 4) == 0; }
        catch (DllNotFoundException) { return false; }
    }

    // corners: 2 rounds like a window, 3 like a tooltip or menu (DWMWCP_ROUNDSMALL).
    public static void Round(Form form, int corners = 2)
    {
        form.HandleCreated += (s, e) =>
        {
            int round = corners, border = 0x3A3A3A;
            try
            {
                Rounded = DwmSetWindowAttribute(form.Handle, 33, ref round, 4) == 0
                    && DwmSetWindowAttribute(form.Handle, 34, ref border, 4) == 0;
            }
            catch (DllNotFoundException) { }
        };
    }
}
