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
    public EffectInfo[] Effects { get { return MouseProtocol.Effects; } }
    public bool HasFactory { get { return true; } }
    public MouseProtocol.State Read(Hidpp hidpp) { return MouseProtocol.Read(hidpp); }

    public void SendDpi(Hidpp hidpp, MouseProtocol.State state, MouseSettings want, MouseSettings sent)
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
    public void Setup(Hidpp hidpp, MouseProtocol.State state, MouseSettings want)
    {
        MouseProtocol.Setup(hidpp, state,
            MouseProtocol.Build(MouseProtocol.ReadSector(hidpp, state.Sector, state.SectorSize), want, state.Zones.Count));
    }
    public void Save(Hidpp hidpp, MouseProtocol.State state, byte[] now, MouseSettings want)
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
    public int DefaultStage(byte[] profile) { return profile[MouseProtocol.DefaultStageAt]; }
}
