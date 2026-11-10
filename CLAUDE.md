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
  - `MainWindow`: the device sidebar, one page per found device, rescans on plug and unplug, the Settings and Logs screens, updates and run on startup. A click on the G-Lite title (not a drag) opens `KeyboardView` with the games on its own `FakeHidpp`; it doesn't look clickable on purpose
  - `Main`: the tray icon, its menu (the devices with their battery, Scan for devices, Settings, Quit G-Lite: `MenuRow` puts the value in the shortcut column) and its hover popup (`TrayTip` in `app/Ui.cs`: CPU, RAM, each device with its battery, a new version), the single-instance mutex and `--demo`
  - `Support`: the device table. Each entry says which devices it matches (`Found.IsG515`, `IsMouse`…), which screen they get, how to make it (with its driver), and the fake `--demo` uses. The first match wins; asleep or unmatched devices get `InfoPage`. To add a device that works like one already supported, add an entry. For a new protocol, add a driver (like `IMouse`) and an entry.
- `app/Devices.cs`:
  - `Devices.Scan`: finds every Logitech HID++ interface. Direct devices answer at index 0xFF. Receivers list their paired slots (register 0xB5), and each slot is pinged. Ports come from cfgmgr32 (`CM_Get_Device_Interface_List`), not WMI. A port with neither is tried as an old HID++ 1.0 USB device at index 0x00 (register 0xF1, firmware), like the G9x Laser Mouse: its name comes from the USB product string (`Found.Hidpp10`, `Found.ShortPath`). The G9x gets the mouse screen, others the info page. It returns G devices only. The G510 and G510s (C22D, C22E) don't speak HID++: they are listed with `Found.PlainHid`, skipped by the HID++ scan, and get the info page. See `docs/G510.md`.
  - `Found`: what the scan learned about a device. `Models` holds its product IDs per connection, decoded from 0x0003 fn 0 (bytes 5–6 the connection bits: 0x01/0x02 Bluetooth, 0x04 LIGHTSPEED, 0x08 USB; bytes 7–12 one ID per set bit, in bit order), or from the USB path for HID++ 1.0 devices. Retail model numbers aren't on the device.
  - `Probe`: the scan's HID++ calls. HID++ 1.0 errors come back on the receiver's short collection.
  - Names of devices seen awake are kept as `seen.<wireless id>` in `settings.txt`, for when they're asleep.
- `app/Pages.cs`:
  - `DevicePage`: what every device screen shares: the title, the presets row (None, which calls `Revert`, plus the presets, Update, and a … menu with Save as new, Rename and Delete; a dot marks a preset changed since it was loaded or updated), the device work queue, the status line with Revert (with a preset loaded: loads it again, enabled while it has the dot; with None: calls `Revert`, enabled while `Unsaved`) and the Backups menu. Presets are stored on the PC as `profile.<device>.<name>`.
  - `ColorRow`: the colour swatches
  - `ReportView`: the report screen that Report opens on every device screen, with Copy and Open GitHub issue
  - `InfoPage` for G devices G-Lite can't change yet or that are asleep, and `EmptyPage`
- `app/Page515.cs`:
  - `Page515`: the G515 screen, the preview debounce, the backups menu and the offer to restore when the saved lighting is half written. Presets are `profile.g515.<name>`.
  - On a G915 the same screen has no Save, Backups or idle settings. It keeps the lighting as `lighting.<device>` in `settings.txt` and sets it again on each load. Presets are `profile.g915.<name>`.
  - `KeyPicture`: the live key picture
- `app/Screen.cs`: the Screen effect. `ScreenSetup` (monitor, up to five zones as fractions of the screen, key id to zone, boost, update rate, smoothing, pauses; kept as `screen.<device>`, and `screen.on.<device>=1` while it runs), `ScreenCapture` (shrinks each zone with StretchBlt and averages it), `KeyLights` (0x8081: fn 0 lists the lit ids, fn 6 sets one colour on up to 13 ids, fn 7 shows the frame, sent with `Hidpp.Quiet` so frames stay out of the log), `Presence` (screen off and lock, from the main window), `ScreenPreview` and `ZoneList`. See `docs/screen.md`.
- `app/PageMouse.cs`: `PageMouse`, the mouse screen with Save to mouse, its backups and the offer to restore a damaged profile, and `MousePicture`. On the G9x it has only Off and Static, and no factory restore.
- `app/PageHeadset.cs`: `PageHeadset`, the headset screen, and `HeadsetPicture`
- `app/Log.cs`: `Log`, `log.txt` next to `settings.txt` (in the temp folder with `--demo`), one tab-separated line per event: time, device, kind (`start`, `found`, `lost`, `you`, `backup`, `sent`, `error`), text, result. `Hidpp.Call` logs every call that isn't in `Report.Reads`, and reads only when they time out. At 512 KB it moves to `log.old.txt`. `LogsPage` in `app/Pages.cs` shows it.
- `app/Report.cs`: `Report.Build`, a text report of a device for an issue. It calls only the functions listed in `Report.Reads`. Add a function there only when it's known to only read. It leaves out the unit ID (0x0003 fn 0), the friendly name (0x0007) and the device path. `Report.Registers` is the report for HID++ 1.0 devices: it reads every short (0x81) and long (0x83) register, which only read, then the profile directory (page 1) and each profile page it lists (0xA2). It stops after two timeouts in a row. `Report.Plain` is the report for `PlainHid` devices: each HID collection's usage, report lengths and feature report ids, and each feature report read with `HidD_GetFeature`.
- `app/Backup.cs`: `Backups`, a copy of what's on a device taken before every save or restore, in `%LOCALAPPDATA%\GLite\backups\<device>\`: `G515` for the keyboard's four onboard files, the device name (like `gprowireless`) for a mouse's profile sector. Each line is `<4 hex digits>=<bytes>`. It keeps the first backup and the last ten.
- `app/Mouse.cs`: `IMouse`, what the mouse screen needs from a mouse, with two drivers: `HidppMouse` (HID++ 2.0, through `GMouse`) and `G9xMouse` (HID++ 1.0, through `G9x`). `PageMouse` gets its driver from `Support` and doesn't check which mouse it has. A mouse with two 0x8070 zones gets the same light on both: on the G502 LIGHTSPEED, zone 0 (type 1) accepts effects but lights nothing visible, and its three DPI lights don't follow it. Invert scrolling (0x2121): fn 0 byte 1 bit 3 says the wheel can invert, fn 1 reads the mode, fn 2 writes it; G-Lite changes only bit 2 of byte 0 and sends the other bytes back as read. Never bit 0 (it diverts the wheel to software) or bit 1 (high resolution: on the G502 LIGHTSPEED Windows then scrolls about 8 times too fast). The mouse forgets the mode when it turns off, so with `wheel.invert.<device>=1` in `settings.txt` the mouse screen checks it every 5 s (`PageMouse.Watch`). Buttons: the profile holds 4 bytes per button from 0x20 (count in 0x8100 fn 0 byte 5): `80 01` + mouse button bits, `80 02` + modifiers + HID key usage, `80 03` + media usage, `90` + a special (DPI, profile, scroll), `80 00` nothing, `00` a macro. `Bindings` names them; the G-shift layer from 0x60 isn't touched. Any button can be changed, the left one too, but while no button is Left click, Save to mouse and Update are off, and `GMouse.Build` refuses a set without one when the profile had one. Button spy (0x8110): fn 0 the count, fn 1 starts, fn 2 stops; while on, the mouse sends event fn 0 with a big-endian bitmap, bit n = profile slot n (checked on the G502 LIGHTSPEED). It only reports: the buttons keep working. `ButtonSpy` reads it on a second handle while the Buttons view is open. Button names come from `Bindings.Names` (by wireless id) or are numbered. A mouse preset is `MouseSet.ToText`: DPI, the light, the rate as the mouse has it, then the buttons as hex (a preset without a Left click gets it back on slot 0). `GMouse`, which reads and sets DPI (0x2201), the DPI stage (0x8100), the report rate (0x8060) and the lighting (0x8070), and reads, builds and writes the active onboard profile (0x8100). `G9x`: the G9x Laser Mouse over HID++ 1.0 registers (0x57 colour, 0x63 DPI in units of 400/17, 0x64 report interval, 0x0F and 0xA2 for the active profile's stages), set live, and saved by writing the active profile page. `PageMouse` shows it. See `docs/G9x.md`.
- `app/Headset.cs`: `GHeadset`, which reads and sets sidetone (0x8300) and the auto-off time (0x1F20 fn 1 and fn 2, in minutes, 0 is never). 0x1F20 fn 0 is the battery voltage, and byte 2 is 3 while charging.
- `app/Ui.cs`:
  - `Theme`: dark, light or system, from `theme=` in `settings.txt`
  - The owner-drawn controls. They read `Theme` when they paint. Labels and containers register their colours with `Ui.Style`, so a theme change restyles them. `Ui.Battery` draws the battery icon (whole pixels, no anti-aliasing) for the tray popup, the device list (`DevItem`) and the device screen's second line (`SubLine`).
- `app/Fake.cs`:
  - `Model`: loads `models/*.json`, which are embedded resources
  - `FakeHidpp`: an in-memory G515
  - `FakeG915`, `FakeMouse`, `FakeG9x` and `FakeHeadset`: in-memory devices for `--demo` and the self-tests
  - `KeyboardView`: the demo window
- `app/Game.cs`: Blocks, played on the 15x5 grid over the main key block, and Snake and Mines (`KeyGame`), played on the keys of that block
- `app/Update.cs`: `Updater`, which checks GitHub releases, verifies the download against `checksums.txt`, swaps the exe and restarts. If the exe's folder isn't writable, it runs the new exe elevated with `--swap <path>` to do the swap, then restarts unelevated.
- `app/Settings.cs`: `settings.txt` with `key=value` lines, next to the exe, or in `%LOCALAPPDATA%\GLite` if that folder isn't writable. Run on startup stays in the Windows `Run` key.
- `app/Win.cs`: per-monitor DPI awareness, the scale of a screen, pixel fonts, rounded corners. Windows are laid out at 100% and scaled with `Scale()`; `WM_DPICHANGED` rescales them. `PrivateMemory` is the memory Task Manager shows, and `Trim` hands unused memory back to Windows when the window hides.
- `app/Logo.cs`: draws the GL logo at any size and writes the multi-size ico.
- `app/SelfTest.cs`: self-checks with golden bytes from the real keyboard. Add a check here whenever the encoding or rules change.
- `app/Traces.cs`: traces, run by the self-test. Each one drives a real screen (`Page515`, `PageMouse`, `PageHeadset`) on a fake device through a script of clicks, and records every message sent (`Hidpp.Tap`) and the state of the screen after each step. They're compared with `app/tests/<device>.txt`, and the build stops at the first line that differs. A change that's meant to change what's sent: run `build\selftest.exe --record` from `app`, check the diff of `app/tests`, and commit it with the change. A failing run writes `app/tests/<device>.fail.txt`. The screens get test hooks: `Ctl.Press`, `Seg.Pick`, `Drop.Pick`, `Slider.Put`, `Ui.Popped` for menus, `Win.TestIdle`, `Presence.Set`, `Page515.Manual` (no watch timer; the script calls `Watch`). `FakeMouse.G305()` is built from the bytes of a real G305.
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
  - Never write 0x0100 and up (the factory profiles). The factory profile at 0x0101 has no checksum.
  - Sector 0x0000 (the profile directory) is written only by `GMouse.Setup`, on a mouse that runs a factory profile and has no directory (fn 5 answers error 02, like the G305) or an empty one (`State.Fresh`). The first Save to mouse then writes the profile, built from the factory one, to 0x0001, then the directory `GMouse.Directory` builds (`00 01 01 00`, `FF FF 00 00`, `FF` to the end, CRC-16), then loads 0x0001 with fn 3. `WriteSector` still refuses 0x0000. Same layout as libratbag's `hidpp20_onboard_profiles_write_dict`. G HUB writes each entry as `00 0N <enabled> FF` instead (seen on a G502 LIGHTSPEED: five profiles, the first enabled); both are read the same, and G-Lite never rewrites a list that exists.
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
