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
        var real = Hex("01020290012003d007800c0019ffffffff00ffffffffffffffffffffffffffff8001000180010002800100048001000880010010900500008001000880010010ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff8001000180010002800100048001000880010010900500008001000880010010ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff440065006600610075006c0074000000000000000000000000000000000000000000000000000000000000000000000001ffffff0000000000000001ffffff00000000000000ffffffffffffffffffffffffffffffffffffffffffff00bfdd");
        Check(GMouse.Valid(real) && GMouse.Crc(real, 253) == 0xBFDD, "the profile checksum matches the real mouse");
        var mouseLight = new MouseLight { Effect = 0x0A, Color = 0xFF0000, Period = 3000, Bright = 80 };
        var built = GMouse.Build(real, new MouseSet { Dpi = 1200, Rate = 500, Light = mouseLight, Stage = 2 }, 2);
        var allowed = new HashSet<int>(new[] { 0, 1, 253, 254 }.Concat(Enumerable.Range(3, 10)).Concat(Enumerable.Range(0xD0, 22)));
        Check(GMouse.Valid(built) && built[0] == 2 && built[1] == 2 && GMouse.StagesOf(built).SequenceEqual(new[] { 400, 800, 1200, 3200, 6400 })
            && built[0xD0] == 0x0A && built[0xDB] == 0x0A && Enumerable.Range(0, 255).All(i => allowed.Contains(i) || built[i] == real[i]), "a saved mouse profile changes only DPI, report rate and lighting");
        var picked = GMouse.Build(real, new MouseSet { Dpi = 800, Stage = 1 }, 2);
        Check(picked[1] == 1 && GMouse.StagesOf(picked).SequenceEqual(GMouse.StagesOf(real)) && picked[0] == real[0], "saving a DPI stage only makes it the default");
        var moved = GMouse.Build(real, new MouseSet { Dpi = 800, Stage = 2 }, 2);
        Check(moved[1] == 2 && GMouse.StagesOf(moved).SequenceEqual(new[] { 400, 800, 800, 3200, 6400 }), "a stage dragged to another stage's value keeps its place");
        var m2 = new FakeMouse();
        var st = GMouse.Read(m2);
        Check(st.Savable && st.Sector == 1 && st.SectorSize == 255 && st.Format == 3, "the fake mouse can be saved to");
        GMouse.Store(m2, st, GMouse.Build(st.Profile, new MouseSet { Dpi = 3200, Rate = 500, Stage = 3, Light = new MouseLight { Effect = 0x01, Color = 0xFF2020, Bright = 100 } }, 2));
        var st2 = GMouse.Read(m2);
        Check(st2.Dpi == 3200 && st2.Rate == 500 && st2.Light != null && st2.Light.Effect == 0x01 && st2.Light.Color == 0xFF2020 && m2.Reloads == 1 && st2.ProfileOk, "saving to the mouse sticks and reloads the profile");
        GMouse.Store(m2, st2, GMouse.Factory(GMouse.ReadSector(m2, GMouse.RomSector, 255), st2.Profile));
        var st3 = GMouse.Read(m2);
        Check(st3.Dpi == 1600 && st3.Light.Effect == 0x03 && st3.ProfileOk && System.Text.Encoding.Unicode.GetString(st3.Profile, 0xA0, 14) == "Default", "the factory profile comes back with the profile's name");
        int refusals = 0;
        foreach (var sec in new[] { 0x0000, 0x0101 }) { try { GMouse.WriteSector(m2, sec, st3.Profile); } catch (Refused) { refusals++; } }
        var broken = (byte[])st3.Profile.Clone();
        broken[5] ^= 1;
        try { GMouse.WriteSector(m2, 1, broken); } catch (Refused) { refusals++; }
        Check(refusals == 3 && GMouse.Valid(m2.Sectors[1]) && GMouse.Valid(m2.Sectors[0x0101]) == false, "the directory, the factory profile and bad checksums are never written");

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
        Check(new Found { Name = "G915 TKL LIGHTSPEED Wireless RGB Mechanical Gaming Keyboard", Features = new[] { 0x8071, 0x8040, 0x8100 } }.IsG915
            && !new Found { Name = "G915 X LIGHTSPEED", Features = new[] { 0x8071, 0x8040, 0x8100 } }.IsG915
            && new Found { Name = "G915 WIRELESS RGB MECHANICAL GAMING KEYBOARD" }.Short == "G915"
            && new Found { Name = "G915 TKL LIGHTSPEED Wireless RGB Mechanical Gaming Keyboard" }.Short == "G915 TKL", "the G915 is found, and the G915 X isn't");
        Check(new Found { Index = 0, Hidpp10 = true }.Slot == "" && new Found { Index = 0xFF }.Slot == "" && new Found { Index = 2 }.Slot == " · slot 2"
            && Found.LooksG("G9x Laser Mouse") && !new Found { Name = "G9x Laser Mouse", Kind = "mouse", Hidpp10 = true }.IsMouse, "an old HID++ 1.0 device has no slot and isn't taken for a HID++ 2.0 mouse");
        Check(G9x.Dpi(17) == 400 && G9x.Dpi(136) == 3200 && G9x.Dpi(242) == 5700 && G9x.Dpi(9) == 200 && G9x.Dpi(85) == 2000 && G9x.Raw(5700) == 242 && G9x.Raw(800) == 34, "G9x DPI matches what LGS shows");
        var g9xMouse = new FakeG9x();
        var g9xState = G9x.Read(g9xMouse);
        Check(g9xState.Dpi == 800 && g9xState.Stage == 1 && g9xState.Stages.SequenceEqual(new[] { 400, 800, 1600, 3200 }) && g9xState.Rate == 500 && g9xState.Light.Effect == 0x01 && g9xState.Light.Color == 0xFF0000 && g9xState.Light.Bright == 100, "the G9x reads its DPI, stages, rate and colour");
        G9x.SetDpi(g9xMouse, 5700);
        G9x.SetRate(g9xMouse, 1000);
        G9x.SetLight(g9xMouse, new MouseLight { Effect = 0x01, Color = 0x0000FF, Bright = 50 });
        Check(g9xMouse.Registers[0x63].Take(6).SequenceEqual(FakeHidpp.Hex("F200F2008001")) && g9xMouse.Registers[0x64][0] == 1 && g9xMouse.Registers[0x57].SequenceEqual(FakeHidpp.Hex("00007F")), "the G9x sets DPI, rate and colour through its registers");
        G9x.SetLight(g9xMouse, new MouseLight { Effect = 0x00 });
        Check(g9xMouse.Registers[0x57].SequenceEqual(new byte[3]) && G9x.Read(g9xMouse).Light.Effect == 0x00, "the G9x light turns off");
        Check(G9x.StagesOf(g9xMouse.Pages[3]).SequenceEqual(new[] { 200, 800, 2000 }) && new Found { Hidpp10 = true, Path = @"\\?\hid#vid_046d&pid_c066&mi_01&col04" }.IsG9x && !new Found { Path = @"\\?\hid#vid_046d&pid_c066" }.IsG9x, "G9x profile 2 and detection");
        Check(Found.ModelText(FakeHidpp.Hex("0400000000000EB38940B4C35500")) == "USB C355 · LIGHTSPEED 40B4 · Bluetooth B389"
            && Found.ModelText(FakeHidpp.Hex("0300000000000C4079C0880000")) == "USB C088 · LIGHTSPEED 4079"
            && Found.ModelText(FakeHidpp.Hex("01000000000003000000000ABA")) == "0ABA", "product IDs from 0x0003 as the G515, G Pro Wireless and PRO X Wireless answer");
        var g9xSaver = new FakeG9x();
        var g9xBefore = G9x.Read(g9xSaver);
        var g9xOtherPage = (byte[])g9xSaver.Pages[3].Clone();
        var g9xPage = G9x.Build(g9xSaver.Pages[2], new MouseSet { Dpi = 1200, Stage = 2, Light = new MouseLight { Effect = 0x01, Color = 0x00FF00, Bright = 100 } }, 1000);
        G9x.Store(g9xSaver, g9xBefore, g9xPage);
        var g9xAfter = G9x.Read(g9xSaver);
        Check(g9xBefore.Writable && g9xSaver.Pages[2].SequenceEqual(g9xPage) && g9xAfter.ProfileOk && g9xAfter.Stages.SequenceEqual(new[] { 400, 800, 1200, 3200 })
            && g9xPage[16] == 0 && g9xPage[17] == 51 && g9xPage[18] == 0 && g9xPage[19] == 51 && g9xPage[0x23] == 2 && g9xPage[0x26] == 1 && g9xPage.Take(3).SequenceEqual(FakeHidpp.Hex("00FF00"))
            && g9xSaver.Pages[3].SequenceEqual(g9xOtherPage) && g9xSaver.Writes.SequenceEqual(new[] { "erase 2:0", "copy 2:0", "copy 2:128" })
            && g9xSaver.Registers[0x0F][0] == 0x00 && g9xSaver.Registers[0x0F][1] == 0x00, "the G9x saves a profile page through RAM, erase and copy, and loads it again");
        var g9xDirectory = G9x.Read(new FakeG9x());
        g9xDirectory.Sector = 1;
        bool g9xRefused = false;
        try { G9x.Store(g9xSaver, g9xDirectory, g9xPage); } catch (Refused) { g9xRefused = true; }
        Check(g9xRefused && !g9xDirectory.Writable && g9xSaver.Writes.Count == 3, "the G9x never writes the directory page");
        Check(new Found { Name = "G510 Gaming Keyboard" }.Short == "G510" && Found.LooksG("G510 Gaming Keyboard"), "the G510 gets a short name and counts as a G device");
        var g9xReport = Report.Registers(new FakeG9x(), new Found { Name = "G9x Laser Mouse", Kind = "mouse", Link = "USB", Hidpp10 = true }, "PageMouse");
        Check(g9xReport.Contains("long 63=22 00 22 00 80 01") && g9xReport.Contains("page 02 · current") && g9xReport.Contains("page 04"), "the G9x report has its registers and profile pages");
        Check(new[] { "g515-tkl.json", "g915-tkl.json", "g915.json" }.All(x => Model.Load(x).Rows.Length > 5), "the key layouts load");
        var g9 = new FakeG915();
        var cl = G915.Clusters(g9);
        Check(cl.Count == 2 && G915.Keys(cl) == 1, "the G915's keys are the cluster with the most effects");
        G915.Claim(g9);
        G915.Preview(g9, cl, new Lighting { Effect = 0x15, Period = 3000 });
        Check(g9.Claimed == 3 && g9.Live[1][1] == 3 && g9.Live[1][7] == 0x0B && g9.Live[1][8] == 0xB8 && g9.Live[1][9] == 100 && g9.Live[1][12] == 1, "cycle on the G915 uses effect 0x03, with its period at byte 5");
        Check(g9.Live[0][1] == 2 && g9.Live[0][7] == 0x0B, "the logo cycles too");
        G915.Preview(g9, cl, new Lighting { Effect = 0x16, Period = 4000, Direction = 1 });
        Check(g9.Live[1][1] == 4 && g9.Live[1][8] == 0xA0 && g9.Live[1][9] == 1 && g9.Live[1][10] == 100 && g9.Live[1][11] == 0x0F, "wave on the G915 uses effect 0x04");
        Check(g9.Live[0][1] == 2, "the logo cycles when the keys wave");
        G915.Preview(g9, cl, new Lighting { Effect = 0x17, Color = 0x3080FF, Period = 20 });
        Check(g9.Live[1][1] == 5 && g9.Live[1][2] == 0x30 && g9.Live[1][7] == 20 && g9.Live[0][1] == 1 && g9.Live[0][4] == 0xFF, "ripple on the keys, the ripple's colour on the logo");
        G915.Preview(g9, cl, new Lighting { Effect = 0x0A, Color = 0xFF0000, Period = 5000 });
        Check(g9.Live[1][1] == 2 && g9.Live[0][1] == 3 && g9.Live[1][5] == 0x13 && g9.Live[1][6] == 0x88, "breathe on keys and logo");
        bool lacks = false;
        try { G915.Preview(g9, cl, new Lighting { Effect = 0x02 }); } catch (Refused) { lacks = true; }
        Check(lacks && g9.Live[1][1] == 2, "an effect the G915 lacks is refused without sending anything");
        G915.Release(g9);
        Check(g9.Claimed == 0, "release gives the lighting back to the G915");
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
        Log.File = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "glite-selftest-log.txt");
        Log.Clear();
        var lm = new FakeMouse { Who = "M" };
        lm.Call(lm.F(0x2201), 2, 0);
        GMouse.SetDpi(lm, 1600);
        var logged = Log.Read();
        Check(logged.Count == 1 && logged[0].Who == "M" && logged[0].Text == "2201 fn3  00 06 40" && logged[0].Result == "ok", "the log keeps commands and leaves out reads");
        Log.Sent("M", 0x8100, 7, new byte[] { 1 }, "ok");
        Log.Sent("M", 0x8100, 7, new byte[] { 2 }, "ok");
        var folded = Log.Fold(Log.Read());
        Check(folded.Count == 2 && folded[1].Times == 2 && Log.Format(folded[1]).Contains("8100 fn7  ok ×2       01"), "the log folds calls in a row to the same function");
        Log.Clear();
        var spy = new Recorder(new FakeMouse());
        var mreport = Report.Build(spy, new Found { Name = "G Pro Wireless Gaming Mouse", Kind = "mouse", Link = "LIGHTSPEED", Index = 1, Wpid = 0x4079, Path = @"\\?\hid#vid_046d&pid_c547&mi_02&col02#7&abc&0&0001#{x}", Features = new[] { 0x2201, 0x8060, 0x8070, 0x8100 } }, "PageMouse");
        Check(mreport.Contains("ID 4079 · USB 046D:C547") && mreport.Contains("0000=0001") && mreport.Contains("0001=" + BitConverter.ToString(spy.Inner.Sectors[1]).Replace("-", ""))
            && mreport.Contains("0101=") && mreport.Contains("dpi list=") && mreport.Contains("zone 1 effect 3=") && !mreport.Contains("abc"), "the mouse report has its features, profiles and no device path");
        Check(spy.Calls.Count > 20 && spy.Calls.All(c => Report.Reads.ContainsKey(c.Item1) && Report.Reads[c.Item1].Contains(c.Item2)), "the mouse report only reads");
        var kspy = new Recorder(new FakeHidpp());
        var kreport = Report.Build(kspy, new Found { Name = "G515 LS TKL", Kind = "keyboard", Link = "LIGHTSPEED", Index = 1, Wpid = 0x40B4, Features = new[] { 0x4540, 0x8040, 0x8071, 0x8081, 0x8101 } }, "Page515");
        Check(kreport.Contains("lit ids=01 02 03") && kreport.Contains(" 4F 61 62") && kreport.Contains("layout=0D") && kreport.Contains("0008=") && kreport.Contains("0101=") && kreport.Contains("cluster 0 effect 1=") && kspy.Calls.All(c => Report.Reads.ContainsKey(c.Item1) && Report.Reads[c.Item1].Contains(c.Item2)), "the keyboard report reads its files and effects only");
        var hreport = Report.Build(new FakeHeadset(), new Found { Name = "PRO X Wireless Gaming Headset", Kind = "headset", Link = "LIGHTSPEED", Index = 0xFF, Features = new[] { 0x8300, 0x1F20 } }, "PageHeadset");
        Check(hreport.Contains("sidetone=1E") && hreport.Contains("auto-off=1E") && hreport.Contains("% · 3926 mV"), "the headset report reads sidetone, auto-off and the battery");
        Check(!Report.Reads[0x8100].Intersect(new[] { 1, 3, 6, 7, 8, 0xC }).Any() && !Report.Reads[0x8101].Intersect(new[] { 2, 3, 6, 7 }).Any() && !Report.Reads[0x8071].Contains(1) && !Report.Reads[0x8070].Contains(3), "the report never calls a write function");

        foreach (var file in new[] { "g515-tkl.json", "g515-tkl-iso.json", "g915-tkl.json", "g915.json" })
        {
            var board = Model.Load(file);
            int keys = board.Rows.Sum(r => r.Split('|').Count(t => !t.StartsWith("_")));
            var keyIds = board.KeyIds();
            Check(keyIds.Count == keys && board.Ids.Length == board.Rows.Length, file + " has one key id per key");
            Check(keyIds.Where(i => i >= 0).All(i => i >= 0x01 && i <= 0x6F), file + " key ids are lighting ids");
        }
        var isoIds = Model.Load("g515-tkl-iso.json").KeyIds();
        var ansiIds = Model.Load("g515-tkl.json").KeyIds();
        Check(isoIds.Contains(0x61) && isoIds.Contains(0x2F) && !isoIds.Contains(0x2E) && ansiIds.Contains(0x2E) && !ansiIds.Contains(0x61), "ISO has <> and the key next to Enter, ANSI has \\");
        Check(ansiIds.Distinct().Count() == 87 && isoIds.Distinct().Count() == 88, "the G515 has 87 keys in ANSI and 88 in ISO");
        Check(ansiIds.Contains(0x6F) && ansiIds.Contains(0x62) && ansiIds.Contains(0x6E), "Fn, Menu and AltGr have ids");

        var screen = new ScreenSetup { Monitor = @"\\.\DISPLAY2", Boost = 60, Interval = 300, Smooth = false, PauseOnBattery = false, PauseWhenLocked = false };
        screen.Keys[0x26] = 3;
        screen.Add();
        screen.Zones[4].Area = new System.Drawing.RectangleF(0.1f, 0.2f, 0.3f, 0.4f);
        var back2 = ScreenSetup.FromText(screen.ToText());
        Check(back2.Monitor == screen.Monitor && back2.Boost == 60 && back2.Interval == 300 && !back2.Smooth && !back2.PauseOnBattery && !back2.PauseWhenLocked
            && back2.Zones.Count == 5 && back2.Zones[4].Name == "Zone 5" && Math.Abs(back2.Zones[4].Area.Height - 0.4f) < 0.001f && back2.Keys[0x26] == 3, "screen setup round-trips through text");
        Check(ScreenSetup.FromText("garbage;zones:x").Zones.Count == 4 && ScreenSetup.FromText("ms:5000").Interval == 1000, "bad screen setup text falls back to defaults");
        Check(screen.Add() == -1, "at most five zones");
        screen.Remove(3);
        Check(screen.Zones.Count == 4 && screen.Keys[0x26] == 2, "removing a zone moves its keys to the zone before");
        Check(screen.ZoneOf(0x01, 0.1f) == 0 && screen.ZoneOf(0x01, 0.99f) == 3 && screen.ZoneOf(0x26, 0.1f) == 2, "keys go to the zone under them unless picked");
        Check(ScreenCapture.Boost(0x808080, 0) == 0xFFFFFF && ScreenCapture.Boost(0x050505, 50) == 0 && ScreenCapture.Boost(0x804020, 0) == 0xFF7F3F, "boost keeps the hue and brightens to full");
        int boosted = ScreenCapture.Boost(0x806060, 100);
        Check((boosted & 0xFF) < 0x60 * 255 / 0x80, "boost adds saturation");
        Check(ScreenCapture.Blend(0x000000, 0xFF0000, 0.5) == 0x800000, "blend mixes halfway");

        var perKey = new FakeHidpp();
        var groups = new Dictionary<int, List<int>> { { 0xFF0000, Enumerable.Range(0x01, 20).ToList() }, { 0x0000FF, new List<int> { 0x61, 0x6F } } };
        Check(KeyLights.Present(perKey).Count == 94, "the fake reports 94 lighting ids");
        KeyLights.Send(perKey, groups);
        Check(perKey.Keys.Count == 22 && perKey.Keys[0x14] == 0xFF0000 && perKey.Keys[0x6F] == 0x0000FF && perKey.Frames == 1, "per-key colours go out in groups of 13 and end with a frame");
        Check(perKey.Call(perKey.F(0x4540), 0)[0] == 0x0D, "the fake keyboard reports a Swiss layout");

        Console.WriteLine("selftest ok");
        return 0;
    }
}

class Recorder : Hidpp
{
    public readonly FakeMouse Inner;
    readonly Hidpp inner;
    readonly Dictionary<byte, int> ids = new Dictionary<byte, int>();
    public readonly List<Tuple<int, int>> Calls = new List<Tuple<int, int>>();

    public Recorder(Hidpp inner) : base(false) { this.inner = inner; Inner = inner as FakeMouse; }

    public override void Dispose() { }

    protected override byte[] Send(byte feat, int fn, byte[] p)
    {
        var r = inner.Call(feat, fn, p);
        if (feat == 0) { if (r[0] != 0) ids[r[0]] = p[0] << 8 | p[1]; }
        else Calls.Add(Tuple.Create(ids.ContainsKey(feat) ? ids[feat] : -1, fn));
        return r;
    }
}
