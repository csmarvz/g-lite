# G-Lite

A Windows tray app that controls the lighting on a Logitech G515 LIGHTSPEED TKL without G HUB.

## Build

```
app\build.cmd
```

The build script runs `build\selftest.exe`, which also writes `build\GLite.ico`, and stops if it fails. The ico is embedded in the exe. It then builds `build\GLite.exe`, kills the running app and copies the new exe to `%LOCALAPPDATA%\GLite`. It doesn't restart the app. Run the app with `--demo` to use a simulated keyboard.

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
- `app/Game.cs`: Blocks, played on the 15x5 grid over the main key block, and Snake and Mines (`KeyGame`), played on the keys of that block
- `app/Update.cs`: `Updater`, which checks GitHub releases, verifies the download against `checksums.txt`, swaps the exe and restarts. If the exe's folder isn't writable, it runs the new exe elevated with `--swap <path>` to do the swap, then restarts unelevated.
- `app/Settings.cs`: `settings.txt` with `key=value` lines, next to the exe, or in `%LOCALAPPDATA%\GLite` if that folder isn't writable. Run on startup stays in the Windows `Run` key.
- `app/Win.cs`: per-monitor DPI awareness, the scale of a screen, pixel fonts, rounded corners. Windows are laid out at 100% and scaled with `Scale()`; `WM_DPICHANGED` rescales them.
- `app/Logo.cs`: draws the GL logo at any size and writes the multi-size ico.
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
- The version is set by the three assembly attributes at the top of `app/Program.cs`. Change all three together.
- To release: set the version, add its section to `CHANGELOG.md`, commit, then push a tag `v<version>`. `.github/workflows/release.yml` checks the tag matches the version, builds and publishes `GLite.exe` with `checksums.txt`. Releases are not marked as pre-releases, so `/releases/latest` finds them.
- Never commit unless explicitly asked.
- Keep the binary small. Prefer the simplest thing that works.
- Write plainly, without filler.
