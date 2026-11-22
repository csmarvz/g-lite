using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

// The keys the Game mode key turns off, through 0x4522 (disable keys by usage; docs/G815-G915.md, tried on the G815): fn 3 clears the
// list, fn 1 adds USB keyboard usages, 16 per call, ended by 00. The firmware turns off the Windows and Menu keys itself, and the list
// only applies while Game mode is on. The keyboard keeps it until it loses power, so G-Lite sends it again at each load.
static class GameModeKeys
{
    public static readonly int[] AlwaysOff = { 0xE3, 0xE7, 0x65 };

    // Lighting ids are USB usages minus 3 (KeyLayout.Iso), except the modifiers, 0x68-0x6F for usages E0-E7.
    public static int Usage(int lightingId)
    {
        if (lightingId >= 0x01 && lightingId <= 0x64) return lightingId + 3;
        if (lightingId >= 0x68 && lightingId <= 0x6F) return 0xE0 + lightingId - 0x68;
        return -1;
    }

    public static int LightingId(int usage)
    {
        if (usage >= 0x04 && usage <= 0x67) return usage - 3;
        if (usage >= 0xE0 && usage <= 0xE7) return 0x68 + usage - 0xE0;
        return -1;
    }

    public static bool CanPick(int usage) { return usage >= 0 && LightingId(usage) >= 0 && !AlwaysOff.Contains(usage); }

    public static List<int> Clean(IEnumerable<int> usages) { return usages.Where(CanPick).Distinct().OrderBy(usage => usage).ToList(); }

    public static void Send(Hidpp hidpp, IList<int> usages)
    {
        byte feature = hidpp.FeatureIndex(0x4522);
        hidpp.Call(feature, 3);
        foreach (var packet in Packets(usages)) hidpp.Call(feature, 1, packet);
    }

    // A full packet has no 00 at its end: the keyboard takes 16 usages as the whole packet (OpenLogi).
    public static List<byte[]> Packets(IList<int> usages)
    {
        var packets = new List<byte[]>();
        for (int at = 0; at < usages.Count; at += 16)
        {
            var packet = new byte[16];
            for (int i = 0; i < 16 && at + i < usages.Count; i++) packet[i] = (byte)usages[at + i];
            packets.Add(packet);
        }
        return packets;
    }

    public static string ToText(IEnumerable<int> usages) { return string.Concat(usages.Select(usage => usage.ToString("X2"))); }

    public static List<int> FromText(string text)
    {
        var usages = new List<int>();
        if (text == null) return usages;
        for (int at = 0; at + 1 < text.Length; at += 2)
        {
            int usage;
            if (int.TryParse(text.Substring(at, 2), System.Globalization.NumberStyles.HexNumber, null, out usage)) usages.Add(usage);
        }
        return Clean(usages);
    }

    // A preset ends in "!" and the usages in hex when it holds Game mode keys ("!" alone for none); one saved before has no "!" and
    // leaves the keys as they are (null). The last "!" counts, since a Screen zone's name may hold one.
    public static string Split(string preset, out string keys)
    {
        keys = null;
        int bang = preset.LastIndexOf('!');
        if (bang < 0) return preset;
        var tail = preset.Substring(bang + 1);
        if (tail.Length % 2 != 0 || !tail.All(Uri.IsHexDigit)) return preset;
        keys = tail;
        return preset.Substring(0, bang);
    }

    // What the key types with the Windows keyboard layout in use, for the keys whose legend depends on it (letters, digits and
    // punctuation); null for the others, which keep the picture's name. Scan codes are the PC set 1 codes of those usages.
    static readonly Dictionary<int, uint> ScanCodes = new Dictionary<int, uint>
    {
        { 0x04, 0x1E }, { 0x05, 0x30 }, { 0x06, 0x2E }, { 0x07, 0x20 }, { 0x08, 0x12 }, { 0x09, 0x21 }, { 0x0A, 0x22 }, { 0x0B, 0x23 },
        { 0x0C, 0x17 }, { 0x0D, 0x24 }, { 0x0E, 0x25 }, { 0x0F, 0x26 }, { 0x10, 0x32 }, { 0x11, 0x31 }, { 0x12, 0x18 }, { 0x13, 0x19 },
        { 0x14, 0x10 }, { 0x15, 0x13 }, { 0x16, 0x1F }, { 0x17, 0x14 }, { 0x18, 0x16 }, { 0x19, 0x2F }, { 0x1A, 0x11 }, { 0x1B, 0x2D },
        { 0x1C, 0x15 }, { 0x1D, 0x2C }, { 0x1E, 0x02 }, { 0x1F, 0x03 }, { 0x20, 0x04 }, { 0x21, 0x05 }, { 0x22, 0x06 }, { 0x23, 0x07 },
        { 0x24, 0x08 }, { 0x25, 0x09 }, { 0x26, 0x0A }, { 0x27, 0x0B }, { 0x2D, 0x0C }, { 0x2E, 0x0D }, { 0x2F, 0x1A }, { 0x30, 0x1B },
        { 0x31, 0x2B }, { 0x32, 0x2B }, { 0x33, 0x27 }, { 0x34, 0x28 }, { 0x35, 0x29 }, { 0x36, 0x33 }, { 0x37, 0x34 }, { 0x38, 0x35 },
        { 0x64, 0x56 }
    };

    [DllImport("user32.dll")]
    static extern uint MapVirtualKeyEx(uint code, uint mapType, IntPtr layout);

    [DllImport("user32.dll")]
    static extern IntPtr GetKeyboardLayout(uint thread);

    public static string Typed(int usage)
    {
        uint scanCode;
        if (!ScanCodes.TryGetValue(usage, out scanCode)) return null;
        try
        {
            var layout = GetKeyboardLayout(0);
            uint virtualKey = MapVirtualKeyEx(scanCode, 1, layout);
            uint typed = MapVirtualKeyEx(virtualKey, 2, layout) & 0xFFFF;
            return virtualKey == 0 || typed == 0 ? null : char.ToUpperInvariant((char)typed).ToString();
        }
        catch (Exception) { return null; }
    }
}

// The Game view's box, on KeyboardPage and ZonesPage: what Game mode does, the keyboard to pick keys on, and Clear. The page keeps
// the list (settings, presets) and sends it; Picked gives the usage of a key clicked, Cleared the Clear button.
class GameModeBox : RoundedBox
{
    readonly KeyPicture picture;
    readonly FlatButton clear;
    public event Action<int> Picked;
    public event Action Cleared;

    // picking: the keyboard's 0x4522 is known, so keys can be picked; without it the box only says so.
    public GameModeBox(KeyLayout keys, bool picking, string note)
    {
        Visible = false;
        Ui.Text(this, "Game mode", 14, 12, () => Theme.Dim, 9f, Ui.Semibold);
        var noteText = Ui.Text(this, note, 14, 36, () => Theme.Dim, 9f);
        noteText.AutoSize = false;
        noteText.Size = new Size(588, 56);
        // Tall enough that the width limits the keys, so the picture spans the box like the Clear button's column below.
        picture = Ui.Add(this, new KeyPicture(keys) { Picking = true, GameModeKey = "Game" }, 14, 96, 588, 210);
        picture.AccessibleName = "Game mode keys";
        // The G915s' Fn key has the id the right Windows key has on the G815, and stays on.
        var keyNames = keys.KeyNames();
        var keyIds = keys.KeyIds();
        picture.FixedIds = !picking ? new HashSet<int>() : new HashSet<int>(keyIds.Where((id, key) =>
            id >= 0 && GameModeKeys.AlwaysOff.Contains(GameModeKeys.Usage(id)) && keyNames[key] != "Fn"));
        picture.CanPick = id => picking && GameModeKeys.CanPick(GameModeKeys.Usage(id));
        picture.Label = (id, name) => GameModeKeys.Typed(GameModeKeys.Usage(id)) ?? name;
        picture.IdPicked += id => { if (Picked != null) Picked(GameModeKeys.Usage(id)); };
        clear = Ui.Add(this, new FlatButton { Text = "Clear", AccessibleName = "Clear the Game mode keys", Visible = picking },
            14, 318, 100, 32);
        clear.Click += (s, e) => { if (Cleared != null) Cleared(); };
        picture.Resize += (s, e) => AlignClear();
        AlignClear();
    }

    public void SetModel(KeyLayout model)
    {
        picture.SetModel(model);
        AlignClear();
    }

    // Clear lines up with the drawn keyboard, which a narrow layout (a TKL) leaves centred in the picture.
    void AlignClear() { clear.Left = picture.Left + picture.DrawnLeft; }

    public void ShowKeys(IList<int> usages)
    {
        picture.PickedIds = new HashSet<int>(usages.Select(GameModeKeys.LightingId));
        picture.Invalidate();
        clear.Enabled = usages.Count > 0;
    }
}
