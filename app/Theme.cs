using System;
using System.Drawing;
using System.Globalization;
using Microsoft.Win32;

static class Theme
{
    public static Color Background, Sidebar, Card, Button, ButtonHover, ButtonPressed, Border, Text, Dim,
        Accent, AccentBackground, OnAccent, Warn, Error, KeyOff, PictureBackground, Track;
    public static bool Light;
    public static event Action Changed;
    public static readonly string[] Modes = { "system", "dark", "light" }, Accents = { "g-lite", "windows", "custom" };
    // The accent in use when it isn't G-Lite's own.
    static Color? accentColor;

    static Theme() { Set(false, null); }

    public static string Mode
    {
        get { var mode = Settings.Get("theme"); return Array.IndexOf(Modes, mode) > 0 ? mode : "system"; }
        set { Settings.Set("theme", value); Apply(); }
    }

    public static string AccentMode
    {
        get { var accent = Settings.Get("accent"); return Array.IndexOf(Accents, accent) > 0 ? accent : "g-lite"; }
        set { Settings.Set("accent", value); Apply(); }
    }

    // RGB, kept as 6 hex digits.
    public static int CustomAccent
    {
        get { int color; return int.TryParse(Settings.Get("accent-color"), NumberStyles.HexNumber, null, out color) ? color : 0x3FA98A; }
        set { Settings.Set("accent-color", value.ToString("X6")); Apply(); }
    }

    public static bool SystemLight() { return Personalize("AppsUseLightTheme"); }

    // AccentPalette holds the accent's shades, 4 bytes (RGBA) each, lightest first; like Windows 11, G-Lite takes the second
    // lightest on a dark background and the first dark one (after the accent itself) on a light one. AccentColor (ABGR) is the
    // accent itself, for when there's no palette.
    static Color? WindowsAccent(bool light)
    {
        try
        {
            using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Accent"))
            {
                var palette = key == null ? null : key.GetValue("AccentPalette") as byte[];
                int at = (light ? 4 : 1) * 4;
                if (palette != null && palette.Length >= 32) return Color.FromArgb(palette[at], palette[at + 1], palette[at + 2]);
            }
            using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\DWM"))
            {
                var value = key == null ? null : key.GetValue("AccentColor");
                if (value is int) return Color.FromArgb((int)value & 0xFF, (int)value >> 8 & 0xFF, (int)value >> 16 & 0xFF);
            }
        }
        catch { }
        return null;
    }

    public static bool TaskbarLight() { return Personalize("SystemUsesLightTheme"); }

    static bool Personalize(string name)
    {
        try
        {
            using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                return key != null && Equals(key.GetValue(name), 1);
        }
        catch { return false; }
    }

    public static void Apply()
    {
        var mode = Mode;
        bool light = mode == "light" || (mode == "system" && SystemLight());
        var accent = AccentMode == "windows" ? WindowsAccent(light) : AccentMode == "custom" ? FromHex(CustomAccent) : (Color?)null;
        if (light == Light && accent == accentColor) return;
        Set(light, accent);
        if (Changed != null) Changed();
    }

    public static Color FromHex(int value) { return Color.FromArgb(value >> 16 & 0xFF, value >> 8 & 0xFF, value & 0xFF); }

    static Color Mix(Color from, Color to, double amount)
    {
        return Color.FromArgb((int)(from.R + (to.R - from.R) * amount), (int)(from.G + (to.G - from.G) * amount),
            (int)(from.B + (to.B - from.B) * amount));
    }

    static void Set(bool light, Color? accent)
    {
        Light = light;
        accentColor = accent;
        if (light)
        {
            Background = FromHex(0xF7F7F7); Sidebar = FromHex(0xEDEDED); Card = FromHex(0xFFFFFF);
            Button = FromHex(0xECECEC); ButtonHover = FromHex(0xE0E0E0); ButtonPressed = FromHex(0xDADADA); Border = FromHex(0xD4D4D4);
            Text = FromHex(0x1A1A1A); Dim = FromHex(0x5A5A5A);
            Accent = FromHex(0x1D7A60); AccentBackground = FromHex(0xD9EDE6); OnAccent = FromHex(0xFFFFFF);
            Warn = FromHex(0x9A5B00); Error = FromHex(0xC42B1C); KeyOff = FromHex(0xD6D6D6); PictureBackground = FromHex(0xBDBDBD); Track = FromHex(0xC8C8C8);
        }
        else
        {
            Background = FromHex(0x1C1C1C); Sidebar = FromHex(0x161616); Card = FromHex(0x232323);
            Button = FromHex(0x2B2B2B); ButtonHover = FromHex(0x353535); ButtonPressed = FromHex(0x242424); Border = FromHex(0x3A3A3A);
            Text = FromHex(0xE8E8E8); Dim = FromHex(0xA3A3A3);
            Accent = FromHex(0x3FA98A); AccentBackground = FromHex(0x26302D); OnAccent = FromHex(0x0C1A15);
            Warn = FromHex(0xE0A458); Error = FromHex(0xE5534B); KeyOff = FromHex(0x2E2E2E); PictureBackground = FromHex(0x151515); Track = FromHex(0x444444);
        }
        if (accent == null) return;
        // Another accent: a tint of it under picked things, close to G-Lite's own, and dark or white text on it, whichever reads.
        Accent = accent.Value;
        AccentBackground = Mix(Background, Accent, light ? 0.16 : 0.14);
        OnAccent = 0.299 * Accent.R + 0.587 * Accent.G + 0.114 * Accent.B > 150 ? FromHex(0x111111) : FromHex(0xFFFFFF);
    }
}
