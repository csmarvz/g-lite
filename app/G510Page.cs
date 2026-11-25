using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

class G510Page : DevicePage
{
    // Each M key's lighting, null until one is picked (the keyboard's colour as read is shown then), as LGS kept one per M key.
    readonly G510Settings[] lights = new G510Settings[G510Keys.Banks];
    G510Settings read, sent;
    // Whether the last load read the colour.
    bool answered;
    G510Settings Active { get { return lights[bank]; } set { lights[bank] = value; } }
    bool loading, ready, framing;
    int sequence;
    volatile int framed = -1;
    DateTime pausedUntil;
    readonly Stopwatch clock = Stopwatch.StartNew();
    readonly Timer debounce = new Timer { Interval = 120 }, animationTimer = new Timer { Interval = 50 };

    internal override bool Busy { get { return debounce.Enabled; } }

    readonly KeyPicture picture;
    readonly RowText effectText, colorText, brightText, speedText;
    readonly Dropdown effects;
    readonly ColorRow colors;
    readonly Slider bright, speed;
    readonly Label brightValue, speedValue;
    // Settings, Bindings and Game, each view's sections built from the sheet's top and shown one at a time, the picture above all three.
    const int SettingsView = 0, BindingsView = 1, GameView = 2, PictureHeight = 168, NotePadding = 14;
    int openView;
    readonly SegmentedPicker viewPick;
    readonly List<Control>[] viewParts = { new List<Control>(), new List<Control>(), new List<Control>() };
    readonly int[] viewHeights = new int[3];
    // The G keys' bindings, one set per M key, kept in settings (G510Keys); bank is the M key picked, M1 0.
    readonly byte[][][] gKeys;
    int bank;
    readonly SegmentedPicker bankPicker;
    readonly ButtonRows buttonRows;
    // The Game mode switch as last read, when the Game view opens.
    readonly Label gameState;
    string gameText = "";

    // M1's is where the one lighting was kept before the M keys had their own.
    string Remembered(int set) { return "lighting." + found.SettingsKey + (set == 0 ? "" : ".m" + (set + 1)); }
    string GKeysSetting(int set) { return "gkeys." + found.SettingsKey + ".m" + (set + 1); }

    public G510Page(FoundDevice found, Hidpp fake)
        : base(found, fake, found.ShortName, "keyboard", "profile." + found.SettingsKey + ".")
    {
        debounce.Tick += (s, e) => { debounce.Stop(); Send(); };
        animationTimer.Tick += (s, e) => Animate();

        // The picture stays above the rows, which scroll under it.
        picture = Ui.Add(this, new KeyPicture(KeyLayout.Load("g510.json")) { Live = true }, 20, BodyTop, Rows.Width, PictureHeight);
        var sheet = UseBody(BodyTop + PictureHeight + 12).Sheet;
        viewPick = Ui.Add(this, new SegmentedPicker { Items = new[] { "Settings", "Bindings", "Game" }, AccessibleName = "View" },
            636 - 192, 72, 192, 32);
        viewPick.Picked += ShowView;

        const int rowHeight = Ui.RowHeight;
        int top = 0, y = 0;
        Label heading;
        var light = Rows.Section(sheet, "Lighting", ref top, new RoundedBox(), 4 * rowHeight, out heading);
        viewParts[SettingsView].AddRange(new Control[] { heading, light });
        effectText = Rows.Row(light, y, "Effect", "");
        effects = Rows.Dropdown(light, y, "Effect");
        effects.Items.AddRange(G510Settings.Effects);
        // Breathe and Cycle are drawn by G-Lite, frame by frame (after Off and Static).
        effects.Badge = i => i > G510Settings.Static + 1 ? "Live" : null;
        effects.Picked += index => Edit(settings => { settings.On = index > 0; if (index > 0) settings.Effect = index - 1; }, true);
        y += rowHeight;
        colorText = Rows.Row(light, y, "Color", "One color for the whole keyboard");
        colors = new ColorRow(light, Rows.Width - Rows.Inset - ColorRow.Widest, y + (rowHeight - ColorRow.Size) / 2, true);
        colors.Picked += color => Edit(settings => settings.Color = color, true);
        y += rowHeight;
        brightText = Rows.Row(light, y, "Brightness", "How bright the keys are");
        bright = Rows.Slider(light, y, "Brightness", out brightValue);
        bright.Changed += (s, e) => { int brightness = Math.Max(1, bright.Value); Edit(settings => settings.Bright = brightness, false); };
        y += rowHeight;
        speedText = Rows.Row(light, y, "Speed", "How fast the effect moves");
        speed = Rows.Slider(light, y, "Speed", out speedValue);
        speed.Changed += (s, e) =>
        {
            int period = SpeedScale.Period(speed.Value, G510Settings.MinPeriod, G510Settings.MaxPeriod);
            Edit(settings => settings.Period = period, false);
        };
        string savingText = "G-Lite can't save to this keyboard yet. It keeps your lighting and sets it again when G-Lite starts and when "
            + "you open this screen.";
        var saving = Rows.Section(sheet, "Saving", ref top, new RoundedBox(), Rows.NoteHeight(savingText) + 2 * NotePadding, out heading);
        Rows.Note(saving, NotePadding, savingText);
        viewParts[SettingsView].AddRange(new Control[] { heading, saving });
        viewHeights[SettingsView] = top;

        gKeys = Enumerable.Range(0, G510Keys.Banks).Select(set => G510Keys.FromText(Settings.Get(GKeysSetting(set)))).ToArray();
        // In the footer like the G815's onboard profiles: an M key has its own lighting and G keys, as in LGS.
        bankPicker = Ui.Add(this, new SegmentedPicker { Items = new[] { "M1", "M2", "M3" }, AccessibleName = "M key" }, 264, 685, 144, 32);
        bankPicker.Picked += PickBank;
        FooterPicker = bankPicker;
        FooterPickerShown = true;
        InFooter(bankPicker);
        top = 0;
        int keysTop = top;
        var keysBox = Rows.Section(sheet, "G keys", ref top, new RoundedBox(), G510Keys.Count * rowHeight, out heading);
        // The hint on the title's line, at its right.
        buttonRows = new ButtonRows(keysBox, Bindings.KeyboardActions, Value(sheet, 220, keysTop, Rows.Width - 200));
        buttonRows.Assigned += (row, binding) =>
        {
            gKeys[bank][row] = binding;
            Settings.Set(GKeysSetting(bank), G510Keys.ToText(gKeys[bank]));
            LogUserAction(G510Keys.Names[row] + " in M" + (bank + 1) + ": " + Bindings.Describe(binding));
            RefreshControls();
        };
        viewParts[BindingsView].AddRange(new Control[] { heading, keysBox, buttonRows.Hint });
        viewHeights[BindingsView] = top;

        top = 0;
        string gameNote = "While its Game mode switch is on, the keyboard turns off its Windows keys itself. There are no other keys to pick.";
        int noteHeight = Rows.NoteHeight(gameNote) + 2 * NotePadding;
        var game = Rows.Section(sheet, "Game mode", ref top, new RoundedBox(), noteHeight + rowHeight, out heading);
        viewParts[GameView].AddRange(new Control[] { heading, game });
        Rows.Note(game, NotePadding, gameNote);
        Rows.Row(game, noteHeight, "Game mode switch", "Read from the keyboard when this view opens");
        gameState = Ui.Text(game, "", Rows.Width - Rows.Inset - 160, noteHeight + 22, () => Theme.Text, 9.75f, Ui.Semibold);
        gameState.AutoSize = false;
        gameState.Size = new Size(160, 20);
        gameState.TextAlign = ContentAlignment.TopRight;
        viewHeights[GameView] = top;

        for (int set = 0; set < lights.Length; set++) lights[set] = G510Settings.FromText(Settings.Get(Remembered(set)));
        RefreshControls();
        if (lights.Any(remembered => remembered != null)) Load();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { debounce.Dispose(); animationTimer.Dispose(); }
        base.Dispose(disposing);
    }

    public override void Restyle() { RefreshControls(); }

    // Every M key's lighting, so picking another M key doesn't change the preset.
    protected override string Current()
    {
        return lights.All(light => light == null) ? "" : string.Join("|", lights.Select(light => light == null ? "" : light.ToText()));
    }

    // A preset from before the M keys had their own lighting holds one: M1's, where it was kept. An empty one has none.
    protected override void ApplyPreset(string text)
    {
        var pieces = text.Split('|');
        if (pieces.Length == 1 && text != "")
        {
            var settings = G510Settings.FromText(text);
            if (settings == null) return;
            lights[0] = settings;
        }
        else for (int set = 0; set < lights.Length; set++) lights[set] = set < pieces.Length ? G510Settings.FromText(pieces[set]) : null;
        Changed(true);
    }

    // The M key's own lighting, sent with its light lit (feature 0x04, as LGS did); the G keys follow in the Bindings view.
    void PickBank(int index)
    {
        if (index < 0 || index == bank) return;
        bank = index;
        buttonRows.Recording = -1;
        LogUserAction("M" + (bank + 1));
        RefreshControls();
        if (!ready) return;
        int set = bank;
        RunOnDevice(hidpp => G510Keys.LightBank(hidpp, set));
        Send();
    }

    protected override bool Unsaved { get { return ready && Active != null && read != null && !Active.Same(read); } }

    protected override void Lost() { sent = null; framed = -1; }

    protected override void Revert()
    {
        if (read == null) return;
        Active = null;
        KeepLights();
        RefreshControls();
        Send(read.Clone());
    }

    void ShowView(int view)
    {
        openView = view;
        buttonRows.Recording = -1;
        RefreshControls();
        if (view != GameView) return;
        string state = null;
        RunOnDevice(hidpp =>
        {
            try { state = G510Keys.GameMode(hidpp) ? "On" : "Off"; }
            catch (IOException) { state = "Unknown"; }
        }, () => { gameText = state; RefreshControls(); });
    }

    void Edit(Action<G510Settings> change, bool now)
    {
        if (loading || !ready) return;
        var settings = (Active ?? read ?? new G510Settings()).Clone();
        change(settings);
        Active = settings;
        Changed(now);
    }

    void Changed(bool now)
    {
        sequence++;
        Edited();
        RefreshControls();
        // While a slider moves, sent at most every 120 ms rather than once it stops.
        if (now) { debounce.Stop(); Send(); }
        else if (!debounce.Enabled) debounce.Start();
    }

    void RefreshControls()
    {
        loading = true;
        for (int view = 0; view < viewParts.Length; view++)
            foreach (var control in viewParts[view]) control.Visible = view == openView;
        FitBody(viewHeights[openView]);
        viewPick.Index = openView;
        viewPick.Invalidate();

        var settings = Active ?? read;
        bool known = ready && settings != null, lit = known && settings.On,
            color = lit && settings.Effect != G510Settings.Cycle, moving = lit && settings.Effect != G510Settings.Static;
        effectText.Enable(ready);
        effectText.Hint.Text = moving ? "Drawn by G-Lite: it stops when you quit G-Lite" : "What the lighting does";
        effects.Enabled = ready;
        effects.Index = known ? (settings.On ? settings.Effect + 1 : 0) : -1;
        effects.Invalidate();
        colorText.Enable(color);
        colors.RefreshControls(known ? settings.Color : 0xFFFFFF, color);
        brightText.Enable(lit);
        bright.Enabled = lit;
        bright.Value = known ? settings.Bright : 100;
        brightValue.Text = lit ? settings.Bright + "%" : "";
        bright.Invalidate();
        speedText.Enable(moving);
        speed.Enabled = moving;
        speed.Value = known ? SpeedScale.Percent(settings.Period, G510Settings.MinPeriod, G510Settings.MaxPeriod) : 0;
        speedValue.Text = moving ? speed.Value + "%" : "";
        speed.Invalidate();

        bankPicker.Index = bank;
        bankPicker.Invalidate();
        buttonRows.Show(G510Keys.Names, gKeys[bank], true, "Not working yet: G-Lite doesn't read the G keys yet");
        gameState.Text = gameText;
        ShowFrame();
        loading = false;
        Animation();
    }

    // Every backlit key in the colour the keyboard shows, Breathe and Cycle frame by frame.
    void ShowFrame()
    {
        var settings = Active ?? read;
        int frame = ready && settings != null ? G510Lighting.Frame(settings, clock.Elapsed.TotalMilliseconds) : 0;
        int red = frame >> 16 & 0xFF, green = frame >> 8 & 0xFF, blue = frame & 0xFF, max = Math.Max(red, Math.Max(green, blue));
        var shown = max == 0 ? Theme.KeyOff
            : Ui.Mix(Theme.KeyOff, Color.FromArgb(red * 255 / max, green * 255 / max, blue * 255 / max), max / 255.0);
        var lit = Enumerable.Range(0, picture.Count).Select(key => picture.IdOf(key) >= 0 ? shown : Theme.KeyOff).ToArray();
        picture.ShowLive(lit, new bool[picture.Count]);
    }

    bool Animated { get { return ready && Active != null && Active.On && Active.Effect != G510Settings.Static; } }

    void Animation()
    {
        bool on = Animated && !Manual;
        if (on && !animationTimer.Enabled) animationTimer.Start();
        if (!on && animationTimer.Enabled) animationTimer.Stop();
    }

    void Animate()
    {
        if (IsDisposed || !Animated) return;
        ShowFrame();
        if (framing || DateTime.UtcNow < pausedUntil) return;
        int frame = G510Lighting.Frame(Active, clock.Elapsed.TotalMilliseconds);
        if (frame == framed) return;
        framing = true;
        RunOnDevice(hidpp =>
        {
            try { G510Lighting.Set(hidpp, frame, true); framed = frame; }
            finally { framing = false; }
        }, null, message =>
        {
            // A refused frame would otherwise fail, and be logged, 20 times a second.
            framing = false;
            framed = -1;
            pausedUntil = DateTime.UtcNow.AddSeconds(10);
            ShowStatus("The keyboard refused the effect's colors: " + message, true);
        });
    }

    protected override void ClearStatus()
    {
        if (ready && !answered) { ShowStatus("The keyboard didn't answer. Is it plugged in?", true, Load, "Try again"); return; }
        ShowStatus(Active == null ? "" : "Set by G-Lite, not saved to the keyboard");
    }

    void KeepLights()
    {
        for (int set = 0; set < lights.Length; set++)
            if (lights[set] == null) Settings.Remove(Remembered(set)); else Settings.Set(Remembered(set), lights[set].ToText());
    }

    // An M key without its own lighting goes back to the colour read.
    void Send()
    {
        KeepLights();
        if (Active != null) Send(Active.Clone());
        else if (read != null) Send(read.Clone());
    }

    // Breathe and Cycle are sent by Animate, frame by frame.
    void Send(G510Settings want)
    {
        int startedAt = ++sequence;
        framed = -1;
        pausedUntil = DateTime.MinValue;
        if (want.On && want.Effect != G510Settings.Static)
        {
            RunOnDevice(hidpp => sent = null, () => { if (startedAt == sequence) ClearStatus(); });
            return;
        }
        RunOnDevice(hidpp =>
        {
            if (startedAt != sequence || want.Same(sent)) return;
            G510Lighting.Set(hidpp, G510Lighting.Frame(want, 0));
            sent = want;
        }, () => { if (startedAt == sequence) ClearStatus(); });
    }

    public override void Load()
    {
        debounce.Stop();
        ShowStatus("Reading the keyboard…");
        var wanted = lights.Select(light => light == null ? null : light.Clone()).ToArray();
        int color = -1, lit = bank;
        RunOnDevice(hidpp =>
        {
            sent = null;
            framed = -1;
            // Starts on the M key the keyboard has lit.
            try { lit = G510Keys.LitBank(hidpp, lit); }
            catch (IOException) { }
            var want = wanted[lit];
            try { color = G510Lighting.Read(hidpp); }
            catch (IOException) { color = -1; }
            if (want != null && color >= 0 && (!want.On || want.Effect == G510Settings.Static))
            {
                G510Lighting.Set(hidpp, G510Lighting.Frame(want, 0));
                sent = want;
            }
        }, () =>
        {
            ready = true;
            bank = lit;
            answered = color >= 0;
            // Only the first: later loads read what G-Lite set, which would pass one M key's colour to another.
            if (read == null && answered) read = new G510Settings { On = color != 0, Color = color == 0 ? 0xFFFFFF : color, Bright = 100 };
            RefreshControls();
            Loaded();
        });
    }
}
