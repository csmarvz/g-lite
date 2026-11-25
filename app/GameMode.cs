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
    // The picture the Game view's keys are picked on, ids passed to IdPicked; the keys the keyboard turns off itself are marked fixed.
    // The G915s' Fn key has the id the right Windows key has on the G815, and stays on.
    public static KeyPicture Picture(KeyLayout keys, bool controlBitmap)
    {
        var picture = new KeyPicture(keys) { Picking = true, GameModeKey = "Game", AccessibleName = "Game mode keys" };
        var keyNames = keys.KeyNames();
        var keyIds = keys.KeyIds();
        picture.FixedIds = controlBitmap ? new HashSet<int>() : new HashSet<int>(keyIds.Where((id, key) =>
            id >= 0 && AlwaysOff.Contains(Usage(id)) && keyNames[key] != "Fn"));
        picture.CanPick = id => CanPick(Usage(id), controlBitmap);
        picture.Label = (id, name) => Typed(Usage(id)) ?? name;
        return picture;
    }

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

    // controlBitmap: a 0x4523 keyboard, whose list holds any key with a control, Windows and Menu too: the G515 leaves them on in
    // Game mode (tried).
    public static bool CanPick(int usage, bool controlBitmap)
    {
        if (controlBitmap) return ControlUsages.ContainsValue(usage) && LightingId(usage) >= 0;
        return usage >= 0 && LightingId(usage) >= 0 && !AlwaysOff.Contains(usage);
    }

    public static List<int> Clean(IEnumerable<int> usages, bool controlBitmap)
    {
        return usages.Where(usage => CanPick(usage, controlBitmap)).Distinct().OrderBy(usage => usage).ToList();
    }

    // controlBitmap: the keyboard has 0x4523 instead of 0x4522 (the G515, the G915 X).
    public static void Send(Hidpp hidpp, IList<int> usages, bool controlBitmap)
    {
        if (controlBitmap) { hidpp.Call(hidpp.FeatureIndex(0x4523), 0, Bitmap(Controls(hidpp), usages)); return; }
        byte feature = hidpp.FeatureIndex(0x4522);
        hidpp.Call(feature, 3);
        foreach (var packet in Packets(usages)) hidpp.Call(feature, 1, packet);
    }

    // 0x4523 fn 0 takes a bitmap of the keyboard's control list (0x1B10), bit i for control i from the first byte's low bit: tried on
    // a G515 TKL, where byte 7 turned off A, D, G, J, L, à, Enter and End, controls 0x38-0x3F. 0x1B10 fn 0 counts the controls (128),
    // fn 1 gives eight ids (big endian) from an index. Some ids are Solaar's (F1 0x00F6, F2-F8 0x00EF-0x00F5, Tab 0x010C, Home
    // 0x0118); the others are placed from where they sit in the G515's list, which follows the key matrix: a row of keys takes turns
    // between two groups of eight (` at 0x08, 1 at 0x18, 2 at 0x09, 3 at 0x19 and so on). Ids where JIS keyboards have their extra
    // keys (0x017C, 0x017E, 0x017F, 0x0180) aren't listed, so those stay on.
    static readonly Dictionary<int, int> ControlUsages = MakeControlUsages();

    static Dictionary<int, int> MakeControlUsages()
    {
        var usages = new Dictionary<int, int>();
        for (int letter = 0; letter < 26; letter++) usages[0x0122 + letter] = 0x04 + letter;
        for (int digit = 1; digit <= 9; digit++) usages[0x0142 + digit] = 0x1D + digit;
        usages[0x0142] = 0x27;
        usages[0x00F6] = 0x3A;
        for (int key = 0; key < 7; key++) usages[0x00EF + key] = 0x3B + key;
        for (int key = 0; key < 4; key++) usages[0x014D + key] = 0x42 + key;
        int[] pairs =
        {
            0x0155, 0x28, 0x014C, 0x29, 0x0156, 0x2A, 0x010C, 0x2B, 0x0161, 0x2C, 0x0158, 0x2D, 0x0153, 0x2E, 0x015A, 0x2F,
            0x015B, 0x30, 0x011E, 0x31, 0x0169, 0x32, 0x015C, 0x33, 0x015D, 0x34, 0x010B, 0x35, 0x0160, 0x36, 0x015F, 0x37,
            0x015E, 0x38, 0x010D, 0x39, 0x004C, 0x46, 0x004E, 0x47, 0x004D, 0x48, 0x0116, 0x49, 0x0118, 0x4A, 0x011A, 0x4B,
            0x0117, 0x4C, 0x0119, 0x4D, 0x011B, 0x4E, 0x00EB, 0x4F, 0x00EC, 0x50, 0x0151, 0x51, 0x0152, 0x52, 0x016A, 0x64,
            0x004F, 0x65, 0x010F, 0xE0, 0x010E, 0xE1, 0x018B, 0xE2, 0x018C, 0xE3, 0x0114, 0xE4, 0x0115, 0xE5, 0x018D, 0xE6
        };
        for (int at = 0; at < pairs.Length; at += 2) usages[pairs[at]] = pairs[at + 1];
        return usages;
    }

    public static byte[] Bitmap(int[] controls, IList<int> usages)
    {
        var bitmap = new byte[16];
        for (int index = 0; index < controls.Length && index < 128; index++)
        {
            int usage;
            if (ControlUsages.TryGetValue(controls[index], out usage) && usages.Contains(usage))
                bitmap[index / 8] |= (byte)(1 << index % 8);
        }
        return bitmap;
    }

    // Read once per connection.
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Hidpp, int[]> ControlLists =
        new System.Runtime.CompilerServices.ConditionalWeakTable<Hidpp, int[]>();

    static int[] Controls(Hidpp hidpp)
    {
        return ControlLists.GetValue(hidpp, connection =>
        {
            byte feature = connection.FeatureIndex(0x1B10);
            int count = Math.Min((int)connection.Call(feature, 0)[0], 128);
            var controls = new int[count];
            for (int at = 0; at < count; at += 8)
            {
                var reply = connection.Call(feature, 1, (byte)at);
                for (int i = 0; i < 8 && at + i < count; i++) controls[at + i] = reply[i * 2] << 8 | reply[i * 2 + 1];
            }
            return controls;
        });
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

    public static List<int> FromText(string text, bool controlBitmap)
    {
        var usages = new List<int>();
        if (text == null) return usages;
        for (int at = 0; at + 1 < text.Length; at += 2)
        {
            int usage;
            if (int.TryParse(text.Substring(at, 2), System.Globalization.NumberStyles.HexNumber, null, out usage)) usages.Add(usage);
        }
        return Clean(usages, controlBitmap);
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
