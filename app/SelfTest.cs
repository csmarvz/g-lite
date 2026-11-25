using System;
using System.Linq;
using System.Collections.Generic;

static class SelfTest
{
    static readonly string[] LayoutFiles =
        { "g515-tkl.json", "g915-tkl.json", "g915.json", "g915-x.json", "g915-x-tkl.json", "g815.json", "g512-x-75.json", "g-full.json", "g-60.json", "g213.json", "g413.json", "g510.json", "g-pro.json", "g410.json", "g810.json", "g910.json" };

    static bool LightsM3(FakeG510 keyboard)
    {
        G510Keys.LightBank(keyboard, 2);
        return keyboard.Features[0x04][1] == 0x20 && G510Keys.LitBank(keyboard, 0) == 2;
    }

    static byte[] Hex(string hex)
    {
        return Enumerable.Range(0, hex.Length / 2).Select(i => Convert.ToByte(hex.Substring(i * 2, 2), 16)).ToArray();
    }

    static void Check(bool ok, string what) { if (!ok) { Console.WriteLine("FAIL " + what); Environment.Exit(1); } }

    [STAThread]
    static int Main(string[] args)
    {
        Latency.Ms = 0;
        var factory = Hex("0101003c012cffffffffffffffffffffffffffffffffffffffffffffffff18ff008180aaff00006400000000000000000000000019ff008180aaff0000640000000000000000000000000300000000000000000010000104000200ffffffffffffffff");
        var factoryDir = Hex("5e380925010181014063996dec0101ff");
        var pyProfile = Hex("0101003c012cffffffffffffffffffffffffffffffffffffffffffffffff000000000000000000000001ffffff000000000000000000000000000000000000017f7f7f000000000000000000000000000000000010000104000200ffffffffffffffff");

        Check(G515Profile.Crc32(factoryDir.Skip(4).ToArray()) == 0x5e380925, "crc32");
        Check(G515Profile.BuildDirectory(factoryDir, pyProfile).SequenceEqual(Hex("8b5bf5610101810140639a0ad7d308ff")),
            "directory matches the one the keyboard accepted");

        var expected = (byte[])pyProfile.Clone();
        expected[45] = 0x02; expected[67] = 0x02;
        var white = new Lighting { Effect = 0x01, Color = 0xFFFFFF, Dim = 50, Idle = 60, Sleep = 300 };
        Check(G515Profile.BuildProfile(factory, white).SequenceEqual(expected), "static white profile");
        Check(G515Profile.Decode(factory) == null, "factory animation is unknown");

        foreach (var effect in G515Profile.Effects)
        {
            var lighting = new Lighting
            {
                Effect = effect.Id, Color = 0x3080FF, Period = effect.Speed ? (effect.MinPeriod + effect.MaxPeriod) / 2 : 5000,
                Direction = 6, Dim = 25, Idle = 0, Sleep = 900
            };
            var back = G515Profile.Decode(G515Profile.BuildProfile(factory, lighting));
            Check(back != null && back.Effect == lighting.Effect && back.Idle == 0 && back.Sleep == 900, effect.Name + " effect/timers");
            if (effect.Color) Check(back.Color == lighting.Color && back.Dim == 25, effect.Name + " color/dim");
            if (effect.Speed) Check(back.Period == lighting.Period, effect.Name + " period");
            if (effect.Direction) Check(back.Direction == 6, effect.Name + " direction");
        }
        var fake = new FakeHidpp();
        Check(G515Profile.Decode(G515Profile.Snapshot(fake).FactoryProfile) == null, "fake starts on factory profile");
        var blue = new Lighting { Effect = 0x0A, Color = 0x3080FF, Period = 3000, Dim = 25, Idle = 30, Sleep = 600 };
        G515Profile.Claim(fake);
        G515Profile.Preview(fake, blue);
        Check(fake.Live[1] == 2 && fake.Live[2] == 0x30, "preview sends breathe at effect index 2");
        G515Profile.Store(fake, blue);
        Check(G515Profile.Decode(G515Profile.Snapshot(fake).UserProfile).Same(blue), "store then read back");
        G515Profile.Store(fake, white);
        Check(G515Profile.Decode(G515Profile.Snapshot(fake).UserProfile).Same(white), "second store overwrites");

        Backups.Root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "glite-selftest-" + Guid.NewGuid().ToString("N"));
        var keyboard = new FakeHidpp();
        G515Profile.Store(keyboard, blue);
        Backups.Save("T", G515Profile.Read(keyboard));
        Backups.Save("T", G515Profile.Read(keyboard));
        var saved = Backups.List("T");
        Check(saved.Count == 1 && saved[0].First && Backups.Describe(saved[0].Files) == "Breathe",
            "first backup, and no copy of an unchanged keyboard");
        G515Profile.Store(keyboard, white);
        G515Profile.Restore(keyboard, saved[0].Files);
        Check(G515Profile.Decode(G515Profile.Snapshot(keyboard).UserProfile).Same(blue), "restoring a backup brings its lighting back");
        G515Profile.Restore(keyboard, new G515Profile.Files());
        Check(G515Profile.Decode(G515Profile.Snapshot(keyboard).UserProfile) == null,
            "restoring the factory lighting brings the factory animation back");
        keyboard.Flash[0x0008] = G515Profile.BuildProfile(keyboard.Flash[0x0008], white);
        bool half = false;
        try { G515Profile.Snapshot(keyboard); } catch (HalfWrittenException) { half = true; }
        Check(half && !G515Profile.FilesAreValid(G515Profile.Read(keyboard)), "a directory that doesn't match its profile is caught");
        G515Profile.Restore(keyboard, saved[0].Files);
        Check(G515Profile.Decode(G515Profile.Snapshot(keyboard).UserProfile).Same(blue), "a backup repairs a half written keyboard");
        var stranger = new G515Profile.Files
        {
            FactoryProfile = saved[0].Files.FactoryProfile, FactoryDir = (byte[])saved[0].Files.FactoryDir.Clone(),
            UserProfile = saved[0].Files.UserProfile, UserDir = saved[0].Files.UserDir
        };
        stranger.FactoryDir[11] ^= 1;
        bool refused = false;
        try { G515Profile.Restore(keyboard, stranger); } catch (RefusedException) { refused = true; }
        Check(refused, "a backup from another keyboard is refused");
        var foreign = G515Profile.Read(keyboard);
        foreign.UserDir = (byte[])foreign.UserDir.Clone();
        foreign.UserDir[15] = 0x00;
        bool other = false, offered = false;
        try { G515Profile.CheckFiles(foreign); } catch (HalfWrittenException) { offered = true; } catch (RefusedException) { other = true; }
        Check(other && !offered, "profiles G-Lite didn't write are refused, not offered for restore");
        System.IO.File.WriteAllLines(System.IO.Path.Combine(Backups.Folder("T"), "20250101-120000-000.txt"),
            System.IO.File.ReadAllLines(saved[0].File));
        Check(Backups.List("T").Any(entry => entry.When == new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Local).ToUniversalTime()),
            "backups named in local time still read");
        System.IO.File.Delete(System.IO.Path.Combine(Backups.Folder("T"), "20250101-120000-000.txt"));
        for (int i = 0; i < Backups.Keep + 3; i++)
        {
            G515Profile.Store(keyboard, new Lighting { Effect = 0x01, Color = i + 1 });
            Backups.Save("T", G515Profile.Read(keyboard));
        }
        saved = Backups.List("T");
        Check(saved.Count == Backups.Keep + 1 && saved.Count(entry => entry.First) == 1 && saved.Last().First,
            "the first backup stays, with the last ten");
        System.IO.Directory.Delete(Backups.Root, true);

        var dpis = MouseProtocol.DpiList(Hex("000064e0326400"));
        Check(dpis.Length == 511 && dpis[0] == 100 && dpis[1] == 150 && dpis.Last() == 25600, "DPI range with a step");
        Check(MouseProtocol.RateList(0x8B).SequenceEqual(new[] { 125, 250, 500, 1000 }), "report rates from their bits");
        foreach (var effect in MouseProtocol.Effects)
        {
            var light = new MouseLight { Effect = effect.Id, Color = 0x3080FF, Period = 4000, Bright = 50 };
            var back = MouseProtocol.Decode(new[] { effect.Id }.Concat(MouseProtocol.Encode(light)).ToArray());
            Check(back != null && back.Effect == effect.Id, effect.Name + " mouse effect");
            if (effect.Id != 0) Check(back.Bright == 50, effect.Name + " mouse brightness");
            if (effect.Color) Check(back.Color == 0x3080FF, effect.Name + " mouse color");
            if (effect.Speed) Check(back.Period == 4000, effect.Name + " mouse period");
        }
        var mouse = new FakeMouse();
        var mouseState = MouseProtocol.Read(mouse);
        Check(mouseState.Dpi == 1600 && mouseState.Stage == 2 && mouseState.Stages.SequenceEqual(new[] { 400, 800, 1600, 3200, 6400 })
            && mouseState.Onboard && mouseState.Rate == 1000
            && mouseState.Light != null && mouseState.Light.Effect == 0x03
            && mouseState.Light.Period == 8000 && mouseState.Zones.Count == 2,
            "mouse state read from its onboard profile");
        MouseProtocol.SetStage(mouse, 3);
        MouseProtocol.SetLight(mouse, mouseState, new MouseLight { Effect = 0x0A, Color = 0xFF0000, Period = 3000, Bright = 80 });
        Check(mouse.Dpi == 3200 && mouse.Zones[1][1] == 3 && mouse.Zones[1][2] == 0xFF && mouse.Zones[1][8] == 80,
            "mouse stage and lighting are set");
        var mouseRoundTrip = MouseSettings.FromText(
            new MouseSettings { Dpi = 1200, Light = new MouseLight { Effect = 0x0A, Color = 0x30D050, Period = 3000, Bright = 70 } }
                .ToText());
        Check(mouseRoundTrip.Dpi == 1200
            && mouseRoundTrip.Light.Same(new MouseLight { Effect = 0x0A, Color = 0x30D050, Period = 3000, Bright = 70 }),
            "a mouse profile round-trips through text");
        var real = Hex("01020290012003d007800c0019ffffffff00ffffffffffffffffffffffffffff8001000180010002800100048001000880010010900500008001000880010010ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff8001000180010002800100048001000880010010900500008001000880010010ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff440065006600610075006c0074000000000000000000000000000000000000000000000000000000000000000000000001ffffff0000000000000001ffffff00000000000000ffffffffffffffffffffffffffffffffffffffffffff00bfdd");
        Check(MouseProtocol.Valid(real) && MouseProtocol.Crc(real, 253) == 0xBFDD, "the profile checksum matches the real mouse");
        var mouseLight = new MouseLight { Effect = 0x0A, Color = 0xFF0000, Period = 3000, Bright = 80 };
        var built = MouseProtocol.Build(real, new MouseSettings { Dpi = 1200, Rate = 500, Light = mouseLight, Stage = 2 }, 2);
        var allowed = new HashSet<int>(new[] { 0, 1, 253, 254 }.Concat(Enumerable.Range(3, 10)).Concat(Enumerable.Range(0xD0, 22)));
        Check(MouseProtocol.Valid(built) && built[0] == 2 && built[1] == 2
            && MouseProtocol.StagesOf(built).SequenceEqual(new[] { 400, 800, 1200, 3200, 6400 })
            && built[0xD0] == 0x0A && built[0xDB] == 0x0A && Enumerable.Range(0, 255).All(i => allowed.Contains(i) || built[i] == real[i]),
            "a saved mouse profile changes only DPI, report rate and lighting");
        var picked = MouseProtocol.Build(real, new MouseSettings { Dpi = 800, Stage = 1 }, 2);
        Check(picked[1] == 1 && MouseProtocol.StagesOf(picked).SequenceEqual(MouseProtocol.StagesOf(real)) && picked[0] == real[0],
            "saving a DPI stage only makes it the default");
        var moved = MouseProtocol.Build(real, new MouseSettings { Dpi = 800, Stage = 2 }, 2);
        Check(moved[1] == 2 && MouseProtocol.StagesOf(moved).SequenceEqual(new[] { 400, 800, 800, 3200, 6400 }),
            "a stage dragged to another stage's value keeps its place");
        var secondMouse = new FakeMouse();
        var secondState = MouseProtocol.Read(secondMouse);
        Check(secondState.Savable && secondState.Sector == 1 && secondState.SectorSize == 255 && secondState.Format == 3,
            "the fake mouse can be saved to");
        MouseProtocol.Store(secondMouse, secondState, MouseProtocol.Build(secondState.Profile,
            new MouseSettings
            {
                Dpi = 3200, Rate = 500, Stage = 3,
                Light = new MouseLight { Effect = 0x01, Color = 0xFF2020, Bright = 100 }
            },
            2));
        var savedState = MouseProtocol.Read(secondMouse);
        Check(savedState.Dpi == 3200 && savedState.Rate == 500
            && savedState.Light != null && savedState.Light.Effect == 0x01 && savedState.Light.Color == 0xFF2020
            && secondMouse.Reloads == 1 && savedState.ProfileOk, "saving to the mouse sticks and reloads the profile");
        MouseProtocol.Store(secondMouse, savedState,
            MouseProtocol.Factory(MouseProtocol.ReadSector(secondMouse, MouseProtocol.RomSector, 255), savedState.Profile, savedState));
        var restoredState = MouseProtocol.Read(secondMouse);
        Check(restoredState.Dpi == 1600 && restoredState.Light.Effect == 0x03 && restoredState.ProfileOk
            && System.Text.Encoding.Unicode.GetString(restoredState.Profile, 0xA0, 14) == "Default",
            "the factory profile comes back with the profile's name");
        int refusals = 0;
        foreach (var sector in new[] { 0x0000, 0x0101 })
        {
            try { MouseProtocol.WriteSector(secondMouse, sector, restoredState.Profile); } catch (RefusedException) { refusals++; }
        }
        var broken = (byte[])restoredState.Profile.Clone();
        broken[5] ^= 1;
        try { MouseProtocol.WriteSector(secondMouse, 1, broken); } catch (RefusedException) { refusals++; }
        Check(refusals == 3 && MouseProtocol.Valid(secondMouse.Sectors[1]) && MouseProtocol.Valid(secondMouse.Sectors[0x0101]) == false,
            "the directory, the factory profile and bad checksums are never written");
        Check(!restoredState.Fresh, "a mouse with a profile isn't set up again");
        var fresh = new FakeMouse(true);
        var freshFactory = (byte[])fresh.Sectors[0x0101].Clone();
        var freshState = MouseProtocol.Read(fresh);
        Check(freshState.Fresh && freshState.Writable && freshState.Savable && freshState.Sector == 0x0101,
            "a mouse with only its factory profile can be saved to");
        int freshRefusals = 0;
        try { MouseProtocol.Store(fresh, freshState, MouseProtocol.Build(freshState.Profile, new MouseSettings { Dpi = 800 }, 2)); }
        catch (RefusedException) { freshRefusals++; }
        try { MouseProtocol.Setup(secondMouse, restoredState, restoredState.Profile); } catch (RefusedException) { freshRefusals++; }
        try { MouseProtocol.Directory(255, 0); } catch (RefusedException) { freshRefusals++; }
        Check(freshRefusals == 3 && !fresh.Sectors.ContainsKey(0x0001) && !fresh.Sectors.ContainsKey(0x0000),
            "setting up is refused on a mouse that has a profile, and a save never writes the factory profile");
        MouseProtocol.Setup(fresh, freshState,
            MouseProtocol.Build(freshState.Profile, new MouseSettings { Dpi = 3200, Rate = 500, Stage = 3 }, 2));
        var setUpState = MouseProtocol.Read(fresh);
        var writtenDirectory = fresh.Sectors[0x0000];
        Check(setUpState.Sector == 1 && !setUpState.Fresh && setUpState.Savable
            && setUpState.Dpi == 3200 && setUpState.Rate == 500 && fresh.Reloads == 1
            && MouseProtocol.Valid(writtenDirectory) && writtenDirectory.Take(8).SequenceEqual(FakeHidpp.Hex("00010100FFFF0000"))
            && writtenDirectory.Skip(8).Take(245).All(value => value == 0xFF) && fresh.Sectors[0x0101].SequenceEqual(freshFactory),
            "the first save writes the profile and the directory, and leaves the factory profile as it was");

        var hero = new FakeMouse { Info = "01020105050B1000FF0A01" };
        var heroState = MouseProtocol.Read(hero);
        MouseProtocol.Store(hero, heroState, MouseProtocol.Build(heroState.Profile, new MouseSettings { Dpi = 3200 }, 2));
        var heroFresh = new FakeMouse(true) { Info = "01020105050B1000FF0A01" };
        heroFresh.Sectors[0x0000] = new byte[255];
        var heroFreshState = MouseProtocol.Read(heroFresh);
        MouseProtocol.Setup(heroFresh, heroFreshState, MouseProtocol.Build(heroFreshState.Profile, new MouseSettings { Dpi = 800 }, 2));
        Check(heroState.Savable && MouseProtocol.Read(hero).Dpi == 3200 && heroFreshState.Fresh && heroFreshState.Savable
            && MouseProtocol.Valid(heroFresh.Sectors[0x0000]) && MouseProtocol.Read(heroFresh).Sector == 1
            && !MouseProtocol.Read(new FakeMouse { Info = "01010105050B1000FF0A01" }).Writable
            && MouseProtocol.Unwritten(Enumerable.Repeat((byte)0xFF, 255).ToArray()) && !MouseProtocol.Unwritten(restoredState.Profile),
            "profile format 2 (G502 HERO) saves like format 3, a directory of zeros counts as none, format 1 stays read-only");

        var headset = new FakeHeadset();
        var headsetState = HeadsetProtocol.Read(headset, true);
        Check(headsetState.HasSidetone && headsetState.HasOff && headsetState.Set.Sidetone == 30 && headsetState.Set.AutoOffMinutes == 30
            && headsetState.Millivolts == 3926 && !headsetState.Charging, "headset sidetone, auto-off and battery are read");
        HeadsetProtocol.SetSidetone(headset, 55);
        HeadsetProtocol.SetOff(headset, 10);
        Check(headset.Sidetone == 55 && headset.AutoOffMinutes == 10, "headset sidetone and auto-off are set");
        var headsetRoundTrip = HeadsetSettings.FromText(new HeadsetSettings { Sidetone = 20, AutoOffMinutes = 15 }.ToText());
        Check(headsetRoundTrip.Sidetone == 20 && headsetRoundTrip.AutoOffMinutes == 15, "a headset profile round-trips through text");
        Check(new FoundDevice { Name = "G Pro Wireless Gaming Mouse" }.SettingsKey == "gprowireless", "profile names for a device");
        var layout = KeyLayout.Load("g515-tkl.json");
        Check(layout.Name == "G515 LIGHTSPEED TKL" && layout.Rows.Length == 9 && layout.Rows[8].EndsWith("→"), "keyboard model loads");
        var game = new Blocks();
        for (int y = 0; y < Game.Rows; y++) game.Cells[y, Game.Columns - 1] = 1;
        game.Cells[0, Game.Columns - 2] = 2;
        game.ClearFull();
        Check(game.Score == 1 && game.Cells[0, Game.Columns - 1] == 2 && game.Cells[0, Game.Columns - 2] == 0,
            "full column clears and shifts");
        game.Key(System.Windows.Forms.Keys.Space);
        Check(!game.Over, "piece locks after drop");
        var rects = new System.Drawing.RectangleF[Game.Columns * Game.Rows];
        var cells = new List<System.Drawing.Point>[rects.Length];
        for (int i = 0; i < rects.Length; i++)
        {
            rects[i] = new System.Drawing.RectangleF(i % Game.Columns * 40, i / Game.Columns * 40, 36, 36);
            cells[i] = new List<System.Drawing.Point> { new System.Drawing.Point(i % Game.Columns, i / Game.Columns) };
        }
        var snake = new Snake(cells);
        snake.Key(System.Windows.Forms.Keys.Left);
        snake.Step();
        Check(!snake.Over && snake.At(2, 5) != 0, "snake ignores reversing into itself");
        for (int i = 0; i < Game.Columns - 5; i++) snake.Step();
        Check(!snake.Over && snake.At(2, 0) != 0, "snake wraps through the wall");
        var mines = new Mines(rects, cells);
        mines.Open(2 * Game.Columns + 7);
        Check(!mines.Over && mines.Opened(2 * Game.Columns + 7) && mines.MinesAround(2 * Game.Columns + 7) == 0,
            "first open is never a mine");
        mines = new Mines(rects, cells) { Placed = true };
        mines.Mine[0] = true;
        mines.Open(rects.Length - 1);
        Check(mines.Over && mines.Score == rects.Length - 1, "opening an empty key floods the board and wins");
        mines = new Mines(rects, cells) { Placed = true };
        mines.Mine[0] = true;
        mines.Open(0);
        Check(mines.Over && mines.Score == 0 && mines.At(0, 0) == 0xFF3030, "opening a mine ends the game and shows it");
        var wide = new[]
        {
            new System.Drawing.RectangleF(0, 0, 76, 36), new System.Drawing.RectangleF(80, 0, 36, 36),
            new System.Drawing.RectangleF(20, 40, 36, 36)
        };
        var wideCells = new[]
        {
            new List<System.Drawing.Point> { new System.Drawing.Point(0, 0), new System.Drawing.Point(1, 0) },
            new List<System.Drawing.Point> { new System.Drawing.Point(2, 0) },
            new List<System.Drawing.Point> { new System.Drawing.Point(0, 1) }
        };
        mines = new Mines(wide, wideCells) { Placed = true };
        mines.Mine[1] = true;
        Check(mines.MinesAround(0) == 1 && mines.MinesAround(2) == 0, "neighbours follow the keys, not the grid");
        Check(Updater.IsNewerVersion("0.0.2-alpha", "0.0.1-alpha") && Updater.IsNewerVersion("0.0.1", "0.0.1-alpha")
            && Updater.IsNewerVersion("0.0.1-beta", "0.0.1-alpha")
            && Updater.IsNewerVersion("v0.1.0", "0.0.9")
            && !Updater.IsNewerVersion("0.0.1-alpha", "0.0.1-alpha") && !Updater.IsNewerVersion("0.0.1-alpha", "0.0.1")
            && !Updater.IsNewerVersion("0.0.9", "0.0.10"), "versions compare");
        var releases = Updater.Releases(
            "# Changelog\r\n\r\n## [Unreleased]\r\n\r\n## [0.0.3-alpha] - 2026-11-12\r\n\r\n### Added\r\n\r\n- One\r\n- Two\r\n\r\n## [0.0.2-alpha] - 2026-11-01\r\n\r\n### Fixed\r\n\r\n- Three\r\n\r\n## [0.0.1-alpha] - 2026-10-20\r\n\r\n- Four\r\n");
        var newer = Updater.Between(releases, "0.0.1-alpha", "0.0.3-alpha");
        Check(releases.Count == 4 && newer.Count == 2 && newer[0].Version == "0.0.3-alpha" && newer[0].Date == "12 November 2026"
            && string.Join("|", newer[0].Lines) == "### Added|- One|- Two" && newer[1].Version == "0.0.2-alpha",
            "changelog sections between two versions");
        Check(Updater.TagFrom("https://github.com/csmarvz/g-lite/releases/tag/v0.0.2-alpha") == "v0.0.2-alpha"
            && Updater.TagFrom("https://github.com/csmarvz/g-lite/releases") == null, "latest tag read from the redirect");
        Settings.Location = System.IO.Path.GetTempFileName();
        Settings.Init();
        Check(Settings.Get("check-updates") == "1" && Settings.Get("skip-version") == "", "settings file starts with the defaults");
        Settings.Set("skip-version", "0.0.3-alpha");
        Check(Settings.Get("skip-version") == "0.0.3-alpha"
            && System.IO.File.ReadAllText(Settings.Location).Contains("skip-version=0.0.3-alpha"),
            "settings are written to the file");
        int setBright;
        var set = Lighting.FromText(new Lighting { Effect = 0x16, Color = 0x3080FF, Period = 2500, Direction = 6 }.ToText(40),
            out setBright);
        Check(set.Same(new Lighting { Effect = 0x16, Color = 0x3080FF, Period = 2500, Direction = 6 }) && setBright == 40,
            "a set round-trips through text");
        var audio = new Lighting { Effect = 0xE2, Color = 0x3080FF, Intensity = 100, Solid = true };
        int audioBright;
        Check(audio.ToText(40) == "226,3080FF,5000,1,50,60,300,40,100,1" && Lighting.FromText(audio.ToText(40), out audioBright).Same(audio)
            && new Lighting { Intensity = 30 }.ToText(40).EndsWith(",40,30") && new Lighting().ToText(40).EndsWith(",300,40"),
            "the intensity and Solid are written only when set, and read back");
        var full = new FoundDevice { BatteryId = 0x1001 };
        Devices.Decode(full, new byte[] { 0x10, 0x72, 0x81 });
        var charging = new FoundDevice { BatteryId = 0x1001 };
        Devices.Decode(charging, new byte[] { 0x10, 0x72, 0x80 });
        var unplugged = new FoundDevice { BatteryId = 0x1001 };
        Devices.Decode(unplugged, new byte[] { 0x0F, 0x3C, 0x00 });
        Check(full.Charging && full.Battery == 100 && charging.Charging && charging.Battery == 68
            && !unplugged.Charging && unplugged.Battery == 66 && unplugged.Millivolts == 3900,
            "battery voltage flags: charging, full, unplugged");
        var good = new FoundDevice { Battery = 30, BatteryGoodOrLowOnly = true };
        var low = new FoundDevice { Battery = 5, BatteryGoodOrLowOnly = true };
        Check(good.Level == 100 && good.BatteryText == "" && low.Level == 5 && low.BatteryText == "",
            "a battery that only reports good or low shows a full or low icon, without a percentage");
        var pro = new FoundDevice { Name = "G Pro Wireless Gaming Mouse", Battery = 95 };
        Devices.Remember(pro, 1000);
        pro = new FoundDevice { Name = "G Pro Wireless Gaming Mouse", Battery = 68, Charging = true };
        Devices.Remember(pro, 1010);
        var later = new FoundDevice { Name = "G Pro Wireless Gaming Mouse", Battery = 68, Charging = true };
        Devices.Remember(later, 1013);
        var stale = new FoundDevice { Name = "G Pro Wireless Gaming Mouse", Battery = 30 };
        Devices.Remember(stale, 2000);
        stale = new FoundDevice { Name = "G Pro Wireless Gaming Mouse", Battery = 68, Charging = true };
        Devices.Remember(stale, 2100);
        Check(pro.Battery == 95 && later.Battery == 98 && stale.Battery == 68,
            "a charging battery keeps the level it had before and counts up");
        Settings.Set("set.Night", "1,FFFFFF,5000,1,50,60,300,20");
        Settings.Remove("set.Night");
        Check(!Settings.Keys().Contains("set.Night"), "a removed set is gone from the file");
        Settings.Set("set.Evening", "1,FF8000,5000,1,50,60,300,40");
        Settings.Set("profile.g515.Day", "1,FFFFFF,5000,1,50,60,300,100");
        Settings.Set("set.Day", "1,000000,5000,1,50,60,300,0");
        Settings.Init();
        Check(Settings.Get("profile.g515.Evening").StartsWith("1,FF8000") && Settings.Get("profile.g515.Day").StartsWith("1,FFFFFF")
            && !Settings.Keys().Any(key => key.StartsWith("set.")),
            "old sets move to G515 profiles without overwriting");
        Check(FoundDevice.LooksG("G515 LIGHTSPEED TKL") && FoundDevice.LooksG("G Pro Wireless Gaming Mouse")
            && FoundDevice.LooksG("PRO X Wireless Gaming Headset")
            && FoundDevice.LooksG("G502 X PLUS")
            && !FoundDevice.LooksG("MX KEYS S") && !FoundDevice.LooksG("Logitech USB Receiver") && !FoundDevice.LooksG(""),
            "G devices are told from other Logitech devices");
        Check(new FoundDevice { Name = "G Pro Wireless Gaming Mouse" }.ShortName == "G Pro Wireless"
            && new FoundDevice { Name = "G515 LIGHTSPEED TKL" }.ShortName == "G515 TKL", "short device names");
        Check(FoundDevice.FromVolts(4200) == 100 && FoundDevice.FromVolts(3300) == 0 && FoundDevice.FromVolts(3800) == 50,
            "battery estimate from the voltage");
        Check(FoundDevice.FromChargingVolts(4096) == 44 && FoundDevice.FromChargingVolts(4200) == 68
            && FoundDevice.FromChargingVolts(4300) == 68 && FoundDevice.FromChargingVolts(3000) == 2,
            "battery estimate from the charging voltage");
        Check(new FoundDevice { Name = "G515 LIGHTSPEED TKL", Features = new[] { 0x8071, 0x8040, 0x8101 } }.IsG515
            && !new FoundDevice { Name = "G515 LIGHTSPEED TKL", Features = new[] { 0x8071 } }.IsG515, "the G515 needs its three features");
        Check(new FoundDevice { Name = "G512 X 75", Features = new[] { 0x8071, 0x8040, 0x8081, 0x8101 } }.IsG512X
            && !new FoundDevice { Name = "G512 X 98", Features = new[] { 0x8071, 0x8040, 0x8081, 0x8101 } }.IsG512X
            && !new FoundDevice { Name = "G512 X 75", Features = new[] { 0x8071 } }.IsG512X,
            "the G512 X 75 is told apart, with its features");
        var g512 = new FoundDevice { Name = "G512 RGB MECHANICAL GAMING KEYBOARD", Kind = "keyboard", Features = new[] { 0x8070, 0x8080 } };
        var g213 = new FoundDevice { Name = "Gaming Keyboard G213", Kind = "keyboard", Features = new[] { 0x8070 } };
        Check(new FoundDevice { Link = "USB", Kind = "keyboard" }.Level == Ui.Usb
            && new FoundDevice { Link = "USB", Kind = "keyboard", Battery = 80 }.Level == 80
            && new FoundDevice { Link = "LIGHTSPEED" }.Level == -1
            && new FoundDevice { Link = "USB", Kind = "headset", PlainHid = true }.Level == -1
            && new FoundDevice { Link = "USB", Asleep = true }.Level == Ui.Asleep,
            "a wired device without a battery shows the USB mark, a wireless one without a level shows nothing");
        Check(g213.IsG && g213.ShortName == "G213" && DeviceTable.For(g213).Screen == typeof(ZonesPage)
            && !FoundDevice.LooksG("Gaming Keyboard") && !FoundDevice.LooksG("Wireless Keyboard K270")
            && ZoneLighting.ZoneName(1, 0) == "Keys" && ZoneLighting.ZoneName(8, 3) == "Zone 3" && ZoneLighting.ZoneName(0, 1) == "Zone 2",
            "the G213 counts as a G device with its model last, and its zones are named from their location");
        var wave = new MouseLight { Effect = 0x04, Period = 0x1388, Bright = 80, Direction = 3 };
        Check(ZoneLighting.Encode(wave).SequenceEqual(new byte[] { 0, 0, 0, 0, 0, 0, 0x88, 3, 80, 0x13 })
            && ZoneLighting.FromText(ZoneLighting.ToText(new[] { wave }), 1)[0].Same(wave)
            && ZoneLighting.FromText("1,FF0000,5000,100", 1)[0].Direction == 6
            && KeyLayout.Load("g213.json").KeyIds().All(zone => zone >= 1 && zone <= 5)
            && KeyLayout.Load("g213.json").Iso().KeyIds().All(zone => zone >= 1 && zone <= 5),
            "0x8070 Wave is encoded with its direction and kept in text, and every G213 key has a zone");
        int dpiShift = Array.FindIndex(Bindings.MouseActions, action => action.Name == "DPI shift"),
            disabled =Array.FindIndex(Bindings.MouseActions, action => action.Name == "Disabled");
        var mouseActions = Bindings.MouseActions;
        Check(Bindings.Find(mouseActions, Hex("9007FFFF")) == dpiShift && Bindings.Find(mouseActions, Hex("9007FF00")) == dpiShift
            && Bindings.Find(mouseActions, Hex("8000FFFF")) == disabled && Bindings.Find(mouseActions, Hex("80010005")) < 0
            && Bindings.Describe(Hex("900AFFFF")) == "Cycle profile" && Bindings.Describe(Hex("900D0003")) == "Profile 3"
            && Bindings.Describe(Hex("90000000")) == "Disabled" && Bindings.Find(Bindings.KeyboardActions, Hex("9000FF00")) >= 0
            && Bindings.Names(new FoundDevice { Models = "USB C08B" }, 11)[5] == "Sniper",
            "factory special bindings (90 xx FF FF) are named, and the G502 HERO gets the G502's button names");
        Check(g512.IsLedKeyboard && g512.ShortName == "G512" && DeviceTable.For(g512).Screen == typeof(ZonesPage)
            && !new FoundDevice { Name = "G515 LIGHTSPEED TKL", Kind = "keyboard", Features = new[] { 0x8070, 0x8071 } }.IsLedKeyboard
            && !new FoundDevice { Name = "G502 HERO Gaming Mouse", Kind = "mouse", Features = new[] { 0x8070 } }.IsLedKeyboard,
            "a keyboard with 0x8070 and no 0x8071 gets the zone lighting screen");
        var g413 = new FoundDevice
        {
            Name = "G413 Silver Mechanical Gaming Keyboard", Kind = "keyboard", Features = new[] { 0x4540, 0x8060, 0x8070 }
        };
        bool g413Monochrome, speakersMonochrome;
        var g413Zones = ZoneLighting.Zones(FakeSpeaker.G413Silver(), out g413Monochrome);
        ZoneLighting.Zones(new FakeSpeaker(), out speakersMonochrome);
        Check(g413.ShortName == "G413 Silver" && g413.IsLedKeyboard && DeviceTable.For(g413).Screen == typeof(ZonesPage)
            && g413Monochrome && !speakersMonochrome && g413Zones.Count == 1 && g413Zones[0][ZoneLighting.Place] == 1
            && g413Zones[0].ContainsKey(0x0A) && !g413Zones[0].ContainsKey(0x03),
            "a G413 Silver is named without Mechanical and gets the zones screen; its 0x8070 says monochrome (fn 0 byte 4 bit 3)");
        var g513 = FakeSpeaker.G512();
        var g513Sent = new List<string>();
        Hidpp.Tap = line => g513Sent.Add(line);
        ZoneLighting.SendKeys(g513, new Dictionary<int, int>
        {
            { 0x01, 0xFF0000 }, { 0x02, 0x00FF00 }, { 0x03, 0x0000FF }, { 0x68, 0x101010 }, { -1, 0xFFFFFF }
        });
        Hidpp.Tap = null;
        Check(g513.KeyColors.Count == 4 && g513.KeyColors[0x04] == 0xFF0000 && g513.KeyColors[0x06] == 0x0000FF
            && g513.KeyColors[0xE0] == 0x101010 && g513.Frames == 1 && g513Sent.Count == 3
            && g513Sent[0] == "8080 fn3  00 01 00 03 04 FF 00 00 05 00 FF 00 06 00 00 FF" && g513Sent[2] == "8080 fn5",
            "0x8080 colours go three keys to a message by USB usage (modifiers E0-E7), and fn 5 shows them");
        var g512Device = new FoundDevice
        {
            Name = "G512 RGB MECHANICAL GAMING KEYBOARD", Kind = "keyboard", Features = new[] { 0x4540, 0x8060, 0x8070, 0x8080 }
        };
        Check(ZoneLighting.Picture(g512Device) == "g413.json" && ZoneLighting.Picture(new FoundDevice { Name = "G513", Kind = "keyboard" }) == "g413.json"
            && ZoneLighting.FromText("227,FF0000,2000,80,7", 2)[0].Direction == 7 && ZoneLighting.FromText("227,FF0000,2000,80,4", 2)[0].Direction == 2,
            "the G512 and G513 get the G413's picture; a live effect's direction reads back, and a wrong one is its default");
        Check(new FoundDevice
            {
                Name = "G815 RGB MECHANICAL GAMING KEYBOARD", Kind = "keyboard", Features = new[] { 0x8071, 0x8040, 0x8081, 0x8100 }
            }.IsG915,
            "the G815 is taken as a wired G915");
        var rapid = new FoundDevice { Name = "G515 RAPID TKL", Kind = "keyboard", Features = new[] { 0x8071, 0x8040, 0x8101 } };
        var bigBoard = KeyboardKinds.For(new FoundDevice { Name = "G316 X 75", Kind = "keyboard" });
        Check(!rapid.IsG515 && rapid.IsRgbKeyboard && DeviceTable.For(rapid).Screen == typeof(KeyboardPage)
            && KeyboardKinds.For(rapid).Guessed && !KeyboardKinds.For(rapid).Saves && KeyboardKinds.For(rapid).Keys.Name == "G515 RAPID TKL"
            && KeyboardKinds.File("PRO X 60") == "g-60.json" && KeyboardKinds.File("G512 X 98") == "g-full.json"
            && KeyboardKinds.File("G915 X LIGHTSPEED TKL") == "g915-x-tkl.json" && KeyboardKinds.File("G915 X") == "g915-x.json"
            && KeyboardKinds.File("G715") == "g515-tkl.json"
            && bigBoard.Keys.Rows.Length == 7 && KeyboardKinds.For(new FoundDevice { Name = "G915 X LIGHTSPEED TKL" }).Keys.Rows.Length == 9
            && bigBoard.Bar == null && bigBoard.Prefix == "profile.g316x75.",
            "other G keyboards with 0x8071 get live lighting with a picture picked by their name, and never save");
        Check(Devices.Unfound(@"HID\VID_046D&PID_0AD8&MI_03\8&1A2B3C&0&0000",
                new string[] { null, @"\\?\hid#vid_046d&pid_c547&mi_02&col02#x" }) == "0AD8"
            && Devices.Unfound(@"HID\VID_046D&PID_0ABA&MI_03&COL02\8&1A2B3C&0&0001",
                new[] { @"\\?\hid#vid_046d&pid_0aba&mi_03&col02#x" }) == null
            && Devices.Unfound(@"HID\VID_046D&PID_C24C&MI_01&COL02\8&1A2B3C&0&0001", new string[0]) == "C24C"
            && Devices.Unfound(@"HID\VID_046D&PID_C066&MI_01&COL04\8&1A2B3C&0&0003",
                new[] { @"\\?\hid#vid_046d&pid_c066&mi_01&col04#x" }) == null
            && Devices.Unfound(@"HID\VID_046D&PID_C547&MI_02&COL02\8&1A2B3C&0&0001", new string[0]) == null,
            "a Logitech audio device or old G device the HID++ scan didn't find is listed for its report, others aren't");
        var superlight = FakeMouse.Superlight();
        var superlightMouse = new HidppMouse();
        var superlightState = superlightMouse.Read(superlight);
        var superlightBefore = (byte[])superlight.Sectors[1].Clone();
        var superlightButtons = superlightState.Buttons.Select(binding => (byte[])binding.Clone()).ToArray();
        superlightButtons[3] = FakeHidpp.Hex("90050000");
        superlightMouse.Save(superlight, superlightState, superlightBefore, new MouseSettings
        {
            Dpi = 1000, Stage = 1, Rate = 500, Buttons = superlightButtons, Light = new MouseLight { Effect = 0x01, Color = 0xFF0000 }
        });
        var superlightSaved = superlight.Sectors[1];
        var superlightFresh = new FakeMouse(true) { Info = "0104010501051000ff0a04" };
        superlightFresh.Sectors[0x0000] = new byte[255];
        var superlightFreshState = MouseProtocol.Read(superlightFresh);
        Check(superlightState.Format == 4 && superlightState.Savable && superlightState.Zones.Count == 0
            && MouseProtocol.Read(new FakeMouse()).Zones.Count == 2 && superlightMouse.HasFactory
            && superlightState.Stages.SequenceEqual(new[] { 400, 800, 1600, 3200, 6400 }) && superlightState.Buttons.Length == 5
            && MouseProtocol.Valid(superlightSaved) && superlight.Reloads == 1
            && BitConverter.ToString(superlightSaved, 0, 0x0D) == "02-01-02-90-01-E8-03-40-06-80-0C-00-19"
            && BitConverter.ToString(superlightSaved, 0x2C, 4) == "90-05-00-00"
            && superlightSaved.Skip(0x30).Take(0xFD - 0x30).SequenceEqual(superlightBefore.Skip(0x30).Take(0xFD - 0x30))
            && superlightSaved.Skip(0x0D).Take(0x2C - 0x0D).SequenceEqual(superlightBefore.Skip(0x0D).Take(0x2C - 0x0D))
            && !superlightFreshState.Fresh && !superlightFreshState.Writable,
            "a format 4 mouse (the original SUPERLIGHT) saves format 3's fields and keeps its LED records; its hidden 0x8070 shows no "
                + "lighting; one on its factory profile isn't set up");
        var g502xPlus = FakeMouse.G502XPlus();
        var g502xPlusBefore = (byte[])g502xPlus.Sectors[1].Clone();
        var g502xPlusMouse = new HidppMouse();
        var g502xPlusState = g502xPlusMouse.Read(g502xPlus);
        g502xPlusMouse.Save(g502xPlus, g502xPlusState, g502xPlusBefore,
            new MouseSettings { Dpi = 1000, Stage = 1, Rate = 500, Buttons = g502xPlusState.Buttons });
        var fastRateBuild = MouseProtocol.Build(g502xPlusBefore, new MouseSettings { Dpi = 800, Stage = 1, Rate = 2000 }, 0);
        Check(g502xPlusState.Format == 5 && g502xPlusState.Savable && g502xPlusMouse.HasFactory && g502xPlusState.Zones.Count == 0
            && BitConverter.ToString(g502xPlus.Sectors[1], 0, 7) == "02-01-02-20-03-E8-03"
            && g502xPlus.Sectors[1][0x12] == 0x00 && g502xPlus.Sectors[1][0x13] == 0x12
            && g502xPlus.Sectors[1].Skip(0x0D).Take(0xFD - 0x0D).SequenceEqual(g502xPlusBefore.Skip(0x0D).Take(0xFD - 0x0D))
            && fastRateBuild[0] == g502xPlusBefore[0],
            "a format 5 mouse (the G502 X PLUS) saves format 3's fields, keeps the write counter at 0x12 and its LED records, and a rate "
                + "above 1000 Hz leaves the interval byte alone");
        var proX2 = MouseProtocol.Read(FakeMouse.ProX2());
        Check(new FoundDevice { Name = "PRO X SUPERLIGHT 2", Kind = "mouse", Features = new[] { 0x2202, 0x8061 } }.IsMouse
            && proX2.Dpi == 800 && proX2.Dpis.First() == 100 && proX2.Dpis.Last() == 32000 && proX2.Dpis.Length == 639
            && proX2.Stages.SequenceEqual(new[] { 400, 800, 1600, 3200 }) && proX2.Stage == 1 && proX2.Rate == 1000
            && proX2.Rates.SequenceEqual(new[] { 125, 250, 500, 1000, 2000, 4000, 8000 })
            && proX2.Onboard && proX2.Savable && proX2.Format == 7 && new MouseProtocol.State().ProfileOk && proX2.ProfileOk
            && proX2.Buttons.Length == 5 && proX2.Buttons[4].SequenceEqual(FakeHidpp.Hex("80010010"))
            && new MouseProtocol.State { Onboard = true, Format = 6, SectorSize = 255, Sector = 1 }.Writable
            && new MouseProtocol.State { Onboard = true, Format = 8, SectorSize = 255, Sector = 1 }.Writable
            && !new MouseProtocol.State { Onboard = true, Format = 9, SectorSize = 255, Sector = 1 }.Writable
            && !new MouseProtocol.State { Onboard = true, Format = 10, SectorSize = 255, Sector = 1 }.Writable,
            "a format 7 mouse with 0x2202 and 0x8061 reads its DPI, stages, rate and buttons (from 0x30) and can be saved to, like 6 and 8; "
                + "9 and 10 can't");
        var proX2Profile = FakeMouse.ProX2().Sectors[1];
        var wideButtons = proX2.Buttons.Select(binding => (byte[])binding.Clone()).ToArray();
        wideButtons[3] = FakeHidpp.Hex("90050000");
        var wideProfile = MouseProtocol.BuildWide(proX2Profile, new MouseSettings { Dpi = 1800, Stage = 2, Rate = 2000, Buttons = wideButtons }, false);
        var wideWiredProfile = MouseProtocol.BuildWide(proX2Profile, new MouseSettings { Dpi = 800, Stage = 1, Rate = 500 }, true);
        Check(MouseProtocol.Valid(wideProfile) && wideProfile.Length == 255
            && BitConverter.ToString(wideProfile, 0, 0x1D) == "04-03-02-00-90-01-90-01-02-20-03-20-03-02-08-07-08-07-02-80-0C-80-0C-02-00-00-00-00-00"
            && BitConverter.ToString(wideProfile, 0x3C, 4) == "90-05-00-00"
            && wideProfile.Skip(0x44).Take(0xFD - 0x44).SequenceEqual(proX2Profile.Skip(0x44).Take(0xFD - 0x44))
            && wideWiredProfile[0] == 3 && wideWiredProfile[1] == 2 && wideWiredProfile[2] == 1
            && MouseProtocol.DescribeWide(wideProfile) == "1800 DPI",
            "a format 7 save changes the stage (X and Y), the default stage, the rate of the link in use and the buttons, nothing else");
        var apart = (byte[])proX2Profile.Clone();
        apart[0x0B] = 0x40; apart[0x0C] = 0x06;
        MouseProtocol.WriteChecksum(apart);
        var apartSame = MouseProtocol.BuildWide(apart, new MouseSettings { Dpi = 800, Stage = 1, Buttons = wideButtons }, false);
        var apartChanged = MouseProtocol.BuildWide(apart, new MouseSettings { Dpi = 1000, Stage = 1 }, false);
        var damagedMouse = FakeMouse.ProX2();
        damagedMouse.Sectors[1][0x40] ^= 0xFF;
        var damaged = MouseProtocol.Read(damagedMouse);
        var formatSix = FakeMouse.ProX2();
        formatSix.Info = "0106010501051000ff0a04";
        var formatTen = FakeMouse.ProX2();
        formatTen.Info = "010A010501101000ff0a04";
        var manyButtons = FakeMouse.ProX2();
        manyButtons.Info = "0108010501101000ff0a04";
        Check(BitConverter.ToString(apartSame, 0x09, 4) == "20-03-40-06"
            && BitConverter.ToString(apartChanged, 0x09, 4) == "E8-03-D0-07"
            && !damaged.ProfileOk && !damaged.Savable
            && MouseProtocol.Read(formatSix).Buttons.Length == 5 && MouseProtocol.Read(formatSix).Savable
            && MouseProtocol.Read(formatTen).Buttons == null && MouseProtocol.Read(formatTen).Stages.Length == 0
            && !MouseProtocol.Read(formatTen).Writable
            && MouseProtocol.Read(manyButtons).Buttons.Length == 12,
            "a format 7 stage is left alone unless its DPI changes, a Y apart from X keeps its ratio, a bad CRC isn't saved; format 6 "
                + "reads its buttons, format 10 shows no buttons or stages, and at most 12 buttons are read from 0x30");
        var millisecondRateMouse = FakeMouse.ProX2();
        millisecondRateMouse.Info = "0104010501051000ff0a04";
        Check(MouseProtocol.Read(millisecondRateMouse).Rates.SequenceEqual(new[] { 125, 250, 500, 1000 })
            && MouseProtocol.Read(FakeMouse.ProX2()).Rates.Last() == 8000,
            "a mouse whose profile keeps the rate in ms (formats 2 to 5) isn't offered rates above 1000 Hz, even with 0x8061");
        var wideFactoryMouse = FakeMouse.ProX2();
        var wideFactory = wideFactoryMouse.Sectors[0x0101];
        wideFactory[4] = 0x20; wideFactory[5] = 0x03; wideFactory[6] = 0x20; wideFactory[7] = 0x03;
        wideFactory[0xFD] = 0; wideFactory[0xFE] = 0;
        var wideNamed = (byte[])wideFactoryMouse.Sectors[1].Clone();
        System.Text.Encoding.Unicode.GetBytes("Mine").CopyTo(wideNamed, 0xA0);
        MouseProtocol.WriteChecksum(wideNamed);
        wideFactoryMouse.Sectors[1] = wideNamed;
        var wideMouse = new HidppMouse();
        var wideState = wideMouse.Read(wideFactoryMouse);
        wideMouse.Restore(wideFactoryMouse, wideState, wideNamed, null);
        var wideRestored = wideFactoryMouse.Sectors[1];
        Func<int, byte, bool> factoryRefused = (offset, value) =>
        {
            var bad = (byte[])wideFactory.Clone();
            bad[offset] = value;
            try { MouseProtocol.Factory(bad, wideNamed, wideState); return false; }
            catch (RefusedException) { return true; }
        };
        var noFactory = FakeMouse.ProX2();
        noFactory.Info = "0107010500051000ff0a04";
        var noFactoryMouse = new HidppMouse();
        noFactoryMouse.Read(noFactory);
        Check(wideMouse.HasFactory && MouseProtocol.Valid(wideRestored) && wideRestored[4] == 0x20 && wideRestored[5] == 0x03
            && System.Text.Encoding.Unicode.GetString(wideRestored, 0xA0, 8) == "Mine"
            && wideRestored.Take(0xA0).SequenceEqual(wideFactory.Take(0xA0))
            && wideRestored.Skip(0xD0).Take(0xFD - 0xD0).SequenceEqual(wideFactory.Skip(0xD0).Take(0xFD - 0xD0))
            && wideFactoryMouse.Reloads == 1
            && factoryRefused(0, 0xFF) && factoryRefused(1, 0x07) && factoryRefused(2, 0x04) && factoryRefused(5, 0x7D) && !factoryRefused(2, 0x03)
            && !noFactoryMouse.HasFactory && new HidppMouse().HasFactory,
            "a format 7 factory restore copies the factory profile (which has no CRC) with the profile's name and a new CRC; it refuses "
                + "one whose rate bytes aren't 0x8061 indexes, whose default stage isn't one of its stages or whose stages are out of "
                + "the mouse's range, and isn't offered when the mouse has no factory profile");
        var g300Report = new FakeG300().Profiles[1];
        var g300 = G300Mouse.Decode(g300Report);
        Check(!g300.ReadOnly && g300.SaveOnly && g300.Writable && g300.Stages.SequenceEqual(new[] { 1000, 2000, 2500, 2000 })
            && g300.Stage == 1 && g300.Dpi == 2000 && g300.Rate == 500 && g300.Light.Effect == 0x01 && g300.Light.Color == 0x0000FF
            && g300.Buttons.Length == 9 && g300.Buttons[5].SequenceEqual(FakeHidpp.Hex("80020106"))
            && g300.Buttons[8].SequenceEqual(FakeHidpp.Hex("90070000")) && new G300Mouse().DefaultStage(g300Report) == 1
            && G300Mouse.Build(g300Report, new MouseSettings
                {
                    Dpi = g300.Dpi, Stage = g300.Stage, Rate = g300.Rate, Light = g300.Light, Buttons = g300.Buttons
                }).SequenceEqual(g300Report)
            && G300Mouse.Entry(FakeHidpp.Hex("80010010")).SequenceEqual(FakeHidpp.Hex("050000"))
            && G300Mouse.Entry(FakeHidpp.Hex("900A0000")).SequenceEqual(FakeHidpp.Hex("0D0000"))
            && G300Mouse.Entry(FakeHidpp.Hex("80020106")).SequenceEqual(FakeHidpp.Hex("000106"))
            && G300Mouse.Entry(FakeHidpp.Hex("90010000")) == null
            && G300Mouse.ColorBits(new MouseLight { Color = 0xFFD000 }) == 3 && G300Mouse.ColorBits(new MouseLight { Color = 0x202020 }) == 7
            && G300Mouse.ColorBits(new MouseLight { Color = 0x7F0000, Bright = 40 }) == 1
            && G300Mouse.ColorBits(new MouseLight { Effect = 0x00 }) == 0
            && G300Mouse.Entry(FakeHidpp.Hex("90080000")).SequenceEqual(FakeHidpp.Hex("0D0000"))
            && G300Mouse.Binding(FakeHidpp.Hex("0D0000"), 0).SequenceEqual(FakeHidpp.Hex("90080000"))
            && g300.Effects.Select(effect => effect.Id).SequenceEqual(new byte[] { 0x00, 0x01 })
            && G300Mouse.Decode(FakeHidpp.Hex("F302000284060A40" + new string('0', 54))).Stage == 1,
            "G300: the profile report decodes (DPI × 250, the 0x80 default, rate codes, colour bits, buttons), builds back to the "
                + "same bytes, and maps bindings both ways");
        bool g300Refused = false;
        try { G300Mouse.Build(g300Report, new MouseSettings { Dpi = 2600, Stage = 0 }); }
        catch (RefusedException) { g300Refused = true; }
        var rightChanged = g300.Buttons.Select(binding => (byte[])binding.Clone()).ToArray();
        rightChanged[1] = FakeHidpp.Hex("80010008");
        bool rightRefused = false;
        try { G300Mouse.Build(g300Report, new MouseSettings { Dpi = g300.Dpi, Stage = g300.Stage, Buttons = rightChanged }); }
        catch (RefusedException) { rightRefused = true; }
        Check(rightRefused, "G300: a change to the right button is refused, not dropped");
        Check(g300Refused, "G300: a DPI it can't take is refused");
        var unreadLevel = (byte[])g300Report.Clone();
        // Byte 4 is level 2, the default: 0x80 alone reads as 0 DPI.
        unreadLevel[4] = 0x80;
        Check(G300Mouse.Build(unreadLevel, new MouseSettings { Dpi = 0, Stage = 1, Light = new MouseLight { Effect = 0x01, Color = 0xFF0000 } })[4]
                == 0x80,
            "G300: a level read outside 250 to 2500 DPI stays as it was when a save changes something else");
        bool otherProfileRefused = false;
        try { new G300Mouse().Save(new FakeG300 { Profile = 0 }, g300, g300Report, new MouseSettings { Dpi = g300.Dpi, Stage = g300.Stage }); }
        catch (RefusedException) { otherProfileRefused = true; }
        Check(otherProfileRefused, "G300: Save refuses when the mouse's profile button moved it off the profile shown");
        var g600Report = new FakeG600().Modes[2];
        var g600 = G600Mouse.Decode(g600Report, 3);
        Check(g600.ReadOnly && !g600.Writable && g600.Stages.SequenceEqual(new[] { 400, 1200, 2000, 3200 }) && g600.Stage == 3
            && g600.Dpi == 3200 && g600.Rate == 1000 && g600.Light.Effect == 0x0A && g600.Light.Color == 0x00FF00
            && g600.Light.Period == 4000 && g600.Buttons.Length == 20 && new G600Mouse().DefaultStage(g600Report) == 1
            && G600Mouse.Binding(FakeHidpp.Hex("070000"), 0).SequenceEqual(FakeHidpp.Hex("80010040"))
            && G600Mouse.Binding(FakeHidpp.Hex("140000"), 0).SequenceEqual(FakeHidpp.Hex("900A0000"))
            && G600Mouse.Binding(FakeHidpp.Hex("170000"), 0).SequenceEqual(FakeHidpp.Hex("900B0000"))
            && G600Mouse.Binding(FakeHidpp.Hex("000205"), 0).SequenceEqual(FakeHidpp.Hex("80020205"))
            && G600Mouse.Binding(FakeHidpp.Hex("000000"), 0).SequenceEqual(FakeHidpp.Hex("80000000"))
            && G600Mouse.Decode(new FakeG600().Modes[0], 1).Light.Effect == 0x03
            && G600Mouse.Decode(new byte[G600Mouse.ReportLength], 0).Light.Effect == 0x00,
            "a G600 mode report reads as DPI levels, rate, lighting (solid black is off) and its 20 buttons as HID++ bindings");
        Check(MouseProtocol.WideStages(FakeHidpp.Hex("03030100" + "9001900102" + "FFFF") .Concat(new byte[40]).ToArray(),
                new[] { 100, 32000 }).SequenceEqual(new[] { 400 })
            && MouseProtocol.WideStages(FakeHidpp.Hex("03030100" + "0A00000002").Concat(new byte[40]).ToArray(),
                new[] { 100, 32000 }).Length == 0,
            "DPI stages outside the mouse's range mean the profile isn't the layout we know");
        Check(new FoundDevice { Name = "G560 Gaming Speaker", Features = new[] { 0x8070, 0x8040 } }.IsG560
            && !new FoundDevice { Name = "G560 Gaming Speaker", Features = new[] { 0x8040 } }.IsG560,
            "the G560 needs its lighting feature");
        Check(new FoundDevice { Name = "Litra Beam LX", Features = new[] { 0x1990 } }.IsLight
            && new FoundDevice { Path = @"\?\hid#vid_046d&pid_c903&col02", Features = new[] { 0x1990 } }.IsLight
            && !new FoundDevice { Name = "Litra Beam LX" }.IsLight, "a Litra is found by its name or product id, with its light feature");
        Check(DeviceTable.For(new FoundDevice { Name = "MX Master 3", Kind = "mouse", Features = new[] { 0x2201, 0x8100 } })
                == DeviceTable.Other
            && DeviceTable.For(new FoundDevice { Name = "G Pro Wireless Gaming Mouse", Kind = "mouse", Features = new[] { 0x2201 } }).Screen
                == typeof(MousePage)
            && DeviceTable.For(new FoundDevice { Name = "M720 Triathlon", Kind = "mouse", Features = new[] { 0x8100, 0x1B04 } }) == null
            && DeviceTable.For(new FoundDevice
                {
                    Name = "K295 Silent Wireless Keyboard", BatteryGoodOrLowOnly = true, Features = new[] { 0x40A0, 0x1814 }
                }) == null,
            "a device that isn't a G device gets the other devices' screen only with a setting it can change, and one with media keys keeps the info page");
        Check(OtherSettings.FromText("dpi:1600,shift:255,invert:1,level:3,bogus:2").ToText() == "dpi:1600,shift:255,invert:1,level:3"
            && OtherSettings.FromText("").ToText() == "", "other devices' settings read back as written, skipping what isn't known");
        {
            var mxMaster = new FoundDevice { Name = "MX Master 3S", Kind = "mouse", Features = new[] { 0x2110, 0x2121, 0x2201 } };
            var device = new FakeOther { RefuseDpi = true };
            var read = OtherDevice.Read(device, mxMaster);
            var want = read.Set.Clone();
            want.Merge(OtherDevice.KeepSupported(OtherSettings.FromText("dpi:1625,invert:1,shift:120,thumb:1,bright:30,level:2"), read));
            var refusedSettings = OtherDevice.Apply(device, read, want, read.Set);
            Check(want.ToText() == "dpi:1600,shift:10,invert:1" && refusedSettings.SequenceEqual(new[] { OtherSettings.Dpi })
                && device.Wheel == 0x04 && device.Shift == 10,
                "other devices: a setting the device lacks or out of range isn't sent, and a refused one doesn't stop the others");
        }
        var lightState = new LightState
        {
            FrontOn = true, Lumen = 120, Kelvin = 5000,
            BackOn = false, BackLevel = 40, Colors = new[] { 1, 2, 3, 4, 5, 6, 0xFF0000 }
        };
        var lightCopy = new LightState();
        Check(lightCopy.ReadFront(lightState.FrontText()) && lightCopy.ReadBack(lightState.BackText())
            && lightCopy.FrontText() == lightState.FrontText() && lightCopy.BackText() == lightState.BackText()
            && !lightCopy.ReadBack("1,50,FFFFFF"), "the light's settings round-trip through text, with all seven zones");
        var breatheState = new LightState { Effect = LightState.Breathe, Period = 4000, Colors = Enumerable.Repeat(0xFF8000, 7).ToArray() };
        var waveState = new LightState { Effect = LightState.Wave, Period = 7000 };
        var waveCopy = new LightState();
        Check(LitraProtocol.Frame(breatheState, 0)[0] == 0 && LitraProtocol.Frame(breatheState, 2000)[3] == 0xFF8000
            && Math.Abs((LitraProtocol.Frame(breatheState, 1000)[6] >> 16) - 128) <= 1
            && LitraProtocol.Hue(0) == 0xFF0000 && LitraProtocol.Hue(1 / 3.0) == 0x00FF00 && LitraProtocol.Hue(-1 / 3.0) == 0x0000FF
            && LitraProtocol.Frame(waveState, 1000)[1] == LitraProtocol.Frame(waveState, 0)[0]
            && LitraProtocol.Frame(new LightState { Effect = LightState.Cycle, Period = 3000 }, 1000).All(channel => channel == 0x00FF00)
            && waveCopy.ReadBack(waveState.BackText()) && waveCopy.Effect == LightState.Wave && waveCopy.Period == 7000
            && !waveCopy.ReadBack("1,50,FFFFFF/FFFFFF/FFFFFF/FFFFFF/FFFFFF/FFFFFF/FFFFFF,9,5000"),
            "the light's breathe, cycle and wave frames, and its effect round-trips through text");
        var lightScreen = LitraProtocol.Screen("");
        Check(lightScreen.Zones.Count == 7 && LitraProtocol.Screen(lightScreen.ToText()).Zones[6].Area == lightScreen.Zones[6].Area
            && LitraProtocol.Screen(new ScreenSettings().ToText()).Zones.Count == 7,
            "the light's screen effect has seven areas and keeps them");
        var fakeLight = new FakeLight();
        var lumenRange = LitraProtocol.Lumens(fakeLight);
        var kelvinRange = LitraProtocol.Kelvins(fakeLight);
        LitraProtocol.Colors(fakeLight, new[] { 0x000000, 0xFF0000, 0x00FF00, 0x0000FF, 0x123456, 0xFFFFFF, 0x808080 }, false);
        Check(lumenRange.Min == 30 && lumenRange.Max == 400 && kelvinRange.Min == 2700 && kelvinRange.Max == 6500
            && kelvinRange.Step == 100 && kelvinRange.Snap(4049) == 4000
            && fakeLight.Colors[0] == 0x010101 && fakeLight.Colors[1] == 0xFF0101
            && fakeLight.Colors[6] == 0x808080 && fakeLight.Frames == 1,
            "the light reads its ranges, and sets its seven back zones in two messages and one frame");
        var breathe = new MouseLight { Effect = 0x0A, Color = 0x30D050, Period = 4800, Bright = 50 };
        var speakerScreen = ZoneLighting.Screen("");
        Check(speakerScreen.Zones.Count == 4 && ZoneLighting.Screen(speakerScreen.ToText()).Zones[3].Area == speakerScreen.Zones[3].Area
            && ZoneLighting.Screen(new ScreenSettings().ToText()).Zones.Count == 4,
            "the speakers' screen effect has one area per zone and keeps it");
        var speakerLights = ZoneLighting.FromText(ZoneLighting.ToText(new[] { breathe, null, new MouseLight(), breathe }), 4);
        var oneForAll = ZoneLighting.FromText("1,FF0000,5000,100", 4);
        Check(speakerLights != null && speakerLights[0].Same(breathe) && speakerLights[1] == null
            && speakerLights[2].Same(new MouseLight()) && speakerLights[3].Same(breathe)
            && oneForAll.All(light => light != null && light.Color == 0xFF0000)
            && ZoneLighting.FromText("7,FFFFFF,1000,100", 4) == null && ZoneLighting.FromText("", 4) == null,
            "speaker zone lighting round-trips through text, one light sets every zone, unknown effects are dropped");
        Check(new FoundDevice
            {
                Name = "G915 TKL LIGHTSPEED Wireless RGB Mechanical Gaming Keyboard", Features = new[] { 0x8071, 0x8040, 0x8100 }
            }.IsG915
            && !new FoundDevice { Name = "G915 X LIGHTSPEED", Features = new[] { 0x8071, 0x8040, 0x8100 } }.IsG915
            && new FoundDevice { Name = "G915 WIRELESS RGB MECHANICAL GAMING KEYBOARD" }.ShortName == "G915"
            && new FoundDevice { Name = "G915 TKL LIGHTSPEED Wireless RGB Mechanical Gaming Keyboard" }.ShortName == "G915 TKL",
            "the G915 is found, and the G915 X isn't");
        Check(new FoundDevice { Index = 0, Hidpp10 = true }.Slot == "" && new FoundDevice { Index = 0xFF }.Slot == ""
            && new FoundDevice { Index = 2 }.Slot == ", slot 2"
            && FoundDevice.LooksG("G9x Laser Mouse")
            && !new FoundDevice { Name = "G9x Laser Mouse", Kind = "mouse", Hidpp10 = true }.IsMouse,
            "an old HID++ 1.0 device has no slot and isn't taken for a HID++ 2.0 mouse");
        Check(G9xModel.G9x.Dpi(17) == 400 && G9xModel.G9x.Dpi(136) == 3200 && G9xModel.G9x.Dpi(242) == 5700
            && G9xModel.G9x.Dpi(9) == 200 && G9xModel.G9x.Dpi(85) == 2000
            && G9xModel.G9x.DpiToRaw(5700) == 242 && G9xModel.G9x.DpiToRaw(800) == 34, "G9x DPI matches what LGS shows");
        var g9xMouse = new FakeG9x();
        var g9xState = G9xRegisters.Read(g9xMouse, G9xModel.G9x);
        Check(g9xState.Dpi == 800 && g9xState.Stage == 1 && g9xState.Stages.SequenceEqual(new[] { 400, 800, 1600, 3200 })
            && g9xState.Rate == 500 && g9xState.Light.Effect == 0x01 && g9xState.Light.Color == 0xFF0000 && g9xState.Light.Bright == 100,
            "the G9x reads its DPI, stages, rate and colour");
        G9xRegisters.SetDpi(g9xMouse, 5700, G9xModel.G9x);
        G9xRegisters.SetRate(g9xMouse, 1000);
        G9xRegisters.SetLight(g9xMouse, new MouseLight { Effect = 0x01, Color = 0x0000FF, Bright = 50 });
        Check(g9xMouse.Registers[0x63].Take(6).SequenceEqual(FakeHidpp.Hex("F200F2008001")) && g9xMouse.Registers[0x64][0] == 1
            && g9xMouse.Registers[0x57].SequenceEqual(FakeHidpp.Hex("00007F")), "the G9x sets DPI, rate and colour through its registers");
        G9xRegisters.SetLight(g9xMouse, new MouseLight { Effect = 0x00 });
        Check(g9xMouse.Registers[0x57].SequenceEqual(new byte[3]) && G9xRegisters.Read(g9xMouse, G9xModel.G9x).Light.Effect == 0x00,
            "the G9x light turns off");
        Check(G9xRegisters.StagesOf(g9xMouse.Pages[3], G9xModel.G9x).SequenceEqual(new[] { 200, 800, 2000 })
            && new FoundDevice { Hidpp10 = true, Path = @"\\?\hid#vid_046d&pid_c066&mi_01&col04" }.IsG9x
            && !new FoundDevice { Path = @"\\?\hid#vid_046d&pid_c066" }.IsG9x, "G9x profile 2 and detection");
        Check(FoundDevice.ModelText(FakeHidpp.Hex("0400000000000EB38940B4C35500")) == "USB C355, LIGHTSPEED 40B4, Bluetooth B389"
            && FoundDevice.ModelText(FakeHidpp.Hex("0300000000000C4079C0880000")) == "USB C088, LIGHTSPEED 4079"
            && FoundDevice.ModelText(FakeHidpp.Hex("01000000000003000000000ABA")) == "0ABA",
            "product IDs from 0x0003 as the G515, G Pro Wireless and PRO X Wireless answer");
        var g9xSaver = new FakeG9x();
        var g9xBefore = G9xRegisters.Read(g9xSaver, G9xModel.G9x);
        var g9xOtherPage = (byte[])g9xSaver.Pages[3].Clone();
        var g9xPage = G9xRegisters.Build(g9xSaver.Pages[2],
            new MouseSettings { Dpi = 1200, Stage = 2, Light = new MouseLight { Effect = 0x01, Color = 0x00FF00, Bright = 100 } },
            1000, G9xModel.G9x);
        G9xRegisters.Store(g9xSaver, g9xBefore, g9xPage, g9xBefore.Profile);
        var g9xAfter = G9xRegisters.Read(g9xSaver, G9xModel.G9x);
        Check(g9xBefore.Writable && g9xSaver.Pages[2].SequenceEqual(g9xPage) && g9xAfter.ProfileOk
            && g9xAfter.Stages.SequenceEqual(new[] { 400, 800, 1200, 3200 })
            && g9xPage[16] == 0 && g9xPage[17] == 51 && g9xPage[18] == 0 && g9xPage[19] == 51 && g9xPage[0x23] == 2 && g9xPage[0x26] == 1
            && g9xPage.Take(3).SequenceEqual(FakeHidpp.Hex("00FF00"))
            && g9xSaver.Pages[3].SequenceEqual(g9xOtherPage)
            && g9xSaver.Writes.SequenceEqual(new[] { "erase 2:0", "copy 2:0", "copy 2:128" })
            && g9xSaver.Registers[0x0F][0] == 0x00 && g9xSaver.Registers[0x0F][1] == 0x00,
            "the G9x saves a profile page through RAM, erase and copy, and loads it again");
        var g9xDirectory = G9xRegisters.Read(new FakeG9x(), G9xModel.G9x);
        g9xDirectory.Sector = 1;
        bool g9xRefused = false;
        try { G9xRegisters.Store(g9xSaver, g9xDirectory, g9xPage, null); } catch (RefusedException) { g9xRefused = true; }
        Check(g9xRefused && !g9xDirectory.Writable && g9xSaver.Writes.Count == 3, "the G9x never writes the directory page");
        var g9xCut = new FakeG9x();
        var g9xCutState = G9xRegisters.Read(g9xCut, G9xModel.G9x);
        var g9xKept = (byte[])g9xCut.Pages[2].Clone();
        g9xCut.FailSecondHalf = true;
        string g9xCutError = null;
        try
        {
            G9xRegisters.Store(g9xCut, g9xCutState,
                G9xRegisters.Build(g9xKept, new MouseSettings { Dpi = 1200, Stage = 2 }, 1000, G9xModel.G9x), g9xKept);
        }
        catch (System.IO.IOException ex) { g9xCutError = ex.Message; }
        Check(g9xCutError != null && g9xCutError.Contains("as it was is back")
            && g9xCut.Pages[2].SequenceEqual(g9xKept) && g9xCut.Registers[0x0F][0] == 0x00
            && g9xCut.Writes.SequenceEqual(new[] { "erase 2:0", "copy 2:0", "erase 2:0", "copy 2:0", "copy 2:128" }),
            "a G9x save cut off after the erase puts the page back as it was and loads it, instead of leaving the mouse on its factory profile");
        Check(FoundDevice.FirmwareText(new byte[] { 0x00, 0x55, 0x31, 0x00, 0x12, 0x01, 0x00, 0x05 }) == "U1 12.01, build 0005"
            && FoundDevice.FirmwareText(new byte[] { 0x00, 0x4D, 0x50, 0x4D, 0x17, 0x00, 0x00, 0x08 }) == "MPM 17.00, build 0008",
            "a zero byte in the firmware name doesn't end the report");
        Check(new FoundDevice { Name = "G510 Gaming Keyboard" }.ShortName == "G510" && FoundDevice.LooksG("G510 Gaming Keyboard"),
            "the G510 gets a short name and counts as a G device");
        var g510 = new FoundDevice
        {
            Name = "G510s Gaming Keyboard", Kind = "keyboard", Link = "USB",
            PlainHid = true, HasVendorCollection = true, Path = @"\\?\hid#vid_046d&pid_c22d&mi_01&col02"
        };
        Check(g510.IsG510 && DeviceTable.For(g510).Screen == typeof(G510Page)
            && !new FoundDevice { Name = "G510s Gaming Keyboard", PlainHid = true, Path = g510.Path }.IsG510
            && G510Lighting.Frame(new G510Settings { Color = 0xFF2020, Bright = 50 }, 0) == 0x7F1010
            && G510Lighting.Frame(new G510Settings { On = false }, 0) == 0
            && G510Lighting.Frame(new G510Settings { Effect = G510Settings.Breathe, Period = 2000 }, 1000) == 0xFFFFFF
            && G510Lighting.Frame(new G510Settings { Effect = G510Settings.Breathe, Period = 2000 }, 0) == 0
            && G510Lighting.Frame(new G510Settings { Effect = G510Settings.Cycle }, 0) == 0xFF0000
            && G510Settings.FromText(new G510Settings { Color = 0x3080FF, Bright = 40, On = false }.ToText())
                .Same(new G510Settings { Color = 0x3080FF, Bright = 40, On = false })
            && G510Settings.FromText(new G510Settings { Effect = G510Settings.Cycle, Period = 3000 }.ToText())
                .Same(new G510Settings { Effect = G510Settings.Cycle, Period = 3000 })
            && G510Settings.FromText("1,FF0000,100").Effect == G510Settings.Static,
            "the G510 gets its screen only when its vendor collection was found, and its backlight is the colour scaled by brightness");
        var g510Keys = G510Keys.FromText("80020105 800300E9 nonsense");
        var g510Switch = new FakeG510 { GameMode = true };
        Check(g510Keys.Length == 18 && G510Keys.ToText(g510Keys).StartsWith("80020105 800300E9 80000000 ")
            && G510Keys.FromText(G510Keys.ToText(g510Keys)).Zip(g510Keys, (x, y) => x.SequenceEqual(y)).All(same => same)
            && G510Keys.GameMode(g510Switch) && !G510Keys.GameMode(new FakeG510())
            && G510Keys.LitBank(g510Switch, 2) == 0 && LightsM3(g510Switch)
            && KeyLayout.Load("g510.json").KeyIds().Count(id => id >= 0) == 18 + 104,
            "the G510's G key bindings read back from their text, unbound keys as Disabled; its Game mode switch is bit 2 of byte 3 of "
                + "input report 03; M1 to M3 light as 80, 40 and 20 in feature 04; its picture lights the 18 G keys and the 104 others");
        var g500s = FakeG9x.G500s();
        var g500sState = G9xRegisters.Read(g500s, G9xModel.G500s);
        G9xRegisters.SetDpi(g500s, 8200, G9xModel.G500s);
        var g500sPage = G9xRegisters.Build(g500s.Pages[2],
            new MouseSettings { Dpi = 6400, Stage = 3, Light = new MouseLight { Effect = 0x01, Color = 0x00FF00, Bright = 100 } },
            1000, G9xModel.G500s);
        G9xRegisters.Store(g500s, g500sState, g500sPage, g500sState.Profile);
        var g500sAfter = G9xRegisters.Read(g500s, G9xModel.G500s);
        Check(g500sState.Dpi == 800 && g500sState.Light == null
            && g500sState.Stages.SequenceEqual(new[] { 400, 800, 1600, 3200 }) && g500sState.Dpis.Last() == 8200
            && g500s.Registers[0x63][0] == 164 && g500sAfter.Stages.SequenceEqual(new[] { 400, 800, 1600, 6400 })
            && g500s.Pages[2][0x23] == 3 && g500s.Pages[2].Take(3).All(value => value == 0)
            && new FoundDevice { Hidpp10 = true, Path = @"\\?\hid#vid_046d&pid_c24e&mi_01&col04" }.G500Model == G9xModel.G500s
            && new FoundDevice { Hidpp10 = true, Path = @"\\?\hid#vid_046d&pid_c068" }.IsG500,
            "the G500s reads and saves its DPI in units of 50 up to 8200, and has no colour light to read or save");
        var g5Mouse = FakeG9x.Short(0x82);
        var g5State = G5Mouse.G5().Read(g5Mouse);
        G5Mouse.G5().SendDpi(g5Mouse, g5State, new MouseSettings { Dpi = 2000 }, null);
        var g9Mouse = FakeG9x.Short(0x84);
        var g9State = G5Mouse.G9().Read(g9Mouse);
        G5Mouse.G9().SendDpi(g9Mouse, g9State, new MouseSettings { Dpi = 3200 }, null);
        bool g5Refused = false;
        try { G5Mouse.G5().SendDpi(g5Mouse, g5State, new MouseSettings { Dpi = 1000 }, null); }
        catch (RefusedException) { g5Refused = true; }
        Check(g5State.Dpi == 1600 && g5State.Rate == 500 && !g5State.Writable && g5State.Light == null
            && g5Mouse.Registers[0x63][0] == 0x83 && g5Refused
            && g9State.Dpi == 800 && g9State.Dpis.Length == 16 && g9Mouse.Registers[0x63][0] == 0x90 && G5Mouse.G9().DpiToRaw(200) == 0x81
            && new FoundDevice { Hidpp10 = true, Path = @"\\?\hid#vid_046d&pid_c049&mi_00" }.IsG5
            && new FoundDevice { Hidpp10 = true, Path = @"\\?\hid#vid_046d&pid_c048&mi_00" }.IsG9,
            "the G5 and G9 read and set their DPI as 0x80 + an index into their list (the G9's 200 DPI is 0x81), live only");
        var g700s = FakeG9x.G500s();
        var g700sState = G9xRegisters.Read(g700s,
            new FoundDevice { Hidpp10 = true, Path = @"\\?\hid#vid_046d&pid_c07c&mi_01" }.G500Model);
        Check(g700sState.Dpi == 800 && g700sState.Dpis.Last() == 8200 && g700sState.Profile == null
            && !g700sState.Writable && g700sState.Stages.Length == 0
            && new FoundDevice { Hidpp10 = true, Path = @"\\?\hid#vid_046d&pid_c06b" }.G500Model == G9xModel.G700
            && new FoundDevice { Hidpp10 = true, Index = 1, WirelessId = 0x102A, Path = @"\\?\hid#vid_046d&pid_c531&mi_01" }.G500Model
                == G9xModel.G700s
            && new FoundDevice { Hidpp10 = true, Index = 1, WirelessId = 0x1023, Path = @"\\?\hid#vid_046d&pid_c531&mi_01" }.IsG500
            && !new FoundDevice { Hidpp10 = true, Index = 1, Path = @"\\?\hid#vid_046d&pid_c531&mi_01" }.IsG500,
            "the G700 and G700s on a cable read their DPI with the G500's and G500s's units, and never read or write a profile page");
        var g9xReport = Report.Registers(new FakeG9x(),
            new FoundDevice { Name = "G9x Laser Mouse", Kind = "mouse", Link = "USB", Hidpp10 = true }, "MousePage");
        Check(g9xReport.Contains("long 63=22 00 22 00 80 01") && g9xReport.Contains("page 02, current") && g9xReport.Contains("page 04"),
            "the G9x report has its registers and profile pages");
        Check(new[] { "g515-tkl.json", "g915-tkl.json", "g915.json", "g512-x-75.json" }.All(x => KeyLayout.Load(x).Rows.Length > 5),
            "the key layouts load");
        var g915 = new FakeG915();
        var clusters = G915Lighting.Clusters(g915);
        Check(clusters.Count == 2 && G915Lighting.Keys(clusters) == 1, "the G915's keys are the cluster with the most effects");
        bool g515Vertical, g915Vertical;
        G915Lighting.Clusters(new FakeHidpp(), out g515Vertical);
        G915Lighting.Clusters(new FakeG915(), out g915Vertical);
        Check(g515Vertical && !g915Vertical, "Wave goes up and down when its capabilities say so (the G515's DC E1), not otherwise");
        G915Lighting.Claim(g915);
        G915Lighting.Preview(g915, clusters, new Lighting { Effect = 0x15, Period = 3000 });
        Check(g915.Claimed == 3 && g915.Live[1][1] == 3
            && g915.Live[1][7] == 0x0B && g915.Live[1][8] == 0xB8 && g915.Live[1][9] == 100 && g915.Live[1][12] == 1,
            "cycle on the G915 uses effect 0x03, with its period at byte 5");
        Check(g915.Live[0][1] == 2 && g915.Live[0][7] == 0x0B, "the logo cycles too");
        G915Lighting.Preview(g915, clusters, new Lighting { Effect = 0x16, Period = 4000, Direction = 1 });
        Check(g915.Live[1][1] == 4 && g915.Live[1][8] == 0xA0
            && g915.Live[1][9] == 1 && g915.Live[1][10] == 100 && g915.Live[1][11] == 0x0F,
            "wave on the G915 uses effect 0x04");
        Check(g915.Live[0][1] == 2, "the logo cycles when the keys wave");
        G915Lighting.Preview(g915, clusters, new Lighting { Effect = 0x17, Color = 0x3080FF, Period = 20 });
        Check(g915.Live[1][1] == 5 && g915.Live[1][2] == 0x30 && g915.Live[1][7] == 20 && g915.Live[0][1] == 1 && g915.Live[0][4] == 0xFF,
            "ripple on the keys, the ripple's colour on the logo");
        G915Lighting.Preview(g915, clusters, new Lighting { Effect = 0x0A, Color = 0xFF0000, Period = 5000 });
        Check(g915.Live[1][1] == 2 && g915.Live[0][1] == 3 && g915.Live[1][5] == 0x13 && g915.Live[1][6] == 0x88,
            "breathe on keys and logo");
        bool lacks = false;
        try { G915Lighting.Preview(g915, clusters, new Lighting { Effect = 0x02 }); }
        catch (RefusedException) { lacks = true; }
        Check(lacks && g915.Live[1][1] == 2, "an effect the G915 lacks is refused without sending anything");
        G915Lighting.Release(g915);
        Check(g915.Claimed == 0, "release gives the lighting back to the G915");
        int g9Sector;
        var g9Profile = G915Lighting.ReadProfile(g915, out g9Sector);
        var g9Saved = G915Lighting.Decode(g9Profile, 1);
        Check(g9Sector == 1 && g9Saved.Effect == 0x15 && g9Saved.Period == 10000
            && g9Saved.Idle == 60 && g9Saved.Sleep == 300 && g9Saved.Dim == 50,
            "the G915 TKL's profile reads as Cycle at 10 s, dimmed to half after 60 s, off after 300 s");
        var g9Built = G915Lighting.Build(g9Profile, clusters,
            new Lighting { Effect = 0x16, Period = 3000, Direction = 1, Dim = 50, Idle = 15, Sleep = 600 }, null, true);
        // Profile 3 of the same keyboard, Cycle on the logo and Wave on the keys, normal then dimmed
        // (its Wave also has FF in its first and sixth parameter bytes).
        var g9Want = FakeHidpp.Hex("0300000000000BB864000004000000000000B801640B0300000000000BB832000004000000000000B801320B");
        Check(g9Built.Skip(0xD0).Take(44).SequenceEqual(g9Want) && MouseProtocol.Valid(g9Built)
            && g9Built[0x1C] == 15 && g9Built[0x1E] == 0x58 && g9Built[0x1F] == 2
            && g9Built.Take(0xD0).Where((x, i) => i < 0x1C || i > 0x1F)
                .SequenceEqual(g9Profile.Take(0xD0).Where((x, i) => i < 0x1C || i > 0x1F)),
            "saving Wave on the G915 writes the records the keyboard's own Wave profile has, the idle times, and keeps every other byte");
        // Profile 2 of an owner's G815 (256-byte sectors, the CRC as reported): Breathe, no idle times,
        // the power-save records copies of the others.
        var g815Profile = FakeHidpp.Hex("01000000000000000000000000" + new string('F', 30) + "00000000"
            + "8002001E8002001F800200208002002180020022900DFF01900DFF02900DFF03"
            + new string('F', 192) + new string('0', 96) + string.Concat(Enumerable.Repeat("0A00DCFF0BB80000000000", 4)) + "FFFF74A5");
        var g815Saved = G915Lighting.Decode(g815Profile, 1);
        var g815Built = G915Lighting.Build(g815Profile, clusters,
            new Lighting { Effect = 0x01, Color = 0xFF0000, Dim = 50, Idle = 60, Sleep = 300 }, null, false);
        Check(g815Profile.Length == 256 && MouseProtocol.Valid(g815Profile)
            && g815Saved.Effect == 0x0A && g815Saved.Color == 0x00DCFF && g815Saved.Period == 3000 && g815Saved.Idle == 0
            && MouseProtocol.Valid(g815Built) && g815Built.Skip(0xD0).Take(22).SequenceEqual(g815Built.Skip(0xE6).Take(22))
            && g815Built[0xDB] == 0x01 && g815Built[0xDC] == 0xFF
            && g815Built.Take(0xD0).SequenceEqual(g815Profile.Take(0xD0)) && g815Built.Skip(0xFC).Take(2).All(value => value == 0xFF),
            "the G815's profile reads, and saving keeps its idle times at 0 and writes the power-save records as copies, as G HUB does");
        G915Lighting.Claim(g915);
        G915Lighting.Store(g915, g9Sector, g9Built);
        Check(g915.Sectors[1].SequenceEqual(g9Built) && g915.Reloads == 1 && g915.Claimed == 0,
            "the G915's profile is written, loaded again, and the lighting given back");
        Check(g915.Modes.Count == 1, "the G915 stays in onboard mode while saving");
        var g815 = new FakeG915();
        G915Lighting.Store(g815, g9Sector, g9Built, KeyboardKinds.G815.ReloadThroughHostMode);
        Check(g815.Reloads == 1 && g815.Modes.SequenceEqual(new byte[] { 1, 2, 1 }),
            "the G815's profile is loaded again by leaving onboard mode and coming back, which ends in onboard mode");
        Check(Settings.Get("theme") == "system" && Theme.Mode == "system", "theme starts on system");
        Theme.Mode = "light";
        Check(Theme.Light && Settings.Get("theme") == "light", "light theme is saved and applied");
        Theme.Mode = "dark";
        Check(!Theme.Light && Theme.Text.R > 200, "dark theme is applied");
        Settings.Set("theme", "pink");
        Check(Theme.Mode == "system", "an unknown theme falls back to system");
        var red = G515Profile.Encode(new Lighting { Effect = 0x01, Color = 0xFF0000 }, 100);
        Check(KeyboardView.EffectColorAt(0x01, red, 0.5, 0.5, 0, 0) == System.Drawing.Color.FromArgb(255, 0, 0)
            && KeyboardView.EffectColorAt(0x00, new byte[10], 0.5, 0.5, 0, 0).ToArgb() == System.Drawing.Color.Black.ToArgb(),
            "key colors for static and off");
        System.IO.File.Delete(Settings.Location);
        using (var logo = Logo.Draw(64))
            Check(logo.GetPixel(0, 0).A == 0 && logo.GetPixel(32, 4).A == 255 && logo.GetPixel(32, 32).A < 255,
                "logo is a white disc with a transparent GL");
        bool trayFits = true;
        for (int side = 16; side <= 64; side++)
            using (var tray = Logo.Slanted(side, System.Drawing.Color.White))
                trayFits &= tray.GetPixel(0, 0).A == 0 && tray.GetPixel(side - 1, side - 1).A == 0;
        Check(trayFits, "the slanted GL fits the tray icon at every size");
        if (args.Length > 0 && !args[0].StartsWith("--")) Logo.WriteIco(args[0]);
        Log.File = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "glite-selftest-log.txt");
        Log.Clear();
        var labeledMouse = new FakeMouse { DeviceLabel = "M" };
        labeledMouse.Call(labeledMouse.FeatureIndex(0x2201), 2, 0);
        MouseProtocol.SetDpi(labeledMouse, 1600);
        var logged = Log.Read();
        Check(logged.Count == 1 && logged[0].Who == "M" && logged[0].Text == "2201 fn3  00 06 40" && logged[0].Result == "ok",
            "the log keeps commands and leaves out reads");
        Log.Sent("M", 0x8100, 7, new byte[] { 1 }, "ok");
        Log.Sent("M", 0x8100, 7, new byte[] { 2 }, "ok");
        var folded = Log.MergeRepeatedSends(Log.Read());
        Check(folded.Count == 2 && folded[1].Times == 2 && Log.Format(folded[1]).Contains("8100 fn7  ok ×2       01"),
            "the log folds calls in a row to the same function");
        try { labeledMouse.Call(labeledMouse.FeatureIndex(0x8100), 5, 0x00, 0x42, 0x00, 0x00); } catch (HidppError) { }
        var refusedRead = Log.Read().Last();
        Check(refusedRead.Text == "8100 fn5  00 42 00 00" && refusedRead.Result == "error 02", "a read the device refuses is logged");
        Log.Clear();
        var mouseSpy = new Recorder(new FakeMouse());
        var mouseReport = Report.Build(mouseSpy, new FoundDevice
            {
                Name = "G Pro Wireless Gaming Mouse", Kind = "mouse", Link = "LIGHTSPEED", Index = 1, WirelessId = 0x4079,
                Path = @"\\?\hid#vid_046d&pid_c547&mi_02&col02#7&abc&0&0001#{x}", Features = new[] { 0x2201, 0x8060, 0x8070, 0x8100 }
            }, "MousePage");
        Check(mouseReport.Contains("ID 4079, USB 046D:C547") && mouseReport.Contains("0000=0001")
            && mouseReport.Contains("0001=" + BitConverter.ToString(mouseSpy.Inner.Sectors[1]).Replace("-", ""))
            && mouseReport.Contains("0101=") && mouseReport.Contains("dpi list=")
            && mouseReport.Contains("zone 1 effect 3=") && !mouseReport.Contains("abc"),
            "the mouse report has its features, profiles and no device path");
        Check(mouseSpy.Calls.Count > 20
            && mouseSpy.Calls.All(call => Report.IsRead(call.Item1, call.Item2, call.Item3)),
            "the mouse report only reads");
        var keyboardSpy = new Recorder(new FakeHidpp());
        var keyboardReport = Report.Build(keyboardSpy, new FoundDevice
            {
                Name = "G515 LS TKL", Kind = "keyboard", Link = "LIGHTSPEED", Index = 1, WirelessId = 0x40B4,
                Features = new[] { 0x4540, 0x8040, 0x8071, 0x8081, 0x8101 }
            }, "KeyboardPage");
        Check(keyboardReport.Contains("lit ids=01 02 03") && keyboardReport.Contains(" 4F 61 62") && keyboardReport.Contains("layout=0D")
            && keyboardReport.Contains("0008=") && keyboardReport.Contains("0101=") && keyboardReport.Contains("cluster 0 effect 1=")
            && keyboardReport.Contains("control=") && keyboardReport.Contains("power mode=")
            && keyboardSpy.Calls.All(call => Report.IsRead(call.Item1, call.Item2, call.Item3)),
            "the keyboard report reads its files and effects only");
        var g915WithMacros = new FakeG915();
        g915WithMacros.Sectors[4] = Enumerable.Repeat((byte)0xAB, 255).ToArray();
        g915WithMacros.Sectors[6] = Enumerable.Repeat((byte)0xFF, 255).ToArray();
        g915WithMacros.Sectors[7] = new byte[255];
        g915WithMacros.Sectors.Remove(2);
        var g915Report = Report.Build(g915WithMacros, new FoundDevice
            {
                Name = "G915 TKL LIGHTSPEED Wireless RGB Mechanical Gaming Keyboard", Kind = "keyboard", Link = "LIGHTSPEED", Index = 1,
                Features = new[] { 0x4540, 0x8040, 0x8071, 0x8100 }
            }, "KeyboardPage");
        Check(g915Report.Contains("0000=000101FF000201FF000301FF") && g915Report.Contains("0001=0100")
            && g915Report.Contains("0002=error 02") && g915Report.Contains("0101=0100"),
            "the G915 report reads the profiles its directory lists, and goes on past one it can't read");
        var g915Lines = g915Report.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        Check(g915Lines.Contains("0004=" + string.Concat(Enumerable.Repeat("AB", 255)))
            && g915Lines.Contains("unused=0006 0007")
            && g915Lines.Any(line => line.StartsWith("unreadable=0005 (error 02) 0008 (error 02)"))
            && g915Lines.Count(line => line.StartsWith("0001=")) == 1,
            "the G915 report reads every other user sector once, for the macros, and lists the unused and unreadable ones");
        var headsetReport = Report.Build(new FakeHeadset(),
            new FoundDevice
            {
                Name = "PRO X Wireless Gaming Headset", Kind = "headset", Link = "LIGHTSPEED", Index = 0xFF,
                Features = new[] { 0x8300, 0x1F20 }
            },
            "HeadsetPage");
        Check(headsetReport.Contains("sidetone=1E") && headsetReport.Contains("auto-off=1E") && headsetReport.Contains("%, 3926 mV"),
            "the headset report reads sidetone, auto-off and the battery");
        Check(!Report.Reads[0x8100].Intersect(new[] { 1, 3, 6, 7, 8, 0xC }).Any()
            && !Report.Reads[0x8101].Intersect(new[] { 2, 3, 6, 7 }).Any()
            && !Report.Reads[0x8071].Contains(1) && !Report.Reads[0x8070].Contains(3)
            && Report.IsRead(0x8071, 8, new byte[] { 0, 0, 0 }) && !Report.IsRead(0x8071, 8, new byte[] { 1, 1, 0 })
            && !Report.IsRead(0x8071, 5, new byte[] { 1, 0, 0 }), "the report never calls a write function");

        Check(GameModeKeys.Usage(0x01) == 0x04 && GameModeKeys.Usage(0x17) == 0x1A && GameModeKeys.Usage(0x62) == 0x65
            && GameModeKeys.Usage(0x68) == 0xE0 && GameModeKeys.Usage(0x6F) == 0xE7 && GameModeKeys.Usage(0xB4) == -1
            && GameModeKeys.Usage(0x99) == -1 && GameModeKeys.LightingId(0x1A) == 0x17 && GameModeKeys.LightingId(0xE3) == 0x6B,
            "Game mode keys: lighting ids are USB usages minus 3, the modifiers 0x68-0x6F, and G, media and brightness keys have none");
        Check(!GameModeKeys.CanPick(0xE3, false) && !GameModeKeys.CanPick(0xE7, false) && !GameModeKeys.CanPick(0x65, false)
            && GameModeKeys.CanPick(0x2B, false)
            && GameModeKeys.Clean(new[] { 0x1A, 0xE3, 0x0E, 0x1A, 0xC0 }, false).SequenceEqual(new[] { 0x0E, 0x1A }),
            "Game mode keys: Windows and Menu are always off and can't be picked, and the list is sorted without repeats");
        var gamePackets = GameModeKeys.Packets(Enumerable.Range(4, 18).ToList());
        Check(gamePackets.Count == 2 && gamePackets[0].SequenceEqual(Enumerable.Range(4, 16).Select(usage => (byte)usage))
            && gamePackets[1][0] == 20 && gamePackets[1][1] == 21 && gamePackets[1].Skip(2).All(value => value == 0)
            && GameModeKeys.Packets(new int[0]).Count == 0,
            "Game mode keys: 16 usages per fn 1 call, the last one ended by zeros");
        string gameText, oldPreset = GameModeKeys.Split("1,FF0000,0,0,0,0,0,100", out gameText);
        string noKeys, emptyPreset = GameModeKeys.Split("1,FF0000,0,0,0,0,0,100!", out noKeys);
        string zoneKeys, zonePreset = GameModeKeys.Split("254,FF0000,0,0,0,0,0,100|zones:Hey!,FF0000!0E1A", out zoneKeys);
        Check(gameText == null && oldPreset == "1,FF0000,0,0,0,0,0,100" && noKeys == "" && emptyPreset == oldPreset
            && zoneKeys == "0E1A" && zonePreset.EndsWith("Hey!,FF0000")
            && GameModeKeys.FromText("1A0EE3zz", false).SequenceEqual(new[] { 0x0E, 0x1A }) && GameModeKeys.ToText(new[] { 0x0E, 0x1A }) == "0E1A",
            "Game mode keys in a preset: after the last !, none in an older preset, read back without what can't be picked");
        var g515Controls = Enumerable.Range(0, FakeHidpp.ControlList.Length / 2)
            .Select(i => FakeHidpp.ControlList[i * 2] << 8 | FakeHidpp.ControlList[i * 2 + 1]).ToArray();
        var aAndEnter = GameModeKeys.Bitmap(g515Controls, new[] { 0x04, 0x28 });
        Check(aAndEnter[7] == 0x41 && aAndEnter.Where((bits, index) => index != 7).All(bits => bits == 0),
            "Game mode bitmap (0x4523): A and Enter are the G515's controls 0x38 and 0x3E, bits 0 and 6 of byte 7 (tried)");
        var g515Usages = KeyLayout.Load("g515-tkl.json").KeyIds().Select(GameModeKeys.Usage).Where(usage => GameModeKeys.CanPick(usage, true))
            .Concat(new[] { 0x32, 0x64 }).Distinct().ToList();
        Check(g515Usages.Count == 88 && g515Usages.All(usage =>
            GameModeKeys.Bitmap(g515Controls, new[] { usage }).Sum(bits => Convert.ToString(bits, 2).Count(bit => bit == '1')) == 1),
            "Game mode bitmap: each key that can be picked on the G515 (ANSI and ISO) is one control");
        Check(GameModeKeys.CanPick(0xE3, true) && GameModeKeys.CanPick(0x65, true) && !GameModeKeys.CanPick(0xE7, true)
            && GameModeKeys.FromText("E365", true).SequenceEqual(new[] { 0x65, 0xE3 }),
            "Game mode bitmap: Windows and Menu can be picked (the G515 leaves them on), the Fn key can't (no control)");

        foreach (var file in new[] { "g515-tkl.json", "g915-tkl.json", "g915.json", "g915-x.json", "g915-x-tkl.json", "g815.json", "g512-x-75.json", "g-full.json", "g-60.json" })
        {
            var board = KeyLayout.Load(file);
            int keys = board.Rows.Sum(row => row.Split('|').Count(cell => !cell.StartsWith("_")));
            var keyIds = board.KeyIds();
            Check(keyIds.Count == keys && board.Ids.Length == board.Rows.Length, file + " has one key id per key");
            Check(keyIds.Where(i => i >= 0).All(i => (i >= 0x01 && i <= 0x6F) || (i >= 0x99 && i <= 0xFE)),
                file + " key ids are lighting ids");
        }
        var g815Layout = KeyLayout.Load("g815.json");
        var g815Names = g815Layout.Rows.SelectMany(row => row.Split('|')).Where(cell => !cell.StartsWith("_"))
            .Select(cell => cell.Split(':')[0]).ToList();
        Check(LayoutFiles.All(file => (KeyLayout.Load(file).Shaped ?? new string[0]).All(name =>
                KeyLayout.Load(file).Rows.Any(row => row.Split('|').Any(cell => cell.Split(':')[0] == name))))
            && g815Layout.Shaped.Length == 12 && g815Layout.KeyIds()[g815Names.IndexOf("Wheel")] == -1,
            "every shaped key is in its layout; the G815's volume roller has no lighting id");
        Check(LayoutFiles.All(file => KeyLayout.Load(file).KeyNames().Count == KeyLayout.Load(file).KeyIds().Count)
            && KeyboardKinds.G815.GameMode == GameModeSupport.Tested && KeyboardKinds.G915.GameMode == GameModeSupport.Experimental
            && KeyboardKinds.G915Tkl.GameMode == GameModeSupport.Experimental
            && KeyboardKinds.G515.GameMode == GameModeSupport.Tested && KeyboardKinds.G512X.GameMode == GameModeSupport.Experimental
            && KeyboardKinds.For(new FoundDevice { Name = "G915 X LIGHTSPEED TKL", Kind = "keyboard" }).GameMode
                == GameModeSupport.Experimental
            && KeyboardKinds.For(new FoundDevice { Name = "PRO X 60", Kind = "keyboard" }).GameMode == GameModeSupport.None,
            "Game mode: set on the G815 and the G515, experimental on the G915s, the G915 X models and the G512 X 75, none elsewhere");
        var borrowedTkl = KeyboardKinds.For(new FoundDevice { Name = "G515 RAPID TKL", Kind = "keyboard" }).Keys;
        var borrowed75 = KeyboardKinds.For(new FoundDevice { Name = "G316 X 75", Kind = "keyboard" }).Keys;
        Check(borrowedTkl.Shaped == null && borrowedTkl.Rows[0].StartsWith("Esc|") && borrowedTkl.Rows.Length == 7
            && borrowed75.Shaped == null && borrowed75.Rows[0].EndsWith("|F12") && !borrowed75.Ids[0].Contains("--")
            && borrowed75.KeyNames().Count == borrowed75.KeyIds().Count && borrowedTkl.KeyNames().Count == borrowedTkl.KeyIds().Count,
            "a keyboard that borrows the G515's or the G512 X 75's picture shows neither the top strip nor the knobs");
        Check(Devices.LinkUp(FakeHidpp.Hex("1001411001B440")) && !Devices.LinkUp(FakeHidpp.Hex("1001411041B440"))
            && Devices.LinkUp(FakeHidpp.Hex("1001414001B440")),
            "0x41: the link is down when bit 6 of the flags byte (after the protocol type) is set, whatever the protocol byte holds");
        Check(!KeyLayout.IsIso(-1) && !KeyLayout.IsIso(0) && !KeyLayout.IsIso(1) && KeyLayout.IsIso(2) && KeyLayout.IsIso(0x0D)
            && KeyLayout.IsIso(0x3E), "0x4540: 1 is ANSI, 2 and up have ISO's shape (Solaar), unknown stays ANSI");
        var turnedOff = LogitechSoftware.ApprovedValue(false, new DateTime(2026, 11, 23, 0, 0, 0, DateTimeKind.Utc));
        Check(LogitechSoftware.ApprovedValue(true, DateTime.UtcNow).SequenceEqual(new byte[] { 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 })
            && turnedOff.Length == 12 && turnedOff[0] == 3
            && BitConverter.ToInt64(turnedOff, 4) == new DateTime(2026, 11, 23, 0, 0, 0, DateTimeKind.Utc).ToFileTimeUtc()
            && LogitechSoftware.Mentions(@"C:\Program Files\Logitech Gaming Software\LCore.exe /minimized", "LCore.exe")
            && !LogitechSoftware.Mentions(null, "LCore.exe")
            && LogitechSoftware.ByIds(new[] { "lgs", "Spooler", "ghub" }).Select(item => item.Id).SequenceEqual(new[] { "ghub", "lgs" })
            && LogitechSoftware.RunElevated(new[] { "--services", "delete", "ghub" }) != 0,
            "Services: Task Manager's startup values (02 on, 03 off with a FILETIME at byte 4), Run entries found by program, "
                + "and the elevated G-Lite acting only on its own list and commands");
        // The built ISO pictures were checked equal to the hand-made g515-tkl-iso.json and g213-iso.json they replaced.
        foreach (var file in LayoutFiles)
        {
            var isoBoard = KeyLayout.Load(file).Iso();
            int isoKeys = isoBoard.Rows.Sum(row => row.Split('|').Count(cell => !cell.StartsWith("_")));
            Check(isoBoard.KeyIds().Count == isoKeys && isoBoard.Ids.All(row => row.Split(' ').Length <= 40),
                file + " builds an ISO picture with one key id per key");
        }
        var g515Iso = KeyLayout.Load("g515-tkl.json").Iso();
        Check(g515Iso.Rows[5] == "Tab:1.5|Q|W|E|R|T|Y|U|I|O|P|[|]|_.25|Enter:1.25:2:.25|_.25|Del|End|PgDn"
            && g515Iso.Rows[6] == "Caps:1.75|A|S|D|F|G|H|J|K|L|;|'|#"
            && g515Iso.Rows[7] == "Shift:1.25|<>|Z|X|C|V|B|N|M|,|.|/|Shift:2.75|_1.25|↑" && g515Iso.Rows[8].Contains("|AltGr:1.25|")
            && g515Iso.Ids[5] == "28 11 17 05 12 14 19 15 09 0F 10 2C 2D 25 49 4A 4B"
            && g515Iso.Ids[6] == "36 01 13 04 06 07 08 0A 0B 0C 30 31 2F"
            && g515Iso.Ids[7] == "69 61 1A 18 03 16 02 0E 0D 33 34 35 6D 4F",
            "the G515's ISO picture: an L-shaped Enter from the Tab row with its id, # (2F), a short left Shift and <> (61), AltGr");
        var g213Iso = KeyLayout.Load("g213.json").Iso();
        Check(g213Iso.Rows[6] == "Caps:1.75|A|S|D|F|G|H|J|K|L|;|'|#|_4.75|4|5|6"
            && g213Iso.Ids[6] == "1 1 1 1 1 2 2 2 2 3 3 3 3 5 5 5" && g213Iso.Ids[7] == "1 1 1 1 1 1 2 2 2 2 3 3 3 4 5 5 5 5"
            && g213Iso.Zones && !KeyLayout.Load("g515-tkl.json").Zones,
            "on the G213's zones, # and <> take the zone of the key they replace or sit by");
        var isoIds = g515Iso.KeyIds();
        var ansiIds = KeyLayout.Load("g515-tkl.json").KeyIds();
        Check(isoIds.Contains(0x61) && isoIds.Contains(0x2F) && !isoIds.Contains(0x2E) && ansiIds.Contains(0x2E) && !ansiIds.Contains(0x61),
            "ISO has <> and the key next to Enter, ANSI has \\");
        Check(ansiIds.Where(id => id >= 0).Distinct().Count() == 87 && isoIds.Where(id => id >= 0).Distinct().Count() == 88,
            "the G515 has 87 lit keys in ANSI and 88 in ISO (its top strip is unlit)");
        Check(ansiIds.Contains(0x6F) && ansiIds.Contains(0x62) && ansiIds.Contains(0x6E), "Fn, Menu and AltGr have ids");

        var screen = new ScreenSettings
        {
            Monitor = @"\\.\DISPLAY2", Boost = 60, Interval = 300, Smooth = false, PauseOnBattery = false, PauseWhenLocked = false
        };
        screen.Keys[0x26] = 3;
        screen.Add();
        screen.Zones[4].Area = new System.Drawing.RectangleF(0.1f, 0.2f, 0.3f, 0.4f);
        var screenRoundTrip = ScreenSettings.FromText(screen.ToText());
        Check(screenRoundTrip.Monitor == screen.Monitor && screenRoundTrip.Boost == 60 && screenRoundTrip.Interval == 300
            && !screenRoundTrip.Smooth && !screenRoundTrip.PauseOnBattery && !screenRoundTrip.PauseWhenLocked
            && screenRoundTrip.Zones.Count == 5 && screenRoundTrip.Zones[4].Name == "Zone 5"
            && Math.Abs(screenRoundTrip.Zones[4].Area.Height - 0.4f) < 0.001f
            && screenRoundTrip.Keys[0x26] == 3, "screen setup round-trips through text");
        Check(ScreenSettings.FromText("garbage;zones:x").Zones.Count == 4 && ScreenSettings.FromText("ms:5000").Interval == 1000,
            "bad screen setup text falls back to defaults");
        Check(screen.Add() == -1, "at most five zones");
        var stripes = new ScreenSettings();
        stripes.Zones[1].Area = new System.Drawing.RectangleF(0.3f, 0.1f, 0.4f, 0.2f);
        stripes.Add();
        Check(Enumerable.Range(0, 5).All(i => Math.Abs(stripes.Zones[i].Area.X - i * 0.2f) < 0.001f
                && Math.Abs(stripes.Zones[i].Area.Width - 0.2f) < 0.001f)
            && Math.Abs(stripes.Zones[1].Area.Y - 0.1f) < 0.001f && Math.Abs(stripes.Zones[4].Area.Height - 0.35f) < 0.001f,
            "an added zone makes every zone an equal strip, left to right with the new one at the right, each keeping its height");
        screen.Remove(3);
        Check(screen.Zones.Count == 4 && screen.Keys[0x26] == 2, "removing a zone moves its keys to the zone before");
        Check(screen.ZoneOf(0x01, 0.1f) == 0 && screen.ZoneOf(0x01, 0.99f) == 3 && screen.ZoneOf(0x26, 0.1f) == 2,
            "keys go to the zone under them unless picked");
        Check(ScreenCapture.Boost(0x808080, 0) == 0xFFFFFF && ScreenCapture.Boost(0x050505, 50) == 0
            && ScreenCapture.Boost(0x804020, 0) == 0xFF7F3F,
            "boost keeps the hue and brightens to full");
        int boosted = ScreenCapture.Boost(0x806060, 100);
        Check((boosted & 0xFF) < 0x60 * 255 / 0x80, "boost adds saturation");
        Check(ScreenCapture.Blend(0x000000, 0xFF0000, 0.5) == 0x800000, "blend mixes halfway");

        var perKey = new FakeHidpp();
        var groups = new Dictionary<int, List<int>>
        {
            { 0xFF0000, Enumerable.Range(0x01, 20).ToList() }, { 0x0000FF, new List<int> { 0x61, 0x6F } }
        };
        Check(KeyLights.Present(perKey).Count == 94, "the fake reports 94 lighting ids");
        KeyLights.Send(perKey, groups);
        Check(perKey.Keys.Count == 22 && perKey.Keys[0x14] == 0xFF0000 && perKey.Keys[0x6F] == 0x0000FF && perKey.Frames == 1,
            "per-key colours go out in groups of 13 and end with a frame");
        var busyKeyboard = new FakeHidpp { BusyFrames = 3 };
        bool individualColors = true;
        KeyLights.SendColors(busyKeyboard, new Dictionary<int, int> { { 0x04, 0xFF0000 }, { 0x05, 0x00FF00 } }, ref individualColors);
        bool shownAfterWaiting = busyKeyboard.Frames == 1 && busyKeyboard.BusyFrames == 0 && individualColors;
        busyKeyboard.BusyFrames = 25;
        bool gaveUp = false;
        try { KeyLights.Send(busyKeyboard, new Dictionary<int, List<int>> { { 0x0000FF, new List<int> { 0x06 } } }); }
        catch (HidppError ex) { gaveUp = ex.Code == 8; }
        Check(shownAfterWaiting && gaveUp && busyKeyboard.Frames == 1 && busyKeyboard.BusyFrames == 5,
            "a busy fn 7 is sent again until the frame shows, and after 20 tries the error stands");
        Check(perKey.Call(perKey.FeatureIndex(0x4540), 0)[0] == 0x0D, "the fake keyboard reports a Swiss layout");

        // models/iso keeps each layout's ISO picture as a file to read; it isn't embedded, the app builds it with KeyLayout.Iso.
        var isoFolder = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\models\iso");
        foreach (var file in LayoutFiles)
        {
            var builtIso = KeyLayout.Load(file).Iso();
            var path = System.IO.Path.Combine(isoFolder, file.Replace(".json", "-iso.json"));
            if (args.Contains("--record"))
            {
                System.IO.Directory.CreateDirectory(isoFolder);
                System.IO.File.WriteAllText(path, builtIso.ToJson(), new System.Text.UTF8Encoding(false));
            }
            var kept = System.IO.File.Exists(path) ? KeyLayout.Parse(System.IO.File.ReadAllText(path)) : null;
            Check(kept != null && kept.Rows.SequenceEqual(builtIso.Rows) && kept.Ids.SequenceEqual(builtIso.Ids)
                && kept.Zones == builtIso.Zones && kept.Name == builtIso.Name
                && (kept.Shaped ?? new string[0]).SequenceEqual(builtIso.Shaped ?? new string[0]),
                "models/iso/" + System.IO.Path.GetFileName(path) + " is the ISO picture built from " + file
                    + " (run build\\selftest.exe --record after changing a layout)");
        }

        using (var stepped = new Slider { Stops = KeyboardKinds.G815.BrightnessLevels, Value = 100 })
        {
            stepped.SetValue(40);
            int dragged = stepped.Value;
            stepped.SetValue(90);
            Check(dragged == 50 && stepped.Value == 100, "a slider with stops takes the nearest one (the G815's brightness levels)");
        }

        var traceFailures = Traces.Run(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\tests"),
            args.Contains("--record"));
        foreach (var failure in traceFailures) Console.WriteLine("FAIL " + failure);
        if (traceFailures.Count > 0) return 1;
        foreach (var trace in System.IO.Directory.GetFiles(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\tests"), "*.txt"))
            Check(!System.IO.File.ReadAllText(trace).Contains("\" (cut)"),
                System.IO.Path.GetFileName(trace) + " shows no status cut short (shorten the text, or make room in the footer)");
        Console.WriteLine("selftest ok");
        return 0;
    }
}

class Recorder : Hidpp
{
    public readonly FakeMouse Inner;
    readonly Hidpp inner;
    readonly Dictionary<byte, int> ids = new Dictionary<byte, int>();
    public readonly List<Tuple<int, int, byte[]>> Calls = new List<Tuple<int, int, byte[]>>();

    public Recorder(Hidpp inner) : base(false) { this.inner = inner; Inner = inner as FakeMouse; }

    public override void Dispose() { }

    protected override byte[] Send(byte feature, int fn, byte[] parameters)
    {
        var reply = inner.Call(feature, fn, parameters);
        if (feature == 0) { if (reply[0] != 0) ids[reply[0]] = parameters[0] << 8 | parameters[1]; }
        else Calls.Add(Tuple.Create(ids.ContainsKey(feature) ? ids[feature] : -1, fn, parameters));
        return reply;
    }
}
