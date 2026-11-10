using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

static class Win
{
    public const int DpiChanged = 0x02E0;
    public static bool Rounded;

    struct MemoryCounters { public int Size, PageFaults; public IntPtr PeakWorkingSet, WorkingSet, PeakPagedPool, PagedPool, PeakNonPagedPool, NonPagedPool, Pagefile, PeakPagefile, Private, PrivateWorkingSet; public long SharedCommit; }
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
        if (GetProcessMemoryInfo(GetCurrentProcess(), ref counters, counters.Size) && counters.PrivateWorkingSet != IntPtr.Zero) return counters.PrivateWorkingSet.ToInt64();
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

    public static void Border(Form f, Color c)
    {
        if (!f.IsHandleCreated) return;
        int v = c.R | c.G << 8 | c.B << 16;
        try { DwmSetWindowAttribute(f.Handle, 34, ref v, 4); } catch (DllNotFoundException) { }
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
