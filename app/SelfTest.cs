using System;
using System.Linq;
using System.Collections.Generic;

static class SelfTest
{
    static byte[] Hex(string s) { return Enumerable.Range(0, s.Length / 2).Select(i => Convert.ToByte(s.Substring(i * 2, 2), 16)).ToArray(); }

    static void Check(bool ok, string what) { if (!ok) { Console.WriteLine("FAIL " + what); Environment.Exit(1); } }

    static int Main(string[] args)
    {
        var factory = Hex("0101003c012cffffffffffffffffffffffffffffffffffffffffffffffff18ff008180aaff00006400000000000000000000000019ff008180aaff0000640000000000000000000000000300000000000000000010000104000200ffffffffffffffff");
        var factoryDir = Hex("5e380925010181014063996dec0101ff");
        var pyProfile = Hex("0101003c012cffffffffffffffffffffffffffffffffffffffffffffffff000000000000000000000001ffffff000000000000000000000000000000000000017f7f7f000000000000000000000000000000000010000104000200ffffffffffffffff");

        Check(G515.Crc32(factoryDir.Skip(4).ToArray()) == 0x5e380925, "crc32");
        Check(G515.BuildDirectory(factoryDir, pyProfile).SequenceEqual(Hex("8b5bf5610101810140639a0ad7d308ff")), "directory matches the one the keyboard accepted");

        var expected = (byte[])pyProfile.Clone();
        expected[45] = 0x02; expected[67] = 0x02;
        var white = new Lighting { Effect = 0x01, Color = 0xFFFFFF, Dim = 50, Idle = 60, Sleep = 300 };
        Check(G515.BuildProfile(factory, white).SequenceEqual(expected), "static white profile");
        Check(G515.Decode(factory) == null, "factory animation is unknown");

        foreach (var d in G515.Effects)
        {
            var l = new Lighting { Effect = d.Id, Color = 0x3080FF, Period = d.Speed ? (d.MinP + d.MaxP) / 2 : 5000, Direction = 6, Dim = 25, Idle = 0, Sleep = 900 };
            var back = G515.Decode(G515.BuildProfile(factory, l));
            Check(back != null && back.Effect == l.Effect && back.Idle == 0 && back.Sleep == 900, d.Name + " effect/timers");
            if (d.Color) Check(back.Color == l.Color && back.Dim == 25, d.Name + " color/dim");
            if (d.Speed) Check(back.Period == l.Period, d.Name + " period");
            if (d.Direction) Check(back.Direction == 6, d.Name + " direction");
        }
        var fake = new FakeHidpp();
        Check(G515.Decode(G515.Snapshot(fake).FactoryProfile) == null, "fake starts on factory profile");
        var blue = new Lighting { Effect = 0x0A, Color = 0x3080FF, Period = 3000, Dim = 25, Idle = 30, Sleep = 600 };
        G515.Claim(fake);
        G515.Preview(fake, blue);
        Check(fake.Live[1] == 2 && fake.Live[2] == 0x30, "preview sends breathe at effect index 2");
        G515.Store(fake, blue);
        Check(G515.Decode(G515.Snapshot(fake).UserProfile).Same(blue), "store then read back");
        G515.Store(fake, white);
        Check(G515.Decode(G515.Snapshot(fake).UserProfile).Same(white), "second store overwrites");

        Backups.Root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "glite-selftest-" + Guid.NewGuid().ToString("N"));
        var kb = new FakeHidpp();
        G515.Store(kb, blue);
        Backups.Save("T", G515.Read(kb));
        Backups.Save("T", G515.Read(kb));
        var saved = Backups.List("T");
        Check(saved.Count == 1 && saved[0].First && Backups.Describe(saved[0].Files) == "Breathe", "first backup, and no copy of an unchanged keyboard");
        G515.Store(kb, white);
        G515.Restore(kb, saved[0].Files);
        Check(G515.Decode(G515.Snapshot(kb).UserProfile).Same(blue), "restoring a backup brings its lighting back");
        G515.Restore(kb, new G515.Files());
        Check(G515.Decode(G515.Snapshot(kb).UserProfile) == null, "restoring the factory lighting brings the factory animation back");
        kb.Flash[0x0008] = G515.BuildProfile(kb.Flash[0x0008], white);
        bool half = false;
        try { G515.Snapshot(kb); } catch (HalfWritten) { half = true; }
        Check(half && !G515.Good(G515.Read(kb)), "a directory that doesn't match its profile is caught");
        G515.Restore(kb, saved[0].Files);
        Check(G515.Decode(G515.Snapshot(kb).UserProfile).Same(blue), "a backup repairs a half written keyboard");
        var stranger = new G515.Files { FactoryProfile = saved[0].Files.FactoryProfile, FactoryDir = (byte[])saved[0].Files.FactoryDir.Clone(), UserProfile = saved[0].Files.UserProfile, UserDir = saved[0].Files.UserDir };
        stranger.FactoryDir[11] ^= 1;
        bool refused = false;
        try { G515.Restore(kb, stranger); } catch (Refused) { refused = true; }
        Check(refused, "a backup from another keyboard is refused");
        var foreign = G515.Read(kb);
        foreign.UserDir = (byte[])foreign.UserDir.Clone();
        foreign.UserDir[15] = 0x00;
        bool other = false, offered = false;
        try { G515.Check(foreign); } catch (HalfWritten) { offered = true; } catch (Refused) { other = true; }
        Check(other && !offered, "profiles G-Lite didn't write are refused, not offered for restore");
        System.IO.File.WriteAllLines(System.IO.Path.Combine(Backups.Folder("T"), "20250101-120000-000.txt"), System.IO.File.ReadAllLines(saved[0].File));
        Check(Backups.List("T").Any(e => e.When == new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Local).ToUniversalTime()), "backups named in local time still read");
        System.IO.File.Delete(System.IO.Path.Combine(Backups.Folder("T"), "20250101-120000-000.txt"));
        for (int i = 0; i < Backups.Keep + 3; i++)
        {
            G515.Store(kb, new Lighting { Effect = 0x01, Color = i + 1 });
            Backups.Save("T", G515.Read(kb));
        }
        saved = Backups.List("T");
        Check(saved.Count == Backups.Keep + 1 && saved.Count(e => e.First) == 1 && saved.Last().First, "the first backup stays, with the last ten");
        System.IO.Directory.Delete(Backups.Root, true);

        var dpis = GMouse.DpiList(Hex("000064e0326400"));
        Check(dpis.Length == 511 && dpis[0] == 100 && dpis[1] == 150 && dpis.Last() == 25600, "DPI range with a step");
        Check(GMouse.RateList(0x8B).SequenceEqual(new[] { 125, 250, 500, 1000 }), "report rates from their bits");
        foreach (var d in GMouse.Effects)
        {
            var l = new MouseLight { Effect = d.Id, Color = 0x3080FF, Period = 4000, Bright = 50 };
            var back = GMouse.Decode(new[] { d.Id }.Concat(GMouse.Encode(l)).ToArray());
            Check(back != null && back.Effect == d.Id, d.Name + " mouse effect");
            if (d.Id != 0) Check(back.Bright == 50, d.Name + " mouse brightness");
            if (d.Color) Check(back.Color == 0x3080FF, d.Name + " mouse color");
            if (d.Speed) Check(back.Period == 4000, d.Name + " mouse period");
        }
        var mouse = new FakeMouse();
        var ms = GMouse.Read(mouse);
        Check(ms.Dpi == 1600 && ms.Stage == 2 && ms.Stages.SequenceEqual(new[] { 400, 800, 1600, 3200, 6400 }) && ms.Onboard && ms.Rate == 1000
            && ms.Light != null && ms.Light.Effect == 0x03 && ms.Light.Period == 8000 && ms.Zones.Count == 2, "mouse state read from its onboard profile");
        GMouse.SetStage(mouse, 3);
        GMouse.SetLight(mouse, ms, new MouseLight { Effect = 0x0A, Color = 0xFF0000, Period = 3000, Bright = 80 });
        Check(mouse.Dpi == 3200 && mouse.Zones[1][1] == 3 && mouse.Zones[1][2] == 0xFF && mouse.Zones[1][8] == 80, "mouse stage and lighting are set");
        var mset = MouseSet.FromText(new MouseSet { Dpi = 1200, Light = new MouseLight { Effect = 0x0A, Color = 0x30D050, Period = 3000, Bright = 70 } }.ToText());
        Check(mset.Dpi == 1200 && mset.Light.Same(new MouseLight { Effect = 0x0A, Color = 0x30D050, Period = 3000, Bright = 70 }), "a mouse profile round-trips through text");

        var headset = new FakeHeadset();
        var hs = GHeadset.Read(headset, true);
        Check(hs.HasSidetone && hs.HasOff && hs.Set.Sidetone == 30 && hs.Set.Off == 30 && hs.Millivolts == 3926 && !hs.Charging, "headset sidetone, auto-off and battery are read");
        GHeadset.SetSidetone(headset, 55);
        GHeadset.SetOff(headset, 10);
        Check(headset.Sidetone == 55 && headset.Off == 10, "headset sidetone and auto-off are set");
        var hset = HeadSet.FromText(new HeadSet { Sidetone = 20, Off = 15 }.ToText());
        Check(hset.Sidetone == 20 && hset.Off == 15, "a headset profile round-trips through text");
        Check(new Found { Name = "G Pro Wireless Gaming Mouse" }.Slug == "gprowireless", "profile names for a device");
        var m = Model.Load("g515-tkl.json");
        Check(m.Name == "G515 LIGHTSPEED TKL" && m.Rows.Length == 7 && m.Rows[6].EndsWith("→"), "keyboard model loads");
        var game = new Blocks();
        for (int y = 0; y < Game.H; y++) game.Cells[y, Game.W - 1] = 1;
        game.Cells[0, Game.W - 2] = 2;
        game.ClearFull();
        Check(game.Score == 1 && game.Cells[0, Game.W - 1] == 2 && game.Cells[0, Game.W - 2] == 0, "full column clears and shifts");
        game.Key(System.Windows.Forms.Keys.Space);
        Check(!game.Over, "piece locks after drop");
        var rects = new System.Drawing.RectangleF[Game.W * Game.H];
        var cells = new List<System.Drawing.Point>[rects.Length];
        for (int i = 0; i < rects.Length; i++)
        {
            rects[i] = new System.Drawing.RectangleF(i % Game.W * 40, i / Game.W * 40, 36, 36);
            cells[i] = new List<System.Drawing.Point> { new System.Drawing.Point(i % Game.W, i / Game.W) };
        }
        var snake = new Snake(cells);
        snake.Key(System.Windows.Forms.Keys.Left);
        snake.Step();
        Check(!snake.Over && snake.At(2, 5) != 0, "snake ignores reversing into itself");
        for (int i = 0; i < Game.W - 5; i++) snake.Step();
        Check(!snake.Over && snake.At(2, 0) != 0, "snake wraps through the wall");
        var mines = new Mines(rects, cells);
        mines.Open(2 * Game.W + 7);
        Check(!mines.Over && mines.Opened(2 * Game.W + 7) && mines.Near(2 * Game.W + 7) == 0, "first open is never a mine");
        mines = new Mines(rects, cells) { Placed = true };
        mines.Mine[0] = true;
        mines.Open(rects.Length - 1);
        Check(mines.Over && mines.Score == rects.Length - 1, "opening an empty key floods the board and wins");
        mines = new Mines(rects, cells) { Placed = true };
        mines.Mine[0] = true;
        mines.Open(0);
        Check(mines.Over && mines.Score == 0 && mines.At(0, 0) == 0xFF3030, "opening a mine ends the game and shows it");
        var wide = new[] { new System.Drawing.RectangleF(0, 0, 76, 36), new System.Drawing.RectangleF(80, 0, 36, 36), new System.Drawing.RectangleF(20, 40, 36, 36) };
        var wideCells = new[] { new List<System.Drawing.Point> { new System.Drawing.Point(0, 0), new System.Drawing.Point(1, 0) }, new List<System.Drawing.Point> { new System.Drawing.Point(2, 0) }, new List<System.Drawing.Point> { new System.Drawing.Point(0, 1) } };
        mines = new Mines(wide, wideCells) { Placed = true };
        mines.Mine[1] = true;
        Check(mines.Near(0) == 1 && mines.Near(2) == 0, "neighbours follow the keys, not the grid");
        Check(Updater.Newer("0.0.2-alpha", "0.0.1-alpha") && Updater.Newer("0.0.1", "0.0.1-alpha") && Updater.Newer("0.0.1-beta", "0.0.1-alpha") && Updater.Newer("v0.1.0", "0.0.9")
            && !Updater.Newer("0.0.1-alpha", "0.0.1-alpha") && !Updater.Newer("0.0.1-alpha", "0.0.1") && !Updater.Newer("0.0.9", "0.0.10"), "versions compare");
        Check(Updater.TagFrom("https://github.com/csmarvz/g-lite/releases/tag/v0.0.2-alpha") == "v0.0.2-alpha" && Updater.TagFrom("https://github.com/csmarvz/g-lite/releases") == null, "latest tag read from the redirect");
        Settings.Location = System.IO.Path.GetTempFileName();
        Settings.Init();
        Check(Settings.Get("check-updates") == "1" && Settings.Get("skip-version") == "", "settings file starts with the defaults");
        Settings.Set("skip-version", "0.0.3-alpha");
        Check(Settings.Get("skip-version") == "0.0.3-alpha" && System.IO.File.ReadAllText(Settings.Location).Contains("skip-version=0.0.3-alpha"), "settings are written to the file");
        int setBright;
        var set = Lighting.FromText(new Lighting { Effect = 0x16, Color = 0x3080FF, Period = 2500, Direction = 6 }.ToText(40), out setBright);
        Check(set.Same(new Lighting { Effect = 0x16, Color = 0x3080FF, Period = 2500, Direction = 6 }) && setBright == 40, "a set round-trips through text");
        Settings.Set("set.Night", "1,FFFFFF,5000,1,50,60,300,20");
        Settings.Remove("set.Night");
        Check(!Settings.Keys().Contains("set.Night"), "a removed set is gone from the file");
        Settings.Set("set.Evening", "1,FF8000,5000,1,50,60,300,40");
        Settings.Set("profile.g515.Day", "1,FFFFFF,5000,1,50,60,300,100");
        Settings.Set("set.Day", "1,000000,5000,1,50,60,300,0");
        Settings.Init();
        Check(Settings.Get("profile.g515.Evening").StartsWith("1,FF8000") && Settings.Get("profile.g515.Day").StartsWith("1,FFFFFF") && !Settings.Keys().Any(k => k.StartsWith("set.")),
            "old sets move to G515 profiles without overwriting");
        Check(Found.LooksG("G515 LIGHTSPEED TKL") && Found.LooksG("G Pro Wireless Gaming Mouse") && Found.LooksG("PRO X Wireless Gaming Headset") && Found.LooksG("G502 X PLUS")
            && !Found.LooksG("MX KEYS S") && !Found.LooksG("Logitech USB Receiver") && !Found.LooksG(""), "G devices are told from other Logitech devices");
        Check(new Found { Name = "G Pro Wireless Gaming Mouse" }.Short == "G Pro Wireless" && new Found { Name = "G515 LIGHTSPEED TKL" }.Short == "G515 TKL", "short device names");
        Check(Found.FromVolts(4200) == 100 && Found.FromVolts(3300) == 0 && Found.FromVolts(3800) == 50, "battery estimate from the voltage");
        Check(new Found { Name = "G515 LIGHTSPEED TKL", Features = new[] { 0x8071, 0x8040, 0x8101 } }.IsG515 && !new Found { Name = "G515 LIGHTSPEED TKL", Features = new[] { 0x8071 } }.IsG515, "the G515 needs its three features");
        Check(Settings.Get("theme") == "system" && Theme.Mode == "system", "theme starts on system");
        Theme.Mode = "light";
        Check(Theme.Light && Settings.Get("theme") == "light", "light theme is saved and applied");
        Theme.Mode = "dark";
        Check(!Theme.Light && Theme.Text.R > 200, "dark theme is applied");
        Settings.Set("theme", "pink");
        Check(Theme.Mode == "system", "an unknown theme falls back to system");
        var red = G515.Encode(new Lighting { Effect = 0x01, Color = 0xFF0000 }, 100);
        Check(KeyboardView.Lit(0x01, red, 0.5, 0.5, 0, 0) == System.Drawing.Color.FromArgb(255, 0, 0) && KeyboardView.Lit(0x00, new byte[10], 0.5, 0.5, 0, 0).ToArgb() == System.Drawing.Color.Black.ToArgb(), "key colors for static and off");
        System.IO.File.Delete(Settings.Location);
        using (var logo = Logo.Draw(64))
            Check(logo.GetPixel(0, 0).A == 0 && logo.GetPixel(32, 4).A == 255 && logo.GetPixel(32, 32).A < 255, "logo is a white disc with a transparent GL");
        if (args.Length > 0) Logo.WriteIco(args[0]);
        Console.WriteLine("selftest ok");
        return 0;
    }
}
