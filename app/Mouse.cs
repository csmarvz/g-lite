using System;
using System.Globalization;
using System.Linq;

class MouseLight
{
    public byte Effect = 0x01;
    public int Color = 0xFFFFFF, Period = 5000, Bright = 100, Direction = 6;
    public MouseLight Clone() { return (MouseLight)MemberwiseClone(); }

    public string ToText() { return string.Join(",", Effect, Color.ToString("X6"), Period, Bright); }

    public static MouseLight FromText(string text)
    {
        var parts = text.Split(',');
        return new MouseLight
        {
            Effect = byte.Parse(parts[0]), Color = int.Parse(parts[1], NumberStyles.HexNumber),
            Period = int.Parse(parts[2]), Bright = int.Parse(parts[3])
        };
    }

    public bool Same(MouseLight other)
    {
        return other != null && Effect == other.Effect && Color == other.Color && Period == other.Period && Bright == other.Bright
            && (Effect != 0x04 || Direction == other.Direction);
    }
}

class MouseSettings
{
    public int Dpi = 800, Rate, Stage = -1;
    public MouseLight Light;
    public byte[][] Buttons;

    public MouseSettings Clone()
    {
        return new MouseSettings
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

    public static MouseSettings FromText(string text)
    {
        var parts = text.Split(',');
        return new MouseSettings
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

interface IMouse
{
    EffectInfo[] Effects { get; }
    bool HasFactory { get; }
    MouseProtocol.State Read(Hidpp hidpp);
    void SendDpi(Hidpp hidpp, MouseProtocol.State state, MouseSettings want, MouseSettings sent);
    void SetLight(Hidpp hidpp, MouseProtocol.State state, MouseLight light);
    void SetRate(Hidpp hidpp, int hz);
    void SetInvert(Hidpp hidpp, bool on);
    byte[] ReadProfile(Hidpp hidpp, MouseProtocol.State state);
    void Setup(Hidpp hidpp, MouseProtocol.State state, MouseSettings want);
    void Save(Hidpp hidpp, MouseProtocol.State state, byte[] now, MouseSettings want);
    void Restore(Hidpp hidpp, MouseProtocol.State state, byte[] now, byte[] data);
    string Describe(byte[] profile);
    int DefaultStage(byte[] profile);
}

class HidppMouse : IMouse
{
    // The last profile format and factory profile count read: formats 6 to 8 have their own layout.
    int format, factoryProfiles = -1;
    bool Wide { get { return MouseProtocol.State.WideFormat(format); } }

    public EffectInfo[] Effects { get { return MouseProtocol.Effects; } }
    public bool HasFactory { get { return factoryProfiles != 0; } }
    public MouseProtocol.State Read(Hidpp hidpp)
    {
        var state = MouseProtocol.Read(hidpp);
        format = state.Format;
        factoryProfiles = state.FactoryProfiles;
        return state;
    }

    // A format 7 mouse refuses a DPI that isn't one of its stages while it runs its profile (logi-cli, OpenGMice), and 6 and 8 are
    // taken to do the same: an edited stage goes on the mouse when saved, so only the stage is picked.
    public void SendDpi(Hidpp hidpp, MouseProtocol.State state, MouseSettings want, MouseSettings sent)
    {
        if (sent != null && want.Dpi == sent.Dpi && want.Stage == sent.Stage) return;
        bool staged = state.Onboard && want.Stage >= 0 && want.Stage < state.Stages.Length;
        bool saved = staged && state.Stages[want.Stage] == want.Dpi;
        if (saved) MouseProtocol.SetStage(hidpp, want.Stage);
        else if (Wide && state.Savable) { if (staged && (sent == null || sent.Stage != want.Stage)) MouseProtocol.SetStage(hidpp, want.Stage); }
        else MouseProtocol.SetDpi(hidpp, want.Dpi);
    }

    public void SetLight(Hidpp hidpp, MouseProtocol.State state, MouseLight light) { MouseProtocol.SetLight(hidpp, state, light); }
    public void SetRate(Hidpp hidpp, int hz) { MouseProtocol.SetRate(hidpp, hz); }
    public void SetInvert(Hidpp hidpp, bool on) { MouseProtocol.SetInvert(hidpp, on); }
    public byte[] ReadProfile(Hidpp hidpp, MouseProtocol.State state)
    {
        return MouseProtocol.ReadSector(hidpp, state.Sector, state.SectorSize);
    }
    // The LED records of formats 4 and 5 aren't checked, so they're kept: a zone count of 0 leaves them.
    byte[] Build(byte[] now, MouseSettings want, MouseProtocol.State state)
    {
        if (Wide) return MouseProtocol.BuildWide(now, want, state.Wired);
        return MouseProtocol.Build(now, want, MouseProtocol.State.KnownFormat(format) ? state.Zones.Count : 0);
    }

    public void Setup(Hidpp hidpp, MouseProtocol.State state, MouseSettings want)
    {
        MouseProtocol.Setup(hidpp, state,
            MouseProtocol.Build(MouseProtocol.ReadSector(hidpp, state.Sector, state.SectorSize), want, state.Zones.Count));
    }
    public void Save(Hidpp hidpp, MouseProtocol.State state, byte[] now, MouseSettings want)
    {
        MouseProtocol.Store(hidpp, state, Build(now, want, state));
    }
    // Reloading the active profile (fn 3) leaves a G305's DPI as it was set live, so the restored default stage is set too. Live
    // changes the restored profile doesn't hold (a light, a DPI) stay too: leaving onboard mode and coming back drops them (fn 1
    // `02` then `01`, tried by PeriHub on a G502 HERO), done only when the mouse runs its onboard profiles.
    public void Restore(Hidpp hidpp, MouseProtocol.State state, byte[] now, byte[] data)
    {
        var restored = data ?? MouseProtocol.Factory(MouseProtocol.ReadSector(hidpp, MouseProtocol.RomSector, state.SectorSize), now, state);
        MouseProtocol.Store(hidpp, state, restored);
        if (state.Onboard)
        {
            byte profiles = hidpp.FeatureIndex(0x8100);
            bool refused = false;
            // `01` follows unless `02` was refused, even when `02` timed out and may have been taken: tried twice and not swallowed,
            // since a mouse left out of onboard mode would ignore its profiles.
            try { hidpp.Call(profiles, 1, 0x02); }
            catch (HidppError) { refused = true; }
            finally
            {
                if (!refused)
                    try { hidpp.Call(profiles, 1, 0x01); }
                    catch (HidppError) { hidpp.Call(profiles, 1, 0x01); }
            }
        }
        int stage = DefaultStage(restored);
        if (stage < (Wide ? MouseProtocol.WideStagesOf(restored) : MouseProtocol.StagesOf(restored)).Length)
            try { MouseProtocol.SetStage(hidpp, stage); }
            catch (HidppError) { }
    }
    public string Describe(byte[] profile) { return Wide ? MouseProtocol.DescribeWide(profile) : MouseProtocol.Describe(profile); }
    public int DefaultStage(byte[] profile) { return profile[Wide ? MouseProtocol.WideDefaultStageAt : MouseProtocol.DefaultStageAt]; }
}
