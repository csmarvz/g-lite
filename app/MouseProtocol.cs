using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

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
        public bool Onboard, ProfileOk = true, Flash, Fresh, CanInvert, Inverted, Wired;
        // Shown, not changed: the G600, whose writes aren't tried yet.
        public bool ReadOnly;
        // Nothing is set live: every change waits for Save to mouse, which writes the whole profile (the G300's report).
        public bool SaveOnly;
        public byte ProfilesFeature;
        public MouseLight Light;
        public EffectInfo[] Effects = MouseProtocol.Effects;
        public Dictionary<int, Dictionary<int, int>> Zones = new Dictionary<int, Dictionary<int, int>>();
        public int Sector = -1, SectorSize, Format, ButtonCount;
        // 0x8100 fn 0 byte 4; -1 until read. OMM offers no restore on a mouse that has none.
        public int FactoryProfiles = -1;
        public byte[][] Buttons;
        public byte[] Current, Profile;

        public bool Writable
        {
            get
            {
                if (SaveOnly) return Profile != null;
                // Pages 1–16 are the flash; 17 and up hold the G500's and G700s's factory profiles (cvuchener's g500 doc/memory.md).
                if (Flash) return Sector >= 2 && Sector <= 16 && SectorSize == G9xRegisters.PageSize && Current != null;
                return Onboard && (ClassicFormat(Format) || WideFormat(Format)) && SectorSize >= 0xF0
                    && ((Sector > 0 && Sector < 0x100) || Fresh);
            }
        }

        // Format 7 (PRO X SUPERLIGHT 2, PRO X 2 DEX): written by Featherweight on a SUPERLIGHT 2 and by
        // marco-vrinssen/logitech-onboard-memory on a DEX, each read back byte for byte (docs/newer-mice.md). Formats 6 and 8 (a
        // SUPERSTRIKE, per superstrike-studio) have the same layout at every byte BuildWide writes, per OMM's layout builders
        // (docs/research/omm): 6 lacks bunny-hop prevention at 0x25, 8 adds analog buttons at 0x26. OMM refuses format 9, and 10 is
        // another layout.
        public static bool WideFormat(int format) { return format >= 6 && format <= 8; }

        // Format 2 (G502 HERO, G303, G502 Proteus Spectrum) has format 3's layout up to the LED blocks at 0xD0 and 0xDB:
        // libratbag, Solaar and cvuchener's hidpp agree (summed up in g502-hub's docs/protocol.md). Format 1 ends at 0xD0; 4 keeps
        // the same records at 0xD0 and 0xDB and adds power-save copies at 0xE6 and 0xF1, and 5 has 0x8071 records there and flags at
        // 0xFC (OMM's builders, docs/research/omm), which G-Lite doesn't write. Only these get their LED records written and a profile
        // set up from the factory one.
        public static bool KnownFormat(int format) { return format == 2 || format == 3; }

        // Format 4 (the original PRO X SUPERLIGHT) has format 3's layout up to 0xD0: GFlick (wrote one and read it back) and
        // onboard-lite (saved on one, wired and over LIGHTSPEED) both use it, and with g502-hub and libratbag treat formats 1–5 alike
        // there. So does format 5 (the G502 X PLUS: GOMM), per OMM's layout builders too (docs/research/omm). Both have a write counter
        // at 0x12 and timeouts at 0x1C and 0x1E, which G-Lite doesn't touch, and their LED records are left as they are
        // (docs/newer-mice.md).
        public static bool ClassicFormat(int format) { return KnownFormat(format) || format == 4 || format == 5; }
        public bool Savable { get { return Writable && ProfileOk; } }
        public string Key { get { return Sector.ToString("X4"); } }
    }

    public const int RomSector = 0x0101;

    // Where things are in an onboard profile, formats 2 and 3 (libratbag, Solaar and cvuchener's hidpp agree): the report interval in ms,
    // the default DPI stage, five 2-byte stages (little-endian), 4 bytes per button, the name in UTF-16, then an 11-byte LED record per
    // zone (the effect id, then the 0x8070 parameters). The CRC is in the last two bytes.
    public const int ReportIntervalAt = 0x00, DefaultStageAt = 0x01, StagesAt = 0x03, StageCount = 5, ButtonsAt = 0x20, ButtonLength = 4,
        NameAt = 0xA0, NameLength = 0x30, LightRecordsAt = 0xD0, LightRecordLength = 11;

    // Formats 6 to 8: the rate as a 0x8061 index, the receiver's at 0 and the cable's at 1 (Featherweight changed byte 0 on a
    // SUPERLIGHT 2 and its live rate over the receiver followed), the default stage at 2, five 5-byte stage records from 4 (X and Y
    // little-endian, lift-off), the buttons from 0x30 (docs/newer-mice.md).
    public const int WideRateAt = 0, WideWiredRateAt = 1, WideDefaultStageAt = 2;
    const int WideStagesAt = 4, WideStageLength = 5, WideButtonsAt = 0x30;
    static readonly int[] WideRateValues = { 125, 250, 500, 1000, 2000, 4000, 8000 };

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
        return Enumerable.Range(0, StageCount)
            .Select(i => profile[StagesAt + 2 * i] | profile[StagesAt + 1 + 2 * i] << 8)
            .TakeWhile(x => x != 0 && x != 0xFFFF)
            .ToArray();
    }

    public static int[] WideStages(byte[] profile, int[] dpis)
    {
        var stages = Enumerable.Range(0, StageCount)
            .Select(i => profile[WideStagesAt + WideStageLength * i] | profile[WideStagesAt + 1 + WideStageLength * i] << 8)
            .TakeWhile(x => x != 0 && x != 0xFFFF)
            .ToArray();
        return dpis.Length > 0 && stages.All(x => x >= dpis[0] && x <= dpis[dpis.Length - 1]) ? stages : new int[0];
    }

    static byte[][] ButtonsOf(byte[] profile, int at, int count)
    {
        return Enumerable.Range(0, count).Select(i => profile.Skip(at + ButtonLength * i).Take(ButtonLength).ToArray()).ToArray();
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
        var light = Decode(profile.Skip(LightRecordsAt + LightRecordLength).Take(LightRecordLength).ToArray());
        int defaultStage = profile[DefaultStageAt], interval = profile[ReportIntervalAt];
        return (defaultStage < stages.Length ? stages[defaultStage] + " DPI" : "DPI stages")
            + ", " + (light == null ? "lighting" : EffectById(light.Effect).Name)
            + ", " + (interval > 0 ? 1000 / interval + " Hz" : "");
    }

    public static byte[] Build(byte[] old, MouseSettings want, int zones)
    {
        var built = (byte[])old.Clone();
        var stages = StagesOf(built);
        if (stages.Length == 0) throw new RefusedException("The profile on the mouse has no DPI stages, not touching it");
        int i = want.Stage >= 0 && want.Stage < stages.Length ? want.Stage : Array.IndexOf(stages, want.Dpi);
        if (i < 0) i = Math.Min((int)built[DefaultStageAt], stages.Length - 1);
        built[StagesAt + 2 * i] = (byte)want.Dpi;
        built[StagesAt + 1 + 2 * i] = (byte)(want.Dpi >> 8);
        built[DefaultStageAt] = (byte)i;
        if (want.Rate > 0 && want.Rate <= 1000) built[ReportIntervalAt] = (byte)(1000 / want.Rate);
        if (want.Buttons != null) CopyButtons(built, want.Buttons, ButtonsAt);
        if (want.Light != null)
            for (int zone = 0; zone < Math.Min(zones, 2); zone++)
            {
                built[LightRecordsAt + LightRecordLength * zone] = want.Light.Effect;
                Encode(want.Light).CopyTo(built, LightRecordsAt + 1 + LightRecordLength * zone);
            }
        WriteChecksum(built);
        return built;
    }

    public static int[] WideStagesOf(byte[] profile) { return WideStages(profile, new[] { 1, 0xFFFE }); }

    // Formats 6 to 8: only the DPI stages, the default stage, the rate for the link in use and the buttons change; the rest stays as read.
    public static byte[] BuildWide(byte[] old, MouseSettings want, bool wired)
    {
        var built = (byte[])old.Clone();
        var stages = WideStagesOf(built);
        if (stages.Length == 0) throw new RefusedException("The profile on the mouse has no DPI stages, not touching it");
        int i = want.Stage >= 0 && want.Stage < stages.Length ? want.Stage : Array.IndexOf(stages, want.Dpi);
        if (i < 0) i = Math.Min((int)built[WideDefaultStageAt], stages.Length - 1);
        // The stage is only rewritten when its DPI changed, and a Y set apart from X (in G HUB) keeps its ratio to X.
        int at = WideStagesAt + WideStageLength * i, x = stages[i], y = built[at + 2] | built[at + 3] << 8;
        if (want.Dpi != x)
        {
            int newY = y == x || y == 0 || y == 0xFFFF ? want.Dpi : (int)Math.Round((double)y * want.Dpi / x);
            built[at] = (byte)want.Dpi;
            built[at + 1] = (byte)(want.Dpi >> 8);
            built[at + 2] = (byte)newY;
            built[at + 3] = (byte)(newY >> 8);
        }
        built[WideDefaultStageAt] = (byte)i;
        int rate = Array.IndexOf(WideRateValues, want.Rate);
        if (rate >= 0) built[wired ? WideWiredRateAt : WideRateAt] = (byte)rate;
        if (want.Buttons != null) CopyButtons(built, want.Buttons, WideButtonsAt);
        WriteChecksum(built);
        return built;
    }

    public static string DescribeWide(byte[] profile)
    {
        if (!Valid(profile)) return "damaged, can't be restored";
        var stages = WideStagesOf(profile);
        int defaultStage = profile[WideDefaultStageAt];
        return defaultStage < stages.Length ? stages[defaultStage] + " DPI" : "DPI stages";
    }

    static void CopyButtons(byte[] built, byte[][] buttons, int at)
    {
        int count = Math.Min(buttons.Length, 16);
        var before = ButtonsOf(built, at, count);
        if (Bindings.HasLeft(before, count) && !Bindings.HasLeft(buttons, count))
            throw new RefusedException("No button is set to Left click, not saving");
        for (int button = 0; button < count; button++) buttons[button].CopyTo(built, at + ButtonLength * button);
    }

    // OMM restores a profile by copying a factory one over it, so the factory profiles have the user profiles' layout, formats 6 to 8
    // too (docs/research/omm). Featherweight read the SUPERLIGHT 2's with no valid CRC, so its rate bytes, stages (in the mouse's
    // range) and default stage are what tell it's a profile; the checksum is written here.
    public static byte[] Factory(byte[] rom, byte[] current, State state)
    {
        bool usable;
        if (State.WideFormat(state.Format))
        {
            int stages = WideStages(rom, state.Dpis).Length;
            usable = rom[WideRateAt] < WideRateValues.Length && rom[WideWiredRateAt] < WideRateValues.Length && stages > 0
                && rom[WideDefaultStageAt] < stages;
        }
        else usable = rom[ReportIntervalAt] >= 1 && rom[ReportIntervalAt] <= 8 && StagesOf(rom).Length > 0;
        if (!usable)
            throw new RefusedException("Couldn't read the mouse's factory profile, not restoring it (it starts "
                + BitConverter.ToString(rom, 0, Math.Min(13, rom.Length)).Replace("-", " ") + ")");
        var built = (byte[])rom.Clone();
        if (Valid(current)) Array.Copy(current, NameAt, built, NameAt, NameLength);
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
        var state = new State { Wired = hidpp.Direct };
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
            // The original PRO X SUPERLIGHT flags 0x8070 internal and hidden: its status light, which refuses fn 1 with error 5
            // (Solaar's PRO X Wireless 4093 report; GFlick doesn't show it either).
            int zones = hidpp.Hidden(0x8070) ? 0 : hidpp.Call(lightFeature, 0)[0];
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
                state.FactoryProfiles = info[4];
                // Formats 2 to 5 keep the rate as an interval in ms, so a faster rate couldn't be saved.
                if (State.ClassicFormat(state.Format)) state.Rates = state.Rates.Where(hz => hz <= 1000).ToArray();
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
                        bool wide = State.WideFormat(state.Format);
                        // Their buttons take 48 bytes from 0x30, then reserved bytes up to the G-shift layer at 0x70 (OMM's builders).
                        if (wide) state.ButtonCount = Math.Min(12, state.ButtonCount);
                        // Format 10 has format 1's layout with other fields (docs/newer-mice.md), and read at these offsets its stages
                        // can come out wrong but in range, so formats past 8 show none.
                        if (state.ProfileOk && wide)
                        {
                            state.Stages = WideStages(state.Profile, state.Dpis);
                            state.Buttons = ButtonsOf(state.Profile, WideButtonsAt, state.ButtonCount);
                        }
                        state.Profile = null;
                        // Only formats 6 to 8 are saved, so only their damage matters (shown, and no save); the others are just read.
                        if (!wide) state.ProfileOk = true;
                    }
                    else
                    {
                        state.Stages = StagesOf(state.Profile);
                        state.Buttons = ButtonsOf(state.Profile, ButtonsAt, state.ButtonCount);
                        if (logo >= 0 && logo < 2)
                            state.Light = Decode(
                                state.Profile.Skip(LightRecordsAt + LightRecordLength * logo).Take(LightRecordLength).ToArray());
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
