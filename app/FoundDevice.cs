using System;
using System.Linq;
using System.Text.RegularExpressions;

class FoundDevice
{
    public string Key, Path, ShortPath, Name = "", Kind = "device", Link = "", Firmware = "", Models = "";
    public byte Index;
    public int WirelessId, Battery = -1, Millivolts, BatteryId;
    public byte BatteryFeature;
    public bool BatteryFromVoltage, Charging, Asleep, Hidpp10, PlainHid, BatteryGoodOrLowOnly, HasVendorCollection;
    public int[] Features = new int[0];

    public bool Has(int feature) { return Features.Contains(feature); }
    public string Slot { get { return Index == 0xFF || Hidpp10 ? "" : ", slot " + Index; } }
    public bool IsG515
    {
        get
        {
            var upper = Name.ToUpper();
            return upper.Contains("G515") && !upper.Contains("RAPID") && Has(0x8071) && Has(0x8040) && Has(0x8101);
        }
    }
    public bool IsG915
    {
        get
        {
            var upper = Name.ToUpper();
            return (upper.Contains("G915") || upper.Contains("G815")) && !upper.Contains("G915 X")
                && Has(0x8071) && Has(0x8040) && Has(0x8100) && !Has(0x8101);
        }
    }
    public bool IsG512X { get { return Name.ToUpper().Contains("G512 X 75") && Has(0x8071) && Has(0x8040) && Has(0x8081); } }
    public bool IsRgbKeyboard { get { return Kind == "keyboard" && Has(0x8071) && Has(0x8040); } }
    public bool IsLedKeyboard { get { return Kind == "keyboard" && Has(0x8070) && !Has(0x8071); } }
    public bool IsG510
    {
        get { return PlainHid && HasVendorCollection && Path != null && Regex.IsMatch(Path, @"PID_C22[DE]", RegexOptions.IgnoreCase); }
    }
    public bool IsG560 { get { return Name.ToUpper().Contains("G560") && Has(0x8070); } }
    public bool IsMouse { get { return Kind == "mouse" && (Has(0x2201) || Has(0x2202)); } }

    // The G305 and G304 (4074) have one status LED that 0x8070 drives live, but the mouse ignores the profile's lighting record: saved
    // as Static, the LED went off at the reload (tried on a G305; libratbag lists no LEDs for it).
    public bool LightIsLiveOnly { get { return WirelessId == 0x4074; } }
    public bool IsG9x { get { return Hidpp10 && Path != null && Regex.IsMatch(Path, @"PID_(C066|C249)", RegexOptions.IgnoreCase); } }
    public bool IsG500
    {
        get
        {
            return Hidpp10 && Path != null
                && (Regex.IsMatch(Path, @"PID_(C068|C24E|C06B|C07C)", RegexOptions.IgnoreCase)
                    || WirelessId == 0x1023 || WirelessId == 0x102A);
        }
    }
    public bool IsG5 { get { return Hidpp10 && Path != null && Regex.IsMatch(Path, @"PID_(C041|C049)", RegexOptions.IgnoreCase); } }
    // The G600 doesn't speak HID++: its settings are feature reports on its vendor collection (G600.cs).
    public bool IsG600
    {
        get { return PlainHid && HasVendorCollection && Path != null && Regex.IsMatch(Path, @"PID_C24A", RegexOptions.IgnoreCase); }
    }
    // The G300 too, on the same FF80 collection (G300.cs).
    public bool IsG300
    {
        get { return PlainHid && HasVendorCollection && Path != null && Regex.IsMatch(Path, @"PID_C246", RegexOptions.IgnoreCase); }
    }
    // The original G502 (Proteus Core) has no model id in 0x0003 (docs/reports/g502-c07d.txt), so its USB id tells it.
    public bool IsOriginalG502
    {
        get { return Models.Contains("C07D") || (Path != null && Regex.IsMatch(Path, @"PID_C07D", RegexOptions.IgnoreCase)); }
    }
    public bool IsG9 { get { return Hidpp10 && Path != null && Regex.IsMatch(Path, @"PID_C048", RegexOptions.IgnoreCase); } }
    public G9xModel G500Model
    {
        get
        {
            if (WirelessId == 0x1023) return G9xModel.G700;
            if (WirelessId == 0x102A) return G9xModel.G700s;
            var pid = Regex.Match(Path ?? "", "PID_([0-9A-F]{4})", RegexOptions.IgnoreCase).Groups[1].Value.ToUpperInvariant();
            return pid == "C24E" ? G9xModel.G500s : pid == "C06B" ? G9xModel.G700 : pid == "C07C" ? G9xModel.G700s : G9xModel.G500;
        }
    }
    public bool IsLitra
    {
        get { return Name.ToUpper().Contains("LITRA") || (Path != null && Regex.IsMatch(Path, @"PID_C90[0-3]", RegexOptions.IgnoreCase)); }
    }
    public bool IsLight { get { return IsLitra && Has(0x1990); } }
    public bool IsG { get { return LooksG(Name) || IsLitra; } }
    public bool IsHeadset { get { return Kind == "headset" && Has(0x8300); } }
    public bool HasAutoOff
    {
        get { return Path != null && Regex.IsMatch(Path, @"PID_(0AAA|0ABA|0AFB|0AFC)", RegexOptions.IgnoreCase) && Has(0x1F20); }
    }

    public string SettingsKey { get { return new string(ShortName.ToLower().Where(char.IsLetterOrDigit).ToArray()); } }

    public string ShortName
    {
        get
        {
            return Regex.Replace(
                Regex.Replace(
                    Regex.Replace(Name, "^" + GamingFirst, "", RegexOptions.IgnoreCase),
                    @"(\s+(Wireless\s+)?(RGB\s+)?Mechanical)?\s+Gaming\s+(Mouse|Headset|Keyboard)$", "", RegexOptions.IgnoreCase),
                @"\s+LIGHTSPEED\b", "", RegexOptions.IgnoreCase);
        }
    }

    public bool MediaKeys { get { return BatteryGoodOrLowOnly && Has(0x40A0); } }

    public string BatteryText { get { return Battery < 0 || BatteryGoodOrLowOnly ? "" : Battery + "%"; } }

    public string BatteryWords
    {
        get
        {
            return Battery < 0 ? "–"
                : BatteryGoodOrLowOnly ? (Battery >= 30 ? "Good" : "Low") + " (it only reports good or low)"
                : BatteryText;
        }
    }

    // The audio-only headsets are listed through their wireless dongle, so they don't count as on a cable.
    public bool Wired { get { return Link == "USB" && !(PlainHid && Kind == "headset"); } }

    public int Level
    {
        get
        {
            return Asleep ? Ui.Asleep
                : Battery < 0 ? (Wired ? Ui.Usb : -1)
                : (BatteryGoodOrLowOnly && Battery >= 30 ? 100 : Battery) + (Charging ? Ui.Charging : 0);
        }
    }

    public static string ModelText(byte[] info)
    {
        if (info == null || info.Length < 13) return "";
        int transports = info[5] << 8 | info[6];
        var ids = Enumerable.Range(0, 3).Select(i => info[7 + 2 * i] << 8 | info[8 + 2 * i]).ToList();
        var names = new[] { "Bluetooth", "Bluetooth", "LIGHTSPEED", "USB" };
        var labels = Enumerable.Range(0, 4).Where(bit => (transports >> bit & 1) != 0).Select(bit => names[bit]).ToList();
        bool matches = labels.Count > 0 && labels.Count <= 3
            && ids.Take(labels.Count).All(id => id != 0) && ids.Skip(labels.Count).All(id => id == 0);
        if (!matches) return string.Join(", ", ids.Where(id => id != 0).Select(id => id.ToString("X4")));
        var order = new[] { "USB", "LIGHTSPEED", "Bluetooth" };
        return string.Join(", ", labels
            .Select((label, i) => new { label, id = ids[i] })
            .OrderBy(pair => Array.IndexOf(order, pair.label))
            .Select(pair => pair.label + " " + pair.id.ToString("X4")));
    }

    public string Subtitle
    {
        get
        {
            return Link;
        }
    }

    // Some older devices put the model last: "Gaming Keyboard G213".
    const string GamingFirst = @"((\w+\s+)*?Gaming\s+(Keyboard|Mouse|Headset)\s+(?=G\s?\d))";

    public static bool LooksG(string name)
    {
        return Regex.IsMatch(name ?? "",
            @"^(Logitech\s+)?" + GamingFirst + @"?(G\s?\d|G\s+PRO|PRO\b|ASTRO|A\d0\b)", RegexOptions.IgnoreCase);
    }

    public static string FirmwareText(byte[] info)
    {
        var prefix = new string(info.Skip(1).Take(3)
            .TakeWhile(value => value >= 0x20 && value < 0x7F)
            .Select(value => (char)value)
            .ToArray()).Trim();
        return (prefix + " " + info[4].ToString("X2") + "." + info[5].ToString("X2")
            + ", build " + info[6].ToString("X2") + info[7].ToString("X2")).Trim();
    }

    public static int FromVolts(int millivolts) { return Math.Max(0, Math.Min(100, (millivolts - 3500) * 100 / 600)); }

    static readonly int[] ChargingCurve = { 3270, 2, 3670, 3, 3790, 5, 3910, 8, 4000, 20, 4050, 33, 4100, 46, 4150, 57, 4200, 68 };

    public static int FromChargingVolts(int millivolts)
    {
        var curve = ChargingCurve;
        if (millivolts <= curve[0]) return curve[1];
        for (int i = 2; i < curve.Length; i += 2)
            if (millivolts <= curve[i])
                return curve[i - 1] + (millivolts - curve[i - 2]) * (curve[i + 1] - curve[i - 1]) / (curve[i] - curve[i - 2]);
        return curve[curve.Length - 1];
    }
}
