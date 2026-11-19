using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

class MouseLight
{
    public byte Effect = 0x01;
    public int Color = 0xFFFFFF, Period = 5000, Bright = 100, Direction = 6;
    public MouseLight Clone() { return (MouseLight)MemberwiseClone(); }

    public bool Same(MouseLight other)
    {
        return other != null && Effect == other.Effect && Color == other.Color && Period == other.Period && Bright == other.Bright
            && (Effect != 0x04 || Direction == other.Direction);
    }
}

class MouseAction
{
    public string Name;
    public byte[] Bytes;
    public MouseAction(string name, string hex)
    {
        Name = name;
        Bytes = Enumerable.Range(0, 4).Select(i => Convert.ToByte(hex.Substring(i * 2, 2), 16)).ToArray();
    }
}

static class Bindings
{
    public static readonly MouseAction[] Actions =
    {
        new MouseAction("Left click", "80010001"), new MouseAction("Right click", "80010002"), new MouseAction("Middle click", "80010004"),
        new MouseAction("Back", "80010008"), new MouseAction("Forward", "80010010"),
        new MouseAction("DPI up", "90030000"), new MouseAction("DPI down", "90040000"),
        new MouseAction("DPI cycle", "90050000"), new MouseAction("DPI shift", "90070000"),
        new MouseAction("Scroll left", "90010000"), new MouseAction("Scroll right", "90020000"),
        new MouseAction("Next profile", "90080000"), new MouseAction("Previous profile", "90090000"),
        new MouseAction("G-shift", "900B0000"), new MouseAction("Battery level", "900C0000"),
        new MouseAction("Play or pause", "800300CD"), new MouseAction("Next track", "800300B5"),
        new MouseAction("Previous track", "800300B6"),
        new MouseAction("Volume up", "800300E9"), new MouseAction("Volume down", "800300EA"), new MouseAction("Mute", "800300E2"),
        new MouseAction("Disabled", "80000000"),
    };

    static readonly string[] Modifiers = { "Ctrl", "Shift", "Alt", "Win" };

    public static bool HasLeft(byte[][] buttons, int count)
    {
        return buttons != null && buttons.Take(count).Any(binding => binding != null && binding.SequenceEqual(Actions[0].Bytes));
    }

    // Factory profiles store a special's last two bytes as FF FF (G502 HERO) or FF 00 (a G502 X LIGHTSPEED; a 409F on
    // MPM 30.00 has 00 00), G HUB as 00 00,
    // and "no action" as 80 00 FF FF (Solaar): only the first two bytes say what the button does (g502-hub's docs/protocol.md, facts only).
    public static int Find(byte[] binding)
    {
        if (binding == null) return -1;
        int exact = Array.FindIndex(Actions, action => action.Bytes.SequenceEqual(binding));
        if (exact >= 0 || binding.Length < 2 || !(binding[0] == 0x90 || (binding[0] == 0x80 && binding[1] == 0x00))) return exact;
        return Array.FindIndex(Actions, action => action.Bytes[0] == binding[0] && action.Bytes[1] == binding[1]);
    }

    static readonly Dictionary<int, string> Specials = new Dictionary<int, string>
    {
        { 0x06, "Default DPI" }, { 0x0A, "Cycle profile" }, { 0x0E, "Mode switch" }, { 0x0F, "Host button" },
        { 0x10, "Scroll down" }, { 0x11, "Scroll up" },
    };

    public static byte[] Key(int modifiers, int usage) { return new byte[] { 0x80, 0x02, (byte)modifiers, (byte)usage }; }

    public static string KeyName(int usage)
    {
        if (usage >= 0x04 && usage <= 0x1D) return ((char)('A' + usage - 0x04)).ToString();
        if (usage >= 0x1E && usage <= 0x26) return ((char)('1' + usage - 0x1E)).ToString();
        if (usage >= 0x3A && usage <= 0x45) return "F" + (usage - 0x39);
        var names = new Dictionary<int, string>
        {
            { 0x27, "0" }, { 0x28, "Enter" }, { 0x29, "Esc" }, { 0x2A, "Backspace" }, { 0x2B, "Tab" }, { 0x2C, "Space" },
            { 0x2D, "-" }, { 0x2E, "=" }, { 0x2F, "[" }, { 0x30, "]" }, { 0x31, "\\" }, { 0x33, ";" }, { 0x34, "'" }, { 0x35, "`" },
            { 0x36, "," }, { 0x37, "." }, { 0x38, "/" },
            { 0x39, "Caps Lock" }, { 0x46, "Print Screen" }, { 0x47, "Scroll Lock" }, { 0x48, "Pause" }, { 0x49, "Insert" },
            { 0x4A, "Home" }, { 0x4B, "Page Up" }, { 0x4C, "Delete" }, { 0x4D, "End" }, { 0x4E, "Page Down" },
            { 0x4F, "Right" }, { 0x50, "Left" }, { 0x51, "Down" }, { 0x52, "Up" },
        };
        string name;
        return names.TryGetValue(usage, out name) ? name : "key " + usage.ToString("X2");
    }

    public static string Describe(byte[] binding)
    {
        if (binding == null) return "";
        int i = Find(binding);
        if (i >= 0) return Actions[i].Name;
        if (binding[0] == 0x80 && binding[1] == 0x02)
        {
            var parts = Enumerable.Range(0, 8)
                .Where(bit => (binding[2] >> bit & 1) != 0)
                .Select(bit => Modifiers[bit % 4])
                .Distinct()
                .ToList();
            if (binding[3] != 0) parts.Add(KeyName(binding[3]));
            return string.Join(" + ", parts);
        }
        if (binding[0] == 0x80 && binding[1] == 0x01) return "Mouse button " + (binding[2] << 8 | binding[3]).ToString("X4");
        if (binding[0] == 0x80 && binding[1] == 0x03) return "Media key " + (binding[2] << 8 | binding[3]).ToString("X4");
        string special;
        if (binding[0] == 0x90 && Specials.TryGetValue(binding[1], out special)) return special;
        if (binding[0] == 0x90 && binding[1] == 0x0D) return "Profile " + binding[3];
        if (binding[0] == 0x90) return "Special " + binding[1].ToString("X2");
        if (binding[0] == 0x00) return "Macro";
        if (binding[0] == 0xFF) return "Not set";
        return BitConverter.ToString(binding).Replace("-", " ");
    }

    public static bool Same(byte[][] first, byte[][] second)
    {
        if (first == null || second == null) return first == second;
        return first.Length == second.Length && first.Zip(second, (x, y) => x.SequenceEqual(y)).All(x => x);
    }

    public static byte[][] Copy(byte[][] buttons)
    {
        return buttons == null ? null : buttons.Select(binding => (byte[])binding.Clone()).ToArray();
    }

    // The G502 HERO (USB C08B) has the G502 LIGHTSPEED's slots (g502-hub's docs/hardware-notes.md, from its button spy).
    public static string[] Names(FoundDevice found, int count)
    {
        int wpid = found.Models.Contains("C08B") ? 0x407F : found.WirelessId;
        var known = wpid == 0x407F
                ? new[]
                {
                    "Left button", "Right button", "Wheel click", "Thumb, back", "Thumb, front", "Sniper", "G7", "G8", "G9",
                    "Wheel tilt right", "Wheel tilt left"
                }
            // The G502 X LIGHTSPEED's slots, from its factory bindings (docs/reports/g502x-409f.txt): Back, DPI shift, Forward, Scroll left
            // and right, Cycle profile, DPI up and down.
            : wpid == 0x409F
                ? new[]
                {
                    "Left button", "Right button", "Wheel click", "Thumb, back (G4)", "Sniper (G6)", "Thumb, front (G5)",
                    "Wheel tilt left", "Wheel tilt right", "G9", "G8", "G7"
                }
            : wpid == 0x4079
                ? new[]
                {
                    "Left button", "Right button", "Wheel click", "Left side, back", "Left side, front", "DPI, underneath",
                    "Right side, back", "Right side, front"
                }
            : new[] { "Left button", "Right button", "Wheel click" };
        return Enumerable.Range(0, count).Select(i => i < known.Length ? known[i] : "Button " + (i + 1)).ToArray();
    }
}

class ButtonSpy : IDisposable
{
    readonly FileStream stream;
    volatile bool done;

    public ButtonSpy(string path, byte device, byte feature, Action<int> pressed)
    {
        var handle = Devices.Open(path);
        if (handle.IsInvalid) throw new IOException("Can't listen to the mouse's buttons");
        stream = new FileStream(handle, FileAccess.ReadWrite, 1, true);
        Task.Run(() =>
        {
            var buffer = new byte[20];
            while (!done)
            {
                int read;
                try { read = stream.Read(buffer, 0, 20); }
                catch (Exception) { return; }
                if (read >= 6 && buffer[1] == device && buffer[2] == feature && buffer[3] == 0x00) pressed(buffer[4] << 8 | buffer[5]);
            }
        });
    }

    public void Dispose() { done = true; stream.Dispose(); }
}

class MouseSet
{
    public int Dpi = 800, Rate, Stage = -1;
    public MouseLight Light;
    public byte[][] Buttons;

    public MouseSet Clone()
    {
        return new MouseSet
        {
            Dpi = Dpi, Rate = Rate, Stage = Stage,
            Light = Light == null ? null : Light.Clone(),
            Buttons = Bindings.Copy(Buttons)
        };
    }

    public string ToText()
    {
        var light = Light ?? new MouseLight();
        var text = string.Join(",", Dpi, light.Effect, light.Color.ToString("X6"), light.Period, light.Bright, Rate);
        if (Buttons != null) text += "," + string.Concat(Buttons.Select(binding => BitConverter.ToString(binding).Replace("-", "")));
        return text;
    }

    public static MouseSet FromText(string text)
    {
        var parts = text.Split(',');
        return new MouseSet
        {
            Dpi = int.Parse(parts[0]),
            Light = new MouseLight
            {
                Effect = byte.Parse(parts[1]), Color = int.Parse(parts[2], NumberStyles.HexNumber),
                Period = int.Parse(parts[3]), Bright = int.Parse(parts[4])
            },
            Rate = parts.Length > 5 ? int.Parse(parts[5]) : 0,
            Buttons = parts.Length > 6 && parts[6].Length % 8 == 0 && parts[6].Length > 0
                ? Enumerable.Range(0, parts[6].Length / 8)
                    .Select(i => Enumerable.Range(0, 4)
                        .Select(byteIndex => Convert.ToByte(parts[6].Substring(i * 8 + byteIndex * 2, 2), 16))
                        .ToArray())
                    .ToArray()
                : null,
        };
    }
}

static class MouseProtocol
{
    public static readonly EffectInfo[] Effects =
    {
        new EffectInfo { Id = 0x00, Name = "Off" },
        new EffectInfo { Id = 0x01, Name = "Static", Color = true },
        new EffectInfo
        {
            Id = 0x0A, Name = "Breathe", Color = true, Speed = true, MinPeriod = 1000, MaxPeriod = 20000, DefaultPeriod = 5000
        },
        new EffectInfo { Id = 0x03, Name = "Cycle", Speed = true, MinPeriod = 1000, MaxPeriod = 20000, DefaultPeriod = 8000 },
    };

    public static EffectInfo EffectById(byte id) { return Effects.FirstOrDefault(effect => effect.Id == id); }

    public class State
    {
        public int Dpi, Stage = -1, Rate;
        public int[] Dpis = new int[0], Stages = new int[0], Rates = new int[0];
        public bool Onboard, ProfileOk = true, Flash, Fresh, CanInvert, Inverted;
        public byte ProfilesFeature;
        public MouseLight Light;
        public EffectInfo[] Effects = MouseProtocol.Effects;
        public Dictionary<int, Dictionary<int, int>> Zones = new Dictionary<int, Dictionary<int, int>>();
        public int Sector = -1, SectorSize, Format, ButtonCount;
        public byte[][] Buttons;
        public byte[] Current, Profile;

        public bool Writable
        {
            get
            {
                // Pages 1–16 are the flash; 17 and up hold the G500's and G700s's factory profiles (cvuchener's g500 doc/memory.md).
                if (Flash) return Sector >= 2 && Sector <= 16 && SectorSize == 512 && Current != null;
                return Onboard && KnownFormat(Format) && SectorSize >= 0xF0 && ((Sector > 0 && Sector < 0x100) || Fresh);
            }
        }

        // Format 2 (G502 HERO, G303, G502 Proteus Spectrum) has format 3's layout up to the LED blocks at 0xD0 and 0xDB:
        // libratbag, Solaar and cvuchener's hidpp agree (summed up in g502-hub's docs/protocol.md). Format 1 ends at 0xD0, 4 and 5
        // differ in the LED area.
        public static bool KnownFormat(int format) { return format == 2 || format == 3; }
        public bool Savable { get { return Writable && ProfileOk; } }
        public string Key { get { return Sector.ToString("X4"); } }
    }

    public const int RomSector = 0x0101;

    public static ushort Crc(byte[] data, int length)
    {
        int crc = 0xFFFF;
        for (int i = 0; i < length; i++)
        {
            crc ^= data[i] << 8;
            for (int bit = 0; bit < 8; bit++) crc = (crc & 0x8000) != 0 ? (crc << 1 ^ 0x1021) & 0xFFFF : crc << 1 & 0xFFFF;
        }
        return (ushort)crc;
    }

    public static bool Valid(byte[] profile)
    {
        return profile != null && profile.Length >= 0xF0
            && Crc(profile, profile.Length - 2) == ReadUInt16BigEndian(profile, profile.Length - 2);
    }

    public static void WriteChecksum(byte[] profile)
    {
        int crc = Crc(profile, profile.Length - 2);
        profile[profile.Length - 2] = (byte)(crc >> 8);
        profile[profile.Length - 1] = (byte)crc;
    }

    public static int[] StagesOf(byte[] profile)
    {
        return Enumerable.Range(0, 5)
            .Select(i => profile[3 + 2 * i] | profile[4 + 2 * i] << 8)
            .TakeWhile(x => x != 0 && x != 0xFFFF)
            .ToArray();
    }

    public static int[] WideStages(byte[] profile, int[] dpis)
    {
        var stages = Enumerable.Range(0, 5)
            .Select(i => profile[4 + 5 * i] | profile[5 + 5 * i] << 8)
            .TakeWhile(x => x != 0 && x != 0xFFFF)
            .ToArray();
        return dpis.Length > 0 && stages.All(x => x >= dpis[0] && x <= dpis[dpis.Length - 1]) ? stages : new int[0];
    }

    public static int[] WideRates(byte bits)
    {
        return Enumerable.Range(0, 7).Where(i => (bits >> i & 1) != 0).Select(i => 125 << i).ToArray();
    }

    static void ReadWideDpi(Hidpp hidpp, State state)
    {
        byte feature = hidpp.FeatureIndex(0x2202);
        var list = new List<byte> { 0 };
        bool ended = false;
        for (int page = 0; page < 16 && !ended; page++)
        {
            list.AddRange(hidpp.Call(feature, 2, 0, 0, (byte)page).Skip(3).Take(13));
            for (int at = 1; at + 1 < list.Count; at += 2) ended |= list[at] == 0 && list[at + 1] == 0;
        }
        state.Dpis = DpiList(list.ToArray());
        if (state.Dpis.Length > 1000) state.Dpis = state.Dpis.Where(x => x % 50 == 0).ToArray();
        state.Dpi = ReadUInt16BigEndian(hidpp.Call(feature, 5, 0), 1);
    }

    public static string Describe(byte[] profile)
    {
        if (!Valid(profile)) return "damaged, can't be restored";
        var stages = StagesOf(profile);
        var light = Decode(profile.Skip(0xDB).Take(11).ToArray());
        return (profile[1] < stages.Length ? stages[profile[1]] + " DPI" : "DPI stages")
            + ", " + (light == null ? "lighting" : EffectById(light.Effect).Name)
            + ", " + (profile[0] > 0 ? 1000 / profile[0] + " Hz" : "");
    }

    public static byte[] Build(byte[] old, MouseSet want, int zones)
    {
        var built = (byte[])old.Clone();
        var stages = StagesOf(built);
        if (stages.Length == 0) throw new RefusedException("The profile on the mouse has no DPI stages, not touching it");
        int i = want.Stage >= 0 && want.Stage < stages.Length ? want.Stage : Array.IndexOf(stages, want.Dpi);
        if (i < 0) i = Math.Min((int)built[1], stages.Length - 1);
        built[3 + 2 * i] = (byte)want.Dpi;
        built[4 + 2 * i] = (byte)(want.Dpi >> 8);
        built[1] = (byte)i;
        if (want.Rate > 0) built[0] = (byte)(1000 / want.Rate);
        if (want.Buttons != null)
        {
            int count = Math.Min(want.Buttons.Length, 16);
            var before = Enumerable.Range(0, count).Select(button => built.Skip(0x20 + 4 * button).Take(4).ToArray()).ToArray();
            if (Bindings.HasLeft(before, count) && !Bindings.HasLeft(want.Buttons, count))
                throw new RefusedException("No button is set to Left click, not saving");
            for (int button = 0; button < count; button++) want.Buttons[button].CopyTo(built, 0x20 + 4 * button);
        }
        if (want.Light != null)
            for (int zone = 0; zone < Math.Min(zones, 2); zone++)
            {
                built[0xD0 + 11 * zone] = want.Light.Effect;
                Encode(want.Light).CopyTo(built, 0xD1 + 11 * zone);
            }
        WriteChecksum(built);
        return built;
    }

    public static byte[] Factory(byte[] rom, byte[] current)
    {
        if (rom[0] == 0 || rom[0] > 8 || StagesOf(rom).Length == 0)
            throw new RefusedException("Couldn't read the mouse's factory profile, not restoring it (it starts "
                + BitConverter.ToString(rom, 0, Math.Min(13, rom.Length)).Replace("-", " ") + ")");
        var built = (byte[])rom.Clone();
        if (Valid(current)) Array.Copy(current, 0xA0, built, 0xA0, 0x30);
        WriteChecksum(built);
        return built;
    }

    public static byte[] ReadSector(Hidpp hidpp, int sector, int size)
    {
        byte feature = hidpp.FeatureIndex(0x8100);
        var data = new byte[size];
        for (int offset = 0; offset < size; offset += 16)
        {
            int at = Math.Min(offset, size - 16);
            var chunk = Memory(hidpp, feature, sector, at);
            Array.Copy(chunk, offset - at, data, offset, Math.Min(16, size - offset));
        }
        return data;
    }

    public static void WriteSector(Hidpp hidpp, int sector, byte[] profile)
    {
        if (sector <= 0 || sector >= 0x100) throw new RefusedException("Refusing to write that part of the mouse's memory");
        if (!Valid(profile)) throw new RefusedException("Refusing to write a profile with a bad checksum");
        Write(hidpp, sector, profile);
    }

    static void Write(Hidpp hidpp, int sector, byte[] profile)
    {
        byte feature = hidpp.FeatureIndex(0x8100);
        hidpp.Call(feature, 6, (byte)(sector >> 8), (byte)sector, 0, 0, (byte)(profile.Length >> 8), (byte)profile.Length);
        for (int i = 0; i < profile.Length; i += 16)
        {
            var chunk = new byte[16];
            Array.Copy(profile, i, chunk, 0, Math.Min(16, profile.Length - i));
            hidpp.Call(feature, 7, chunk);
        }
        hidpp.Call(feature, 8);
        if (!ReadSector(hidpp, sector, profile.Length).SequenceEqual(profile))
            throw new IOException("The profile on the mouse didn't read back the same after saving");
    }

    // An empty directory, or a sector never written: all 00 on a G502 HERO (Solaar #2313), all FF on a G502 X LIGHTSPEED (omalogi).
    // Solaar treats both as no directory, and the mouse runs its factory profiles.
    public static bool Unwritten(byte[] directory)
    {
        return (Valid(directory) && ReadUInt16BigEndian(directory, 0) == 0xFFFF)
            || directory.All(value => value == 0x00)
            || directory.All(value => value == 0xFF);
    }

    public static byte[] Directory(int size, int profiles)
    {
        if (profiles < 1 || profiles > 8 || size < 0xF0) throw new RefusedException("Refusing to build that profile directory");
        var directory = Enumerable.Repeat((byte)0xFF, size).ToArray();
        for (int i = 0; i < profiles; i++)
        {
            directory[4 * i] = 0;
            directory[4 * i + 1] = (byte)(i + 1);
            directory[4 * i + 2] = (byte)(i == 0 ? 1 : 0);
            directory[4 * i + 3] = 0;
        }
        directory[4 * profiles + 2] = 0;
        directory[4 * profiles + 3] = 0;
        WriteChecksum(directory);
        return directory;
    }

    public static void Setup(Hidpp hidpp, State state, byte[] profile)
    {
        if (!state.Fresh || !State.KnownFormat(state.Format) || profile.Length != state.SectorSize)
            throw new RefusedException("Saving isn't supported on this mouse");
        WriteSector(hidpp, 1, profile);
        var directory = Directory(state.SectorSize, 1);
        Write(hidpp, 0x0000, directory);
        try { hidpp.Call(hidpp.FeatureIndex(0x8100), 3, 0x00, 0x01); }
        catch (HidppError) { }
        if (hidpp.DeviceLabel != null)
            Log.Add(hidpp.DeviceLabel, "sent",
                "Onboard memory set up: the profile at 0001, built from the factory profile at " + state.Sector.ToString("X4")
                    + ", and the directory at 0000",
                "ok");
    }

    public static void Store(Hidpp hidpp, State state, byte[] profile)
    {
        if (!state.Writable || state.Fresh || profile.Length != state.SectorSize)
            throw new RefusedException("Saving isn't supported on this mouse");
        WriteSector(hidpp, state.Sector, profile);
        try { hidpp.Call(hidpp.FeatureIndex(0x8100), 3, state.Current); }
        catch (HidppError) { }
    }

    static int ReadUInt16BigEndian(byte[] bytes, int offset) { return bytes[offset] << 8 | bytes[offset + 1]; }

    public static int[] DpiList(byte[] reply)
    {
        var dpis = new List<int>();
        for (int at = 1; at + 1 < reply.Length; at += 2)
        {
            int x = ReadUInt16BigEndian(reply, at);
            if (x == 0) break;
            if ((x & 0xE000) == 0xE000)
            {
                int step = x & 0x1FFF;
                if (dpis.Count == 0 || step == 0 || at + 3 >= reply.Length) break;
                int end = ReadUInt16BigEndian(reply, at + 2);
                for (int dpi = dpis[dpis.Count - 1] + step; dpi < end; dpi += step) dpis.Add(dpi);
                continue;
            }
            dpis.Add(x);
        }
        return dpis.ToArray();
    }

    public static int[] RateList(byte bits)
    {
        return Enumerable.Range(0, 8).Where(i => (bits >> i & 1) != 0).Select(i => 1000 / (i + 1)).OrderBy(x => x).ToArray();
    }

    public static byte[] Encode(MouseLight light)
    {
        var parameters = new byte[10];
        int color = light.Color, brightness = Math.Max(1, Math.Min(100, light.Bright));
        if (light.Effect == 0x01)
            color = ((color >> 16 & 0xFF) * brightness / 100) << 16
                | ((color >> 8 & 0xFF) * brightness / 100) << 8
                | (color & 0xFF) * brightness / 100;
        switch (light.Effect)
        {
            case 0x01: parameters[0] = (byte)(color >> 16); parameters[1] = (byte)(color >> 8); parameters[2] = (byte)color; break;
            case 0x0A:
                parameters[0] = (byte)(color >> 16); parameters[1] = (byte)(color >> 8); parameters[2] = (byte)color;
                parameters[3] = (byte)(light.Period >> 8); parameters[4] = (byte)light.Period; parameters[6] = (byte)brightness;
                break;
            case 0x03:
                parameters[5] = (byte)(light.Period >> 8); parameters[6] = (byte)light.Period; parameters[7] = (byte)brightness;
                break;
        }
        return parameters;
    }

    public static MouseLight Decode(byte[] record)
    {
        var effect = EffectById(record[0]);
        if (effect == null) return null;
        var light = new MouseLight { Effect = record[0] };
        int color = record[1] << 16 | record[2] << 8 | record[3];
        switch (record[0])
        {
            case 0x01:
                int max = Math.Max(record[1], Math.Max(record[2], record[3]));
                if (max == 0) { light.Bright = 100; break; }
                light.Color = (record[1] * 255 / max) << 16 | (record[2] * 255 / max) << 8 | record[3] * 255 / max;
                light.Bright = Math.Max(1, (max * 100 + 127) / 255);
                break;
            case 0x0A:
                light.Color = color; light.Period = ReadUInt16BigEndian(record, 4); light.Bright = record[7] == 0 ? 100 : record[7];
                break;
            case 0x03: light.Period = ReadUInt16BigEndian(record, 6); light.Bright = record[8] == 0 ? 100 : record[8]; break;
        }
        if (effect.Speed) light.Period = Math.Max(effect.MinPeriod, Math.Min(effect.MaxPeriod, light.Period));
        light.Bright = Math.Min(100, light.Bright);
        return light;
    }

    static byte[] Memory(Hidpp hidpp, byte feature, int sector, int offset)
    {
        return hidpp.Call(feature, 5, (byte)(sector >> 8), (byte)sector, (byte)(offset >> 8), (byte)offset);
    }

    public static State Read(Hidpp hidpp)
    {
        var state = new State();
        try
        {
            byte dpiFeature = hidpp.FeatureIndex(0x2201);
            state.Dpis = DpiList(hidpp.Call(dpiFeature, 1, 0));
            state.Dpi = ReadUInt16BigEndian(hidpp.Call(dpiFeature, 2, 0), 1);
        }
        catch (NotSupportedException) { ReadWideDpi(hidpp, state); }
        try
        {
            byte rateFeature = hidpp.FeatureIndex(0x8060);
            state.Rates = RateList(hidpp.Call(rateFeature, 0)[0]);
            int interval = hidpp.Call(rateFeature, 1)[0];
            state.Rate = interval > 0 ? 1000 / interval : 0;
        }
        catch (NotSupportedException)
        {
            try
            {
                byte rateFeature = hidpp.FeatureIndex(0x8061);
                state.Rates = WideRates(hidpp.Call(rateFeature, 1)[1]);
                int index = hidpp.Call(rateFeature, 2, (byte)(hidpp.Direct ? 0 : 1))[0];
                state.Rate = index < 7 ? 125 << index : 0;
            }
            catch (NotSupportedException) { }
            catch (HidppError) { }
        }
        catch (HidppError) { }
        int logo = -1;
        try
        {
            byte lightFeature = hidpp.FeatureIndex(0x8070);
            int zones = hidpp.Call(lightFeature, 0)[0];
            for (int zone = 0; zone < zones; zone++)
            {
                var info = hidpp.Call(lightFeature, 1, (byte)zone, 0xFF, 0);
                if (ReadUInt16BigEndian(info, 1) == 2) logo = zone;
                var map = new Dictionary<int, int>();
                for (int effect = 0; effect < info[3]; effect++)
                    map[ReadUInt16BigEndian(hidpp.Call(lightFeature, 2, (byte)zone, (byte)effect, 0xFF, 0), 2)] = effect;
                state.Zones[zone] = map;
            }
        }
        catch (NotSupportedException) { }
        catch (HidppError) { state.Zones.Clear(); }
        if (logo < 0) logo = state.Zones.Count - 1;
        try
        {
            byte wheelFeature = hidpp.FeatureIndex(0x2121);
            state.CanInvert = (hidpp.Call(wheelFeature, 0)[1] & 0x08) != 0;
            state.Inverted = (hidpp.Call(wheelFeature, 1)[0] & 0x04) != 0;
        }
        catch (NotSupportedException) { }
        catch (HidppError) { state.CanInvert = false; }
        try
        {
            byte profilesFeature = hidpp.FeatureIndex(0x8100);
            state.ProfilesFeature = profilesFeature;
            state.Onboard = hidpp.Call(profilesFeature, 2)[0] == 1;
            if (state.Onboard)
            {
                var info = hidpp.Call(profilesFeature, 0);
                state.ProfileOk = false;
                state.ButtonCount = Math.Min(16, (int)info[5]);
                state.Format = info[1];
                state.SectorSize = ReadUInt16BigEndian(info, 7);
                var current = hidpp.Call(profilesFeature, 4);
                state.Current = new[] { current[0], current[1] };
                state.Sector = ReadUInt16BigEndian(current, 0);
                if (state.SectorSize >= 0xF0 && state.SectorSize <= 0x1000)
                {
                    state.Profile = ReadSector(hidpp, state.Sector, state.SectorSize);
                    state.ProfileOk = Valid(state.Profile) || state.Sector >= 0x100;
                    if (state.Format >= 6)
                    {
                        if (state.ProfileOk) state.Stages = WideStages(state.Profile, state.Dpis);
                        state.Profile = null;
                        state.ProfileOk = true;
                    }
                    else
                    {
                        state.Stages = StagesOf(state.Profile);
                        state.Buttons = Enumerable.Range(0, state.ButtonCount)
                            .Select(i => state.Profile.Skip(0x20 + 4 * i).Take(4).ToArray())
                            .ToArray();
                        if (logo >= 0 && logo < 2) state.Light = Decode(state.Profile.Skip(0xD0 + 11 * logo).Take(11).ToArray());
                    }
                }
                if (state.Sector >= 0x100 && State.KnownFormat(state.Format) && info[3] >= 1
                    && state.SectorSize >= 0xF0 && state.SectorSize <= 0x1000)
                {
                    try { state.Fresh = Unwritten(ReadSector(hidpp, 0x0000, state.SectorSize)); }
                    catch (HidppError ex) { state.Fresh = ex.Code == 2; }
                }
                state.Stage = hidpp.Call(profilesFeature, 0xB)[0];
                if (state.Stage >= state.Stages.Length) state.Stage = -1;
            }
        }
        catch (NotSupportedException) { }
        catch (HidppError) { }
        return state;
    }

    // After the mouse's own DPI button, which 0x8100 reports as event fn 1 (MyHUB, docs/research/more-references.md).
    public static void ReadDpiAndStage(Hidpp hidpp, State state, out int dpi, out int stage)
    {
        var now = new State();
        try { now.Dpi = ReadUInt16BigEndian(hidpp.Call(hidpp.FeatureIndex(0x2201), 2, 0), 1); }
        catch (NotSupportedException) { ReadWideDpi(hidpp, now); }
        dpi = now.Dpi;
        stage = hidpp.Call(state.ProfilesFeature, 0xB)[0];
        if (stage >= state.Stages.Length) stage = -1;
    }

    public static void SetDpi(Hidpp hidpp, int dpi)
    {
        byte high = (byte)(dpi >> 8), low = (byte)dpi;
        try { hidpp.Call(hidpp.FeatureIndex(0x2201), 3, 0, high, low); return; }
        catch (NotSupportedException) { }
        byte feature = hidpp.FeatureIndex(0x2202);
        bool both = (hidpp.Call(feature, 1, 0)[2] & 1) != 0;
        byte lift = hidpp.Call(feature, 5, 0)[9];
        try { hidpp.Call(feature, 6, 0, high, low, both ? high : (byte)0, both ? low : (byte)0, lift); }
        catch (HidppError) { throw new RefusedException("The mouse refused this DPI. It may only take its saved DPI stages."); }
    }

    public static void SetInvert(Hidpp hidpp, bool on)
    {
        byte feature = hidpp.FeatureIndex(0x2121);
        var mode = hidpp.CallUnlogged(feature, 1);
        bool now = (mode[0] & 0x04) != 0;
        if (now == on) return;
        hidpp.Call(feature, 2, (byte)(on ? mode[0] | 0x04 : mode[0] & ~0x04), mode[1], mode[2]);
    }

    public static void SetStage(Hidpp hidpp, int stage) { hidpp.Call(hidpp.FeatureIndex(0x8100), 0xC, (byte)stage); }

    public static void SetRate(Hidpp hidpp, int hz)
    {
        try { hidpp.Call(hidpp.FeatureIndex(0x8060), 2, (byte)(1000 / hz)); return; }
        catch (NotSupportedException) { }
        int index = Array.IndexOf(new[] { 125, 250, 500, 1000, 2000, 4000, 8000 }, hz);
        if (index < 0) throw new RefusedException("This mouse has no " + hz + " Hz rate");
        hidpp.Call(hidpp.FeatureIndex(0x8061), 3, (byte)index);
    }

    public static void SetLight(Hidpp hidpp, State state, MouseLight light)
    {
        byte feature = hidpp.FeatureIndex(0x8070);
        foreach (var zone in state.Zones)
        {
            int effectIndex;
            if (!zone.Value.TryGetValue(light.Effect, out effectIndex)) continue;
            hidpp.Call(feature, 3, new byte[] { (byte)zone.Key, (byte)effectIndex }.Concat(Encode(light)).ToArray());
        }
    }
}

interface IMouse
{
    EffectInfo[] Effects { get; }
    bool HasFactory { get; }
    MouseProtocol.State Read(Hidpp hidpp);
    void SendDpi(Hidpp hidpp, MouseProtocol.State state, MouseSet want, MouseSet sent);
    void SetLight(Hidpp hidpp, MouseProtocol.State state, MouseLight light);
    void SetRate(Hidpp hidpp, int hz);
    void SetInvert(Hidpp hidpp, bool on);
    byte[] ReadProfile(Hidpp hidpp, MouseProtocol.State state);
    void Setup(Hidpp hidpp, MouseProtocol.State state, MouseSet want);
    void Save(Hidpp hidpp, MouseProtocol.State state, byte[] now, MouseSet want);
    void Restore(Hidpp hidpp, MouseProtocol.State state, byte[] now, byte[] data);
    string Describe(byte[] profile);
    int DefaultStage(byte[] profile);
}

class HidppMouse : IMouse
{
    public EffectInfo[] Effects { get { return MouseProtocol.Effects; } }
    public bool HasFactory { get { return true; } }
    public MouseProtocol.State Read(Hidpp hidpp) { return MouseProtocol.Read(hidpp); }

    public void SendDpi(Hidpp hidpp, MouseProtocol.State state, MouseSet want, MouseSet sent)
    {
        if (sent != null && want.Dpi == sent.Dpi && want.Stage == sent.Stage) return;
        bool saved = state.Onboard && want.Stage >= 0 && want.Stage < state.Stages.Length && state.Stages[want.Stage] == want.Dpi;
        if (saved) MouseProtocol.SetStage(hidpp, want.Stage); else MouseProtocol.SetDpi(hidpp, want.Dpi);
    }

    public void SetLight(Hidpp hidpp, MouseProtocol.State state, MouseLight light) { MouseProtocol.SetLight(hidpp, state, light); }
    public void SetRate(Hidpp hidpp, int hz) { MouseProtocol.SetRate(hidpp, hz); }
    public void SetInvert(Hidpp hidpp, bool on) { MouseProtocol.SetInvert(hidpp, on); }
    public byte[] ReadProfile(Hidpp hidpp, MouseProtocol.State state)
    {
        return MouseProtocol.ReadSector(hidpp, state.Sector, state.SectorSize);
    }
    public void Setup(Hidpp hidpp, MouseProtocol.State state, MouseSet want)
    {
        MouseProtocol.Setup(hidpp, state,
            MouseProtocol.Build(MouseProtocol.ReadSector(hidpp, state.Sector, state.SectorSize), want, state.Zones.Count));
    }
    public void Save(Hidpp hidpp, MouseProtocol.State state, byte[] now, MouseSet want)
    {
        MouseProtocol.Store(hidpp, state, MouseProtocol.Build(now, want, state.Zones.Count));
    }
    // Reloading the active profile (fn 3) leaves a G305's DPI as it was set live, so the restored default stage is set too.
    public void Restore(Hidpp hidpp, MouseProtocol.State state, byte[] now, byte[] data)
    {
        var restored = data ?? MouseProtocol.Factory(MouseProtocol.ReadSector(hidpp, MouseProtocol.RomSector, state.SectorSize), now);
        MouseProtocol.Store(hidpp, state, restored);
        int stage = DefaultStage(restored);
        if (stage < MouseProtocol.StagesOf(restored).Length)
            try { MouseProtocol.SetStage(hidpp, stage); }
            catch (HidppError) { }
    }
    public string Describe(byte[] profile) { return MouseProtocol.Describe(profile); }
    public int DefaultStage(byte[] profile) { return profile[1]; }
}

// The mice that share the G9x's profile page and registers. From cvuchener's hidpp (GPL-3.0, facts only): the G500 has the G9x's sensor
// (0x63 in units of 400/17 DPI, 200–5700), the G500s another (units of 50 DPI, 200–8200), and neither has the G9x's colour light (0x57).
class G9xModel
{
    public static readonly G9xModel G9x = new G9xModel(17, 400, 5700, true, true),
        G500 = new G9xModel(17, 400, 5700, false, true),
        G500s = new G9xModel(1, 50, 8200, false, true);

    // The G700 and G700s have the G500's and G500s's sensors, but another profile layout (4-byte modes, 13 buttons from 35) and
    // memory commands nobody has confirmed (g500 doc/hidpp10.md marks 0xA0 and 0xA1 "?"): live DPI and rate only, no pages read or
    // written.
    public static readonly G9xModel G700 = new G9xModel(17, 400, 5700, false, false), G700s = new G9xModel(1, 50, 8200, false, false);

    readonly int rawPerStep, dpiPerStep;
    public readonly int Max;
    public readonly bool Light, Pages;

    G9xModel(int rawPerStep, int dpiPerStep, int max, bool light, bool pages)
    {
        this.rawPerStep = rawPerStep; this.dpiPerStep = dpiPerStep; Max = max; Light = light; Pages = pages;
    }

    public int Dpi(int raw) { return (int)Math.Round(raw * (double)dpiPerStep / rawPerStep / 50) * 50; }

    public int DpiToRaw(int dpi) { return (int)Math.Round(Math.Max(200, Math.Min(Max, dpi)) * (double)rawPerStep / dpiPerStep); }

    public int[] Dpis() { return Enumerable.Range(0, (Max - 200) / 50 + 1).Select(step => 200 + 50 * step).ToArray(); }
}

class G9xMouse : IMouse
{
    readonly G9xModel model;

    public G9xMouse(G9xModel model) { this.model = model; }

    public EffectInfo[] Effects { get { return G9xRegisters.Effects; } }
    public bool HasFactory { get { return false; } }
    public MouseProtocol.State Read(Hidpp hidpp) { return G9xRegisters.Read(hidpp, model); }

    public void SendDpi(Hidpp hidpp, MouseProtocol.State state, MouseSet want, MouseSet sent)
    {
        if (sent == null || want.Dpi != sent.Dpi) G9xRegisters.SetDpi(hidpp, want.Dpi, model);
    }

    public void SetLight(Hidpp hidpp, MouseProtocol.State state, MouseLight light)
    {
        if (!model.Light) throw new RefusedException("This mouse has no colour light");
        G9xRegisters.SetLight(hidpp, light);
    }

    public void SetRate(Hidpp hidpp, int hz) { G9xRegisters.SetRate(hidpp, hz); }
    public void SetInvert(Hidpp hidpp, bool on) { throw new RefusedException("This mouse can't invert its wheel"); }
    public byte[] ReadProfile(Hidpp hidpp, MouseProtocol.State state) { return G9xRegisters.ReadPage(hidpp, state.Sector); }
    public void Setup(Hidpp hidpp, MouseProtocol.State state, MouseSet want)
    {
        throw new RefusedException("Saving isn't supported on this mouse");
    }
    public void Save(Hidpp hidpp, MouseProtocol.State state, byte[] now, MouseSet want)
    {
        G9xRegisters.Store(hidpp, state, G9xRegisters.Build(now, want, state.Rate, model), now);
    }

    public void Restore(Hidpp hidpp, MouseProtocol.State state, byte[] now, byte[] data)
    {
        if (data == null) throw new RefusedException("G-Lite can't restore the factory profile on this mouse");
        G9xRegisters.Store(hidpp, state, data, now);
    }

    public string Describe(byte[] profile) { return G9xRegisters.Describe(profile, model); }
    public int DefaultStage(byte[] profile) { return profile[0x23]; }
}

// The G5 and G9: short register 0x63 holds 0x80 + an index into the mouse's DPI list, 0x64 the report interval in ms.
// From cvuchener's g500 docs (commands/63-resolution.md, 64-usbrate.md) and hidpp's sensors (GPL-3.0, facts only).
// Live only: the G5 has no profile memory, and nobody has confirmed how a G9 writes its pages.
class G5Mouse : IMouse
{
    // The G9's index 0 is "off", so its 200 DPI is index 1.
    public static G5Mouse G5() { return new G5Mouse(new[] { 400, 800, 1600, 2000 }, 0); }
    public static G5Mouse G9() { return new G5Mouse(Enumerable.Range(1, 16).Select(step => 200 * step).ToArray(), 1); }

    readonly int[] dpis;
    readonly int firstIndex;

    G5Mouse(int[] dpis, int firstIndex) { this.dpis = dpis; this.firstIndex = firstIndex; }

    public int Dpi(int raw)
    {
        int index = (raw & 0x7F) - firstIndex;
        return (raw & 0x80) != 0 && index >= 0 && index < dpis.Length ? dpis[index] : 0;
    }

    public int DpiToRaw(int dpi)
    {
        int index = Array.IndexOf(dpis, dpi);
        if (index < 0) throw new RefusedException("This mouse has no " + dpi + " DPI setting");
        return 0x80 | index + firstIndex;
    }

    public EffectInfo[] Effects { get { return G9xRegisters.Effects; } }
    public bool HasFactory { get { return false; } }

    public MouseProtocol.State Read(Hidpp hidpp)
    {
        var state = new MouseProtocol.State { ProfileOk = true, Effects = G9xRegisters.Effects, Rates = G9xRegisters.Rates, Dpis = dpis };
        state.Dpi = Dpi(hidpp.Register(0x81, 0x63)[0]);
        int interval = hidpp.Register(0x81, 0x64)[0];
        state.Rate = interval > 0 ? 1000 / interval : 0;
        return state;
    }

    public void SendDpi(Hidpp hidpp, MouseProtocol.State state, MouseSet want, MouseSet sent)
    {
        if (sent == null || want.Dpi != sent.Dpi) hidpp.Register(0x80, 0x63, (byte)DpiToRaw(want.Dpi));
    }

    public void SetLight(Hidpp hidpp, MouseProtocol.State state, MouseLight light)
    {
        throw new RefusedException("G-Lite can't set this mouse's light yet");
    }
    public void SetRate(Hidpp hidpp, int hz) { G9xRegisters.SetRate(hidpp, hz); }
    public void SetInvert(Hidpp hidpp, bool on) { throw new RefusedException("This mouse can't invert its wheel"); }
    public byte[] ReadProfile(Hidpp hidpp, MouseProtocol.State state) { return null; }
    public void Setup(Hidpp hidpp, MouseProtocol.State state, MouseSet want)
    {
        throw new RefusedException("Saving isn't supported on this mouse");
    }
    public void Save(Hidpp hidpp, MouseProtocol.State state, byte[] now, MouseSet want)
    {
        throw new RefusedException("Saving isn't supported on this mouse");
    }
    public void Restore(Hidpp hidpp, MouseProtocol.State state, byte[] now, byte[] data)
    {
        throw new RefusedException("Saving isn't supported on this mouse");
    }
    public string Describe(byte[] profile) { return ""; }
    public int DefaultStage(byte[] profile) { return 0; }
}

static class G9xRegisters
{
    public static readonly EffectInfo[] Effects = MouseProtocol.Effects.Where(effect => effect.Id <= 0x01).ToArray();
    public static readonly int[] Rates = { 125, 200, 250, 333, 500, 1000 };

    public static bool Valid(byte[] page) { return page.Length == 512 && MouseProtocol.Valid(page); }

    public static byte[] ReadPage(Hidpp device, int pageNumber)
    {
        var page = new byte[512];
        for (int offset = 0; offset < 512; offset += 16)
            Array.Copy(device.Register(0x83, 0xA2, (byte)pageNumber, (byte)(offset / 2)), 0, page, offset, 16);
        return page;
    }

    // The directory's entries are 3 bytes (page, offset in words, LEDs), ended by one 0xFF; past it page 1 holds macros and names.
    public static int Entry(byte[] directory, int index)
    {
        for (int entry = 0; entry * 3 + 2 < directory.Length - 2 && directory[entry * 3] != 0xFF; entry++)
            if (entry == index) return directory[entry * 3];
        return 0xFF;
    }

    public static int[] StagesOf(byte[] page, G9xModel model)
    {
        return Enumerable.Range(0, 5)
            .Select(stage => page[4 + 6 * stage] << 8 | page[5 + 6 * stage])
            .TakeWhile(raw => raw != 0 && raw != 0xFFFF)
            .Select(model.Dpi)
            .ToArray();
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

    public static MouseProtocol.State Read(Hidpp device, G9xModel model)
    {
        var state = new MouseProtocol.State
        {
            ProfileOk = true, Flash = model.Pages, Effects = Effects, Rates = Rates, Dpis = model.Dpis()
        };
        var resolution = device.Register(0x83, 0x63);
        state.Dpi = model.Dpi(resolution[0] | resolution[1] << 8);
        int interval = device.Register(0x81, 0x64)[0];
        state.Rate = interval > 0 ? 1000 / interval : 0;
        if (model.Light)
        {
            state.Light = Light(device.Register(0x81, 0x57));
            state.Zones[0] = new Dictionary<int, int> { { 0x00, 0 }, { 0x01, 1 } };
        }
        if (model.Pages) try
        {
            var current = device.Register(0x81, 0x0F);
            var directory = current[0] == 0x00 && current[1] < 16 ? ReadPage(device, 1) : null;
            int pageNumber = directory != null && Valid(directory) ? Entry(directory, current[1]) : 0xFF;
            var profile = pageNumber >= 2 && pageNumber < 32 ? ReadPage(device, pageNumber) : null;
            if (profile != null)
            {
                state.Sector = pageNumber;
                state.SectorSize = 512;
                state.Current = new[] { current[1] };
                state.Profile = profile;
                state.ProfileOk = Valid(profile);
                if (state.ProfileOk) state.Stages = StagesOf(profile, model);
            }
        }
        catch (HidppError) { }
        state.Stage = Array.IndexOf(state.Stages, state.Dpi);
        return state;
    }

    public static void SetDpi(Hidpp device, int dpi, G9xModel model)
    {
        int raw = model.DpiToRaw(dpi);
        var resolution = new byte[16];
        Array.Copy(device.Register(0x83, 0x63), resolution, 6);
        resolution[0] = resolution[2] = (byte)raw;
        resolution[1] = resolution[3] = (byte)(raw >> 8);
        device.Register(0x82, 0x63, resolution);
    }

    public static void SetRate(Hidpp device, int hz) { device.Register(0x80, 0x64, (byte)(1000 / hz)); }

    public static string Describe(byte[] page, G9xModel model)
    {
        if (!Valid(page)) return "damaged, can't be restored";
        var stages = StagesOf(page, model);
        var dpi = page[0x23] < stages.Length ? stages[page[0x23]] + " DPI" : "DPI stages";
        return page[0x26] > 0 ? dpi + ", " + 1000 / page[0x26] + " Hz" : dpi;
    }

    public static byte[] Build(byte[] old, MouseSet want, int hz, G9xModel model)
    {
        var page = (byte[])old.Clone();
        var stages = StagesOf(page, model);
        if (stages.Length == 0) throw new RefusedException("The profile on the mouse has no DPI stages, not touching it");
        int stage = want.Stage >= 0 && want.Stage < stages.Length ? want.Stage : Array.IndexOf(stages, want.Dpi);
        if (stage < 0) stage = Math.Min((int)page[0x23], stages.Length - 1);
        int raw = model.DpiToRaw(want.Dpi);
        page[4 + 6 * stage] = page[6 + 6 * stage] = (byte)(raw >> 8);
        page[5 + 6 * stage] = page[7 + 6 * stage] = (byte)raw;
        page[0x23] = (byte)stage;
        if (hz > 0) page[0x26] = (byte)(1000 / hz);
        if (want.Light != null && model.Light)
        {
            var color = want.Light.Effect == 0x01 ? MouseProtocol.Encode(want.Light) : new byte[3];
            Array.Copy(color, page, 3);
        }
        MouseProtocol.WriteChecksum(page);
        return page;
    }

    // previous: the page as it was before (backed up), written back if the write fails after the erase.
    public static void Store(Hidpp device, MouseProtocol.State state, byte[] page, byte[] previous)
    {
        if (!state.Flash || !state.Writable) throw new RefusedException("Saving isn't supported on this mouse");
        if (page.Length != 512 || !Valid(page)) throw new RefusedException("Refusing to write a profile with a bad checksum");
        byte pageNumber = (byte)state.Sector;
        device.Register(0x80, 0x0F, 0xFF);
        bool erased = false;
        try { WritePage(device, pageNumber, page, () => erased = true); }
        catch (Exception ex)
        {
            if (!erased)
            {
                try { device.Register(0x80, 0x0F, 0x00, state.Current[0]); }
                catch (Exception) { }
                throw;
            }
            // The page may be half written, and the mouse is on its factory profile (0x0F FF): put the page back as it was, then load it.
            try
            {
                if (previous == null || previous.Length != 512 || !Valid(previous)) throw new RefusedException("no good copy to put back");
                WritePage(device, pageNumber, previous, () => { });
                device.Register(0x80, 0x0F, 0x00, state.Current[0]);
            }
            catch (Exception)
            {
                throw new IOException("Saving failed halfway (" + ex.Message
                    + "). The mouse is on its factory profile: restore the backup from Backups, or replug it and save again.", ex);
            }
            throw new IOException("Saving failed (" + ex.Message + "), so the profile as it was is back on the mouse.", ex);
        }
        device.Register(0x80, 0x0F, 0x00, state.Current[0]);
        if (!ReadPage(device, pageNumber).SequenceEqual(page))
            throw new IOException("The profile on the mouse didn't read back the same after saving");
    }

    static void WritePage(Hidpp device, byte pageNumber, byte[] page, Action erasing)
    {
        for (int half = 0; half < 2; half++)
        {
            var data = new byte[256];
            Array.Copy(page, 256 * half, data, 0, 256);
            Upload(device, data);
            if (half == 0) { erasing(); device.Register(0x82, 0xA0, 0x02, 0, 0, 0, 0, 0, pageNumber, 0, 0, 0, 0x01, 0x00); }
            device.Register(0x82, 0xA0, 0x03, 0, 0, 0, 0, 0, pageNumber, (byte)(0x80 * half), 0, 0, 0x01, 0x00);
        }
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
        var color = light.Effect == 0x01 ? MouseProtocol.Encode(light) : new byte[3];
        device.Register(0x80, 0x57, color[0], color[1], color[2]);
    }
}
