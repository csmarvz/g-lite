using System;
using System.Linq;
using System.Runtime.InteropServices;

class MicrophoneCapture : IDisposable
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
    struct Header
    {
        public IntPtr Data; public int Length, Recorded; public IntPtr User;
        public int Flags, Loops; public IntPtr Next, Reserved;
    }

    [DllImport("winmm.dll")] static extern int waveInGetNumDevs();
    [DllImport("winmm.dll", CharSet = CharSet.Unicode)] static extern int waveInGetDevCapsW(IntPtr id, ref Caps caps, int size);
    [DllImport("winmm.dll")]
    static extern int waveInOpen(out IntPtr waveIn, IntPtr id, ref Format format, IntPtr callback, IntPtr instance, int flags);
    [DllImport("winmm.dll")] static extern int waveInPrepareHeader(IntPtr waveIn, IntPtr header, int size);
    [DllImport("winmm.dll")] static extern int waveInUnprepareHeader(IntPtr waveIn, IntPtr header, int size);
    [DllImport("winmm.dll")] static extern int waveInAddBuffer(IntPtr waveIn, IntPtr header, int size);
    [DllImport("winmm.dll")] static extern int waveInStart(IntPtr waveIn);
    [DllImport("winmm.dll")] static extern int waveInReset(IntPtr waveIn);
    [DllImport("winmm.dll")] static extern int waveInClose(IntPtr waveIn);

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
            var caps = new Caps();
            if (waveInGetDevCapsW((IntPtr)i, ref caps, Marshal.SizeOf(typeof(Caps))) == 0 && caps.Name != null
                && caps.Name.IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0)
                return i;
        }
        return -1;
    }

    public MicrophoneCapture(int device)
    {
        var format = new Format { Tag = 1, Channels = 1, Rate = 48000, Bits = 16, Align = 2, Bytes = 96000 };
        int result = waveInOpen(out handle, (IntPtr)device, ref format, IntPtr.Zero, IntPtr.Zero, 0);
        if (result != 0) throw new InvalidOperationException("Couldn't open the headset's microphone (error " + result + ")");
        for (int i = 0; i < headers.Length; i++)
        {
            headers[i] = Marshal.AllocHGlobal(Size);
            Marshal.StructureToPtr(new Header { Data = Marshal.AllocHGlobal(48000), Length = 48000 }, headers[i], false);
            waveInPrepareHeader(handle, headers[i], Size);
            waveInAddBuffer(handle, headers[i], Size);
        }
        waveInStart(handle);
    }

    public void RequeueBuffers()
    {
        foreach (var header in headers)
        {
            int flags = Marshal.ReadInt32(header, FlagsAt);
            if ((flags & 1) == 0) continue;
            Marshal.WriteInt32(header, FlagsAt, flags & ~1);
            waveInAddBuffer(handle, header, Size);
        }
    }

    public void Dispose()
    {
        if (handle == IntPtr.Zero) return;
        waveInReset(handle);
        foreach (var header in headers)
        {
            waveInUnprepareHeader(handle, header, Size);
            Marshal.FreeHGlobal(((Header)Marshal.PtrToStructure(header, typeof(Header))).Data);
            Marshal.FreeHGlobal(header);
        }
        waveInClose(handle);
        handle = IntPtr.Zero;
    }
}

class HeadsetSettings
{
    public int Sidetone, AutoOffMinutes = -1;

    public HeadsetSettings Clone() { return (HeadsetSettings)MemberwiseClone(); }

    public string ToText() { return Sidetone + "," + AutoOffMinutes; }

    public static HeadsetSettings FromText(string text)
    {
        var values = text.Split(',').Select(int.Parse).ToArray();
        if (values.Length != 2) throw new FormatException();
        return new HeadsetSettings { Sidetone = values[0], AutoOffMinutes = values[1] };
    }
}

static class HeadsetProtocol
{
    public static readonly int[] OffMinutes = { 0, 5, 10, 15, 30, 60, 90 };

    public class State
    {
        public bool HasSidetone, HasOff, Charging;
        public int Millivolts;
        public HeadsetSettings Set = new HeadsetSettings();
    }

    public static State Read(Hidpp hidpp, bool autoOff)
    {
        var state = new State();
        try { state.Set.Sidetone = hidpp.Call(hidpp.FeatureIndex(0x8300), 0)[0]; state.HasSidetone = true; }
        catch (NotSupportedException) { }
        catch (HidppError) { }
        try
        {
            var reply = hidpp.Call(hidpp.FeatureIndex(0x1F20), 0);
            state.Millivolts = reply[0] << 8 | reply[1];
            state.Charging = reply[2] == 3;
        }
        catch (NotSupportedException) { }
        catch (HidppError) { }
        if (!autoOff) return state;
        try { state.Set.AutoOffMinutes = hidpp.Call(hidpp.FeatureIndex(0x1F20), 1)[0]; state.HasOff = true; }
        catch (NotSupportedException) { }
        catch (HidppError) { }
        return state;
    }

    public static void SetSidetone(Hidpp hidpp, int level)
    {
        hidpp.Call(hidpp.FeatureIndex(0x8300), 1, (byte)Math.Max(0, Math.Min(100, level)));
    }

    public static void SetOff(Hidpp hidpp, int minutes)
    {
        hidpp.Call(hidpp.FeatureIndex(0x1F20), 2, (byte)Math.Max(0, Math.Min(90, minutes)));
    }
}
