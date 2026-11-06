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
        public G515.Files Files;
    }

    public static string Folder(string device) { return Path.Combine(Root, device); }

    static string Hex(byte[] b) { return b == null ? "" : string.Concat(b.Select(x => x.ToString("x2"))); }

    static byte[] Bytes(string s) { return s == "" ? null : Enumerable.Range(0, s.Length / 2).Select(i => Convert.ToByte(s.Substring(i * 2, 2), 16)).ToArray(); }

    static bool Same(byte[] a, byte[] b) { return a == null ? b == null : b != null && a.SequenceEqual(b); }

    public static bool Same(G515.Files a, G515.Files b)
    {
        return Same(a.FactoryProfile, b.FactoryProfile) && Same(a.FactoryDir, b.FactoryDir) && Same(a.UserProfile, b.UserProfile) && Same(a.UserDir, b.UserDir);
    }

    public static void Save(string device, G515.Files f)
    {
        try
        {
            var list = List(device);
            if (list.Count > 0 && Same(list[0].Files, f)) return;
            var dir = Folder(device);
            Directory.CreateDirectory(dir);
            var name = (list.Count == 0 ? "first-" : "") + DateTime.UtcNow.ToString(Utc, CultureInfo.InvariantCulture) + ".txt";
            File.WriteAllLines(Path.Combine(dir, name), new[]
            {
                "0101=" + Hex(f.FactoryProfile), "0100=" + Hex(f.FactoryDir), "0008=" + Hex(f.UserProfile), "0000=" + Hex(f.UserDir),
            });
            foreach (var old in List(device).Where(e => !e.First).Skip(Keep)) File.Delete(old.File);
        }
        catch (Exception ex)
        {
            if (ex is IOException || ex is UnauthorizedAccessException) throw new Refused("Couldn't back up the keyboard, so nothing was written: " + ex.Message);
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
                var v = File.ReadAllLines(file).Where(l => l.Length > 4 && l[4] == '=').ToDictionary(l => l.Substring(0, 4), l => l.Substring(5).Trim());
                r.Add(new Entry
                {
                    File = file, When = when, First = first,
                    Files = new G515.Files { FactoryProfile = Bytes(v["0101"]), FactoryDir = Bytes(v["0100"]), UserProfile = Bytes(v["0008"]), UserDir = Bytes(v["0000"]) },
                });
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
