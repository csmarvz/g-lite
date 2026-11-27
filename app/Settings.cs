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

    // The lines read last, for when the file can't be read.
    static string[] lastLines = new string[0];

    // Another program can hold the file for a moment (an antivirus scan, a sync client): a read or write is tried again a few times,
    // and a read that still fails gives what was read last. A mouse screen's timer reading it threw out of G-Lite.
    static T Retry<T>(Func<T> action, T failed)
    {
        for (int attempt = 0; ; attempt++)
            try { return action(); }
            catch (IOException) { if (attempt == 4) return failed; System.Threading.Thread.Sleep(20); }
            catch (UnauthorizedAccessException) { if (attempt == 4) return failed; System.Threading.Thread.Sleep(20); }
    }

    static Dictionary<string, string> Read()
    {
        var settings = new Dictionary<string, string>();
        var lines = lastLines = Retry(() => File.Exists(Location) ? File.ReadAllLines(Location) : new string[0], lastLines);
        foreach (var line in Defaults.Concat(lines))
        {
            int equalsAt = line.IndexOf('=');
            if (equalsAt > 0) settings[line.Substring(0, equalsAt).Trim()] = line.Substring(equalsAt + 1).Trim();
        }
        return settings;
    }

    static void Write(Dictionary<string, string> settings)
    {
        var lines = settings.Select(pair => pair.Key + "=" + pair.Value).ToArray();
        if (Retry(() => { File.WriteAllLines(Location, lines); return true; }, false)) lastLines = lines;
    }

    public static void Init()
    {
        if (!File.Exists(Location) || new FileInfo(Location).Length == 0) Write(Read());
        try { Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(@"Software\GLite", false); } catch { }
        Rename("set.", "profile.g515.");
    }

    public static void Rename(string from, string to)
    {
        var settings = Read();
        var old = settings.Keys.Where(key => key.StartsWith(from)).ToList();
        if (old.Count == 0) return;
        foreach (var key in old)
        {
            var newKey = to + key.Substring(from.Length);
            if (!settings.ContainsKey(newKey)) settings[newKey] = settings[key];
            settings.Remove(key);
        }
        Write(settings);
    }

    public static string Get(string key)
    {
        string value;
        return Read().TryGetValue(key, out value) ? value : "";
    }

    public static IEnumerable<string> Keys() { return Read().Keys; }

    public static void Remove(string key)
    {
        var settings = Read();
        if (settings.Remove(key)) Write(settings);
    }

    public static void Set(string key, string value)
    {
        var settings = Read();
        settings[key] = value;
        Write(settings);
    }
}
