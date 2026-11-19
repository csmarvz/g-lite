using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;

class HidppError : Exception
{
    public int Code;
    public HidppError(int code, string message) : base(message) { Code = code; }
}

class RefusedException : Exception
{
    public RefusedException(string message) : base(message) { }
}

class HalfWrittenException : RefusedException
{
    public HalfWrittenException(string message) : base(message) { }
}

class Hidpp : IDisposable
{
    int calls = new Random().Next(12);
    readonly byte DeviceIndex = 1;
    public bool Direct { get { return DeviceIndex == 0xFF; } }
    FileStream longStream, shortStream;
    byte[] longBuffer = new byte[20], shortBuffer = new byte[7];
    Task<int> pending, shortPending;
    Dictionary<int, byte> features = new Dictionary<int, byte>();
    public string DeviceLabel;
    public static Action<string> Tap;
    readonly string path;
    int featureLength;

    [DllImport("hid.dll", SetLastError = true)] static extern bool HidD_GetFeature(SafeFileHandle handle, byte[] buffer, int length);
    [DllImport("hid.dll", SetLastError = true)] static extern bool HidD_SetFeature(SafeFileHandle handle, byte[] buffer, int length);
    [DllImport("hid.dll")] static extern bool HidD_GetPreparsedData(SafeFileHandle handle, out IntPtr data);
    [DllImport("hid.dll")] static extern bool HidD_FreePreparsedData(IntPtr data);
    [DllImport("hid.dll")] static extern int HidP_GetCaps(IntPtr data, short[] caps);

    static string Bytes(byte[] bytes, int most)
    {
        var used = bytes.Take(most).ToArray();
        int length = used.Length;
        while (length > 0 && used[length - 1] == 0) length--;
        return length == 0 ? "" : "  " + BitConverter.ToString(used, 0, length).Replace("-", " ");
    }

    void Record(byte featureIndex, int fn, byte[] parameters)
    {
        if (Tap == null || featureIndex == 0) return;
        int id = features.Where(pair => pair.Value == featureIndex).Select(pair => pair.Key).FirstOrDefault();
        Tap((id != 0 ? id.ToString("X4") : "index " + featureIndex.ToString("X2")) + " fn" + fn.ToString("X") + Bytes(parameters, 16));
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);

    protected Hidpp(bool open) { }

    public Hidpp(string path, byte index)
    {
        DeviceIndex = index;
        this.path = path;
        var handle = CreateFile(path, 0xC0000000, 3, IntPtr.Zero, 3, 0x40000000, IntPtr.Zero);
        if (handle.IsInvalid) throw new IOException("Can't open the device (error " + Marshal.GetLastWin32Error() + ")");
        longStream = new FileStream(handle, FileAccess.ReadWrite, 1, true);
    }

    public Hidpp(string path, byte index, string shortPath) : this(path, index)
    {
        if (shortPath == null) return;
        var handle = CreateFile(shortPath, 0xC0000000, 3, IntPtr.Zero, 3, 0x40000000, IntPtr.Zero);
        if (!handle.IsInvalid) shortStream = new FileStream(handle, FileAccess.ReadWrite, 1, true);
    }

    public virtual void Dispose() { longStream.Dispose(); if (shortStream != null) shortStream.Dispose(); }

    public byte[] Register(byte subId, byte register, params byte[] parameters)
    {
        bool isSet = subId == 0x80 || subId == 0x82;
        if (Tap != null) Tap("register " + subId.ToString("X2") + " " + register.ToString("X2") + Bytes(parameters, 16));
        try
        {
            var reply = SendRegister(subId, register, parameters);
            if (isSet && DeviceLabel != null) Log.Register(DeviceLabel, register, parameters, "ok");
            return reply;
        }
        catch (HidppError ex) { if (isSet && DeviceLabel != null) Log.Register(DeviceLabel, register, parameters, "error " + ex.Code.ToString("X2")); throw; }
        catch (TimeoutException) { if (DeviceLabel != null) Log.Register(DeviceLabel, register, parameters, "no answer"); throw; }
    }

    protected virtual byte[] SendRegister(byte subId, byte register, byte[] parameters)
    {
        if (shortStream == null) throw new NotSupportedException("The device has no HID++ 1.0 channel");
        bool isLong = subId == 0x82;
        var message = new byte[isLong ? 20 : 7];
        message[0] = isLong ? (byte)0x11 : (byte)0x10;
        message[1] = DeviceIndex;
        message[2] = subId;
        message[3] = register;
        Array.Copy(parameters, 0, message, 4, Math.Min(parameters.Length, message.Length - 4));
        (isLong ? longStream : shortStream).Write(message, 0, message.Length);
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (true)
        {
            var reply = ReadBoth(deadline);
            if (reply == null) throw new TimeoutException("The device didn't answer. Is it on?");
            if (reply[1] != DeviceIndex) continue;
            if (reply[2] == 0x8F && reply[3] == subId && reply[4] == register)
                throw new HidppError(reply[5], string.Format("HID++ 1.0 error 0x{0:x2} (register {1:x2})", reply[5], register));
            if (reply[2] == subId && reply[3] == register) return reply.Skip(4).ToArray();
        }
    }

    public void Upload(byte subId, byte sequence, byte[] data)
    {
        if (Tap != null) Tap("upload " + subId.ToString("X2") + " " + sequence.ToString("X2") + Bytes(data, 16));
        try
        {
            SendUpload(subId, sequence, data);
            if (DeviceLabel != null) Log.Upload(DeviceLabel, subId, sequence, "ok");
        }
        catch (HidppError ex) { if (DeviceLabel != null) Log.Upload(DeviceLabel, subId, sequence, "error " + ex.Code.ToString("X2")); throw; }
        catch (TimeoutException) { if (DeviceLabel != null) Log.Upload(DeviceLabel, subId, sequence, "no answer"); throw; }
    }

    protected virtual void SendUpload(byte subId, byte sequence, byte[] data)
    {
        if (shortStream == null) throw new NotSupportedException("The device has no HID++ 1.0 channel");
        var message = new byte[20];
        message[0] = 0x11;
        message[1] = DeviceIndex;
        message[2] = subId;
        message[3] = sequence;
        Array.Copy(data, 0, message, 4, Math.Min(data.Length, 16));
        longStream.Write(message, 0, message.Length);
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (true)
        {
            var reply = ReadBoth(deadline);
            if (reply == null) throw new TimeoutException("The mouse didn't take the upload");
            if (reply[1] != DeviceIndex) continue;
            if (reply[2] == 0x8F && reply[3] == subId) throw new HidppError(reply[5], string.Format("HID++ 1.0 error 0x{0:x2} (upload)", reply[5]));
            if (reply[2] != 0x50) continue;
            // An error ack doesn't repeat the sequence number (cvuchener's g500 doc/hidpp10.md: byte 4 "if there was no error").
            if (reply[3] != 0x01) throw new HidppError(reply[3], string.Format("The mouse refused the upload (0x{0:x2})", reply[3]));
            if (reply[4] != sequence) continue;
            return;
        }
    }

    byte[] ReadBoth(DateTime deadline)
    {
        if (pending == null) pending = longStream.ReadAsync(longBuffer, 0, 20);
        if (shortPending == null) shortPending = shortStream.ReadAsync(shortBuffer, 0, 7);
        var milliseconds = (int)(deadline - DateTime.UtcNow).TotalMilliseconds;
        if (milliseconds <= 0) return null;
        int which = Task.WaitAny(new Task[] { pending, shortPending }, milliseconds);
        if (which < 0) return null;
        var done = which == 0 ? pending : shortPending;
        if (which == 0) pending = null; else shortPending = null;
        if (done.IsFaulted || done.IsCanceled) throw new IOException("The device stopped answering. Was it unplugged?");
        if (which == 0) return (byte[])longBuffer.Clone();
        var reply = new byte[20];
        Array.Copy(shortBuffer, reply, 7);
        return reply;
    }

    public byte[] Call(byte featureIndex, int fn, params byte[] parameters)
    {
        Record(featureIndex, fn, parameters);
        if (DeviceLabel == null || featureIndex == 0) return Send(featureIndex, fn, parameters);
        int id = features.Where(pair => pair.Value == featureIndex).Select(pair => pair.Key).FirstOrDefault();
        bool read = Log.Reads(id, fn);
        try
        {
            var reply = Send(featureIndex, fn, parameters);
            if (!read) Log.Sent(DeviceLabel, id, fn, parameters, "ok");
            return reply;
        }
        catch (HidppError ex) { Log.Sent(DeviceLabel, id, fn, parameters, "error " + ex.Code.ToString("X2")); throw; }
        catch (TimeoutException) { Log.Sent(DeviceLabel, id, fn, parameters, "no answer"); throw; }
    }

    public byte[] CallUnlogged(byte featureIndex, int fn, params byte[] parameters) { Record(featureIndex, fn, parameters); return Send(featureIndex, fn, parameters); }

    // For a call the device doesn't answer, like 0x1814 fn 1: it switches to another computer first.
    public void SendWithoutReply(byte featureIndex, int fn, params byte[] parameters)
    {
        Record(featureIndex, fn, parameters);
        Write(featureIndex, fn, parameters);
        if (DeviceLabel != null) Log.Sent(DeviceLabel, features.Where(pair => pair.Value == featureIndex).Select(pair => pair.Key).FirstOrDefault(), fn, parameters, "sent");
    }

    protected virtual void Write(byte featureIndex, int fn, byte[] parameters) { Message(featureIndex, fn, parameters); }

    byte[] Message(byte featureIndex, int fn, byte[] parameters)
    {
        var message = new byte[20];
        message[0] = 0x11; message[1] = DeviceIndex; message[2] = featureIndex; message[3] = (byte)(fn << 4 | 1 + (System.Threading.Interlocked.Increment(ref calls) & 0x7FFFFFFF) % 12);
        Array.Copy(parameters, 0, message, 4, parameters.Length);
        longStream.Write(message, 0, 20);
        return message;
    }

    protected virtual byte[] Send(byte featureIndex, int fn, byte[] parameters)
    {
        var message = Message(featureIndex, fn, parameters);
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (true)
        {
            var reply = Read(deadline);
            if (reply == null) throw new TimeoutException("The device didn't answer. Is it on?");
            if (reply[1] != DeviceIndex) continue;
            if (reply[2] == 0xFF && reply[3] == featureIndex && reply[4] == message[3])
                throw new HidppError(reply[5], string.Format("HID++ error 0x{0:x2} (feature {1}, fn {2})", reply[5], featureIndex, fn));
            if (reply[2] == featureIndex && reply[3] == message[3]) return reply.Skip(4).ToArray();
        }
    }

    byte[] Read(DateTime deadline)
    {
        if (pending == null) pending = longStream.ReadAsync(longBuffer, 0, 20);
        var milliseconds = (int)(deadline - DateTime.UtcNow).TotalMilliseconds;
        if (milliseconds <= 0 || !pending.Wait(milliseconds)) return null;
        pending = null;
        return (byte[])longBuffer.Clone();
    }

    // Feature reports, for the devices that don't speak HID++ (the G510). Each call opens the collection without overlapped I/O, which HidD_GetFeature and HidD_SetFeature need.
    public byte[] GetFeature(byte id)
    {
        if (Tap != null) Tap("get feature " + id.ToString("X2"));
        return ReadFeature(id);
    }

    public void SetFeature(params byte[] report)
    {
        if (Tap != null) Tap("set feature" + Bytes(report, 16));
        try
        {
            WriteFeature(report);
            if (DeviceLabel != null) Log.Add(DeviceLabel, "sent", "feature" + Bytes(report, 16), "ok");
        }
        catch (IOException) { if (DeviceLabel != null) Log.Add(DeviceLabel, "sent", "feature" + Bytes(report, 16), "error"); throw; }
    }

    // For animation frames: tapped for traces but kept out of the log, like CallUnlogged.
    public void SetFeatureUnlogged(params byte[] report)
    {
        if (Tap != null) Tap("set feature" + Bytes(report, 16));
        WriteFeature(report);
    }

    protected virtual byte[] ReadFeature(byte id)
    {
        using (var handle = CreateFile(path, 0xC0000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero))
        {
            var buffer = new byte[FeatureLength(handle)];
            buffer[0] = id;
            if (handle.IsInvalid || !HidD_GetFeature(handle, buffer, buffer.Length)) throw new IOException("Couldn't read feature report " + id.ToString("X2") + " (error " + Marshal.GetLastWin32Error() + ")");
            return buffer;
        }
    }

    protected virtual void WriteFeature(byte[] report)
    {
        using (var handle = CreateFile(path, 0xC0000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero))
        {
            var buffer = new byte[Math.Max(report.Length, FeatureLength(handle))];
            Array.Copy(report, buffer, report.Length);
            if (handle.IsInvalid || !HidD_SetFeature(handle, buffer, buffer.Length)) throw new IOException("Couldn't set feature report " + report[0].ToString("X2") + " (error " + Marshal.GetLastWin32Error() + ")");
        }
    }

    int FeatureLength(SafeFileHandle handle)
    {
        if (featureLength > 0 || handle.IsInvalid) return Math.Max(1, featureLength);
        IntPtr data;
        if (!HidD_GetPreparsedData(handle, out data)) return 1;
        var caps = new short[32];
        HidP_GetCaps(data, caps);
        HidD_FreePreparsedData(data);
        featureLength = caps[4];
        return Math.Max(1, featureLength);
    }

    public byte FeatureIndex(int id)
    {
        byte index;
        if (!features.TryGetValue(id, out index))
        {
            features[id] = index = Call(0, 0, (byte)(id >> 8), (byte)id)[0];
        }
        if (index == 0) throw new NotSupportedException(string.Format("The device lacks feature {0:x4}", id));
        return index;
    }
}

class EffectInfo
{
    public byte Id;
    public string Name;
    public bool Color, Speed, Direction;
    public int MinPeriod, MaxPeriod, DefaultPeriod;
}

class Lighting
{
    public byte Effect = 0x01;
    public int Color = 0xFFFFFF, Period = 5000, Direction = 1, Dim = 50, Idle = 60, Sleep = 300;
    public Lighting Clone() { return (Lighting)MemberwiseClone(); }

    public string ToText(int brightness)
    {
        return string.Join(",", Effect, Color.ToString("X6"), Period, Direction, Dim, Idle, Sleep, brightness);
    }

    public static Lighting FromText(string text, out int brightness)
    {
        var parts = text.Split(',');
        brightness = int.Parse(parts[7]);
        return new Lighting
        {
            Effect = byte.Parse(parts[0]), Color = int.Parse(parts[1], System.Globalization.NumberStyles.HexNumber), Period = int.Parse(parts[2]),
            Direction = int.Parse(parts[3]), Dim = int.Parse(parts[4]), Idle = int.Parse(parts[5]), Sleep = int.Parse(parts[6]),
        };
    }
    public bool Same(Lighting other)
    {
        return other != null && Effect == other.Effect && Color == other.Color && Period == other.Period && Direction == other.Direction
            && Dim == other.Dim && Idle == other.Idle && Sleep == other.Sleep;
    }
}

static class G515Profile
{
    public static readonly EffectInfo[] Effects =
    {
        new EffectInfo { Id = 0x00, Name = "Off" },
        new EffectInfo { Id = 0x01, Name = "Static", Color = true },
        new EffectInfo { Id = 0x0A, Name = "Breathe", Color = true, Speed = true, MinPeriod = 1000, MaxPeriod = 20000, DefaultPeriod = 5000 },
        new EffectInfo { Id = 0x15, Name = "Cycle", Speed = true, MinPeriod = 1000, MaxPeriod = 20000, DefaultPeriod = 5000 },
        new EffectInfo { Id = 0x16, Name = "Wave", Speed = true, Direction = true, MinPeriod = 1000, MaxPeriod = 20000, DefaultPeriod = 5000 },
        new EffectInfo { Id = 0x17, Name = "Ripple", Color = true, Speed = true, MinPeriod = 2, MaxPeriod = 200, DefaultPeriod = 20 },
    };

    public static EffectInfo EffectById(byte id) { return Effects.FirstOrDefault(effect => effect.Id == id); }

    static int Scale(int color, int percent)
    {
        return ((color >> 16 & 0xFF) * percent / 100) << 16 | ((color >> 8 & 0xFF) * percent / 100) << 8 | (color & 0xFF) * percent / 100;
    }

    public static byte[] Encode(Lighting lighting, int percent)
    {
        var parameters = new byte[10];
        int color = Scale(lighting.Color, percent), intensity = Math.Max(1, percent);
        Action putColor = () => { parameters[0] = (byte)(color >> 16); parameters[1] = (byte)(color >> 8); parameters[2] = (byte)color; };
        switch (lighting.Effect)
        {
            case 0x01: putColor(); parameters[3] = 0x02; break;
            case 0x0A: putColor(); parameters[3] = (byte)(lighting.Period >> 8); parameters[4] = (byte)lighting.Period; parameters[6] = (byte)intensity; break;
            case 0x15:
            case 0x16:
                parameters[1] = 0xFF; parameters[6] = (byte)(lighting.Period >> 8); parameters[7] = (byte)lighting.Period; parameters[8] = (byte)intensity;
                if (lighting.Effect == 0x16) parameters[9] = (byte)lighting.Direction;
                break;
            case 0x17: putColor(); parameters[3] = 0xFF; parameters[6] = (byte)(lighting.Period >> 8); parameters[7] = (byte)lighting.Period; break;
        }
        return parameters;
    }

    static int Rgb(byte[] bytes, int offset) { return bytes[offset] << 16 | bytes[offset + 1] << 8 | bytes[offset + 2]; }
    static int Max3(int color) { return Math.Max(color >> 16 & 0xFF, Math.Max(color >> 8 & 0xFF, color & 0xFF)); }
    static int ReadUInt16BigEndian(byte[] bytes, int offset) { return bytes[offset] << 8 | bytes[offset + 1]; }
    static void Put16(byte[] bytes, int offset, int value) { bytes[offset] = (byte)(value >> 8); bytes[offset + 1] = (byte)value; }
    static void Put32(byte[] bytes, int offset, uint value) { bytes[offset] = (byte)(value >> 24); bytes[offset + 1] = (byte)(value >> 16); bytes[offset + 2] = (byte)(value >> 8); bytes[offset + 3] = (byte)value; }

    // Returns null when the profile holds an effect this app doesn't know (the factory animation).
    public static Lighting Decode(byte[] profile)
    {
        var record = profile.Skip(30 + ((profile[74] & 1) != 0 ? 0 : 11)).Take(11).ToArray();
        var dim = profile.Skip(30 + ((profile[74] & 2) != 0 ? 22 : 33)).Take(11).ToArray();
        var effect = EffectById(record[0]);
        if (effect == null) return null;
        var lighting = new Lighting { Effect = record[0] };
        int idle = ReadUInt16BigEndian(profile, 2), sleep = ReadUInt16BigEndian(profile, 4);
        lighting.Idle = idle == 0xFFFF ? 0 : idle;
        lighting.Sleep = sleep == 0xFFFF ? 0 : sleep;
        if (effect.Color) lighting.Color = Rgb(record, 1);
        if (effect.Speed) lighting.Period = Math.Min(effect.MaxPeriod, Math.Max(effect.MinPeriod, record[0] == 0x0A ? ReadUInt16BigEndian(record, 4) : ReadUInt16BigEndian(record, 7)));
        if (effect.Direction) lighting.Direction = record[10];
        if (dim[0] == record[0])
        {
            if (effect.Color && Max3(lighting.Color) > 0) lighting.Dim = (Max3(Rgb(dim, 1)) * 100 + Max3(lighting.Color) / 2) / Max3(lighting.Color);
            else if (record[0] == 0x15 || record[0] == 0x16) lighting.Dim = dim[9];
        }
        return lighting;
    }

    public static byte[] BuildProfile(byte[] baseProfile, Lighting lighting)
    {
        var profile = (byte[])baseProfile.Clone();
        Put16(profile, 2, lighting.Idle <= 0 ? 0xFFFF : lighting.Idle);
        Put16(profile, 4, lighting.Sleep <= 0 ? 0xFFFF : lighting.Sleep);
        Array.Clear(profile, 30, 44);
        profile[41] = lighting.Effect; Array.Copy(Encode(lighting, 100), 0, profile, 42, 10);
        profile[63] = lighting.Effect; Array.Copy(Encode(lighting, lighting.Dim), 0, profile, 64, 10);
        profile[74] = 0;
        return profile;
    }

    public static byte[] BuildDirectory(byte[] factoryDir, byte[] profile)
    {
        var directory = (byte[])factoryDir.Clone();
        Put32(directory, 10, Crc32(profile));
        directory[14] = 8;
        Put32(directory, 0, Crc32(directory.Skip(4).ToArray()));
        return directory;
    }

    public static uint Crc32(byte[] data)
    {
        uint crc = 0xFFFFFFFF;
        foreach (var value in data)
        {
            crc ^= value;
            for (int bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ (0xEDB88320 & (uint)-(int)(crc & 1));
        }
        return ~crc;
    }

    static readonly byte[] DirHeader = { 0x01, 0x01, 0x81, 0x01, 0x40, 0x63 };

    public static bool ValidProfile(byte[] profile) { return profile != null && profile.Length == 99 && profile[0] == 1 && profile[1] == 1; }

    static bool ValidDir(byte[] directory)
    {
        return directory != null && directory.Length == 16 && directory.Skip(4).Take(6).SequenceEqual(DirHeader) && directory[15] == 0xFF
            && Crc32(directory.Skip(4).ToArray()) == (uint)(directory[0] << 24 | directory[1] << 16 | directory[2] << 8 | directory[3]);
    }

    // ---- device operations ----

    static byte[] ReadBuffer(Hidpp hidpp, int size)
    {
        var collected = new List<byte>();
        for (int offset = 0; offset < size; offset += 16) collected.AddRange(hidpp.Call(hidpp.FeatureIndex(0x8101), 0xC, (byte)(offset >> 8), (byte)offset));
        return collected.Take(size).ToArray();
    }

    static byte[] ReadFile(Hidpp hidpp, int address, int size)
    {
        try { hidpp.Call(hidpp.FeatureIndex(0x8101), 8, (byte)(address >> 8), (byte)address, (byte)(size >> 8), (byte)size); }
        catch (HidppError ex) { if (ex.Code == 4 && address < 0x0100) return null; throw; }
        return ReadBuffer(hidpp, size);
    }

    static void SaveFile(Hidpp hidpp, int address, byte[] data, uint checksum)
    {
        if (address != 0x0000 && address != 0x0008) throw new InvalidOperationException("refusing to write factory storage");
        byte feature = hidpp.FeatureIndex(0x8101);
        hidpp.Call(feature, 2, 0, (byte)data.Length);
        for (int offset = 0; offset < data.Length; offset += 16) hidpp.Call(feature, 3, data.Skip(offset).Take(16).ToArray());
        if (!ReadBuffer(hidpp, data.Length).SequenceEqual(data)) throw new IOException("Staging buffer mismatch, nothing written");
        var crc = new byte[4]; Put32(crc, 0, checksum);
        hidpp.Call(feature, 7, (byte)(address >> 8), (byte)address, 0, (byte)data.Length, crc[0], crc[1], crc[2], crc[3]);
        if (!data.SequenceEqual(ReadFile(hidpp, address, data.Length) ?? new byte[0])) throw new IOException("Flash readback mismatch");
    }

    public class Files { public byte[] FactoryProfile, FactoryDir, UserProfile, UserDir; }

    public static Files Read(Hidpp hidpp)
    {
        return new Files
        {
            FactoryProfile = ReadFile(hidpp, 0x0101, 99),
            FactoryDir = ReadFile(hidpp, 0x0100, 16),
            UserProfile = ReadFile(hidpp, 0x0008, 99),
            UserDir = ReadFile(hidpp, 0x0000, 16),
        };
    }

    public static void CheckFiles(Files files)
    {
        if (!ValidProfile(files.FactoryProfile) || !ValidDir(files.FactoryDir)) throw new RefusedException("Unknown onboard profile layout, not touching it");
        if (files.UserProfile == null && files.UserDir == null) return;
        bool ours = files.UserDir == null || (ValidDir(files.UserDir) && files.UserDir[14] == 8);
        if (ours && (files.UserProfile == null || files.UserDir == null)) throw new HalfWrittenException("User profile is half written, not touching it");
        if (!ours || !ValidProfile(files.UserProfile))
            throw new RefusedException("The profiles saved on the keyboard aren't laid out the way G-Lite writes them, not touching them");
        if (Crc32(files.UserProfile) != (uint)(files.UserDir[10] << 24 | files.UserDir[11] << 16 | files.UserDir[12] << 8 | files.UserDir[13]))
            throw new HalfWrittenException("User profile doesn't match its directory entry, not touching it");
    }

    public static bool FilesAreValid(Files files)
    {
        try { CheckFiles(files); return true; } catch (RefusedException) { return false; }
    }

    public static Files Snapshot(Hidpp hidpp)
    {
        var files = Read(hidpp);
        CheckFiles(files);
        return files;
    }

    public static int Brightness(Hidpp hidpp) { var reply = hidpp.Call(hidpp.FeatureIndex(0x8040), 1); return ReadUInt16BigEndian(reply, 0); }
    public static void SetBrightness(Hidpp hidpp, int value) { hidpp.Call(hidpp.FeatureIndex(0x8040), 2, (byte)(value >> 8), (byte)value); }

    public static void Claim(Hidpp hidpp)
    {
        hidpp.Call(hidpp.FeatureIndex(0x8101), 6, 0x05);
        hidpp.Call(hidpp.FeatureIndex(0x8071), 5, 0x01, 0x03, 0x04);
    }

    public static void Release(Hidpp hidpp)
    {
        hidpp.Call(hidpp.FeatureIndex(0x8071), 5, 0x01, 0x00, 0x00);
        hidpp.Call(hidpp.FeatureIndex(0x8101), 6, 0x03);
    }

    public static void Preview(Hidpp hidpp, Lighting lighting) { Preview(hidpp, lighting, 100); }

    public static void Preview(Hidpp hidpp, Lighting lighting, int level)
    {
        byte feature = hidpp.FeatureIndex(0x8071);
        int index = -1;
        for (int effectIndex = 0; effectIndex < 16 && index < 0; effectIndex++)
            if (hidpp.Call(feature, 0, 0, (byte)effectIndex, 0)[3] == lighting.Effect) index = effectIndex;
        if (index < 0) throw new NotSupportedException("Effect not supported by this keyboard");
        hidpp.Call(feature, 1, new byte[] { 0, (byte)index }.Concat(Encode(lighting, level)).Concat(new byte[] { 0x01 }).ToArray());
    }

    public static void Store(Hidpp hidpp, Lighting lighting) { Store(hidpp, Snapshot(hidpp), lighting); }

    public static void Store(Hidpp hidpp, Files files, Lighting lighting)
    {
        var profile = BuildProfile(files.UserProfile ?? files.FactoryProfile, lighting);
        WriteFiles(hidpp, files, profile, BuildDirectory(files.FactoryDir, profile));
    }

    public static void Restore(Hidpp hidpp, Files backup)
    {
        var now = Read(hidpp);
        if (!ValidProfile(now.FactoryProfile) || !ValidDir(now.FactoryDir)) throw new RefusedException("Unknown onboard profile layout, not touching it");
        if (backup.FactoryProfile != null && (!backup.FactoryProfile.SequenceEqual(now.FactoryProfile) || !backup.FactoryDir.SequenceEqual(now.FactoryDir)))
            throw new RefusedException("This backup is from a different keyboard, not restoring it");
        if (backup.UserProfile == null)
        {
            var profile = (byte[])now.FactoryProfile.Clone();
            WriteFiles(hidpp, now, profile, BuildDirectory(now.FactoryDir, profile));
            return;
        }
        CheckFiles(new Files { FactoryProfile = now.FactoryProfile, FactoryDir = now.FactoryDir, UserProfile = backup.UserProfile, UserDir = backup.UserDir });
        WriteFiles(hidpp, now, backup.UserProfile, backup.UserDir);
    }

    static void WriteFiles(Hidpp hidpp, Files now, byte[] profile, byte[] directory)
    {
        if (now.UserProfile == null || !profile.SequenceEqual(now.UserProfile)) SaveFile(hidpp, 0x0008, profile, Crc32(profile));
        if (now.UserDir == null || !directory.SequenceEqual(now.UserDir)) SaveFile(hidpp, 0x0000, directory, Crc32(directory.Skip(4).ToArray()));
        Activate(hidpp, profile);
    }

    public static void Activate(Hidpp hidpp, byte[] profile)
    {
        hidpp.Call(hidpp.FeatureIndex(0x8071), 5, 0x01, 0x00, 0x00);
        byte feature = hidpp.FeatureIndex(0x8101);
        hidpp.Call(feature, 6, 0x0B);
        var config = new byte[] { 0x81, 0x01, 0x01, 0x00, 0x01, 0x00, 0x63, 0, 0, 0, 0 };
        Put32(config, 7, Crc32(profile));
        hidpp.Call(feature, 9, config);
        var reply = hidpp.Call(feature, 6, 0x00);
        if ((reply[0] & 1) == 0 || reply[1] != 0 || reply[2] != 1) throw new IOException(string.Format("Unexpected state {0:x2} {1:x2} {2:x2} after activating", reply[0], reply[1], reply[2]));
    }
}

static class G915Lighting
{
    static readonly Dictionary<byte, byte[]> Alike = new Dictionary<byte, byte[]>
    {
        { 0x00, new byte[] { 0x00 } },
        { 0x01, new byte[] { 0x01 } },
        { 0x0A, new byte[] { 0x0A } },
        { 0x15, new byte[] { 0x15, 0x03 } },
        { 0x16, new byte[] { 0x16, 0x04 } },
        { 0x17, new byte[] { 0x17, 0x0B } },
    };

    static readonly Dictionary<byte, byte> Instead = new Dictionary<byte, byte> { { 0x16, 0x15 }, { 0x17, 0x01 } };

    public static List<byte[]> Clusters(Hidpp hidpp)
    {
        byte feature = hidpp.FeatureIndex(0x8071);
        var all = new List<byte[]>();
        for (int cluster = 0; cluster < 8; cluster++)
        {
            var ids = new List<byte>();
            for (int effectIndex = 0; effectIndex < 32; effectIndex++)
            {
                try { ids.Add(hidpp.CallUnlogged(feature, 0, (byte)cluster, (byte)effectIndex, 0)[3]); }
                catch (HidppError) { break; }
            }
            if (ids.Count == 0) break;
            all.Add(ids.ToArray());
        }
        if (all.Count == 0) throw new NotSupportedException("The keyboard lists no lighting effects");
        return all;
    }

    public static int Keys(List<byte[]> clusters)
    {
        int largestIndex = 0;
        for (int cluster = 1; cluster < clusters.Count; cluster++) if (clusters[cluster].Length > clusters[largestIndex].Length) largestIndex = cluster;
        return largestIndex;
    }

    public static int Pick(byte want, byte[] has, bool loose)
    {
        while (true)
        {
            byte[] ids;
            if (Alike.TryGetValue(want, out ids))
                foreach (var id in ids)
                {
                    int index = Array.IndexOf(has, id);
                    if (index >= 0) return index;
                }
            if (!loose || !Instead.TryGetValue(want, out want)) return -1;
        }
    }

    public static byte[] Encode(byte id, Lighting lighting)
    {
        var parameters = new byte[10];
        int color = lighting.Color;
        switch (id)
        {
            case 0x03: parameters[5] = (byte)(lighting.Period >> 8); parameters[6] = (byte)lighting.Period; parameters[7] = 100; return parameters;
            case 0x04: parameters[6] = (byte)lighting.Period; parameters[7] = (byte)lighting.Direction; parameters[8] = 100; parameters[9] = (byte)(lighting.Period >> 8); return parameters;
            case 0x0B: parameters[0] = (byte)(color >> 16); parameters[1] = (byte)(color >> 8); parameters[2] = (byte)color; parameters[4] = (byte)(lighting.Period >> 8); parameters[5] = (byte)lighting.Period; return parameters;
        }
        var copy = lighting.Clone();
        copy.Effect = id;
        return G515Profile.Encode(copy, 100);
    }

    // The G915 TKL turns itself off after its timeout even while G-Lite holds the power modes, and wakes in mode 3 (off) on its own
    // lighting, ignoring effects until fn 8 sets full power again (tested on an owner's keyboard).
    public static void Claim(Hidpp hidpp)
    {
        byte feature = hidpp.FeatureIndex(0x8071);
        hidpp.Call(feature, 5, 0x01, 0x03, 0x04);
        try { hidpp.Call(feature, 8, 0x01, 0x01, 0x00); } catch (HidppError) { }
    }

    public static void Release(Hidpp hidpp) { hidpp.Call(hidpp.FeatureIndex(0x8071), 5, 0x01, 0x00, 0x00); }

    // The onboard profile, format 4, from a G915 TKL's report (docs/G915.md): power save and off after (seconds, little-endian) at
    // 0x1C and 0x1E, then four 11-byte lighting records from 0xD0, one per cluster, then the same dimmed for power save.
    const int Records = 0xD0;

    public static byte[] ReadProfile(Hidpp hidpp, out int sector)
    {
        byte feature = hidpp.FeatureIndex(0x8100);
        if (hidpp.Call(feature, 2)[0] != 1) throw new RefusedException("The keyboard isn't on its onboard profiles");
        var info = hidpp.Call(feature, 0);
        int size = info[7] << 8 | info[8];
        if (info[1] != 4 || size < Records + 44 + 2 || size > 0x1000) throw new RefusedException("Saving isn't supported on this keyboard (profile format " + info[1] + ")");
        var current = hidpp.Call(feature, 4);
        sector = current[0] << 8 | current[1];
        if (sector == 0 || sector >= 0x100) throw new RefusedException("The keyboard runs a factory profile");
        var profile = MouseProtocol.ReadSector(hidpp, sector, size);
        if (!MouseProtocol.Valid(profile)) throw new RefusedException("The keyboard's profile has a bad checksum");
        return profile;
    }

    // Returns null when the keys hold an effect this app doesn't know.
    public static Lighting Decode(byte[] profile, int keys)
    {
        var record = profile.Skip(Records + 11 * keys).Take(11).ToArray();
        var lighting = new Lighting { Idle = profile[0x1C] | profile[0x1D] << 8, Sleep = profile[0x1E] | profile[0x1F] << 8, Color = record[1] << 16 | record[2] << 8 | record[3] };
        switch (record[0])
        {
            case 0x00: lighting.Effect = 0x00; break;
            case 0x01: lighting.Effect = 0x01; break;
            case 0x0A: lighting.Effect = 0x0A; lighting.Period = record[4] << 8 | record[5]; break;
            case 0x03: lighting.Effect = 0x15; lighting.Period = record[6] << 8 | record[7]; break;
            case 0x04: lighting.Effect = 0x16; lighting.Period = record[10] << 8 | record[7]; lighting.Direction = record[8]; break;
            case 0x0B: lighting.Effect = 0x17; lighting.Period = record[5] << 8 | record[6]; break;
            default: return null;
        }
        if (lighting.Effect != 0x01 && lighting.Effect != 0x0A && lighting.Effect != 0x17) lighting.Color = 0xFFFFFF;
        var dim = profile.Skip(Records + 11 * (2 + keys)).Take(11).ToArray();
        lighting.Dim = dim[0] == record[0] && Level(record) > 0 ? Math.Min(100, Level(dim) * 100 / Level(record)) : 50;
        return lighting;
    }

    static int Level(byte[] record)
    {
        switch (record[0])
        {
            case 0x03: return record[8];
            case 0x04: return record[9];
            case 0x0A: return record[7];
            default: return Math.Max(record[1], Math.Max(record[2], record[3]));
        }
    }

    // The dimmed records keep the colour and lower the intensity, as the keyboard's own profiles do; Static and Ripple have no
    // intensity, so their colour is scaled.
    static byte[] Dimmed(byte id, byte[] parameters, int percent)
    {
        parameters = (byte[])parameters.Clone();
        switch (id)
        {
            case 0x03: parameters[7] = (byte)Math.Max(1, percent); break;
            case 0x04: parameters[8] = (byte)Math.Max(1, percent); break;
            case 0x0A: parameters[6] = (byte)Math.Max(1, percent); break;
            default: for (int i = 0; i < 3; i++) parameters[i] = (byte)(parameters[i] * percent / 100); break;
        }
        return parameters;
    }

    public static byte[] Build(byte[] profile, List<byte[]> clusters, Lighting lighting, Lighting others, bool idleTimes)
    {
        if (clusters.Count != 2) throw new RefusedException("Saving isn't supported on this keyboard (" + clusters.Count + " lighting clusters)");
        if (idleTimes && (lighting.Idle <= 0 || lighting.Sleep <= 0)) throw new RefusedException("This keyboard can't keep its lighting on forever: 0 dims and turns it off at once");
        var built = (byte[])profile.Clone();
        int keys = Keys(clusters);
        for (int cluster = 0; cluster < 2; cluster++)
        {
            var light = cluster == keys || others == null ? lighting : others;
            int effectIndex = Pick(light.Effect, clusters[cluster], cluster != keys);
            if (effectIndex < 0) throw new RefusedException("This keyboard doesn't have that effect");
            byte id = clusters[cluster][effectIndex];
            var parameters = Encode(id, light);
            built[Records + 11 * cluster] = id;
            parameters.CopyTo(built, Records + 11 * cluster + 1);
            built[Records + 11 * (2 + cluster)] = id;
            Dimmed(id, parameters, idleTimes ? lighting.Dim : 100).CopyTo(built, Records + 11 * (2 + cluster) + 1);
        }
        if (idleTimes)
        {
            built[0x1C] = (byte)lighting.Idle; built[0x1D] = (byte)(lighting.Idle >> 8);
            built[0x1E] = (byte)lighting.Sleep; built[0x1F] = (byte)(lighting.Sleep >> 8);
        }
        MouseProtocol.WriteChecksum(built);
        return built;
    }

    // The lighting and idle times of the factory copy of the profile at `sector` (0101 for 0001 on the G915 TKL), into a copy of
    // `profile`. Only those: a whole factory profile would drop what else the user changed, like G-shift macros.
    public static byte[] FactoryLighting(Hidpp hidpp, int sector, byte[] profile)
    {
        var rom = MouseProtocol.ReadSector(hidpp, 0x100 + sector, profile.Length);
        if (rom.Skip(Records).Take(44).All(value => value == 0xFF) || rom[0x1C] == 0xFF) throw new RefusedException("Couldn't read the keyboard's factory lighting");
        var built = (byte[])profile.Clone();
        Array.Copy(rom, 0x1C, built, 0x1C, 4);
        Array.Copy(rom, Records, built, Records, 44);
        MouseProtocol.WriteChecksum(built);
        return built;
    }

    // Writes the profile (MouseProtocol.WriteSector refuses 0000 and the factory sectors, and reads it back), loads it again and gives
    // the lighting back, so the keyboard runs it, its power save included.
    public static void Store(Hidpp hidpp, int sector, byte[] profile)
    {
        MouseProtocol.WriteSector(hidpp, sector, profile);
        try { hidpp.Call(hidpp.FeatureIndex(0x8100), 3, (byte)(sector >> 8), (byte)sector); }
        catch (HidppError) { }
        Release(hidpp);
    }

    public static void Preview(Hidpp hidpp, List<byte[]> clusters, Lighting lighting, Lighting others = null)
    {
        byte feature = hidpp.FeatureIndex(0x8071);
        int keys = Keys(clusters);
        if (Pick(lighting.Effect, clusters[keys], false) < 0) throw new RefusedException("This keyboard doesn't have that effect");
        for (int cluster = 0; cluster < clusters.Count; cluster++)
        {
            var light = cluster == keys || others == null ? lighting : others;
            int effectIndex = Pick(light.Effect, clusters[cluster], cluster != keys);
            if (effectIndex < 0) continue;
            hidpp.Call(feature, 1, new byte[] { (byte)cluster, (byte)effectIndex }.Concat(Encode(clusters[cluster][effectIndex], light)).Concat(new byte[] { 0x01 }).ToArray());
        }
    }
}
