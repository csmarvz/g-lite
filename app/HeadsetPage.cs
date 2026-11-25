using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

// The headset, centred. Its battery shows under the device's name, so the picture doesn't repeat it.
class HeadsetPicture : DrawnControl
{
    public HeadsetPicture()
    {
        Cursor = Cursors.Default;
        TabStop = false;
        AccessibleRole = AccessibleRole.Graphic;
        AccessibleName = "Headset";
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var graphics = Begin(e);
        float height = Height - 16 * DpiScale, width = height * 0.9f, y = 8 * DpiScale, x = (Width - width) / 2;
        float cupW = width * 0.26f, cupH = height * 0.42f, cupY = y + height * 0.5f;
        using (var band = new Pen(Theme.KeyOff, 9 * DpiScale) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            graphics.DrawArc(band, x + cupW / 2, y + 4 * DpiScale, width - cupW, height * 1.05f, 180, 180);
        using (var boom = new Pen(Theme.KeyOff, 5 * DpiScale) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            graphics.DrawBezier(boom, x + cupW * 0.6f, cupY + cupH * 0.8f, x + cupW * 0.4f, y + height * 1.02f,
                x + width * 0.28f, y + height * 0.98f, x + width * 0.4f, y + height * 0.96f);
        foreach (var cupX in new[] { x, x + width - cupW })
        {
            Ui.Fill(graphics, new RectangleF(cupX, cupY, cupW, cupH), cupW * 0.42f, Theme.KeyOff, Theme.Border);
            Ui.Fill(graphics, new RectangleF(cupX + cupW * 0.22f, cupY + cupH * 0.16f, cupW * 0.56f, cupH * 0.68f),
                cupW * 0.26f, Theme.PictureBackground, Theme.PictureBackground);
        }
        using (var brush = new SolidBrush(Theme.Accent))
            graphics.FillEllipse(brush, x + width * 0.4f - 4 * DpiScale, y + height * 0.96f - 4 * DpiScale, 8 * DpiScale, 8 * DpiScale);
    }
}

class HeadsetPage : DevicePage
{
    HeadsetSettings current = new HeadsetSettings(), stored, sent;
    HeadsetProtocol.State state;
    bool loading, dirty;
    int sequence;
    int[] offMinutes = HeadsetProtocol.OffMinutes;
    readonly Timer debounce = new Timer { Interval = 120 }, testing = new Timer { Interval = 100 };

    internal override bool Busy { get { return debounce.Enabled; } }
    MicrophoneCapture microphone;
    DateTime testEnd;

    readonly HeadsetPicture picture;
    readonly Slider sidetone;
    readonly Label sideValue;
    readonly FlatButton testButton, flatButton;
    readonly Dropdown offBox;
    // Sound, Power and the equalizer (experimental), in design units; rows the headset doesn't have go once it's read. The equalizer
    // has a row for each band up to MaxBands, those past the headset's count left out, and shows only once read.
    readonly RowLayout rows;
    readonly RoundedBox sound, power, equalizer;
    readonly int sideRow, testRow;
    readonly RowText sideText, testText, offText;
    const int PictureHeight = 168, NotePadding = 14, MaxBands = 15;
    // The bands last written to the EEPROM too (persistence 1); a drag writes RAM only, then these once let go. Device queue only.
    int[] keptBands;
    readonly int[] bandRows = new int[MaxBands];
    readonly RowText[] bandTexts = new RowText[MaxBands];
    readonly Slider[] bandSliders = new Slider[MaxBands];
    readonly Label[] bandValues = new Label[MaxBands];

    public HeadsetPage(FoundDevice found, Hidpp fake)
        : base(found, fake, found.ShortName, "headset", "profile." + found.SettingsKey + ".")
    {
        debounce.Tick += (s, e) => { debounce.Stop(); Send(); };

        // The picture stays above the rows, which scroll under it.
        picture = Ui.Add(this, new HeadsetPicture(), 20, BodyTop, Rows.Width, PictureHeight);
        var sheet = UseBody(BodyTop + PictureHeight + 12).Sheet;
        rows = new RowLayout(sheet);

        Label heading;
        sound = rows.Section("Sound", out heading);
        sideRow = rows.Row(sound);
        sideText = Rows.Row(sound, sideRow, "Sidetone", "Your own voice, while an app uses the microphone");
        sidetone = Rows.Slider(sound, sideRow, "Sidetone", out sideValue);
        sidetone.Changed += (s, e) => { current.Sidetone = sidetone.Value; Changed(false); };
        testRow = rows.Row(sound);
        testText = Rows.Row(sound, testRow, "Test sidetone", "Hear yourself for 30 s");
        testButton = Rows.Button(sound, testRow, "Test", "Test sidetone");
        testButton.Click += (s, e) => { if (microphone == null) StartTest(); else StopTest(); };
        testing.Tick += (s, e) =>
        {
            if (microphone == null) return;
            if (DateTime.UtcNow > testEnd) { StopTest(); return; }
            microphone.RequeueBuffers();
        };

        power = rows.Section("Power", out heading);
        int offRow = rows.Row(power);
        offText = Rows.Row(power, offRow, "Turn off after", "When the headset isn't used");
        offBox = Rows.Dropdown(power, offRow, "Turn off after");
        offBox.Picked += i => { current.AutoOffMinutes = offMinutes[i]; Changed(true); };

        equalizer = rows.Section("Equalizer", out heading);
        string note = "Experimental. The headset stores these bands, but it hasn't been heard to play them yet: G HUB applies its "
            + "equalizer on the PC. Changes are sent to the headset right away.";
        int noteRow = rows.Row(equalizer, Rows.NoteHeight(note) + 2 * NotePadding);
        Rows.Note(equalizer, noteRow + NotePadding, note);
        for (int i = 0; i < MaxBands; i++)
        {
            int band = i;
            bandRows[i] = rows.Row(equalizer);
            bandTexts[i] = Rows.Row(equalizer, bandRows[i], "", "");
            bandSliders[i] = Rows.Slider(equalizer, bandRows[i], "Band " + (i + 1), out bandValues[i]);
            bandSliders[i].Step = 1;
            bandSliders[i].Changed += (s, e) =>
            {
                if (loading || current.Bands == null || band >= current.Bands.Length) return;
                current.Bands[band] = bandSliders[band].Value - state.Range;
                Changed(false);
            };
            bandSliders[i].MouseUp += (s, e) => { if (state != null) { debounce.Stop(); Send(); } };
        }
        int flatRow = rows.Row(equalizer);
        Rows.Row(equalizer, flatRow, "Flat", "Every band back to 0 dB");
        flatButton = Rows.Button(equalizer, flatRow, "Flat", "Set every band to 0 dB");
        flatButton.Click += (s, e) =>
        {
            if (current.Bands == null) return;
            current.Bands = new int[current.Bands.Length];
            Changed(true);
        };

        RefreshControls();
    }

    static string OffText(int minutes) { return minutes == 0 ? "Never" : minutes + " min"; }

    static string FrequencyText(int hertz)
    {
        return hertz >= 1000 ? (hertz / 1000.0).ToString("0.#", CultureInfo.InvariantCulture) + " kHz" : hertz + " Hz";
    }

    static string RangeText(int hertz)
    {
        return hertz < 50 ? "Deep bass" : hertz < 200 ? "Bass" : hertz < 400 ? "Low mids" : hertz < 1500 ? "Mids"
            : hertz < 3000 ? "Upper mids" : hertz < 6000 ? "Presence" : hertz < 12000 ? "Treble" : "Air";
    }

    void StartTest()
    {
        int microphoneIndex = MicrophoneCapture.Find(found.ShortName);
        if (microphoneIndex < 0) { ShowStatus("Couldn't find the headset's microphone in Windows", true); return; }
        try { microphone = new MicrophoneCapture(microphoneIndex); }
        catch (Exception ex) { ShowStatus(ex.Message, true); return; }
        testEnd = DateTime.UtcNow.AddSeconds(30);
        testing.Start();
        testButton.Text = "Stop";
        testButton.Style = ButtonStyle.Primary;
        testButton.Invalidate();
        ShowStatus("Talk now. You hear yourself at the sidetone level. The test stops after 30 s.");
    }

    void StopTest()
    {
        testing.Stop();
        if (microphone != null) { microphone.Dispose(); microphone = null; }
        testButton.Text = "Test";
        testButton.Style = ButtonStyle.Normal;
        testButton.Invalidate();
        ClearStatus();
    }

    public override void Hidden() { if (microphone != null) StopTest(); }

    protected override void Dispose(bool disposing)
    {
        if (disposing && microphone != null) StopTest();
        base.Dispose(disposing);
    }

    public override void Restyle() { RefreshControls(); }

    protected override bool Unsaved { get { return dirty; } }

    protected override string Current() { return current.ToText(); }

    // A preset's bands count only on a headset with as many; otherwise the bands stay as they are.
    protected override void ApplyPreset(string text)
    {
        var settings = HeadsetSettings.FromText(text);
        settings.Sidetone = Math.Max(0, Math.Min(100, settings.Sidetone));
        int range = state != null ? state.Range : 0;
        if (settings.Bands == null || current.Bands == null || settings.Bands.Length != current.Bands.Length)
            settings.Bands = current.Bands == null ? null : (int[])current.Bands.Clone();
        else settings.Bands = settings.Bands.Select(band => Math.Max(-range, Math.Min(range, band))).ToArray();
        current = settings;
        Changed(true);
    }

    protected override void Revert()
    {
        if (stored == null) return;
        current = stored.Clone();
        Changed(true);
        dirty = false;
        ClearStatus();
    }

    void RefreshControls()
    {
        loading = true;
        bool ready = state != null, hasSide = ready && state.HasSidetone, hasOff = ready && state.HasOff,
            hasBands = ready && state.Frequencies != null && current.Bands != null;
        int bandCount = hasBands ? Math.Min(MaxBands, state.Frequencies.Length) : 0;
        // Before the headset is read every row shows but the equalizer's, whose bands it says.
        rows.ShowRow(sound, sideRow, !ready || hasSide);
        rows.ShowRow(sound, testRow, !ready || hasSide);
        rows.ShowSection(power, !ready || hasOff);
        rows.ShowSection(equalizer, hasBands);
        for (int i = 0; i < MaxBands; i++) rows.ShowRow(equalizer, bandRows[i], i < bandCount);
        FitBody(rows.Place(Scaled, true));

        sideText.Enable(hasSide);
        sidetone.Enabled = hasSide;
        sidetone.Value = current.Sidetone;
        sideValue.Text = hasSide ? current.Sidetone + "%" : "";
        sidetone.Invalidate();
        testText.Enable(hasSide);
        testButton.Enabled = hasSide;
        offMinutes = current.AutoOffMinutes >= 0 && !HeadsetProtocol.OffMinutes.Contains(current.AutoOffMinutes)
            ? HeadsetProtocol.OffMinutes.Concat(new[] { current.AutoOffMinutes }).OrderBy(minutes => minutes).ToArray()
            : HeadsetProtocol.OffMinutes;
        offBox.Items.Clear();
        offBox.Items.AddRange(offMinutes.Select(OffText));
        offBox.Index = hasOff ? Array.IndexOf(offMinutes, current.AutoOffMinutes) : -1;
        offBox.Enabled = hasOff;
        offBox.Invalidate();
        offText.Enable(hasOff);
        for (int i = 0; i < bandCount; i++)
        {
            int hertz = state.Frequencies[i], band = current.Bands[i];
            bandTexts[i].Title.Text = FrequencyText(hertz);
            bandTexts[i].Hint.Text = RangeText(hertz);
            bandSliders[i].AccessibleName = FrequencyText(hertz);
            bandSliders[i].Max = 2 * state.Range;
            if (!bandSliders[i].Capture) bandSliders[i].Value = band + state.Range;
            bandValues[i].Text = (band > 0 ? "+" : "") + band + " dB";
            bandSliders[i].Invalidate();
        }
        flatButton.Enabled = hasBands && current.Bands.Any(band => band != 0);
        loading = false;
    }

    void Changed(bool now)
    {
        if (loading || state == null) return;
        dirty = true;
        sequence++;
        Edited();
        RefreshControls();
        ClearStatus();
        // While a slider moves, sent at most every 120 ms rather than once it stops: restarting the timer on each move sent nothing
        // during a drag.
        if (now) { debounce.Stop(); Send(); }
        else if (!debounce.Enabled) debounce.Start();
    }

    protected override void ClearStatus()
    {
        if (microphone != null) return;
        if (state != null && !state.HasSidetone && !state.HasOff && state.Millivolts == 0)
        {
            ShowStatus("The headset didn't answer. Is it on?", true, Load, "Try again");
            return;
        }
        ShowStatus(state == null ? "" : dirty ? "Applied to the headset" : "Matches the headset");
    }

    void Send()
    {
        var want = current.Clone();
        var deviceState = state;
        bool held = bandSliders.Any(slider => slider.Capture);
        int startedAt = ++sequence;
        RunOnDevice(hidpp =>
        {
            if (startedAt != sequence) return;
            if (deviceState.HasSidetone && (sent == null || want.Sidetone != sent.Sidetone))
                HeadsetProtocol.SetSidetone(hidpp, want.Sidetone);
            if (deviceState.HasOff && want.AutoOffMinutes >= 0 && (sent == null || want.AutoOffMinutes != sent.AutoOffMinutes))
                HeadsetProtocol.SetOff(hidpp, want.AutoOffMinutes);
            if (deviceState.Frequencies != null && want.Bands != null
                && (sent == null || !want.SameBands(sent) || !held && (keptBands == null || !want.Bands.SequenceEqual(keptBands))))
            {
                HeadsetProtocol.SetEqualizer(hidpp, want.Bands, !held);
                if (!held) keptBands = (int[])want.Bands.Clone();
            }
            sent = want;
        }, () => { if (startedAt == sequence) ClearStatus(); });
    }

    public override void Load()
    {
        debounce.Stop();
        ShowStatus("Reading the headset…");
        HeadsetProtocol.State read = null;
        int startedAt = sequence;
        bool autoOff = Demo || found.HasAutoOff;
        RunOnDevice(hidpp =>
        {
            read = HeadsetProtocol.Read(hidpp, autoOff);
            keptBands = read.Set.Bands == null ? null : (int[])read.Set.Bands.Clone();
        }, () =>
        {
            state = read;
            stored = read.Set.Clone();
            if (startedAt == sequence)
            {
                current = read.Set.Clone();
                sent = read.Set.Clone();
                dirty = false;
            }
            RefreshControls();
            Loaded();
        });
    }
}
