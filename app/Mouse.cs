using System;
using System.Collections.Generic;
using System.Globalization;
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
    public int Dpi = 800;
    public MouseLight Light;

    public MouseSet Clone() { return new MouseSet { Dpi = Dpi, Light = Light == null ? null : Light.Clone() }; }

    public string ToText()
    {
        var l = Light ?? new MouseLight();
        return string.Join(",", Dpi, l.Effect, l.Color.ToString("X6"), l.Period, l.Bright);
    }

    public static MouseSet FromText(string text)
    {
        var v = text.Split(',');
        return new MouseSet
        {
            Dpi = int.Parse(v[0]),
            Light = new MouseLight { Effect = byte.Parse(v[1]), Color = int.Parse(v[2], NumberStyles.HexNumber), Period = int.Parse(v[3]), Bright = int.Parse(v[4]) },
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
        public bool Onboard;
        public MouseLight Light;
        public Dictionary<int, Dictionary<int, int>> Zones = new Dictionary<int, Dictionary<int, int>>();
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
                int sector = U16(h.Call(o, 4), 0);
                var head = Memory(h, o, sector, 0);
                s.Stages = Enumerable.Range(0, 5).Select(i => head[3 + 2 * i] | head[4 + 2 * i] << 8).TakeWhile(x => x != 0 && x != 0xFFFF).ToArray();
                s.Stage = h.Call(o, 0xB)[0];
                if (s.Stage >= s.Stages.Length) s.Stage = -1;
                if (logo >= 0 && logo < 2)
                {
                    var leds = Memory(h, o, sector, 0xD0).Concat(Memory(h, o, sector, 0xE0)).ToArray();
                    s.Light = Decode(leds.Skip(11 * logo).Take(11).ToArray());
                }
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
