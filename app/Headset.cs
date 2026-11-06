using System;
using System.Linq;
using System.Runtime.InteropServices;

class Mic : IDisposable
{
    [StructLayout(LayoutKind.Sequential)]
    struct Format { public short Tag, Channels; public int Rate, Bytes; public short Align, Bits, Extra; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct Caps
    {
        public short Mid, Pid;
        public int Driver;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Name;
        public int Formats;
        public short Channels, Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct Header { public IntPtr Data; public int Length, Recorded; public IntPtr User; public int Flags, Loops; public IntPtr Next, Reserved; }

    [DllImport("winmm.dll")] static extern int waveInGetNumDevs();
    [DllImport("winmm.dll", CharSet = CharSet.Unicode)] static extern int waveInGetDevCapsW(IntPtr id, ref Caps caps, int size);
    [DllImport("winmm.dll")] static extern int waveInOpen(out IntPtr h, IntPtr id, ref Format f, IntPtr cb, IntPtr inst, int flags);
    [DllImport("winmm.dll")] static extern int waveInPrepareHeader(IntPtr h, IntPtr hdr, int size);
    [DllImport("winmm.dll")] static extern int waveInUnprepareHeader(IntPtr h, IntPtr hdr, int size);
    [DllImport("winmm.dll")] static extern int waveInAddBuffer(IntPtr h, IntPtr hdr, int size);
    [DllImport("winmm.dll")] static extern int waveInStart(IntPtr h);
    [DllImport("winmm.dll")] static extern int waveInReset(IntPtr h);
    [DllImport("winmm.dll")] static extern int waveInClose(IntPtr h);

    static readonly int Size = Marshal.SizeOf(typeof(Header));
    static readonly int FlagsAt = (int)Marshal.OffsetOf(typeof(Header), "Flags");
    IntPtr handle;
    readonly IntPtr[] headers = new IntPtr[2];

    public static int Find(string name)
    {
        if (string.IsNullOrEmpty(name)) return -1;
        var part = name.Substring(0, Math.Min(name.Length, 16));
        for (int i = 0; i < waveInGetNumDevs(); i++)
        {
            var c = new Caps();
            if (waveInGetDevCapsW((IntPtr)i, ref c, Marshal.SizeOf(typeof(Caps))) == 0 && c.Name != null && c.Name.IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0) return i;
        }
        return -1;
    }

    public Mic(int device)
    {
        var f = new Format { Tag = 1, Channels = 1, Rate = 48000, Bits = 16, Align = 2, Bytes = 96000 };
        int r = waveInOpen(out handle, (IntPtr)device, ref f, IntPtr.Zero, IntPtr.Zero, 0);
        if (r != 0) throw new InvalidOperationException("Couldn't open the headset's microphone (error " + r + ")");
        for (int i = 0; i < headers.Length; i++)
        {
            headers[i] = Marshal.AllocHGlobal(Size);
            Marshal.StructureToPtr(new Header { Data = Marshal.AllocHGlobal(48000), Length = 48000 }, headers[i], false);
            waveInPrepareHeader(handle, headers[i], Size);
            waveInAddBuffer(handle, headers[i], Size);
        }
        waveInStart(handle);
    }

    public void Keep()
    {
        foreach (var h in headers)
        {
            int flags = Marshal.ReadInt32(h, FlagsAt);
            if ((flags & 1) == 0) continue;
            Marshal.WriteInt32(h, FlagsAt, flags & ~1);
            waveInAddBuffer(handle, h, Size);
        }
    }

    public void Dispose()
    {
        if (handle == IntPtr.Zero) return;
        waveInReset(handle);
        foreach (var h in headers)
        {
            waveInUnprepareHeader(handle, h, Size);
            Marshal.FreeHGlobal(((Header)Marshal.PtrToStructure(h, typeof(Header))).Data);
            Marshal.FreeHGlobal(h);
        }
        waveInClose(handle);
        handle = IntPtr.Zero;
    }
}

class HeadSet
{
    public int Sidetone, Off = -1;

    public HeadSet Clone() { return (HeadSet)MemberwiseClone(); }

    public string ToText() { return Sidetone + "," + Off; }

    public static HeadSet FromText(string text)
    {
        var v = text.Split(',').Select(int.Parse).ToArray();
        if (v.Length != 2) throw new FormatException();
        return new HeadSet { Sidetone = v[0], Off = v[1] };
    }
}

static class GHeadset
{
    public static readonly int[] OffMinutes = { 0, 5, 10, 15, 30, 60, 90 };

    public class State
    {
        public bool HasSidetone, HasOff, Charging;
        public int Millivolts;
        public HeadSet Set = new HeadSet();
    }

    public static State Read(Hidpp h, bool autoOff)
    {
        var s = new State();
        try { s.Set.Sidetone = h.Call(h.F(0x8300), 0)[0]; s.HasSidetone = true; }
        catch (NotSupportedException) { }
        catch (HidppError) { }
        try
        {
            var b = h.Call(h.F(0x1F20), 0);
            s.Millivolts = b[0] << 8 | b[1];
            s.Charging = b[2] == 3;
        }
        catch (NotSupportedException) { }
        catch (HidppError) { }
        if (!autoOff) return s;
        try { s.Set.Off = h.Call(h.F(0x1F20), 1)[0]; s.HasOff = true; }
        catch (NotSupportedException) { }
        catch (HidppError) { }
        return s;
    }

    public static void SetSidetone(Hidpp h, int v) { h.Call(h.F(0x8300), 1, (byte)Math.Max(0, Math.Min(100, v))); }

    public static void SetOff(Hidpp h, int minutes) { h.Call(h.F(0x1F20), 2, (byte)Math.Max(0, Math.Min(90, minutes))); }
}
