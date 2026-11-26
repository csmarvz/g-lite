using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

class EffectInfo
{
    public byte Id;
    public string Name;
    public bool Color, Speed, Direction, Intensity;
    // Offers Solid: the colour as it is, instead of turning with height (Audio).
    public bool OffersSolid;
    public int MinPeriod, MaxPeriod, DefaultPeriod;
}

class Lighting
{
    public byte Effect = 0x01;
    public int Color = 0xFFFFFF, Period = 5000, Direction = 1, Dim = 50, Idle = 60, Sleep = 300, Intensity = 100;
    // A live effect's colour kept as it is (Audio's Solid).
    public bool Solid;
    public Lighting Clone() { return (Lighting)MemberwiseClone(); }

    // The intensity only when it isn't 100, and Solid only when on, so the text of a lighting and of older presets stays the same.
    public string ToText(int brightness)
    {
        return string.Join(",", Effect, Color.ToString("X6"), Period, Direction, Dim, Idle, Sleep, brightness)
            + (Intensity != 100 || Solid ? "," + Intensity : "") + (Solid ? ",1" : "");
    }

    public static Lighting FromText(string text, out int brightness)
    {
        var parts = text.Split(',');
        brightness = int.Parse(parts[7]);
        return new Lighting
        {
            Effect = byte.Parse(parts[0]), Color = int.Parse(parts[1], System.Globalization.NumberStyles.HexNumber),
            Period = int.Parse(parts[2]), Direction = int.Parse(parts[3]), Dim = int.Parse(parts[4]),
            Idle = int.Parse(parts[5]), Sleep = int.Parse(parts[6]), Intensity = parts.Length > 8 ? int.Parse(parts[8]) : 100,
            Solid = parts.Length > 9 && parts[9] == "1",
        };
    }
    public bool Same(Lighting other)
    {
        return other != null && Effect == other.Effect && Color == other.Color && Period == other.Period && Direction == other.Direction
            && Dim == other.Dim && Idle == other.Idle && Sleep == other.Sleep && Intensity == other.Intensity && Solid == other.Solid;
    }
}

static class G515Profile
{
    // The onboard files (0x8101), read off the G515: never write the factory ones (CLAUDE.md).
    const int UserDirFile = 0x0000, UserProfileFile = 0x0008, FactoryDirFile = 0x0100, FactoryProfileFile = 0x0101;

    // A profile file: `01 01`, the idle and sleep times in seconds (big-endian, FFFF for never), then four 11-byte lighting records: two
    // for the lighting, two for it dimmed. Each bit of the record choice picks the first of its pair (bit 0 the lighting, bit 1 the
    // dimmed); G-Lite writes the second of each and a choice of 0.
    const int ProfileLength = 99, IdleAt = 2, SleepAt = 4, RecordsAt = 30, RecordLength = 11, RecordChoiceAt = 74;

    // A directory file: its CRC32 over the rest (big-endian), a header, the profile's CRC32, the profile's file id, then FF.
    const int DirLength = 16, DirChecksumAt = 0, DirHeaderAt = 4, DirProfileChecksumAt = 10, DirProfileFileAt = 14;

    public static readonly EffectInfo[] Effects =
    {
        new EffectInfo { Id = 0x00, Name = "Off" },
        new EffectInfo { Id = 0x01, Name = "Static", Color = true },
        new EffectInfo
        {
            Id = 0x0A, Name = "Breathe", Color = true, Speed = true, Intensity = true, MinPeriod = 1000, MaxPeriod = 20000,
            DefaultPeriod = 5000
        },
        new EffectInfo
        {
            Id = 0x15, Name = "Cycle", Speed = true, Intensity = true, MinPeriod = 1000, MaxPeriod = 20000, DefaultPeriod = 5000
        },
        new EffectInfo
        {
            Id = 0x16, Name = "Wave", Speed = true, Direction = true, Intensity = true, MinPeriod = 1000, MaxPeriod = 20000,
            DefaultPeriod = 5000
        },
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
            case 0x0A:
                putColor();
                parameters[3] = (byte)(lighting.Period >> 8); parameters[4] = (byte)lighting.Period; parameters[6] = (byte)intensity;
                break;
            case 0x15:
            case 0x16:
                parameters[1] = 0xFF;
                parameters[6] = (byte)(lighting.Period >> 8); parameters[7] = (byte)lighting.Period; parameters[8] = (byte)intensity;
                if (lighting.Effect == 0x16) parameters[9] = (byte)lighting.Direction;
                break;
            case 0x17:
                putColor();
                parameters[3] = 0xFF; parameters[6] = (byte)(lighting.Period >> 8); parameters[7] = (byte)lighting.Period;
                break;
        }
        return parameters;
    }

    static int Rgb(byte[] bytes, int offset) { return bytes[offset] << 16 | bytes[offset + 1] << 8 | bytes[offset + 2]; }
    static int Max3(int color) { return Math.Max(color >> 16 & 0xFF, Math.Max(color >> 8 & 0xFF, color & 0xFF)); }
    static int ReadUInt16BigEndian(byte[] bytes, int offset) { return bytes[offset] << 8 | bytes[offset + 1]; }
    static void Put16(byte[] bytes, int offset, int value) { bytes[offset] = (byte)(value >> 8); bytes[offset + 1] = (byte)value; }
    static void Put32(byte[] bytes, int offset, uint value)
    {
        bytes[offset] = (byte)(value >> 24); bytes[offset + 1] = (byte)(value >> 16);
        bytes[offset + 2] = (byte)(value >> 8); bytes[offset + 3] = (byte)value;
    }

    static uint Get32(byte[] bytes, int offset)
    {
        return (uint)(bytes[offset] << 24 | bytes[offset + 1] << 16 | bytes[offset + 2] << 8 | bytes[offset + 3]);
    }

    // Returns null when the profile holds an effect this app doesn't know (the factory animation).
    public static Lighting Decode(byte[] profile)
    {
        int choice = profile[RecordChoiceAt];
        int recordAt = RecordsAt + ((choice & 1) != 0 ? 0 : RecordLength);
        int dimAt = RecordsAt + ((choice & 2) != 0 ? 2 * RecordLength : 3 * RecordLength);
        var record = profile.Skip(recordAt).Take(RecordLength).ToArray();
        var dim = profile.Skip(dimAt).Take(RecordLength).ToArray();
        var effect = EffectById(record[0]);
        if (effect == null) return null;
        var lighting = new Lighting { Effect = record[0] };
        int idle = ReadUInt16BigEndian(profile, IdleAt), sleep = ReadUInt16BigEndian(profile, SleepAt);
        lighting.Idle = idle == 0xFFFF ? 0 : idle;
        lighting.Sleep = sleep == 0xFFFF ? 0 : sleep;
        if (effect.Color) lighting.Color = Rgb(record, 1);
        if (effect.Speed)
            lighting.Period = Math.Min(effect.MaxPeriod,
                Math.Max(effect.MinPeriod, record[0] == 0x0A ? ReadUInt16BigEndian(record, 4) : ReadUInt16BigEndian(record, 7)));
        if (effect.Direction) lighting.Direction = record[10];
        if (dim[0] == record[0])
        {
            if (effect.Color && Max3(lighting.Color) > 0)
                lighting.Dim = (Max3(Rgb(dim, 1)) * 100 + Max3(lighting.Color) / 2) / Max3(lighting.Color);
            else if (record[0] == 0x15 || record[0] == 0x16) lighting.Dim = dim[9];
        }
        return lighting;
    }

    public static byte[] BuildProfile(byte[] baseProfile, Lighting lighting)
    {
        var profile = (byte[])baseProfile.Clone();
        Put16(profile, IdleAt, lighting.Idle <= 0 ? 0xFFFF : lighting.Idle);
        Put16(profile, SleepAt, lighting.Sleep <= 0 ? 0xFFFF : lighting.Sleep);
        Array.Clear(profile, RecordsAt, 4 * RecordLength);
        int recordAt = RecordsAt + RecordLength, dimAt = RecordsAt + 3 * RecordLength;
        profile[recordAt] = lighting.Effect; Array.Copy(Encode(lighting, 100), 0, profile, recordAt + 1, 10);
        profile[dimAt] = lighting.Effect; Array.Copy(Encode(lighting, lighting.Dim), 0, profile, dimAt + 1, 10);
        profile[RecordChoiceAt] = 0;
        return profile;
    }

    public static byte[] BuildDirectory(byte[] factoryDir, byte[] profile)
    {
        var directory = (byte[])factoryDir.Clone();
        Put32(directory, DirProfileChecksumAt, Crc32(profile));
        directory[DirProfileFileAt] = UserProfileFile;
        Put32(directory, DirChecksumAt, Crc32(directory.Skip(DirHeaderAt).ToArray()));
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

    public static bool ValidProfile(byte[] profile)
    {
        return profile != null && profile.Length == ProfileLength && profile[0] == 1 && profile[1] == 1;
    }

    static bool ValidDir(byte[] directory)
    {
        return directory != null && directory.Length == DirLength
            && directory.Skip(DirHeaderAt).Take(DirHeader.Length).SequenceEqual(DirHeader) && directory[DirLength - 1] == 0xFF
            && Crc32(directory.Skip(DirHeaderAt).ToArray()) == Get32(directory, DirChecksumAt);
    }

    // ---- device operations ----

    static byte[] ReadBuffer(Hidpp hidpp, int size)
    {
        var collected = new List<byte>();
        for (int offset = 0; offset < size; offset += 16)
            collected.AddRange(hidpp.Call(hidpp.FeatureIndex(0x8101), 0xC, (byte)(offset >> 8), (byte)offset));
        return collected.Take(size).ToArray();
    }

    static byte[] ReadFile(Hidpp hidpp, int address, int size)
    {
        try { hidpp.Call(hidpp.FeatureIndex(0x8101), 8, (byte)(address >> 8), (byte)address, (byte)(size >> 8), (byte)size); }
        catch (HidppError ex) { if (ex.Code == 4 && address < FactoryDirFile) return null; throw; }
        return ReadBuffer(hidpp, size);
    }

    static void SaveFile(Hidpp hidpp, int address, byte[] data, uint checksum)
    {
        if (address != UserDirFile && address != UserProfileFile) throw new InvalidOperationException("refusing to write factory storage");
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
            FactoryProfile = ReadFile(hidpp, FactoryProfileFile, ProfileLength),
            FactoryDir = ReadFile(hidpp, FactoryDirFile, DirLength),
            UserProfile = ReadFile(hidpp, UserProfileFile, ProfileLength),
            UserDir = ReadFile(hidpp, UserDirFile, DirLength),
        };
    }

    public static void CheckFiles(Files files)
    {
        if (!ValidProfile(files.FactoryProfile) || !ValidDir(files.FactoryDir))
            throw new RefusedException("Unknown onboard profile layout, not touching it");
        if (files.UserProfile == null && files.UserDir == null) return;
        bool ours = files.UserDir == null || (ValidDir(files.UserDir) && files.UserDir[DirProfileFileAt] == UserProfileFile);
        if (ours && (files.UserProfile == null || files.UserDir == null))
            throw new HalfWrittenException("User profile is half written, not touching it");
        if (!ours || !ValidProfile(files.UserProfile))
            throw new RefusedException("The profiles saved on the keyboard aren't laid out the way G-Lite writes them, not touching them");
        if (Crc32(files.UserProfile)
            != Get32(files.UserDir, DirProfileChecksumAt))
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

    public static int Brightness(Hidpp hidpp)
    {
        var reply = hidpp.Call(hidpp.FeatureIndex(0x8040), 1);
        return ReadUInt16BigEndian(reply, 0);
    }
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

    // Each effect's index on the keys' cluster, looked up once per connection: a preview runs every 120 ms during a slider drag, and
    // the lookup took up to 16 calls each time.
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Hidpp, Dictionary<byte, int>> EffectIndexes =
        new System.Runtime.CompilerServices.ConditionalWeakTable<Hidpp, Dictionary<byte, int>>();

    public static void Preview(Hidpp hidpp, Lighting lighting, int level)
    {
        byte feature = hidpp.FeatureIndex(0x8071);
        var known = EffectIndexes.GetOrCreateValue(hidpp);
        int index;
        lock (known)
            if (!known.TryGetValue(lighting.Effect, out index))
            {
                index = -1;
                for (int effectIndex = 0; effectIndex < 16 && index < 0; effectIndex++)
                    if (hidpp.Call(feature, 0, 0, (byte)effectIndex, 0)[3] == lighting.Effect) index = effectIndex;
                if (index >= 0) known[lighting.Effect] = index;
            }
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
        if (!ValidProfile(now.FactoryProfile) || !ValidDir(now.FactoryDir))
            throw new RefusedException("Unknown onboard profile layout, not touching it");
        if (backup.FactoryProfile != null
            && (!backup.FactoryProfile.SequenceEqual(now.FactoryProfile) || !backup.FactoryDir.SequenceEqual(now.FactoryDir)))
            throw new RefusedException("This backup is from a different keyboard, not restoring it");
        if (backup.UserProfile == null)
        {
            var profile = (byte[])now.FactoryProfile.Clone();
            WriteFiles(hidpp, now, profile, BuildDirectory(now.FactoryDir, profile));
            return;
        }
        CheckFiles(new Files
        {
            FactoryProfile = now.FactoryProfile, FactoryDir = now.FactoryDir,
            UserProfile = backup.UserProfile, UserDir = backup.UserDir
        });
        WriteFiles(hidpp, now, backup.UserProfile, backup.UserDir);
    }

    static void WriteFiles(Hidpp hidpp, Files now, byte[] profile, byte[] directory)
    {
        if (now.UserProfile == null || !profile.SequenceEqual(now.UserProfile)) SaveFile(hidpp, UserProfileFile, profile, Crc32(profile));
        if (now.UserDir == null || !directory.SequenceEqual(now.UserDir))
            SaveFile(hidpp, UserDirFile, directory, Crc32(directory.Skip(DirHeaderAt).ToArray()));
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
        if ((reply[0] & 1) == 0 || reply[1] != 0 || reply[2] != 1)
            throw new IOException(string.Format("Unexpected state {0:x2} {1:x2} {2:x2} after activating", reply[0], reply[1], reply[2]));
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
        bool verticalWave;
        return Clusters(hidpp, out verticalWave);
    }

    // verticalWave: a Wave (0x0016) lists a vertical direction among its capabilities (bytes 4-5): 0x0040 vertical (direction 2,
    // Down) or 0x0800 reverse vertical (7, Up), per Logitech's x8071 spec as PeriHub reads it. The G815 lists both and goes up and
    // down (tried); the G515, G915 TKL and G512 X 75 list them too (their reports: DC E1); the G213 and G560 don't.
    public static List<byte[]> Clusters(Hidpp hidpp, out bool verticalWave)
    {
        byte feature = hidpp.FeatureIndex(0x8071);
        var all = new List<byte[]>();
        verticalWave = false;
        for (int cluster = 0; cluster < 8; cluster++)
        {
            var ids = new List<byte>();
            for (int effectIndex = 0; effectIndex < 32; effectIndex++)
            {
                byte[] info;
                try { info = hidpp.CallUnlogged(feature, 0, (byte)cluster, (byte)effectIndex, 0); }
                catch (HidppError) { break; }
                ids.Add(info[3]);
                if (info[2] == 0x00 && info[3] == 0x16 && ((info[4] << 8 | info[5]) & 0x0840) != 0) verticalWave = true;
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
        for (int cluster = 1; cluster < clusters.Count; cluster++)
            if (clusters[cluster].Length > clusters[largestIndex].Length) largestIndex = cluster;
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
        // 0 shows as 100 on the G815, so the slider's 0 is sent as 1.
        int color = lighting.Color, intensity = Math.Max(1, Math.Min(100, lighting.Intensity));
        switch (id)
        {
            case 0x03:
                parameters[5] = (byte)(lighting.Period >> 8); parameters[6] = (byte)lighting.Period; parameters[7] = (byte)intensity;
                return parameters;
            case 0x04:
                parameters[6] = (byte)lighting.Period; parameters[7] = (byte)lighting.Direction; parameters[8] = (byte)intensity;
                parameters[9] = (byte)(lighting.Period >> 8);
                return parameters;
            case 0x0A:
                parameters[0] = (byte)(color >> 16); parameters[1] = (byte)(color >> 8); parameters[2] = (byte)color;
                parameters[3] = (byte)(lighting.Period >> 8); parameters[4] = (byte)lighting.Period; parameters[6] = (byte)intensity;
                return parameters;
            case 0x0B:
                parameters[0] = (byte)(color >> 16); parameters[1] = (byte)(color >> 8); parameters[2] = (byte)color;
                parameters[4] = (byte)(lighting.Period >> 8); parameters[5] = (byte)lighting.Period;
                return parameters;
        }
        var copy = lighting.Clone();
        copy.Effect = id;
        return G515Profile.Encode(copy, 100);
    }

    // The G915 TKL turns itself off after its timeout even while G-Lite holds the power modes, and wakes in mode 3 (off) on its own
    // lighting, ignoring effects until fn 8 sets full power again (tested on a real keyboard).
    public static void Claim(Hidpp hidpp)
    {
        byte feature = hidpp.FeatureIndex(0x8071);
        hidpp.Call(feature, 5, 0x01, 0x03, 0x04);
        try { hidpp.Call(feature, 8, 0x01, 0x01, 0x00); } catch (HidppError) { }
    }

    public static void Release(Hidpp hidpp) { hidpp.Call(hidpp.FeatureIndex(0x8071), 5, 0x01, 0x00, 0x00); }

    // The onboard profile, format 4, from a G915 TKL's report (docs/G815-G915.md): power save and off after (seconds, little-endian), then four
    // 11-byte lighting records: one per cluster, then the same dimmed for power save. The CRC is in the last two bytes, as on the mice.
    const int PowerSaveAt = 0x1C, OffAt = 0x1E, IdleTimesLength = 4, RecordsAt = 0xD0, RecordLength = 11, RecordsLength = 4 * RecordLength;

    // The enabled profiles' sectors, from the directory (sector 0000: `00 <sector> <enabled> FF` each, then FF FF), and the one
    // running (fn 4). Sector N is the key MN (docs/on-board-memory.md). Empty when the keyboard isn't in onboard mode.
    public static List<int> Slots(Hidpp hidpp, out int running, out bool onboard)
    {
        byte feature = hidpp.FeatureIndex(0x8100);
        running = 0;
        var slots = new List<int>();
        onboard = hidpp.Call(feature, 2)[0] == 1;
        if (!onboard) return slots;
        var current = hidpp.Call(feature, 4);
        running = current[0] << 8 | current[1];
        for (int offset = 0; offset < 64; offset += 16)
        {
            var chunk = hidpp.Call(feature, 5, 0, 0, 0, (byte)offset);
            for (int at = 0; at < 16; at += 4)
            {
                if (chunk[at] == 0xFF) return slots;
                int sector = chunk[at] << 8 | chunk[at + 1];
                if (chunk[at + 2] != 0 && sector > 0 && sector < 0x100) slots.Add(sector);
            }
        }
        return slots;
    }

    public static byte[] ReadProfile(Hidpp hidpp, out int sector)
    {
        int buttons;
        return ReadProfile(hidpp, out sector, out buttons);
    }

    // buttons: the profile's button slots, from 0x8100 fn 0 byte 5 (8 on the G815: G1-G5 and M1-M3; 15 on the G915 TKL).
    public static byte[] ReadProfile(Hidpp hidpp, out int sector, out int buttons)
    {
        byte feature = hidpp.FeatureIndex(0x8100);
        if (hidpp.Call(feature, 2)[0] != 1) throw new RefusedException("The keyboard isn't on its onboard profiles");
        var info = hidpp.Call(feature, 0);
        buttons = info[5];
        int size = info[7] << 8 | info[8];
        if (info[1] != 4 || size < RecordsAt + RecordsLength + 2 || size > 0x1000)
            throw new RefusedException("Saving isn't supported on this keyboard (profile format " + info[1] + ")");
        var current = hidpp.Call(feature, 4);
        sector = current[0] << 8 | current[1];
        if (sector == 0 || sector >= 0x100) throw new RefusedException("The keyboard runs a factory profile");
        var profile = MouseProtocol.ReadSector(hidpp, sector, size);
        if (!MouseProtocol.Valid(profile)) throw new RefusedException("The keyboard's profile has a bad checksum");
        return profile;
    }

    // Returns null when the keys hold an effect this app doesn't know. The intensity is read only when the screen shows it (else
    // G-Lite sends 100); G HUB saves 0, which shows as 100.
    public static Lighting Decode(byte[] profile, int keys, bool intensity = false)
    {
        var record = profile.Skip(RecordsAt + RecordLength * keys).Take(RecordLength).ToArray();
        var lighting = new Lighting
        {
            Idle = profile[PowerSaveAt] | profile[PowerSaveAt + 1] << 8, Sleep = profile[OffAt] | profile[OffAt + 1] << 8,
            Color = record[1] << 16 | record[2] << 8 | record[3]
        };
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
        bool hasIntensity = record[0] == 0x03 || record[0] == 0x04 || record[0] == 0x0A;
        if (intensity && hasIntensity && Level(record) > 0) lighting.Intensity = Math.Min(100, Level(record));
        var dim = profile.Skip(RecordsAt + RecordLength * (2 + keys)).Take(RecordLength).ToArray();
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

    // The dimmed records keep the colour and lower the intensity, as the keyboard's own profiles do, to percent of the normal one;
    // Static and Ripple have no intensity, so their colour is scaled.
    static byte[] Dimmed(byte id, byte[] parameters, int percent)
    {
        parameters = (byte[])parameters.Clone();
        switch (id)
        {
            case 0x03: parameters[7] = (byte)Math.Max(1, parameters[7] * percent / 100); break;
            case 0x04: parameters[8] = (byte)Math.Max(1, parameters[8] * percent / 100); break;
            case 0x0A: parameters[6] = (byte)Math.Max(1, parameters[6] * percent / 100); break;
            default: for (int i = 0; i < 3; i++) parameters[i] = (byte)(parameters[i] * percent / 100); break;
        }
        return parameters;
    }

    // The G keys are the profile's first buttons, 4 bytes each from 0x20, in the mice's format (Bindings); M1-M3 follow them
    // (docs/G815-G915.md).
    const int ButtonsAt = 0x20;

    public static byte[][] ReadButtons(byte[] profile, int count)
    {
        return Enumerable.Range(0, count).Select(i => profile.Skip(ButtonsAt + 4 * i).Take(4).ToArray()).ToArray();
    }

    // Only the buttons that differ from were (null: all), so a change made elsewhere since the read, to another key, stays.
    public static byte[] WithButtons(byte[] profile, byte[][] buttons, byte[][] were = null)
    {
        var built = (byte[])profile.Clone();
        for (int i = 0; i < buttons.Length; i++)
            if (were == null || i >= were.Length || !buttons[i].SequenceEqual(were[i])) buttons[i].CopyTo(built, ButtonsAt + 4 * i);
        MouseProtocol.WriteChecksum(built);
        return built;
    }

    public static byte[] Build(byte[] profile, List<byte[]> clusters, Lighting lighting, Lighting others, bool idleTimes)
    {
        if (clusters.Count != 2)
            throw new RefusedException("Saving isn't supported on this keyboard (" + clusters.Count + " lighting clusters)");
        if (idleTimes && (lighting.Idle <= 0 || lighting.Sleep <= 0))
            throw new RefusedException("This keyboard can't keep its lighting on forever: 0 dims and turns it off at once");
        var built = (byte[])profile.Clone();
        int keys = Keys(clusters);
        for (int cluster = 0; cluster < 2; cluster++)
        {
            var light = cluster == keys || others == null ? lighting : others;
            int effectIndex = Pick(light.Effect, clusters[cluster], cluster != keys);
            if (effectIndex < 0) throw new RefusedException("This keyboard doesn't have that effect");
            byte id = clusters[cluster][effectIndex];
            var parameters = Encode(id, light);
            int recordAt = RecordsAt + RecordLength * cluster, dimAt = RecordsAt + RecordLength * (2 + cluster);
            built[recordAt] = id;
            parameters.CopyTo(built, recordAt + 1);
            built[dimAt] = id;
            Dimmed(id, parameters, idleTimes ? lighting.Dim : 100).CopyTo(built, dimAt + 1);
        }
        if (idleTimes)
        {
            built[PowerSaveAt] = (byte)lighting.Idle; built[PowerSaveAt + 1] = (byte)(lighting.Idle >> 8);
            built[OffAt] = (byte)lighting.Sleep; built[OffAt + 1] = (byte)(lighting.Sleep >> 8);
        }
        MouseProtocol.WriteChecksum(built);
        return built;
    }

    // The profile with its first count buttons (the G keys) as its factory copy has them; the rest stays.
    public static byte[] FactoryButtons(Hidpp hidpp, int sector, byte[] profile, int count)
    {
        var rom = MouseProtocol.ReadSector(hidpp, 0x100 + sector, profile.Length);
        var buttons = ReadButtons(rom, count);
        if (buttons.Any(binding => binding.All(value => value == 0xFF)))
            throw new RefusedException("Couldn't read the keyboard's factory G keys");
        return WithButtons(profile, buttons);
    }

    // The lighting and idle times of the factory copy of the profile at `sector` (0101 for 0001 on the G915 TKL), into a copy of
    // `profile`. Only those: a whole factory profile would drop what else the user changed, like G-shift macros.
    public static byte[] FactoryLighting(Hidpp hidpp, int sector, byte[] profile)
    {
        var rom = MouseProtocol.ReadSector(hidpp, 0x100 + sector, profile.Length);
        if (rom.Skip(RecordsAt).Take(RecordsLength).All(value => value == 0xFF) || rom[PowerSaveAt] == 0xFF)
            throw new RefusedException("Couldn't read the keyboard's factory lighting");
        var built = (byte[])profile.Clone();
        Array.Copy(rom, PowerSaveAt, built, PowerSaveAt, IdleTimesLength);
        Array.Copy(rom, RecordsAt, built, RecordsAt, RecordsLength);
        MouseProtocol.WriteChecksum(built);
        return built;
    }

    // Writes the profile (MouseProtocol.WriteSector refuses 0000 and the factory sectors, and reads it back), loads it again and gives
    // the lighting back, so the keyboard runs it, its power save included.
    // The G815 acks fn 3 for the profile it runs but keeps running its old copy, until an M key is pressed. Leaving onboard mode and
    // coming back (fn 1 02, then 01) makes it read the profile again, with a barely visible flash of the M key (tested on a real
    // G815). Loading another profile and coming back also works, but flashes the other profile's M key.
    public static void Store(Hidpp hidpp, int sector, byte[] profile, bool reloadThroughHostMode = false)
    {
        MouseProtocol.WriteSector(hidpp, sector, profile);
        byte feature = hidpp.FeatureIndex(0x8100);
        try { hidpp.Call(feature, 3, (byte)(sector >> 8), (byte)sector); }
        catch (HidppError) { }
        Release(hidpp);
        if (!reloadThroughHostMode) return;
        try { hidpp.Call(feature, 1, 0x02); }
        finally { hidpp.Call(feature, 1, 0x01); }
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
            hidpp.Call(feature, 1, new byte[] { (byte)cluster, (byte)effectIndex }
                .Concat(Encode(clusters[cluster][effectIndex], light))
                .Concat(new byte[] { 0x01 })
                .ToArray());
        }
    }
}
