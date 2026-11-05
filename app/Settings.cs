using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

static class Settings
{
    static readonly string[] Defaults = { "check-updates=1", "auto-update=0", "skip-version=", "tray-tip-shown=0", "theme=system" };
    public static string Location = Locate();

    static string Locate()
    {
        var here = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.txt");
        if (Writable(here)) return here;
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GLite");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "settings.txt");
    }

    static bool Writable(string file)
    {
        try { using (new FileStream(file, FileMode.OpenOrCreate, FileAccess.ReadWrite)) { } return true; }
        catch { return false; }
    }

    static Dictionary<string, string> Read()
    {
        var d = new Dictionary<string, string>();
        var lines = File.Exists(Location) ? File.ReadAllLines(Location) : new string[0];
        foreach (var l in Defaults.Concat(lines))
        {
            int i = l.IndexOf('=');
            if (i > 0) d[l.Substring(0, i).Trim()] = l.Substring(i + 1).Trim();
        }
        return d;
    }

    static void Write(Dictionary<string, string> d)
    {
        try { File.WriteAllLines(Location, d.Select(p => p.Key + "=" + p.Value)); } catch { }
    }

    public static void Init()
    {
        if (!File.Exists(Location) || new FileInfo(Location).Length == 0) Write(Read());
        try { Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(@"Software\GLite", false); } catch { }
        Rename("set.", "profile.g515.");
    }

    public static void Rename(string from, string to)
    {
        var d = Read();
        var old = d.Keys.Where(k => k.StartsWith(from)).ToList();
        if (old.Count == 0) return;
        foreach (var k in old)
        {
            var n = to + k.Substring(from.Length);
            if (!d.ContainsKey(n)) d[n] = d[k];
            d.Remove(k);
        }
        Write(d);
    }

    public static string Get(string key)
    {
        string v;
        return Read().TryGetValue(key, out v) ? v : "";
    }

    public static IEnumerable<string> Keys() { return Read().Keys; }

    public static void Remove(string key)
    {
        var d = Read();
        if (d.Remove(key)) Write(d);
    }

    public static void Set(string key, string value)
    {
        var d = Read();
        d[key] = value;
        Write(d);
    }
}
