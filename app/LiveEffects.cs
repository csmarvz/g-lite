using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

// Effects G-Lite draws itself, key by key, through 0x8081 while it runs (like the Screen effect): Starlight, Echo and Audio, after
// G HUB's Starlight, Echo press and Audio visualizer, and G-Lite's own Rain, Fire and Aurora. Their ids sit above the keyboards' own effects and aren't sent as one.
static class LiveEffects
{
    // Each takes the Intensity slider (Starlight's and Rain's density, Echo's spread, Audio's and Fire's height, Aurora's colour mix);
    // 50 is the look each was first made with.
    public static readonly EffectInfo[] All =
    {
        new EffectInfo
        {
            Id = 0xE0, Name = "Starlight", Color = true, Speed = true, Intensity = true, MinPeriod = 500, MaxPeriod = 5000, DefaultPeriod = 2000
        },
        new EffectInfo
        {
            Id = 0xE1, Name = "Echo", Color = true, Speed = true, Intensity = true, MinPeriod = 200, MaxPeriod = 3000, DefaultPeriod = 1000
        },
        new EffectInfo { Id = 0xE2, Name = "Audio", Color = true, Direction = true, Intensity = true, OffersSolid = true },
        new EffectInfo
        {
            Id = 0xE3, Name = "Rain", Color = true, Speed = true, Direction = true, Intensity = true, MinPeriod = 300, MaxPeriod = 3000,
            DefaultPeriod = 1200
        },
        new EffectInfo
        {
            Id = 0xE4, Name = "Fire", Speed = true, Direction = true, Intensity = true, MinPeriod = 300, MaxPeriod = 3000, DefaultPeriod = 1200
        },
        new EffectInfo
        {
            Id = 0xE5, Name = "Aurora", Speed = true, Intensity = true, MinPeriod = 5000, MaxPeriod = 60000, DefaultPeriod = 20000
        },
    };
    public const int DefaultIntensity = 50;

    // Directions use Wave's numbers: 7 up, 2 down, 6 left, 1 right, 3 centre out, 8 centre in (both along the rows, mirrored about the
    // middle, as Wave's). Rain falls, Fire and Audio rise.
    public static readonly int[] Directions = { 7, 2, 6, 1, 3, 8 };
    public static readonly string[] ShortDirectionNames = { "Up", "Down", "Left", "Right", "Out", "In" };
    public static int DefaultDirection(byte id) { return id == 0xE3 ? 2 : 7; }

    public static EffectInfo Info(byte id) { return All.FirstOrDefault(effect => effect.Id == id); }

    public static bool Is(byte id) { return Info(id) != null; }

    public static LiveEffect Make(byte id)
    {
        switch (id)
        {
            case 0xE0: return new Starlight();
            case 0xE1: return new Echo();
            case 0xE2: return new Audio();
            case 0xE3: return new Rain();
            case 0xE4: return new Fire();
            case 0xE5: return new Aurora();
            default: return null;
        }
    }
}

// The keys an effect draws on: each lighting id once, with its centre spread over 0 to 1 (x left to right, y top to bottom).
class KeyField
{
    public int[] Ids;
    public float[] X, Y;
    // The distance from the first key centre to the last, in keys, across and down.
    public float Across = 20, Down = 5;
    public int Count { get { return Ids.Length; } }

    public double Distance(int first, int second)
    {
        double dx = (X[first] - X[second]) * Across, dy = (Y[first] - Y[second]) * Down;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    public static KeyField From(KeyPicture picture)
    {
        var ids = new List<int>();
        var lefts = new List<float>();
        var tops = new List<float>();
        var seen = new HashSet<int>();
        for (int key = 0; key < picture.Count; key++)
        {
            int id = picture.IdOf(key);
            if (id < 0 || !seen.Add(id)) continue;
            ids.Add(id);
            lefts.Add(picture.XOf(key));
            tops.Add(picture.YOf(key));
        }
        var field = new KeyField { Ids = ids.ToArray(), X = Spread(lefts), Y = Spread(tops) };
        if (ids.Count > 1)
        {
            field.Across = Math.Max(1, (lefts.Max() - lefts.Min()) * picture.WidthInKeys);
            field.Down = Math.Max(1, (tops.Max() - tops.Min()) * picture.HeightInKeys);
        }
        return field;
    }

    static float[] Spread(List<float> values)
    {
        if (values.Count == 0) return new float[0];
        float low = values.Min(), range = Math.Max(0.0001f, values.Max() - low);
        return values.Select(value => (value - low) / range).ToArray();
    }

    // Each key's place along a direction (LiveEffects.Directions), from the edge the effect starts at (0) to the other (1).
    public float[] Along(int direction)
    {
        switch (direction)
        {
            case 7: return Y.Select(y => 1 - y).ToArray();
            case 2: return Y;
            case 6: return X.Select(x => 1 - x).ToArray();
            case 3: return X.Select(x => Math.Abs(x - 0.5f) * 2).ToArray();
            case 8: return X.Select(x => 1 - Math.Abs(x - 0.5f) * 2).ToArray();
            default: return X;
        }
    }

    // Along the rows: left, right, centre out and centre in.
    public static bool Sideways(int direction) { return direction == 6 || direction == 1 || direction == 3 || direction == 8; }

    // Each key's lane across the direction, out of `count`: columns going up or down, rows going sideways.
    public int[] Lanes(int direction, int count)
    {
        return (Sideways(direction) ? Y : X).Select(across => Math.Min(count - 1, (int)(across * count))).ToArray();
    }
}

abstract class LiveEffect : IDisposable
{
    public int Color = 0xFFFFFF, Period = 2000, Intensity = LiveEffects.DefaultIntensity;
    public bool Solid;
    public int Direction = 7;
    protected double Level { get { return Math.Max(0, Math.Min(100, Intensity)) / 100.0; } }
    // Why the effect can't run, for the status line; null while it can.
    public string Problem;
    protected readonly Random random = new Random();

    // colors[i] is the colour of field.Ids[i]. seconds counts from the effect's start; elapsed is the time since the last frame.
    public abstract void Frame(KeyField field, double seconds, double elapsed, int[] colors);

    public virtual void Dispose() { }

    protected static int Scale(int color, double factor)
    {
        factor = Math.Max(0, Math.Min(1, factor));
        return (int)((color >> 16 & 0xFF) * factor) << 16 | (int)((color >> 8 & 0xFF) * factor) << 8 | (int)((color & 0xFF) * factor);
    }

    protected static int Blend(int from, int to, double fraction)
    {
        return ScreenCapture.Blend(from, to, Math.Max(0, Math.Min(1, fraction)));
    }

    protected static int Hsv(double hue, double saturation, double value)
    {
        var color = KeyboardView.Hsv(hue, saturation, Math.Max(0, Math.Min(1, value)));
        return color.R << 16 | color.G << 8 | color.B;
    }
}

// Keys light up at random and fade out over the period, on a sky of the colour at 4%. Intensity: from 2% to a third of the keys lit at
// a time (one in six at 50).
class Starlight : LiveEffect
{
    double[] born;

    public override void Frame(KeyField field, double seconds, double elapsed, int[] colors)
    {
        if (born == null || born.Length != field.Count) born = Enumerable.Repeat(-1000.0, field.Count).ToArray();
        double life = Period / 1000.0, chance = elapsed / life * (0.02 + Level * 0.31);
        int sky = Scale(Color, 0.04);
        for (int i = 0; i < field.Count; i++)
        {
            double age = seconds - born[i];
            if (age >= life && random.NextDouble() < chance) { born[i] = seconds; age = 0; }
            colors[i] = age < life ? Blend(sky, Color, Math.Sin(Math.PI * age / life)) : sky;
        }
    }
}

// A key lights up when it's pressed, anywhere in Windows, and fades out over the period. Intensity: how far the glow reaches the keys
// around it, from none to 4 keys away.
class Echo : LiveEffect
{
    readonly KeyPresses presses = new KeyPresses();
    // Key index in the field to the time it was pressed.
    readonly Dictionary<int, double> pressedAt = new Dictionary<int, double>();

    public Echo() { if (!presses.Listening) Problem = "Echo can't see key presses on this PC."; }

    public override void Frame(KeyField field, double seconds, double elapsed, int[] colors)
    {
        foreach (int usage in presses.Take())
        {
            int index = Array.IndexOf(field.Ids, GameModeKeys.LightingId(usage));
            // Scan code 2B is \ on ANSI keyboards and # on ISO ones, which have no \ there.
            if (usage == 0x31 && index < 0) index = Array.IndexOf(field.Ids, GameModeKeys.LightingId(0x32));
            if (index >= 0) pressedAt[index] = seconds;
        }
        double fade = Period / 1000.0, reach = Level * 4;
        foreach (var gone in pressedAt.Where(pair => seconds - pair.Value >= fade).Select(pair => pair.Key).ToList()) pressedAt.Remove(gone);
        for (int i = 0; i < field.Count; i++)
        {
            double level = 0;
            foreach (var pressed in pressedAt)
            {
                double distance = pressed.Key == i ? 0 : field.Distance(i, pressed.Key);
                double near = distance < 0.01 ? 1 : reach <= 0 ? 0 : Math.Max(0, 1 - distance / (reach + 0.6));
                level = Math.Max(level, near * (1 - (seconds - pressed.Value) / fade));
            }
            colors[i] = Scale(Color, level);
        }
    }

    public override void Dispose() { presses.Dispose(); }
}

// Bars grow with the sound playing on the PC, up from the bottom row by default (or down, or across the rows): low notes first. They
// start in the colour and turn as they grow: a colour's hue by a third (green to red), white (or grey) to red. Solid keeps the colour.
// Intensity: how loud a sound has to be to reach the far edge.
class Audio : LiveEffect
{
    // Bands across the keyboard's columns, or its rows going sideways.
    const int Bands = 20, RowBands = 6;
    AudioLoopback audio;
    double[] levels = new double[Bands];
    double retryAt;

    public override void Frame(KeyField field, double seconds, double elapsed, int[] colors)
    {
        if (audio == null && seconds >= retryAt)
            try { audio = new AudioLoopback(); Problem = null; }
            catch (Exception) { retryAt = seconds + 3; Problem = "Audio can't hear the PC's sound."; }
        int bands = KeyField.Sideways(Direction) ? RowBands : Bands;
        double[] heard = null;
        if (audio != null)
        {
            heard = audio.Bands(bands);
            if (heard == null) { audio.Dispose(); audio = null; retryAt = seconds + 1; }
        }
        for (int band = 0; band < Bands; band++)
        {
            double level = heard != null && band < bands ? heard[band] : 0;
            levels[band] = level > levels[band] ? level : Math.Max(level, levels[band] - elapsed * 1.6);
        }
        var lanes = field.Lanes(Direction, bands);
        var along = field.Along(Direction);
        double gain = 1.15 * (0.4 + Level * 1.2);
        int red = Color >> 16 & 0xFF, green = Color >> 8 & 0xFF, blue = Color & 0xFF, top = Math.Max(red, Math.Max(green, blue));
        double saturation = top == 0 ? 0 : (top - Math.Min(red, Math.Min(green, blue))) / (double)top;
        bool grey = saturation < 0.05;
        double hue = System.Drawing.Color.FromArgb(red, green, blue).GetHue() / 360, value = top / 255.0;
        for (int i = 0; i < field.Count; i++)
        {
            double height = along[i], lit = Math.Max(0, Math.Min(1, (levels[lanes[i]] * gain - height) * 6 + 1));
            int color = Solid ? Color
                : grey ? Blend(Color, Scale(0xFF0000, value), height)
                : Hsv(hue - 0.33 * height, saturation, value);
            colors[i] = Scale(color, lit);
        }
    }

    public override void Dispose() { if (audio != null) audio.Dispose(); }
}

// Drops fall down the columns with a fading trail (or up, or along the rows), taking the period to cross the keyboard. Intensity:
// from 0.1 to 1.3 drops per lane per fall.
class Rain : LiveEffect
{
    const int Columns = 22, Rows = 6;
    // Each drop: its lane, how far it is along the direction, its speed.
    readonly List<double[]> drops = new List<double[]>();
    int direction;

    public override void Frame(KeyField field, double seconds, double elapsed, int[] colors)
    {
        if (direction != Direction) { drops.Clear(); direction = Direction; }
        double fall = Period / 1000.0;
        int lanes = KeyField.Sideways(Direction) ? Rows : Columns;
        for (int lane = 0; lane < lanes; lane++)
            if (random.NextDouble() < elapsed * (0.1 + Level * 1.2) / fall)
                drops.Add(new double[] { lane, -0.1, (0.8 + 0.4 * random.NextDouble()) * 1.3 / fall });
        foreach (var drop in drops) drop[1] += drop[2] * elapsed;
        drops.RemoveAll(drop => drop[1] > 1.5);
        var keyLanes = field.Lanes(Direction, lanes);
        var along = field.Along(Direction);
        for (int i = 0; i < field.Count; i++)
        {
            double level = 0;
            foreach (var drop in drops)
            {
                if ((int)drop[0] != keyLanes[i]) continue;
                double behind = drop[1] - along[i];
                if (behind >= -0.1 && behind < 0) level = Math.Max(level, 1 + behind / 0.1);
                else if (behind >= 0 && behind < 0.45) level = Math.Max(level, 1 - behind / 0.45);
            }
            colors[i] = Scale(Color, level * level);
        }
    }
}

// Flames flicker up from the bottom row (or down from the top, or in from a side): black, red, orange, yellow. Intensity: how far they
// reach.
class Fire : LiveEffect
{
    double[] heat, phases;
    // Periods gone by, added up frame by frame so a speed change doesn't jump.
    double time;

    public override void Frame(KeyField field, double seconds, double elapsed, int[] colors)
    {
        if (heat == null || heat.Length != field.Count)
        {
            heat = new double[field.Count];
            phases = Enumerable.Range(0, field.Count).Select(i => random.NextDouble() * 6.28).ToArray();
        }
        time += elapsed * 1000 / Period;
        var along = field.Along(Direction);
        var across = KeyField.Sideways(Direction) ? field.Y : field.X;
        for (int i = 0; i < field.Count; i++)
        {
            double wave = across[i] * 7;
            double flicker = (Math.Sin(wave + time * 3.1) + Math.Sin(wave * 1.7 - time * 4.3) + Math.Sin(phases[i] + time * 5.9)) / 3;
            double target = (1 - along[i]) * 1.3 - 0.4 + (Level - 0.5) * 0.9 + 0.45 * flicker + 0.15 * (random.NextDouble() - 0.5);
            heat[i] += (target - heat[i]) * Math.Min(1, elapsed * 10);
            colors[i] = FireColor(heat[i]);
        }
    }

    static int FireColor(double heat)
    {
        heat = Math.Max(0, Math.Min(1, heat));
        if (heat < 0.4) return Scale(0xFF0000, heat / 0.4);
        if (heat < 0.75) return Blend(0xFF0000, 0xFF7000, (heat - 0.4) / 0.35);
        return Blend(0xFF7000, 0xFFD040, (heat - 0.75) / 0.25);
    }
}

// Slow bands of green, teal, blue and purple drift across the keys, a full turn per period. Intensity: from one teal to twice the
// range of hues.
class Aurora : LiveEffect
{
    // Added up frame by frame so a speed change doesn't jump.
    double turn;

    public override void Frame(KeyField field, double seconds, double elapsed, int[] colors)
    {
        turn += elapsed * 2 * Math.PI / (Period / 1000.0);
        double mix = Level * 2;
        for (int i = 0; i < field.Count; i++)
        {
            double x = field.X[i], y = field.Y[i];
            double hue = 0.47 + mix * (0.17 * Math.Sin(x * 3.1 + turn) + 0.1 * Math.Sin(y * 2.3 - turn * 2 + x * 1.7));
            double value = 0.3 + 0.7 * (0.5 + 0.5 * Math.Sin(x * 4.2 - turn * 3 + y * 1.5));
            colors[i] = Hsv(hue, 1, value);
        }
    }
}

// Key presses anywhere in Windows, as USB usages, from a low-level keyboard hook. Only the usage is kept, to light the key; the
// hook is removed when the effect stops.
class KeyPresses : IDisposable
{
    delegate IntPtr HookProcedure(int code, IntPtr message, IntPtr data);
    [DllImport("user32.dll")] static extern IntPtr SetWindowsHookEx(int hookType, HookProcedure callback, IntPtr module, uint threadId);
    [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr hookHandle);
    [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hookHandle, int code, IntPtr message, IntPtr data);
    [DllImport("kernel32.dll")] static extern IntPtr GetModuleHandle(string name);

    const int KeyboardLowLevel = 13, KeyDown = 0x100, SystemKeyDown = 0x104;
    readonly HookProcedure procedure;
    readonly Queue<int> usages = new Queue<int>();
    IntPtr hook;

    public KeyPresses()
    {
        procedure = Hooked;
        hook = SetWindowsHookEx(KeyboardLowLevel, procedure, GetModuleHandle(null), 0);
    }

    public bool Listening { get { return hook != IntPtr.Zero; } }

    IntPtr Hooked(int code, IntPtr message, IntPtr data)
    {
        if (code >= 0 && (message == (IntPtr)KeyDown || message == (IntPtr)SystemKeyDown))
        {
            int usage = Usage(Marshal.ReadInt32(data, 4), (Marshal.ReadInt32(data, 8) & 1) != 0);
            if (usage > 0) lock (usages) if (usages.Count < 64) usages.Enqueue(usage);
        }
        return CallNextHookEx(hook, code, message, data);
    }

    public List<int> Take()
    {
        lock (usages)
        {
            var taken = usages.ToList();
            usages.Clear();
            return taken;
        }
    }

    public void Dispose()
    {
        if (hook != IntPtr.Zero) UnhookWindowsHookEx(hook);
        hook = IntPtr.Zero;
    }

    // Scan codes (set 1) to USB usages, from the HID usage tables' keyboard page. Scan code 45 is Num Lock with the extended flag in
    // a low-level hook, and Pause without it.
    static readonly int[] Plain =
    {
        0x00, 0x29, 0x1E, 0x1F, 0x20, 0x21, 0x22, 0x23, 0x24, 0x25, 0x26, 0x27, 0x2D, 0x2E, 0x2A, 0x2B,
        0x14, 0x1A, 0x08, 0x15, 0x17, 0x1C, 0x18, 0x0C, 0x12, 0x13, 0x2F, 0x30, 0x28, 0xE0, 0x04, 0x16,
        0x07, 0x09, 0x0A, 0x0B, 0x0D, 0x0E, 0x0F, 0x33, 0x34, 0x35, 0xE1, 0x31, 0x1D, 0x1B, 0x06, 0x19,
        0x05, 0x11, 0x10, 0x36, 0x37, 0x38, 0xE5, 0x55, 0xE2, 0x2C, 0x39, 0x3A, 0x3B, 0x3C, 0x3D, 0x3E,
        0x3F, 0x40, 0x41, 0x42, 0x43, 0x48, 0x47, 0x5F, 0x60, 0x61, 0x56, 0x5C, 0x5D, 0x5E, 0x57, 0x59,
        0x5A, 0x5B, 0x62, 0x63, 0x00, 0x00, 0x64, 0x44, 0x45
    };

    static readonly Dictionary<int, int> Extended = new Dictionary<int, int>
    {
        { 0x1C, 0x58 }, { 0x1D, 0xE4 }, { 0x35, 0x54 }, { 0x37, 0x46 }, { 0x38, 0xE6 }, { 0x45, 0x53 }, { 0x47, 0x4A },
        { 0x48, 0x52 }, { 0x49, 0x4B }, { 0x4B, 0x50 }, { 0x4D, 0x4F }, { 0x4F, 0x4D }, { 0x50, 0x51 }, { 0x51, 0x4E },
        { 0x52, 0x49 }, { 0x53, 0x4C }, { 0x5B, 0xE3 }, { 0x5C, 0xE7 }, { 0x5D, 0x65 }
    };

    public static int Usage(int scanCode, bool extended)
    {
        int usage;
        if (extended) return Extended.TryGetValue(scanCode, out usage) ? usage : 0;
        return scanCode >= 0 && scanCode < Plain.Length ? Plain[scanCode] : 0;
    }
}

// The sound playing on the default output, through WASAPI loopback capture (shared mode, the mix format), as levels per band.
class AudioLoopback : IDisposable
{
    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] class DeviceEnumerator { }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice endpoint);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid interfaceId, int context, IntPtr parameters, [MarshalAs(UnmanagedType.IUnknown)] out object instance);
    }

    [ComImport, Guid("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioClient
    {
        [PreserveSig] int Initialize(int shareMode, int streamFlags, long bufferDuration, long periodicity, IntPtr format, IntPtr session);
        [PreserveSig] int GetBufferSize(out uint frames);
        [PreserveSig] int GetStreamLatency(out long latency);
        [PreserveSig] int GetCurrentPadding(out uint frames);
        [PreserveSig] int IsFormatSupported(int shareMode, IntPtr format, out IntPtr closest);
        [PreserveSig] int GetMixFormat(out IntPtr format);
        [PreserveSig] int GetDevicePeriod(out long defaultPeriod, out long minimumPeriod);
        [PreserveSig] int Start();
        [PreserveSig] int Stop();
        [PreserveSig] int Reset();
        [PreserveSig] int SetEventHandle(IntPtr handle);
        [PreserveSig] int GetService(ref Guid interfaceId, [MarshalAs(UnmanagedType.IUnknown)] out object service);
    }

    [ComImport, Guid("C8ADBD64-E71E-48A0-A4DE-185C395CD317"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioCaptureClient
    {
        [PreserveSig] int GetBuffer(out IntPtr data, out uint frames, out uint flags, out ulong devicePosition, out ulong counterPosition);
        [PreserveSig] int ReleaseBuffer(uint frames);
        [PreserveSig] int GetNextPacketSize(out uint frames);
    }

    const int Render = 0, Multimedia = 1, AllContexts = 23, Loopback = 0x00020000, Silent = 2, Size = 1024;
    readonly IAudioClient client;
    readonly IAudioCaptureClient capture;
    readonly int channels, rate, bits;
    readonly bool floating;
    readonly float[] ring = new float[4096];
    float[] floats = new float[0];
    short[] shorts = new short[0];
    int ringAt, lastPacket = Environment.TickCount;

    public AudioLoopback()
    {
        var enumerator = (IMMDeviceEnumerator)new DeviceEnumerator();
        IMMDevice device;
        Check(enumerator.GetDefaultAudioEndpoint(Render, Multimedia, out device));
        var clientId = typeof(IAudioClient).GUID;
        object instance;
        Check(device.Activate(ref clientId, AllContexts, IntPtr.Zero, out instance));
        client = (IAudioClient)instance;
        IntPtr format;
        Check(client.GetMixFormat(out format));
        try
        {
            // WAVEFORMATEX: tag, channels, rate, then the bits at 14; an extensible one has its subformat's first 4 bytes at 24
            // (3 for float, 1 for PCM).
            int tag = Marshal.ReadInt16(format, 0) & 0xFFFF;
            channels = Marshal.ReadInt16(format, 2);
            rate = Marshal.ReadInt32(format, 4);
            bits = Marshal.ReadInt16(format, 14);
            floating = bits == 32 && (tag == 3 || (tag == 0xFFFE && Marshal.ReadInt32(format, 24) == 3));
            Check(client.Initialize(0, Loopback, 2000000, 0, format, IntPtr.Zero));
        }
        finally { Marshal.FreeCoTaskMem(format); }
        var captureId = typeof(IAudioCaptureClient).GUID;
        object service;
        Check(client.GetService(ref captureId, out service));
        capture = (IAudioCaptureClient)service;
        Check(client.Start());
    }

    static void Check(int result) { if (result < 0) Marshal.ThrowExceptionForHR(result); }

    // False when the device went away (unplugged, or another default output picked).
    bool Read()
    {
        while (true)
        {
            uint waiting;
            if (capture.GetNextPacketSize(out waiting) < 0) return false;
            if (waiting == 0) return true;
            IntPtr data;
            uint frames, flags;
            ulong devicePosition, counterPosition;
            if (capture.GetBuffer(out data, out frames, out flags, out devicePosition, out counterPosition) < 0) return false;
            lastPacket = Environment.TickCount;
            int count = (int)frames * channels;
            bool usable = (flags & Silent) == 0 && channels > 0 && (floating || bits == 16);
            if (usable && floating) { if (floats.Length < count) floats = new float[count]; Marshal.Copy(data, floats, 0, count); }
            if (usable && !floating) { if (shorts.Length < count) shorts = new short[count]; Marshal.Copy(data, shorts, 0, count); }
            for (int frame = 0; frame < frames; frame++)
            {
                float sum = 0;
                if (usable)
                    for (int channel = 0; channel < channels; channel++)
                        sum += floating ? floats[frame * channels + channel] : shorts[frame * channels + channel] / 32768f;
                ring[ringAt] = usable ? sum / channels : 0;
                ringAt = (ringAt + 1) % ring.Length;
            }
            capture.ReleaseBuffer(frames);
        }
    }

    // Levels from 0 to 1, log spaced from 40 Hz to 16 kHz, over the last 1024 samples; null when the device went away.
    public double[] Bands(int count)
    {
        if (!Read()) return null;
        // Loopback sends no packets at all while nothing plays, which would leave the last sound in the ring.
        if (Environment.TickCount - lastPacket > 150) Array.Clear(ring, 0, ring.Length);
        var real = new double[Size];
        var imaginary = new double[Size];
        for (int i = 0; i < Size; i++)
            real[i] = ring[(ringAt - Size + i + ring.Length) % ring.Length] * (0.5 - 0.5 * Math.Cos(2 * Math.PI * i / (Size - 1)));
        Fourier(real, imaginary);
        var bands = new double[count];
        double low = 40, high = Math.Min(16000, rate / 2.0);
        for (int band = 0; band < count; band++)
        {
            double from = low * Math.Pow(high / low, (double)band / count), to = low * Math.Pow(high / low, (double)(band + 1) / count);
            int first = Math.Max(1, (int)(from * Size / rate)), last = Math.Max(first, (int)(to * Size / rate));
            double peak = 0;
            for (int bin = first; bin <= last && bin < Size / 2; bin++)
                peak = Math.Max(peak, Math.Sqrt(real[bin] * real[bin] + imaginary[bin] * imaginary[bin]));
            // A full-scale sine through the Hann window peaks at a quarter of the size.
            double decibels = 20 * Math.Log10(peak / (Size / 4.0) + 1e-9);
            bands[band] = Math.Max(0, Math.Min(1, (decibels + 60) / 54));
        }
        return bands;
    }

    // In-place radix-2 FFT.
    static void Fourier(double[] real, double[] imaginary)
    {
        int length = real.Length;
        for (int i = 1, j = 0; i < length; i++)
        {
            int bit = length >> 1;
            for (; (j & bit) != 0; bit >>= 1) j ^= bit;
            j ^= bit;
            if (i >= j) continue;
            double swap = real[i]; real[i] = real[j]; real[j] = swap;
            swap = imaginary[i]; imaginary[i] = imaginary[j]; imaginary[j] = swap;
        }
        for (int span = 2; span <= length; span <<= 1)
        {
            double angle = -2 * Math.PI / span, stepReal = Math.Cos(angle), stepImaginary = Math.Sin(angle);
            for (int start = 0; start < length; start += span)
            {
                double turnReal = 1, turnImaginary = 0;
                for (int offset = 0; offset < span / 2; offset++)
                {
                    int top = start + offset, bottom = top + span / 2;
                    double productReal = real[bottom] * turnReal - imaginary[bottom] * turnImaginary;
                    double productImaginary = real[bottom] * turnImaginary + imaginary[bottom] * turnReal;
                    real[bottom] = real[top] - productReal;
                    imaginary[bottom] = imaginary[top] - productImaginary;
                    real[top] += productReal;
                    imaginary[top] += productImaginary;
                    double nextReal = turnReal * stepReal - turnImaginary * stepImaginary;
                    turnImaginary = turnReal * stepImaginary + turnImaginary * stepReal;
                    turnReal = nextReal;
                }
            }
        }
    }

    public void Dispose()
    {
        try { client.Stop(); } catch (Exception) { }
        try { Marshal.ReleaseComObject(capture); Marshal.ReleaseComObject(client); } catch (Exception) { }
    }
}
