using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

class Release
{
    public string Version, Date;
    public readonly List<string> Lines = new List<string>();
}

static class Updater
{
    public const string Repo = "https://github.com/csmarvz/g-lite";
    const string RawUrl = "https://raw.githubusercontent.com/csmarvz/g-lite/v";
    static readonly string Temp = Path.Combine(Path.GetTempPath(), "GLite-update.exe");

    public static bool CheckForUpdates
    {
        get { return Settings.Get("check-updates") != "0"; }
        set { Settings.Set("check-updates", value ? "1" : "0"); }
    }
    public static bool Auto { get { return Settings.Get("auto-update") == "1"; } set { Settings.Set("auto-update", value ? "1" : "0"); } }
    public static string Skipped { get { return Settings.Get("skip-version"); } set { Settings.Set("skip-version", value); } }

    static string Exe { get { return System.Reflection.Assembly.GetEntryAssembly().Location; } }

    public static string TagFrom(string location)
    {
        if (location == null) return null;
        int tagAt = location.LastIndexOf("/tag/");
        return tagAt < 0 ? null : location.Substring(tagAt + 5);
    }

    public static bool IsNewerVersion(string version, string other)
    {
        version = version.TrimStart('v');
        other = other.TrimStart('v');
        int versionDash = version.IndexOf('-'), otherDash = other.IndexOf('-');
        var versionNumber = new Version(versionDash < 0 ? version : version.Substring(0, versionDash));
        var otherNumber = new Version(otherDash < 0 ? other : other.Substring(0, otherDash));
        if (versionNumber != otherNumber) return versionNumber > otherNumber;
        string versionLabel = versionDash < 0 ? "" : version.Substring(versionDash + 1),
            otherLabel = otherDash < 0 ? "" : other.Substring(otherDash + 1);
        if (versionLabel == otherLabel) return false;
        if (versionLabel == "") return true;
        if (otherLabel == "") return false;
        return string.CompareOrdinal(versionLabel, otherLabel) > 0;
    }

    public static string Latest()
    {
        ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
        var request = (HttpWebRequest)WebRequest.Create(Repo + "/releases/latest");
        request.Method = "HEAD";
        request.AllowAutoRedirect = false;
        request.UserAgent = "G-Lite";
        request.Timeout = 15000;
        using (var response = (HttpWebResponse)request.GetResponse())
        {
            var tag = TagFrom(response.Headers["Location"]);
            return tag == null ? null : tag.TrimStart('v');
        }
    }

    public static List<Release> Releases(string changelog)
    {
        var list = new List<Release>();
        Release current = null;
        foreach (var raw in changelog.Split('\n'))
        {
            var line = raw.Trim();
            var head = Regex.Match(line, @"^## \[?([^\]\s]+)\]?(?:\s*-\s*(\d{4}-\d{2}-\d{2}))?");
            if (head.Success)
            {
                DateTime day;
                current = new Release
                {
                    Version = head.Groups[1].Value,
                    Date = DateTime.TryParseExact(head.Groups[2].Value, "yyyy-MM-dd",
                            CultureInfo.InvariantCulture, DateTimeStyles.None, out day)
                        ? day.ToString("d MMMM yyyy", CultureInfo.InvariantCulture)
                        : ""
                };
                list.Add(current);
            }
            else if (current != null && line != "") current.Lines.Add(line);
        }
        return list;
    }

    public static List<Release> Between(List<Release> all, string installed, string latest)
    {
        return all.Where(release =>
        {
            try { return IsNewerVersion(release.Version, installed) && !IsNewerVersion(release.Version, latest); }
            catch (Exception) { return false; }
        }).ToList();
    }

    public static string Changelog(string version)
    {
        ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
        using (var client = new WebClient { Encoding = Encoding.UTF8 })
        {
            client.Headers["User-Agent"] = "G-Lite";
            return client.DownloadString(RawUrl + version + "/CHANGELOG.md");
        }
    }

    static byte[] Download(string url, Action<int> progress)
    {
        var request = (HttpWebRequest)WebRequest.Create(url);
        request.UserAgent = "G-Lite";
        request.Timeout = 30000;
        using (var response = request.GetResponse())
        using (var stream = response.GetResponseStream())
        using (var memory = new MemoryStream())
        {
            long total = response.ContentLength;
            var buffer = new byte[65536];
            int read, last = -1;
            while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                memory.Write(buffer, 0, read);
                int percent = total > 0 ? (int)Math.Min(100, memory.Length * 100 / total) : -1;
                if (percent != last && progress != null) { last = percent; progress(percent); }
            }
            return memory.ToArray();
        }
    }

    public static void Install(string version, Action<int> progress)
    {
        ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
        string url = Repo + "/releases/download/v" + version + "/", exe = Exe;
        byte[] exeBytes = Download(url + "GLite.exe", progress);
        string sums;
        using (var client = new WebClient())
        {
            client.Headers["User-Agent"] = "G-Lite";
            sums = client.DownloadString(url + "checksums.txt");
        }
        string hash;
        using (var sha = SHA256.Create()) hash = BitConverter.ToString(sha.ComputeHash(exeBytes)).Replace("-", "").ToLowerInvariant();
        if (!sums.Split('\n').Any(line => line.Contains("GLite.exe") && line.Trim().StartsWith(hash)))
            throw new Exception("the download doesn't match its checksum, so nothing was changed");
        Log.Add("", "update", "Downloaded " + version + ", " + exeBytes.Length / 1024 + " KB, checksum ok");
        try
        {
            File.WriteAllBytes(exe + ".new", exeBytes);
            if (File.Exists(exe + ".old")) File.Delete(exe + ".old");
            File.Move(exe, exe + ".old");
            try { File.Move(exe + ".new", exe); }
            catch { File.Move(exe + ".old", exe); throw; }
            Log.Add("", "update", "Replaced GLite.exe with " + version + ", the old one is GLite.exe.old until the next start");
        }
        catch (UnauthorizedAccessException)
        {
            Log.Add("", "update", "The folder isn't writable, asking for administrator rights");
            Elevated(exe, exeBytes);
            Log.Add("", "update", "Replaced GLite.exe with " + version + " with administrator rights");
        }
    }

    public static void Restart()
    {
        var args = Environment.GetCommandLineArgs()
            .Skip(1)
            .Where(arg => arg != "--restart" && arg != "--tray")
            .Concat(new[] { "--restart" });
        Process.Start(Exe, string.Join(" ", args));
    }

    static void Elevated(string exe, byte[] exeBytes)
    {
        File.WriteAllBytes(Temp, exeBytes);
        Process process;
        try { process = Process.Start(new ProcessStartInfo(Temp, "--swap \"" + exe + "\"") { Verb = "runas", UseShellExecute = true }); }
        catch (System.ComponentModel.Win32Exception)
        {
            throw new Exception("the update needs administrator rights, and they weren't given");
        }
        process.WaitForExit();
        if (process.ExitCode != 0) throw new Exception("couldn't replace " + exe);
    }

    public static int Swap(string exe)
    {
        try
        {
            if (File.Exists(exe + ".old")) File.Delete(exe + ".old");
            File.Move(exe, exe + ".old");
            try { File.Copy(Exe, exe); }
            catch { File.Move(exe + ".old", exe); throw; }
            return 0;
        }
        catch { return 1; }
    }

    public static void Cleanup()
    {
        try
        {
            if (File.Exists(Exe + ".old"))
            {
                File.Delete(Exe + ".old");
                Log.Add("", "update", "Removed the previous version, GLite.exe.old");
            }
        }
        catch (Exception ex) { Log.Add("", "update", "Couldn't remove GLite.exe.old: " + ex.Message); }
        try { if (File.Exists(Temp)) File.Delete(Temp); } catch { }
    }
}
