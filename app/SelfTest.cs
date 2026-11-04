using System;
using System.Linq;

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
        Console.WriteLine("selftest ok");
        return 0;
    }
}
