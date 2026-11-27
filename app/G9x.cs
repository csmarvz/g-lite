using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

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

    public void SendDpi(Hidpp hidpp, MouseProtocol.State state, MouseSettings want, MouseSettings sent)
    {
        if (sent == null || want.Dpi != sent.Dpi) G9xRegisters.SetDpi(hidpp, want.Dpi, model);
    }

    public void SetLight(Hidpp hidpp, MouseProtocol.State state, MouseLight light)
    {
        if (!model.Light) throw new RefusedException("This mouse has no colour light");
        G9xRegisters.SetLight(hidpp, light);
    }

    public void ReleaseLight(Hidpp hidpp) { }
    public void SetRate(Hidpp hidpp, int hz) { G9xRegisters.SetRate(hidpp, hz); }
    public void SetInvert(Hidpp hidpp, bool on) { throw new RefusedException("This mouse can't invert its wheel"); }
    public byte[] ReadProfile(Hidpp hidpp, MouseProtocol.State state) { return G9xRegisters.ReadPage(hidpp, state.Sector); }
    public void Setup(Hidpp hidpp, MouseProtocol.State state, MouseSettings want)
    {
        throw new RefusedException("Saving isn't supported on this mouse");
    }
    public void Save(Hidpp hidpp, MouseProtocol.State state, byte[] now, MouseSettings want)
    {
        G9xRegisters.Store(hidpp, state, G9xRegisters.Build(now, want, state.Rate, model), now);
    }

    public void Restore(Hidpp hidpp, MouseProtocol.State state, byte[] now, byte[] data)
    {
        if (data == null) throw new RefusedException("G-Lite can't restore the factory profile on this mouse");
        G9xRegisters.Store(hidpp, state, data, now);
    }

    public string Describe(byte[] profile) { return G9xRegisters.Describe(profile, model); }
    public int DefaultStage(byte[] profile) { return profile[G9xRegisters.DefaultStageAt]; }
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

    public void SendDpi(Hidpp hidpp, MouseProtocol.State state, MouseSettings want, MouseSettings sent)
    {
        if (sent == null || want.Dpi != sent.Dpi) hidpp.Register(0x80, 0x63, (byte)DpiToRaw(want.Dpi));
    }

    public void SetLight(Hidpp hidpp, MouseProtocol.State state, MouseLight light)
    {
        throw new RefusedException("G-Lite can't set this mouse's light yet");
    }
    public void ReleaseLight(Hidpp hidpp) { }
    public void SetRate(Hidpp hidpp, int hz) { G9xRegisters.SetRate(hidpp, hz); }
    public void SetInvert(Hidpp hidpp, bool on) { throw new RefusedException("This mouse can't invert its wheel"); }
    public byte[] ReadProfile(Hidpp hidpp, MouseProtocol.State state) { return null; }
    public void Setup(Hidpp hidpp, MouseProtocol.State state, MouseSettings want)
    {
        throw new RefusedException("Saving isn't supported on this mouse");
    }
    public void Save(Hidpp hidpp, MouseProtocol.State state, byte[] now, MouseSettings want)
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

    // A profile page (docs/G9x.md): the LED colour, five 6-byte stages (X and Y, big-endian, then the LED nibbles), the default stage,
    // the report interval in ms. The CRC is in the last two bytes.
    public const int PageSize = 512, ColorAt = 0x00, StagesAt = 0x04, StageCount = 5, StageLength = 6, DefaultStageAt = 0x23,
        ReportIntervalAt = 0x26;

    public static bool Valid(byte[] page) { return page.Length == PageSize && MouseProtocol.Valid(page); }

    public static byte[] ReadPage(Hidpp device, int pageNumber)
    {
        var page = new byte[PageSize];
        for (int offset = 0; offset < PageSize; offset += 16)
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
        return Enumerable.Range(0, StageCount)
            .Select(stage => page[StagesAt + StageLength * stage] << 8 | page[StagesAt + 1 + StageLength * stage])
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
                state.SectorSize = PageSize;
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
        int defaultStage = page[DefaultStageAt], interval = page[ReportIntervalAt];
        var dpi = defaultStage < stages.Length ? stages[defaultStage] + " DPI" : "DPI stages";
        return interval > 0 ? dpi + ", " + 1000 / interval + " Hz" : dpi;
    }

    public static byte[] Build(byte[] old, MouseSettings want, int hz, G9xModel model)
    {
        var page = (byte[])old.Clone();
        var stages = StagesOf(page, model);
        if (stages.Length == 0) throw new RefusedException("The profile on the mouse has no DPI stages, not touching it");
        int stage = want.Stage >= 0 && want.Stage < stages.Length ? want.Stage : Array.IndexOf(stages, want.Dpi);
        if (stage < 0) stage = Math.Min((int)page[DefaultStageAt], stages.Length - 1);
        int raw = model.DpiToRaw(want.Dpi), stageAt = StagesAt + StageLength * stage;
        page[stageAt] = page[stageAt + 2] = (byte)(raw >> 8);
        page[stageAt + 1] = page[stageAt + 3] = (byte)raw;
        page[DefaultStageAt] = (byte)stage;
        if (hz > 0) page[ReportIntervalAt] = (byte)(1000 / hz);
        if (want.Light != null && model.Light)
        {
            var color = want.Light.Effect == 0x01 ? MouseProtocol.Encode(want.Light) : new byte[3];
            Array.Copy(color, 0, page, ColorAt, 3);
        }
        MouseProtocol.WriteChecksum(page);
        return page;
    }

    // previous: the page as it was before (backed up), written back if the write fails after the erase.
    public static void Store(Hidpp device, MouseProtocol.State state, byte[] page, byte[] previous)
    {
        if (!state.Flash || !state.Writable) throw new RefusedException("Saving isn't supported on this mouse");
        if (page.Length != PageSize || !Valid(page)) throw new RefusedException("Refusing to write a profile with a bad checksum");
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
                if (previous == null || previous.Length != PageSize || !Valid(previous))
                    throw new RefusedException("no good copy to put back");
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
            var data = new byte[PageSize / 2];
            Array.Copy(page, PageSize / 2 * half, data, 0, PageSize / 2);
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
