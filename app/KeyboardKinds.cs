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
    // The G815 runs a profile written again only after leaving onboard mode and coming back (G915Lighting.Store).
    public bool ReloadThroughHostMode;
    // The brightness levels 0x8040 keeps; any other value drops to the one below (the G815, tried on a real keyboard). Null for any.
    public int[] BrightnessLevels;
    // The names of the keys whose bindings the profile holds first (G1-G5); null when G-Lite doesn't edit them.
    public string[] GKeys;
    // The G keys come from a layout seen in one report (the G915 TKL's F1-F12) or inferred (the G915's G1-G5, as the G815's), so
    // they're shown only when 0x8100 counts exactly them and M1-M3, and the view says it's experimental.
    public bool GKeysExperimental;
    // Whether the Game view can set the Game mode key's list of keys: through 0x4522, tried on the G815, and experimental on the G915s,
    // which have 0x4522 but where OpenGHub saw it change nothing; through 0x4523 (GameModeKeys.Bitmap), tried on the G515, and
    // experimental on the G915 X and the G512 X 75, whose reports list the same 0x4523 and 0x1B10 versions.
    public GameModeSupport GameMode;
    // Wave 0x04 also goes up (direction 7) and down (2) on the G815, and 0 cycles through every direction; 4, 5, 9 and 10 change
    // nothing (tried on a real keyboard). The G915s wait for a try on a real one.
    public bool VerticalWave;
    // An Intensity slider for the effects that have one (0x8071's intensity byte; 0 shows as 100 on the G815). It doesn't make finer
    // brightness steps: tried as the level above with the intensity lowered, 60% looked darker than 50% (on a real G815).
    public bool Intensity;
    public int[] Bar;

    public static string File(string name)
    {
        name = name.ToUpper();
        bool small = name.Contains("TKL");
        return name.Contains("G915 X") ? (small ? "g915-x-tkl.json" : "g915-x.json")
            : name.Contains(" 60") ? "g-60.json"
            : name.Contains(" 75") ? "g512-x-75.json"
            : name.Contains(" 98") ? "g-full.json"
            : "g515-tkl.json";
    }

    public static KeyboardKinds For(FoundDevice found)
    {
        string file = File(found.Name);
        var keys = LoadLayout(file, found.ShortName);
        // The G515's top strip and the G512 X 75's knobs are theirs: a keyboard that borrows the picture shows neither.
        if (file == "g515-tkl.json")
        {
            keys.Rows = keys.Rows.Skip(2).ToArray();
            keys.Ids = keys.Ids.Skip(2).ToArray();
        }
        if (file == "g512-x-75.json")
        {
            keys.Rows = keys.Rows.Take(7).ToArray();
            keys.Ids = keys.Ids.Take(7).ToArray();
            keys.Rows[0] = keys.Rows[0].Substring(0, keys.Rows[0].IndexOf("|_.4|Knob"));
            keys.Ids[0] = keys.Ids[0].Replace(" -- --", "");
        }
        if (file == "g515-tkl.json" || file == "g512-x-75.json") keys.Shaped = null;
        // The G915 X's pictures come from projects that lit every id on the keyboard (docs/G915X.md), so they aren't a guess.
        bool known = file.StartsWith("g915-x");
        return new KeyboardKinds
        {
            Keys = keys, Prefix = "profile." + found.SettingsKey + ".", Guessed = !known,
            GameMode = known ? GameModeSupport.Experimental : GameModeSupport.None
        };
    }

    static KeyLayout LoadLayout(string file, string name)
    {
        var model = KeyLayout.Load(file);
        model.Name = name;
        return model;
    }

    public static readonly KeyboardKinds
        G515 = new KeyboardKinds
        {
            Keys = KeyLayout.Load("g515-tkl.json"), Prefix = "profile.g515.", Saves = true, GameMode = GameModeSupport.Tested
        },
        G915Tkl = new KeyboardKinds
        {
            Keys = KeyLayout.Load("g915-tkl.json"), Prefix = "profile.g915.", Profile = true, GameMode = GameModeSupport.Experimental,
            GKeys = new[] { "F1", "F2", "F3", "F4", "F5", "F6", "F7", "F8", "F9", "F10", "F11", "F12" }, GKeysExperimental = true
        },
        G915 = new KeyboardKinds
        {
            Keys = KeyLayout.Load("g915.json"), Prefix = "profile.g915.", Profile = true, GameMode = GameModeSupport.Experimental,
            GKeys = new[] { "G1", "G2", "G3", "G4", "G5" }, GKeysExperimental = true
        },
        G815 = new KeyboardKinds { Keys = KeyLayout.Load("g815.json"), Prefix = "profile.g815.", Profile = true, NoIdleTimes = true,
            ReloadThroughHostMode = true, BrightnessLevels = new[] { 0, 12, 25, 50, 100 },
            GKeys = new[] { "G1", "G2", "G3", "G4", "G5" }, GameMode = GameModeSupport.Tested, VerticalWave = true,
            Intensity = true },
        G512X = new KeyboardKinds
        {
            Keys = KeyLayout.Load("g512-x-75.json"), Prefix = "profile.g512x.", Bar = Enumerable.Range(0xA0, 33).ToArray(),
            GameMode = GameModeSupport.Experimental
        };
}

enum GameModeSupport { None, Tested, Experimental }
