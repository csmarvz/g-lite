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
    public HidppError(int code, string msg) : base(msg) { Code = code; }
}

class Refused : Exception
{
    public Refused(string msg) : base(msg) { }
}

class HalfWritten : Refused
{
    public HalfWritten(string msg) : base(msg) { }
}

class Hidpp : IDisposable
{
    const byte Swid = 0x0B;
    readonly byte Idx = 1;
    FileStream fs, shortStream;
    byte[] rbuf = new byte[20], shortBuffer = new byte[7];
    Task<int> pending, shortPending;
    Dictionary<int, byte> features = new Dictionary<int, byte>();
    public string Who;

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr sec, uint disp, uint flags, IntPtr tmpl);

    protected Hidpp(bool open) { }

    public Hidpp(string path, byte index)
    {
        Idx = index;
        var h = CreateFile(path, 0xC0000000, 3, IntPtr.Zero, 3, 0x40000000, IntPtr.Zero);
        if (h.IsInvalid) throw new IOException("Can't open the device (error " + Marshal.GetLastWin32Error() + ")");
        fs = new FileStream(h, FileAccess.ReadWrite, 1, true);
    }

    public Hidpp(string path, byte index, string shortPath) : this(path, index)
    {
        if (shortPath == null) return;
        var handle = CreateFile(shortPath, 0xC0000000, 3, IntPtr.Zero, 3, 0x40000000, IntPtr.Zero);
        if (!handle.IsInvalid) shortStream = new FileStream(handle, FileAccess.ReadWrite, 1, true);
    }

    public virtual void Dispose() { fs.Dispose(); if (shortStream != null) shortStream.Dispose(); }

    public byte[] Register(byte subId, byte register, params byte[] parameters)
    {
        bool isSet = subId == 0x80 || subId == 0x82;
        try
        {
            var reply = SendRegister(subId, register, parameters);
            if (isSet && Who != null) Log.Register(Who, register, parameters, "ok");
            return reply;
        }
        catch (HidppError e) { if (isSet && Who != null) Log.Register(Who, register, parameters, "error " + e.Code.ToString("X2")); throw; }
        catch (TimeoutException) { if (Who != null) Log.Register(Who, register, parameters, "no answer"); throw; }
    }

    protected virtual byte[] SendRegister(byte subId, byte register, byte[] parameters)
    {
        if (shortStream == null) throw new NotSupportedException("The device has no HID++ 1.0 channel");
        bool isLong = subId == 0x82;
        var message = new byte[isLong ? 20 : 7];
        message[0] = isLong ? (byte)0x11 : (byte)0x10;
        message[1] = Idx;
        message[2] = subId;
        message[3] = register;
        Array.Copy(parameters, 0, message, 4, Math.Min(parameters.Length, message.Length - 4));
        (isLong ? fs : shortStream).Write(message, 0, message.Length);
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (true)
        {
            var reply = ReadBoth(deadline);
            if (reply == null) throw new TimeoutException("The device didn't answer. Is it on?");
            if (reply[1] != Idx) continue;
            if (reply[2] == 0x8F && reply[3] == subId && reply[4] == register)
                throw new HidppError(reply[5], string.Format("HID++ 1.0 error 0x{0:x2} (register {1:x2})", reply[5], register));
            if (reply[2] == subId && reply[3] == register) return reply.Skip(4).ToArray();
        }
    }

    public void Upload(byte subId, byte sequence, byte[] data)
    {
        try
        {
            SendUpload(subId, sequence, data);
            if (Who != null) Log.Upload(Who, subId, sequence, "ok");
        }
        catch (HidppError e) { if (Who != null) Log.Upload(Who, subId, sequence, "error " + e.Code.ToString("X2")); throw; }
        catch (TimeoutException) { if (Who != null) Log.Upload(Who, subId, sequence, "no answer"); throw; }
    }

    protected virtual void SendUpload(byte subId, byte sequence, byte[] data)
    {
        if (shortStream == null) throw new NotSupportedException("The device has no HID++ 1.0 channel");
        var message = new byte[20];
        message[0] = 0x11;
        message[1] = Idx;
        message[2] = subId;
        message[3] = sequence;
        Array.Copy(data, 0, message, 4, Math.Min(data.Length, 16));
        fs.Write(message, 0, message.Length);
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (true)
        {
            var reply = ReadBoth(deadline);
            if (reply == null) throw new TimeoutException("The mouse didn't take the upload");
            if (reply[1] != Idx) continue;
            if (reply[2] == 0x8F && reply[3] == subId) throw new HidppError(reply[5], string.Format("HID++ 1.0 error 0x{0:x2} (upload)", reply[5]));
            if (reply[2] != 0x50 || reply[4] != sequence) continue;
            if (reply[3] != 0x01) throw new HidppError(reply[3], string.Format("The mouse refused the upload (0x{0:x2})", reply[3]));
            return;
        }
    }

    byte[] ReadBoth(DateTime deadline)
    {
        if (pending == null) pending = fs.ReadAsync(rbuf, 0, 20);
        if (shortPending == null) shortPending = shortStream.ReadAsync(shortBuffer, 0, 7);
        var ms = (int)(deadline - DateTime.UtcNow).TotalMilliseconds;
        if (ms <= 0) return null;
        int which = Task.WaitAny(new Task[] { pending, shortPending }, ms);
        if (which < 0) return null;
        var done = which == 0 ? pending : shortPending;
        if (which == 0) pending = null; else shortPending = null;
        if (done.IsFaulted || done.IsCanceled) throw new IOException("The device stopped answering. Was it unplugged?");
        if (which == 0) return (byte[])rbuf.Clone();
        var reply = new byte[20];
        Array.Copy(shortBuffer, reply, 7);
        return reply;
    }

    public byte[] Call(byte feat, int fn, params byte[] p)
    {
        if (Who == null || feat == 0) return Send(feat, fn, p);
        int id = features.Where(x => x.Value == feat).Select(x => x.Key).FirstOrDefault();
        bool read = Log.Reads(id, fn);
        try
        {
            var r = Send(feat, fn, p);
            if (!read) Log.Sent(Who, id, fn, p, "ok");
            return r;
        }
        catch (HidppError e) { if (!read) Log.Sent(Who, id, fn, p, "error " + e.Code.ToString("X2")); throw; }
        catch (TimeoutException) { Log.Sent(Who, id, fn, p, "no answer"); throw; }
    }

    public byte[] Quiet(byte feat, int fn, params byte[] p) { return Send(feat, fn, p); }

    protected virtual byte[] Send(byte feat, int fn, byte[] p)
    {
        var msg = new byte[20];
        msg[0] = 0x11; msg[1] = Idx; msg[2] = feat; msg[3] = (byte)(fn << 4 | Swid);
        Array.Copy(p, 0, msg, 4, p.Length);
        fs.Write(msg, 0, 20);
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (true)
        {
            var r = Read(deadline);
            if (r == null) throw new TimeoutException("The device didn't answer. Is it on?");
            if (r[1] != Idx) continue;
            if (r[2] == 0xFF && r[3] == feat && r[4] == msg[3])
                throw new HidppError(r[5], string.Format("HID++ error 0x{0:x2} (feature {1}, fn {2})", r[5], feat, fn));
            if (r[2] == feat && r[3] == msg[3]) return r.Skip(4).ToArray();
        }
    }

    byte[] Read(DateTime deadline)
    {
        if (pending == null) pending = fs.ReadAsync(rbuf, 0, 20);
        var ms = (int)(deadline - DateTime.UtcNow).TotalMilliseconds;
        if (ms <= 0 || !pending.Wait(ms)) return null;
        pending = null;
        return (byte[])rbuf.Clone();
    }

    public byte F(int id)
    {
        byte i;
        if (!features.TryGetValue(id, out i))
        {
            i = Call(0, 0, (byte)(id >> 8), (byte)id)[0];
            if (i == 0) throw new NotSupportedException(string.Format("The device lacks feature {0:x4}", id));
            features[id] = i;
        }
        return i;
    }
}

class EffectDef
{
    public byte Id;
    public string Name;
    public bool Color, Speed, Direction;
    public int MinP, MaxP, DefP;
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
        var v = text.Split(',');
        brightness = int.Parse(v[7]);
        return new Lighting
        {
            Effect = byte.Parse(v[0]), Color = int.Parse(v[1], System.Globalization.NumberStyles.HexNumber), Period = int.Parse(v[2]),
            Direction = int.Parse(v[3]), Dim = int.Parse(v[4]), Idle = int.Parse(v[5]), Sleep = int.Parse(v[6]),
        };
    }
    public bool Same(Lighting o)
    {
        return o != null && Effect == o.Effect && Color == o.Color && Period == o.Period && Direction == o.Direction
            && Dim == o.Dim && Idle == o.Idle && Sleep == o.Sleep;
    }
}

static class G515
{
    public static readonly EffectDef[] Effects =
    {
        new EffectDef { Id = 0x00, Name = "Off" },
        new EffectDef { Id = 0x01, Name = "Static", Color = true },
        new EffectDef { Id = 0x0A, Name = "Breathe", Color = true, Speed = true, MinP = 1000, MaxP = 20000, DefP = 5000 },
        new EffectDef { Id = 0x15, Name = "Cycle", Speed = true, MinP = 1000, MaxP = 20000, DefP = 5000 },
        new EffectDef { Id = 0x16, Name = "Wave", Speed = true, Direction = true, MinP = 1000, MaxP = 20000, DefP = 5000 },
        new EffectDef { Id = 0x17, Name = "Ripple", Color = true, Speed = true, MinP = 2, MaxP = 200, DefP = 20 },
    };

    public static EffectDef Def(byte id) { return Effects.FirstOrDefault(e => e.Id == id); }

    static int Scale(int c, int pct)
    {
        return ((c >> 16 & 0xFF) * pct / 100) << 16 | ((c >> 8 & 0xFF) * pct / 100) << 8 | (c & 0xFF) * pct / 100;
    }

    public static byte[] Encode(Lighting l, int pct)
    {
        var b = new byte[10];
        int c = Scale(l.Color, pct), inten = Math.Max(1, pct);
        Action rgb = () => { b[0] = (byte)(c >> 16); b[1] = (byte)(c >> 8); b[2] = (byte)c; };
        switch (l.Effect)
        {
            case 0x01: rgb(); b[3] = 0x02; break;
            case 0x0A: rgb(); b[3] = (byte)(l.Period >> 8); b[4] = (byte)l.Period; b[6] = (byte)inten; break;
            case 0x15:
            case 0x16:
                b[1] = 0xFF; b[6] = (byte)(l.Period >> 8); b[7] = (byte)l.Period; b[8] = (byte)inten;
                if (l.Effect == 0x16) b[9] = (byte)l.Direction;
                break;
            case 0x17: rgb(); b[3] = 0xFF; b[6] = (byte)(l.Period >> 8); b[7] = (byte)l.Period; break;
        }
        return b;
    }

    static int Rgb(byte[] r, int o) { return r[o] << 16 | r[o + 1] << 8 | r[o + 2]; }
    static int Max3(int c) { return Math.Max(c >> 16 & 0xFF, Math.Max(c >> 8 & 0xFF, c & 0xFF)); }
    static int U16(byte[] b, int o) { return b[o] << 8 | b[o + 1]; }
    static void Put16(byte[] b, int o, int v) { b[o] = (byte)(v >> 8); b[o + 1] = (byte)v; }
    static void Put32(byte[] b, int o, uint v) { b[o] = (byte)(v >> 24); b[o + 1] = (byte)(v >> 16); b[o + 2] = (byte)(v >> 8); b[o + 3] = (byte)v; }

    // Returns null when the profile holds an effect this app doesn't know (the factory animation).
    public static Lighting Decode(byte[] p)
    {
        var rec = p.Skip(30 + ((p[74] & 1) != 0 ? 0 : 11)).Take(11).ToArray();
        var dim = p.Skip(30 + ((p[74] & 2) != 0 ? 22 : 33)).Take(11).ToArray();
        var def = Def(rec[0]);
        if (def == null) return null;
        var l = new Lighting { Effect = rec[0] };
        int idle = U16(p, 2), sleep = U16(p, 4);
        l.Idle = idle == 0xFFFF ? 0 : idle;
        l.Sleep = sleep == 0xFFFF ? 0 : sleep;
        if (def.Color) l.Color = Rgb(rec, 1);
        if (def.Speed) l.Period = Math.Min(def.MaxP, Math.Max(def.MinP, rec[0] == 0x0A ? U16(rec, 4) : U16(rec, 7)));
        if (def.Direction) l.Direction = rec[10];
        if (dim[0] == rec[0])
        {
            if (def.Color && Max3(l.Color) > 0) l.Dim = (Max3(Rgb(dim, 1)) * 100 + Max3(l.Color) / 2) / Max3(l.Color);
            else if (rec[0] == 0x15 || rec[0] == 0x16) l.Dim = dim[9];
        }
        return l;
    }

    public static byte[] BuildProfile(byte[] baseProfile, Lighting l)
    {
        var p = (byte[])baseProfile.Clone();
        Put16(p, 2, l.Idle <= 0 ? 0xFFFF : l.Idle);
        Put16(p, 4, l.Sleep <= 0 ? 0xFFFF : l.Sleep);
        Array.Clear(p, 30, 44);
        p[41] = l.Effect; Array.Copy(Encode(l, 100), 0, p, 42, 10);
        p[63] = l.Effect; Array.Copy(Encode(l, l.Dim), 0, p, 64, 10);
        p[74] = 0;
        return p;
    }

    public static byte[] BuildDirectory(byte[] factoryDir, byte[] profile)
    {
        var d = (byte[])factoryDir.Clone();
        Put32(d, 10, Crc32(profile));
        d[14] = 8;
        Put32(d, 0, Crc32(d.Skip(4).ToArray()));
        return d;
    }

    public static uint Crc32(byte[] data)
    {
        uint c = 0xFFFFFFFF;
        foreach (var x in data)
        {
            c ^= x;
            for (int k = 0; k < 8; k++) c = (c >> 1) ^ (0xEDB88320 & (uint)-(int)(c & 1));
        }
        return ~c;
    }

    static readonly byte[] DirHeader = { 0x01, 0x01, 0x81, 0x01, 0x40, 0x63 };

    public static bool ValidProfile(byte[] p) { return p != null && p.Length == 99 && p[0] == 1 && p[1] == 1; }

    static bool ValidDir(byte[] d)
    {
        return d != null && d.Length == 16 && d.Skip(4).Take(6).SequenceEqual(DirHeader) && d[15] == 0xFF
            && Crc32(d.Skip(4).ToArray()) == (uint)(d[0] << 24 | d[1] << 16 | d[2] << 8 | d[3]);
    }

    // ---- device operations ----

    static byte[] ReadBuffer(Hidpp h, int size)
    {
        var o = new List<byte>();
        for (int off = 0; off < size; off += 16) o.AddRange(h.Call(h.F(0x8101), 0xC, (byte)(off >> 8), (byte)off));
        return o.Take(size).ToArray();
    }

    static byte[] ReadFile(Hidpp h, int addr, int size)
    {
        try { h.Call(h.F(0x8101), 8, (byte)(addr >> 8), (byte)addr, (byte)(size >> 8), (byte)size); }
        catch (HidppError e) { if (e.Code == 4 && addr < 0x0100) return null; throw; }
        return ReadBuffer(h, size);
    }

    static void SaveFile(Hidpp h, int addr, byte[] data, uint checksum)
    {
        if (addr != 0x0000 && addr != 0x0008) throw new InvalidOperationException("refusing to write factory storage");
        byte f = h.F(0x8101);
        h.Call(f, 2, 0, (byte)data.Length);
        for (int o = 0; o < data.Length; o += 16) h.Call(f, 3, data.Skip(o).Take(16).ToArray());
        if (!ReadBuffer(h, data.Length).SequenceEqual(data)) throw new IOException("Staging buffer mismatch, nothing written");
        var c = new byte[4]; Put32(c, 0, checksum);
        h.Call(f, 7, (byte)(addr >> 8), (byte)addr, 0, (byte)data.Length, c[0], c[1], c[2], c[3]);
        if (!data.SequenceEqual(ReadFile(h, addr, data.Length) ?? new byte[0])) throw new IOException("Flash readback mismatch");
    }

    public class Files { public byte[] FactoryProfile, FactoryDir, UserProfile, UserDir; }

    public static Files Read(Hidpp h)
    {
        return new Files
        {
            FactoryProfile = ReadFile(h, 0x0101, 99),
            FactoryDir = ReadFile(h, 0x0100, 16),
            UserProfile = ReadFile(h, 0x0008, 99),
            UserDir = ReadFile(h, 0x0000, 16),
        };
    }

    public static void Check(Files f)
    {
        if (!ValidProfile(f.FactoryProfile) || !ValidDir(f.FactoryDir)) throw new Refused("Unknown onboard profile layout, not touching it");
        if (f.UserProfile == null && f.UserDir == null) return;
        bool ours = f.UserDir == null || (ValidDir(f.UserDir) && f.UserDir[14] == 8);
        if (ours && (f.UserProfile == null || f.UserDir == null)) throw new HalfWritten("User profile is half written, not touching it");
        if (!ours || !ValidProfile(f.UserProfile))
            throw new Refused("The profiles saved on the keyboard aren't laid out the way G-Lite writes them, not touching them");
        if (Crc32(f.UserProfile) != (uint)(f.UserDir[10] << 24 | f.UserDir[11] << 16 | f.UserDir[12] << 8 | f.UserDir[13]))
            throw new HalfWritten("User profile doesn't match its directory entry, not touching it");
    }

    public static bool Good(Files f)
    {
        try { Check(f); return true; } catch (Refused) { return false; }
    }

    public static Files Snapshot(Hidpp h)
    {
        var f = Read(h);
        Check(f);
        return f;
    }

    public static int Brightness(Hidpp h) { var r = h.Call(h.F(0x8040), 1); return U16(r, 0); }
    public static void SetBrightness(Hidpp h, int v) { h.Call(h.F(0x8040), 2, (byte)(v >> 8), (byte)v); }

    public static void Claim(Hidpp h)
    {
        h.Call(h.F(0x8101), 6, 0x05);
        h.Call(h.F(0x8071), 5, 0x01, 0x03, 0x04);
    }

    public static void Release(Hidpp h)
    {
        h.Call(h.F(0x8071), 5, 0x01, 0x00, 0x00);
        h.Call(h.F(0x8101), 6, 0x03);
    }

    public static void Preview(Hidpp h, Lighting l) { Preview(h, l, 100); }

    public static void Preview(Hidpp h, Lighting l, int level)
    {
        byte rgb = h.F(0x8071);
        int index = -1;
        for (int e = 0; e < 16 && index < 0; e++)
            if (h.Call(rgb, 0, 0, (byte)e, 0)[3] == l.Effect) index = e;
        if (index < 0) throw new NotSupportedException("Effect not supported by this keyboard");
        h.Call(rgb, 1, new byte[] { 0, (byte)index }.Concat(Encode(l, level)).Concat(new byte[] { 0x01 }).ToArray());
    }

    public static void Store(Hidpp h, Lighting l) { Store(h, Snapshot(h), l); }

    public static void Store(Hidpp h, Files f, Lighting l)
    {
        var p = BuildProfile(f.UserProfile ?? f.FactoryProfile, l);
        Put(h, f, p, BuildDirectory(f.FactoryDir, p));
    }

    public static void Restore(Hidpp h, Files backup)
    {
        var now = Read(h);
        if (!ValidProfile(now.FactoryProfile) || !ValidDir(now.FactoryDir)) throw new Refused("Unknown onboard profile layout, not touching it");
        if (backup.FactoryProfile != null && (!backup.FactoryProfile.SequenceEqual(now.FactoryProfile) || !backup.FactoryDir.SequenceEqual(now.FactoryDir)))
            throw new Refused("This backup is from a different keyboard, not restoring it");
        if (backup.UserProfile == null)
        {
            var p = (byte[])now.FactoryProfile.Clone();
            Put(h, now, p, BuildDirectory(now.FactoryDir, p));
            return;
        }
        Check(new Files { FactoryProfile = now.FactoryProfile, FactoryDir = now.FactoryDir, UserProfile = backup.UserProfile, UserDir = backup.UserDir });
        Put(h, now, backup.UserProfile, backup.UserDir);
    }

    static void Put(Hidpp h, Files now, byte[] p, byte[] d)
    {
        if (now.UserProfile == null || !p.SequenceEqual(now.UserProfile)) SaveFile(h, 0x0008, p, Crc32(p));
        if (now.UserDir == null || !d.SequenceEqual(now.UserDir)) SaveFile(h, 0x0000, d, Crc32(d.Skip(4).ToArray()));
        Activate(h, p);
    }

    public static void Activate(Hidpp h, byte[] p)
    {
        h.Call(h.F(0x8071), 5, 0x01, 0x00, 0x00);
        byte f = h.F(0x8101);
        h.Call(f, 6, 0x0B);
        var cfg = new byte[] { 0x81, 0x01, 0x01, 0x00, 0x01, 0x00, 0x63, 0, 0, 0, 0 };
        Put32(cfg, 7, Crc32(p));
        h.Call(f, 9, cfg);
        var m = h.Call(f, 6, 0x00);
        if ((m[0] & 1) == 0 || m[1] != 0 || m[2] != 1) throw new IOException(string.Format("Unexpected state {0:x2} {1:x2} {2:x2} after activating", m[0], m[1], m[2]));
    }
}

static class G915
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

    public static List<byte[]> Clusters(Hidpp h)
    {
        byte f = h.F(0x8071);
        var all = new List<byte[]>();
        for (int c = 0; c < 8; c++)
        {
            var ids = new List<byte>();
            for (int e = 0; e < 32; e++)
            {
                try { ids.Add(h.Call(f, 0, (byte)c, (byte)e, 0)[3]); }
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
        int k = 0;
        for (int c = 1; c < clusters.Count; c++) if (clusters[c].Length > clusters[k].Length) k = c;
        return k;
    }

    public static int Pick(byte want, byte[] has, bool loose)
    {
        while (true)
        {
            byte[] ids;
            if (Alike.TryGetValue(want, out ids))
                foreach (var id in ids)
                {
                    int i = Array.IndexOf(has, id);
                    if (i >= 0) return i;
                }
            if (!loose || !Instead.TryGetValue(want, out want)) return -1;
        }
    }

    public static byte[] Encode(byte id, Lighting l)
    {
        var b = new byte[10];
        int c = l.Color;
        switch (id)
        {
            case 0x03: b[5] = (byte)(l.Period >> 8); b[6] = (byte)l.Period; b[7] = 100; return b;
            case 0x04: b[6] = (byte)l.Period; b[7] = (byte)l.Direction; b[8] = 100; b[9] = (byte)(l.Period >> 8); return b;
            case 0x0B: b[0] = (byte)(c >> 16); b[1] = (byte)(c >> 8); b[2] = (byte)c; b[4] = (byte)(l.Period >> 8); b[5] = (byte)l.Period; return b;
        }
        var s = l.Clone();
        s.Effect = id;
        return G515.Encode(s, 100);
    }

    public static void Claim(Hidpp h) { h.Call(h.F(0x8071), 5, 0x01, 0x03, 0x04); }

    public static void Release(Hidpp h) { h.Call(h.F(0x8071), 5, 0x01, 0x00, 0x00); }

    public static void Preview(Hidpp h, List<byte[]> clusters, Lighting l)
    {
        byte f = h.F(0x8071);
        int keys = Keys(clusters);
        if (Pick(l.Effect, clusters[keys], false) < 0) throw new Refused("This keyboard doesn't have that effect");
        for (int c = 0; c < clusters.Count; c++)
        {
            int i = Pick(l.Effect, clusters[c], c != keys);
            if (i < 0) continue;
            h.Call(f, 1, new byte[] { (byte)c, (byte)i }.Concat(Encode(clusters[c][i], l)).Concat(new byte[] { 0x01 }).ToArray());
        }
    }
}
