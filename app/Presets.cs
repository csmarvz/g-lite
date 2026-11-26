using System.Collections.Generic;
using System.Linq;

// A device's presets in settings.txt, without UI: each one as <prefix><name>, holding the screen's Current text, and the one picked
// last as preset.<device>, its name with "=edited" once the settings moved away from it (a name can't hold "="). DevicePage shows
// and picks them.
class Presets
{
    readonly string prefix;
    // The device whose settings key names the picked preset's entry.
    public FoundDevice Found;

    public Presets(string prefix, FoundDevice found)
    {
        this.prefix = prefix;
        Found = found;
    }

    public IEnumerable<string> Names()
    {
        return Settings.Keys().Where(key => key.StartsWith(prefix)).Select(key => key.Substring(prefix.Length)).OrderBy(key => key);
    }

    public string Text(string name) { return Settings.Get(prefix + name); }

    public void Store(string name, string text) { Settings.Set(prefix + name, text); }

    public void Delete(string name) { Settings.Remove(prefix + name); }

    public void Rename(string oldName, string newName)
    {
        Settings.Set(prefix + newName, Settings.Get(prefix + oldName));
        Settings.Remove(prefix + oldName);
    }

    string PickedKey { get { return "preset." + Found.SettingsKey; } }

    // "" when none.
    public string Picked
    {
        get { return Settings.Get(PickedKey); }
        set { if (value == "") Settings.Remove(PickedKey); else Settings.Set(PickedKey, value); }
    }
}
