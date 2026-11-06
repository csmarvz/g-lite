using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

static class Report
{
    public static readonly Dictionary<int, int[]> Reads = new Dictionary<int, int[]>
    {
        { 0x0001, new[] { 0, 1 } },
        { 0x0003, new[] { 0, 1 } },
        { 0x1B04, new[] { 0, 1 } },
        { 0x1F20, new[] { 0, 1 } },
        { 0x2201, new[] { 0, 1, 2 } },
        { 0x4540, new[] { 0 } },
        { 0x8010, new[] { 0 } },
        { 0x8020, new[] { 0 } },
        { 0x8040, new[] { 0, 1 } },
        { 0x8060, new[] { 0, 1 } },
        { 0x8070, new[] { 0, 1, 2 } },
        { 0x8071, new[] { 0 } },
        { 0x8100, new[] { 0, 2, 4, 5, 0xB } },
        { 0x8101, new[] { 8, 0xC } },
        { 0x8300, new[] { 0 } },
        { 0x8310, new[] { 2 } },
    };

    static readonly Dictionary<int, string> Names = new Dictionary<int, string>
    {
        { 0x0001, "feature set" }, { 0x0003, "firmware" }, { 0x0005, "name" }, { 0x0007, "friendly name" },
        { 0x0020, "config change" }, { 0x1000, "battery" }, { 0x1001, "battery voltage" }, { 0x1004, "battery" },
        { 0x1300, "LED control" }, { 0x1814, "change host" }, { 0x1815, "hosts info" }, { 0x1B04, "controls" },
        { 0x1D4B, "wireless status" }, { 0x1F20, "battery and auto-off" }, { 0x2201, "DPI" }, { 0x4540, "keyboard layout" },
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
        s.AppendLine("G-Lite device report");
        s.AppendLine("G-Lite " + Version() + " · " + Windows());
        s.AppendLine();
        Row(s, "Device", f.Name == "" ? "–" : f.Name);
        Row(s, "Kind", f.Kind);
        Row(s, "Link", f.Link + (f.Index == 0xFF ? "" : " · slot " + f.Index) + (f.Wpid != 0 ? " · ID " + f.Wpid.ToString("X4") : "") + Usb(f.Path));
        Row(s, "Firmware", f.Firmware == "" ? "–" : f.Firmware);
        Row(s, "Battery", (f.Battery < 0 ? "–" : f.BatteryText) + (f.Millivolts > 0 ? " · " + f.Millivolts + " mV" : ""));
        Row(s, "Screen", screen);

        var ids = Features(r, f);
        foreach (var id in ids.Where(Reads.ContainsKey).Where(x => x != 0x0001))
        {
            s.AppendLine();
            s.AppendLine(id.ToString("X4") + " " + Name(id));
            Section(r, f, id);
        }
        return s.ToString();
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
                    for (int e = 0; c != null && e < Math.Min((int)c[0], 8); e++) r.Show("entity " + e, 0x0003, 1, (byte)e);
                    break;
                }
            case 0x1B04:
                {
                    var c = r.Show("count", 0x1B04, 0);
                    for (int i = 0; c != null && i < Math.Min((int)c[0], 48); i++) r.Show("control " + i, 0x1B04, 1, (byte)i);
                    break;
                }
            case 0x1F20: r.Show("battery", id, 0); r.Show("auto-off", id, 1); break;
            case 0x2201: r.Show("sensors", id, 0); r.Show("dpi list", id, 1, 0); r.Show("dpi", id, 2, 0); break;
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
            case 0x8100: Profiles(r); break;
            case 0x8101:
                {
                    var files = f.IsG515 ? new[] { 0x0000, 16, 0x0008, 99, 0x0100, 16, 0x0101, 99 } : new[] { 0x0000, 16, 0x0100, 16 };
                    for (int i = 0; i < files.Length; i += 2) File(r, files[i], files[i + 1]);
                    break;
                }
            case 0x8300: r.Show("sidetone", id, 0); break;
            case 0x8310: r.Show("bands", id, 2); break;
        }
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

    static void File(Reader r, int addr, int size)
    {
        if (r.Ask(0x8101, 8, (byte)(addr >> 8), (byte)addr, (byte)(size >> 8), (byte)size) == null) { r.S.AppendLine(addr.ToString("X4") + "=" + r.Error); return; }
        var o = new List<byte>();
        for (int off = 0; off < size; off += 16)
        {
            var x = r.Ask(0x8101, 0xC, (byte)(off >> 8), (byte)off);
            if (x == null) { r.S.AppendLine(addr.ToString("X4") + "=" + r.Error); return; }
            o.AddRange(x);
        }
        r.S.AppendLine(addr.ToString("X4") + "=" + Hex(o.Take(size).ToArray(), ""));
    }

    static string Name(int id) { string n; return Names.TryGetValue(id, out n) ? n : ""; }

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

    static string Windows()
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
