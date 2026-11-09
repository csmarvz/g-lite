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
  - `Hidpp`: HID++ 2.0 over the receiver, and `Register` and `Upload` for HID++ 1.0 registers and memory uploads when it's given the short collection
  - `Lighting`: the lighting settings
  - `G515`: the effect table, profile encode and decode, CRC32, onboard file read and write, preview
  - `G915`: live lighting on the G915 and G915 TKL (0x8071 keys and logo clusters, the older effect ids). No saving yet. See `docs/G915.md`.
- `app/Program.cs`:
  - `MainWindow`: the device sidebar, one page per found device, rescans on plug and unplug, the Settings and Logs screens, updates and run on startup
  - `Main`: the tray icon, the single-instance mutex and `--demo`
- `app/Devices.cs`:
  - `Devices.Scan`: finds every Logitech HID++ interface. Direct devices answer at index 0xFF. Receivers list their paired slots (register 0xB5), and each slot is pinged. A port with neither is tried as an old HID++ 1.0 USB device at index 0x00 (register 0xF1, firmware), like the G9x Laser Mouse: its name comes from the USB product string (`Found.Hidpp10`, `Found.ShortPath`). The G9x gets the mouse screen, others the info page. It returns G devices only.
  - `Found`: what the scan learned about a device. `Models` holds its product IDs per connection, decoded from 0x0003 fn 0 (bytes 5–6 the connection bits: 0x01/0x02 Bluetooth, 0x04 LIGHTSPEED, 0x08 USB; bytes 7–12 one ID per set bit, in bit order), or from the USB path for HID++ 1.0 devices. Retail model numbers aren't on the device.
  - `Probe`: the scan's HID++ calls. HID++ 1.0 errors come back on the receiver's short collection.
  - Names of devices seen awake are kept as `seen.<wireless id>` in `settings.txt`, for when they're asleep.
- `app/Pages.cs`:
  - `DevicePage`: what every device screen shares: the title, the profile row, the device work queue, the status line and the Backups menu. Profiles are stored as `profile.<device>.<name>`.
  - `ColorRow`: the colour swatches
  - `ReportView`: the report screen that Report opens on every device screen, with Copy and Open GitHub issue
  - `InfoPage` for G devices G-Lite can't change yet or that are asleep, and `EmptyPage`
- `app/Page515.cs`:
  - `Page515`: the G515 screen, the preview debounce, the backups menu and the offer to restore when the saved lighting is half written. Profiles are `profile.g515.<name>`.
  - On a G915 the same screen has no Save, Backups or idle settings. It keeps the lighting as `lighting.<device>` in `settings.txt` and sets it again on each load. Profiles are `profile.g915.<name>`.
  - `KeyPicture`: the live key picture
- `app/Screen.cs`: the Screen effect. `ScreenSetup` (monitor, up to five zones as fractions of the screen, key id to zone, boost, update rate, smoothing, pauses; kept as `screen.<device>`, and `screen.on.<device>=1` while it runs), `ScreenCapture` (shrinks each zone with StretchBlt and averages it), `KeyLights` (0x8081: fn 0 lists the lit ids, fn 6 sets one colour on up to 13 ids, fn 7 shows the frame, sent with `Hidpp.Quiet` so frames stay out of the log), `Presence` (screen off and lock, from the main window), `ScreenPreview` and `ZoneList`. See `docs/screen.md`.
- `app/PageMouse.cs`: `PageMouse`, the mouse screen with Save to mouse, its backups and the offer to restore a damaged profile, and `MousePicture`. On the G9x it has only Off and Static, and no factory restore.
- `app/PageHeadset.cs`: `PageHeadset`, the headset screen, and `HeadsetPicture`
- `app/Log.cs`: `Log`, `log.txt` next to `settings.txt` (in the temp folder with `--demo`), one tab-separated line per event: time, device, kind (`start`, `found`, `lost`, `you`, `backup`, `sent`, `error`), text, result. `Hidpp.Call` logs every call that isn't in `Report.Reads`, and reads only when they time out. At 512 KB it moves to `log.old.txt`. `LogsPage` in `app/Pages.cs` shows it.
- `app/Report.cs`: `Report.Build`, a text report of a device for an issue. It calls only the functions listed in `Report.Reads`. Add a function there only when it's known to only read. It leaves out the unit ID (0x0003 fn 0), the friendly name (0x0007) and the device path. `Report.Registers` is the report for HID++ 1.0 devices: it reads every short (0x81) and long (0x83) register, which only read, then the profile directory (page 1) and each profile page it lists (0xA2). It stops after two timeouts in a row.
- `app/Backup.cs`: `Backups`, a copy of what's on a device taken before every save or restore, in `%LOCALAPPDATA%\GLite\backups\<device>\`: `G515` for the keyboard's four onboard files, the device name (like `gprowireless`) for a mouse's profile sector. Each line is `<4 hex digits>=<bytes>`. It keeps the first backup and the last ten.
- `app/Mouse.cs`: `GMouse`, which reads and sets DPI (0x2201), the DPI stage (0x8100), the report rate (0x8060) and the lighting (0x8070), and reads, builds and writes the active onboard profile (0x8100). `G9x`: the G9x Laser Mouse over HID++ 1.0 registers (0x57 colour, 0x63 DPI in units of 400/17, 0x64 report interval, 0x0F and 0xA2 for the active profile's stages), set live, and saved by writing the active profile page. `PageMouse` shows it. See `docs/G9x.md`.
- `app/Headset.cs`: `GHeadset`, which reads and sets sidetone (0x8300) and the auto-off time (0x1F20 fn 1 and fn 2, in minutes, 0 is never). 0x1F20 fn 0 is the battery voltage, and byte 2 is 3 while charging.
- `app/Ui.cs`:
  - `Theme`: dark, light or system, from `theme=` in `settings.txt`
  - The owner-drawn controls. They read `Theme` when they paint. Labels and containers register their colours with `Ui.Style`, so a theme change restyles them.
- `app/Fake.cs`:
  - `Model`: loads `models/*.json`, which are embedded resources
  - `FakeHidpp`: an in-memory G515
  - `FakeG915`, `FakeMouse`, `FakeG9x` and `FakeHeadset`: in-memory devices for `--demo` and the self-tests
  - `KeyboardView`: the demo window
- `app/Game.cs`: Blocks, played on the 15x5 grid over the main key block, and Snake and Mines (`KeyGame`), played on the keys of that block
- `app/Update.cs`: `Updater`, which checks GitHub releases, verifies the download against `checksums.txt`, swaps the exe and restarts. If the exe's folder isn't writable, it runs the new exe elevated with `--swap <path>` to do the swap, then restarts unelevated.
- `app/Settings.cs`: `settings.txt` with `key=value` lines, next to the exe, or in `%LOCALAPPDATA%\GLite` if that folder isn't writable. Run on startup stays in the Windows `Run` key.
- `app/Win.cs`: per-monitor DPI awareness, the scale of a screen, pixel fonts, rounded corners. Windows are laid out at 100% and scaled with `Scale()`; `WM_DPICHANGED` rescales them.
- `app/Logo.cs`: draws the GL logo at any size and writes the multi-size ico.
- `app/SelfTest.cs`: self-checks with golden bytes from the real keyboard. Add a check here whenever the encoding or rules change.
- `app/models/g515-tkl.json`, `g515-tkl-iso.json`, `g915-tkl.json`, `g915.json`: the key layouts. The G515 page picks ISO when 0x4540 reports a layout above 2.
  - A row is written `Label:width|...`.
  - `_n` is a gap of n units.
  - A row made only of gaps adds vertical space.
  - `ids` has one row per row: the 0x8081 lighting id of each key in hex (USB key code minus 3, modifiers 0x68–0x6F), `--` for a key with no known LED.

## Protocol essentials

- The receiver is VID 046D, PID C547. Long reports go through `MI_02&COL02`, at device index 1. The keyboard can take about 3 s to answer when it's asleep.
- Features: 0x8071 for RGB effects, 0x8040 for brightness (0–100), 0x8101 for profiles. The code looks up their indexes at runtime.
- Never write the factory profile (0x0101) or the factory directory (0x0100). The user profile is 0x0008 and the user directory is 0x0000.
- Windows Dynamic Lighting can fight with the app. The panel warns when Dynamic Lighting is on.
- HID++ 1.0 (G9x): short reports `10 <idx> <sub> <register> p0 p1 p2`, sub-id 0x81 reads and 0x80 sets a short register, 0x83 reads and 0x82 sets a long one, 0x8F is an error. The G9x answers at index 0x00. `G9x.Store` saves a page: 0x0F `FF`, then for each half 0xA1 `01`, an upload to RAM page 0 (0x92, then 0x93, each acked by 0x50), erase (0xA0 op 2, first half only) and copy (0xA0 op 3), then 0x0F `00 <index>` and a read back. Never without a backup, and never to pages 0 (RAM) or 1 (the directory).
- Mouse changes are sent live, and Save to mouse writes them into the active onboard profile. On the G Pro Wireless in onboard mode, DPI, the DPI stage (0x8100 fn 0x0C) and lighting (0x8070 fn 3) can be set live, but the report rate is refused, so it only changes on save.
- Mouse onboard profiles (0x8100, profile format 3 only): fn 0 gives the sector size at bytes 7–8 (255 on the G Pro Wireless), fn 4 the active profile's sector, fn 5 reads 16 bytes at a sector and offset.
  - Layout: byte 0 is the report interval in ms, byte 1 the default DPI stage, bytes 3–12 the five stages (little endian), 0xA0 the name (UTF-16), LED records of 11 bytes from 0xD0 (effect id, then the 0x8070 parameters). The last two bytes are a CRC-16 (CCITT, start 0xFFFF, big endian) over the rest.
  - Writing: fn 6 with sector, offset 0 and length, fn 7 for each 16 bytes, fn 8 to finish, then read back. fn 3 with the active profile reloads it.
  - Never write sector 0x0000 (the profile directory) or 0x0100 and up (the factory profiles). The factory profile at 0x0101 has no checksum.
- The PRO X Wireless dongle (PID 0ABA) answers HID++ itself at index 0xFF. Its sidetone only plays while an app uses the microphone. It stores an equalizer (0x8310: fn 2 reads one signed byte per band, fn 3 writes a 0x02 followed by them) but doesn't apply it, so G-Lite doesn't show it. G HUB applies EQ on the PC.

## Rules

- The repo is MIT. Don't bring in code from GPL projects.
- `backup-*/` and `lighting-backup.reg` stay untracked.
- No code comments unless asked.
- The version is set by the three assembly attributes at the top of `app/Program.cs`. Change all three together.
- To release: set the version, add its section to `CHANGELOG.md`, commit, then push a tag `v<version>`. `.github/workflows/release.yml` checks the tag matches the version, builds and publishes `GLite.exe` with `checksums.txt`. Releases are not marked as pre-releases, so `/releases/latest` finds them.
- Never commit unless explicitly asked.
- Keep the binary small. Prefer the simplest thing that works.
- Write plainly, without filler.
