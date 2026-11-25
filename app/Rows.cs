using System.Windows.Forms;

// The layout Settings and the device screens share: titled boxes, one below the other, of rows Ui.RowHeight high, each a title and
// a hint at the left and its control at the right. Built in design units (100%), like the rest of a page.
static class Rows
{
    public const int Width = 616, Inset = 14, TitleGap = 22, SectionGap = 20;

    // A titled box below the one before; top moves past it.
    public static T Section<T>(Control parent, string title, ref int top, T box, int height) where T : Control
    {
        Label heading;
        return Section(parent, title, ref top, box, height, out heading);
    }

    public static T Section<T>(Control parent, string title, ref int top, T box, int height, out Label heading) where T : Control
    {
        heading = Ui.Text(parent, title, 20, top, () => Theme.Dim, 9f, Ui.Semibold);
        Ui.Add(parent, box, 20, top + TitleGap, Width, height);
        top += TitleGap + height + SectionGap;
        return box;
    }

    // The title and hint of the row at y, with a rule above it unless it's the box's first.
    public static RowText Row(Control box, int y, string title, string hint)
    {
        if (y > 0) Rule(box, y);
        return new RowText(Ui.Text(box, title, Inset, y + 12, () => Theme.Text, 9.75f, Ui.Semibold),
            Ui.Text(box, hint, Inset, y + 33, () => Theme.Dim, 9f));
    }

    public static void Rule(Control box, int y)
    {
        var line = Ui.Add(box, new Control(), Inset, y, Width - 2 * Inset, 1);
        Ui.Style(line, () => line.BackColor = Theme.Border);
    }

    // A control at the right of the row at y, centred in its height.
    public static T Place<T>(Control box, int y, T control, int width, int height) where T : Control
    {
        return Ui.Add(box, control, Width - Inset - width, y + (Ui.RowHeight - height) / 2, width, height);
    }

    // Every kind of control has one size in a row, so the same control looks the same on every screen.
    public const int ControlWidth = 240, ControlHeight = 32, ToggleWidth = 40, ToggleHeight = 22, SliderHeight = 24, ValueWidth = 64;

    public static Toggle Toggle(Control box, int y, string name)
    {
        return Place(box, y, new Toggle { AccessibleName = name }, ToggleWidth, ToggleHeight);
    }

    public static Dropdown Dropdown(Control box, int y, string name)
    {
        return Place(box, y, new Dropdown { AccessibleName = name }, ControlWidth, ControlHeight);
    }

    // Wider than the others when its labels need it, like the keyboard's six directions.
    public static SegmentedPicker Picker(Control box, int y, string name, string[] items, int width = ControlWidth)
    {
        return Place(box, y, new SegmentedPicker { AccessibleName = name, Items = items }, width, ControlHeight);
    }

    // A slider with its value at its left, as "100%".
    public static Slider Slider(Control box, int y, string name, out Label value)
    {
        value = Ui.Text(box, "", Width - Inset - ControlWidth - 8 - ValueWidth, y + 23, () => Theme.Dim, 9f);
        value.AutoSize = false;
        value.Size = new System.Drawing.Size(ValueWidth, 18);
        value.TextAlign = System.Drawing.ContentAlignment.TopRight;
        return Place(box, y, new Slider { AccessibleName = name, ZeroWhenOff = true }, ControlWidth, SliderHeight);
    }
}

class RowText
{
    public readonly Label Title, Hint;

    public RowText(Label title, Label hint) { Title = title; Hint = hint; }

    // A row whose setting doesn't apply now is greyed, both lines.
    public void Enable(bool on)
    {
        Title.ForeColor = on ? Theme.Text : Ui.Off;
        Hint.ForeColor = on ? Theme.Dim : Ui.Off;
    }
}
