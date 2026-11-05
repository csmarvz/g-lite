using System;
using System.Linq;
using System.Collections.Generic;

static class SelfTest
{
    static byte[] Hex(string s) { return Enumerable.Range(0, s.Length / 2).Select(i => Convert.ToByte(s.Substring(i * 2, 2), 16)).ToArray(); }

    static void Check(bool ok, string what) { if (!ok) { Console.WriteLine("FAIL " + what); Environment.Exit(1); } }

    static int Main()
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
        System.IO.File.Delete(Settings.Location);
        Console.WriteLine("selftest ok");
        return 0;
    }
}
