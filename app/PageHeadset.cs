using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

class HeadsetPicture : DrawnControl
{
    int battery = -1, millivolts;
    bool approx, charging;
    string line = "";

    public HeadsetPicture()
    {
        Cursor = Cursors.Default;
        TabStop = false;
        AccessibleRole = AccessibleRole.Graphic;
        AccessibleName = "Headset";
    }

    public void Display(int percent, bool estimated, int newMillivolts, bool isCharging, string text)
    {
        battery = percent;
        approx = estimated;
        millivolts = newMillivolts;
        charging = isCharging;
        line = text;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var graphics = Begin(e);
        float height = Height - 16 * DpiScale, width = height * 0.9f, x = 62 * DpiScale, y = 8 * DpiScale;
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

        float textX = x + width + 70 * DpiScale, top = Height / 2f - 46 * DpiScale;
        var bigFont = Ui.Get(27f, Ui.Semibold);
        var text = battery < 0 ? "–" : battery + "%";
        Ui.Draw(graphics, text, bigFont, new RectangleF(textX, top, Width - textX, 48 * DpiScale),
            Theme.Text, TextFormatFlags.Left | TextFormatFlags.Bottom);
        float percentWidth = TextRenderer.MeasureText(graphics, text, bigFont, Size.Empty, TextFormatFlags.NoPadding).Width;
        Ui.Draw(graphics, charging ? "charging" : "battery", Ui.Get(11f, Ui.Semibold),
            new RectangleF(textX + percentWidth + 8 * DpiScale, top, 120 * DpiScale, 44 * DpiScale),
            Theme.Dim, TextFormatFlags.Left | TextFormatFlags.Bottom);
        var small = Ui.Get(9.75f, Ui.Regular);
        var volts = millivolts > 0
            ? (millivolts / 1000.0).ToString("0.00", CultureInfo.InvariantCulture) + " V" + (approx ? ", estimated from the voltage" : "")
            : "";
        Ui.Draw(graphics, volts, small, new RectangleF(textX, top + 58 * DpiScale, Width - textX, 20 * DpiScale),
            Theme.Dim, TextFormatFlags.Left);
        Ui.Draw(graphics, line, small, new RectangleF(textX, top + 80 * DpiScale, Width - textX, 20 * DpiScale),
            Theme.Dim, TextFormatFlags.Left);
    }
}

class PageHeadset : DevicePage
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

    readonly Label sideHead, sideValue, offHead, firmware;
    readonly FlatButton testButton;
    readonly Slider sidetone;
    readonly Dropdown offBox;
    readonly HeadsetPicture picture;

    public PageHeadset(FoundDevice found, Hidpp fake, MouseEventHandler drag)
        : base(found, fake, drag, found.ShortName, "headset", "profile." + found.SettingsKey + ".")
    {
        debounce.Tick += (s, e) => { debounce.Stop(); Send(); };

        var pictureBox = Ui.Add(this, new RoundedBox(), 20, 118, 616, 209);
        Ui.Text(pictureBox, "Headset", 14, 12, () => Theme.Dim, 9f, Ui.Semibold);
        picture = Ui.Add(pictureBox, new HeadsetPicture(), 14, 36, 588, 161);

        var sound = Ui.Add(this, new RoundedBox(), 20, 339, 380, 272);
        Ui.Text(sound, "Sound", 14, 12, () => Theme.Dim, 9f, Ui.Semibold);
        sideHead = Ui.Text(sound, "Sidetone", 14, 40, () => Theme.Dim, 9f);
        sideValue = Value(sound, 14, 40, 352);
        sidetone = Ui.Add(sound, new Slider { AccessibleName = "Sidetone" }, 14, 60, 352, 24);
        sidetone.Changed += (s, e) => { current.Sidetone = sidetone.Value; Changed(false); };
        var hint = Ui.Text(sound,
            "How much of your own voice you hear while you talk. It works while an app is using the microphone, like a call or a voice chat.",
            14, 92, () => Theme.Dim, 9f);
        hint.AutoSize = false;
        hint.Size = new Size(352, 54);
        testButton = Ui.Add(sound, new FlatButton { Text = "Test sidetone", Icon = "headset" }, 14, 150, 150, 32);
        testButton.Click += (s, e) => { if (microphone == null) StartTest(); else StopTest(); };
        testing.Tick += (s, e) =>
        {
            if (microphone == null) return;
            if (DateTime.UtcNow > testEnd) { StopTest(); return; }
            microphone.RequeueBuffers();
        };

        var power = Ui.Add(this, new RoundedBox(), 412, 339, 224, 272);
        Ui.Text(power, "Power", 14, 12, () => Theme.Dim, 9f, Ui.Semibold);
        offHead = Ui.Text(power, "Turn off after", 14, 40, () => Theme.Dim, 9f);
        offBox = Ui.Add(power, new Dropdown { AccessibleName = "Turn off after" }, 14, 58, 196, 32);
        offBox.Picked += i => { current.AutoOffMinutes = offMinutes[i]; Changed(true); };
        Ui.Text(power, "Firmware", 14, 98, () => Theme.Dim, 9f);
        firmware = Ui.Text(power, "", 14, 116, () => Theme.Text, 9.75f);
        firmware.AutoSize = false;
        firmware.AutoEllipsis = true;
        firmware.Size = new Size(196, 22);
        var note = Ui.Text(power, "Changes apply right away.", 14, 156, () => Theme.Dim, 9f);
        note.AutoSize = false;
        note.Size = new Size(196, 48);

        RefreshControls();
    }

    static string OffText(int minutes) { return minutes == 0 ? "Never" : minutes + " min"; }

    void StartTest()
    {
        int microphoneIndex = MicrophoneCapture.Find(found.ShortName);
        if (microphoneIndex < 0) { ShowStatus("Couldn't find the headset's microphone in Windows", true); return; }
        try { microphone = new MicrophoneCapture(microphoneIndex); }
        catch (Exception ex) { ShowStatus(ex.Message, true); return; }
        testEnd = DateTime.UtcNow.AddSeconds(30);
        testing.Start();
        testButton.Text = "Stop the test";
        testButton.Style = ButtonStyle.Primary;
        testButton.Invalidate();
        ShowStatus("Talk now. You hear yourself at the sidetone level. The test stops after 30 s.");
    }

    void StopTest()
    {
        testing.Stop();
        if (microphone != null) { microphone.Dispose(); microphone = null; }
        testButton.Text = "Test sidetone";
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

    protected override void ApplyPreset(string text)
    {
        var settings = HeadsetSettings.FromText(text);
        settings.Sidetone = Math.Max(0, Math.Min(100, settings.Sidetone));
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
        bool ready = state != null, hasSide = ready && state.HasSidetone, hasOff = ready && state.HasOff;
        SetDimmed(sideHead, hasSide);
        sidetone.Enabled = hasSide;
        testButton.Enabled = hasSide;
        sidetone.Value = current.Sidetone;
        sideValue.Text = hasSide ? current.Sidetone + "%" : "";
        sidetone.Invalidate();
        offMinutes = current.AutoOffMinutes >= 0 && !HeadsetProtocol.OffMinutes.Contains(current.AutoOffMinutes)
            ? HeadsetProtocol.OffMinutes.Concat(new[] { current.AutoOffMinutes }).OrderBy(minutes => minutes).ToArray()
            : HeadsetProtocol.OffMinutes;
        offBox.Items.Clear();
        offBox.Items.AddRange(offMinutes.Select(OffText));
        offBox.Index = hasOff ? Array.IndexOf(offMinutes, current.AutoOffMinutes) : -1;
        offBox.Enabled = hasOff;
        offBox.Invalidate();
        SetDimmed(offHead, hasOff);
        firmware.Text = found.Firmware == "" ? "–" : found.Firmware;
        int readMillivolts = ready && state.Millivolts > 0 ? state.Millivolts : found.Millivolts;
        bool charging = ready && state.Charging;
        int percent = !charging ? (readMillivolts > 0 ? FoundDevice.FromVolts(readMillivolts) : found.Battery)
            : found.Battery >= 0 ? found.Battery
            : FoundDevice.FromChargingVolts(readMillivolts);
        picture.Display(percent, readMillivolts > 0 || found.BatteryFromVoltage, readMillivolts, ready && state.Charging,
            hasOff ? (current.AutoOffMinutes == 0 ? "Stays on when idle" : "Turns off after " + current.AutoOffMinutes + " min idle") : "");
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
        debounce.Stop();
        if (now) Send(); else debounce.Start();
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
        int startedAt = ++sequence;
        RunOnDevice(hidpp =>
        {
            if (startedAt != sequence) return;
            if (deviceState.HasSidetone && (sent == null || want.Sidetone != sent.Sidetone))
                HeadsetProtocol.SetSidetone(hidpp, want.Sidetone);
            if (deviceState.HasOff && want.AutoOffMinutes >= 0 && (sent == null || want.AutoOffMinutes != sent.AutoOffMinutes))
                HeadsetProtocol.SetOff(hidpp, want.AutoOffMinutes);
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
        RunOnDevice(hidpp => read = HeadsetProtocol.Read(hidpp, autoOff), () =>
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
