using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

class MouseAction
{
    public string Name;
    public byte[] Bytes;
    public MouseAction(string name, string hex)
    {
        Name = name;
        Bytes = Enumerable.Range(0, 4).Select(i => Convert.ToByte(hex.Substring(i * 2, 2), 16)).ToArray();
    }
}

static class Bindings
{
    public static readonly MouseAction[] Actions =
    {
        new MouseAction("Left click", "80010001"), new MouseAction("Right click", "80010002"), new MouseAction("Middle click", "80010004"),
        new MouseAction("Back", "80010008"), new MouseAction("Forward", "80010010"),
        new MouseAction("DPI up", "90030000"), new MouseAction("DPI down", "90040000"),
        new MouseAction("DPI cycle", "90050000"), new MouseAction("DPI shift", "90070000"),
        new MouseAction("Scroll left", "90010000"), new MouseAction("Scroll right", "90020000"),
        new MouseAction("Next profile", "90080000"), new MouseAction("Previous profile", "90090000"),
        new MouseAction("G-shift", "900B0000"), new MouseAction("Battery level", "900C0000"),
        new MouseAction("Play or pause", "800300CD"), new MouseAction("Next track", "800300B5"),
        new MouseAction("Previous track", "800300B6"),
        new MouseAction("Volume up", "800300E9"), new MouseAction("Volume down", "800300EA"), new MouseAction("Mute", "800300E2"),
        new MouseAction("Disabled", "80000000"),
    };

    static readonly string[] Modifiers = { "Ctrl", "Shift", "Alt", "Win" };

    public static bool HasLeft(byte[][] buttons, int count)
    {
        return buttons != null && buttons.Take(count).Any(binding => binding != null && binding.SequenceEqual(Actions[0].Bytes));
    }

    // Factory profiles store a special's last two bytes as FF FF (G502 HERO) or FF 00 (a G502 X LIGHTSPEED; a 409F on
    // MPM 30.00 has 00 00), G HUB as 00 00,
    // and "no action" as 80 00 FF FF (Solaar): only the first two bytes say what the button does (g502-hub's docs/protocol.md, facts only).
    public static int Find(byte[] binding)
    {
        if (binding == null) return -1;
        int exact = Array.FindIndex(Actions, action => action.Bytes.SequenceEqual(binding));
        if (exact >= 0 || binding.Length < 2 || !(binding[0] == 0x90 || (binding[0] == 0x80 && binding[1] == 0x00))) return exact;
        return Array.FindIndex(Actions, action => action.Bytes[0] == binding[0] && action.Bytes[1] == binding[1]);
    }

    static readonly Dictionary<int, string> Specials = new Dictionary<int, string>
    {
        { 0x06, "Default DPI" }, { 0x0A, "Cycle profile" }, { 0x0E, "Mode switch" }, { 0x0F, "Host button" },
        { 0x10, "Scroll down" }, { 0x11, "Scroll up" },
    };

    public static byte[] Key(int modifiers, int usage) { return new byte[] { 0x80, 0x02, (byte)modifiers, (byte)usage }; }

    public static string KeyName(int usage)
    {
        if (usage >= 0x04 && usage <= 0x1D) return ((char)('A' + usage - 0x04)).ToString();
        if (usage >= 0x1E && usage <= 0x26) return ((char)('1' + usage - 0x1E)).ToString();
        if (usage >= 0x3A && usage <= 0x45) return "F" + (usage - 0x39);
        var names = new Dictionary<int, string>
        {
            { 0x27, "0" }, { 0x28, "Enter" }, { 0x29, "Esc" }, { 0x2A, "Backspace" }, { 0x2B, "Tab" }, { 0x2C, "Space" },
            { 0x2D, "-" }, { 0x2E, "=" }, { 0x2F, "[" }, { 0x30, "]" }, { 0x31, "\\" }, { 0x33, ";" }, { 0x34, "'" }, { 0x35, "`" },
            { 0x36, "," }, { 0x37, "." }, { 0x38, "/" },
            { 0x39, "Caps Lock" }, { 0x46, "Print Screen" }, { 0x47, "Scroll Lock" }, { 0x48, "Pause" }, { 0x49, "Insert" },
            { 0x4A, "Home" }, { 0x4B, "Page Up" }, { 0x4C, "Delete" }, { 0x4D, "End" }, { 0x4E, "Page Down" },
            { 0x4F, "Right" }, { 0x50, "Left" }, { 0x51, "Down" }, { 0x52, "Up" },
        };
        string name;
        return names.TryGetValue(usage, out name) ? name : "key " + usage.ToString("X2");
    }

    public static string Describe(byte[] binding)
    {
        if (binding == null) return "";
        int i = Find(binding);
        if (i >= 0) return Actions[i].Name;
        if (binding[0] == 0x80 && binding[1] == 0x02)
        {
            var parts = Enumerable.Range(0, 8)
                .Where(bit => (binding[2] >> bit & 1) != 0)
                .Select(bit => Modifiers[bit % 4])
                .Distinct()
                .ToList();
            if (binding[3] != 0) parts.Add(KeyName(binding[3]));
            return string.Join(" + ", parts);
        }
        if (binding[0] == 0x80 && binding[1] == 0x01) return "Mouse button " + (binding[2] << 8 | binding[3]).ToString("X4");
        if (binding[0] == 0x80 && binding[1] == 0x03) return "Media key " + (binding[2] << 8 | binding[3]).ToString("X4");
        string special;
        if (binding[0] == 0x90 && Specials.TryGetValue(binding[1], out special)) return special;
        if (binding[0] == 0x90 && binding[1] == 0x0D) return "Profile " + binding[3];
        if (binding[0] == 0x90) return "Special " + binding[1].ToString("X2");
        if (binding[0] == 0x00) return "Macro";
        if (binding[0] == 0xFF) return "Not set";
        return BitConverter.ToString(binding).Replace("-", " ");
    }

    public static bool Same(byte[][] first, byte[][] second)
    {
        if (first == null || second == null) return first == second;
        return first.Length == second.Length && first.Zip(second, (x, y) => x.SequenceEqual(y)).All(x => x);
    }

    public static byte[][] Copy(byte[][] buttons)
    {
        return buttons == null ? null : buttons.Select(binding => (byte[])binding.Clone()).ToArray();
    }

    // The G502 HERO (USB C08B) has the G502 LIGHTSPEED's slots (g502-hub's docs/hardware-notes.md, from its button spy).
    public static string[] Names(FoundDevice found, int count)
    {
        int wpid = found.Models.Contains("C08B") ? 0x407F : found.WirelessId;
        var known = wpid == 0x407F
                ? new[]
                {
                    "Left button", "Right button", "Wheel click", "Thumb, back", "Thumb, front", "Sniper", "G7", "G8", "G9",
                    "Wheel tilt right", "Wheel tilt left"
                }
            // The G502 X LIGHTSPEED's slots, from its factory bindings (docs/reports/g502x-409f.txt): Back, DPI shift, Forward, Scroll left
            // and right, Cycle profile, DPI up and down.
            : wpid == 0x409F
                ? new[]
                {
                    "Left button", "Right button", "Wheel click", "Thumb, back (G4)", "Sniper (G6)", "Thumb, front (G5)",
                    "Wheel tilt left", "Wheel tilt right", "G9", "G8", "G7"
                }
            : wpid == 0x4079
                ? new[]
                {
                    "Left button", "Right button", "Wheel click", "Left side, back", "Left side, front", "DPI, underneath",
                    "Right side, back", "Right side, front"
                }
            : new[] { "Left button", "Right button", "Wheel click" };
        return Enumerable.Range(0, count).Select(i => i < known.Length ? known[i] : "Button " + (i + 1)).ToArray();
    }
}

class ButtonSpy : IDisposable
{
    readonly FileStream stream;
    volatile bool done;

    public ButtonSpy(string path, byte device, byte feature, Action<int> pressed)
    {
        var handle = Devices.Open(path);
        if (handle.IsInvalid) throw new IOException("Can't listen to the mouse's buttons");
        stream = new FileStream(handle, FileAccess.ReadWrite, 1, true);
        Task.Run(() =>
        {
            var buffer = new byte[20];
            while (!done)
            {
                int read;
                try { read = stream.Read(buffer, 0, 20); }
                catch (Exception) { return; }
                if (read >= 6 && buffer[1] == device && buffer[2] == feature && buffer[3] == 0x00) pressed(buffer[4] << 8 | buffer[5]);
            }
        });
    }

    public void Dispose() { done = true; stream.Dispose(); }
}
