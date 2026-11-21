using System;
using System.Drawing;
using Microsoft.Win32;

static class Theme
{
    public static Color Background, Sidebar, Card, Button, ButtonHover, ButtonPressed, Border, Text, Dim,
        Accent, AccentBackground, OnAccent, Warn, Error, KeyOff, PictureBackground, Track;
    public static bool Light;
    public static event Action Changed;
    public static readonly string[] Modes = { "system", "dark", "light" };

    static Theme() { Set(false); }

    public static string Mode
    {
        get { var mode = Settings.Get("theme"); return Array.IndexOf(Modes, mode) > 0 ? mode : "system"; }
        set { Settings.Set("theme", value); Apply(); }
    }

    public static bool SystemLight() { return Personalize("AppsUseLightTheme"); }

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
        if (light == Light) return;
        Set(light);
        if (Changed != null) Changed();
    }

    public static Color FromHex(int value) { return Color.FromArgb(value >> 16 & 0xFF, value >> 8 & 0xFF, value & 0xFF); }

    static void Set(bool light)
    {
        Light = light;
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
    }
}
