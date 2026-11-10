using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

class MouseLight
{
    public byte Effect = 0x01;
    public int Color = 0xFFFFFF, Period = 5000, Bright = 100;
    public MouseLight Clone() { return (MouseLight)MemberwiseClone(); }

    public bool Same(MouseLight o)
    {
        return o != null && Effect == o.Effect && Color == o.Color && Period == o.Period && Bright == o.Bright;
    }
}

class MouseSet
{
    public int Dpi = 800, Rate, Stage = -1;
    public MouseLight Light;

    public MouseSet Clone() { return new MouseSet { Dpi = Dpi, Rate = Rate, Stage = Stage, Light = Light == null ? null : Light.Clone() }; }

    public string ToText()
    {
        var l = Light ?? new MouseLight();
        return string.Join(",", Dpi, l.Effect, l.Color.ToString("X6"), l.Period, l.Bright, Rate);
    }

    public static MouseSet FromText(string text)
    {
        var v = text.Split(',');
        return new MouseSet
        {
            Dpi = int.Parse(v[0]),
            Light = new MouseLight { Effect = byte.Parse(v[1]), Color = int.Parse(v[2], NumberStyles.HexNumber), Period = int.Parse(v[3]), Bright = int.Parse(v[4]) },
            Rate = v.Length > 5 ? int.Parse(v[5]) : 0,
        };
    }
}

static class GMouse
{
    public static readonly EffectDef[] Effects =
    {
        new EffectDef { Id = 0x00, Name = "Off" },
        new EffectDef { Id = 0x01, Name = "Static", Color = true },
        new EffectDef { Id = 0x0A, Name = "Breathe", Color = true, Speed = true, MinP = 1000, MaxP = 20000, DefP = 5000 },
        new EffectDef { Id = 0x03, Name = "Cycle", Speed = true, MinP = 1000, MaxP = 20000, DefP = 8000 },
    };

    public static EffectDef Def(byte id) { return Effects.FirstOrDefault(e => e.Id == id); }

    public class State
    {
        public int Dpi, Stage = -1, Rate;
        public int[] Dpis = new int[0], Stages = new int[0], Rates = new int[0];
        public bool Onboard, ProfileOk, Flash, Fresh;
        public MouseLight Light;
        public EffectDef[] Effects = GMouse.Effects;
        public Dictionary<int, Dictionary<int, int>> Zones = new Dictionary<int, Dictionary<int, int>>();
        public int Sector = -1, SectorSize, Format;
        public byte[] Current, Profile;

        public bool Writable
        {
            get
            {
                if (Flash) return Sector >= 2 && Sector < 32 && SectorSize == 512 && Current != null;
                return Onboard && Format == 3 && SectorSize >= 0xF0 && (Sector > 0 && Sector < 0x100 || Fresh);
            }
        }
        public bool Savable { get { return Writable && ProfileOk; } }
        public string Key { get { return Sector.ToString("X4"); } }
    }

    public const int RomSector = 0x0101;

    public static ushort Crc(byte[] d, int n)
    {
        int c = 0xFFFF;
        for (int i = 0; i < n; i++)
        {
            c ^= d[i] << 8;
            for (int k = 0; k < 8; k++) c = (c & 0x8000) != 0 ? (c << 1 ^ 0x1021) & 0xFFFF : c << 1 & 0xFFFF;
        }
        return (ushort)c;
    }

    public static bool Valid(byte[] p)
    {
        return p != null && p.Length >= 0xF0 && Crc(p, p.Length - 2) == U16(p, p.Length - 2);
    }

    public static void Seal(byte[] p)
    {
        int c = Crc(p, p.Length - 2);
        p[p.Length - 2] = (byte)(c >> 8);
        p[p.Length - 1] = (byte)c;
    }

    public static int[] StagesOf(byte[] p)
    {
        return Enumerable.Range(0, 5).Select(i => p[3 + 2 * i] | p[4 + 2 * i] << 8).TakeWhile(x => x != 0 && x != 0xFFFF).ToArray();
    }

    public static string Describe(byte[] p)
    {
        if (!Valid(p)) return "damaged, can't be restored";
        var stages = StagesOf(p);
        var l = Decode(p.Skip(0xDB).Take(11).ToArray());
        return (p[1] < stages.Length ? stages[p[1]] + " DPI" : "DPI stages") + " · " + (l == null ? "lighting" : Def(l.Effect).Name) + " · " + (p[0] > 0 ? 1000 / p[0] + " Hz" : "");
    }

    public static byte[] Build(byte[] old, MouseSet want, int zones)
    {
        var p = (byte[])old.Clone();
        var stages = StagesOf(p);
        if (stages.Length == 0) throw new Refused("The profile on the mouse has no DPI stages, not touching it");
        int i = want.Stage >= 0 && want.Stage < stages.Length ? want.Stage : Array.IndexOf(stages, want.Dpi);
        if (i < 0) i = Math.Min((int)p[1], stages.Length - 1);
        p[3 + 2 * i] = (byte)want.Dpi;
        p[4 + 2 * i] = (byte)(want.Dpi >> 8);
        p[1] = (byte)i;
        if (want.Rate > 0) p[0] = (byte)(1000 / want.Rate);
        if (want.Light != null)
            for (int z = 0; z < Math.Min(zones, 2); z++)
            {
                p[0xD0 + 11 * z] = want.Light.Effect;
                Encode(want.Light).CopyTo(p, 0xD1 + 11 * z);
            }
        Seal(p);
        return p;
    }

    public static byte[] Factory(byte[] rom, byte[] current)
    {
        if (rom[0] == 0 || rom[0] > 8 || StagesOf(rom).Length == 0) throw new Refused("Couldn't read the mouse's factory profile, not restoring it (it starts " + BitConverter.ToString(rom, 0, Math.Min(13, rom.Length)).Replace("-", " ") + ")");
        var p = (byte[])rom.Clone();
        if (Valid(current)) Array.Copy(current, 0xA0, p, 0xA0, 0x30);
        Seal(p);
        return p;
    }

    public static byte[] ReadSector(Hidpp h, int sector, int size)
    {
        byte o = h.F(0x8100);
        var p = new byte[size];
        for (int off = 0; off < size; off += 16)
        {
            int at = Math.Min(off, size - 16);
            var r = Memory(h, o, sector, at);
            Array.Copy(r, off - at, p, off, Math.Min(16, size - off));
        }
        return p;
    }

    public static void WriteSector(Hidpp h, int sector, byte[] p)
    {
        if (sector <= 0 || sector >= 0x100) throw new Refused("Refusing to write that part of the mouse's memory");
        if (!Valid(p)) throw new Refused("Refusing to write a profile with a bad checksum");
        Write(h, sector, p);
    }

    static void Write(Hidpp h, int sector, byte[] p)
    {
        byte o = h.F(0x8100);
        h.Call(o, 6, (byte)(sector >> 8), (byte)sector, 0, 0, (byte)(p.Length >> 8), (byte)p.Length);
        for (int i = 0; i < p.Length; i += 16)
        {
            var chunk = new byte[16];
            Array.Copy(p, i, chunk, 0, Math.Min(16, p.Length - i));
            h.Call(o, 7, chunk);
        }
        h.Call(o, 8);
        if (!ReadSector(h, sector, p.Length).SequenceEqual(p)) throw new IOException("The profile on the mouse didn't read back the same after saving");
    }

    public static byte[] Directory(int size, int profiles)
    {
        if (profiles < 1 || profiles > 8 || size < 0xF0) throw new Refused("Refusing to build that profile directory");
        var d = Enumerable.Repeat((byte)0xFF, size).ToArray();
        for (int i = 0; i < profiles; i++) { d[4 * i] = 0; d[4 * i + 1] = (byte)(i + 1); d[4 * i + 2] = (byte)(i == 0 ? 1 : 0); d[4 * i + 3] = 0; }
        d[4 * profiles + 2] = 0;
        d[4 * profiles + 3] = 0;
        Seal(d);
        return d;
    }

    public static void Setup(Hidpp h, State s, byte[] p)
    {
        if (!s.Fresh || s.Format != 3 || p.Length != s.SectorSize) throw new Refused("Saving isn't supported on this mouse");
        WriteSector(h, 1, p);
        var directory = Directory(s.SectorSize, 1);
        Write(h, 0x0000, directory);
        try { h.Call(h.F(0x8100), 3, 0x00, 0x01); }
        catch (HidppError) { }
        if (h.Who != null) Log.Add(h.Who, "sent", "Onboard memory set up: the profile at 0001, built from the factory profile at " + s.Sector.ToString("X4") + ", and the directory at 0000", "ok");
    }

    public static void Store(Hidpp h, State s, byte[] p)
    {
        if (!s.Writable || s.Fresh || p.Length != s.SectorSize) throw new Refused("Saving isn't supported on this mouse");
        WriteSector(h, s.Sector, p);
        try { h.Call(h.F(0x8100), 3, s.Current); }
        catch (HidppError) { }
    }

    static int U16(byte[] b, int o) { return b[o] << 8 | b[o + 1]; }

    public static int[] DpiList(byte[] r)
    {
        var v = new List<int>();
        for (int o = 1; o + 1 < r.Length; o += 2)
        {
            int x = U16(r, o);
            if (x == 0) break;
            if ((x & 0xE000) == 0xE000)
            {
                int step = x & 0x1FFF;
                if (v.Count == 0 || step == 0 || o + 3 >= r.Length) break;
                int end = U16(r, o + 2);
                for (int d = v[v.Count - 1] + step; d < end; d += step) v.Add(d);
                continue;
            }
            v.Add(x);
        }
        return v.ToArray();
    }

    public static int[] RateList(byte bits)
    {
        return Enumerable.Range(0, 8).Where(i => (bits >> i & 1) != 0).Select(i => 1000 / (i + 1)).OrderBy(x => x).ToArray();
    }

    public static byte[] Encode(MouseLight l)
    {
        var b = new byte[10];
        int c = l.Color, k = Math.Max(1, Math.Min(100, l.Bright));
        if (l.Effect == 0x01)
            c = ((c >> 16 & 0xFF) * k / 100) << 16 | ((c >> 8 & 0xFF) * k / 100) << 8 | (c & 0xFF) * k / 100;
        switch (l.Effect)
        {
            case 0x01: b[0] = (byte)(c >> 16); b[1] = (byte)(c >> 8); b[2] = (byte)c; break;
            case 0x0A: b[0] = (byte)(c >> 16); b[1] = (byte)(c >> 8); b[2] = (byte)c; b[3] = (byte)(l.Period >> 8); b[4] = (byte)l.Period; b[6] = (byte)k; break;
            case 0x03: b[5] = (byte)(l.Period >> 8); b[6] = (byte)l.Period; b[7] = (byte)k; break;
        }
        return b;
    }

    public static MouseLight Decode(byte[] rec)
    {
        var def = Def(rec[0]);
        if (def == null) return null;
        var l = new MouseLight { Effect = rec[0] };
        int c = rec[1] << 16 | rec[2] << 8 | rec[3];
        switch (rec[0])
        {
            case 0x01:
                int max = Math.Max(rec[1], Math.Max(rec[2], rec[3]));
                if (max == 0) { l.Bright = 100; break; }
                l.Color = (rec[1] * 255 / max) << 16 | (rec[2] * 255 / max) << 8 | rec[3] * 255 / max;
                l.Bright = Math.Max(1, (max * 100 + 127) / 255);
                break;
            case 0x0A: l.Color = c; l.Period = U16(rec, 4); l.Bright = rec[7] == 0 ? 100 : rec[7]; break;
            case 0x03: l.Period = U16(rec, 6); l.Bright = rec[8] == 0 ? 100 : rec[8]; break;
        }
        if (def.Speed) l.Period = Math.Max(def.MinP, Math.Min(def.MaxP, l.Period));
        l.Bright = Math.Min(100, l.Bright);
        return l;
    }

    static byte[] Memory(Hidpp h, byte f, int sector, int offset)
    {
        return h.Call(f, 5, (byte)(sector >> 8), (byte)sector, (byte)(offset >> 8), (byte)offset);
    }

    public static State Read(Hidpp h)
    {
        var s = new State();
        byte d = h.F(0x2201);
        s.Dpis = DpiList(h.Call(d, 1, 0));
        s.Dpi = U16(h.Call(d, 2, 0), 1);
        try
        {
            byte r = h.F(0x8060);
            s.Rates = RateList(h.Call(r, 0)[0]);
            int ms = h.Call(r, 1)[0];
            s.Rate = ms > 0 ? 1000 / ms : 0;
        }
        catch (NotSupportedException) { }
        catch (HidppError) { }
        int logo = -1;
        try
        {
            byte f = h.F(0x8070);
            int zones = h.Call(f, 0)[0];
            for (int z = 0; z < zones; z++)
            {
                var info = h.Call(f, 1, (byte)z, 0xFF, 0);
                if (U16(info, 1) == 2) logo = z;
                var map = new Dictionary<int, int>();
                for (int e = 0; e < info[3]; e++) map[U16(h.Call(f, 2, (byte)z, (byte)e, 0xFF, 0), 2)] = e;
                s.Zones[z] = map;
            }
        }
        catch (NotSupportedException) { }
        catch (HidppError) { s.Zones.Clear(); }
        if (logo < 0) logo = s.Zones.Count - 1;
        try
        {
            byte o = h.F(0x8100);
            s.Onboard = h.Call(o, 2)[0] == 1;
            if (s.Onboard)
            {
                var desc = h.Call(o, 0);
                s.Format = desc[1];
                s.SectorSize = U16(desc, 7);
                var cp = h.Call(o, 4);
                s.Current = new[] { cp[0], cp[1] };
                s.Sector = U16(cp, 0);
                if (s.SectorSize >= 0xF0 && s.SectorSize <= 0x1000)
                {
                    s.Profile = ReadSector(h, s.Sector, s.SectorSize);
                    s.ProfileOk = Valid(s.Profile) || s.Sector >= 0x100;
                    s.Stages = StagesOf(s.Profile);
                    if (logo >= 0 && logo < 2) s.Light = Decode(s.Profile.Skip(0xD0 + 11 * logo).Take(11).ToArray());
                }
                if (s.Sector >= 0x100 && s.Format == 3 && desc[3] >= 1 && s.SectorSize >= 0xF0 && s.SectorSize <= 0x1000)
                {
                    try
                    {
                        var directory = ReadSector(h, 0x0000, s.SectorSize);
                        s.Fresh = Valid(directory) && U16(directory, 0) == 0xFFFF;
                    }
                    catch (HidppError e) { s.Fresh = e.Code == 2; }
                }
                s.Stage = h.Call(o, 0xB)[0];
                if (s.Stage >= s.Stages.Length) s.Stage = -1;
            }
        }
        catch (NotSupportedException) { }
        catch (HidppError) { }
        return s;
    }

    public static void SetDpi(Hidpp h, int dpi) { h.Call(h.F(0x2201), 3, 0, (byte)(dpi >> 8), (byte)dpi); }

    public static void SetStage(Hidpp h, int i) { h.Call(h.F(0x8100), 0xC, (byte)i); }

    public static void SetRate(Hidpp h, int hz) { h.Call(h.F(0x8060), 2, (byte)(1000 / hz)); }

    public static void SetLight(Hidpp h, State s, MouseLight l)
    {
        byte f = h.F(0x8070);
        foreach (var z in s.Zones)
        {
            int e;
            if (!z.Value.TryGetValue(l.Effect, out e)) continue;
            h.Call(f, 3, new byte[] { (byte)z.Key, (byte)e }.Concat(Encode(l)).ToArray());
        }
    }
}

static class G9x
{
    public static readonly EffectDef[] Effects = GMouse.Effects.Where(effect => effect.Id <= 0x01).ToArray();
    public static readonly int[] Rates = { 125, 200, 250, 333, 500, 1000 };

    public static int Dpi(int raw) { return (int)Math.Round(raw * 400 / 17.0 / 50) * 50; }

    public static int Raw(int dpi) { return (int)Math.Round(dpi * 17 / 400.0); }

    public static int[] Dpis() { return Enumerable.Range(0, 111).Select(step => 200 + 50 * step).ToArray(); }

    public static bool Valid(byte[] page) { return page.Length == 512 && GMouse.Valid(page); }

    public static byte[] ReadPage(Hidpp device, int pageNumber)
    {
        var page = new byte[512];
        for (int offset = 0; offset < 512; offset += 16)
            Array.Copy(device.Register(0x83, 0xA2, (byte)pageNumber, (byte)(offset / 2)), 0, page, offset, 16);
        return page;
    }

    public static int[] StagesOf(byte[] page)
    {
        return Enumerable.Range(0, 5).Select(stage => page[4 + 6 * stage] << 8 | page[5 + 6 * stage]).TakeWhile(raw => raw != 0 && raw != 0xFFFF).Select(Dpi).ToArray();
    }

    public static MouseLight Light(byte[] color)
    {
        int brightest = Math.Max(color[0], Math.Max(color[1], color[2]));
        if (brightest == 0) return new MouseLight { Effect = 0x00 };
        return new MouseLight
        {
            Effect = 0x01,
            Color = (color[0] * 255 / brightest) << 16 | (color[1] * 255 / brightest) << 8 | color[2] * 255 / brightest,
            Bright = Math.Max(1, (brightest * 100 + 127) / 255)
        };
    }

    public static GMouse.State Read(Hidpp device)
    {
        var state = new GMouse.State { ProfileOk = true, Flash = true, Effects = Effects, Rates = Rates, Dpis = Dpis() };
        var resolution = device.Register(0x83, 0x63);
        state.Dpi = Dpi(resolution[0] | resolution[1] << 8);
        int interval = device.Register(0x81, 0x64)[0];
        state.Rate = interval > 0 ? 1000 / interval : 0;
        state.Light = Light(device.Register(0x81, 0x57));
        state.Zones[0] = new Dictionary<int, int> { { 0x00, 0 }, { 0x01, 1 } };
        try
        {
            var current = device.Register(0x81, 0x0F);
            var directory = current[0] == 0x00 && current[1] < 16 ? ReadPage(device, 1) : null;
            int pageNumber = directory != null && Valid(directory) ? directory[current[1] * 3] : 0xFF;
            var profile = pageNumber >= 2 && pageNumber < 32 ? ReadPage(device, pageNumber) : null;
            if (profile != null)
            {
                state.Sector = pageNumber;
                state.SectorSize = 512;
                state.Current = new[] { current[1] };
                state.Profile = profile;
                state.ProfileOk = Valid(profile);
                if (state.ProfileOk) state.Stages = StagesOf(profile);
            }
        }
        catch (HidppError) { }
        state.Stage = Array.IndexOf(state.Stages, state.Dpi);
        return state;
    }

    public static void SetDpi(Hidpp device, int dpi)
    {
        int raw = Raw(Math.Max(200, Math.Min(5700, dpi)));
        var resolution = new byte[16];
        Array.Copy(device.Register(0x83, 0x63), resolution, 6);
        resolution[0] = resolution[2] = (byte)raw;
        resolution[1] = resolution[3] = (byte)(raw >> 8);
        device.Register(0x82, 0x63, resolution);
    }

    public static void SetRate(Hidpp device, int hz) { device.Register(0x80, 0x64, (byte)(1000 / hz)); }

    public static string Describe(byte[] page)
    {
        if (!Valid(page)) return "damaged, can't be restored";
        var stages = StagesOf(page);
        var dpi = page[0x23] < stages.Length ? stages[page[0x23]] + " DPI" : "DPI stages";
        return page[0x26] > 0 ? dpi + " · " + 1000 / page[0x26] + " Hz" : dpi;
    }

    public static byte[] Build(byte[] old, MouseSet want, int hz)
    {
        var page = (byte[])old.Clone();
        var stages = StagesOf(page);
        if (stages.Length == 0) throw new Refused("The profile on the mouse has no DPI stages, not touching it");
        int stage = want.Stage >= 0 && want.Stage < stages.Length ? want.Stage : Array.IndexOf(stages, want.Dpi);
        if (stage < 0) stage = Math.Min((int)page[0x23], stages.Length - 1);
        int raw = Raw(Math.Max(200, Math.Min(5700, want.Dpi)));
        page[4 + 6 * stage] = page[6 + 6 * stage] = (byte)(raw >> 8);
        page[5 + 6 * stage] = page[7 + 6 * stage] = (byte)raw;
        page[0x23] = (byte)stage;
        if (hz > 0) page[0x26] = (byte)(1000 / hz);
        if (want.Light != null)
        {
            var color = want.Light.Effect == 0x01 ? GMouse.Encode(want.Light) : new byte[3];
            Array.Copy(color, page, 3);
        }
        GMouse.Seal(page);
        return page;
    }

    public static void Store(Hidpp device, GMouse.State state, byte[] page)
    {
        if (!state.Flash || !state.Writable) throw new Refused("Saving isn't supported on this mouse");
        if (page.Length != 512 || !Valid(page)) throw new Refused("Refusing to write a profile with a bad checksum");
        byte pageNumber = (byte)state.Sector;
        device.Register(0x80, 0x0F, 0xFF);
        try
        {
            for (int half = 0; half < 2; half++)
            {
                var data = new byte[256];
                Array.Copy(page, 256 * half, data, 0, 256);
                Upload(device, data);
                if (half == 0) device.Register(0x82, 0xA0, 0x02, 0, 0, 0, 0, 0, pageNumber, 0, 0, 0, 0x01, 0x00);
                device.Register(0x82, 0xA0, 0x03, 0, 0, 0, 0, 0, pageNumber, (byte)(0x80 * half), 0, 0, 0x01, 0x00);
            }
        }
        catch
        {
            try { device.Register(0x80, 0x0F, 0x00, state.Current[0]); }
            catch (Exception) { }
            throw;
        }
        device.Register(0x80, 0x0F, 0x00, state.Current[0]);
        if (!ReadPage(device, pageNumber).SequenceEqual(page)) throw new IOException("The profile on the mouse didn't read back the same after saving");
    }

    static void Upload(Hidpp device, byte[] data)
    {
        device.Register(0x80, 0xA1, 0x01);
        var first = new byte[16];
        new byte[] { 0x01, 0x00, 0x00, 0x00, 0x00, (byte)(data.Length >> 8), (byte)data.Length, 0x00, 0x00 }.CopyTo(first, 0);
        Array.Copy(data, 0, first, 9, 7);
        device.Upload(0x92, 0, first);
        for (int offset = 7, sequence = 1; offset < data.Length; offset += 16, sequence++)
        {
            var chunk = Enumerable.Repeat((byte)0xFF, 16).ToArray();
            Array.Copy(data, offset, chunk, 0, Math.Min(16, data.Length - offset));
            device.Upload(0x93, (byte)sequence, chunk);
        }
    }

    public static void SetLight(Hidpp device, MouseLight light)
    {
        var color = light.Effect == 0x01 ? GMouse.Encode(light) : new byte[3];
        device.Register(0x80, 0x57, color[0], color[1], color[2]);
    }
}
