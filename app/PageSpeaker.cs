using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

static class GSpeaker
{
    public static List<Dictionary<int, int>> Zones(Hidpp h)
    {
        byte f = h.F(0x8070);
        var zones = new List<Dictionary<int, int>>();
        int count = h.Call(f, 0)[0];
        for (int zone = 0; zone < count; zone++)
        {
            var info = h.Call(f, 1, (byte)zone, 0xFF, 0);
            var map = new Dictionary<int, int>();
            for (int effect = 0; effect < info[3]; effect++)
            {
                var r = h.Call(f, 2, (byte)zone, (byte)effect, 0xFF, 0);
                map[r[2] << 8 | r[3]] = effect;
            }
            zones.Add(map);
        }
        return zones;
    }

    public static void SetLight(Hidpp h, int zone, Dictionary<int, int> effects, MouseLight light)
    {
        int index;
        if (!effects.TryGetValue(light.Effect, out index)) return;
        h.Call(h.F(0x8070), 3, new byte[] { (byte)zone, (byte)index }.Concat(GMouse.Encode(light)).ToArray());
    }

    public static string ToText(MouseLight[] lights)
    {
        return string.Join(";", lights.Select(l => l == null ? "" : string.Join(",", l.Effect, l.Color.ToString("X6"), l.Period, l.Bright)));
    }

    public static MouseLight[] FromText(string text, int zones)
    {
        var parts = (text ?? "").Split(';');
        var lights = new MouseLight[zones];
        for (int zone = 0; zone < zones; zone++) lights[zone] = One(parts.Length == 1 ? parts[0] : zone < parts.Length ? parts[zone] : "");
        return lights.Any(l => l != null) ? lights : null;
    }

    static MouseLight One(string text)
    {
        var v = text.Split(',');
        byte effect;
        int color, period, bright;
        if (v.Length < 4 || !byte.TryParse(v[0], out effect) || GMouse.Def(effect) == null
            || !int.TryParse(v[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out color)
            || !int.TryParse(v[2], out period) || !int.TryParse(v[3], out bright)) return null;
        var d = GMouse.Def(effect);
        if (d.Speed) period = Math.Max(d.MinP, Math.Min(d.MaxP, period));
        return new MouseLight { Effect = effect, Color = color & 0xFFFFFF, Period = period, Bright = Math.Max(1, Math.Min(100, bright)) };
    }
}

class PageSpeaker : DevicePage
{
    const int Count = 4;
    MouseLight[] cur, sent = new MouseLight[Count];
    List<Dictionary<int, int>> zones;
    bool loading;
    int seq, picked;
    readonly Timer debounce = new Timer { Interval = 120 };

    internal override bool Busy { get { return debounce.Enabled; } }

    readonly Label lightHead, colorHead, speedHead, speedValue, brightHead, brightValue;
    readonly Seg zoneSeg, effects;
    readonly ColorRow colors;
    readonly Slider speed, bright;

    string Remembered { get { return "lighting." + found.Slug; } }

    public PageSpeaker(Found found, Hidpp fake, MouseEventHandler drag) : base(found, fake, drag, found.Short, "device", "profile." + found.Slug + ".")
    {
        debounce.Tick += (s, e) => { debounce.Stop(); Send(); };

        var light = Ui.Add(this, new Box(), 20, 118, 616, 330);
        lightHead = Ui.Text(light, "Lighting", 14, 12, () => Theme.Dim, 9f, Ui.Semi);
        Ui.Text(light, "Zone", 14, 40, () => Theme.Dim, 9f);
        zoneSeg = Ui.Add(light, new Seg { Items = new[] { "All", "Zone 1", "Zone 2", "Zone 3", "Zone 4" }, AccessibleName = "Zone", Index = 0 }, 14, 58, 440, 32);
        zoneSeg.Picked += i => { picked = i; Sync(); };
        Ui.Text(light, "Effect", 14, 98, () => Theme.Dim, 9f);
        effects = Ui.Add(light, new Seg { Items = GMouse.Effects.Select(d => d.Name).ToArray(), AccessibleName = "Effect" }, 14, 116, 352, 32);
        effects.Picked += i =>
        {
            var d = GMouse.Effects[i];
            Edit(l =>
            {
                l.Effect = d.Id;
                if (d.Speed && (l.Period < d.MinP || l.Period > d.MaxP)) l.Period = d.DefP;
            }, true);
        };
        colorHead = Ui.Text(light, "Color", 14, 156, () => Theme.Dim, 9f);
        colors = new ColorRow(light, 173);
        colors.Picked += c => Edit(l => l.Color = c, true);

        brightHead = Ui.Text(light, "Brightness", 14, 214, () => Theme.Dim, 9f);
        brightValue = Value(light, 14, 214, 172);
        bright = Ui.Add(light, new Slider { AccessibleName = "Brightness" }, 14, 234, 172, 24);
        bright.Changed += (s, e) => { int b = Math.Max(1, bright.Value); Edit(l => l.Bright = b, false); };
        speedHead = Ui.Text(light, "Speed", 194, 214, () => Theme.Dim, 9f);
        speedValue = Value(light, 194, 214, 172);
        speed = Ui.Add(light, new Slider { AccessibleName = "Speed" }, 194, 234, 172, 24);
        speed.Changed += (s, e) =>
        {
            var d = GMouse.Def(Shown().Effect);
            int period = d.MaxP - (d.MaxP - d.MinP) * speed.Value / 100;
            Edit(l => l.Period = period, false);
        };
        var note = Ui.Text(light, "G-Lite sets the lighting again when it starts and when you open the speakers. Which zone is which isn't known yet.", 14, 272, () => Theme.Dim, 9f);
        note.AutoSize = false;
        note.Size = new Size(588, 40);

        cur = GSpeaker.FromText(Settings.Get(Remembered), Count);
        Sync();
        if (cur != null) Load();
    }

    MouseLight Shown()
    {
        if (cur == null) return null;
        return cur[picked == 0 ? 0 : picked - 1];
    }

    void Edit(Action<MouseLight> change, bool now)
    {
        if (loading || zones == null) return;
        if (cur == null) cur = Enumerable.Range(0, Count).Select(i => new MouseLight()).ToArray();
        if (picked == 0)
        {
            var all = (cur[0] ?? new MouseLight()).Clone();
            change(all);
            for (int zone = 0; zone < Count; zone++) cur[zone] = all.Clone();
        }
        else
        {
            var one = (cur[picked - 1] ?? new MouseLight()).Clone();
            change(one);
            cur[picked - 1] = one;
        }
        seq++;
        Edited();
        Sync();
        debounce.Stop();
        if (now) Send(); else debounce.Start();
    }

    public override void Restyle() { Sync(); }

    protected override string Current() { return cur == null ? "" : GSpeaker.ToText(cur); }

    protected override void Use(string text)
    {
        var lights = GSpeaker.FromText(text, Count);
        if (lights == null) return;
        cur = lights;
        seq++;
        Edited();
        Sync();
        Send();
    }

    protected override void Revert() { }

    protected override void Lost() { zones = null; sent = new MouseLight[Count]; }

    void Sync()
    {
        loading = true;
        bool ready = zones != null && zones.Count > 0;
        var l = Shown();
        bool known = l != null;
        var d = known ? GMouse.Def(l.Effect) : null;
        bool mixed = picked == 0 && cur != null && cur.Any(z => z == null || !z.Same(cur[0]));
        lightHead.Text = zones != null && zones.Count == 0 ? "Lighting · not found on these speakers" : !known ? "Lighting · as on the speakers" : mixed ? "Lighting · zones differ, a change sets all of them" : "Lighting";
        zoneSeg.Enabled = ready;
        zoneSeg.Index = picked;
        zoneSeg.Invalidate();
        effects.Enabled = ready;
        effects.Index = known ? Array.FindIndex(GMouse.Effects, e => e.Id == l.Effect) : -1;
        effects.Invalidate();
        bool color = known && d.Color, spd = known && d.Speed, br = known && l.Effect != 0;
        Shade(colorHead, color);
        colors.Sync(known ? l.Color : 0xFFFFFF, color);
        Shade(brightHead, br);
        bright.Enabled = br;
        bright.Value = known ? l.Bright : 100;
        brightValue.Text = br ? l.Bright + "%" : "";
        bright.Invalidate();
        Shade(speedHead, spd);
        speed.Enabled = spd;
        if (spd) speed.Value = (d.MaxP - l.Period) * 100 / (d.MaxP - d.MinP);
        speedValue.Text = spd ? speed.Value + "%" : "";
        speed.Invalidate();
        loading = false;
    }

    protected override void Calm()
    {
        if (zones != null && zones.Count == 0) { Say("These speakers list no lighting zones", true); return; }
        Say(cur == null ? "" : "Set by G-Lite · not saved to the speakers");
    }

    void Send()
    {
        if (cur == null) return;
        var want = cur.Select(l => l == null ? null : l.Clone()).ToArray();
        var z = zones;
        int n = ++seq;
        Settings.Set(Remembered, GSpeaker.ToText(want));
        Do(h =>
        {
            if (n != seq || z == null) return;
            Apply(h, z, want);
        }, () => { if (n == seq) Calm(); });
    }

    void Apply(Hidpp h, List<Dictionary<int, int>> z, MouseLight[] want)
    {
        for (int zone = 0; zone < z.Count && zone < Count; zone++)
        {
            if (want[zone] == null || (sent[zone] != null && sent[zone].Same(want[zone]))) continue;
            GSpeaker.SetLight(h, zone, z[zone], want[zone]);
            sent[zone] = want[zone];
        }
    }

    public override void Load()
    {
        debounce.Stop();
        Say("Reading the speakers…");
        List<Dictionary<int, int>> read = null;
        var want = cur == null ? null : cur.Select(l => l == null ? null : l.Clone()).ToArray();
        Do(h =>
        {
            read = GSpeaker.Zones(h);
            sent = new MouseLight[Count];
            if (want != null) Apply(h, read, want);
        }, () =>
        {
            zones = read;
            Sync();
            Loaded();
        });
    }
}
