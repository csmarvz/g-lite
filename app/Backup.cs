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

        public byte[] Get(string key) { byte[] bytes; return Data.TryGetValue(key, out bytes) ? bytes : null; }

        public G515Profile.Files Files
        {
            get { return new G515Profile.Files { FactoryProfile = Get("0101"), FactoryDir = Get("0100"), UserProfile = Get("0008"), UserDir = Get("0000") }; }
        }
    }

    public static string Folder(string device) { return Path.Combine(Root, device); }

    static string Hex(byte[] bytes) { return bytes == null ? "" : string.Concat(bytes.Select(value => value.ToString("x2"))); }

    static byte[] Bytes(string hex) { return hex == "" ? null : Enumerable.Range(0, hex.Length / 2).Select(i => Convert.ToByte(hex.Substring(i * 2, 2), 16)).ToArray(); }

    static bool Same(byte[] first, byte[] second) { return first == null ? second == null : second != null && first.SequenceEqual(second); }

    static bool Same(Dictionary<string, byte[]> first, Dictionary<string, byte[]> second)
    {
        return first.Count == second.Count && first.All(pair => second.ContainsKey(pair.Key) && Same(pair.Value, second[pair.Key]));
    }

    public static Dictionary<string, byte[]> FromFiles(G515Profile.Files files)
    {
        return new Dictionary<string, byte[]> { { "0101", files.FactoryProfile }, { "0100", files.FactoryDir }, { "0008", files.UserProfile }, { "0000", files.UserDir } };
    }

    public static void Save(string device, G515Profile.Files files, string who = null) { Save(device, FromFiles(files), who); }

    public static void Save(string device, Dictionary<string, byte[]> data, string who = null)
    {
        try
        {
            var list = List(device);
            if (list.Count > 0 && Same(list[0].Data, data)) return;
            var dir = Folder(device);
            Directory.CreateDirectory(dir);
            var now = DateTime.UtcNow;
            var when = new DateTime(now.Ticks - now.Ticks % TimeSpan.TicksPerMillisecond, DateTimeKind.Utc);
            // Two backups in the same millisecond would share a name (the second replacing the first) or a time (sorting them at random).
            while (list.Any(entry => entry.When == when)) when = when.AddMilliseconds(1);
            var name = (list.Count == 0 ? "first-" : "") + when.ToString(Utc, CultureInfo.InvariantCulture) + ".txt";
            File.WriteAllLines(Path.Combine(dir, name), data.Select(pair => pair.Key + "=" + Hex(pair.Value)));
            if (who != null) Log.Add(who, "backup", data.Count == 1 ? "sector " + data.Keys.First() : data.Count + " files");
            foreach (var old in List(device).Where(entry => !entry.First).Skip(Keep)) File.Delete(old.File);
        }
        catch (Exception ex)
        {
            if (ex is IOException || ex is UnauthorizedAccessException) throw new RefusedException("Couldn't make a backup, so nothing was written: " + ex.Message);
            throw;
        }
    }

    public static List<Entry> List(string device)
    {
        var entries = new List<Entry>();
        string[] files;
        try { files = Directory.GetFiles(Folder(device), "*.txt"); }
        catch (Exception) { return entries; }
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
                var data = File.ReadAllLines(file).Where(line => line.Length > 4 && line[4] == '=').ToDictionary(line => line.Substring(0, 4), line => Bytes(line.Substring(5).Trim()));
                entries.Add(new Entry { File = file, When = when, First = first, Data = data });
            }
            catch (Exception) { }
        }
        return entries.OrderByDescending(entry => entry.When).ToList();
    }

    public static void Move(string from, string to)
    {
        try { if (Directory.Exists(Folder(from)) && !Directory.Exists(Folder(to))) Directory.Move(Folder(from), Folder(to)); }
        catch (Exception) { }
    }

    public static string Describe(G515Profile.Files files)
    {
        if (!G515Profile.FilesAreValid(files)) return "damaged, can't be restored";
        if (files.UserProfile == null) return "factory lighting";
        var lighting = G515Profile.Decode(files.UserProfile);
        return lighting == null ? "factory animation" : G515Profile.EffectById(lighting.Effect).Name;
    }
}
