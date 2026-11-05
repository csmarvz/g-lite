using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;

class HidppError : Exception
{
    public int Code;
    public HidppError(int code, string msg) : base(msg) { Code = code; }
}

class Hidpp : IDisposable
{
    const byte Idx = 1, Swid = 0x0B;
    FileStream fs;
    byte[] rbuf = new byte[20];
    Task<int> pending;
    Dictionary<int, byte> features = new Dictionary<int, byte>();

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr sec, uint disp, uint flags, IntPtr tmpl);

    protected Hidpp(bool open) { }

    public Hidpp()
    {
        string path = null;
        using (var s = new ManagementObjectSearcher("SELECT DeviceID FROM Win32_PnPEntity WHERE DeviceID LIKE 'HID\\\\VID_046D&PID_C547&MI_02&COL02%'"))
            foreach (ManagementObject o in s.Get())
                path = @"\\?\" + ((string)o["DeviceID"]).Replace('\\', '#') + "#{4d1e55b2-f16f-11cf-88cb-001111000030}";
        if (path == null) throw new IOException("Lightspeed receiver not found");
        var h = CreateFile(path, 0xC0000000, 3, IntPtr.Zero, 3, 0x40000000, IntPtr.Zero);
        if (h.IsInvalid) throw new IOException("Cannot open receiver (error " + Marshal.GetLastWin32Error() + ")");
        fs = new FileStream(h, FileAccess.ReadWrite, 1, true);
    }

    public virtual void Dispose() { fs.Dispose(); }

    public virtual byte[] Call(byte feat, int fn, params byte[] p)
    {
        var msg = new byte[20];
        msg[0] = 0x11; msg[1] = Idx; msg[2] = feat; msg[3] = (byte)(fn << 4 | Swid);
        Array.Copy(p, 0, msg, 4, p.Length);
        fs.Write(msg, 0, 20);
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (true)
        {
            var r = Read(deadline);
            if (r == null) throw new TimeoutException("Keyboard did not answer (is it on?)");
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
            if (i == 0) throw new NotSupportedException(string.Format("Keyboard lacks feature {0:x4}", id));
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
    public bool Same(Lighting o)
    {
        return o != null && Effect == o.Effect && Color == o.Color && Period == o.Period && Direction == o.Direction
            && Dim == o.Dim && Idle == o.Idle && Sleep == o.Sleep;
    }
}

// Protocol notes: github.com/mevdschee/logirgb research/ (0x8071, 0x8101, 0x8040)
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

    static bool ValidProfile(byte[] p) { return p != null && p.Length == 99 && p[0] == 1 && p[1] == 1; }

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

    public static Files Snapshot(Hidpp h)
    {
        var f = new Files
        {
            FactoryProfile = ReadFile(h, 0x0101, 99),
            FactoryDir = ReadFile(h, 0x0100, 16),
            UserProfile = ReadFile(h, 0x0008, 99),
            UserDir = ReadFile(h, 0x0000, 16),
        };
        if (!ValidProfile(f.FactoryProfile) || !ValidDir(f.FactoryDir)) throw new IOException("Unknown onboard profile layout, not touching it");
        if ((f.UserProfile == null) != (f.UserDir == null)) throw new IOException("User profile is half written, not touching it");
        if (f.UserProfile != null && (!ValidProfile(f.UserProfile) || !ValidDir(f.UserDir) || f.UserDir[14] != 8
            || Crc32(f.UserProfile) != (uint)(f.UserDir[10] << 24 | f.UserDir[11] << 16 | f.UserDir[12] << 8 | f.UserDir[13])))
            throw new IOException("User profile doesn't match its directory entry, not touching it");
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

    public static void Preview(Hidpp h, Lighting l)
    {
        byte rgb = h.F(0x8071);
        int index = -1;
        for (int e = 0; e < 16 && index < 0; e++)
            if (h.Call(rgb, 0, 0, (byte)e, 0)[3] == l.Effect) index = e;
        if (index < 0) throw new NotSupportedException("Effect not supported by this keyboard");
        h.Call(rgb, 1, new byte[] { 0, (byte)index }.Concat(Encode(l, 100)).Concat(new byte[] { 0x01 }).ToArray());
    }

    public static void Store(Hidpp h, Lighting l)
    {
        var f = Snapshot(h);
        var p = BuildProfile(f.UserProfile ?? f.FactoryProfile, l);
        var d = BuildDirectory(f.FactoryDir, p);
        if (f.UserProfile == null || !p.SequenceEqual(f.UserProfile)) SaveFile(h, 0x0008, p, Crc32(p));
        if (f.UserDir == null || !d.SequenceEqual(f.UserDir)) SaveFile(h, 0x0000, d, Crc32(d.Skip(4).ToArray()));
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
