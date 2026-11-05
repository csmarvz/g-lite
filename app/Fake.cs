using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

// In-memory G515 that answers the HID++ calls the app makes, for running without the keyboard.
class FakeHidpp : Hidpp
{
    static readonly Dictionary<int, byte> Index = new Dictionary<int, byte> { { 0x8071, 9 }, { 0x8040, 14 }, { 0x8101, 15 } };
    static readonly byte[] EffectIds = { 0x00, 0x01, 0x0A, 0x15, 0x16, 0x17 };

    public static byte[] Hex(string s) { return Enumerable.Range(0, s.Length / 2).Select(i => Convert.ToByte(s.Substring(i * 2, 2), 16)).ToArray(); }

    Dictionary<int, byte[]> files = new Dictionary<int, byte[]>
    {
        { 0x0101, Hex("0101003c012cffffffffffffffffffffffffffffffffffffffffffffffff18ff008180aaff00006400000000000000000000000019ff008180aaff0000640000000000000000000000000300000000000000000010000104000200ffffffffffffffff") },
        { 0x0100, Hex("5e380925010181014063996dec0101ff") },
    };
    List<byte> buffer = new List<byte>();
    int brightness = 100, mode = 0x03;
    public byte[] Live = new byte[12];

    public FakeHidpp() : base(false) { }

    public override void Dispose() { }

    public override byte[] Call(byte feat, int fn, params byte[] p)
    {
        Thread.Sleep(5);
        var r = new byte[16];
        Func<int, int> u16 = o => p[o] << 8 | p[o + 1];
        if (feat == 0) { byte i; r[0] = Index.TryGetValue(u16(0), out i) ? i : (byte)0; }
        else if (feat == Index[0x8040])
        {
            if (fn == 1) { r[0] = (byte)(brightness >> 8); r[1] = (byte)brightness; }
            if (fn == 2) brightness = u16(0);
        }
        else if (feat == Index[0x8071])
        {
            if (fn == 0)
            {
                if (p[1] >= EffectIds.Length) throw new HidppError(2, "invalid argument");
                r[1] = p[1]; r[3] = EffectIds[p[1]];
            }
            if (fn == 1) Live = p.Take(12).ToArray();
        }
        else if (feat == Index[0x8101])
        {
            switch (fn)
            {
                case 2: buffer.Clear(); break;
                case 3: buffer.AddRange(p.Take(16)); break;
                case 6: if ((p[0] & 1) != 0) mode = p[0] >> 1 & 1; r[0] = (byte)mode; r[2] = 1; break;
                case 7: files[u16(0)] = buffer.Take(u16(2)).ToArray(); break;
                case 8:
                    if (!files.ContainsKey(u16(0))) throw new HidppError(4, "HID++ error 0x04");
                    buffer = files[u16(0)].ToList();
                    break;
                case 0xC: buffer.Skip(u16(0)).Take(16).ToArray().CopyTo(r, 0); break;
            }
        }
        return r;
    }
}
