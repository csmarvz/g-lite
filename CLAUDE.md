# G-Lite

A Windows tray app that controls the lighting on a Logitech G515 LIGHTSPEED TKL without G HUB.

## Build

```
app\build.cmd
```

The build script runs `build\selftest.exe` and stops if it fails. It then builds `build\GLite.exe`, kills the running app and copies the new exe to `%LOCALAPPDATA%\GLite`. It doesn't restart the app. Run the app with `--demo` to use a simulated keyboard.

The compiler is `%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe`, targeting .NET Framework 4.8 with WinForms. It supports **C# 5 only**: no `$""` strings, no `?.`, no `=>` members, no `nameof`. Don't add NuGet packages or other dependencies.

## Files

- `app/Keyboard.cs`:
  - `Hidpp`: HID++ 2.0 over the receiver
  - `Lighting`: the lighting settings
  - `G515`: the effect table, profile encode and decode, CRC32, onboard file read and write, preview
- `app/Program.cs`:
  - `Theme`
  - `Slider`
  - `Panel515`: the panel, the device work queue and the preview debounce
  - `Main`: the tray icon, the single-instance mutex and `--demo`
- `app/Fake.cs`:
  - `Model`: loads `models/*.json`, which are embedded resources
  - `FakeHidpp`: an in-memory G515
  - `KeyboardView`: the demo window
- `app/Game.cs`: Blocks and Snake, played on the 15x5 main key block
- `app/SelfTest.cs`: self-checks with golden bytes from the real keyboard. Add a check here whenever the encoding or rules change.
- `app/models/g515-tkl.json`: the key layout
  - A row is written `Label:width|...`.
  - `_n` is a gap of n units.
  - A row made only of gaps adds vertical space.

## Protocol essentials

- The receiver is VID 046D, PID C547. Long reports go through `MI_02&COL02`, at device index 1. The keyboard can take about 3 s to answer when it's asleep.
- Features: 0x8071 for RGB effects, 0x8040 for brightness (0–100), 0x8101 for profiles. The code looks up their indexes at runtime.
- Never write the factory profile (0x0101) or the factory directory (0x0100). The user profile is 0x0008 and the user directory is 0x0000.
- Windows Dynamic Lighting can fight with the app. The panel warns when Dynamic Lighting is on.

## Rules

- The repo is MIT. Don't bring in code from GPL projects.
- `backup-*/` and `lighting-backup.reg` stay untracked.
- No code comments unless asked.
- Never commit unless explicitly asked.
- Keep the binary small. Prefer the simplest thing that works.
- Write plainly, without filler.
