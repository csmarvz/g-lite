using System.Linq;

class KeyboardKinds
{
    public KeyLayout Keys;
    KeyLayout iso;
    bool isoBuilt;
    public KeyLayout Iso { get { if (!isoBuilt) { iso = Keys.Iso(); isoBuilt = true; } return iso; } }
    public string Prefix;
    public bool Saves, Profile, Guessed;
    // The G815 is wired: its profiles hold 0 for both idle times, and G HUB writes the power-save records as copies of the others.
    public bool NoIdleTimes;
    public int[] Bar;

    public static string File(string name)
    {
        name = name.ToUpper();
        bool small = name.Contains("TKL");
        return name.Contains("G915 X") ? (small ? "g915-tkl.json" : "g915.json")
            : name.Contains(" 60") ? "g-60.json"
            : name.Contains(" 75") ? "g512-x-75.json"
            : name.Contains(" 98") ? "g-full.json"
            : "g515-tkl.json";
    }

    public static KeyboardKinds For(FoundDevice found)
    {
        string file = File(found.Name);
        var keys = LoadLayout(file, found.ShortName);
        if (file == "g512-x-75.json")
        {
            keys.Rows = keys.Rows.Take(7).ToArray();
            keys.Ids = keys.Ids.Take(7).ToArray();
        }
        return new KeyboardKinds { Keys = keys, Prefix = "profile." + found.SettingsKey + ".", Guessed = true };
    }

    static KeyLayout LoadLayout(string file, string name)
    {
        var model = KeyLayout.Load(file);
        model.Name = name;
        return model;
    }

    public static readonly KeyboardKinds
        G515 = new KeyboardKinds { Keys = KeyLayout.Load("g515-tkl.json"), Prefix = "profile.g515.", Saves = true },
        G915Tkl = new KeyboardKinds { Keys = KeyLayout.Load("g915-tkl.json"), Prefix = "profile.g915.", Profile = true },
        G915 = new KeyboardKinds { Keys = KeyLayout.Load("g915.json"), Prefix = "profile.g915.", Profile = true },
        G815 = new KeyboardKinds { Keys = LoadLayout("g915.json", "G815"), Prefix = "profile.g815.", Profile = true, NoIdleTimes = true },
        G512X = new KeyboardKinds
        {
            Keys = KeyLayout.Load("g512-x-75.json"), Prefix = "profile.g512x.", Bar = Enumerable.Range(0xA0, 33).ToArray()
        };
}
