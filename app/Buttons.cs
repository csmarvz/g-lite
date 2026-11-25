using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Drawing;
using System.Windows.Forms;

class ButtonAction
{
    public string Name;
    public byte[] Bytes;
    public ButtonAction(string name, string hex)
    {
        Name = name;
        Bytes = Enumerable.Range(0, 4).Select(i => Convert.ToByte(hex.Substring(i * 2, 2), 16)).ToArray();
    }
}

static class Bindings
{
    public static readonly ButtonAction[] MouseActions =
    {
        new ButtonAction("Left click", "80010001"), new ButtonAction("Right click", "80010002"), new ButtonAction("Middle click", "80010004"),
        new ButtonAction("Back", "80010008"), new ButtonAction("Forward", "80010010"),
        new ButtonAction("DPI up", "90030000"), new ButtonAction("DPI down", "90040000"),
        new ButtonAction("DPI cycle", "90050000"), new ButtonAction("DPI shift", "90070000"),
        new ButtonAction("Scroll left", "90010000"), new ButtonAction("Scroll right", "90020000"),
        new ButtonAction("Next profile", "90080000"), new ButtonAction("Previous profile", "90090000"),
        new ButtonAction("G-shift", "900B0000"), new ButtonAction("Battery level", "900C0000"),
        new ButtonAction("Play or pause", "800300CD"), new ButtonAction("Next track", "800300B5"),
        new ButtonAction("Previous track", "800300B6"),
        new ButtonAction("Volume up", "800300E9"), new ButtonAction("Volume down", "800300EA"), new ButtonAction("Mute", "800300E2"),
        new ButtonAction("Disabled", "80000000"),
    };

    // What a keyboard's G keys offer: the mouse's clicks, media keys and Disabled, without DPI, scrolling, profiles, G-shift and battery.
    public static readonly ButtonAction[] KeyboardActions = MouseActions.Where(action => action.Bytes[0] == 0x80).ToArray();

    static readonly string[] Modifiers = { "Ctrl", "Shift", "Alt", "Win" };

    public static bool HasLeft(byte[][] buttons, int count)
    {
        return buttons != null && buttons.Take(count).Any(binding => binding != null && binding.SequenceEqual(MouseActions[0].Bytes));
    }

    // Factory profiles store a special's last two bytes as FF FF (G502 HERO) or FF 00 (a G502 X LIGHTSPEED; a 409F on
    // MPM 30.00 has 00 00), G HUB as 00 00,
    // and "no action" as 80 00 FF FF (Solaar): only the first two bytes say what the button does (g502-hub's docs/protocol.md, facts only).
    // G HUB also writes "no action" as the special 90 00 (a G502 X's profile, docs/G502X.md; OMM's enums): shown as Disabled.
    public static int Find(ButtonAction[] actions, byte[] binding)
    {
        if (binding == null) return -1;
        int exact = Array.FindIndex(actions, action => action.Bytes.SequenceEqual(binding));
        if (exact >= 0 || binding.Length < 2 || !(binding[0] == 0x90 || (binding[0] == 0x80 && binding[1] == 0x00))) return exact;
        byte kind = binding[0], function = binding[1];
        if (kind == 0x90 && function == 0x00) kind = 0x80;
        return Array.FindIndex(actions, action => action.Bytes[0] == kind && action.Bytes[1] == function);
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
        int i = Find(MouseActions, binding);
        if (i >= 0) return MouseActions[i].Name;
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

    // The HID usage of a key Windows reports, for a keystroke binding; 0 for one G-Lite doesn't map.
    public static int Usage(Keys key)
    {
        if (key >= Keys.A && key <= Keys.Z) return 0x04 + (int)key - (int)Keys.A;
        if (key >= Keys.D1 && key <= Keys.D9) return 0x1E + (int)key - (int)Keys.D1;
        if (key >= Keys.F1 && key <= Keys.F12) return 0x3A + (int)key - (int)Keys.F1;
        switch (key)
        {
            case Keys.D0: return 0x27;
            case Keys.Enter: return 0x28;
            case Keys.Back: return 0x2A;
            case Keys.Tab: return 0x2B;
            case Keys.Space: return 0x2C;
            case Keys.OemMinus: return 0x2D;
            case Keys.Oemplus: return 0x2E;
            case Keys.OemOpenBrackets: return 0x2F;
            case Keys.OemCloseBrackets: return 0x30;
            case Keys.OemPipe: return 0x31;
            case Keys.OemSemicolon: return 0x33;
            case Keys.OemQuotes: return 0x34;
            case Keys.Oemtilde: return 0x35;
            case Keys.Oemcomma: return 0x36;
            case Keys.OemPeriod: return 0x37;
            case Keys.OemQuestion: return 0x38;
            case Keys.PrintScreen: return 0x46;
            case Keys.Scroll: return 0x47;
            case Keys.Pause: return 0x48;
            case Keys.Insert: return 0x49;
            case Keys.Home: return 0x4A;
            case Keys.PageUp: return 0x4B;
            case Keys.Delete: return 0x4C;
            case Keys.End: return 0x4D;
            case Keys.PageDown: return 0x4E;
            case Keys.Right: return 0x4F;
            case Keys.Left: return 0x50;
            case Keys.Down: return 0x51;
            case Keys.Up: return 0x52;
        }
        return 0;
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
        var known = found.IsG600
                // libratbag's and g600prog's order: G4 and G5 tilt the wheel, G6 is under the ring finger, G7 and G8 are behind the
                // wheel, G9 to G20 the thumb's keypad.
                ? new[] { "Left button", "Right button", "Wheel click", "Wheel tilt left", "Wheel tilt right", "Ring finger (G6)" }
                    .Concat(Enumerable.Range(7, 14).Select(number => "G" + number)).ToArray()
            : wpid == 0x407F
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

// A Bindings view's rows, one per button: its name and a dropdown of actions, and Keystroke…, which records the keys pressed next.
// The mouse and keyboard screens share it; the screen keeps the bindings and gets each change through Assigned.
class ButtonRows
{
    // A row each, in the rows layout (Rows): up to 20, the G600's.
    const int Max = 20;
    readonly Control parent;
    readonly ButtonAction[] actions;
    public readonly Label Hint;
    readonly Label[] rowNames = new Label[Max];
    readonly Dropdown[] rowDropdowns = new Dropdown[Max];
    readonly Control[] rules = new Control[Max];
    readonly TextField keyField;
    string[] names = new string[0];
    byte[][] bindings;
    bool editable;
    string idleHint = "";
    public int Recording = -1, Pressed = -1;
    public event Action<int, byte[]> Assigned;

    public ButtonRows(Control parent, ButtonAction[] actions, Label hint)
    {
        this.parent = parent;
        this.actions = actions;
        Hint = hint;
        var actionNames = actions.Select(action => action.Name).Concat(new[] { "Keystroke…" }).ToList();
        for (int i = 0; i < Max; i++)
        {
            int row = i, y = i * Ui.RowHeight;
            rowNames[i] = Ui.Text(parent, "", Rows.Inset, y + 22, () => Theme.Text, 9.75f, Ui.Semibold);
            rowDropdowns[i] = Rows.Place(parent, y, new Dropdown(), Rows.ControlWidth, Rows.ControlHeight);
            if (i > 0) rules[i] = Rows.Rule(parent, y);
            rowNames[i].Visible = rowDropdowns[i].Visible = false;
            if (rules[i] != null) rules[i].Visible = false;
            rowDropdowns[i].Items.AddRange(actionNames);
            rowDropdowns[i].Picked += actionIndex => Pick(row, actionIndex);
        }
        parent.Paint += (s, e) =>
        {
            if (Pressed < 0 || Pressed >= Max || !rowNames[Pressed].Visible) return;
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            // The parent is scaled with its page.
            float scale = parent.Width / (float)Rows.Width;
            Ui.Fill(e.Graphics, new RectangleF(6 * scale, (Pressed * Ui.RowHeight + 6) * scale, (Rows.Width - 12) * scale,
                (Ui.RowHeight - 12) * scale), 6 * scale, Theme.AccentBackground, Theme.AccentBackground);
        };
        keyField = Rows.Place(parent, 0, new TextField { Visible = false }, Rows.ControlWidth, Rows.ControlHeight);
        keyField.Box.KeyDown += (s, e) => { e.Handled = e.SuppressKeyPress = true; Recorded(e.KeyData); };
    }

    // Null bindings show no rows. hintText is the hint while nothing is being recorded.
    public void Show(string[] buttonNames, byte[][] buttonBindings, bool canEdit, string hintText)
    {
        names = buttonNames;
        bindings = buttonBindings;
        editable = canEdit;
        idleHint = hintText;
        Sync();
    }

    void Sync()
    {
        int count = bindings == null ? 0 : Math.Min(Max, Math.Min(names.Length, bindings.Length));
        for (int i = 0; i < Max; i++)
        {
            bool on = i < count;
            rowNames[i].Visible = on;
            rowDropdowns[i].Visible = on && i != Recording;
            if (rules[i] != null) rules[i].Visible = on;
            if (!on) continue;
            rowNames[i].Text = names[i];
            rowDropdowns[i].AccessibleName = names[i];
            int actionIndex = Bindings.Find(actions, bindings[i]);
            rowDropdowns[i].Index = actionIndex;
            if (actionIndex < 0) rowDropdowns[i].Text = Bindings.Describe(bindings[i]);
            rowDropdowns[i].Enabled = editable && Recording < 0;
        }
        parent.Invalidate(true);
        bool recording = Recording >= 0 && Recording < count;
        keyField.Visible = recording;
        if (recording) keyField.Top = rowDropdowns[Recording].Top;
        Hint.Text = Recording >= 0 ? "Press the keys, or Esc to cancel." : idleHint;
    }

    void Pick(int row, int actionIndex)
    {
        if (bindings == null) return;
        if (actionIndex >= actions.Length)
        {
            Recording = row;
            Sync();
            keyField.Box.Text = "";
            keyField.Box.Focus();
            return;
        }
        if (Assigned != null) Assigned(row, (byte[])actions[actionIndex].Bytes.Clone());
    }

    public void Recorded(Keys keys)
    {
        if (Recording < 0 || bindings == null) return;
        var key = keys & Keys.KeyCode;
        if (key == Keys.Escape) { Recording = -1; Sync(); return; }
        int usage = Bindings.Usage(key);
        if (usage == 0) return;
        int modifiers = ((keys & Keys.Control) != 0 ? 1 : 0) | ((keys & Keys.Shift) != 0 ? 2 : 0) | ((keys & Keys.Alt) != 0 ? 4 : 0);
        int row = Recording;
        Recording = -1;
        if (Assigned != null) Assigned(row, Bindings.Key(modifiers, usage));
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
