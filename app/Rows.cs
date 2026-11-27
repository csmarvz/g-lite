using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

// The layout Settings and the device screens share: titled sections one below the other, rows Ui.RowHeight high on the page (each
// section's box is plain), each a title with a hint after it on its line at the left and its control at the right edge. Built in
// design units (100%), like the rest of a page.
static class Rows
{
    public const int Width = 616, Inset = 0, TitleGap = 22, SectionGap = 28, TitleTop = 13;

    // A titled section below the one before; top moves past it. Its heading in small dim capitals, spaced by hair spaces (a label
    // has no letter spacing), read out as written.
    public static T Section<T>(Control parent, string title, ref int top, T box, int height) where T : Control
    {
        Label heading;
        return Section(parent, title, ref top, box, height, out heading);
    }

    public static T Section<T>(Control parent, string title, ref int top, T box, int height, out Label heading) where T : Control
    {
        heading = Ui.Text(parent, string.Join(" ", title.ToUpperInvariant().Select(character => character.ToString())), 20, top,
            () => Theme.Dim, 8.25f, Ui.Semibold);
        heading.AccessibleName = title;
        Ui.Add(parent, box, 20, top + TitleGap, Width, height);
        var rounded = box as RoundedBox;
        if (rounded != null) rounded.Plain = true;
        top += TitleGap + height + SectionGap;
        return box;
    }

    // The title and hint of the row at y. No line between rows: their height and the section's heading are enough.
    public static RowText Row(Control box, int y, string title, string hint)
    {
        return Title(box, y, title, hint);
    }

    // A row's title, and its hint after it on the same line, dim. The hint ends with an ellipsis before the row's control, the
    // whole text in a tooltip (AutoEllipsis), and moves with the title's width.
    public static RowText Title(Control box, int y, string title, string hint)
    {
        var titleLabel = Ui.Text(box, title, Inset, y + TitleTop, () => Theme.Text, 9.75f);
        var hintLabel = Ui.Text(box, hint, Inset, y + TitleTop + 1, () => Theme.Dim, 9f);
        hintLabel.AutoSize = false;
        hintLabel.AutoEllipsis = true;
        EventHandler fit = (s, e) => FitHint(box, titleLabel, hintLabel);
        // Once the controls have moved: the Layout event comes before the anchors, and the hint was fitted to where they were.
        var rounded = box as RoundedBox;
        if (rounded != null) rounded.LaidOut += () => FitHint(box, titleLabel, hintLabel);
        else box.Layout += (s, e) => FitHint(box, titleLabel, hintLabel);
        box.VisibleChanged += fit;
        titleLabel.SizeChanged += fit;
        hintLabel.TextChanged += fit;
        return new RowText(titleLabel, hintLabel);
    }

    // Visible reads false while the page is hidden, so a hidden box waits until it shows. A hidden control, like a row's that
    // RowLayout hid where it was built, doesn't count.
    static void FitHint(Control box, Label title, Label hint)
    {
        if (!box.Visible) return;
        float scale = Ui.DpiScale;
        int left = title.Text.Length == 0 ? title.Left : title.Right + (int)Math.Round(8 * scale), right = box.ClientSize.Width;
        int height = hint.PreferredHeight;
        foreach (Control control in box.Controls)
            if (control != title && control != hint && control.Visible && control.Left >= left && control.Top < hint.Top + height
                && control.Bottom > hint.Top)
                right = Math.Min(right, control.Left - (int)Math.Round(12 * scale));
        hint.SetBounds(left, hint.Top, Math.Max(0, Math.Min(hint.PreferredWidth, right - left)), height);
    }

    // A control at the right of the row at y, centred in its height.
    public static T Place<T>(Control box, int y, T control, int width, int height) where T : Control
    {
        return Ui.Add(box, control, Width - Inset - width, y + (Ui.RowHeight - height) / 2, width, height);
    }

    // Every kind of control has one size in a row, so the same control looks the same on every screen.
    public const int ControlWidth = 240, ControlHeight = 32, ToggleWidth = 40, ToggleHeight = 22, SliderHeight = 24, ValueWidth = 64,
        ButtonWidth = 120;

    public static FlatButton Button(Control box, int y, string text, string name)
    {
        return Place(box, y, new FlatButton { Text = text, AccessibleName = name }, ButtonWidth, ControlHeight);
    }

    // A paragraph across a box, as tall as its lines. Measured at 100% by a label like the one drawn (the page is built at 100% and
    // scaled after, while its fonts are already at the screen's scale).
    public static int NoteHeight(string text)
    {
        using (var font = WindowHelpers.PixelFont(9f, 1))
        using (var measure = new Label { Font = font, Text = text, UseMnemonic = false })
            return measure.GetPreferredSize(new System.Drawing.Size(Width - 2 * Inset, 0)).Height;
    }

    public static Label Note(Control box, int y, string text)
    {
        var label = Ui.Text(box, text, Inset, y, () => Theme.Dim, 9f);
        label.AutoSize = false;
        label.Size = new System.Drawing.Size(Width - 2 * Inset, NoteHeight(text));
        return label;
    }

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
        value = Ui.Text(box, "", Width - Inset - ControlWidth - 8 - ValueWidth, y + TitleTop + 1, () => Theme.Dim, 9f);
        value.AutoSize = false;
        value.Size = new System.Drawing.Size(ValueWidth, 18);
        value.TextAlign = System.Drawing.ContentAlignment.TopRight;
        return Place(box, y, new Slider { AccessibleName = name, ZeroWhenOff = true }, ControlWidth, SliderHeight);
    }
}

// Sections of rows that come and go, for a screen whose rows depend on the device (a mouse without lighting or Invert scrolling).
// Built in design units like the rest: Section starts a titled box, Row gives the y of its next row, where the row's controls go. Place
// stacks the shown rows and sections, moving their controls from where they were built, and returns the height they take; it's
// called first from the page's constructor, while everything is still where it was built. A row that shows again makes all its
// controls visible: a page that keeps one of them hidden (a swatch not in use) sets it after Place. (Remembering each control's
// Visible doesn't work: it reads false while the page itself is hidden, as it is when it loads in the tray.)
class RowLayout
{
    class RowSlot { public int Top, Height; public bool Shown = true, Placed = true; public readonly List<Control> Controls = new List<Control>(); }
    class SectionSlot { public Label Heading; public Control Box; public int Height; public bool Shown = true; public readonly List<RowSlot> Rows = new List<RowSlot>(); }
    readonly Control sheet;
    readonly List<SectionSlot> sections = new List<SectionSlot>();
    readonly Dictionary<Control, int> builtTop = new Dictionary<Control, int>();
    bool collected;

    public RowLayout(Control sheet) { this.sheet = sheet; }

    public RoundedBox Section(string title, out Label heading)
    {
        int top = 0;
        var box = Rows.Section(sheet, title, ref top, new RoundedBox(), 0, out heading);
        sections.Add(new SectionSlot { Heading = heading, Box = box });
        return box;
    }

    public int Row(Control box, int height = Ui.RowHeight)
    {
        var section = SectionOf(box);
        int y = section.Height;
        section.Rows.Add(new RowSlot { Top = y, Height = height });
        section.Height += height;
        return y;
    }

    public void ShowRow(Control box, int y, bool on) { SectionOf(box).Rows.First(row => row.Top == y).Shown = on; }

    public void ShowSection(Control box, bool on) { SectionOf(box).Shown = on; }

    SectionSlot SectionOf(Control box) { return sections.First(section => section.Box == box); }

    // scaled turns design units into the sheet's pixels; shown false hides every section (another view in their place).
    public int Place(Func<int, int> scaled, bool shown)
    {
        Collect();
        int top = 0;
        foreach (var section in sections)
        {
            bool sectionShown = shown && section.Shown && section.Rows.Any(row => row.Shown);
            section.Heading.Visible = section.Box.Visible = sectionShown;
            if (!sectionShown) continue;
            section.Heading.Top = scaled(top);
            int y = 0;
            foreach (var row in section.Rows)
            {
                if (row.Shown != row.Placed)
                {
                    foreach (var control in row.Controls) control.Visible = row.Shown;
                    row.Placed = row.Shown;
                }
                if (!row.Shown) continue;
                foreach (var control in row.Controls) control.Top = scaled(y + builtTop[control] - row.Top);
                y += row.Height;
            }
            section.Box.Top = scaled(top + Rows.TitleGap);
            section.Box.Height = scaled(y);
            top += Rows.TitleGap + y + Rows.SectionGap;
        }
        return top;
    }

    void Collect()
    {
        if (collected) return;
        collected = true;
        foreach (var section in sections)
            foreach (Control control in section.Box.Controls)
            {
                var row = section.Rows.LastOrDefault(candidate => candidate.Top <= control.Top);
                if (row == null) continue;
                row.Controls.Add(control);
                builtTop[control] = control.Top;
            }
    }
}

class RowText
{
    public readonly Label Title, Hint;

    public RowText(Label title, Label hint) { Title = title; Hint = hint; }

    // A row whose setting doesn't apply now is greyed, its title and hint.
    public void Enable(bool on)
    {
        Title.ForeColor = on ? Theme.Text : Ui.Off;
        Hint.ForeColor = on ? Theme.Dim : Ui.Off;
    }
}
