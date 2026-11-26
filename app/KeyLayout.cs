using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

class KeyLayout
{
    public string Name { get; set; }
    public string[] Rows { get; set; }
    public string[] Ids { get; set; }
    // The ids are 0x8070 zones (the G213), not lighting ids.
    public bool Zones { get; set; }
    // The keys drawn in their own shape instead of a key cap (KeyPicture): Logo the G mark, Wheel the volume roller, others round
    // buttons with their icon or name.
    public string[] Shaped { get; set; }

    // Whether a 0x4540 layout code has ISO's shape: 1 is ANSI, 2 and up ISO (Solaar: 2, 5, 8, 0x0B, 0x0D–0x0F ISO; 3 and 7 ISO
    // QWERTZ; 4 AZERTY), JIS (9, 0x3E) included, whose Enter is ISO's L. 0, unknown, stays ANSI. Seen: 01 and 0D only.
    public static bool IsIso(int layoutCode) { return layoutCode >= 2; }

    // The ISO picture, built from the ANSI one, for a keyboard whose 0x4540 layout is ISO (IsIso): \ goes and Enter becomes an L from
    // the Tab row (it keeps its id), the Caps row's Enter becomes #, the left Shift shortens for <>, the right Alt is AltGr.
    // # and <> are lighting ids 2F and 61 (USB codes minus 3), or on a layout of zones the zone of the key they replace or sit by.
    // Null for a layout without these keys.
    public KeyLayout Iso()
    {
        var rows = Rows.Select(row => row.Split('|').ToList()).ToArray();
        var ids = Rows.Select((row, rowIndex) => Ids != null && rowIndex < Ids.Length
                ? Ids[rowIndex].Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).ToList()
                : new List<string>())
            .ToArray();
        // The first \, Enter and Shift that have a width: \ 1.5, Enter 2.25 and Shift 2.25 on most, \ 1.25 and Enter 2 on the MX Keys S.
        // The numpad's Enter comes after the main one.
        Func<List<string>, string, int> find = (row, name) => row.FindIndex(token => token.StartsWith(name + ":"));
        int tab = Array.FindIndex(rows, row => find(row, "\\") >= 0),
            caps = Array.FindIndex(rows, row => find(row, "Enter") >= 0),
            shift = Array.FindIndex(rows, row => find(row, "Shift") >= 0);
        if (tab < 0 || caps < 0 || shift < 0) return null;
        int backslash = find(rows[tab], "\\"), enter = find(rows[caps], "Enter"), leftShift = find(rows[shift], "Shift");
        float backslashWidth = Width(rows[tab][backslash]), enterWidth = Width(rows[caps][enter]), shiftWidth = Width(rows[shift][leftShift]);
        int backslashKey = KeyIndex(rows[tab], backslash), enterKey = KeyIndex(rows[caps], enter),
            shiftKey = KeyIndex(rows[shift], leftShift);
        string enterId = enterKey < ids[caps].Count ? ids[caps][enterKey] : "--";

        rows[tab][backslash] = "Enter:" + Number(backslashWidth - 0.25f) + ":2:.25";
        rows[tab].Insert(backslash, "_.25");
        if (backslashKey < ids[tab].Count) ids[tab][backslashKey] = enterId;

        rows[caps][enter] = "#";
        if (enter + 1 < rows[caps].Count && rows[caps][enter + 1].StartsWith("_"))
            rows[caps][enter + 1] = "_" + Number(float.Parse(rows[caps][enter + 1].Substring(1), CultureInfo.InvariantCulture) + enterWidth - 1);
        else if (enter + 1 < rows[caps].Count) rows[caps].Insert(enter + 1, "_" + Number(enterWidth - 1));
        if (!Zones && enterKey < ids[caps].Count) ids[caps][enterKey] = "2F";

        rows[shift][leftShift] = "Shift:" + Number(shiftWidth - 1);
        rows[shift].Insert(leftShift + 1, "<>");
        if (shiftKey < ids[shift].Count) ids[shift].Insert(shiftKey + 1, Zones ? ids[shift][shiftKey] : "61");

        foreach (var row in rows)
        {
            var alts = Enumerable.Range(0, row.Count).Where(tokenIndex => row[tokenIndex].Split(':')[0] == "Alt").ToList();
            if (alts.Count == 2) row[alts[1]] = "AltGr" + row[alts[1]].Substring(3);
        }
        return new KeyLayout
        {
            Name = Name, Zones = Zones, Shaped = Shaped,
            Rows = rows.Select(row => string.Join("|", row)).ToArray(),
            Ids = Ids == null ? null : ids.Select(row => string.Join(" ", row)).ToArray()
        };
    }

    static int KeyIndex(List<string> row, int tokenIndex) { return row.Take(tokenIndex).Count(cell => !cell.StartsWith("_")); }

    static float Width(string token) { return float.Parse(token.Split(':')[1], CultureInfo.InvariantCulture); }

    static string Number(float value) { return value.ToString("0.##", CultureInfo.InvariantCulture); }

    public List<int> KeyIds()
    {
        var all = new List<int>();
        for (int rowIndex = 0; rowIndex < Rows.Length; rowIndex++)
        {
            var ids = Ids != null && rowIndex < Ids.Length
                ? Ids[rowIndex].Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                : new string[0];
            int keyIndex = 0;
            foreach (var cell in Rows[rowIndex].Split('|'))
            {
                if (cell.StartsWith("_")) continue;
                all.Add(keyIndex < ids.Length && ids[keyIndex] != "--" ? Convert.ToInt32(ids[keyIndex], 16) : -1);
                keyIndex++;
            }
        }
        return all;
    }

    // The name of each key, in the order of KeyIds.
    public List<string> KeyNames()
    {
        return Rows.SelectMany(row => row.Split('|')).Where(cell => !cell.StartsWith("_")).Select(cell => cell.Split(':')[0]).ToList();
    }

    public static KeyLayout Load(string file)
    {
        string text;
        using (var stream = typeof(KeyLayout).Assembly.GetManifestResourceStream(file))
        using (var reader = new System.IO.StreamReader(stream)) text = reader.ReadToEnd();
        return Parse(text);
    }

    // The layout as its .json file holds it: models/iso/*-iso.json are written with this by selftest --record.
    public string ToJson()
    {
        Func<string, string> quote = s => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        Func<string[], string> list = items => "[\n" + string.Join(",\n", items.Select(item => "    " + quote(item))) + "\n  ]";
        return "{\n  \"name\": " + quote(Name) + ",\n"
            + (Zones ? "  \"kind\": \"zones\",\n" : "")
            + (Shaped != null ? "  \"shaped\": " + list(Shaped) + ",\n" : "")
            + "  \"rows\": " + list(Rows)
            + ",\n  \"ids\": " + list(Ids ?? new string[0]) + "\n}\n";
    }

    public static KeyLayout Parse(string text)
    {
        var kind = Strings(text, "kind");
        return new KeyLayout
        {
            Name = Strings(text, "name")[0], Rows = Strings(text, "rows"), Ids = Strings(text, "ids"),
            Shaped = Strings(text, "shaped"), Zones = kind != null && kind[0] == "zones"
        };
    }

    static string[] Strings(string text, string key)
    {
        const string quoted = @"""(?:[^""\\]|\\.)*""";
        var match = Regex.Match(text, @"""" + key + @"""\s*:\s*(" + quoted + @"|\[(?:\s*" + quoted + @"\s*,?)*\s*\])");
        if (!match.Success) return null;
        return Regex.Matches(match.Groups[1].Value, quoted)
            .Cast<Match>()
            .Select(found => Regex.Unescape(found.Value.Substring(1, found.Value.Length - 2)))
            .ToArray();
    }
}
