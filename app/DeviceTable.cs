using System;
using System.Linq;
using System.Windows.Forms;

class DeviceTable
{
    public Func<FoundDevice, bool> Matches;
    public Type Screen;
    public Func<FoundDevice, Hidpp, MouseEventHandler, Control> Make;
    public Func<FoundDevice, Hidpp> Fake;

    public static readonly DeviceTable[] All =
    {
        new DeviceTable { Matches = found => found.IsG515, Screen = typeof(KeyboardPage),
            Make = (found, fake, drag) => new KeyboardPage(found, fake, drag, KeyboardKinds.G515) },
        new DeviceTable { Matches = found => found.IsG915, Screen = typeof(KeyboardPage),
            Make = (found, fake, drag) => new KeyboardPage(found, fake, drag,
                found.Name.ToUpper().Contains("TKL") ? KeyboardKinds.G915Tkl
                : found.Name.ToUpper().Contains("G815") ? KeyboardKinds.G815
                : KeyboardKinds.G915),
            Fake = found => new FakeG915 { ButtonSlots = found.Name.ToUpper().Contains("TKL") ? 15 : 8 } },
        new DeviceTable { Matches = found => found.IsG512X, Screen = typeof(KeyboardPage),
            Make = (found, fake, drag) => new KeyboardPage(found, fake, drag, KeyboardKinds.G512X),
            Fake = found => FakeG915.G512X() },
        new DeviceTable { Matches = found => found.IsRgbKeyboard, Screen = typeof(KeyboardPage),
            Make = (found, fake, drag) => new KeyboardPage(found, fake, drag, KeyboardKinds.For(found)),
            Fake = found => FakeG915.G512X() },
        new DeviceTable { Matches = found => found.IsLedKeyboard, Screen = typeof(ZonesPage),
            Make = (found, fake, drag) => new ZonesPage(found, fake, drag),
            Fake = found => new FakeSpeaker { Count = 2 } },
        new DeviceTable { Matches = found => found.IsG510, Screen = typeof(G510Page),
            Make = (found, fake, drag) => new G510Page(found, fake, drag),
            Fake = found => new FakeG510() },
        new DeviceTable { Matches = found => found.IsG9x, Screen = typeof(MousePage),
            Make = (found, fake, drag) => new MousePage(found, fake, drag, new G9xMouse(G9xModel.G9x)),
            Fake = found => new FakeG9x() },
        new DeviceTable { Matches = found => found.IsG5, Screen = typeof(MousePage),
            Make = (found, fake, drag) => new MousePage(found, fake, drag, G5Mouse.G5()),
            Fake = found => FakeG9x.Short(0x82) },
        new DeviceTable { Matches = found => found.IsG9, Screen = typeof(MousePage),
            Make = (found, fake, drag) => new MousePage(found, fake, drag, G5Mouse.G9()),
            Fake = found => FakeG9x.Short(0x84) },
        new DeviceTable { Matches = found => found.IsG500, Screen = typeof(MousePage),
            Make = (found, fake, drag) => new MousePage(found, fake, drag, new G9xMouse(found.G500Model)),
            Fake = found => FakeG9x.G500s() },
        new DeviceTable { Matches = found => found.IsG600, Screen = typeof(MousePage),
            Make = (found, fake, drag) => new MousePage(found, fake, drag, new G600Mouse()),
            Fake = found => new FakeG600() },
        new DeviceTable { Matches = found => found.IsMouse && found.Has(0x2202), Screen = typeof(MousePage),
            Make = (found, fake, drag) => new MousePage(found, fake, drag, new HidppMouse()),
            Fake = found => found.WirelessId == 0x40B8 ? FakeMouse.Dex() : FakeMouse.ProX2() },
        new DeviceTable { Matches = found => found.IsMouse, Screen = typeof(MousePage),
            Make = (found, fake, drag) => new MousePage(found, fake, drag, new HidppMouse()),
            Fake = found => FakeMouse.For(found) },
        new DeviceTable { Matches = found => found.IsG560, Screen = typeof(ZonesPage),
            Make = (found, fake, drag) => new ZonesPage(found, fake, drag),
            Fake = found => new FakeSpeaker() },
        new DeviceTable { Matches = found => found.IsLight, Screen = typeof(LightPage),
            Make = (found, fake, drag) => new LightPage(found, fake, drag),
            Fake = found => new FakeLight() },
        new DeviceTable { Matches = found => found.IsHeadset, Screen = typeof(HeadsetPage),
            Make = (found, fake, drag) => new HeadsetPage(found, fake, drag),
            Fake = found => new FakeHeadset() },
    };

    public static readonly DeviceTable Other = new DeviceTable { Matches = OtherDevice.Supports, Screen = typeof(OtherDevicePage),
        Make = (found, fake, drag) => new OtherDevicePage(found, fake, drag),
        Fake = found => new FakeOther() };

    public static DeviceTable For(FoundDevice found)
    {
        if (found.Asleep) return null;
        if (!found.IsG) return Other.Matches(found) ? Other : null;
        return All.FirstOrDefault(entry => entry.Matches(found));
    }
}
