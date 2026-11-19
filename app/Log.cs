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

    public static string Old
    {
        get { return Path.Combine(Path.GetDirectoryName(File), Path.GetFileNameWithoutExtension(File) + ".old.txt"); }
    }

    public static void Add(string who, string kind, string text) { Write(who, kind, text, ""); }

    public static void Add(string who, string kind, string text, string result) { Write(who, kind, text, result); }

    public static void Sent(string who, int id, int fn, byte[] parameters, string result)
    {
        Write(who, "sent",
            id.ToString("X4") + " fn" + fn.ToString("X")
                + (parameters.Length > 0 ? "  " + BitConverter.ToString(parameters).Replace("-", " ") : ""),
            result);
    }

    public static void Register(string who, int register, byte[] parameters, string result)
    {
        Write(who, "sent",
            "register " + register.ToString("X2")
                + (parameters.Length > 0 ? "  " + BitConverter.ToString(parameters).Replace("-", " ") : ""),
            result);
    }

    public static void Upload(string who, int subId, int sequence, string result)
    {
        Write(who, "sent", "upload " + subId.ToString("X2") + "  " + sequence.ToString("X2"), result);
    }

    public static bool Reads(int id, int fn)
    {
        int[] functions;
        return Report.Reads.TryGetValue(id, out functions) && functions.Contains(fn);
    }

    static void Write(string who, string kind, string text, string result)
    {
        var line = string.Join("\t",
            DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture), OneLine(who), kind, OneLine(text), result);
        lock (gate)
            try
            {
                var file = new FileInfo(File);
                if (file.Exists && file.Length > Max)
                {
                    System.IO.File.Delete(Old);
                    file.MoveTo(Old);
                }
                System.IO.File.AppendAllText(File, line + "\r\n");
                Version++;
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
    }

    static string OneLine(string text) { return (text ?? "").Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' '); }

    public static List<Line> Read()
    {
        string[] raw = new string[0];
        lock (gate)
            try { if (System.IO.File.Exists(File)) raw = System.IO.File.ReadAllLines(File); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        var list = new List<Line>();
        foreach (var row in raw)
        {
            var columns = row.Split('\t');
            if (columns.Length == 5)
                list.Add(new Line { Time = columns[0], Who = columns[1], Kind = columns[2], Text = columns[3], Result = columns[4] });
        }
        return list;
    }

    public static long Size()
    {
        try { var file = new FileInfo(File); return file.Exists ? file.Length : 0; }
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

    public static List<Line> MergeRepeatedSends(IEnumerable<Line> lines)
    {
        var merged = new List<Line>();
        foreach (var line in lines)
        {
            var last = merged.Count > 0 ? merged[merged.Count - 1] : null;
            if (last != null && line.Kind == "sent" && last.Kind == "sent" && last.Who == line.Who && last.Result == line.Result
                && FirstWord(last.Text) == FirstWord(line.Text))
            {
                last.Times++;
                continue;
            }
            merged.Add(new Line { Time = line.Time, Who = line.Who, Kind = line.Kind, Text = line.Text, Result = line.Result });
        }
        return merged;
    }

    static string FirstWord(string text) { int gapAt = text.IndexOf("  "); return gapAt < 0 ? text : text.Substring(0, gapAt); }

    public static string Format(Line line)
    {
        var who = line.Who.Length > 14 ? line.Who.Substring(0, 13) + "…" : line.Who;
        var text = line.Text;
        if (line.Kind == "sent")
        {
            var call = FirstWord(line.Text);
            text = call.PadRight(10)
                + (line.Result + (line.Times > 1 ? " ×" + line.Times : "")).PadRight(12)
                + line.Text.Substring(call.Length).TrimStart();
        }
        return (line.Time + "  " + who.PadRight(15) + line.Kind.PadRight(8) + text).TrimEnd();
    }
}
