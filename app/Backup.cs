using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

static class Backups
{
    public const int Keep = 10;
    const string Stamp = "yyyyMMdd-HHmmss-fff", Utc = "yyyyMMdd-HHmmss-fff'Z'";
    public static string Root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GLite", "backups");

    public class Entry
    {
        public string File;
        public DateTime When;
        public bool First;
        public Dictionary<string, byte[]> Data;

        public byte[] Get(string key) { byte[] b; return Data.TryGetValue(key, out b) ? b : null; }

        public G515.Files Files
        {
            get { return new G515.Files { FactoryProfile = Get("0101"), FactoryDir = Get("0100"), UserProfile = Get("0008"), UserDir = Get("0000") }; }
        }
    }

    public static string Folder(string device) { return Path.Combine(Root, device); }

    static string Hex(byte[] b) { return b == null ? "" : string.Concat(b.Select(x => x.ToString("x2"))); }

    static byte[] Bytes(string s) { return s == "" ? null : Enumerable.Range(0, s.Length / 2).Select(i => Convert.ToByte(s.Substring(i * 2, 2), 16)).ToArray(); }

    static bool Same(byte[] a, byte[] b) { return a == null ? b == null : b != null && a.SequenceEqual(b); }

    static bool Same(Dictionary<string, byte[]> a, Dictionary<string, byte[]> b)
    {
        return a.Count == b.Count && a.All(p => b.ContainsKey(p.Key) && Same(p.Value, b[p.Key]));
    }

    public static Dictionary<string, byte[]> Of(G515.Files f)
    {
        return new Dictionary<string, byte[]> { { "0101", f.FactoryProfile }, { "0100", f.FactoryDir }, { "0008", f.UserProfile }, { "0000", f.UserDir } };
    }

    public static void Save(string device, G515.Files f) { Save(device, Of(f)); }

    public static void Save(string device, Dictionary<string, byte[]> data)
    {
        try
        {
            var list = List(device);
            if (list.Count > 0 && Same(list[0].Data, data)) return;
            var dir = Folder(device);
            Directory.CreateDirectory(dir);
            var name = (list.Count == 0 ? "first-" : "") + DateTime.UtcNow.ToString(Utc, CultureInfo.InvariantCulture) + ".txt";
            File.WriteAllLines(Path.Combine(dir, name), data.Select(p => p.Key + "=" + Hex(p.Value)));
            foreach (var old in List(device).Where(e => !e.First).Skip(Keep)) File.Delete(old.File);
        }
        catch (Exception ex)
        {
            if (ex is IOException || ex is UnauthorizedAccessException) throw new Refused("Couldn't make a backup, so nothing was written: " + ex.Message);
            throw;
        }
    }

    public static List<Entry> List(string device)
    {
        var r = new List<Entry>();
        string[] files;
        try { files = Directory.GetFiles(Folder(device), "*.txt"); }
        catch (Exception) { return r; }
        foreach (var file in files)
        {
            var name = Path.GetFileNameWithoutExtension(file);
            bool first = name.StartsWith("first-");
            if (first) name = name.Substring(6);
            DateTime when;
            if (!DateTime.TryParseExact(name, Utc, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out when)
                && !DateTime.TryParseExact(name, Stamp, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal | DateTimeStyles.AdjustToUniversal, out when)) continue;
            try
            {
                var data = File.ReadAllLines(file).Where(l => l.Length > 4 && l[4] == '=').ToDictionary(l => l.Substring(0, 4), l => Bytes(l.Substring(5).Trim()));
                r.Add(new Entry { File = file, When = when, First = first, Data = data });
            }
            catch (Exception) { }
        }
        return r.OrderByDescending(e => e.When).ToList();
    }

    public static void Move(string from, string to)
    {
        try { if (Directory.Exists(Folder(from)) && !Directory.Exists(Folder(to))) Directory.Move(Folder(from), Folder(to)); }
        catch (Exception) { }
    }

    public static string Describe(G515.Files f)
    {
        if (!G515.Good(f)) return "damaged, can't be restored";
        if (f.UserProfile == null) return "factory lighting";
        var l = G515.Decode(f.UserProfile);
        return l == null ? "factory animation" : G515.Def(l.Effect).Name;
    }
}
