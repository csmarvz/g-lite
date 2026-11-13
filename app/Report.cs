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
        { 0x1B04, new[] { 0, 1 } },
        { 0x1F20, new[] { 0, 1 } },
        { 0x2121, new[] { 0, 1, 3 } },
        { 0x2201, new[] { 0, 1, 2 } },
        { 0x2202, new[] { 0, 1, 2, 3, 4, 5 } },
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

    static readonly Dictionary<int, string> Names = new Dictionary<int, string>
    {
        { 0x0001, "feature set" }, { 0x0003, "firmware" }, { 0x0005, "name" }, { 0x0007, "friendly name" },
        { 0x0020, "config change" }, { 0x1000, "battery" }, { 0x1001, "battery voltage" }, { 0x1004, "battery" },
        { 0x1300, "LED control" }, { 0x1814, "change host" }, { 0x1815, "hosts info" }, { 0x1990, "illumination" }, { 0x1B04, "controls" },
        { 0x1D4B, "wireless status" }, { 0x1F20, "battery and auto-off" }, { 0x2121, "scroll wheel" }, { 0x2201, "DPI" }, { 0x2202, "DPI, X and Y" }, { 0x8061, "report rate, to 8 kHz" }, { 0x4540, "keyboard layout" },
        { 0x8010, "G keys" }, { 0x8020, "M keys" }, { 0x8030, "MR key" }, { 0x8040, "brightness" }, { 0x8060, "report rate" },
        { 0x8070, "LED effects" }, { 0x8071, "RGB effects" }, { 0x8080, "per-key lighting" }, { 0x8081, "per-key lighting" },
        { 0x8100, "onboard profiles" }, { 0x8101, "onboard files" }, { 0x8110, "button spy" }, { 0x8300, "sidetone" }, { 0x8310, "equalizer" },
    };

    class Reader
    {
        public Hidpp H;
        public readonly StringBuilder S = new StringBuilder();
        public string Error;
        int misses;

        public byte[] Ask(int feature, int fn, params byte[] p)
        {
            int[] ok;
            if (!Reads.TryGetValue(feature, out ok) || !ok.Contains(fn)) throw new Refused(string.Format("Not a known read: feature {0:X4} fn {1}", feature, fn));
            Error = null;
            if (misses >= 2) { Error = "no answer"; return null; }
            try
            {
                var r = H.Call(H.F(feature), fn, p);
                misses = 0;
                return r;
            }
            catch (HidppError e) { Error = "error " + e.Code.ToString("X2"); }
            catch (NotSupportedException) { Error = "missing"; }
            catch (TimeoutException) { misses++; Error = "no answer"; }
            return null;
        }

        public byte[] Show(string label, int feature, int fn, params byte[] p)
        {
            var r = Ask(feature, fn, p);
            S.AppendLine(label + "=" + (r == null ? Error : Hex(r, " ")));
            return r;
        }
    }

    public static string Build(Hidpp h, Found f, string screen)
    {
        var r = new Reader { H = h };
        var s = r.S;
        Head(s, f, screen, () => f.Battery >= 0 || !f.Has(0x1F20) ? (f.Battery < 0 ? "–" : f.Battery + "%") + (f.Millivolts > 0 ? " · " + f.Millivolts + " mV" : "") + (f.Charging ? " · charging" : "") : HeadsetBattery(r));

        var ids = Features(r, f);
        foreach (var id in ids.Where(Reads.ContainsKey).Where(x => x != 0x0001))
        {
            s.AppendLine();
            s.AppendLine(id.ToString("X4") + " " + Name(id));
            Section(r, f, id);
        }
        return s.ToString();
    }

    public static string Registers(Hidpp device, Found found, string screen)
    {
        var text = new StringBuilder();
        Head(text, found, screen, () => "–");
        text.AppendLine();
        text.AppendLine("HID++ 1.0 registers");
        int misses = 0;
        for (int register = 0; register < 0x100 && misses < 2; register++)
            foreach (byte subId in new byte[] { 0x81, 0x83 })
            {
                var value = ReadRegister(device, subId, (byte)register, ref misses);
                if (value != null) text.AppendLine((subId == 0x81 ? "short " : "long ") + register.ToString("X2") + "=" + Hex(value.Take(subId == 0x81 ? 3 : 16).ToArray(), " "));
            }
        var current = ReadRegister(device, 0x81, 0x0F, ref misses);
        var directory = misses < 2 ? ReadPage(device, 1) : null;
        if (directory == null) return text.ToString();
        var pageNumbers = new List<int> { 1 };
        for (int entry = 0; entry + 2 < 510 && directory[entry] != 0xFF && pageNumbers.Count < 17; entry += 3)
            if (directory[entry] >= 2 && directory[entry] < 32) pageNumbers.Add(directory[entry]);
        int currentPage = current != null && current[0] == 0 && current[1] < 16 ? directory[current[1] * 3] : -1;
        foreach (var pageNumber in pageNumbers)
        {
            var page = pageNumber == 1 ? directory : ReadPage(device, pageNumber);
            if (page == null) break;
            text.AppendLine();
            text.AppendLine("page " + pageNumber.ToString("X2") + (G9x.Valid(page) ? "" : " · bad checksum") + (pageNumber == currentPage ? " · current" : ""));
            for (int offset = 0; offset < 512; offset += 16)
            {
                var row = page.Skip(offset).Take(16).ToArray();
                if (offset > 0 && offset < 496 && (row.All(b => b == 0xFF) || row.All(b => b == 0))) continue;
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

    public static string Plain(Found found, string screen)
    {
        var text = new StringBuilder();
        Head(text, found, screen, () => "–");
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
                var caps = new short[32];
                HidP_GetCaps(data, caps);
                text.AppendLine(string.Format("usage={0:X4}:{1:X4} input={2} output={3} feature={4}{5}", (ushort)caps[1], (ushort)caps[0], caps[2], caps[3], caps[4], shared ? " · held by Windows" : ""));
                var kinds = new[] { "input", "output", "feature" };
                int inputId = -1;
                for (int type = 0; type < 3; type++)
                {
                    int first = Fields(text, data, type, kinds[type], caps[23 + type * 3], true);
                    int value = Fields(text, data, type, kinds[type], caps[24 + type * 3], false);
                    if (type == 0) inputId = first >= 0 ? first : value;
                }
                if (!shared && caps[2] > 0 && inputId >= 0 && (ushort)caps[1] < 0xFF00)
                {
                    var input = new byte[caps[2]];
                    input[0] = (byte)inputId;
                    text.AppendLine("input now=" + (HidD_GetInputReport(handle, input, input.Length) ? Hex(input, " ") : "error " + Marshal.GetLastWin32Error()));
                }
                var ids = new SortedSet<int>();
                FeatureIds(ids, data, caps[29], true);
                FeatureIds(ids, data, caps[30], false);
                if (caps[4] <= 0 || ids.Count == 0) return;
                text.AppendLine("feature ids=" + string.Join(" ", ids.Select(n => n.ToString("X2"))));
                foreach (var reportId in ids)
                {
                    var buffer = new byte[caps[4]];
                    buffer[0] = (byte)reportId;
                    bool read = HidD_GetFeature(handle, buffer, buffer.Length);
                    text.AppendLine("feature " + reportId.ToString("X2") + "=" + (read ? Hex(buffer, " ") : "error " + Marshal.GetLastWin32Error()));
                }
            }
            finally { HidD_FreePreparsedData(data); }
        }
    }

    static int Fields(StringBuilder text, IntPtr data, int type, string kind, short count, bool buttons)
    {
        if (count <= 0) return -1;
        var caps = new byte[72 * count];
        short length = count;
        int status = buttons ? HidP_GetButtonCaps(type, caps, ref length, data) : HidP_GetValueCaps(type, caps, ref length, data);
        if (status != 0x00110000) return -1;
        for (int i = 0; i < length; i++)
        {
            int at = 72 * i;
            Func<int, int> u16 = o => caps[at + o] | caps[at + o + 1] << 8;
            Func<int, int> s32 = o => BitConverter.ToInt32(caps, at + o);
            bool range = caps[at + 12] != 0;
            string usage = u16(56).ToString("X4") + (range ? "-" + u16(58).ToString("X4") : "");
            string line = kind + (buttons ? " buttons" : " value") + " id=" + caps[at + 2].ToString("X2") + " page=" + u16(0).ToString("X4") + " usage=" + usage;
            if (!buttons) line += " bits=" + u16(18) + " count=" + u16(20) + " logical=" + s32(40) + ".." + s32(44) + (caps[at + 16] != 0 ? " null" : "");
            text.AppendLine(line);
        }
        return length > 0 ? caps[2] : -1;
    }

    static void FeatureIds(SortedSet<int> ids, IntPtr data, short count, bool buttons)
    {
        if (count <= 0) return;
        var caps = new byte[72 * count];
        short length = count;
        int status = buttons ? HidP_GetButtonCaps(2, caps, ref length, data) : HidP_GetValueCaps(2, caps, ref length, data);
        if (status != 0x00110000) return;
        for (int i = 0; i < length; i++) ids.Add(caps[72 * i + 2]);
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
        try { return G9x.ReadPage(device, pageNumber); }
        catch (HidppError) { return null; }
        catch (TimeoutException) { return null; }
    }

    static void Head(StringBuilder s, Found f, string screen, Func<string> battery)
    {
        s.AppendLine("G-Lite device report");
        s.AppendLine("G-Lite " + Version() + " · " + Windows());
        s.AppendLine(DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm 'UTC'", System.Globalization.CultureInfo.InvariantCulture));
        s.AppendLine();
        Row(s, "Device", f.Name == "" ? "–" : f.Name);
        Row(s, "Kind", f.Kind);
        Row(s, "Link", f.Link + f.Slot + (f.Hidpp10 ? " · HID++ 1.0" : "") + (f.Wpid != 0 ? " · ID " + f.Wpid.ToString("X4") : "") + Usb(f.Path));
        Row(s, "Model", f.Models == "" ? "–" : f.Models);
        Row(s, "Firmware", f.Firmware == "" ? "–" : f.Firmware);
        Row(s, "Battery", battery());
        Row(s, "Screen", screen);
    }

    static List<int> Features(Reader r, Found f)
    {
        var s = r.S;
        var ids = new List<int>();
        s.AppendLine();
        var count = r.Ask(0x0001, 0);
        if (count == null)
        {
            s.AppendLine("Features (from the scan)");
            ids.AddRange(f.Features);
            foreach (var id in ids) s.AppendLine(id.ToString("X4") + "  " + Name(id));
            return ids;
        }
        s.AppendLine("Features");
        for (int i = 1; i <= count[0]; i++)
        {
            var x = r.Ask(0x0001, 1, (byte)i);
            if (x == null) { s.AppendLine(i.ToString("X2") + " " + r.Error); continue; }
            int id = x[0] << 8 | x[1];
            ids.Add(id);
            var flags = ((x[2] & 0x80) != 0 ? " obsolete" : "") + ((x[2] & 0x40) != 0 ? " hidden" : "") + ((x[2] & 0x20) != 0 ? " internal" : "");
            s.AppendLine(string.Format("{0:X2} {1:X4} v{2,-2} {3}", i, id, x[3], (Name(id) + flags).Trim()).TrimEnd());
        }
        return ids;
    }

    static void Section(Reader r, Found f, int id)
    {
        switch (id)
        {
            case 0x0003:
                {
                    var c = r.Ask(0x0003, 0);
                    for (int e = 0; c != null && e < Math.Min((int)c[0], 8); e++)
                    {
                        var x = r.Ask(0x0003, 1, (byte)e);
                        if (x != null) for (int i = 11; i < x.Length; i++) x[i] = 0;
                        r.S.AppendLine("entity " + e + "=" + (x == null ? r.Error : Hex(x, " ")));
                    }
                    break;
                }
            case 0x1B04:
                {
                    var c = r.Show("count", 0x1B04, 0);
                    for (int i = 0; c != null && i < Math.Min((int)c[0], 48); i++) r.Show("control " + i, 0x1B04, 1, (byte)i);
                    break;
                }
            case 0x1990: r.Show("on", id, 0); r.Show("brightness info", id, 2); r.Show("brightness", id, 3); r.Show("temperature info", id, 7); r.Show("temperature", id, 8); break;
            case 0x1F20: r.Show("battery", id, 0); r.Show("auto-off", id, 1); break;
            case 0x2121: r.Show("wheel", id, 0); r.Show("mode", id, 1); r.Show("ratchet", id, 3); break;
            case 0x2201: r.Show("sensors", id, 0); r.Show("dpi list", id, 1, 0); r.Show("dpi", id, 2, 0); break;
            case 0x2202: r.Show("sensors", id, 0); r.Show("sensor", id, 1, 0); r.Show("dpi ranges", id, 2, 0, 0, 0); r.Show("dpi ranges, page 1", id, 2, 0, 0, 1); r.Show("dpi list", id, 3, 0, 0); r.Show("lift-off list", id, 4, 0); r.Show("dpi", id, 5, 0); break;
            case 0x8061: r.Show("rates, wired", id, 0, 0); r.Show("rates, wireless", id, 0, 1); r.Show("rates now", id, 1); r.Show("now, wired", id, 2, 0); r.Show("now, wireless", id, 2, 1); break;
            case 0x4540: r.Show("layout", id, 0); break;
            case 0x8010:
            case 0x8020: r.Show("count", id, 0); break;
            case 0x8040: r.Show("info", id, 0); r.Show("now", id, 1); break;
            case 0x8060: r.Show("rates", id, 0); r.Show("now", id, 1); break;
            case 0x8070:
                {
                    var info = r.Show("info", id, 0);
                    for (int z = 0; info != null && z < Math.Min((int)info[0], 8); z++)
                    {
                        var zone = r.Show("zone " + z, id, 1, (byte)z, 0xFF, 0);
                        for (int e = 0; zone != null && e < Math.Min((int)zone[3], 32); e++) r.Show("zone " + z + " effect " + e, id, 2, (byte)z, (byte)e, 0xFF, 0);
                    }
                    break;
                }
            case 0x8071:
                for (int c = 0; c < 8; c++)
                {
                    var where = r.Ask(id, 0, (byte)c, 0xFF, 0);
                    if (where != null) r.S.AppendLine("cluster " + c + "=" + Hex(where, " "));
                    int n = 0;
                    for (int e = 0; e < 32; e++)
                    {
                        var x = r.Ask(id, 0, (byte)c, (byte)e, 0);
                        if (x == null) break;
                        r.S.AppendLine("cluster " + c + " effect " + e + "=" + Hex(x, " "));
                        n++;
                    }
                    if (n == 0) break;
                }
                break;
            case 0x8081:
                {
                    var lit = new List<int>();
                    for (int page = 0; page < 3; page++)
                    {
                        var x = r.Show("page " + page, id, 0, 0, (byte)page, 0);
                        for (int i = 0; x != null && i < 14; i++)
                            for (int bit = 0; bit < 8; bit++)
                                if ((x[2 + i] >> bit & 1) != 0) lit.Add(page * 112 + i * 8 + bit);
                    }
                    r.S.AppendLine("lit ids=" + string.Join(" ", lit.Select(n => n.ToString("X2"))));
                    break;
                }
            case 0x8100: Profiles(r); break;
            case 0x8101:
                {
                    var user = File(r, 0x0000, 16);
                    if (f.Kind == "keyboard") File(r, 0x0008, ProfileSize(user));
                    var factory = File(r, 0x0100, 16);
                    if (f.Kind == "keyboard") File(r, 0x0101, ProfileSize(factory));
                    break;
                }
            case 0x8110: r.Show("buttons", id, 0); break;
            case 0x8300: r.Show("sidetone", id, 0); break;
            case 0x8310:
                {
                    var info = r.Show("info", id, 0);
                    int count = info == null ? 0 : Math.Min((int)info[0], 32);
                    for (int start = 0; start < count; start += 7) r.Show("frequencies " + start, id, 1, (byte)start);
                    r.Show("bands", id, 2);
                    break;
                }
        }
    }

    static string HeadsetBattery(Reader r)
    {
        var b = r.Ask(0x1F20, 0);
        if (b == null) return "–";
        int mv = b[0] << 8 | b[1];
        return "~" + Found.FromVolts(mv) + "% · " + mv + " mV" + (b[2] == 3 ? " · charging" : "");
    }

    static void Profiles(Reader r)
    {
        var desc = r.Show("info", 0x8100, 0);
        r.Show("mode", 0x8100, 2);
        r.Show("current", 0x8100, 4);
        r.Show("dpi index", 0x8100, 0xB);
        if (desc == null) return;
        int size = desc[7] << 8 | desc[8];
        if (size < 16 || size > 0x1000) { r.S.AppendLine("sector size " + size + ", not read"); return; }
        var dir = Sector(r, 0x0000, size);
        var sectors = new List<int>();
        for (int i = 0; dir != null && i + 1 < dir.Length && sectors.Count < 8; i += 4)
        {
            int at = dir[i] << 8 | dir[i + 1];
            if (at == 0xFFFF) break;
            if (at > 0 && at < 0x100 && !sectors.Contains(at)) sectors.Add(at);
        }
        foreach (var at in sectors) Sector(r, at, size);
        Sector(r, 0x0100, size);
        for (int k = 1; k <= Math.Min((int)desc[4], 8); k++) Sector(r, 0x0100 + k, size);
    }

    static byte[] Sector(Reader r, int sector, int size)
    {
        var p = new byte[size];
        for (int off = 0; off < size; off += 16)
        {
            int at = Math.Min(off, size - 16);
            var x = r.Ask(0x8100, 5, (byte)(sector >> 8), (byte)sector, (byte)(at >> 8), (byte)at);
            if (x == null) { r.S.AppendLine(sector.ToString("X4") + "=" + r.Error); return null; }
            Array.Copy(x, off - at, p, off, Math.Min(16, size - off));
        }
        r.S.AppendLine(sector.ToString("X4") + "=" + Hex(p, ""));
        return p;
    }

    static byte[] File(Reader r, int addr, int size)
    {
        if (r.Ask(0x8101, 8, (byte)(addr >> 8), (byte)addr, (byte)(size >> 8), (byte)size) == null) { r.S.AppendLine(addr.ToString("X4") + "=" + r.Error); return null; }
        var o = new List<byte>();
        for (int off = 0; off < size; off += 16)
        {
            var x = r.Ask(0x8101, 0xC, (byte)(off >> 8), (byte)off);
            if (x == null) { r.S.AppendLine(addr.ToString("X4") + "=" + r.Error); return null; }
            o.AddRange(x);
        }
        var data = o.Take(size).ToArray();
        r.S.AppendLine(addr.ToString("X4") + "=" + Hex(data, ""));
        return data;
    }

    static int ProfileSize(byte[] directory)
    {
        bool entry = directory != null && directory.Length == 16 && directory[5] == 0x01 && directory[6] == 0x81 && directory[7] == 0x01 && directory[8] == 0x40;
        return entry && directory[9] >= 16 ? directory[9] : 99;
    }

    public static string Name(int id) { string n; return Names.TryGetValue(id, out n) ? n : ""; }

    static void Row(StringBuilder s, string name, string value) { s.AppendLine(name.PadRight(10) + value); }

    static string Hex(byte[] b, string gap) { return BitConverter.ToString(b).Replace("-", gap); }

    static string Usb(string path)
    {
        var m = Regex.Match(path ?? "", @"VID_([0-9A-F]{4})&PID_([0-9A-F]{4})", RegexOptions.IgnoreCase);
        return m.Success ? " · USB " + m.Groups[1].Value.ToUpper() + ":" + m.Groups[2].Value.ToUpper() : "";
    }

    static string Version()
    {
        var a = (AssemblyInformationalVersionAttribute)Attribute.GetCustomAttribute(Assembly.GetExecutingAssembly(), typeof(AssemblyInformationalVersionAttribute));
        return a == null ? "?" : a.InformationalVersion;
    }

    public static string Windows()
    {
        try
        {
            using (var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
            {
                var b = k == null ? null : k.GetValue("CurrentBuild") as string;
                int n;
                int.TryParse(b, out n);
                return (n >= 22000 ? "Windows 11" : n >= 10240 ? "Windows 10" : "Windows") + (b != null ? " build " + b : "");
            }
        }
        catch (Exception) { return "Windows"; }
    }
}
