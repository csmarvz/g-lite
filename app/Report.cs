using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

static class Report
{
    public static readonly Dictionary<int, int[]> Reads = new Dictionary<int, int[]>
    {
        { 0x0001, new[] { 0, 1 } },
        { 0x0003, new[] { 0, 1 } },
        { 0x1990, new[] { 0, 2, 3, 7, 8 } },
        { 0x1814, new[] { 0 } },
        { 0x1815, new[] { 0, 1 } },
        { 0x1982, new[] { 0, 2 } },
        { 0x1B04, new[] { 0, 1, 2 } },
        { 0x1F20, new[] { 0, 1 } },
        { 0x2110, new[] { 0 } },
        { 0x2111, new[] { 0, 1 } },
        { 0x2121, new[] { 0, 1, 3 } },
        { 0x2150, new[] { 0, 1 } },
        { 0x2201, new[] { 0, 1, 2 } },
        { 0x2202, new[] { 0, 1, 2, 3, 4, 5 } },
        { 0x40A3, new[] { 0 } },
        { 0x4521, new[] { 0, 1 } },
        { 0x4540, new[] { 0 } },
        { 0x8010, new[] { 0 } },
        { 0x8020, new[] { 0 } },
        { 0x8040, new[] { 0, 1 } },
        { 0x8060, new[] { 0, 1 } },
        { 0x8061, new[] { 0, 1, 2 } },
        { 0x8070, new[] { 0, 1, 2 } },
        { 0x8071, new[] { 0 } },
        { 0x8081, new[] { 0 } },
        { 0x8100, new[] { 0, 2, 4, 5, 0xB } },
        { 0x8110, new[] { 0 } },
        { 0x8101, new[] { 8, 0xC } },
        { 0x8300, new[] { 0 } },
        { 0x8310, new[] { 0, 1, 2 } },
    };

    // Functions that read when their first byte is 0 and write when it's 1: 0x8071 fn 5 (software control), fn 7 (power-mode
    // settings) and fn 8 (power mode), per docs/lighting-control.md. The report calls them with 0 only.
    public static readonly Dictionary<int, int[]> GetOrSet = new Dictionary<int, int[]> { { 0x8071, new[] { 5, 7, 8 } } };

    public static bool IsRead(int feature, int fn, byte[] parameters)
    {
        int[] functions;
        if (Reads.TryGetValue(feature, out functions) && functions.Contains(fn)) return true;
        return GetOrSet.TryGetValue(feature, out functions) && functions.Contains(fn) && parameters.Length > 0 && parameters[0] == 0;
    }

    static readonly Dictionary<int, string> Names = new Dictionary<int, string>
    {
        { 0x0001, "feature set" }, { 0x0003, "firmware" }, { 0x0005, "name" }, { 0x0007, "friendly name" },
        { 0x0020, "config change" }, { 0x1000, "battery" }, { 0x1001, "battery voltage" }, { 0x1004, "battery" },
        { 0x1300, "LED control" }, { 0x1814, "change host" }, { 0x1815, "hosts info" }, { 0x1982, "backlight" },
        { 0x1990, "illumination" }, { 0x1B04, "controls" }, { 0x1D4B, "wireless status" }, { 0x1F20, "battery and auto-off" },
        { 0x2110, "SmartShift" }, { 0x2111, "SmartShift" }, { 0x2121, "scroll wheel" }, { 0x2150, "thumb wheel" },
        { 0x2201, "DPI" }, { 0x2202, "DPI, X and Y" }, { 0x8061, "report rate, to 8 kHz" }, { 0x4540, "keyboard layout" },
        { 0x40A3, "F1–F12" }, { 0x4521, "disable keys" },
        { 0x8010, "G keys" }, { 0x8020, "M keys" }, { 0x8030, "MR key" }, { 0x8040, "brightness" }, { 0x8060, "report rate" },
        { 0x8070, "LED effects" }, { 0x8071, "RGB effects" }, { 0x8080, "per-key lighting" }, { 0x8081, "per-key lighting" },
        { 0x8100, "onboard profiles" }, { 0x8101, "onboard files" }, { 0x8110, "button spy" }, { 0x8300, "sidetone" },
        { 0x8310, "equalizer" },
    };

    class Reader
    {
        public Hidpp Device;
        public readonly StringBuilder Output = new StringBuilder();
        public string Error;
        int misses;

        public byte[] ReadOnlyCall(int feature, int fn, params byte[] parameters)
        {
            if (!IsRead(feature, fn, parameters))
                throw new RefusedException(string.Format("Not a known read: feature {0:X4} fn {1}", feature, fn));
            Error = null;
            if (misses >= 2) { Error = "no answer"; return null; }
            try
            {
                var reply = Device.CallUnlogged(Device.FeatureIndex(feature), fn, parameters);
                misses = 0;
                return reply;
            }
            catch (HidppError error) { Error = "error " + error.Code.ToString("X2"); }
            catch (NotSupportedException) { Error = "missing"; }
            catch (TimeoutException) { misses++; Error = "no answer"; }
            return null;
        }

        public byte[] Show(string label, int feature, int fn, params byte[] parameters)
        {
            var reply = ReadOnlyCall(feature, fn, parameters);
            Output.AppendLine(label + "=" + (reply == null ? Error : Hex(reply, " ")));
            return reply;
        }
    }

    public static string Build(Hidpp hidpp, FoundDevice found, string screen)
    {
        var reader = new Reader { Device = hidpp };
        var output = reader.Output;
        WriteHeader(output, found, screen, () => found.Battery >= 0 || !found.Has(0x1F20)
            ? (found.Battery < 0 ? "–" : found.Battery + "%")
                + (found.Millivolts > 0 ? ", " + found.Millivolts + " mV" : "")
                + (found.Charging ? ", charging" : "")
            : HeadsetBattery(reader));

        var ids = Features(reader, found);
        foreach (var id in ids.Where(Reads.ContainsKey).Where(feature => feature != 0x0001))
        {
            output.AppendLine();
            output.AppendLine(id.ToString("X4") + " " + Name(id));
            Section(reader, found, id);
        }
        return output.ToString();
    }

    public static string Registers(Hidpp device, FoundDevice found, string screen)
    {
        var text = new StringBuilder();
        WriteHeader(text, found, screen, () => "–");
        text.AppendLine();
        text.AppendLine("HID++ 1.0 registers");
        int misses = 0;
        for (int register = 0; register < 0x100 && misses < 2; register++)
            foreach (byte subId in new byte[] { 0x81, 0x83 })
            {
                var value = ReadRegister(device, subId, (byte)register, ref misses);
                if (value != null)
                    text.AppendLine((subId == 0x81 ? "short " : "long ") + register.ToString("X2")
                        + "=" + Hex(value.Take(subId == 0x81 ? 3 : 16).ToArray(), " "));
            }
        var current = ReadRegister(device, 0x81, 0x0F, ref misses);
        var directory = misses < 2 ? ReadPage(device, 1) : null;
        if (directory == null) return text.ToString();
        var pageNumbers = new List<int> { 1 };
        for (int entry = 0; entry + 2 < 510 && directory[entry] != 0xFF && pageNumbers.Count < 17; entry += 3)
            if (directory[entry] >= 2 && directory[entry] < 32) pageNumbers.Add(directory[entry]);
        int currentPage = current != null && current[0] == 0 && current[1] < 16 ? G9xRegisters.Entry(directory, current[1]) : -1;
        foreach (var pageNumber in pageNumbers)
        {
            var page = pageNumber == 1 ? directory : ReadPage(device, pageNumber);
            if (page == null) break;
            text.AppendLine();
            text.AppendLine("page " + pageNumber.ToString("X2") + (G9xRegisters.Valid(page) ? "" : ", bad checksum")
                + (pageNumber == currentPage ? ", current" : ""));
            for (int offset = 0; offset < 512; offset += 16)
            {
                var row = page.Skip(offset).Take(16).ToArray();
                if (offset > 0 && offset < 496 && (row.All(value => value == 0xFF) || row.All(value => value == 0))) continue;
                text.AppendLine(offset.ToString("X3") + "=" + Hex(row, " "));
            }
        }
        return text.ToString();
    }

    [DllImport("hid.dll")] static extern bool HidD_GetPreparsedData(SafeFileHandle handle, out IntPtr data);
    [DllImport("hid.dll")] static extern bool HidD_FreePreparsedData(IntPtr data);
    [DllImport("hid.dll")] static extern int HidP_GetCaps(IntPtr data, short[] caps);
    [DllImport("hid.dll")] static extern int HidP_GetButtonCaps(int type, byte[] caps, ref short length, IntPtr data);
    [DllImport("hid.dll")] static extern int HidP_GetValueCaps(int type, byte[] caps, ref short length, IntPtr data);
    [DllImport("hid.dll", SetLastError = true)] static extern bool HidD_GetFeature(SafeFileHandle handle, byte[] buffer, int length);
    [DllImport("hid.dll", SetLastError = true)] static extern bool HidD_GetInputReport(SafeFileHandle handle, byte[] buffer, int length);

    public static string PlainHidReport(FoundDevice found, string screen)
    {
        var text = new StringBuilder();
        WriteHeader(text, found, screen, () => "–");
        var pid = Regex.Match(found.Path ?? "", @"PID_([0-9A-F]{4})", RegexOptions.IgnoreCase).Groups[1].Value;
        foreach (var id in Devices.PlainIds(pid))
        {
            var name = Regex.Replace(id.Split('\\')[1], @"^VID_[0-9A-F]{4}&PID_[0-9A-F]{4}&?", "");
            text.AppendLine();
            text.AppendLine("collection " + (name == "" ? "main" : name));
            Collection(text, Devices.PathOf(id));
        }
        return text.ToString();
    }

    static void Collection(StringBuilder text, string path)
    {
        bool shared = false;
        var handle = Devices.OpenSync(path, 0xC0000000);
        if (handle.IsInvalid) { handle.Dispose(); handle = Devices.OpenSync(path, 0); shared = true; }
        using (handle)
        {
            IntPtr data;
            if (handle.IsInvalid || !HidD_GetPreparsedData(handle, out data)) { text.AppendLine("can't open"); return; }
            try
            {
                var caps = new short[HidCaps.Length];
                HidP_GetCaps(data, caps);
                text.AppendLine(string.Format("usage={0:X4}:{1:X4} input={2} output={3} feature={4}{5}",
                    (ushort)caps[HidCaps.UsagePage], (ushort)caps[HidCaps.Usage],
                    caps[HidCaps.InputLength], caps[HidCaps.OutputLength], caps[HidCaps.FeatureLength], shared ? ", held by Windows" : ""));
                var kinds = new[] { "input", "output", "feature" };
                int inputId = -1;
                for (int type = 0; type < 3; type++)
                {
                    int first = Fields(text, data, type, kinds[type], caps[HidCaps.ButtonCaps + type * HidCaps.PerType], true);
                    int value = Fields(text, data, type, kinds[type], caps[HidCaps.ValueCaps + type * HidCaps.PerType], false);
                    if (type == 0) inputId = first >= 0 ? first : value;
                }
                if (!shared && caps[HidCaps.InputLength] > 0 && inputId >= 0 && (ushort)caps[HidCaps.UsagePage] < 0xFF00)
                {
                    var input = new byte[caps[HidCaps.InputLength]];
                    input[0] = (byte)inputId;
                    text.AppendLine("input now="
                        + (HidD_GetInputReport(handle, input, input.Length) ? Hex(input, " ") : "error " + Marshal.GetLastWin32Error()));
                }
                var ids = new SortedSet<int>();
                int featureAt = HidCaps.FeatureType * HidCaps.PerType;
                FeatureIds(ids, data, caps[HidCaps.ButtonCaps + featureAt], true);
                FeatureIds(ids, data, caps[HidCaps.ValueCaps + featureAt], false);
                if (caps[HidCaps.FeatureLength] <= 0 || ids.Count == 0) return;
                text.AppendLine("feature ids=" + string.Join(" ", ids.Select(id => id.ToString("X2"))));
                foreach (var reportId in ids)
                {
                    var buffer = new byte[caps[HidCaps.FeatureLength]];
                    buffer[0] = (byte)reportId;
                    bool read = HidD_GetFeature(handle, buffer, buffer.Length);
                    text.AppendLine("feature " + reportId.ToString("X2")
                        + "=" + (read ? Hex(buffer, " ") : "error " + Marshal.GetLastWin32Error()));
                }
            }
            finally { HidD_FreePreparsedData(data); }
        }
    }

    static int Fields(StringBuilder text, IntPtr data, int type, string kind, short count, bool buttons)
    {
        if (count <= 0) return -1;
        var caps = new byte[HidCaps.RecordLength * count];
        short length = count;
        int status = buttons ? HidP_GetButtonCaps(type, caps, ref length, data) : HidP_GetValueCaps(type, caps, ref length, data);
        if (status != 0x00110000) return -1;
        for (int i = 0; i < length; i++)
        {
            int at = HidCaps.RecordLength * i;
            Func<int, int> readUInt16 = offset => caps[at + offset] | caps[at + offset + 1] << 8;
            Func<int, int> readInt32 = offset => BitConverter.ToInt32(caps, at + offset);
            bool range = caps[at + HidCaps.IsRangeAt] != 0;
            string usage = readUInt16(HidCaps.UsageAt).ToString("X4") + (range ? "-" + readUInt16(HidCaps.UsageMaxAt).ToString("X4") : "");
            string line = kind + (buttons ? " buttons" : " value") + " id=" + caps[at + HidCaps.ReportIdAt].ToString("X2")
                + " page=" + readUInt16(HidCaps.UsagePageAt).ToString("X4") + " usage=" + usage;
            if (!buttons)
                line += " bits=" + readUInt16(HidCaps.BitSizeAt) + " count=" + readUInt16(HidCaps.ReportCountAt)
                    + " logical=" + readInt32(HidCaps.LogicalMinAt) + ".." + readInt32(HidCaps.LogicalMaxAt)
                    + (caps[at + HidCaps.HasNullAt] != 0 ? " null" : "");
            text.AppendLine(line);
        }
        return length > 0 ? caps[HidCaps.ReportIdAt] : -1;
    }

    static void FeatureIds(SortedSet<int> ids, IntPtr data, short count, bool buttons)
    {
        if (count <= 0) return;
        var caps = new byte[HidCaps.RecordLength * count];
        short length = count;
        int status = buttons
            ? HidP_GetButtonCaps(HidCaps.FeatureType, caps, ref length, data)
            : HidP_GetValueCaps(HidCaps.FeatureType, caps, ref length, data);
        if (status != 0x00110000) return;
        for (int i = 0; i < length; i++) ids.Add(caps[HidCaps.RecordLength * i + HidCaps.ReportIdAt]);
    }

    static byte[] ReadRegister(Hidpp device, byte subId, byte register, ref int misses)
    {
        try { var value = device.Register(subId, register); misses = 0; return value; }
        catch (HidppError) { misses = 0; return null; }
        catch (NotSupportedException) { misses = 2; return null; }
        catch (TimeoutException) { misses++; return null; }
    }

    static byte[] ReadPage(Hidpp device, int pageNumber)
    {
        try { return G9xRegisters.ReadPage(device, pageNumber); }
        catch (HidppError) { return null; }
        catch (TimeoutException) { return null; }
    }

    static void WriteHeader(StringBuilder text, FoundDevice found, string screen, Func<string> battery)
    {
        text.AppendLine("G-Lite device report");
        text.AppendLine("G-Lite " + Version() + ", " + Windows());
        text.AppendLine(DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm 'UTC'", System.Globalization.CultureInfo.InvariantCulture));
        text.AppendLine();
        Row(text, "Device", found.Name == "" ? "–" : found.Name);
        Row(text, "Kind", found.Kind);
        Row(text, "Link", found.Link + found.Slot + (found.Hidpp10 ? ", HID++ 1.0" : "")
            + (found.WirelessId != 0 ? ", ID " + found.WirelessId.ToString("X4") : "") + Usb(found.Path));
        Row(text, "Model", found.Models == "" ? "–" : found.Models);
        Row(text, "Firmware", found.Firmware == "" ? "–" : found.Firmware);
        Row(text, "Battery", battery());
        Row(text, "Screen", screen);
    }

    static List<int> Features(Reader reader, FoundDevice found)
    {
        var output = reader.Output;
        var ids = new List<int>();
        output.AppendLine();
        var count = reader.ReadOnlyCall(0x0001, 0);
        if (count == null)
        {
            output.AppendLine("Features (from the scan)");
            ids.AddRange(found.Features);
            foreach (var id in ids) output.AppendLine(id.ToString("X4") + "  " + Name(id));
            return ids;
        }
        output.AppendLine("Features");
        for (int i = 1; i <= count[0]; i++)
        {
            var entry = reader.ReadOnlyCall(0x0001, 1, (byte)i);
            if (entry == null) { output.AppendLine(i.ToString("X2") + " " + reader.Error); continue; }
            int id = entry[0] << 8 | entry[1];
            ids.Add(id);
            var flags = ((entry[2] & 0x80) != 0 ? " obsolete" : "")
                + ((entry[2] & 0x40) != 0 ? " hidden" : "")
                + ((entry[2] & 0x20) != 0 ? " internal" : "");
            output.AppendLine(string.Format("{0:X2} {1:X4} v{2,-2} {3}", i, id, entry[3], (Name(id) + flags).Trim()).TrimEnd());
        }
        return ids;
    }

    static void Section(Reader reader, FoundDevice found, int id)
    {
        switch (id)
        {
            case 0x0003:
                {
                    var count = reader.ReadOnlyCall(0x0003, 0);
                    for (int entity = 0; count != null && entity < Math.Min((int)count[0], 8); entity++)
                    {
                        var info = reader.ReadOnlyCall(0x0003, 1, (byte)entity);
                        if (info != null) for (int i = 11; i < info.Length; i++) info[i] = 0;
                        reader.Output.AppendLine("entity " + entity + "=" + (info == null ? reader.Error : Hex(info, " ")));
                    }
                    break;
                }
            case 0x1B04:
                {
                    var count = reader.Show("count", 0x1B04, 0);
                    // fn 2 is how each control reports now: diverted (byte 2 bit 0) by an app like Logi Options+, and remapped to
                    // (bytes 3–4). docs/mx-key-bindings.md.
                    for (int i = 0; count != null && i < Math.Min((int)count[0], 48); i++)
                    {
                        var info = reader.Show("control " + i, 0x1B04, 1, (byte)i);
                        if (info != null) reader.Show("reporting " + i, 0x1B04, 2, info[0], info[1]);
                    }
                    break;
                }
            case 0x1990:
                reader.Show("on", id, 0);
                reader.Show("brightness info", id, 2);
                reader.Show("brightness", id, 3);
                reader.Show("temperature info", id, 7);
                reader.Show("temperature", id, 8);
                break;
            case 0x1F20: reader.Show("battery", id, 0); reader.Show("auto-off", id, 1); break;
            case 0x2121: reader.Show("wheel", id, 0); reader.Show("mode", id, 1); reader.Show("ratchet", id, 3); break;
            case 0x2110: reader.Show("smartshift", id, 0); break;
            case 0x2111: reader.Show("capabilities", id, 0); reader.Show("smartshift", id, 1); break;
            case 0x2150: reader.Show("info", id, 0); reader.Show("status", id, 1); break;
            case 0x1982: reader.Show("config", id, 0); reader.Show("info", id, 2); break;
            case 0x1814: reader.Show("hosts", id, 0); break;
            case 0x1815:
                {
                    // fn 1 gives each computer's status, bus and name length. Its name (fn 3) stays out: it's often the owner's name.
                    var info = reader.Show("info", id, 0);
                    for (int host = 0; info != null && host < Math.Min((int)info[2], 6); host++)
                        reader.Show("host " + host, id, 1, (byte)host);
                    break;
                }
            case 0x2201: reader.Show("sensors", id, 0); reader.Show("dpi list", id, 1, 0); reader.Show("dpi", id, 2, 0); break;
            case 0x2202:
                reader.Show("sensors", id, 0);
                reader.Show("sensor", id, 1, 0);
                reader.Show("dpi ranges", id, 2, 0, 0, 0);
                reader.Show("dpi ranges, page 1", id, 2, 0, 0, 1);
                reader.Show("dpi list", id, 3, 0, 0);
                reader.Show("lift-off list", id, 4, 0);
                reader.Show("dpi", id, 5, 0);
                break;
            case 0x8061:
                reader.Show("rates, wired", id, 0, 0);
                reader.Show("rates, wireless", id, 0, 1);
                reader.Show("rates now", id, 1);
                reader.Show("now, wired", id, 2, 0);
                reader.Show("now, wireless", id, 2, 1);
                break;
            case 0x4540: reader.Show("layout", id, 0); break;
            case 0x40A3:
                // Each Easy-Switch computer's: FF, "the one in use", reads 00 on an MX Keys S whatever it has (docs/other-devices.md).
                reader.Show("this computer", id, 0, 0xFF);
                for (int host = 0; host < 3; host++) reader.Show("computer " + (host + 1), id, 0, (byte)host);
                break;
            case 0x4521: reader.Show("can disable", id, 0); reader.Show("disabled", id, 1); break;
            case 0x8010:
            case 0x8020: reader.Show("count", id, 0); break;
            case 0x8040: reader.Show("info", id, 0); reader.Show("now", id, 1); break;
            case 0x8060: reader.Show("rates", id, 0); reader.Show("now", id, 1); break;
            case 0x8070:
                {
                    var info = reader.Show("info", id, 0);
                    for (int zoneIndex = 0; info != null && zoneIndex < Math.Min((int)info[0], 8); zoneIndex++)
                    {
                        var zone = reader.Show("zone " + zoneIndex, id, 1, (byte)zoneIndex, 0xFF, 0);
                        for (int effect = 0; zone != null && effect < Math.Min((int)zone[3], 32); effect++)
                            reader.Show("zone " + zoneIndex + " effect " + effect, id, 2, (byte)zoneIndex, (byte)effect, 0xFF, 0);
                    }
                    break;
                }
            case 0x8071:
                for (int cluster = 0; cluster < 8; cluster++)
                {
                    var where = reader.ReadOnlyCall(id, 0, (byte)cluster, 0xFF, 0);
                    if (where != null) reader.Output.AppendLine("cluster " + cluster + "=" + Hex(where, " "));
                    int effects = 0;
                    for (int effect = 0; effect < 32; effect++)
                    {
                        var info = reader.ReadOnlyCall(id, 0, (byte)cluster, (byte)effect, 0);
                        if (info == null) break;
                        reader.Output.AppendLine("cluster " + cluster + " effect " + effect + "=" + Hex(info, " "));
                        effects++;
                    }
                    if (effects == 0) break;
                }
                reader.Show("control", id, 5, 0, 0, 0);
                reader.Show("power settings", id, 7, 0, 0, 0);
                reader.Show("power mode", id, 8, 0, 0, 0);
                break;
            case 0x8081:
                {
                    var lit = new List<int>();
                    for (int page = 0; page < 3; page++)
                    {
                        var bits = reader.Show("page " + page, id, 0, 0, (byte)page, 0);
                        for (int i = 0; bits != null && i < 14; i++)
                            for (int bit = 0; bit < 8; bit++)
                                if ((bits[2 + i] >> bit & 1) != 0) lit.Add(page * 112 + i * 8 + bit);
                    }
                    reader.Output.AppendLine("lit ids=" + string.Join(" ", lit.Select(litId => litId.ToString("X2"))));
                    break;
                }
            case 0x8100: Profiles(reader); break;
            case 0x8101:
                {
                    var user = File(reader, 0x0000, 16);
                    if (found.Kind == "keyboard") File(reader, 0x0008, ProfileSize(user));
                    var factory = File(reader, 0x0100, 16);
                    if (found.Kind == "keyboard") File(reader, 0x0101, ProfileSize(factory));
                    break;
                }
            case 0x8110: reader.Show("buttons", id, 0); break;
            case 0x8300: reader.Show("sidetone", id, 0); break;
            case 0x8310:
                {
                    var info = reader.Show("info", id, 0);
                    int count = info == null ? 0 : Math.Min((int)info[0], 32);
                    for (int start = 0; start < count; start += 7) reader.Show("frequencies " + start, id, 1, (byte)start);
                    reader.Show("bands", id, 2);
                    break;
                }
        }
    }

    static string HeadsetBattery(Reader reader)
    {
        var reply = reader.ReadOnlyCall(0x1F20, 0);
        if (reply == null) return "–";
        int millivolts = reply[0] << 8 | reply[1];
        return "~" + (reply[2] == 3 ? FoundDevice.FromChargingVolts(millivolts) : FoundDevice.FromVolts(millivolts))
            + "%, " + millivolts + " mV" + (reply[2] == 3 ? ", charging" : "");
    }

    static void Profiles(Reader reader)
    {
        var info = reader.Show("info", 0x8100, 0);
        reader.Show("mode", 0x8100, 2);
        reader.Show("current", 0x8100, 4);
        reader.Show("dpi index", 0x8100, 0xB);
        if (info == null) return;
        int size = info[7] << 8 | info[8];
        if (size < 16 || size > 0x1000) { reader.Output.AppendLine("sector size " + size + ", not read"); return; }
        var directory = Sector(reader, 0x0000, size);
        var sectors = new List<int>();
        for (int i = 0; directory != null && i + 1 < directory.Length && sectors.Count < 8; i += 4)
        {
            int at = directory[i] << 8 | directory[i + 1];
            if (at == 0xFFFF) break;
            if (at > 0 && at < 0x100 && !sectors.Contains(at)) sectors.Add(at);
        }
        foreach (var at in sectors) Sector(reader, at, size);
        Sector(reader, 0x0100, size);
        for (int profile = 1; profile <= Math.Min((int)info[4], 8); profile++) Sector(reader, 0x0100 + profile, size);
        OtherSectors(reader, Math.Min((int)info[6], 32), sectors, size);
    }

    // Every other user sector, for the macros: a button entry 00 <sector> <offset> points to one, and a macro too long for a
    // sector goes on in another that no button points to (60 00 05 00 00 on the G815, docs/G815-G915.md). Unused ones (all FF, or all 00 like
    // the G815's last three) on one line.
    static void OtherSectors(Reader reader, int count, List<int> read, int size)
    {
        var unused = new List<string>();
        var unreadable = new List<string>();
        for (int sector = 1; sector < count; sector++)
        {
            if (read.Contains(sector)) continue;
            string error;
            var data = ReadSector(reader, sector, size, out error);
            if (data == null) unreadable.Add(sector.ToString("X4") + " (" + error + ")");
            else if (data.All(value => value == 0xFF) || data.All(value => value == 0x00)) unused.Add(sector.ToString("X4"));
            else reader.Output.AppendLine(sector.ToString("X4") + "=" + Hex(data, ""));
        }
        if (unused.Count > 0) reader.Output.AppendLine("unused=" + string.Join(" ", unused));
        if (unreadable.Count > 0) reader.Output.AppendLine("unreadable=" + string.Join(" ", unreadable));
    }

    static byte[] Sector(Reader reader, int sector, int size)
    {
        string error;
        var data = ReadSector(reader, sector, size, out error);
        reader.Output.AppendLine(sector.ToString("X4") + "=" + (data == null ? error : Hex(data, "")));
        return data;
    }

    static byte[] ReadSector(Reader reader, int sector, int size, out string error)
    {
        error = null;
        var data = new byte[size];
        for (int offset = 0; offset < size; offset += 16)
        {
            int at = Math.Min(offset, size - 16);
            var chunk = reader.ReadOnlyCall(0x8100, 5, (byte)(sector >> 8), (byte)sector, (byte)(at >> 8), (byte)at);
            if (chunk == null) { error = reader.Error; return null; }
            Array.Copy(chunk, offset - at, data, offset, Math.Min(16, size - offset));
        }
        return data;
    }

    static byte[] File(Reader reader, int address, int size)
    {
        if (reader.ReadOnlyCall(0x8101, 8, (byte)(address >> 8), (byte)address, (byte)(size >> 8), (byte)size) == null)
        {
            reader.Output.AppendLine(address.ToString("X4") + "=" + reader.Error);
            return null;
        }
        var collected = new List<byte>();
        for (int offset = 0; offset < size; offset += 16)
        {
            var chunk = reader.ReadOnlyCall(0x8101, 0xC, (byte)(offset >> 8), (byte)offset);
            if (chunk == null) { reader.Output.AppendLine(address.ToString("X4") + "=" + reader.Error); return null; }
            collected.AddRange(chunk);
        }
        var data = collected.Take(size).ToArray();
        reader.Output.AppendLine(address.ToString("X4") + "=" + Hex(data, ""));
        return data;
    }

    static int ProfileSize(byte[] directory)
    {
        bool entry = directory != null && directory.Length == 16
            && directory[5] == 0x01 && directory[6] == 0x81 && directory[7] == 0x01 && directory[8] == 0x40;
        return entry && directory[9] >= 16 ? directory[9] : 99;
    }

    public static string Name(int id) { string name; return Names.TryGetValue(id, out name) ? name : ""; }

    static void Row(StringBuilder text, string name, string value) { text.AppendLine(name.PadRight(10) + value); }

    static string Hex(byte[] bytes, string gap) { return BitConverter.ToString(bytes).Replace("-", gap); }

    static string Usb(string path)
    {
        var match = Regex.Match(path ?? "", @"VID_([0-9A-F]{4})&PID_([0-9A-F]{4})", RegexOptions.IgnoreCase);
        return match.Success ? ", USB " + match.Groups[1].Value.ToUpper() + ":" + match.Groups[2].Value.ToUpper() : "";
    }

    static string Version()
    {
        var attribute = (AssemblyInformationalVersionAttribute)Attribute.GetCustomAttribute(
            Assembly.GetExecutingAssembly(), typeof(AssemblyInformationalVersionAttribute));
        return attribute == null ? "?" : attribute.InformationalVersion;
    }

    public static string Windows()
    {
        try
        {
            using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
            {
                var build = key == null ? null : key.GetValue("CurrentBuild") as string;
                int buildNumber;
                int.TryParse(build, out buildNumber);
                return (buildNumber >= 22000 ? "Windows 11" : buildNumber >= 10240 ? "Windows 10" : "Windows")
                    + (build != null ? " build " + build : "");
            }
        }
        catch (Exception) { return "Windows"; }
    }
}
