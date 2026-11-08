using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

static class Log
{
    public static string File = Path.Combine(Path.GetDirectoryName(Settings.Location), "log.txt");
    public static int Version;
    static readonly object gate = new object();
    const int Max = 512 * 1024;

    public class Line
    {
        public string Time, Who, Kind, Text, Result;
        public int Times = 1;
        public bool Bad { get { return Kind == "error" || (Kind == "sent" && Result != "ok"); } }
    }

    public static string Old { get { return Path.Combine(Path.GetDirectoryName(File), Path.GetFileNameWithoutExtension(File) + ".old.txt"); } }

    public static void Add(string who, string kind, string text) { Write(who, kind, text, ""); }

    public static void Sent(string who, int id, int fn, byte[] p, string result)
    {
        Write(who, "sent", id.ToString("X4") + " fn" + fn.ToString("X") + (p.Length > 0 ? "  " + BitConverter.ToString(p).Replace("-", " ") : ""), result);
    }

    public static void Register(string who, int reg, byte[] p, string result)
    {
        Write(who, "sent", "register " + reg.ToString("X2") + (p.Length > 0 ? "  " + BitConverter.ToString(p).Replace("-", " ") : ""), result);
    }

    public static bool Reads(int id, int fn)
    {
        int[] ok;
        return Report.Reads.TryGetValue(id, out ok) && ok.Contains(fn);
    }

    static void Write(string who, string kind, string text, string result)
    {
        var line = string.Join("\t", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture), Clean(who), kind, Clean(text), result);
        lock (gate)
            try
            {
                var f = new FileInfo(File);
                if (f.Exists && f.Length > Max)
                {
                    System.IO.File.Delete(Old);
                    f.MoveTo(Old);
                }
                System.IO.File.AppendAllText(File, line + "\r\n");
                Version++;
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
    }

    static string Clean(string s) { return (s ?? "").Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' '); }

    public static List<Line> Read()
    {
        string[] raw = new string[0];
        lock (gate)
            try { if (System.IO.File.Exists(File)) raw = System.IO.File.ReadAllLines(File); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        var list = new List<Line>();
        foreach (var r in raw)
        {
            var c = r.Split('\t');
            if (c.Length == 5) list.Add(new Line { Time = c[0], Who = c[1], Kind = c[2], Text = c[3], Result = c[4] });
        }
        return list;
    }

    public static long Size()
    {
        try { var f = new FileInfo(File); return f.Exists ? f.Length : 0; }
        catch (Exception) { return 0; }
    }

    public static void Clear()
    {
        lock (gate)
            try
            {
                System.IO.File.Delete(File);
                System.IO.File.Delete(Old);
                Version++;
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
    }

    public static List<Line> Fold(IEnumerable<Line> lines)
    {
        var o = new List<Line>();
        foreach (var l in lines)
        {
            var last = o.Count > 0 ? o[o.Count - 1] : null;
            if (last != null && l.Kind == "sent" && last.Kind == "sent" && last.Who == l.Who && last.Result == l.Result && Fn(last.Text) == Fn(l.Text))
            {
                last.Times++;
                continue;
            }
            o.Add(new Line { Time = l.Time, Who = l.Who, Kind = l.Kind, Text = l.Text, Result = l.Result });
        }
        return o;
    }

    static string Fn(string t) { int i = t.IndexOf("  "); return i < 0 ? t : t.Substring(0, i); }

    public static string Format(Line l)
    {
        var who = l.Who.Length > 14 ? l.Who.Substring(0, 13) + "…" : l.Who;
        var text = l.Text;
        if (l.Kind == "sent")
        {
            var fn = Fn(l.Text);
            text = fn.PadRight(10) + (l.Result + (l.Times > 1 ? " ×" + l.Times : "")).PadRight(12) + l.Text.Substring(fn.Length).TrimStart();
        }
        return (l.Time + "  " + who.PadRight(15) + l.Kind.PadRight(8) + text).TrimEnd();
    }
}
