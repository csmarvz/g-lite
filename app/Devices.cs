using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;

class Found
{
    public string Key, Path, ShortPath, Name = "", Kind = "device", Link = "", Firmware = "", Models = "";
    public byte Index;
    public int Wpid, Battery = -1, Millivolts;
    public bool Approx, Asleep, Hidpp10, PlainHid;
    public int[] Features = new int[0];

    public bool Has(int f) { return Features.Contains(f); }
    public string Slot { get { return Index == 0xFF || Hidpp10 ? "" : " · slot " + Index; } }
    public bool IsG515 { get { return Name.ToUpper().Contains("G515") && Has(0x8071) && Has(0x8040) && Has(0x8101); } }
    public bool IsG915 { get { var n = Name.ToUpper(); return n.Contains("G915") && !n.Contains("G915 X") && Has(0x8071) && Has(0x8040) && Has(0x8100) && !Has(0x8101); } }
    public bool IsMouse { get { return Kind == "mouse" && Has(0x2201); } }
    public bool IsG9x { get { return Hidpp10 && Path != null && Regex.IsMatch(Path, @"PID_(C066|C249)", RegexOptions.IgnoreCase); } }
    public bool IsHeadset { get { return Kind == "headset" && Has(0x8300); } }
    public bool HasAutoOff { get { return Path != null && Regex.IsMatch(Path, @"PID_(0AAA|0ABA|0AFB|0AFC)", RegexOptions.IgnoreCase) && Has(0x1F20); } }

    public string Slug { get { return new string(Short.ToLower().Where(char.IsLetterOrDigit).ToArray()); } }

    public string Short { get { return Regex.Replace(Regex.Replace(Name, @"(\s+Wireless\s+RGB\s+Mechanical)?\s+Gaming\s+(Mouse|Headset|Keyboard)$", "", RegexOptions.IgnoreCase), @"\s+LIGHTSPEED\b", "", RegexOptions.IgnoreCase); } }

    public string BatteryText { get { return Battery < 0 ? "" : (Approx ? "~" : "") + Battery + "%"; } }

    public static string ModelText(byte[] info)
    {
        if (info == null || info.Length < 13) return "";
        int transports = info[5] << 8 | info[6];
        var ids = Enumerable.Range(0, 3).Select(i => info[7 + 2 * i] << 8 | info[8 + 2 * i]).ToList();
        var names = new[] { "Bluetooth", "Bluetooth", "LIGHTSPEED", "USB" };
        var labels = Enumerable.Range(0, 4).Where(bit => (transports >> bit & 1) != 0).Select(bit => names[bit]).ToList();
        bool matches = labels.Count > 0 && labels.Count <= 3 && ids.Take(labels.Count).All(id => id != 0) && ids.Skip(labels.Count).All(id => id == 0);
        if (!matches) return string.Join(" · ", ids.Where(id => id != 0).Select(id => id.ToString("X4")));
        var order = new[] { "USB", "LIGHTSPEED", "Bluetooth" };
        return string.Join(" · ", labels.Select((label, i) => new { label, id = ids[i] }).OrderBy(pair => Array.IndexOf(order, pair.label)).Select(pair => pair.label + " " + pair.id.ToString("X4")));
    }

    public string Sub
    {
        get
        {
            if (Asleep) return Link + " · asleep";
            return Link + (Battery >= 0 ? " · " + BatteryText : "");
        }
    }

    public static bool LooksG(string name)
    {
        return Regex.IsMatch(name ?? "", @"^(Logitech\s+)?(G\s?\d|G\s+PRO|PRO\b|ASTRO|A\d0\b)", RegexOptions.IgnoreCase);
    }

    public static string FirmwareText(byte[] info)
    {
        var prefix = new string(info.Skip(1).Take(3).TakeWhile(b => b >= 0x20 && b < 0x7F).Select(b => (char)b).ToArray()).Trim();
        return (prefix + " " + info[4].ToString("X2") + "." + info[5].ToString("X2") + " · build " + info[6].ToString("X2") + info[7].ToString("X2")).Trim();
    }

    public static int FromVolts(int mv) { return Math.Max(0, Math.Min(100, (mv - 3500) * 100 / 600)); }
}

static class Devices
{
    const string HidClass = "#{4d1e55b2-f16f-11cf-88cb-001111000030}";
    public static readonly string[] Kinds = { "keyboard", "device", "device", "mouse", "mouse", "mouse", "device", "device", "headset" };

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr sec, uint disp, uint flags, IntPtr tmpl);
    [DllImport("hid.dll")] static extern bool HidD_GetPreparsedData(SafeFileHandle h, out IntPtr p);
    [DllImport("hid.dll")] static extern bool HidD_FreePreparsedData(IntPtr p);
    [DllImport("hid.dll")] static extern int HidP_GetCaps(IntPtr p, short[] caps);
    [DllImport("hid.dll", CharSet = CharSet.Unicode)] static extern bool HidD_GetProductString(SafeFileHandle h, System.Text.StringBuilder b, int len);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)] static extern int CM_Get_Device_Interface_List_Size(out int length, ref Guid interfaceClass, string deviceId, int flags);
    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)] static extern int CM_Get_Device_Interface_List(ref Guid interfaceClass, string deviceId, char[] buffer, int length, int flags);

    static readonly Regex PlainModels = new Regex(@"PID_(C22D|C22E)", RegexOptions.IgnoreCase);

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
                .Where(path => path.StartsWith(@"\\?\HID#", StringComparison.OrdinalIgnoreCase) && path.IndexOf("046D", StringComparison.OrdinalIgnoreCase) >= 0 && path.LastIndexOf("#{") > 4)
                .Select(path => path.Substring(4, path.LastIndexOf("#{") - 4).Replace('#', '\\').ToUpperInvariant()).ToArray();
        }
        return new string[0];
    }

    public static string PathOf(string id) { return @"\\?\" + id.Replace('\\', '#') + HidClass; }

    public static SafeFileHandle Open(string path) { return CreateFile(path, 0xC0000000, 3, IntPtr.Zero, 3, 0x40000000, IntPtr.Zero); }

    public static SafeFileHandle OpenSync(string path, uint access) { return CreateFile(path, access, 3, IntPtr.Zero, 3, 0, IntPtr.Zero); }

    public static string[] PlainIds(string pid) { return pid == "" ? new string[0] : HidIds().Where(id => id.Contains("PID_" + pid.ToUpperInvariant())).OrderBy(id => id).ToArray(); }

    static List<Found> Plain()
    {
        return HidIds().Select(id => PlainModels.Match(id)).Where(m => m.Success).Select(m => m.Groups[1].Value.ToUpperInvariant()).Distinct().Select(pid =>
        {
            var paths = PlainIds(pid).Select(PathOf).ToList();
            var name = Regex.Replace(paths.Select(Product).FirstOrDefault(Found.LooksG) ?? "G510 Gaming Keyboard", @"^Logitech\s+", "", RegexOptions.IgnoreCase);
            return new Found { Key = "plain|" + pid, Path = paths[0], Name = name, Kind = "keyboard", Link = "USB", Index = 0xFF, Models = "USB " + pid, PlainHid = true };
        }).ToList();
    }

    class Port { public string Id, Group, Long, Short; }

    static void Caps(string path, out int page, out int outLen)
    {
        page = 0; outLen = 0;
        using (var h = Open(path))
        {
            if (h.IsInvalid) return;
            IntPtr pp;
            if (!HidD_GetPreparsedData(h, out pp)) return;
            var c = new short[32];
            HidP_GetCaps(pp, c);
            HidD_FreePreparsedData(pp);
            page = (ushort)c[1];
            outLen = c[3];
        }
    }

    static List<Port> Ports()
    {
        var groups = new Dictionary<string, Port>();
        foreach (var id in HidIds())
            {
                int col = id.IndexOf("&COL", StringComparison.OrdinalIgnoreCase), slash = id.LastIndexOf('\\'), amp = id.LastIndexOf('&');
                if (col < 0 || slash < 0 || amp < slash || PlainModels.IsMatch(id)) continue;
                var group = id.Substring(0, col) + "|" + id.Substring(slash, amp - slash);
                int page, len;
                var path = PathOf(id);
                Caps(path, out page, out len);
                if (page < 0xFF00 || (len != 20 && len != 7)) continue;
                Port p;
                if (!groups.TryGetValue(group, out p)) groups[group] = p = new Port { Group = group };
                if (len == 20) { p.Long = path; p.Id = id; } else p.Short = path;
            }
        return groups.Values.Where(p => p.Long != null).ToList();
    }

    public static List<Found> Scan()
    {
        var tasks = Ports().Select(p => Task.Run(() => { try { return ScanPort(p); } catch (Exception) { return new List<Found>(); } })).ToArray();
        Task.WaitAll(tasks, 20000);
        var all = tasks.Where(t => t.IsCompleted).SelectMany(t => t.Result).ToList();
        try { all.AddRange(Plain()); } catch (Exception) { }
        foreach (var f in all.Where(f => !f.Asleep && f.Wpid != 0)) Settings.Set("seen." + f.Wpid.ToString("X4"), f.Name + "|" + f.Kind);
        return all.Where(f => Found.LooksG(f.Name)).OrderBy(f => f.Kind == "keyboard" ? 0 : f.Kind == "mouse" ? 1 : f.Kind == "headset" ? 2 : 3).ThenBy(f => f.Name).ToList();
    }

    static string LinkOf(string id)
    {
        if (id.Contains("{00001812")) return "Bluetooth";
        var m = Regex.Match(id, @"PID_([0-9A-F]{4})", RegexOptions.IgnoreCase);
        var pid = m.Success ? m.Groups[1].Value.ToUpper() : "";
        if (pid == "C52B" || pid == "C532" || pid == "C534") return "Unifying";
        if (pid == "C548") return "Bolt";
        return "LIGHTSPEED";
    }

    static List<Found> ScanPort(Port p)
    {
        var r = new List<Found>();
        using (var pr = new Probe(p.Long, p.Short))
        {
            if (pr.Ping(0xFF, 800) >= 2)
            {
                var f = Describe(pr, p, 0xFF, p.Id.Contains("{00001812") ? "Bluetooth" : "USB");
                if (f.Link == "USB" && f.Kind == "headset" && (f.Has(0x1F20) || f.Has(0x1004) || f.Has(0x1000) || f.Has(0x1001))) f.Link = "LIGHTSPEED";
                r.Add(f);
                return r;
            }
            if (p.Short == null) return r;
            var link = LinkOf(p.Id);
            for (byte n = 1; n <= 6; n++)
            {
                var pair = pr.Register(0xFF, 0x83, 0xB5, (byte)(0x20 + n - 1));
                if (pair == null) continue;
                int wpid = pair[3] << 8 | pair[4];
                int kind = pair[7];
                Found f;
                if (pr.Ping(n, 3500) >= 2) f = Describe(pr, p, n, link);
                else
                {
                    f = new Found { Path = p.Long, Index = n, Link = link, Asleep = true };
                    var seen = Settings.Get("seen." + wpid.ToString("X4")).Split('|');
                    f.Name = seen[0] != "" ? seen[0] : link == "LIGHTSPEED" ? "G device" : "";
                    f.Kind = seen.Length > 1 ? seen[1] : kind == 1 ? "keyboard" : kind == 2 ? "mouse" : "device";
                }
                f.Wpid = wpid;
                f.Key = p.Long + "|" + n;
                r.Add(f);
            }
            if (r.Count == 0)
            {
                var v = pr.Register(0x00, 0x81, 0xF1, 0x01);
                var o = v == null ? null : Old(pr, p, v);
                if (o != null && o.Kind != "device") r.Add(o);
            }
        }
        return r;
    }

    static Found Old(Probe pr, Port p, byte[] v)
    {
        var b = pr.Register(0x00, 0x81, 0xF1, 0x02);
        var name = Product(p.Long);
        var low = name.ToLower();
        return new Found
        {
            Path = p.Long, ShortPath = p.Short, Index = 0, Key = p.Long + "|0", Link = "USB", Hidpp10 = true, Name = name,
            Kind = low.Contains("mouse") ? "mouse" : low.Contains("keyboard") ? "keyboard" : low.Contains("headset") ? "headset" : "device",
            Firmware = v[1].ToString("X2") + "." + v[2].ToString("X2") + (b != null ? " · build " + b[1].ToString("X2") + b[2].ToString("X2") : ""),
            Models = Regex.IsMatch(p.Long, @"PID_[0-9A-F]{4}", RegexOptions.IgnoreCase) ? "USB " + Regex.Match(p.Long, @"PID_([0-9A-F]{4})", RegexOptions.IgnoreCase).Groups[1].Value.ToUpper() : ""
        };
    }

    static string Product(string path)
    {
        using (var h = Open(path))
        {
            var sb = new System.Text.StringBuilder(128);
            return !h.IsInvalid && HidD_GetProductString(h, sb, 256) ? sb.ToString().Trim() : "";
        }
    }

    static Found Describe(Probe pr, Port p, byte idx, string link)
    {
        var f = new Found { Path = p.Long, Index = idx, Link = link, Key = p.Long + "|" + idx };
        byte fs = pr.Feature(idx, 0x0001);
        var list = new List<int>();
        var count = fs == 0 ? null : pr.Call(idx, fs, 0);
        if (count != null)
            for (int i = 1; i <= count[0]; i++)
            {
                var id = pr.Call(idx, fs, 1, (byte)i);
                if (id != null) list.Add(id[0] << 8 | id[1]);
            }
        f.Features = list.ToArray();
        byte nm = pr.Feature(idx, 0x0005);
        if (nm != 0)
        {
            var len = pr.Call(idx, nm, 0);
            var name = "";
            for (int o = 0; len != null && o < len[0]; o += 16)
            {
                var part = pr.Call(idx, nm, 1, (byte)o);
                if (part == null) break;
                name += new string(part.Take(16).TakeWhile(b => b != 0).Select(b => (char)b).ToArray());
            }
            f.Name = len == null ? name : name.Substring(0, Math.Min(name.Length, len[0]));
            var type = pr.Call(idx, nm, 2);
            if (type != null && type[0] < Kinds.Length) f.Kind = Kinds[type[0]];
        }
        byte fw = pr.Feature(idx, 0x0003);
        var entities = fw == 0 ? null : pr.Call(idx, fw, 0);
        f.Models = Found.ModelText(entities);
        byte[] info = null;
        for (int e = 0; entities != null && e < entities[0] && info == null; e++)
        {
            var r = pr.Call(idx, fw, 1, (byte)e);
            if (r != null && r[0] == 0) info = r;
        }
        if (info != null)
            f.Firmware = Found.FirmwareText(info);
        Battery(pr, f);
        return f;
    }

    static void Battery(Probe pr, Found f)
    {
        byte i;
        byte[] r;
        if ((i = pr.Feature(f.Index, 0x1004)) != 0 && (r = pr.Call(f.Index, i, 1)) != null) { f.Battery = r[0]; return; }
        if ((i = pr.Feature(f.Index, 0x1000)) != 0 && (r = pr.Call(f.Index, i, 0)) != null) { f.Battery = r[0]; return; }
        if (((i = pr.Feature(f.Index, 0x1001)) != 0 || (i = pr.Feature(f.Index, 0x1F20)) != 0) && (r = pr.Call(f.Index, i, 0)) != null)
        {
            f.Millivolts = r[0] << 8 | r[1];
            f.Battery = Found.FromVolts(f.Millivolts);
            f.Approx = true;
        }
    }
}

class Probe : IDisposable
{
    const byte Swid = 0x0D;
    readonly FileStream lng, sht;
    readonly byte[] lbuf = new byte[20], sbuf = new byte[7];
    Task<int> lread, sread;

    public Probe(string longPath, string shortPath)
    {
        var h = Devices.Open(longPath);
        if (h.IsInvalid) throw new IOException("can't open " + longPath);
        lng = new FileStream(h, FileAccess.ReadWrite, 1, true);
        if (shortPath == null) return;
        var s = Devices.Open(shortPath);
        if (!s.IsInvalid) sht = new FileStream(s, FileAccess.ReadWrite, 1, true);
    }

    public void Dispose() { lng.Dispose(); if (sht != null) sht.Dispose(); }

    byte[] Next(DateTime end)
    {
        if (lread == null) lread = lng.ReadAsync(lbuf, 0, 20);
        if (sht != null && sread == null) sread = sht.ReadAsync(sbuf, 0, 7);
        int ms = (int)(end - DateTime.UtcNow).TotalMilliseconds;
        if (ms <= 0) return null;
        int w = sht == null ? (lread.Wait(ms) ? 0 : -1) : Task.WaitAny(new Task[] { lread, sread }, ms);
        if (w < 0) return null;
        if (w == 0) { lread = null; return (byte[])lbuf.Clone(); }
        sread = null;
        var r = new byte[20];
        Array.Copy(sbuf, r, 7);
        return r;
    }

    public int Ping(byte idx, int ms)
    {
        var r = Send(idx, 0, 1, ms, 0, 0, 0x5A);
        return r == null ? 0 : r[0];
    }

    public byte Feature(byte idx, int id)
    {
        var r = Call(idx, 0, 0, (byte)(id >> 8), (byte)id);
        return r == null ? (byte)0 : r[0];
    }

    public byte[] Call(byte idx, byte feat, int fn, params byte[] p) { return Send(idx, feat, fn, 3000, p); }

    byte[] Send(byte idx, byte feat, int fn, int ms, params byte[] p)
    {
        var m = new byte[20];
        m[0] = 0x11; m[1] = idx; m[2] = feat; m[3] = (byte)(fn << 4 | Swid);
        Array.Copy(p, 0, m, 4, Math.Min(p.Length, 16));
        lng.Write(m, 0, 20);
        var end = DateTime.UtcNow.AddMilliseconds(ms);
        while (true)
        {
            var r = Next(end);
            if (r == null) return null;
            if (r[1] != idx) continue;
            if (r[2] == 0x8F && r[3] == feat) return null;
            if (r[2] == 0xFF && r[3] == feat && r[4] == m[3]) return null;
            if (r[2] == feat && r[3] == m[3]) return r.Skip(4).ToArray();
        }
    }

    public byte[] Register(byte idx, byte sub, byte addr, byte param)
    {
        if (sht == null) return null;
        var m = new byte[] { 0x10, idx, sub, addr, param, 0, 0 };
        sht.Write(m, 0, 7);
        var end = DateTime.UtcNow.AddMilliseconds(1000);
        while (true)
        {
            var r = Next(end);
            if (r == null) return null;
            if (r[1] != idx) continue;
            if (r[2] == 0x8F && r[3] == sub) return null;
            if (r[2] == sub && r[3] == addr) return r.Skip(4).ToArray();
        }
    }
}
