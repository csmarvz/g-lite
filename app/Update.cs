using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using Microsoft.Win32;

static class Updater
{
    const string Repo = "https://github.com/csmarvz/g-lite";
    const string Key = @"Software\GLite";

    public static bool Check { get { return Get("CheckUpdates", 1) == 1; } set { Set("CheckUpdates", value ? 1 : 0); } }
    public static bool Auto { get { return Get("AutoUpdate", 0) == 1; } set { Set("AutoUpdate", value ? 1 : 0); } }

    public static string Skipped
    {
        get { using (var k = Registry.CurrentUser.OpenSubKey(Key)) return k == null ? null : k.GetValue("SkipVersion") as string; }
        set { using (var k = Registry.CurrentUser.CreateSubKey(Key)) k.SetValue("SkipVersion", value); }
    }

    static int Get(string name, int def)
    {
        using (var k = Registry.CurrentUser.OpenSubKey(Key))
        {
            var v = k == null ? null : k.GetValue(name);
            return v is int ? (int)v : def;
        }
    }

    static void Set(string name, int v) { using (var k = Registry.CurrentUser.CreateSubKey(Key)) k.SetValue(name, v); }

    static string Exe { get { return System.Reflection.Assembly.GetEntryAssembly().Location; } }

    public static string TagFrom(string location)
    {
        if (location == null) return null;
        int i = location.LastIndexOf("/tag/");
        return i < 0 ? null : location.Substring(i + 5);
    }

    public static bool Newer(string a, string b)
    {
        a = a.TrimStart('v');
        b = b.TrimStart('v');
        int ia = a.IndexOf('-'), ib = b.IndexOf('-');
        var va = new Version(ia < 0 ? a : a.Substring(0, ia));
        var vb = new Version(ib < 0 ? b : b.Substring(0, ib));
        if (va != vb) return va > vb;
        string pa = ia < 0 ? "" : a.Substring(ia + 1), pb = ib < 0 ? "" : b.Substring(ib + 1);
        if (pa == pb) return false;
        if (pa == "") return true;
        if (pb == "") return false;
        return string.CompareOrdinal(pa, pb) > 0;
    }

    public static string Latest()
    {
        ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
        var r = (HttpWebRequest)WebRequest.Create(Repo + "/releases/latest");
        r.Method = "HEAD";
        r.AllowAutoRedirect = false;
        r.UserAgent = "G-Lite";
        r.Timeout = 15000;
        using (var res = (HttpWebResponse)r.GetResponse())
        {
            var tag = TagFrom(res.Headers["Location"]);
            return tag == null ? null : tag.TrimStart('v');
        }
    }

    public static void Install(string version)
    {
        ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
        string url = Repo + "/releases/download/v" + version + "/", exe = Exe;
        byte[] bin;
        string sums;
        using (var w = new WebClient())
        {
            w.Headers["User-Agent"] = "G-Lite";
            bin = w.DownloadData(url + "GLite.exe");
            sums = w.DownloadString(url + "checksums.txt");
        }
        string hash;
        using (var sha = SHA256.Create()) hash = BitConverter.ToString(sha.ComputeHash(bin)).Replace("-", "").ToLowerInvariant();
        if (!sums.Split('\n').Any(l => l.Contains("GLite.exe") && l.Trim().StartsWith(hash)))
            throw new Exception("the download doesn't match its checksum, so nothing was changed");
        File.WriteAllBytes(exe + ".new", bin);
        if (File.Exists(exe + ".old")) File.Delete(exe + ".old");
        File.Move(exe, exe + ".old");
        try { File.Move(exe + ".new", exe); }
        catch { File.Move(exe + ".old", exe); throw; }
        var args = Environment.GetCommandLineArgs().Skip(1).Where(a => a != "--restart").Concat(new[] { "--restart" });
        Process.Start(exe, string.Join(" ", args));
    }

    public static void Cleanup()
    {
        try { if (File.Exists(Exe + ".old")) File.Delete(Exe + ".old"); } catch { }
    }
}
