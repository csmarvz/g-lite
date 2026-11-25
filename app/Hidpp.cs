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
    readonly HashSet<int> hiddenFeatures = new HashSet<int>();
    public string DeviceLabel;
    public static Action<string> Tap;
    readonly string path;
    int featureLength;

    [DllImport("hid.dll", SetLastError = true)] static extern bool HidD_GetFeature(SafeFileHandle handle, byte[] buffer, int length);
    [DllImport("hid.dll", SetLastError = true)] static extern bool HidD_SetFeature(SafeFileHandle handle, byte[] buffer, int length);
    [DllImport("hid.dll", SetLastError = true)] static extern bool HidD_GetInputReport(SafeFileHandle handle, byte[] buffer, int length);
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
    static extern SafeFileHandle CreateFile(
        string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);

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
        catch (HidppError ex)
        {
            if (isSet && DeviceLabel != null) Log.Register(DeviceLabel, register, parameters, "error " + ex.Code.ToString("X2"));
            throw;
        }
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
        catch (HidppError ex)
        {
            if (DeviceLabel != null) Log.Upload(DeviceLabel, subId, sequence, "error " + ex.Code.ToString("X2"));
            throw;
        }
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
            if (reply[2] == 0x8F && reply[3] == subId)
                throw new HidppError(reply[5], string.Format("HID++ 1.0 error 0x{0:x2} (upload)", reply[5]));
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
        if (DeviceLabel == null || featureIndex == 0) return SendRetryingBusy(featureIndex, fn, parameters);
        int id = features.Where(pair => pair.Value == featureIndex).Select(pair => pair.Key).FirstOrDefault();
        bool read = Log.Reads(id, fn);
        try
        {
            var reply = SendRetryingBusy(featureIndex, fn, parameters);
            if (!read) Log.Sent(DeviceLabel, id, fn, parameters, "ok");
            return reply;
        }
        catch (HidppError ex) { Log.Sent(DeviceLabel, id, fn, parameters, "error " + ex.Code.ToString("X2")); throw; }
        catch (TimeoutException) { Log.Sent(DeviceLabel, id, fn, parameters, "no answer"); throw; }
    }

    // Error 8, busy, is tried again after 30, 60 and 90 ms, as Solaar does (PeriHub). Not for CallUnlogged: live frames have their
    // own pacing (KeyLights.ShowFrame).
    byte[] SendRetryingBusy(byte featureIndex, int fn, byte[] parameters)
    {
        for (int attempt = 1; ; attempt++)
            try { return Send(featureIndex, fn, parameters); }
            catch (HidppError ex)
            {
                if (ex.Code != 8 || attempt > 3) throw;
                System.Threading.Thread.Sleep(30 * attempt);
            }
    }

    public byte[] CallUnlogged(byte featureIndex, int fn, params byte[] parameters)
    {
        Record(featureIndex, fn, parameters);
        return Send(featureIndex, fn, parameters);
    }

    // For a call the device doesn't answer, like 0x1814 fn 1: it switches to another computer first.
    public void SendWithoutReply(byte featureIndex, int fn, params byte[] parameters)
    {
        Record(featureIndex, fn, parameters);
        Write(featureIndex, fn, parameters);
        if (DeviceLabel != null)
            Log.Sent(DeviceLabel, features.Where(pair => pair.Value == featureIndex).Select(pair => pair.Key).FirstOrDefault(),
                fn, parameters, "sent");
    }

    // For a stream of calls whose replies aren't waited for (0x8080's key colours): tapped for traces, kept out of the log. A later
    // call reads past their replies, and an error for one, which carries its own software id, is skipped.
    public void PostUnlogged(byte featureIndex, int fn, params byte[] parameters)
    {
        Record(featureIndex, fn, parameters);
        Write(featureIndex, fn, parameters);
    }

    protected virtual void Write(byte featureIndex, int fn, byte[] parameters) { Message(featureIndex, fn, parameters); }

    byte[] Message(byte featureIndex, int fn, byte[] parameters)
    {
        var message = new byte[20];
        message[0] = 0x11; message[1] = DeviceIndex; message[2] = featureIndex;
        message[3] = (byte)(fn << 4 | 1 + (System.Threading.Interlocked.Increment(ref calls) & 0x7FFFFFFF) % 12);
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
            // Over Bluetooth a device may answer a request to 0xFF at index 0x00 (Solaar, via PeriHub; untried). The feature and
            // software id below still have to match.
            if (reply[1] != DeviceIndex && !(Direct && reply[1] == 0x00)) continue;
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

    // Feature reports, for the devices that don't speak HID++ (the G510). Each call opens the collection without overlapped I/O,
    // which HidD_GetFeature and HidD_SetFeature need.
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
            if (handle.IsInvalid || !HidD_GetFeature(handle, buffer, buffer.Length))
                throw new IOException(
                    "Couldn't read feature report " + id.ToString("X2") + " (error " + Marshal.GetLastWin32Error() + ")");
            return buffer;
        }
    }

    protected virtual void WriteFeature(byte[] report)
    {
        using (var handle = CreateFile(path, 0xC0000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero))
        {
            var buffer = new byte[Math.Max(report.Length, FeatureLength(handle))];
            Array.Copy(report, buffer, report.Length);
            if (handle.IsInvalid || !HidD_SetFeature(handle, buffer, buffer.Length))
                throw new IOException(
                    "Couldn't set feature report " + report[0].ToString("X2") + " (error " + Marshal.GetLastWin32Error() + ")");
        }
    }

    // One input report, asked for (GET_REPORT), not listened to: the G510's Game mode switch.
    public byte[] GetInput(byte id)
    {
        if (Tap != null) Tap("get input " + id.ToString("X2"));
        return ReadInput(id);
    }

    protected virtual byte[] ReadInput(byte id)
    {
        using (var handle = CreateFile(path, 0xC0000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero))
        {
            IntPtr data;
            int length = 1;
            if (!handle.IsInvalid && HidD_GetPreparsedData(handle, out data))
            {
                var caps = new short[HidCaps.Length];
                HidP_GetCaps(data, caps);
                HidD_FreePreparsedData(data);
                length = Math.Max(1, (int)caps[HidCaps.InputLength]);
            }
            var buffer = new byte[length];
            buffer[0] = id;
            if (handle.IsInvalid || !HidD_GetInputReport(handle, buffer, buffer.Length))
                throw new IOException("Couldn't read input report " + id.ToString("X2") + " (error " + Marshal.GetLastWin32Error() + ")");
            return buffer;
        }
    }

    int FeatureLength(SafeFileHandle handle)
    {
        if (featureLength > 0 || handle.IsInvalid) return Math.Max(1, featureLength);
        IntPtr data;
        if (!HidD_GetPreparsedData(handle, out data)) return 1;
        var caps = new short[HidCaps.Length];
        HidP_GetCaps(data, caps);
        HidD_FreePreparsedData(data);
        featureLength = caps[HidCaps.FeatureLength];
        return Math.Max(1, featureLength);
    }

    public byte FeatureIndex(int id)
    {
        byte index;
        if (!features.TryGetValue(id, out index))
        {
            var reply = Call(0, 0, (byte)(id >> 8), (byte)id);
            features[id] = index = reply[0];
            if ((reply[1] & 0x60) == 0x60) hiddenFeatures.Add(id);
        }
        if (index == 0) throw new NotSupportedException(string.Format("The device lacks feature {0:x4}", id));
        return index;
    }

    // Flagged hidden and internal by the device (root fn 0, byte 1 bits 6 and 5): meant for Logitech's own tools, not for users.
    // Hidden alone isn't enough, since a feature that works can carry it. Known after FeatureIndex.
    public bool Hidden(int id) { return hiddenFeatures.Contains(id); }
}
