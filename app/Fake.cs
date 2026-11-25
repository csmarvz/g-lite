using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;

// The fakes answer after a short pause so --demo feels like a device; the self-test sets it to 0 (on Windows a 5 ms sleep lasts about 15).
static class Latency
{
    public static int Ms = 5;
    public static void Pause() { if (Ms > 0) Thread.Sleep(Ms); }
}

// In-memory G515 that answers the HID++ calls the app makes, for running without the keyboard.
class FakeHidpp : Hidpp
{
    static readonly Dictionary<int, byte> Index = new Dictionary<int, byte>
    {
        { 0x8071, 9 }, { 0x8040, 14 }, { 0x8101, 15 }, { 0x8081, 16 }, { 0x4540, 17 }, { 0x1004, 18 }, { 0x1B10, 11 }, { 0x4523, 12 }
    };
    static readonly byte[] EffectIds = { 0x00, 0x01, 0x0A, 0x15, 0x16, 0x17 };
    public readonly Dictionary<int, int> Keys = new Dictionary<int, int>();
    readonly byte[] control = new byte[2];

    public void PowerCycle() { control[0] = control[1] = 0; }
    public int Frames, Battery = 82;
    public bool Charging;
    // fn 7 answers busy this many times before it shows a frame, like the G515 during Rain.
    public int BusyFrames;

    public static byte[] Hex(string hex)
    {
        return Enumerable.Range(0, hex.Length / 2).Select(i => Convert.ToByte(hex.Substring(i * 2, 2), 16)).ToArray();
    }

    public Dictionary<int, byte[]> Flash = new Dictionary<int, byte[]>
    {
        { 0x0101, Hex("0101003c012cffffffffffffffffffffffffffffffffffffffffffffffff18ff008180aaff00006400000000000000000000000019ff008180aaff0000640000000000000000000000000300000000000000000010000104000200ffffffffffffffff") },
        { 0x0100, Hex("5e380925010181014063996dec0101ff") },
    };
    List<byte> buffer = new List<byte>();
    int brightness = 100, mode = 0x03;
    public byte[] Live = new byte[12];

    public FakeHidpp() : base(false) { }

    // The control list (0x1B10) of an owner's G515 TKL, and the Game mode bitmap 0x4523 fn 0 sets.
    public static readonly byte[] ControlList = Hex("014C00EF00F100F300F5014E0150004E010B014401460148014A01420153015600F600F000F200F4014D014F004C004D0143014501470149014B0158017E0116010C01380133013A012A0131015B0118010D013401270129012C015C0169011701320126013501360130015A011E011A012201250128012B012D015D015501190000010E013B01240123012E015F017C010F018B0161018D004F00EC0152011B0000016A01390137012F0160015E0115018C0180017F00340114015100EB000000000000015900000000000000000000000000000000000000000000000000000000018A01850000000000000000000000000000000000000000000000000000");
    public byte[] GameBitmap = new byte[16];

    public static void Controls(int fn, byte[] parameters, byte[] reply)
    {
        if (fn == 0) reply[0] = (byte)(ControlList.Length / 2);
        if (fn == 1) for (int i = 0; i < 16 && parameters[0] * 2 + i < ControlList.Length; i++) reply[i] = ControlList[parameters[0] * 2 + i];
    }

    // What the keys show now: effect ID and its 10 parameter bytes, plus brightness.
    public void State(out byte id, out byte[] parameters, out int bright)
    {
        bright = brightness;
        var live = Live;
        if (mode == 0 && live[1] < EffectIds.Length && live.Length == 12)
        {
            id = EffectIds[live[1]]; parameters = live.Skip(2).ToArray(); return;
        }
        byte[] profile;
        if (!Flash.TryGetValue(0x0008, out profile)) profile = Flash[0x0101];
        int record = 30 + ((profile[74] & 1) != 0 ? 0 : 11);
        id = profile[record];
        parameters = profile.Skip(record + 1).Take(10).ToArray();
    }

    public override void Dispose() { }

    protected override byte[] Send(byte featureIndex, int fn, byte[] parameters)
    {
        Latency.Pause();
        var reply = new byte[16];
        Func<int, int> readUInt16 = offset => parameters[offset] << 8 | parameters[offset + 1];
        if (featureIndex == 0) { byte i; reply[0] = Index.TryGetValue(readUInt16(0), out i) ? i : (byte)0; }
        else if (featureIndex == Index[0x8040])
        {
            if (fn == 1) { reply[0] = (byte)(brightness >> 8); reply[1] = (byte)brightness; }
            if (fn == 2) brightness = readUInt16(0);
        }
        else if (featureIndex == Index[0x8071])
        {
            if (fn == 0)
            {
                // One cluster, its Wave with the capabilities of the G515's report (DC E1: Up and Down among them).
                if (parameters[0] > 0 || parameters[1] >= EffectIds.Length) throw new HidppError(2, "invalid argument");
                reply[1] = parameters[1]; reply[3] = EffectIds[parameters[1]];
                if (reply[3] == 0x16) { reply[4] = 0xDC; reply[5] = 0xE1; }
            }
            if (fn == 1) Live = parameters.Take(12).ToArray();
            if (fn == 5 && parameters[0] == 1) { control[0] = parameters[1]; control[1] = parameters[2]; }
            if (fn == 5) { reply[1] = control[0]; reply[2] = control[1]; }
        }
        else if (featureIndex == Index[0x8081])
        {
            if (fn == 0)
            {
                reply[0] = 0; reply[1] = parameters[1];
                for (int id = parameters[1] * 112; id < parameters[1] * 112 + 112; id++)
                    if ((id >= 0x01 && id <= 0x4F) || (id >= 0x61 && id <= 0x6F))
                        reply[2 + (id - parameters[1] * 112) / 8] |= (byte)(1 << (id % 8));
            }
            if (fn == 6)
            {
                for (int i = 3; i < 16 && parameters.Length > i; i++)
                {
                    if (parameters[i] == 0 && i > 3) break;
                    if (parameters[i] < 0x01 || (parameters[i] > 0x4F && parameters[i] < 0x61) || parameters[i] > 0x6F)
                        throw new HidppError(2, "invalid argument");
                    Keys[parameters[i]] = parameters[0] << 16 | parameters[1] << 8 | parameters[2];
                }
            }
            if (fn == 1)
                for (int at = 0; at + 3 < 16 && at + 3 < parameters.Length; at += 4)
                    Keys[parameters[at]] = parameters[at + 1] << 16 | parameters[at + 2] << 8 | parameters[at + 3];
            if (fn == 7)
            {
                if (BusyFrames > 0) { BusyFrames--; throw new HidppError(8, "busy"); }
                Frames++;
            }
        }
        else if (featureIndex == Index[0x4540]) reply[0] = 0x0D;
        else if (featureIndex == Index[0x1B10]) Controls(fn, parameters, reply);
        else if (featureIndex == Index[0x4523] && fn == 0) GameBitmap = parameters.Take(16).ToArray();
        else if (featureIndex == Index[0x1004]) { reply[0] = (byte)Battery; reply[2] = (byte)(Charging ? 1 : 0); }
        else if (featureIndex == Index[0x8101])
        {
            switch (fn)
            {
                case 2: buffer.Clear(); break;
                case 3: buffer.AddRange(parameters.Take(16)); break;
                case 6: if ((parameters[0] & 1) != 0) mode = parameters[0] >> 1 & 1; reply[0] = (byte)mode; reply[2] = 1; break;
                case 7: Flash[readUInt16(0)] = buffer.Take(readUInt16(2)).ToArray(); break;
                case 8:
                    if (!Flash.ContainsKey(readUInt16(0))) throw new HidppError(4, "HID++ error 0x04");
                    buffer = Flash[readUInt16(0)].ToList();
                    break;
                case 0xC: buffer.Skip(readUInt16(0)).Take(16).ToArray().CopyTo(reply, 0); break;
            }
        }
        return reply;
    }
}

class FakeSpeaker : Hidpp
{
    static readonly Dictionary<int, byte> Index = new Dictionary<int, byte>
    {
        { 0x8070, 4 }, { 0x8040, 8 }, { 0x4540, 9 }, { 0x4522, 10 }, { 0x8080, 11 }
    };
    static readonly byte[] Effects = { 0x00, 0x01, 0x03, 0x07, 0x0A };
    public readonly byte[][] Live = new byte[6][];
    public int Count = 4;
    public bool G213, G413, PerKey;
    public readonly SortedSet<int> GameKeys = new SortedSet<int>();
    // 0x8080: colours by USB usage as fn 3 left them, and the frames fn 5 showed.
    public readonly Dictionary<int, int> KeyColors = new Dictionary<int, int>();
    public int Frames;

    public FakeSpeaker() : base(false) { }

    // From a G213's report: zone 0 is the whole keyboard, zones 1–5 (locations 6–10) take Off and Static only.
    byte[] ZoneEffects(int zone)
    {
        if (G413) return new byte[] { 0x00, 0x01, 0x0A };
        return !G213 ? Effects : zone == 0 ? new byte[] { 0x00, 0x01, 0x0A, 0x03, 0x04 } : new byte[] { 0x00, 0x01 };
    }

    // A G413 Silver, from its report (docs/reports/g413-c33a.txt): one zone, the keys, with Off, Static and Breathe, and the
    // monochrome bit in fn 0's extended capabilities.
    public static FakeSpeaker G413Silver() { return new FakeSpeaker { Count = 1, G413 = true }; }

    // A G512 or G513: the keys and the logo on 0x8070, per-key colours on 0x8080, 0x4522 (OpenLogi's G512 recording has all three).
    public static FakeSpeaker G512() { return new FakeSpeaker { Count = 2, PerKey = true }; }

    // A posted call (Hidpp.PostUnlogged) gets no reply read.
    protected override void Write(byte featureIndex, int fn, byte[] parameters) { Send(featureIndex, fn, parameters); }

    public override void Dispose() { }

    protected override byte[] Send(byte featureIndex, int fn, byte[] parameters)
    {
        Latency.Pause();
        var reply = new byte[16];
        if (featureIndex == 0) { byte i; reply[0] = Index.TryGetValue(parameters[0] << 8 | parameters[1], out i) ? i : (byte)0; }
        else if (featureIndex == Index[0x8070])
        {
            if (fn == 0)
            {
                reply[0] = (byte)Count;
                if (G413) { reply[2] = 0x01; reply[4] = 0x08; }
            }
            else if (fn == 1)
            {
                reply[0] = parameters[0];
                reply[2] = (byte)(G213 ? (parameters[0] == 0 ? 1 : parameters[0] + 5) : G413 ? 1 : parameters[0] + 1);
                reply[3] = (byte)ZoneEffects(parameters[0]).Length;
            }
            else if (fn == 2)
            {
                var listed = ZoneEffects(parameters[0]);
                if (parameters[1] >= listed.Length) throw new HidppError(2, "invalid argument");
                reply[0] = parameters[0]; reply[1] = parameters[1]; reply[3] = listed[parameters[1]];
            }
            else if (fn == 3)
            {
                if (parameters[0] >= Count || parameters[1] >= ZoneEffects(parameters[0]).Length)
                    throw new HidppError(2, "invalid argument");
                Live[parameters[0]] = parameters.Take(12).ToArray();
            }
        }
        else if (featureIndex == Index[0x4540] && (G213 || G413 || PerKey)) reply[0] = 0x0D;
        else if (featureIndex == Index[0x8080] && PerKey)
        {
            if (fn == 3)
            {
                if (parameters[1] != 0x01 || parameters[3] > 3) throw new HidppError(2, "invalid argument");
                for (int entry = 0; entry < parameters[3]; entry++)
                    KeyColors[parameters[4 + 4 * entry]] = parameters[5 + 4 * entry] << 16 | parameters[6 + 4 * entry] << 8 | parameters[7 + 4 * entry];
            }
            if (fn == 5) Frames++;
        }
        else if (featureIndex == Index[0x4522] && (G413 || PerKey))
        {
            if (fn == 1) foreach (var usage in parameters.TakeWhile(usage => usage != 0)) GameKeys.Add(usage);
            if (fn == 3) GameKeys.Clear();
        }
        else if (featureIndex != Index[0x8040]) throw new HidppError(2, "invalid argument");
        return reply;
    }
}

// A G510s with the feature reports from an owner's report (docs/G510.md).
class FakeG510 : Hidpp
{
    public readonly Dictionary<byte, byte[]> Features = new Dictionary<byte, byte[]>
    {
        { 0x04, new byte[] { 0x04, 0x80 } },
        { 0x05, new byte[] { 0x05, 0xFF, 0xFF, 0xFF } },
        { 0x06, new byte[] { 0x06, 0xFF, 0xFF, 0xFF } },
        { 0x07, new byte[] { 0x07, 0x03, 0x00 } },
    };

    // Input report 0x03 (hid-lg-g15): the G keys, M keys and MR as bits, and the Game mode switch, byte 3 bit 2.
    public bool GameMode;

    public FakeG510() : base(false) { }

    public override void Dispose() { }

    protected override byte[] ReadInput(byte id)
    {
        Latency.Pause();
        if (id != 0x03) throw new System.IO.IOException("Couldn't read input report " + id.ToString("X2") + " (error 31)");
        return new byte[] { 0x03, 0x00, 0x00, (byte)(GameMode ? 0x04 : 0x00), 0x00, 0x00, 0x00, 0x00 };
    }

    protected override byte[] ReadFeature(byte id)
    {
        Latency.Pause();
        byte[] report;
        if (!Features.TryGetValue(id, out report))
            throw new System.IO.IOException("Couldn't read feature report " + id.ToString("X2") + " (error 31)");
        var reply = new byte[258];
        Array.Copy(report, reply, report.Length);
        return reply;
    }

    protected override void WriteFeature(byte[] report)
    {
        Latency.Pause();
        byte[] old;
        if (!Features.TryGetValue(report[0], out old))
            throw new System.IO.IOException("Couldn't set feature report " + report[0].ToString("X2") + " (error 31)");
        Features[report[0]] = report.Take(old.Length).ToArray();
    }
}

class FakeLight : Hidpp
{
    static readonly Dictionary<int, byte> Index = new Dictionary<int, byte>
    {
        { 0x1990, 6 }, { 0x8040, 0x0A }, { 0x8071, 0x0B }, { 0x8081, 0x0C }
    };
    static readonly byte[][] Groups = { new byte[] { 0x01, 0x0A, 0x15 }, new byte[] { 0x13, 0x16, 0x18 } };
    public bool FrontOn = true, BackOn = true;
    public byte[] Effect;
    public bool Controlled;
    public int Lumen = 200, Kelvin = 4000, BackLevel = 80, Frames;
    public readonly int[] Colors = new int[7];

    public FakeLight() : base(false) { }

    public override void Dispose() { }

    protected override byte[] Send(byte featureIndex, int fn, byte[] parameters)
    {
        Latency.Pause();
        var reply = new byte[16];
        if (featureIndex == 0) { byte i; reply[0] = Index.TryGetValue(parameters[0] << 8 | parameters[1], out i) ? i : (byte)0; }
        else if (featureIndex == Index[0x1990])
        {
            if (fn == 0) reply[0] = (byte)(FrontOn ? 1 : 0);
            else if (fn == 1) FrontOn = parameters[0] == 1;
            else if (fn == 2) { reply[2] = 30; reply[3] = 0x01; reply[4] = 0x90; reply[6] = 1; }
            else if (fn == 3) { reply[0] = (byte)(Lumen >> 8); reply[1] = (byte)Lumen; }
            else if (fn == 4) Lumen = parameters[0] << 8 | parameters[1];
            else if (fn == 7) { reply[1] = 0x0A; reply[2] = 0x8C; reply[3] = 0x19; reply[4] = 0x64; reply[6] = 100; }
            else if (fn == 8) { reply[0] = (byte)(Kelvin >> 8); reply[1] = (byte)Kelvin; }
            else if (fn == 9) Kelvin = parameters[0] << 8 | parameters[1];
            else throw new HidppError(7, "invalid function");
        }
        else if (featureIndex == Index[0x8040])
        {
            if (fn == 1) { reply[1] = (byte)BackLevel; }
            else if (fn == 2) BackLevel = parameters[1];
            else if (fn == 3) reply[0] = (byte)(BackOn ? 1 : 0);
            else if (fn == 4) BackOn = parameters[0] == 1;
        }
        else if (featureIndex == Index[0x8071])
        {
            if (fn == 0)
            {
                if (parameters[0] >= Groups.Length || (parameters[1] != 0xFF && parameters[1] >= Groups[parameters[0]].Length))
                    throw new HidppError(2, "invalid argument");
                reply[0] = parameters[0];
                reply[1] = parameters[1];
                if (parameters[1] == 0xFF) reply[4] = (byte)Groups[parameters[0]].Length;
                else reply[3] = Groups[parameters[0]][parameters[1]];
            }
            // A real Beam LX answers 0x05 to fn 1 unless software holds the clusters (fn 5 bit 0).
            else if (fn == 1) { if (!Controlled) throw new HidppError(5, "Logitech internal"); Effect = parameters.Take(13).ToArray(); }
            else if (fn == 5) { if (parameters[0] == 1) Controlled = (parameters[1] & 1) != 0; reply[1] = (byte)(Controlled ? 1 : 0); }
            else throw new HidppError(7, "invalid function");
        }
        else if (featureIndex == Index[0x8081])
        {
            if (fn == 0) { reply[1] = parameters[1]; if (parameters[1] == 0) reply[2] = 0xFE; }
            else if (fn == 1)
            {
                for (int i = 0; i + 3 < parameters.Length && i < 16; i += 4)
                    if (parameters[i] >= 1 && parameters[i] <= 7)
                        Colors[parameters[i] - 1] = parameters[i + 1] << 16 | parameters[i + 2] << 8 | parameters[i + 3];
            }
            else if (fn == 7) Frames++;
        }
        else throw new HidppError(2, "invalid argument");
        return reply;
    }
}

class FakeG915 : Hidpp
{
    Dictionary<int, byte> Index = new Dictionary<int, byte> { { 0x8071, 10 }, { 0x8040, 20 }, { 0x8100, 21 }, { 0x4540, 16 }, { 0x4522, 8 } };
    // The Game mode list (0x4522): usages added with fn 1 and taken off with fn 2, all cleared with fn 3.
    public readonly SortedSet<int> GameKeys = new SortedSet<int>();
    public byte[] GameBitmap = new byte[16];
    public byte[][] Ids = { new byte[] { 0x00, 0x01, 0x03, 0x0A }, new byte[] { 0x00, 0x01, 0x0A, 0x03, 0x04, 0x0B, 0x0C } };
    // Profile 1 of an owner's G915 TKL (Cycle at 10 s on both clusters, power save after 60 s, off after 300 s),
    // and its factory copy (Cycle at 5 s).
    public readonly Dictionary<int, byte[]> Sectors = new Dictionary<int, byte[]>
    {
        { 1, FakeHidpp.Hex("010000000000000000000000000000000000FFFFFFFFFFFFFFFFFFFF3C002C018002003A8002003B8002003C8002003D8002003E8002003F800200408002004180020042800200438002004480020045900DFF01900DFF02900DFF03FFFFFFFF00040000000400020004000400040006000400080004000A0004000C0004000E00040010000400120004001400040016900DFF01900DFF02900DFF03FFFFFFFF500052004F00460049004C0045005F004E0041004D0045005F00440045004600410055004C00540000000000000000000300000000002710320000030000000000271032000003000000000027101900000300000000002710190000004349") },
        { 0x101, FakeHidpp.Hex("010000000000000000000000000000000000FFFFFFFFFFFFFFFFFFFF3C002C018002003A8002003B8002003C8002003D8002003E8002003F800200408002004180020042800200438002004480020045900DFF01900DFF02900D0001900D0002900D0003FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF0300000000001388640000030000000000138864000003000000000013883200000300000000001388320000FFFFFF") }
    };
    public int Reloads, Power = 1, Running = 1;
    // 0x8100's button slots: 15 on the owner's G915 TKL (F1-F12, M1-M3), 8 on the G815 (G1-G5, M1-M3).
    public int ButtonSlots = 15;
    public readonly List<byte> Modes = new List<byte> { 1 };
    List<byte> pending = new List<byte>();
    int writeTo, writeLength;
    public readonly byte[][] Live = { new byte[13], new byte[13] };
    public int Brightness = 80, Claimed;
    public byte Layout = 1;

    // The owner's directory lists profiles 1 to 3 (docs/G815-G915.md). Profiles 2 and 3 are copies of 1 with static red and blue keys.
    public FakeG915() : base(false)
    {
        var directory = Enumerable.Repeat((byte)0xFF, 255).ToArray();
        FakeHidpp.Hex("000101FF000201FF000301FF").CopyTo(directory, 0);
        MouseProtocol.WriteChecksum(directory);
        Sectors[0] = directory;
        Sectors[2] = Recolored(Sectors[1], "01FF0000");
        Sectors[3] = Recolored(Sectors[1], "010000FF");
    }

    static byte[] Recolored(byte[] profile, string record)
    {
        var copy = (byte[])profile.Clone();
        for (int at = 0xD0; at < 0xE6; at += 11)
        {
            for (int i = 0; i < 11; i++) copy[at + i] = 0;
            FakeHidpp.Hex(record).CopyTo(copy, at);
        }
        MouseProtocol.WriteChecksum(copy);
        return copy;
    }

    public static FakeG915 G512X()
    {
        return new FakeG915
        {
            Index = new Dictionary<int, byte>
            {
                { 0x8071, 6 }, { 0x8081, 7 }, { 0x8040, 8 }, { 0x8101, 9 }, { 0x4540, 10 }, { 0x1B10, 11 }, { 0x4523, 12 }
            },
            Ids = new[] { new byte[] { 0x00, 0x01, 0x0A, 0x15, 0x16, 0x17, 0x18 }, new byte[] { 0x00, 0x01, 0x0A, 0x15, 0x16, 0x18 } }
        };
    }

    public override void Dispose() { }

    protected override byte[] Send(byte featureIndex, int fn, byte[] parameters)
    {
        Latency.Pause();
        var reply = new byte[16];
        if (featureIndex == 0) { byte i; reply[0] = Index.TryGetValue(parameters[0] << 8 | parameters[1], out i) ? i : (byte)0; }
        else if (featureIndex == Index[0x4540]) reply[0] = Layout;
        // The G515's list, for the G915 X: its own isn't known.
        else if (Index.ContainsKey(0x1B10) && featureIndex == Index[0x1B10]) FakeHidpp.Controls(fn, parameters, reply);
        else if (Index.ContainsKey(0x4523) && featureIndex == Index[0x4523] && fn == 0) GameBitmap = parameters.Take(16).ToArray();
        else if (Index.ContainsKey(0x4522) && featureIndex == Index[0x4522])
        {
            if (fn == 0) reply[0] = 0xFF;
            if (fn == 1) foreach (var usage in parameters.TakeWhile(usage => usage != 0)) GameKeys.Add(usage);
            if (fn == 2) foreach (var usage in parameters.TakeWhile(usage => usage != 0)) GameKeys.Remove(usage);
            if (fn == 3) GameKeys.Clear();
        }
        else if (Index.ContainsKey(0x8100) && featureIndex == Index[0x8100])
        {
            if (fn == 0) { FakeHidpp.Hex("01040103030F1000FF3604").CopyTo(reply, 0); reply[5] = (byte)ButtonSlots; }
            if (fn == 1) Modes.Add(parameters[0]);
            if (fn == 2) reply[0] = Modes.Last();
            if (fn == 3) { Reloads++; Running = parameters[0] << 8 | parameters[1]; }
            // Off its onboard profiles (G HUB running), the G815 answers 0 (2026-11-21).
            if (fn == 4) reply[1] = Modes.Last() == 1 ? (byte)Running : (byte)0;
            if (fn == 5 && !Sectors.ContainsKey(parameters[0] << 8 | parameters[1])) throw new HidppError(2, "invalid argument");
            if (fn == 5)
                Sectors[parameters[0] << 8 | parameters[1]].Skip(parameters[2] << 8 | parameters[3]).Take(16).ToArray().CopyTo(reply, 0);
            if (fn == 6)
            {
                writeTo = parameters[0] << 8 | parameters[1]; writeLength = parameters[4] << 8 | parameters[5]; pending.Clear();
            }
            if (fn == 7) pending.AddRange(parameters.Take(16));
            if (fn == 8) Sectors[writeTo] = pending.Take(writeLength).ToArray();
        }
        else if (featureIndex == Index[0x8040])
        {
            if (fn == 1) { reply[0] = (byte)(Brightness >> 8); reply[1] = (byte)Brightness; }
            if (fn == 2) Brightness = parameters[0] << 8 | parameters[1];
        }
        else if (featureIndex == Index[0x8071])
        {
            if (fn == 0 || fn == 1)
            {
                if (parameters[0] >= Ids.Length || parameters[1] >= Ids[parameters[0]].Length) throw new HidppError(2, "invalid argument");
                if (fn == 0) { reply[0] = parameters[0]; reply[1] = parameters[1]; reply[3] = Ids[parameters[0]][parameters[1]]; }
                else Live[parameters[0]] = parameters.Take(13).ToArray();
            }
            if (fn == 5 && parameters[0] == 1) Claimed = parameters[1];
            if (fn == 5) reply[1] = (byte)Claimed;
            if (fn == 8 && parameters[0] == 1) Power = parameters[1];
            if (fn == 8) reply[1] = (byte)Power;
        }
        else if (Index.ContainsKey(0x8081) && featureIndex == Index[0x8081] && fn == 0)
        {
            reply[1] = parameters[1];
            for (int id = parameters[1] * 112; id < parameters[1] * 112 + 112; id++)
                if (id <= 0x6F || (id >= 0xA0 && id <= 0xC0) || (id >= 0xF2 && id <= 0xFE))
                    reply[2 + (id - parameters[1] * 112) / 8] |= (byte)(1 << ((id - parameters[1] * 112) % 8));
        }
        else if (!Index.ContainsValue(featureIndex)) throw new HidppError(2, "invalid argument");
        return reply;
    }
}

// A G600 with tulth/g600prog's defaults.json: mode 1 rainbow with the keypad as 1 to =, mode 2 white with the numeric keypad,
// mode 3 a green pulse with DPI buttons. 400, 1200, 2000 and 3200 DPI, 1000 Hz. Each report is the 154 bytes G600Mouse reads.
class FakeG600 : Hidpp
{
    public int Mode, Level = 1;
    public readonly byte[][] Modes =
    {
        Report(0xF3, 0x000000, 2, 6, "010000020000030000040000050000170000000205140000"
            + "00001E00001F000020000021000022000023000024000025000026000027"+ "00002D00002E"),
        Report(0xF4, 0xFFFFFF, 0, 0, "010000020000030000040000050000170000000205140000"
            + "00005900005A00005B00005C00005D00005E00005F000060000061000062" + "000056000057"),
        Report(0xF5, 0x00FF00, 1, 4, "010000020000030000040000050000150000130000140000"
            + "00001E00001F000020000021000022000023" + "110000160000050000120000000008040000"),
    };

    public FakeG600() : base(false) { }

    static byte[] Report(byte id, int color, byte effect, byte duration, string buttons)
    {
        var report = new byte[G600Mouse.ReportLength];
        report[0] = id;
        report[1] = (byte)(color >> 16); report[2] = (byte)(color >> 8); report[3] = (byte)color;
        report[4] = effect;
        report[5] = duration;
        FakeHidpp.Hex("000802" + "08182840").CopyTo(report, 11);
        FakeHidpp.Hex(buttons).CopyTo(report, 31);
        report[91] = report[92] = report[93] = 0xFF;
        FakeHidpp.Hex("010000020000030000").CopyTo(report, 94);
        return report;
    }

    public override void Dispose() { }

    protected override byte[] ReadFeature(byte id)
    {
        Latency.Pause();
        var reply = new byte[G600Mouse.ReportLength];
        if (id == G600Mouse.ActiveReport) { reply[0] = id; reply[1] = (byte)(Mode << 4 | Level << 1); return reply; }
        if (id < G600Mouse.FirstModeReport || id >= G600Mouse.FirstModeReport + G600Mouse.ModeCount)
            throw new System.IO.IOException("Couldn't read feature report " + id.ToString("X2") + " (error 31)");
        Array.Copy(Modes[id - G600Mouse.FirstModeReport], reply, reply.Length);
        return reply;
    }

    protected override void WriteFeature(byte[] report)
    {
        throw new System.IO.IOException("Couldn't set feature report " + report[0].ToString("X2") + " (error 31)");
    }
}

// A G300: three profiles like those PeriHub's owner read (issue #16), green, blue and off; a DPI shift button on profile 2, Ctrl
// alone on profile 3's G7. Profile writes are kept, so a save reads back.
class FakeG300 : Hidpp
{
    public int Profile, Level = 1;
    public readonly byte[][] Profiles =
    {
        FakeHidpp.Hex("F302000284060A40" + "010000020000030000" + "0A00000B00000C0000" + "040000050000" + "0D0000"),
        FakeHidpp.Hex("F4040304880A0804" + "010000020000030000" + "0A00000B0000000106" + "040000050000" + "0E0000"),
        FakeHidpp.Hex("F500020204860A40" + "010000020000030000" + "0C00000D00000F0000" + "000100050000" + "040000"),
    };

    public FakeG300() : base(false) { }

    public override void Dispose() { }

    protected override byte[] ReadFeature(byte id)
    {
        Latency.Pause();
        if (id == G300Mouse.ActiveReport) return new byte[] { id, (byte)(Profile << 4 | Level << 1), 0, 0 };
        if (id < G300Mouse.FirstProfileReport || id >= G300Mouse.FirstProfileReport + G300Mouse.ProfileCount)
            throw new System.IO.IOException("Couldn't read feature report " + id.ToString("X2") + " (error 31)");
        return (byte[])Profiles[id - G300Mouse.FirstProfileReport].Clone();
    }

    protected override void WriteFeature(byte[] report)
    {
        Latency.Pause();
        if (report.Length != G300Mouse.ReportLength || report[0] < G300Mouse.FirstProfileReport
            || report[0] >= G300Mouse.FirstProfileReport + G300Mouse.ProfileCount)
            throw new System.IO.IOException("Couldn't set feature report " + report[0].ToString("X2") + " (error 87)");
        Profiles[report[0] - G300Mouse.FirstProfileReport] = (byte[])report.Clone();
    }
}

class FakeMouse : Hidpp
{
    public int Dpi = 1600, Stage = 2, Reloads, Rate = 1, ZoneCount = 2;
    public string Info = "0103010501081000ff0a04";
    public bool Wheel, Wide, HiddenLight;
    public byte WheelMode;
    public readonly byte[][] Zones = { new byte[12], new byte[12] };
    public readonly Dictionary<int, byte[]> Sectors = new Dictionary<int, byte[]>();
    static readonly byte[] Ids = { 0x00, 0x01, 0x03, 0x0A };
    List<byte> pending = new List<byte>();
    int writeTo, writeLength, current = 1;

    public FakeMouse() : this(false) { }

    public FakeMouse(bool fresh) : base(false)
    {
        var rom = Enumerable.Repeat((byte)0xFF, 255).ToArray();
        rom[0] = 1; rom[1] = 2; rom[2] = 2;
        var stages = new[] { 400, 800, 1600, 3200, 6400 };
        for (int i = 0; i < 5; i++) { rom[3 + 2 * i] = (byte)stages[i]; rom[4 + 2 * i] = (byte)(stages[i] >> 8); }
        var cycle = FakeHidpp.Hex("0300000000001f40000000");
        cycle.CopyTo(rom, 0xD0);
        cycle.CopyTo(rom, 0xDB);
        var user = (byte[])rom.Clone();
        Encoding.Unicode.GetBytes("Default").CopyTo(user, 0xA0);
        int crc = MouseProtocol.Crc(user, 253);
        user[253] = (byte)(crc >> 8);
        user[254] = (byte)crc;
        Sectors[0x0101] = rom;
        Sectors[0x0001] = user;
        Sectors[0x0000] = FakeHidpp.Hex("00010100").Concat(Enumerable.Repeat((byte)0xFF, 251)).ToArray();
        if (!fresh) return;
        Sectors.Remove(0x0000);
        Sectors.Remove(0x0001);
        current = 0x0101;
    }

    public static FakeMouse G502()
    {
        var mouse = new FakeMouse { Info = "01030105010B1000ff0a04", Wheel = true };
        var bindings = new[]
        {
            "80010001", "80010002", "80010004", "80010008", "80010010",
            "90070000", "90040000", "90030000", "90080000", "90020000", "90010000"
        };
        foreach (var sector in new[] { 0x0001, 0x0101 })
        {
            var profile = mouse.Sectors[sector];
            for (int i = 0; i < bindings.Length; i++) FakeHidpp.Hex(bindings[i]).CopyTo(profile, 0x20 + 4 * i);
        }
        MouseProtocol.WriteChecksum(mouse.Sectors[0x0001]);
        return mouse;
    }

    // A G502 HERO that has only run its factory profile: the 0x8100 fn 0 reply, the factory bindings (specials end FF FF)
    // and the directory of zeros are from g502-hub's docs/protocol.md and Solaar #2313.
    public static FakeMouse Hero()
    {
        var mouse = new FakeMouse(true) { Info = "01020105050B1000FF0A01" };
        var bindings = new[]
        {
            "80010001", "80010002", "80010004", "80010008", "80010010",
            "9007FFFF", "9004FFFF", "9003FFFF", "900AFFFF", "9002FFFF", "9001FFFF"
        };
        for (int i = 0; i < bindings.Length; i++) FakeHidpp.Hex(bindings[i]).CopyTo(mouse.Sectors[0x0101], 0x20 + 4 * i);
        mouse.Sectors[0x0000] = new byte[255];
        return mouse;
    }

    public static FakeMouse For(FoundDevice found)
    {
        return found.Models.Contains("C08B") ? Hero()
            : found.WirelessId == 0x407F ? G502()
            : found.WirelessId == 0x4074 ? G305()
            : found.WirelessId == 0x4093 ? Superlight()
            : found.WirelessId == 0x4099 ? G502XPlus()
            : new FakeMouse();
    }

    // A G502 X PLUS: profile format 5 with the G502 X LIGHTSPEED's counts, stages and factory bindings (docs/G502X.md), a write
    // counter at 0x12 and lights on 0x8071 only, so no 0x8070 zones (Solaar's G502 X PLUS 4099 report).
    public static FakeMouse G502XPlus()
    {
        var mouse = new FakeMouse { Info = "01050105020B1000ff0a04", Wheel = true, ZoneCount = 0, Dpi = 1600, Stage = 2 };
        var bindings = new[]
        {
            "80010001", "80010002", "80010004", "80010008", "90070000", "80010010",
            "90010000", "90020000", "900A0000", "90030000", "90040000"
        };
        var stages = new[] { 800, 1200, 1600, 2400, 3200 };
        foreach (var sector in new[] { 0x0001, 0x0101 })
        {
            var profile = mouse.Sectors[sector];
            for (int i = 0; i < stages.Length; i++) { profile[3 + 2 * i] = (byte)stages[i]; profile[4 + 2 * i] = (byte)(stages[i] >> 8); }
            for (int i = 0; i < bindings.Length; i++) FakeHidpp.Hex(bindings[i]).CopyTo(profile, 0x20 + 4 * i);
            FakeHidpp.Hex("0012").CopyTo(profile, 0x12);
            FakeHidpp.Hex("3C002C01").CopyTo(profile, 0x1C);
        }
        MouseProtocol.WriteChecksum(mouse.Sectors[0x0001]);
        return mouse;
    }

    public static FakeMouse G305()
    {
        var mouse = new FakeMouse(true) { ZoneCount = 1, Info = "0103010101060600ff0a02", Dpi = 800, Stage = 1 };
        mouse.Sectors[0x0101] = FakeHidpp.Hex("010100900120034006800C0000FFFFFFFF00FFFFFFFFFFFFFFFFFFFFFFFFFFFF800100018001000280010004800100088001001090050000FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF0000000000001F400000000000000000001F40000000FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF");
        return mouse;
    }

    // An original PRO X SUPERLIGHT: profile format 4, five profiles and five buttons (GFlick's counts; the rest of the 0x8100
    // fn 0 reply is the SUPERLIGHT 2's), and 0x8070 flagged hidden (Solaar's PRO X Wireless 4093 report).
    public static FakeMouse Superlight()
    {
        var mouse = new FakeMouse { Info = "0104010501051000ff0a04", HiddenLight = true, ZoneCount = 1 };
        foreach (var sector in new[] { 0x0001, 0x0101 })
            FakeHidpp.Hex("8001000180010002800100048001000880010010").CopyTo(mouse.Sectors[sector], 0x20);
        MouseProtocol.WriteChecksum(mouse.Sectors[0x0001]);
        return mouse;
    }

    // A PRO X SUPERLIGHT 2: Featherweight's 0x8100 fn 0 reply (format 7, 255-byte sectors, 5 buttons), docs/newer-mice.md.
    public static FakeMouse ProX2() { return Wider("0107010501051000ff0a04", "80010001800100028001000480010008" + "80010010"); }

    // A PRO X 2 DEX, the same format; the thumb buttons swapped to show the bindings come from the profile.
    public static FakeMouse Dex() { return Wider("0107010501051000ff0a04", "80010001800100028001000480010010" + "80010008"); }

    static FakeMouse Wider(string info, string buttons)
    {
        var mouse = new FakeMouse { Wide = true, ZoneCount = 0, Info = info, Dpi = 800, Stage = 1, Rate = 3 };
        var profile = Enumerable.Repeat((byte)0xFF, 255).ToArray();
        FakeHidpp.Hex("03030100" + "9001900102" + "2003200302" + "4006400602" + "800C800C02" + "0000000000").CopyTo(profile, 0);
        FakeHidpp.Hex(buttons).CopyTo(profile, 0x30);
        MouseProtocol.WriteChecksum(profile);
        mouse.Sectors[0x0001] = profile;
        mouse.Sectors[0x0101] = (byte[])profile.Clone();
        return mouse;
    }

    int[] Stages
    {
        get { return Wide ? MouseProtocol.WideStages(Sectors[current], new[] { 100, 32000 }) : MouseProtocol.StagesOf(Sectors[current]); }
    }

    // The DPI button under the wheel: the next stage, as the mouse does on its own before it sends 0x8100 event fn 1.
    public void PressDpiButton() { Stage = (Math.Max(0, Stage) + 1) % Stages.Length; Dpi = Stages[Stage]; }

    public override void Dispose() { }

    protected override byte[] Send(byte featureIndex, int fn, byte[] parameters)
    {
        Latency.Pause();
        var reply = new byte[16];
        Func<int, int> readUInt16 = offset => parameters[offset] << 8 | parameters[offset + 1];
        switch (featureIndex)
        {
            case 0:
                int id = readUInt16(0);
                reply[0] = (byte)(id == 0x2201 ? 1 : id == 0x8060 ? 2 : id == 0x8070 ? 3 : id == 0x8100 ? 4
                    : id == 0x2121 && Wheel ? 5 : id == 0x1D4B ? 9 : 0);
                if (Wide) reply[0] = (byte)(id == 0x2202 ? 6 : id == 0x8061 ? 7 : id == 0x8100 ? 4 : 0);
                if (HiddenLight && id == 0x8070) reply[1] = 0x60;
                break;
            case 6:
                if (fn == 1) { reply[1] = 5; reply[2] = 3; }
                if (fn == 2) { reply[2] = parameters[2]; if (parameters[2] == 0) FakeHidpp.Hex("0064e0327d000000").CopyTo(reply, 3); }
                if (fn == 5)
                {
                    reply[1] = reply[5] = (byte)(Dpi >> 8); reply[2] = reply[6] = (byte)Dpi;
                    reply[3] = 0x03; reply[4] = 0x20; reply[9] = 2;
                }
                if (fn == 6)
                {
                    if (Array.IndexOf(Stages, readUInt16(1)) < 0) throw new HidppError(5, "HID++ error 0x05");
                    Dpi = readUInt16(1);
                    Stage = Array.IndexOf(Stages, Dpi);
                }
                break;
            case 7:
                if (fn == 1) reply[1] = 0x7F;
                if (fn == 2) reply[0] = (byte)(parameters[0] == 1 ? Rate : 0);
                if (fn == 3) throw new HidppError(5, "HID++ error 0x05");
                break;
            case 1:
                if (fn == 1) FakeHidpp.Hex("000064e0326400").CopyTo(reply, 0);
                if (fn == 2) { reply[1] = (byte)(Dpi >> 8); reply[2] = (byte)Dpi; reply[3] = 0x03; reply[4] = 0x20; }
                if (fn == 3) { Dpi = readUInt16(1); Stage = Array.IndexOf(Stages, Dpi); }
                break;
            case 2:
                if (fn == 0) reply[0] = 0x8B;
                if (fn == 1) reply[0] = (byte)Rate;
                if (fn == 2) throw new HidppError(5, "HID++ error 0x05");
                break;
            case 3:
                if (fn == 0) reply[0] = (byte)ZoneCount;
                if (fn == 1) { reply[0] = parameters[0]; reply[2] = (byte)(parameters[0] + 1); reply[3] = 4; }
                if (fn == 2)
                {
                    if (parameters[1] >= Ids.Length) throw new HidppError(2, "invalid argument");
                    reply[0] = parameters[0]; reply[1] = parameters[1]; reply[3] = Ids[parameters[1]];
                }
                if (fn == 3) Zones[parameters[0]] = parameters.Take(12).ToArray();
                break;
            case 4:
                if (fn == 0) FakeHidpp.Hex(Info).CopyTo(reply, 0);
                if (fn == 2) reply[0] = 1;
                if (fn == 3)
                {
                    var prof = Sectors[readUInt16(0)];
                    current = readUInt16(0);
                    Reloads++;
                    Rate = prof[0];
                    Stage = prof[Wide ? MouseProtocol.WideDefaultStageAt : MouseProtocol.DefaultStageAt];
                    Dpi = Stages[Stage];
                    Zones[0] = new byte[] { 0, 0 }.Concat(prof.Skip(0xD1).Take(10)).ToArray();
                    Zones[1] = new byte[] { 1, 0 }.Concat(prof.Skip(0xDC).Take(10)).ToArray();
                }
                if (fn == 4) { reply[0] = (byte)(current >> 8); reply[1] = (byte)current; }
                if (fn == 5)
                {
                    if (!Sectors.ContainsKey(readUInt16(0))) throw new HidppError(2, "HID++ error 0x02");
                    Sectors[readUInt16(0)].Skip(readUInt16(2)).Take(16).ToArray().CopyTo(reply, 0);
                }
                if (fn == 6) { writeTo = readUInt16(0); writeLength = readUInt16(4); pending.Clear(); }
                if (fn == 7) pending.AddRange(parameters.Take(16));
                if (fn == 8) Sectors[writeTo] = pending.Take(writeLength).ToArray();
                if (fn == 0xB) reply[0] = (byte)Math.Max(0, Stage);
                if (fn == 0xC) { Stage = parameters[0]; Dpi = Stages[Stage]; }
                break;
            case 5:
                if (fn == 0) { reply[0] = 0x08; reply[1] = 0x0C; }
                if (fn == 1) reply[0] = WheelMode;
                if (fn == 2) { WheelMode = parameters[0]; reply[0] = WheelMode; }
                break;
        }
        return reply;
    }
}

class FakeG9x : Hidpp
{
    public readonly Dictionary<int, byte[]> Registers = new Dictionary<int, byte[]>();
    public readonly Dictionary<int, byte[]> Pages = new Dictionary<int, byte[]>();

    public FakeG9x() : base(false)
    {
        Registers[0x0F] = new byte[3];
        Registers[0x51] = FakeHidpp.Hex("221200");
        Registers[0x57] = FakeHidpp.Hex("FF0000");
        Registers[0x64] = FakeHidpp.Hex("020000");
        Registers[0x63] = FakeHidpp.Hex("22002200800100000000000000000000");
        Registers[0xF1] = FakeHidpp.Hex("015802");
        Pages[0] = new byte[512];
        Pages[1] = Page("020001030003040007");
        Pages[2] = Page("FF00008000110011221100220022221200440044222200880088122200000000122101011010028101008102008104008108008110008F0000830100830200830400830800");
        Pages[3] = Page("00FF00800009000922110022002222120055005522220000000012220000000012210101101001");
        Pages[4] = Page("0000FF800011001122110022002222120044004422220000000012220000000012210101101002");
    }

    // A G500s: no colour light (0x57), DPI in units of 50 (cvuchener's hidpp), stages 400, 800, 1600, 3200 on page 2.
    public static FakeG9x G500s()
    {
        var mouse = new FakeG9x();
        mouse.Registers.Remove(0x57);
        mouse.Registers[0x63] = FakeHidpp.Hex("10001000800100000000000000000000");
        mouse.Pages[2] = Page("000000800008000822110010001022120020002022220040004012220000000012210101101002");
        return mouse;
    }

    // A G5 or G9: a one-byte 0x63 (0x80 + index), no colour light and no profile memory.
    public static FakeG9x Short(byte resolution)
    {
        var mouse = new FakeG9x();
        mouse.Registers.Remove(0x57);
        mouse.Registers.Remove(0x0F);
        mouse.Registers[0x63] = new byte[] { resolution, 0, 0 };
        mouse.Pages.Clear();
        return mouse;
    }

    public static byte[] Page(string start)
    {
        var page = Enumerable.Repeat((byte)0xFF, 512).ToArray();
        FakeHidpp.Hex(start).CopyTo(page, 0);
        MouseProtocol.WriteChecksum(page);
        return page;
    }

    public readonly List<string> Writes = new List<string>();
    // The next copy of a second half fails once, after its page was erased: a save cut off halfway.
    public bool FailSecondHalf;
    int nextSequence = -1, uploadPage, uploadOffset, uploadLeft;

    public override void Dispose() { }

    protected override byte[] Send(byte featureIndex, int fn, byte[] parameters) { throw new HidppError(1, "HID++ 1.0 only"); }

    protected override void SendUpload(byte subId, byte sequence, byte[] data)
    {
        if (sequence != nextSequence || (subId == 0x92) != (sequence == 0)) throw new HidppError(0x05, "out of sequence");
        nextSequence++;
        int start = 0;
        if (subId == 0x92)
        {
            uploadPage = data[1];
            uploadOffset = data[2] * 2;
            uploadLeft = data[5] << 8 | data[6];
            start = 9;
        }
        if (uploadPage != 0) throw new HidppError(0x07, "uploads go to RAM");
        for (int i = start; i < 16 && uploadLeft > 0; i++, uploadLeft--) Pages[0][uploadOffset++] = data[i];
    }

    protected override byte[] SendRegister(byte subId, byte register, byte[] parameters)
    {
        var reply = new byte[16];
        if (subId == 0x80 && register == 0xA1) { nextSequence = 0; return reply; }
        if (subId == 0x82 && register == 0xA0)
        {
            int target = parameters[6], wordOffset = parameters[7], length = parameters[10] << 8 | parameters[11];
            if (target < 2) throw new HidppError(0x07, "protected page");
            if (FailSecondHalf && parameters[0] == 0x03 && wordOffset == 0x80)
            {
                FailSecondHalf = false; throw new HidppError(0x04, "copy failed");
            }
            Writes.Add((parameters[0] == 0x02 ? "erase " : "copy ") + target + ":" + wordOffset);
            if (parameters[0] == 0x02) Pages[target] = Enumerable.Repeat((byte)0xFF, 512).ToArray();
            else for (int i = 0; i < length; i++) Pages[target][wordOffset * 2 + i] &= Pages[parameters[2]][parameters[3] * 2 + i];
            return reply;
        }
        if (subId == 0x83 && register == 0xA2)
        {
            byte[] page;
            int pageNumber = parameters.Length > 0 ? parameters[0] : 0, offset = parameters.Length > 1 ? parameters[1] * 2 : 0;
            if (!Pages.TryGetValue(pageNumber, out page)) throw new HidppError(3, "no such page");
            Array.Copy(page, offset, reply, 0, 16);
            return reply;
        }
        byte[] value;
        if (!Registers.TryGetValue(register, out value)) throw new HidppError(2, "no such register");
        if (subId == 0x80 || subId == 0x82)
        {
            var stored = new byte[value.Length];
            Array.Copy(parameters, stored, Math.Min(parameters.Length, stored.Length));
            Registers[register] = stored;
            return reply;
        }
        Array.Copy(value, reply, value.Length);
        return reply;
    }
}

class FakeHeadset : Hidpp
{
    public int Sidetone = 30, AutoOffMinutes = 30;
    // The PRO X's equalizer, from its report: ten bands from 32 Hz to 16 kHz, 12 dB up or down, stored as below.
    public sbyte[] Bands = { 0, 0, 0, 0, 0, 1, 3, 5, 6, 6 };
    public int Persistence = -1;
    static readonly int[] Frequencies = { 32, 64, 125, 250, 500, 1000, 2000, 4000, 8000, 16000 };

    public FakeHeadset() : base(false) { }

    public override void Dispose() { }

    protected override byte[] Send(byte featureIndex, int fn, byte[] parameters)
    {
        Latency.Pause();
        var reply = new byte[16];
        if (featureIndex == 0)
        {
            int id = parameters[0] << 8 | parameters[1];
            reply[0] = (byte)(id == 0x8300 ? 1 : id == 0x1F20 ? 2 : id == 0x8310 ? 3 : 0);
        }
        else if (featureIndex == 3)
        {
            if (fn == 0) { reply[0] = (byte)Bands.Length; reply[1] = 12; reply[2] = 1; }
            if (fn == 1)
            {
                reply[0] = parameters[0];
                for (int i = 0; i < 7 && parameters[0] + i < Frequencies.Length; i++)
                {
                    reply[1 + 2 * i] = (byte)(Frequencies[parameters[0] + i] >> 8);
                    reply[2 + 2 * i] = (byte)Frequencies[parameters[0] + i];
                }
            }
            if (fn == 2) for (int i = 0; i < Bands.Length; i++) reply[i] = (byte)Bands[i];
            if (fn == 3) { Persistence = parameters[0]; for (int i = 0; i < Bands.Length; i++) Bands[i] = (sbyte)parameters[1 + i]; }
        }
        else if (featureIndex == 1) { if (fn == 0) reply[0] = (byte)Sidetone; if (fn == 1) Sidetone = parameters[0]; }
        else if (featureIndex == 2)
        {
            if (fn == 0) { reply[0] = 0x0F; reply[1] = 0x56; reply[2] = 1; }
            if (fn == 1) reply[0] = (byte)AutoOffMinutes;
            if (fn == 2) AutoOffMinutes = parameters[0];
        }
        return reply;
    }
}

// Answers like the MX Master 3S, MX Keys S and MX Creative Keypad in OpenLogi's captures and the keypad's report:
// a device only calls the features its FoundDevice lists.
class FakeOther : Hidpp
{
    static readonly Dictionary<int, byte> Index = new Dictionary<int, byte>
    {
        { 0x8040, 2 }, { 0x2201, 3 }, { 0x2110, 4 }, { 0x2121, 5 }, { 0x2150, 6 }, { 0x1982, 7 }, { 0x1814, 8 }, { 0x1815, 9 }
    };
    static readonly string[] Names = { "Desk PC", "MacBook" };
    public int Bright = 100, Dpi = 1000, Shift = 10, Wheel, ThumbInvert, Level = 3, Host;
    public bool RefuseDpi;
    // An MX Keys S's 0x1982 fn 0, from its owner's report: manual, level 3, fading after 30 s, 30 s and 300 s.
    public byte[] Backlight = { 0x01, 0x1D, 0x3D, 0x03, 0x00, 0x03, 0x06, 0x00, 0x06, 0x00, 0x3C, 0x00, 0x00, 0x00, 0x00, 0x00 };

    public FakeOther() : base(false) { }

    public override void Dispose() { }

    protected override void Write(byte featureIndex, int fn, byte[] parameters)
    {
        if (featureIndex == Index[0x1814] && fn == 1) Host = parameters[0];
    }

    protected override byte[] Send(byte featureIndex, int fn, byte[] parameters)
    {
        Latency.Pause();
        var reply = new byte[16];
        if (featureIndex == 0)
        {
            byte index; reply[0] = Index.TryGetValue(parameters[0] << 8 | parameters[1], out index) ? index : (byte)0; return reply;
        }
        int id = Index.First(pair => pair.Value == featureIndex).Key;
        switch (id)
        {
            case 0x8040:
                if (fn == 0) { reply[1] = 100; reply[2] = 0x65; reply[3] = 0x10; }
                if (fn == 1) { reply[0] = (byte)(Bright >> 8); reply[1] = (byte)Bright; }
                if (fn == 2) Bright = parameters[0] << 8 | parameters[1];
                break;
            case 0x2201:
                if (fn == 1) { reply[1] = 0x00; reply[2] = 0xC8; reply[3] = 0xE0; reply[4] = 0x32; reply[5] = 0x1F; reply[6] = 0x40; }
                if (fn == 2) { reply[1] = (byte)(Dpi >> 8); reply[2] = (byte)Dpi; reply[3] = 0x03; reply[4] = 0xE8; }
                if (fn == 3 && RefuseDpi) throw new HidppError(5, "refused");
                if (fn == 3) Dpi = parameters[1] << 8 | parameters[2];
                break;
            case 0x2110:
                if (fn == 1 && parameters[1] != 0) Shift = parameters[1];
                reply[0] = 2; reply[1] = (byte)Shift; reply[2] = 10;
                break;
            case 0x2121:
                if (fn == 0) { reply[0] = 15; reply[1] = 0x0C; reply[2] = 24; reply[3] = 26; }
                if (fn == 2) Wheel = parameters[0];
                if (fn == 1 || fn == 2) reply[0] = (byte)Wheel;
                break;
            case 0x2150:
                if (fn == 2) ThumbInvert = parameters[1] & 1;
                if (fn == 1 || fn == 2) reply[1] = (byte)ThumbInvert;
                break;
            case 0x1982:
                if (fn == 0) Array.Copy(Backlight, reply, 16);
                if (fn == 1)
                {
                    Backlight[0] = parameters[0]; Backlight[1] = parameters[1]; Backlight[5] = parameters[3];
                    Array.Copy(parameters, 4, Backlight, 6, 6);
                    if ((parameters[1] >> 3 & 3) == 3) Level = parameters[3];
                }
                if (fn == 2)
                {
                    reply[0] = 8; reply[1] = (byte)Level;
                    reply[2] = (byte)((Backlight[0] & 1) == 0 ? 0 : (Backlight[1] >> 3 & 3) == 1 ? 2 : 5);
                }
                break;
            case 0x1814:
                if (fn == 0) { reply[0] = 3; reply[1] = (byte)Host; reply[2] = 1; }
                break;
            case 0x1815:
                if (fn == 0) { reply[0] = 0x1F; reply[1] = 0x0E; reply[2] = 3; reply[3] = 0xFF; }
                if (fn == 1)
                {
                    reply[0] = parameters[0]; reply[1] = (byte)(parameters[0] < Names.Length ? 1 : 0); reply[2] = 5;
                    reply[4] = (byte)(parameters[0] < Names.Length ? Names[parameters[0]].Length : 0); reply[5] = 32;
                }
                if (fn == 3 && parameters[0] < Names.Length)
                {
                    reply[0] = parameters[0]; reply[1] = parameters[1];
                    var name = System.Text.Encoding.UTF8.GetBytes(Names[parameters[0]]);
                    for (int offset = 0; offset < 14 && parameters[1] + offset < name.Length; offset++)
                        reply[2 + offset] = name[parameters[1] + offset];
                }
                break;
        }
        return reply;
    }
}
