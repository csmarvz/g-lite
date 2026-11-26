using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;

static class Devices
{
    // A receiver's 0x41 notice, 10 <index> 41 <protocol> <flags> <wireless id LE>: flags bit 6 is set while the link is down (Solaar
    // reads the byte after the protocol type; MyHUB, docs/research/more-references.md).
    public static bool LinkUp(byte[] report) { return report.Length > 4 && (report[4] & 0x40) == 0; }

    const string HidClass = "#{4d1e55b2-f16f-11cf-88cb-001111000030}";
    public static readonly string[] Kinds = { "keyboard", "device", "device", "mouse", "mouse", "mouse", "device", "device", "headset" };

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern SafeFileHandle CreateFile(
        string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);
    [DllImport("hid.dll")] static extern bool HidD_GetPreparsedData(SafeFileHandle handle, out IntPtr data);
    [DllImport("hid.dll")] static extern bool HidD_FreePreparsedData(IntPtr data);
    [DllImport("hid.dll")] static extern int HidP_GetCaps(IntPtr data, short[] caps);
    [DllImport("hid.dll", CharSet = CharSet.Unicode)]
    static extern bool HidD_GetProductString(SafeFileHandle handle, System.Text.StringBuilder buffer, int length);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    static extern int CM_Get_Device_Interface_List_Size(out int length, ref Guid interfaceClass, string deviceId, int flags);
    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    static extern int CM_Get_Device_Interface_List(ref Guid interfaceClass, string deviceId, char[] buffer, int length, int flags);

    static readonly Regex PlainModels = new Regex(@"PID_(C22D|C22E|C216|C218|C219)", RegexOptions.IgnoreCase);
    // Audio devices, and old devices that may not speak HID++ (or answer it): listed for their report when the HID++ scan didn't find them.
    // Ids from docs/research/device-map.md and usb-id-registry.md. Each keyboard by its G-key or LCD component, so it's listed once.
    static readonly Regex UnfoundModels = new Regex(
        @"VID_046D&PID_(0[AB][0-9A-F]{2}|C01E|C041|C048|C049|C051|C066|C068|C06B|C07C|C21C|C222|C225|C227|C229|C22B|C245|C246|C247|C248|C249|C24A|C24B|C24C|C24D|C24E|C531)", RegexOptions.IgnoreCase);
    static readonly Dictionary<string, string[]> PlainNames = new Dictionary<string, string[]>
    {
        { "C22D", new[] { "G510 Gaming Keyboard", "keyboard" } },
        { "C22E", new[] { "G510 Gaming Keyboard", "keyboard" } },
        { "C216", new[] { "Gamepad F310", "gamepad" } },
        { "C218", new[] { "Rumble Gamepad F510", "gamepad" } },
        { "C219", new[] { "Wireless Gamepad F710", "gamepad" } },
        { "C01E", new[] { "MX518 Optical Mouse", "mouse" } },
        { "C051", new[] { "MX518 Optical Mouse", "mouse" } },
        { "C041", new[] { "G5 Laser Mouse", "mouse" } },
        { "C049", new[] { "G5 Laser Mouse", "mouse" } },
        { "C048", new[] { "G9 Laser Mouse", "mouse" } },
        { "C066", new[] { "G9x Laser Mouse", "mouse" } },
        { "C249", new[] { "G9x Laser Mouse", "mouse" } },
        { "C068", new[] { "G500 Laser Mouse", "mouse" } },
        { "C24E", new[] { "G500s Laser Gaming Mouse", "mouse" } },
        { "C06B", new[] { "G700 Wireless Gaming Mouse", "mouse" } },
        { "C07C", new[] { "G700s Rechargeable Gaming Mouse", "mouse" } },
        { "C531", new[] { "G700 / G700s", "mouse" } },
        { "C246", new[] { "G300 Gaming Mouse", "mouse" } },
        { "C245", new[] { "G400 Optical Mouse", "mouse" } },
        { "C24C", new[] { "G400s Optical Mouse", "mouse" } },
        { "C247", new[] { "G100s Optical Gaming Mouse", "mouse" } },
        { "C24A", new[] { "G600 Gaming Mouse", "mouse" } },
        { "C225", new[] { "G11 Gaming Keyboard", "keyboard" } },
        { "C21C", new[] { "G13 Advanced Gameboard", "keyboard" } },
        { "C222", new[] { "G15 Gaming Keyboard", "keyboard" } },
        { "C227", new[] { "G15 Gaming Keyboard", "keyboard" } },
        { "C229", new[] { "G19 Gaming Keyboard", "keyboard" } },
        { "C24B", new[] { "G103 Gaming Keyboard", "keyboard" } },
        { "C248", new[] { "G105 Gaming Keyboard", "keyboard" } },
        { "C22B", new[] { "G110 Gaming Keyboard", "keyboard" } },
        { "C24D", new[] { "G710 Gaming Keyboard", "keyboard" } },
    };

    public static string[] HidIds()
    {
        var hid = new Guid(HidClass.Substring(2, 36));
        for (int attempt = 0; attempt < 3; attempt++)
        {
            int length;
            if (CM_Get_Device_Interface_List_Size(out length, ref hid, null, 0) != 0) break;
            var buffer = new char[length];
            if (CM_Get_Device_Interface_List(ref hid, null, buffer, length, 0) != 0) continue;
            return new string(buffer).Split(new[] { '\0' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(path => path.StartsWith(@"\\?\HID#", StringComparison.OrdinalIgnoreCase)
                    && path.IndexOf("046D", StringComparison.OrdinalIgnoreCase) >= 0 && path.LastIndexOf("#{") > 4)
                .Select(path => path.Substring(4, path.LastIndexOf("#{") - 4).Replace('#', '\\').ToUpperInvariant()).ToArray();
        }
        return new string[0];
    }

    public static string PathOf(string id) { return @"\\?\" + id.Replace('\\', '#') + HidClass; }

    public static SafeFileHandle Open(string path) { return CreateFile(path, 0xC0000000, 3, IntPtr.Zero, 3, 0x40000000, IntPtr.Zero); }

    public static SafeFileHandle OpenSync(string path, uint access) { return CreateFile(path, access, 3, IntPtr.Zero, 3, 0, IntPtr.Zero); }

    public static string[] PlainIds(string pid)
    {
        return pid == "" ? new string[0] : HidIds().Where(id => id.Contains("PID_" + pid.ToUpperInvariant())).OrderBy(id => id).ToArray();
    }

    public static string Unfound(string id, IEnumerable<string> foundPaths)
    {
        var match = UnfoundModels.Match(id);
        if (!match.Success) return null;
        string pid = match.Groups[1].Value.ToUpperInvariant();
        return foundPaths.Any(path => path != null && path.IndexOf("PID_" + pid, StringComparison.OrdinalIgnoreCase) >= 0) ? null : pid;
    }

    static List<FoundDevice> FindPlainHid(string[] ids, List<FoundDevice> found)
    {
        var foundPaths = found.Select(device => device.Path).ToList();
        var plain = new List<FoundDevice>();
        var listedPids = ids
            .Select(id => PlainModels.Match(id))
            .Where(match => match.Success)
            .Select(match => match.Groups[1].Value.ToUpperInvariant())
            .Concat(ids.Select(id => Unfound(id, foundPaths)).Where(pid => pid != null))
            .Distinct();
        foreach (var pid in listedPids)
        {
            var paths = ids.Where(id => id.Contains("PID_" + pid)).OrderBy(id => id).Select(PathOf).ToList();
            string[] known;
            PlainNames.TryGetValue(pid, out known);
            var name = paths
                .Select(Product)
                .Select(product => Regex.Replace(product ?? "", @"^Logitech\s+", "", RegexOptions.IgnoreCase))
                .FirstOrDefault(FoundDevice.LooksG)
                ?? (known != null ? known[0] : null);
            // A headset charging over USB also shows its charging port, like the PRO X Wireless's 0ABB "Battery Charger": it has
            // nothing to read or change.
            if (name == null || name.EndsWith("Charger", StringComparison.OrdinalIgnoreCase)) continue;
            string kind = known != null ? known[1] : name.ToUpper().Contains("SPEAKER") ? "device" : "headset";
            // The G510 takes its feature reports on its vendor collection (usage page FF00, MI_01&COL02 in its report), the G600
            // and the G300 on usage page FF80 (LGS's device files; the G300's from PeriHub, which was used on Windows).
            int vendorPage = pid == "C22D" || pid == "C22E" ? 0xFF00 : pid == "C24A" || pid == "C246" ? 0xFF80 : 0;
            var vendor = vendorPage != 0
                ? paths.FirstOrDefault(path =>
                {
                    int page, outputLength;
                    ReadHidCaps(path, out page, out outputLength);
                    return page == vendorPage;
                })
                : null;
            plain.Add(new FoundDevice
            {
                Key = "plain|" + pid, Path = vendor ?? paths[0], HasVendorCollection = vendor != null,
                Name = name, Kind = kind, Link = "USB", Index = 0xFF, Models = "USB " + pid, PlainHid = true
            });
        }
        return plain;
    }

    class HidPort { public string Id, Group, LongPath, ShortPath; }

    static void ReadHidCaps(string path, out int page, out int outputLength)
    {
        page = 0; outputLength = 0;
        using (var handle = Open(path))
        {
            if (handle.IsInvalid) return;
            IntPtr data;
            if (!HidD_GetPreparsedData(handle, out data)) return;
            var caps = new short[HidCaps.Length];
            HidP_GetCaps(data, caps);
            HidD_FreePreparsedData(data);
            page = (ushort)caps[HidCaps.UsagePage];
            outputLength = caps[HidCaps.OutputLength];
        }
    }

    static List<HidPort> Ports(string[] ids)
    {
        var groups = new Dictionary<string, HidPort>();
        foreach (var id in ids)
        {
            int colAt = id.IndexOf("&COL", StringComparison.OrdinalIgnoreCase), slash = id.LastIndexOf('\\'),
                ampAt = id.LastIndexOf('&');
            if (colAt < 0 || slash < 0 || ampAt < slash || PlainModels.IsMatch(id)) continue;
            var group = id.Substring(0, colAt) + "|" + id.Substring(slash, ampAt - slash);
            int page, outputLength;
            var path = PathOf(id);
            ReadHidCaps(path, out page, out outputLength);
            if (page < 0xFF00 || (outputLength != 20 && outputLength != 7)) continue;
            HidPort port;
            if (!groups.TryGetValue(group, out port)) groups[group] = port = new HidPort { Group = group };
            if (outputLength == 20) { port.LongPath = path; port.Id = id; } else port.ShortPath = path;
        }
        return groups.Values.Where(port => port.LongPath != null).ToList();
    }

    public static Dictionary<string, string> Shorts = new Dictionary<string, string>();

    static readonly Dictionary<int, string> Others = new Dictionary<int, string> { { 0x4023, "K270/K295" } };

    public static List<FoundDevice> Scan()
    {
        var ids = HidIds();
        var ports = Ports(ids);
        Shorts = ports
            .Where(port => port.ShortPath != null)
            .GroupBy(port => port.LongPath)
            .ToDictionary(group => group.Key, group => group.First().ShortPath);
        var tasks = ports
            .Select(port => Task.Run(() =>
            {
                try { return ScanPort(port); }
                catch (Exception) { return new List<FoundDevice>(); }
            }))
            .ToArray();
        Task.WaitAll(tasks, 20000);
        var all = tasks.Where(task => task.IsCompleted).SelectMany(task => task.Result).ToList();
        try { all.AddRange(FindPlainHid(ids, all)); } catch (Exception) { }
        all.RemoveAll(device => device.Asleep && device.WirelessId != 0
            && all.Any(other => !other.Asleep && Regex.IsMatch(other.Models, @"\b" + device.WirelessId.ToString("X4") + @"\b")));
        foreach (var device in all.Where(device => !device.Asleep && device.BatteryFromVoltage && device.Battery >= 0))
            Remember(device, DateTime.UtcNow.Ticks / TimeSpan.TicksPerMinute);
        foreach (var device in all.Where(device => Others.ContainsKey(device.WirelessId)))
        {
            device.Name = Others[device.WirelessId];
            device.BatteryGoodOrLowOnly = true;
        }
        foreach (var device in all.Where(device => device.IsLitra)) device.Kind = "light";
        foreach (var device in all.Where(device => !device.Asleep && device.WirelessId != 0))
            Settings.Set("seen." + device.WirelessId.ToString("X4"), device.Name + "|" + device.Kind);
        return all
            .Where(device => device.Name != "")
            .OrderBy(device => device.PlainHid || device.IsG || Others.ContainsKey(device.WirelessId) ? 0 : 1)
            .ThenBy(device => device.Kind == "keyboard" ? 0 : device.Kind == "mouse" ? 1 : device.Kind == "headset" ? 2 : 3)
            .ThenBy(device => device.Name)
            .ToList();
    }

    public static void Remember(FoundDevice device, long minute)
    {
        var key = "battery." + device.SettingsKey;
        var saved = Settings.Get(key).Split('|');
        int level, wasCharging;
        long at;
        if (device.Charging && saved.Length == 3
            && int.TryParse(saved[0], out level) && long.TryParse(saved[1], out at) && int.TryParse(saved[2], out wasCharging))
        {
            long minutes = minute - at;
            if (wasCharging == 1) device.Battery = Math.Max(device.Battery, (int)Math.Min(100, level + minutes));
            else if (minutes < 60) device.Battery = Math.Max(device.Battery, level);
        }
        Settings.Set(key, device.Battery + "|" + minute + "|" + (device.Charging ? 1 : 0));
    }

    static string LinkOf(string id)
    {
        if (id.Contains("{00001812")) return "Bluetooth";
        var match = Regex.Match(id, @"PID_([0-9A-F]{4})", RegexOptions.IgnoreCase);
        var pid = match.Success ? match.Groups[1].Value.ToUpper() : "";
        if (pid == "C52B" || pid == "C532" || pid == "C534") return "Unifying";
        if (pid == "C548") return "Bolt";
        if (pid == "C531") return "Wireless";
        return "LIGHTSPEED";
    }

    static List<FoundDevice> ScanPort(HidPort port)
    {
        var found = new List<FoundDevice>();
        using (var probe = new ScanProbe(port.LongPath, port.ShortPath))
        {
            if (probe.Ping(0xFF, 800) >= 2)
            {
                var device = Describe(probe, port, 0xFF, port.Id.Contains("{00001812") ? "Bluetooth" : "USB");
                if (device.Link == "USB" && device.Kind == "headset"
                    && (device.Has(0x1F20) || device.Has(0x1004) || device.Has(0x1000) || device.Has(0x1001)))
                    device.Link = "LIGHTSPEED";
                found.Add(device);
                return found;
            }
            if (port.ShortPath == null) return found;
            var link = LinkOf(port.Id);
            for (byte slot = 1; slot <= 6; slot++)
            {
                var pair = probe.Register(0xFF, 0x83, 0xB5, (byte)(0x20 + slot - 1));
                int wirelessId, kind;
                if (pair != null) { wirelessId = pair[3] << 8 | pair[4]; kind = pair[7]; }
                else
                {
                    // Bolt receivers keep pairings in 0x5N, with the wireless id's bytes swapped (Solaar's BoltReceiver).
                    pair = link == "Bolt" ? probe.Register(0xFF, 0x83, 0xB5, (byte)(0x50 + slot)) : null;
                    // The G700's receiver (C531) is a Nano receiver with one device (Solaar); whether it lists it in 0xB5 isn't known,
                    // so slot 1 is tried anyway.
                    if (pair == null && !(link == "Wireless" && slot == 1)) continue;
                    wirelessId = pair == null ? 0 : pair[3] << 8 | pair[2];
                    kind = pair == null ? 2 : pair[1] & 0x0F;
                }
                FoundDevice device;
                // A slot the receiver didn't list (the C531's slot 1) gets a short ping: an asleep or absent mouse shouldn't hold up
                // every scan.
                int protocol = probe.Ping(slot, pair == null ? 1000 : 3500);
                if (protocol >= 2) device = Describe(probe, port, slot, link);
                else if (protocol == 1) device = ProbeHidpp10(probe, port, slot, link, wirelessId);
                else
                {
                    device = new FoundDevice { Path = port.LongPath, Index = slot, Link = link, Asleep = true };
                    var seen = Settings.Get("seen." + wirelessId.ToString("X4")).Split('|');
                    device.Name = seen[0] != "" ? seen[0] : link == "LIGHTSPEED" ? "G device" : "";
                    device.Kind = seen.Length > 1 ? seen[1] : kind == 1 ? "keyboard" : kind == 2 ? "mouse" : "device";
                }
                device.WirelessId = wirelessId;
                device.Key = port.LongPath + "|" + slot;
                found.Add(device);
            }
            if (found.Count == 0)
            {
                var firmware = probe.Register(0x00, 0x81, 0xF1, 0x01);
                var usbDevice = firmware == null ? null : ProbeHidpp10(probe, port, firmware);
                if (usbDevice != null && usbDevice.Kind != "device") found.Add(usbDevice);
            }
        }
        return found;
    }

    // Wireless HID++ 1.0 devices, named by their wireless id (Solaar's descriptors).
    public static readonly Dictionary<int, string[]> OldWireless = new Dictionary<int, string[]>
    {
        { 0x1023, new[] { "G700 Gaming Mouse", "mouse" } },
        { 0x102A, new[] { "G700s Gaming Mouse", "mouse" } },
    };

    // A HID++ 1.0 device in a receiver slot: it answered the HID++ 2.0 ping with "invalid sub-ID".
    static FoundDevice ProbeHidpp10(ScanProbe probe, HidPort port, byte slot, string link, int wirelessId)
    {
        var firmware = probe.Register(slot, 0x81, 0xF1, 0x01);
        var build = firmware == null ? null : probe.Register(slot, 0x81, 0xF1, 0x02);
        string[] known;
        if (!OldWireless.TryGetValue(wirelessId, out known)) known = new[] { link == "Wireless" ? "G700 / G700s" : "", "mouse" };
        return new FoundDevice
        {
            Path = port.LongPath, ShortPath = port.ShortPath, Index = slot, Link = link, Hidpp10 = true, Name = known[0], Kind = known[1],
            Firmware = firmware == null ? ""
                : firmware[1].ToString("X2") + "." + firmware[2].ToString("X2")
                    + (build != null ? ", build " + build[1].ToString("X2") + build[2].ToString("X2") : ""),
            Models = wirelessId == 0 ? "" : link + " " + wirelessId.ToString("X4")
        };
    }

    static FoundDevice ProbeHidpp10(ScanProbe probe, HidPort port, byte[] firmware)
    {
        var build = probe.Register(0x00, 0x81, 0xF1, 0x02);
        var name = Product(port.LongPath);
        var lower = name.ToLower();
        return new FoundDevice
        {
            Path = port.LongPath, ShortPath = port.ShortPath, Index = 0, Key = port.LongPath + "|0", Link = "USB", Hidpp10 = true,
            Name = name,
            Kind = lower.Contains("mouse") ? "mouse"
                : lower.Contains("keyboard") ? "keyboard"
                : lower.Contains("headset") ? "headset"
                : "device",
            Firmware = firmware[1].ToString("X2") + "." + firmware[2].ToString("X2")
                + (build != null ? ", build " + build[1].ToString("X2") + build[2].ToString("X2") : ""),
            Models = Regex.IsMatch(port.LongPath, @"PID_[0-9A-F]{4}", RegexOptions.IgnoreCase)
                ? "USB " + Regex.Match(port.LongPath, @"PID_([0-9A-F]{4})", RegexOptions.IgnoreCase).Groups[1].Value.ToUpper()
                : ""
        };
    }

    static string Product(string path)
    {
        using (var handle = Open(path))
        {
            var text = new System.Text.StringBuilder(128);
            return !handle.IsInvalid && HidD_GetProductString(handle, text, 256) ? text.ToString().Trim() : "";
        }
    }

    static FoundDevice Describe(ScanProbe probe, HidPort port, byte index, string link)
    {
        var device = new FoundDevice { Path = port.LongPath, Index = index, Link = link, Key = port.LongPath + "|" + index };
        byte featureSet = probe.Feature(index, 0x0001);
        var list = new List<int>();
        var count = featureSet == 0 ? null : probe.Call(index, featureSet, 0);
        if (count != null)
            for (int i = 1; i <= count[0]; i++)
            {
                var id = probe.Call(index, featureSet, 1, (byte)i);
                if (id != null) list.Add(id[0] << 8 | id[1]);
            }
        device.Features = list.ToArray();
        byte nameFeature = probe.Feature(index, 0x0005);
        if (nameFeature != 0)
        {
            var nameLength = probe.Call(index, nameFeature, 0);
            var name = "";
            for (int offset = 0; nameLength != null && offset < nameLength[0]; offset += 16)
            {
                var part = probe.Call(index, nameFeature, 1, (byte)offset);
                if (part == null) break;
                name += new string(part.Take(16).TakeWhile(value => value != 0).Select(value => (char)value).ToArray());
            }
            device.Name = (nameLength == null ? name : name.Substring(0, Math.Min(name.Length, nameLength[0]))).Trim();
            var type = probe.Call(index, nameFeature, 2);
            if (type != null && type[0] < Kinds.Length) device.Kind = Kinds[type[0]];
        }
        if (device.Name == "" && index == 0xFF)
            device.Name = Regex.Replace(Product(port.LongPath), @"^Logitech\s+", "", RegexOptions.IgnoreCase);
        byte firmwareFeature = probe.Feature(index, 0x0003);
        var entities = firmwareFeature == 0 ? null : probe.Call(index, firmwareFeature, 0);
        device.Models = FoundDevice.ModelText(entities);
        // The running firmware is the main application (type 0) marked active, byte 8 bit 0: "exactly one entity will return 1"
        // (spec x0003 v4).
        byte[] info = null;
        for (int entity = 0; entities != null && entity < entities[0] && (info == null || (info[8] & 1) == 0); entity++)
        {
            var reply = probe.Call(index, firmwareFeature, 1, (byte)entity);
            if (reply != null && reply[0] == 0 && (info == null || (reply[8] & 1) != 0)) info = reply;
        }
        if (info != null)
            device.Firmware = FoundDevice.FirmwareText(info);
        Battery(probe, device);
        return device;
    }

    static readonly int[] BatteryFeatures = { 0x1004, 0x1000, 0x1001, 0x1F20 };

    public static FoundDevice ReadBattery(FoundDevice device)
    {
        if (device.BatteryFeature == 0) return null;
        var copy = new FoundDevice
        {
            Name = device.Name, Index = device.Index, BatteryFeature = device.BatteryFeature, BatteryId = device.BatteryId
        };
        byte[] reply;
        try
        {
            using (var probe = new ScanProbe(device.Path, device.ShortPath))
                reply = probe.Call(device.Index, device.BatteryFeature, device.BatteryId == 0x1004 ? 1 : 0);
        }
        catch (Exception) { return null; }
        if (reply == null) return null;
        Decode(copy, reply);
        if (copy.BatteryFromVoltage) Remember(copy, DateTime.UtcNow.Ticks / TimeSpan.TicksPerMinute);
        return copy;
    }

    static void Battery(ScanProbe probe, FoundDevice device)
    {
        foreach (var id in BatteryFeatures)
        {
            var featureIndex = probe.Feature(device.Index, id);
            if (featureIndex == 0) continue;
            var reply = probe.Call(device.Index, featureIndex, id == 0x1004 ? 1 : 0);
            if (reply == null) continue;
            device.BatteryFeature = featureIndex;
            device.BatteryId = id;
            Decode(device, reply);
            return;
        }
    }

    public static void Decode(FoundDevice device, byte[] reply)
    {
        if (device.BatteryId == 0x1004) { device.Battery = reply[0]; device.Charging = reply[2] == 1 || reply[2] == 2; return; }
        if (device.BatteryId == 0x1000)
        {
            device.Battery = reply[0];
            device.Charging = reply[2] == 1 || reply[2] == 2 || reply[2] == 4;
            return;
        }
        device.Millivolts = reply[0] << 8 | reply[1];
        device.BatteryFromVoltage = true;
        device.Charging = device.BatteryId == 0x1001 ? (reply[2] & 0x80) != 0 : reply[2] == 3;
        device.Battery = !device.Charging ? FoundDevice.FromVolts(device.Millivolts)
            : device.BatteryId == 0x1001 && (reply[2] & 0x07) == 1 ? 100
            : FoundDevice.FromChargingVolts(device.Millivolts);
    }
}

// Windows' HIDP_CAPS read as 16-bit values, and its HIDP_BUTTON_CAPS and HIDP_VALUE_CAPS records as bytes (hidpi.h).
static class HidCaps
{
    public const int Length = 32, Usage = 0, UsagePage = 1, InputLength = 2, OutputLength = 3, FeatureLength = 4;

    // For each report type (input, output, feature: 0, 1, 2), its count of button caps, then of value caps, 3 values apart.
    public const int ButtonCaps = 23, ValueCaps = 24, PerType = 3, FeatureType = 2;

    public const int RecordLength = 72, UsagePageAt = 0, ReportIdAt = 2, IsRangeAt = 12, HasNullAt = 16, BitSizeAt = 18,
        ReportCountAt = 20, LogicalMinAt = 40, LogicalMaxAt = 44, UsageAt = 56, UsageMaxAt = 58;
}

class DeviceWatcher : IDisposable
{
    readonly List<FileStream> streams = new List<FileStream>();
    volatile bool done;

    public DeviceWatcher(IEnumerable<string> paths, Action<string, byte[]> heard)
    {
        foreach (var path in paths)
        {
            Listen(path, path, 20, heard);
            string shortPath;
            if (Devices.Shorts.TryGetValue(path, out shortPath)) Listen(shortPath, path, 7, heard);
        }
    }

    void Listen(string path, string key, int size, Action<string, byte[]> heard)
    {
        var handle = Devices.Open(path);
        if (handle.IsInvalid) return;
        var stream = new FileStream(handle, FileAccess.ReadWrite, 1, true);
        streams.Add(stream);
        Task.Run(() =>
        {
            var buffer = new byte[size];
            while (!done)
            {
                int read;
                try { read = stream.Read(buffer, 0, size); }
                catch (Exception ex)
                {
                    if (!done) Log.Add("G-Lite", "error", "stopped listening to a device's events: " + ex.Message);
                    return;
                }
                try { if (read >= 7) heard(key, (byte[])buffer.Clone()); }
                catch (Exception ex) { Log.Add("G-Lite", "error", "a device event failed: " + ex.GetType().Name + ", " + ex.Message); }
            }
        });
    }

    public void Dispose() { done = true; foreach (var stream in streams) stream.Dispose(); }
}

class ScanProbe : IDisposable
{
    const byte SoftwareId = 0x0D;
    readonly FileStream longStream, shortStream;
    readonly byte[] longBuffer = new byte[20], shortBuffer = new byte[7];
    Task<int> longRead, shortRead;

    public ScanProbe(string longPath, string shortPath)
    {
        var longHandle = Devices.Open(longPath);
        if (longHandle.IsInvalid) throw new IOException("can't open " + longPath);
        longStream = new FileStream(longHandle, FileAccess.ReadWrite, 1, true);
        if (shortPath == null) return;
        var shortHandle = Devices.Open(shortPath);
        if (!shortHandle.IsInvalid) shortStream = new FileStream(shortHandle, FileAccess.ReadWrite, 1, true);
    }

    public void Dispose() { longStream.Dispose(); if (shortStream != null) shortStream.Dispose(); }

    byte[] Next(DateTime end)
    {
        if (longRead == null) longRead = longStream.ReadAsync(longBuffer, 0, 20);
        if (shortStream != null && shortRead == null) shortRead = shortStream.ReadAsync(shortBuffer, 0, 7);
        int milliseconds = (int)(end - DateTime.UtcNow).TotalMilliseconds;
        if (milliseconds <= 0) return null;
        int which = shortStream == null ? (longRead.Wait(milliseconds) ? 0 : -1)
            : Task.WaitAny(new Task[] { longRead, shortRead }, milliseconds);
        if (which < 0) return null;
        if (which == 0) { longRead = null; return (byte[])longBuffer.Clone(); }
        shortRead = null;
        var message = new byte[20];
        Array.Copy(shortBuffer, message, 7);
        return message;
    }

    // 1 for a HID++ 1.0 device: it answers with error 01, invalid sub-ID (Solaar's base.ping). 0 for no answer.
    public int Ping(byte index, int timeout)
    {
        lastError = -1;
        var reply = Send(index, 0, 1, timeout, 0, 0, 0x5A);
        return reply != null ? reply[0] : lastError == 0x01 ? 1 : 0;
    }

    int lastError;

    public byte Feature(byte index, int id)
    {
        var reply = Call(index, 0, 0, (byte)(id >> 8), (byte)id);
        return reply == null ? (byte)0 : reply[0];
    }

    public byte[] Call(byte index, byte feature, int fn, params byte[] parameters) { return Send(index, feature, fn, 3000, parameters); }

    byte[] Send(byte index, byte feature, int fn, int timeout, params byte[] parameters)
    {
        var message = new byte[20];
        message[0] = 0x11; message[1] = index; message[2] = feature; message[3] = (byte)(fn << 4 | SoftwareId);
        Array.Copy(parameters, 0, message, 4, Math.Min(parameters.Length, 16));
        longStream.Write(message, 0, 20);
        var end = DateTime.UtcNow.AddMilliseconds(timeout);
        while (true)
        {
            var reply = Next(end);
            if (reply == null) return null;
            if (reply[1] != index) continue;
            if (reply[2] == 0x8F && reply[3] == feature) { lastError = reply[5]; return null; }
            if (reply[2] == 0xFF && reply[3] == feature && reply[4] == message[3]) return null;
            if (reply[2] == feature && reply[3] == message[3]) return reply.Skip(4).ToArray();
        }
    }

    public byte[] Register(byte index, byte subId, byte register, byte parameter)
    {
        if (shortStream == null) return null;
        var message = new byte[] { 0x10, index, subId, register, parameter, 0, 0 };
        shortStream.Write(message, 0, 7);
        var end = DateTime.UtcNow.AddMilliseconds(1000);
        while (true)
        {
            var reply = Next(end);
            if (reply == null) return null;
            if (reply[1] != index) continue;
            if (reply[2] == 0x8F && reply[3] == subId) return null;
            if (reply[2] == subId && reply[3] == register) return reply.Skip(4).ToArray();
        }
    }
}
