using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

// The fakes answer after a short pause so --demo feels like a device; the self-test sets it to 0 (on Windows a 5 ms sleep lasts about 15).
static class Latency
{
    public static int Ms = 5;
    public static void Pause() { if (Ms > 0) Thread.Sleep(Ms); }
}

class KeyLayout
{
    public string Name { get; set; }
    public string[] Rows { get; set; }
    public string[] Ids { get; set; }
    // The ids are 0x8070 zones (the G213), not lighting ids.
    public bool Zones { get; set; }

    // The ISO picture, built from the ANSI one, for a keyboard whose 0x4540 layout is above 2: \ goes and Enter becomes an L from
    // the Tab row (it keeps its id), the Caps row's Enter becomes #, the left Shift shortens for <>, the right Alt is AltGr.
    // # and <> are lighting ids 2F and 61 (USB codes minus 3), or on a layout of zones the zone of the key they replace or sit by.
    // Null for a layout without these keys.
    public KeyLayout Iso()
    {
        var rows = Rows.Select(row => row.Split('|').ToList()).ToArray();
        var ids = Rows.Select((row, rowIndex) => Ids != null && rowIndex < Ids.Length ? Ids[rowIndex].Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).ToList() : new List<string>()).ToArray();
        int tab = Array.FindIndex(rows, row => row.Contains("\\:1.5")), caps = Array.FindIndex(rows, row => row.Contains("Enter:2.25")), shift = Array.FindIndex(rows, row => row.Contains("Shift:2.25"));
        if (tab < 0 || caps < 0 || shift < 0) return null;
        int backslash = rows[tab].IndexOf("\\:1.5"), enter = rows[caps].IndexOf("Enter:2.25"), leftShift = rows[shift].IndexOf("Shift:2.25");
        int backslashKey = KeyIndex(rows[tab], backslash), enterKey = KeyIndex(rows[caps], enter), shiftKey = KeyIndex(rows[shift], leftShift);
        string enterId = enterKey < ids[caps].Count ? ids[caps][enterKey] : "--";

        rows[tab][backslash] = "Enter:1.25:2:.25";
        rows[tab].Insert(backslash, "_.25");
        if (backslashKey < ids[tab].Count) ids[tab][backslashKey] = enterId;

        rows[caps][enter] = "#";
        if (enter + 1 < rows[caps].Count && rows[caps][enter + 1].StartsWith("_"))
            rows[caps][enter + 1] = "_" + (float.Parse(rows[caps][enter + 1].Substring(1), CultureInfo.InvariantCulture) + 1.25f).ToString("0.##", CultureInfo.InvariantCulture);
        else if (enter + 1 < rows[caps].Count) rows[caps].Insert(enter + 1, "_1.25");
        if (!Zones && enterKey < ids[caps].Count) ids[caps][enterKey] = "2F";

        rows[shift][leftShift] = "Shift:1.25";
        rows[shift].Insert(leftShift + 1, "<>");
        if (shiftKey < ids[shift].Count) ids[shift].Insert(shiftKey + 1, Zones ? ids[shift][shiftKey] : "61");

        foreach (var row in rows)
        {
            var alts = Enumerable.Range(0, row.Count).Where(tokenIndex => row[tokenIndex].Split(':')[0] == "Alt").ToList();
            if (alts.Count == 2) row[alts[1]] = "AltGr" + row[alts[1]].Substring(3);
        }
        return new KeyLayout { Name = Name, Zones = Zones, Rows = rows.Select(row => string.Join("|", row)).ToArray(), Ids = Ids == null ? null : ids.Select(row => string.Join(" ", row)).ToArray() };
    }

    static int KeyIndex(List<string> row, int tokenIndex) { return row.Take(tokenIndex).Count(cell => !cell.StartsWith("_")); }

    public List<int> KeyIds()
    {
        var all = new List<int>();
        for (int rowIndex = 0; rowIndex < Rows.Length; rowIndex++)
        {
            var ids = Ids != null && rowIndex < Ids.Length ? Ids[rowIndex].Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries) : new string[0];
            int keyIndex = 0;
            foreach (var cell in Rows[rowIndex].Split('|'))
            {
                if (cell.StartsWith("_")) continue;
                all.Add(keyIndex < ids.Length && ids[keyIndex] != "--" ? Convert.ToInt32(ids[keyIndex], 16) : -1);
                keyIndex++;
            }
        }
        return all;
    }

    public static KeyLayout Load(string file)
    {
        string text;
        using (var stream = typeof(KeyLayout).Assembly.GetManifestResourceStream(file))
        using (var reader = new System.IO.StreamReader(stream)) text = reader.ReadToEnd();
        return Parse(text);
    }

    // The layout as its .json file holds it: models/iso/*-iso.json are written with this by selftest --record.
    public string ToJson()
    {
        Func<string, string> quote = s => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        Func<string[], string> list = items => "[\n" + string.Join(",\n", items.Select(item => "    " + quote(item))) + "\n  ]";
        return "{\n  \"name\": " + quote(Name) + ",\n" + (Zones ? "  \"kind\": \"zones\",\n" : "") + "  \"rows\": " + list(Rows) + ",\n  \"ids\": " + list(Ids ?? new string[0]) + "\n}\n";
    }

    public static KeyLayout Parse(string text)
    {
        var kind = Strings(text, "kind");
        return new KeyLayout { Name = Strings(text, "name")[0], Rows = Strings(text, "rows"), Ids = Strings(text, "ids"), Zones = kind != null && kind[0] == "zones" };
    }

    static string[] Strings(string text, string key)
    {
        const string quoted = @"""(?:[^""\\]|\\.)*""";
        var match = Regex.Match(text, @"""" + key + @"""\s*:\s*(" + quoted + @"|\[(?:\s*" + quoted + @"\s*,?)*\s*\])");
        if (!match.Success) return null;
        return Regex.Matches(match.Groups[1].Value, quoted).Cast<Match>().Select(found => Regex.Unescape(found.Value.Substring(1, found.Value.Length - 2))).ToArray();
    }
}

// In-memory G515 that answers the HID++ calls the app makes, for running without the keyboard.
class FakeHidpp : Hidpp
{
    static readonly Dictionary<int, byte> Index = new Dictionary<int, byte> { { 0x8071, 9 }, { 0x8040, 14 }, { 0x8101, 15 }, { 0x8081, 16 }, { 0x4540, 17 }, { 0x1004, 18 } };
    static readonly byte[] EffectIds = { 0x00, 0x01, 0x0A, 0x15, 0x16, 0x17 };
    public readonly Dictionary<int, int> Keys = new Dictionary<int, int>();
    readonly byte[] control = new byte[2];

    public void PowerCycle() { control[0] = control[1] = 0; }
    public int Frames, Battery = 82;
    public bool Charging;

    public static byte[] Hex(string hex) { return Enumerable.Range(0, hex.Length / 2).Select(i => Convert.ToByte(hex.Substring(i * 2, 2), 16)).ToArray(); }

    public Dictionary<int, byte[]> Flash = new Dictionary<int, byte[]>
    {
        { 0x0101, Hex("0101003c012cffffffffffffffffffffffffffffffffffffffffffffffff18ff008180aaff00006400000000000000000000000019ff008180aaff0000640000000000000000000000000300000000000000000010000104000200ffffffffffffffff") },
        { 0x0100, Hex("5e380925010181014063996dec0101ff") },
    };
    List<byte> buffer = new List<byte>();
    int brightness = 100, mode = 0x03;
    public byte[] Live = new byte[12];

    public FakeHidpp() : base(false) { }

    // What the keys show now: effect ID and its 10 parameter bytes, plus brightness.
    public void State(out byte id, out byte[] parameters, out int bright)
    {
        bright = brightness;
        var live = Live;
        if (mode == 0 && live[1] < EffectIds.Length && live.Length == 12) { id = EffectIds[live[1]]; parameters = live.Skip(2).ToArray(); return; }
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
                if (parameters[1] >= EffectIds.Length) throw new HidppError(2, "invalid argument");
                reply[1] = parameters[1]; reply[3] = EffectIds[parameters[1]];
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
                    if (id >= 0x01 && id <= 0x4F || id >= 0x61 && id <= 0x6F) reply[2 + (id - parameters[1] * 112) / 8] |= (byte)(1 << (id % 8));
            }
            if (fn == 6)
            {
                for (int i = 3; i < 16 && parameters.Length > i; i++)
                {
                    if (parameters[i] == 0 && i > 3) break;
                    if (parameters[i] < 0x01 || parameters[i] > 0x4F && parameters[i] < 0x61 || parameters[i] > 0x6F) throw new HidppError(2, "invalid argument");
                    Keys[parameters[i]] = parameters[0] << 16 | parameters[1] << 8 | parameters[2];
                }
            }
            if (fn == 7) Frames++;
        }
        else if (featureIndex == Index[0x4540]) reply[0] = 0x0D;
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
    static readonly Dictionary<int, byte> Index = new Dictionary<int, byte> { { 0x8070, 4 }, { 0x8040, 8 }, { 0x4540, 9 } };
    static readonly byte[] Effects = { 0x00, 0x01, 0x03, 0x07, 0x0A };
    public readonly byte[][] Live = new byte[6][];
    public int Count = 4;
    public bool G213;

    public FakeSpeaker() : base(false) { }

    // From a G213's report: zone 0 is the whole keyboard, zones 1–5 (locations 6–10) take Off and Static only.
    byte[] ZoneEffects(int zone) { return !G213 ? Effects : zone == 0 ? new byte[] { 0x00, 0x01, 0x0A, 0x03, 0x04 } : new byte[] { 0x00, 0x01 }; }

    public override void Dispose() { }

    protected override byte[] Send(byte featureIndex, int fn, byte[] parameters)
    {
        Latency.Pause();
        var reply = new byte[16];
        if (featureIndex == 0) { byte i; reply[0] = Index.TryGetValue(parameters[0] << 8 | parameters[1], out i) ? i : (byte)0; }
        else if (featureIndex == Index[0x8070])
        {
            if (fn == 0) reply[0] = (byte)Count;
            else if (fn == 1) { reply[0] = parameters[0]; reply[2] = (byte)(G213 ? (parameters[0] == 0 ? 1 : parameters[0] + 5) : parameters[0] + 1); reply[3] = (byte)ZoneEffects(parameters[0]).Length; }
            else if (fn == 2) { var listed = ZoneEffects(parameters[0]); if (parameters[1] >= listed.Length) throw new HidppError(2, "invalid argument"); reply[0] = parameters[0]; reply[1] = parameters[1]; reply[3] = listed[parameters[1]]; }
            else if (fn == 3) { if (parameters[0] >= Count || parameters[1] >= ZoneEffects(parameters[0]).Length) throw new HidppError(2, "invalid argument"); Live[parameters[0]] = parameters.Take(12).ToArray(); }
        }
        else if (featureIndex == Index[0x4540] && G213) reply[0] = 0x0D;
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

    public FakeG510() : base(false) { }

    public override void Dispose() { }

    protected override byte[] ReadFeature(byte id)
    {
        Latency.Pause();
        byte[] report;
        if (!Features.TryGetValue(id, out report)) throw new System.IO.IOException("Couldn't read feature report " + id.ToString("X2") + " (error 31)");
        var reply = new byte[258];
        Array.Copy(report, reply, report.Length);
        return reply;
    }

    protected override void WriteFeature(byte[] report)
    {
        Latency.Pause();
        byte[] old;
        if (!Features.TryGetValue(report[0], out old)) throw new System.IO.IOException("Couldn't set feature report " + report[0].ToString("X2") + " (error 31)");
        Features[report[0]] = report.Take(old.Length).ToArray();
    }
}

class FakeLight : Hidpp
{
    static readonly Dictionary<int, byte> Index = new Dictionary<int, byte> { { 0x1990, 6 }, { 0x8040, 0x0A }, { 0x8071, 0x0B }, { 0x8081, 0x0C } };
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
                if (parameters[0] >= Groups.Length || parameters[1] != 0xFF && parameters[1] >= Groups[parameters[0]].Length) throw new HidppError(2, "invalid argument");
                reply[0] = parameters[0];
                reply[1] = parameters[1];
                if (parameters[1] == 0xFF) reply[4] = (byte)Groups[parameters[0]].Length; else reply[3] = Groups[parameters[0]][parameters[1]];
            }
            // A real Beam LX answers 0x05 to fn 1 unless software holds the clusters (fn 5 bit 0).
            else if (fn == 1) { if (!Controlled) throw new HidppError(5, "Logitech internal"); Effect = parameters.Take(13).ToArray(); }
            else if (fn == 5) { if (parameters[0] == 1) Controlled = (parameters[1] & 1) != 0; reply[1] = (byte)(Controlled ? 1 : 0); }
            else throw new HidppError(7, "invalid function");
        }
        else if (featureIndex == Index[0x8081])
        {
            if (fn == 0) { reply[1] = parameters[1]; if (parameters[1] == 0) reply[2] = 0xFE; }
            else if (fn == 1) { for (int i = 0; i + 3 < parameters.Length && i < 16; i += 4) if (parameters[i] >= 1 && parameters[i] <= 7) Colors[parameters[i] - 1] = parameters[i + 1] << 16 | parameters[i + 2] << 8 | parameters[i + 3]; }
            else if (fn == 7) Frames++;
        }
        else throw new HidppError(2, "invalid argument");
        return reply;
    }
}

class FakeG915 : Hidpp
{
    Dictionary<int, byte> Index = new Dictionary<int, byte> { { 0x8071, 10 }, { 0x8040, 20 }, { 0x8100, 21 }, { 0x4540, 16 } };
    public byte[][] Ids = { new byte[] { 0x00, 0x01, 0x03, 0x0A }, new byte[] { 0x00, 0x01, 0x0A, 0x03, 0x04, 0x0B, 0x0C } };
    // Profile 1 of an owner's G915 TKL (Cycle at 10 s on both clusters, power save after 60 s, off after 300 s), and its factory copy (Cycle at 5 s).
    public readonly Dictionary<int, byte[]> Sectors = new Dictionary<int, byte[]> { { 1, FakeHidpp.Hex("010000000000000000000000000000000000FFFFFFFFFFFFFFFFFFFF3C002C018002003A8002003B8002003C8002003D8002003E8002003F800200408002004180020042800200438002004480020045900DFF01900DFF02900DFF03FFFFFFFF00040000000400020004000400040006000400080004000A0004000C0004000E00040010000400120004001400040016900DFF01900DFF02900DFF03FFFFFFFF500052004F00460049004C0045005F004E0041004D0045005F00440045004600410055004C00540000000000000000000300000000002710320000030000000000271032000003000000000027101900000300000000002710190000004349") }, { 0x101, FakeHidpp.Hex("010000000000000000000000000000000000FFFFFFFFFFFFFFFFFFFF3C002C018002003A8002003B8002003C8002003D8002003E8002003F800200408002004180020042800200438002004480020045900DFF01900DFF02900D0001900D0002900D0003FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF0300000000001388640000030000000000138864000003000000000013883200000300000000001388320000FFFFFF") } };
    public int Reloads, Power = 1;
    List<byte> pending = new List<byte>();
    int writeTo, writeLength;
    public readonly byte[][] Live = { new byte[13], new byte[13] };
    public int Brightness = 80, Claimed;
    public byte Layout = 1;

    public FakeG915() : base(false) { }

    public static FakeG915 G512X()
    {
        return new FakeG915
        {
            Index = new Dictionary<int, byte> { { 0x8071, 6 }, { 0x8081, 7 }, { 0x8040, 8 }, { 0x8101, 9 }, { 0x4540, 10 } },
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
        else if (Index.ContainsKey(0x8100) && featureIndex == Index[0x8100])
        {
            if (fn == 0) FakeHidpp.Hex("01040103030F1000FF3604").CopyTo(reply, 0);
            if (fn == 2) reply[0] = 1;
            if (fn == 3) Reloads++;
            if (fn == 4) reply[1] = 1;
            if (fn == 5) Sectors[parameters[0] << 8 | parameters[1]].Skip(parameters[2] << 8 | parameters[3]).Take(16).ToArray().CopyTo(reply, 0);
            if (fn == 6) { writeTo = parameters[0] << 8 | parameters[1]; writeLength = parameters[4] << 8 | parameters[5]; pending.Clear(); }
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
            if (fn == 5) Claimed = parameters[1];
            if (fn == 8 && parameters[0] == 1) Power = parameters[1];
        }
        else if (Index.ContainsKey(0x8081) && featureIndex == Index[0x8081] && fn == 0)
        {
            reply[1] = parameters[1];
            for (int id = parameters[1] * 112; id < parameters[1] * 112 + 112; id++)
                if (id <= 0x6F || id >= 0xA0 && id <= 0xC0 || id >= 0xF2 && id <= 0xFE) reply[2 + (id - parameters[1] * 112) / 8] |= (byte)(1 << ((id - parameters[1] * 112) % 8));
        }
        else if (!Index.ContainsValue(featureIndex)) throw new HidppError(2, "invalid argument");
        return reply;
    }
}

class FakeMouse : Hidpp
{
    public int Dpi = 1600, Stage = 2, Reloads, Rate = 1, ZoneCount = 2;
    public string Info = "0103010501081000ff0a04";
    public bool Wheel, Wide;
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
        var bindings = new[] { "80010001", "80010002", "80010004", "80010008", "80010010", "90070000", "90040000", "90030000", "90080000", "90020000", "90010000" };
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
        var bindings = new[] { "80010001", "80010002", "80010004", "80010008", "80010010", "9007FFFF", "9004FFFF", "9003FFFF", "900AFFFF", "9002FFFF", "9001FFFF" };
        for (int i = 0; i < bindings.Length; i++) FakeHidpp.Hex(bindings[i]).CopyTo(mouse.Sectors[0x0101], 0x20 + 4 * i);
        mouse.Sectors[0x0000] = new byte[255];
        return mouse;
    }

    public static FakeMouse For(FoundDevice found) { return found.Models.Contains("C08B") ? Hero() : found.WirelessId == 0x407F ? G502() : new FakeMouse(); }

    public static FakeMouse G305()
    {
        var mouse = new FakeMouse(true) { ZoneCount = 1, Info = "0103010101060600ff0a02", Dpi = 800, Stage = 1 };
        mouse.Sectors[0x0101] = FakeHidpp.Hex("010100900120034006800C0000FFFFFFFF00FFFFFFFFFFFFFFFFFFFFFFFFFFFF800100018001000280010004800100088001001090050000FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF0000000000001F400000000000000000001F40000000FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF");
        return mouse;
    }

    public static FakeMouse ProX2()
    {
        var mouse = new FakeMouse { Wide = true, ZoneCount = 0, Info = "0106010501051000ff0a04", Dpi = 800, Stage = 1, Rate = 3 };
        var profile = Enumerable.Repeat((byte)0xFF, 255).ToArray();
        FakeHidpp.Hex("03030100" + "9001900102" + "2003200302" + "4006400602" + "800C800C02" + "0000000000").CopyTo(profile, 0);
        MouseProtocol.WriteChecksum(profile);
        mouse.Sectors[0x0001] = profile;
        mouse.Sectors[0x0101] = (byte[])profile.Clone();
        return mouse;
    }

    int[] Stages { get { return Wide ? MouseProtocol.WideStages(Sectors[current], new[] { 100, 32000 }) : MouseProtocol.StagesOf(Sectors[current]); } }

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
                reply[0] = (byte)(id == 0x2201 ? 1 : id == 0x8060 ? 2 : id == 0x8070 ? 3 : id == 0x8100 ? 4 : id == 0x2121 && Wheel ? 5 : 0);
                if (Wide) reply[0] = (byte)(id == 0x2202 ? 6 : id == 0x8061 ? 7 : id == 0x8100 ? 4 : 0);
                break;
            case 6:
                if (fn == 1) { reply[1] = 5; reply[2] = 3; }
                if (fn == 2) { reply[2] = parameters[2]; if (parameters[2] == 0) FakeHidpp.Hex("0064e0327d000000").CopyTo(reply, 3); }
                if (fn == 5) { reply[1] = reply[5] = (byte)(Dpi >> 8); reply[2] = reply[6] = (byte)Dpi; reply[3] = 0x03; reply[4] = 0x20; reply[9] = 2; }
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
                if (fn == 2) { if (parameters[1] >= Ids.Length) throw new HidppError(2, "invalid argument"); reply[0] = parameters[0]; reply[1] = parameters[1]; reply[3] = Ids[parameters[1]]; }
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
                    Stage = prof[1];
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
            if (FailSecondHalf && parameters[0] == 0x03 && wordOffset == 0x80) { FailSecondHalf = false; throw new HidppError(0x04, "copy failed"); }
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

    public FakeHeadset() : base(false) { }

    public override void Dispose() { }

    protected override byte[] Send(byte featureIndex, int fn, byte[] parameters)
    {
        Latency.Pause();
        var reply = new byte[16];
        if (featureIndex == 0) { int id = parameters[0] << 8 | parameters[1]; reply[0] = (byte)(id == 0x8300 ? 1 : id == 0x1F20 ? 2 : 0); }
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

// Answers like the MX Master 3S, MX Keys S and MX Creative Keypad in OpenLogi's captures and the keypad's report: a device only calls the features its FoundDevice lists.
class FakeOther : Hidpp
{
    static readonly Dictionary<int, byte> Index = new Dictionary<int, byte> { { 0x8040, 2 }, { 0x2201, 3 }, { 0x2110, 4 }, { 0x2121, 5 }, { 0x2150, 6 }, { 0x1982, 7 }, { 0x1814, 8 }, { 0x1815, 9 } };
    static readonly string[] Names = { "Desk PC", "MacBook" };
    public int Bright = 100, Dpi = 1000, Shift = 10, Wheel, ThumbInvert, Level = 3, Host;
    public bool RefuseDpi;
    // An MX Keys S's 0x1982 fn 0, from its owner's report: manual, level 3, fading after 30 s, 30 s and 300 s.
    public byte[] Backlight = { 0x01, 0x1D, 0x3D, 0x03, 0x00, 0x03, 0x06, 0x00, 0x06, 0x00, 0x3C, 0x00, 0x00, 0x00, 0x00, 0x00 };

    public FakeOther() : base(false) { }

    public override void Dispose() { }

    protected override void Write(byte featureIndex, int fn, byte[] parameters) { if (featureIndex == Index[0x1814] && fn == 1) Host = parameters[0]; }

    protected override byte[] Send(byte featureIndex, int fn, byte[] parameters)
    {
        Latency.Pause();
        var reply = new byte[16];
        if (featureIndex == 0) { byte index; reply[0] = Index.TryGetValue(parameters[0] << 8 | parameters[1], out index) ? index : (byte)0; return reply; }
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
                if (fn == 2) { reply[0] = 8; reply[1] = (byte)Level; reply[2] = (byte)((Backlight[0] & 1) == 0 ? 0 : (Backlight[1] >> 3 & 3) == 1 ? 2 : 5); }
                break;
            case 0x1814:
                if (fn == 0) { reply[0] = 3; reply[1] = (byte)Host; reply[2] = 1; }
                break;
            case 0x1815:
                if (fn == 0) { reply[0] = 0x1F; reply[1] = 0x0E; reply[2] = 3; reply[3] = 0xFF; }
                if (fn == 1) { reply[0] = parameters[0]; reply[1] = (byte)(parameters[0] < Names.Length ? 1 : 0); reply[2] = 5; reply[4] = (byte)(parameters[0] < Names.Length ? Names[parameters[0]].Length : 0); reply[5] = 32; }
                if (fn == 3 && parameters[0] < Names.Length)
                {
                    reply[0] = parameters[0]; reply[1] = parameters[1];
                    var name = System.Text.Encoding.UTF8.GetBytes(Names[parameters[0]]);
                    for (int offset = 0; offset < 14 && parameters[1] + offset < name.Length; offset++) reply[2 + offset] = name[parameters[1] + offset];
                }
                break;
        }
        return reply;
    }
}

class KeyboardView : Form
{
    const float KeyUnit = 40, Pad = 16, HeaderHeight = 40;
    [DllImport("user32.dll")] static extern bool ReleaseCapture();
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
    readonly FakeHidpp keyboard;
    readonly List<Tuple<RectangleF, string>> keys = new List<Tuple<RectangleF, string>>();
    readonly List<Tuple<PointF, double>> ripples = new List<Tuple<PointF, double>>();
    readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
    float width, height, boardWidth, boardHeight, scale = 1;
    List<Point>[] cells;
    Game game;
    Func<Game> make;
    double nextStep;

    protected override bool ShowWithoutActivation { get { return true; } }

    public KeyboardView(FakeHidpp keyboard, KeyLayout model)
    {
        this.keyboard = keyboard;
        WindowHelpers.Round(this);
        foreach (var row in model.Rows)
        {
            float x = 0, y = height, tall = 0;
            foreach (var token in row.Split('|'))
            {
                if (token.StartsWith("_")) { x += float.Parse(token.Substring(1), System.Globalization.CultureInfo.InvariantCulture) * KeyUnit; continue; }
                var parts = token.Split(':');
                float keyWidth = parts.Length > 1 ? float.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture) * KeyUnit : KeyUnit;
                float keyHeight = parts.Length > 2 ? float.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture) * KeyUnit : KeyUnit;
                keys.Add(Tuple.Create(new RectangleF(Pad + x, HeaderHeight + Pad + y, keyWidth - 4, keyHeight - 4), parts[0]));
                x += keyWidth;
                tall = Math.Max(tall, Math.Min(keyHeight, KeyUnit));
                width = Math.Max(width, x);
            }
            height += tall > 0 ? tall : x;
        }
        var rows = keys.Select(key => key.Item1.Y).Distinct().OrderBy(top => top).ToList();
        cells = keys.Select(key =>
        {
            int row = rows.IndexOf(key.Item1.Y) - (rows.Count - Game.Rows);
            if (row < 0) return null;
            var columns = Enumerable.Range(0, Game.Columns).Where(column => Pad + (column + 0.5f) * KeyUnit >= key.Item1.X && Pad + (column + 0.5f) * KeyUnit < key.Item1.Right + 4).Select(column => new Point(column, row)).ToList();
            return columns.Count > 0 ? columns : null;
        }).ToArray();
        Text = model.Name;
        mines = Button("Mines", () => Play(() => new Mines(keys.Select(key => key.Item1).ToArray(), cells)));
        blocks = Button("Blocks", () => Play(() => new Blocks()));
        snake = Button("Snake", () => Play(() => new Snake(cells)));
        stop = Button("Stop", () => { game = null; RefreshControls(); });
        close = Button("", Hide);
        close.Icon = "close";
        close.AccessibleName = "Close";
        BackColor = Color.FromArgb(28, 28, 28);
        DoubleBuffered = true;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        boardWidth = width + 2 * Pad;
        boardHeight = HeaderHeight + height + 2 * Pad + 20;
        Rescale(WindowHelpers.At(Point.Empty));
        var timer = new System.Windows.Forms.Timer { Interval = 33 };
        timer.Tick += (s, e) => { Tick(); Invalidate(); };
        timer.Start();
        FormClosing += (s, e) => { if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); } };
        MouseDown += (s, e) =>
        {
            if (e.Button != MouseButtons.Left) return;
            float clickX = e.X / scale, clickY = e.Y / scale;
            if (clickY >= HeaderHeight + Pad / 2) { ripples.Add(Tuple.Create(new PointF(clickX, clickY), clock.Elapsed.TotalSeconds)); return; }
            ReleaseCapture();
            SendMessage(Handle, 0xA1, (IntPtr)2, IntPtr.Zero);
        };
    }

    public void Rescale(float newScale)
    {
        scale = newScale;
        ClientSize = new Size((int)Math.Ceiling(boardWidth * scale), (int)Math.Ceiling(boardHeight * scale));
        float right = boardWidth - 12;
        Place(close, ref right, 36);
        right -= 4;
        float playing = right;
        Place(snake, ref right, 72);
        Place(blocks, ref right, 72);
        Place(mines, ref right, 72);
        Place(stop, ref playing, 72);
        RefreshControls();
        Invalidate();
    }

    readonly FlatButton mines, blocks, snake, stop, close;

    FlatButton Button(string text, Action click)
    {
        var button = new FlatButton { Style = ButtonStyle.Ghost, Text = text, AccessibleName = text };
        button.Click += (s, e) => click();
        Controls.Add(button);
        return button;
    }

    void Place(FlatButton button, ref float right, float width)
    {
        right -= width;
        button.Bounds = new Rectangle((int)Math.Round(right * scale), (int)Math.Round(10 * scale), (int)Math.Round(width * scale), (int)Math.Round(28 * scale));
    }

    void RefreshControls()
    {
        if (mines == null) return;
        bool on = game != null;
        foreach (var button in new[] { mines, blocks, snake }) if (button.Visible == on) button.Visible = !on;
        if (stop.Visible != on) stop.Visible = on;
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == WindowHelpers.DpiChanged) Rescale(WindowHelpers.FromMessage(message));
        base.WndProc(ref message);
    }

    void Play(Func<Game> newGame)
    {
        make = newGame;
        game = newGame();
        RefreshControls();
        nextStep = clock.Elapsed.TotalMilliseconds + game.Interval;
        Activate();
    }

    void Tick()
    {
        if (game == null) return;
        double now = clock.Elapsed.TotalMilliseconds;
        if (game.Over) return;
        if (now >= nextStep) { game.Step(); nextStep = now + game.Interval; }
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys key)
    {
        if (game == null) return base.ProcessCmdKey(ref msg, key);
        if (key == Keys.Escape) { game = null; RefreshControls(); } else if (game.Over) Play(make); else game.Key(key);
        Invalidate();
        return true;
    }

    public static double PeriodOf(byte id, byte[] parameters)
    {
        double period = (id == 0x0A ? parameters[3] << 8 | parameters[4] : parameters[6] << 8 | parameters[7]) / 1000.0;
        if (id == 0x17) period = (parameters[6] << 8 | parameters[7]) / 20.0;
        return period <= 0 ? 5 : period;
    }

    public static Color EffectColorAt(byte id, byte[] parameters, double x, double y, double seconds, double ripple)
    {
        if (G515Profile.EffectById(id) == null) return Hsv(seconds / 8 - x, 1, 1);
        var color = Color.FromArgb(parameters[0], parameters[1], parameters[2]);
        double period = PeriodOf(id, parameters);
        int direction = parameters[9];
        switch (id)
        {
            case 0x01: return color;
            case 0x0A: return Scale(color, 0.5 - 0.5 * Math.Cos(2 * Math.PI * seconds / period));
            case 0x15: return Hsv(seconds / period, parameters[1] / 255.0, 1);
            case 0x16:
                double offset = direction == 6 ? x : direction == 3 ? -Math.Abs(x - 0.5) * 2 : direction == 8 ? Math.Abs(x - 0.5) * 2 : direction == 2 ? -y : direction == 7 ? y : -x;
                return Hsv(seconds / period + offset, parameters[1] / 255.0, 1);
            case 0x17: return Scale(color, ripple);
            default: return Color.Black;
        }
    }

    public static Color Hsv(double hue, double saturation, double value)
    {
        hue = (hue % 1 + 1) % 1 * 6;
        int i = (int)hue;
        double fraction = hue - i, low = value * (1 - saturation), falling = value * (1 - saturation * fraction), rising = value * (1 - saturation * (1 - fraction));
        double[] rgb = i == 0 ? new[] { value, rising, low } : i == 1 ? new[] { falling, value, low } : i == 2 ? new[] { low, value, rising } : i == 3 ? new[] { low, falling, value } : i == 4 ? new[] { rising, low, value } : new[] { value, low, falling };
        return Color.FromArgb((int)(rgb[0] * 255), (int)(rgb[1] * 255), (int)(rgb[2] * 255));
    }

    public static Color Scale(Color color, double factor)
    {
        factor = Math.Max(0, Math.Min(1, factor));
        return Color.FromArgb((int)(color.R * factor), (int)(color.G * factor), (int)(color.B * factor));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        byte id; byte[] parameters; int bright;
        keyboard.State(out id, out parameters, out bright);
        var effect = G515Profile.EffectById(id);
        double seconds = clock.Elapsed.TotalSeconds;
        double period = PeriodOf(id, parameters);
        ripples.RemoveAll(ripple => seconds - ripple.Item2 > 3);
        var graphics = e.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.ScaleTransform(scale, scale);
        using (var font = WindowHelpers.PixelFont(7.5f, 1))
        for (int keyIndex = 0; keyIndex < keys.Count; keyIndex++)
        {
            var key = keys[keyIndex];
            var box = key.Item1;
            Color fill = Color.FromArgb(30, 30, 32);
            double x = (box.X + box.Width / 2 - Pad) / width, y = (box.Y - HeaderHeight - Pad) / height;
            double glow = 0;
            if (id == 0x17)
                foreach (var ripple in ripples)
                {
                    double distance = Math.Sqrt(Math.Pow(box.X + box.Width / 2 - ripple.Item1.X, 2) + Math.Pow(box.Y + box.Height / 2 - ripple.Item1.Y, 2)) / KeyUnit;
                    double front = (seconds - ripple.Item2) * 8 / period;
                    glow = Math.Max(glow, Math.Max(0, 1 - Math.Abs(distance - front)) * Math.Max(0, 1 - (seconds - ripple.Item2) / 3));
                }
            Color color = EffectColorAt(id, parameters, x, y, seconds, glow);
            if (game != null)
            {
                int hit = 0;
                if (cells[keyIndex] != null) foreach (var cell in cells[keyIndex]) if (hit == 0) hit = game.At(cell.Y, cell.X);
                bool arrow = "↑↓←→".Contains(key.Item2) && key.Item2.Length == 1;
                color = hit != 0 ? (game.Over && !(game is Mines) ? Color.FromArgb(255, 40, 40) : Color.FromArgb(hit | unchecked((int)0xFF000000))) : arrow ? Color.FromArgb(120, 120, 120) : Color.FromArgb(45, 45, 45);
                if (hit != 0) fill = Scale(color, 0.45);
            }
            color = Scale(color, bright / 100.0);
            fill = Color.FromArgb(Math.Max(30, (int)(fill.R * bright / 100.0)), Math.Max(30, (int)(fill.G * bright / 100.0)), Math.Max(32, (int)(fill.B * bright / 100.0)));
            using (var path = new GraphicsPath())
            {
                float radius = 5;
                path.AddArc(box.X, box.Y, radius * 2, radius * 2, 180, 90);
                path.AddArc(box.Right - radius * 2, box.Y, radius * 2, radius * 2, 270, 90);
                path.AddArc(box.Right - radius * 2, box.Bottom - radius * 2, radius * 2, radius * 2, 0, 90);
                path.AddArc(box.X, box.Bottom - radius * 2, radius * 2, radius * 2, 90, 90);
                path.CloseFigure();
                using (var keyBrush = new SolidBrush(fill)) graphics.FillPath(keyBrush, path);
                using (var pen = new Pen(Color.FromArgb(55, 55, 58))) graphics.DrawPath(pen, path);
                using (var lit = new SolidBrush(color))
                    graphics.DrawString(key.Item2, font, lit, box, new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center });
                using (var under = new SolidBrush(Color.FromArgb(90, color))) graphics.FillRectangle(under, box.X + 6, box.Bottom - 4, box.Width - 12, 2);
            }
        }
        if (!WindowHelpers.Rounded) using (var pen = new Pen(Color.FromArgb(58, 58, 58))) graphics.DrawRectangle(pen, 0, 0, boardWidth - 1, boardHeight - 1);
        using (var font = WindowHelpers.PixelFont(9, 1))
        using (var brush = new SolidBrush(Color.FromArgb(150, 150, 150)))
        {
            graphics.DrawString(Text, font, brush, Pad, 17);
        }
        string what = effect == null ? "factory animation (approximation)" : effect.Name;
        if (id == 0x17) what += " — click the keys";
        if (game != null) what = game.Over ? "game over      score " + game.Score + "      any key to play again" : game.Help;
        using (var font = WindowHelpers.PixelFont(9, 1))
        using (var brush = new SolidBrush(Color.FromArgb(150, 150, 150)))
        {
            graphics.DrawString(what + "      brightness " + bright + "%", font, brush, Pad, boardHeight - 26);
            if (game is Mines && !game.Over)
            {
                float legendX = boardWidth - Pad;
                for (int i = Mines.LegendNames.Length - 1; i >= 0; i--)
                {
                    legendX -= graphics.MeasureString(Mines.LegendNames[i], font).Width;
                    graphics.DrawString(Mines.LegendNames[i], font, brush, legendX, boardHeight - 26);
                    legendX -= 12;
                    using (var swatch = new SolidBrush(Color.FromArgb(Mines.LegendColors[i] | unchecked((int)0xFF000000)))) graphics.FillRectangle(swatch, legendX, boardHeight - 23, 10, 10);
                    legendX -= 12;
                }
            }
        }
    }
}
