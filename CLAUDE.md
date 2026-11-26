# G-Lite

A Windows tray app that controls the lighting on a Logitech G515 LIGHTSPEED TKL without G HUB.

## Build

```
app\build.cmd
```

The build script runs `build\selftest.exe`, which also writes `build\GLite.ico`, and stops if it fails. The ico is embedded in the exe. It then builds `build\GLite.new.exe`, and only if that compiles kills the running app (which may be running from `build\GLite.exe`), moves the new exe to `build\GLite.exe` and copies it to `%LOCALAPPDATA%\GLite`. It doesn't restart the app. It prints a line per step, and which step failed. Run the app with `--demo` to use a simulated keyboard. The source files are listed in `app\sources.rsp`, in compile order, for both builds; the self-test adds `SelfTest.cs` and `Traces.cs`. A new source file goes in that list.

The compiler is `%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe`, targeting .NET Framework 4.8 with WinForms. It supports **C# 5 only**: no `$""` strings, no `?.`, no `=>` members, no `nameof`. Don't add NuGet packages or other dependencies.

## Files

`docs/code-map.md` describes each source file: its classes, what they do and the protocol facts behind them. Read a file's entry there before changing it, and keep the entry up to date with the code (a new file gets one). To add a device, follow `docs/adding-a-device.md`.

- `app/Program.cs`: `MainWindow` and `Main` (the tray icon, the single instance, `--demo`)
- `app/DeviceTable.cs`: which screen and driver each device gets; a device that works like a supported one is a new entry here
- `app/Devices.cs`, `app/FoundDevice.cs`: the scan, what it learned about each device, `DeviceWatcher` (events, battery)
- `app/Hidpp.cs`: HID++ 2.0 and 1.0 calls and their errors
- `app/Pages.cs`: `DevicePage` (what every device screen shares: presets, work queue, status line, backups menu), `ColorRow`, `InfoPage`, `EmptyPage`; `app/DeviceSession.cs` (a screen's connection and work queue) and `app/Presets.cs` (its presets in settings.txt), without UI
- Keyboards: `app/KeyboardPage.cs`, `KeyboardKinds.cs`, `KeyboardLighting.cs` (`Lighting`, `G515Profile`, `G915Lighting`), `GameMode.cs` (the Game mode key's list), `KeyPicture.cs`, `KeyLayout.cs` and the layouts in `app/models/`
- Zones (the G560 and the 0x8070 keyboards): `app/ZonesPage.cs`, `ZoneLighting.cs`
- Mice: `app/MousePage.cs`, `MouseShapes.cs` (the pictures' shapes), `Mouse.cs` (`IMouse`, `MouseSettings`), `MouseProtocol.cs`, `G9x.cs` (HID++ 1.0 mice), `G600.cs` (the G600's feature reports), `G300.cs` (the G300's)
- Button bindings, for mice and the G815's G keys: `app/Buttons.cs`
- Headsets: `app/HeadsetPage.cs`, `HeadsetProtocol.cs`
- G510: `app/G510Page.cs`, `G510.cs`
- Litra lights: `app/LightPage.cs`, `LitraProtocol.cs`
- Other Logitech devices: `app/OtherDevicePage.cs`, `OtherDevice.cs`
- `app/Screen.cs`: the Screen effect
- `app/Services.cs`: Settings' Services box: G HUB, Logitech Gaming Software, the LampArray service and Dynamic Lighting, stopped and started (`docs/services.md`)
- `app/LiveEffects.cs`: the live effects G-Lite draws key by key (Starlight, Echo press, Audio visualizer, Rain, Fire, Aurora)
- `app/Report.cs`, `ReportView.cs`: device reports
- `app/Log.cs`, `LogsPage.cs`: `log.txt` and its screen
- `app/Backups.cs`: copies of a device's memory before every write
- `app/Updater.cs`, `UpdatePage.cs`: updates
- The window and its look: `app/Settings.cs`, `Theme.cs`, `Ui.cs`, `Controls.cs`, `Rows.cs` (the rows layout of Settings and the device screens), `DeviceList.cs`, `Tray.cs`, `WindowHelpers.cs`, `Logo.cs`
- The demo: `app/Fake.cs` (in-memory devices, also used by the tests), `KeyboardView.cs`, `Game.cs`
- Tests: `app/SelfTest.cs` (golden bytes: add a check whenever the encoding or rules change), `app/Traces.cs` and `app/tests/` (each screen driven on a fake device, every message compared). A change meant to change what's sent: run `build\selftest.exe --record` from `app`, check the diff of `app/tests` and commit it with the change. Run it too after changing a layout in `app/models/`.
- `site/`: the website, g-lite.org, published by `.github/workflows/pages.yml`

## Protocol essentials

- The receiver is VID 046D, PID C547. Long reports go through `MI_02&COL02`, at device index 1. The keyboard can take about 3 s to answer when it's asleep.
- Features: 0x8071 for RGB effects, 0x8040 for brightness (0–100), 0x8101 for profiles. The code looks up their indexes at runtime.
- Never write the factory profile (0x0101) or the factory directory (0x0100). The user profile is 0x0008 and the user directory is 0x0000.
- Windows Dynamic Lighting can fight with the app. The panel warns when Dynamic Lighting is on.
- HID++ 1.0 (G9x): short reports `10 <idx> <sub> <register> p0 p1 p2`, sub-id 0x81 reads and 0x80 sets a short register, 0x83 reads and 0x82 sets a long one, 0x8F is an error. The G9x answers at index 0x00. `G9xRegisters.Store` saves a page: 0x0F `FF`, then for each half 0xA1 `01`, an upload to RAM page 0 (0x92, then 0x93, each acked by 0x50), erase (0xA0 op 2, first half only) and copy (0xA0 op 3), then 0x0F `00 <index>` and a read back. If it fails after the erase, it writes the page as it was (the caller passes it) and loads it again. Never without a backup, and never to pages 0 (RAM) or 1 (the directory).
- Never set bit 0 of 0x2121 byte 0 (it diverts the wheel to software). No button diversion.
- `Report.Build` calls only the functions in `Report.Reads`. Add a function there only when it's known to only read.
- Mouse profile formats 6 to 8 (`State.WideFormat`: the PRO X SUPERLIGHT 2 and PRO X 2 DEX are format 7, a SUPERSTRIKE 8, per superstrike-studio; 6 and 8 match 7 at every byte G-Lite writes, per OMM's builders in `docs/research/omm`) are written by `MouseProtocol.BuildWide`: byte 0 the rate as a 0x8061 index over the receiver, byte 1 on the cable, byte 2 the default stage, five stages of 5 bytes from 4 (X and Y LE16, lift-off; a stage is rewritten only when its DPI changed, Y keeping its ratio to X), the buttons from 0x30; nothing else changes, not the LED records. Restore the factory profile copies 0x0101 (it has the user layout, per OMM, and no valid CRC, so one is written) after checking its rate bytes, its stages (in the mouse's DPI range) and its default stage (`MouseProtocol.Factory`). At most 12 buttons (48 bytes from 0x30). Formats 9 and up show no stages or buttons and aren't `Writable` (format 10 has another layout). Restore the factory profile is offered on any 0x8100 mouse only when fn 0 byte 4 counts a factory profile (`State.FactoryProfiles`, as OMM does). See `docs/newer-mice.md`.
- Mouse profile formats 4 and 5 (`State.ClassicFormat`: the original PRO X SUPERLIGHT, per GFlick and onboard-lite; the G502 X PLUS, per GOMM and OMM's builders) are saved like formats 2 and 3, except their LED records, which a save never writes (`HidppMouse.Build` passes no zones; Restore the factory profile copies the factory one, its LED records too, keeping only the profile's name). The write counter at 0x12 and the timeouts at 0x1C and 0x1E stay as read too. `MouseProtocol.Setup` stays on formats 2 and 3. A 0x8070 the device flags hidden and internal (`Hidpp.Hidden`, the SUPERLIGHT's status light) shows no lighting. See `docs/newer-mice.md`.
- The G600 isn't HID++: feature reports F0 (active mode and DPI level) and F3–F5 (one 154-byte report per mode) on its FF80 collection. Only read (`State.ReadOnly`), never written until it's tried on a real one. See `docs/G600.md`.
- The G300 has the same transport, with 35-byte profile reports F3–F5 (`G300.cs`, from PeriHub, which saved on a G300). Nothing is live (`State.SaveOnly`): Save to mouse writes the active profile's whole report after a backup and reads it back; left and right buttons and the DPI shift stay as read, and F0, F1 and F2 are never sent. See `docs/G300.md`.
- Mouse changes are sent live, and Save to mouse writes them into the active onboard profile. On the G Pro Wireless in onboard mode, DPI, the DPI stage (0x8100 fn 0x0C) and lighting (0x8070 fn 3) can be set live, but the report rate is refused, so it only changes on save.
- Mouse onboard profiles (0x8100, profile formats 2 and 3, `State.KnownFormat`: format 2, the G502 HERO, G303 and G502 Proteus Spectrum, has the same layout up to the LED blocks, per libratbag, Solaar and cvuchener's hidpp, untested on a real device): fn 0 gives the sector size at bytes 7–8 (255 on the G Pro Wireless), fn 4 the active profile's sector, fn 5 reads 16 bytes at a sector and offset.
  - Layout: byte 0 is the report interval in ms, byte 1 the default DPI stage, bytes 3–12 the five stages (little endian), 0xA0 the name (UTF-16), LED records of 11 bytes from 0xD0 (effect id, then the 0x8070 parameters). The last two bytes are a CRC-16 (CCITT, start 0xFFFF, big endian) over the rest.
  - Writing: fn 6 with sector, offset 0 and length, fn 7 for each 16 bytes, fn 8 to finish, then read back. fn 3 with the active profile reloads it (on a G502 HERO it keeps live changes the profile doesn't hold, like a DPI set live; fn 1 `02` then `01` drops them, per PeriHub).
  - Never write 0x0100 and up (the factory profiles). The factory profile at 0x0101 has no checksum.
  - Sector 0x0000 (the profile directory) is written only by `MouseProtocol.Setup`, on a mouse that runs a factory profile and has no directory (fn 5 answers error 02, like the G305), an empty one, or a sector of all `00` or all `FF` (`MouseProtocol.Unwritten`: the G502 HERO reads zeros, per Solaar #2313) (`State.Fresh`). The first Save to mouse then writes the profile, built from the factory one, to 0x0001, then the directory `MouseProtocol.Directory` builds (`00 01 01 00`, `FF FF 00 00`, `FF` to the end, CRC-16), then loads 0x0001 with fn 3. `WriteSector` still refuses 0x0000. Same layout as libratbag's `hidpp20_onboard_profiles_write_dict`. G HUB writes each entry as `00 0N <enabled> FF` instead (seen on a G502 LIGHTSPEED: five profiles, the first enabled); both are read the same, and G-Lite never rewrites a list that exists.
- The PRO X Wireless dongle (PID 0ABA) answers HID++ itself at index 0xFF. Its sidetone only plays while an app uses the microphone. It stores an equalizer (0x8310: fn 2 reads one signed byte per band, fn 3 writes a persistence byte followed by them) but wasn't heard to apply it with persistence 2; the headset screen shows it as experimental and writes persistence 1. Logitech's spec (x8310 v2) explains why: persistence 2 is EEPROM only, 1 is RAM and EEPROM, 0 RAM only; fn 2 takes a location byte, 0 the stored EQ, 1 the active one. Trying `01` on the PRO X is the next step (the G733 refuses 0 and 1, `docs/research/more-references.md`). G HUB applies EQ on the PC.

## Code conventions

- Names say what they hold, in full words: classes, methods, fields, parameters, locals and lambda parameters. No abbreviations or one-letter names (`reply`, not `r`; `saveButton`, not `saveBtn`; `featureIndex`, not `feat`).
  - Exceptions: `i` and `j` as loop counters; `x`, `y`, `dx`, `dy` as coordinates; `(s, e)` in event handlers and `e` in overrides like `OnPaint(PaintEventArgs e)`; `ex` in `catch`; `fn` for a HID++ function number, as in the docs.
  - A `Hidpp` is `hidpp`. A feature's runtime index is named after the feature (`dpiFeature`, `lightFeature`) or `feature` when there's only one; the feature's id stays a hex literal (`0x8070`).
  - A page's snapshot of its sequence before a send or load is `startedAt`.
- A parameter or local must not share a name with a field of its class: C# lets it hide the field without an error, and code meant for the field then reads the local. When a parameter sets a field, name it apart (`newColor` for the field `color`) or assign with `this.`.
- When renaming, rename by symbol, not text: strings, comments and other members with the same name stay. The traces must pass without `--record`.

## Rules

- The repo is MIT. Don't bring in code from GPL projects.
- `backup-*/` and `lighting-backup.reg` stay untracked.
- Comments only for what the code can't say: why, a decision and what it ruled out, or the source of a fact (a report, a doc in `docs/`, another project). Short, and kept up to date: change or remove them with the code. No comments that repeat what the code does.
- The version is set by the three assembly attributes at the top of `app/Program.cs`. Change all three together.
- To release: set the version, add its section to `CHANGELOG.md`, commit, then push a tag `v<version>`. `.github/workflows/release.yml` checks the tag matches the version, builds and publishes `GLite.exe` with `checksums.txt`. Releases are not marked as pre-releases, so `/releases/latest` finds them. When it succeeds, `.github/workflows/pages.yml` publishes the site again with the release's tag written under the download button (the page doesn't ask GitHub's API, which refuses a visitor after 60 calls an hour).
- Keep every device report in `docs/reports/`, as sent: saved from the clipboard or copied from the message, never retyped (long hex lines get miscounted). One file per device (`<model>-<id>.txt`), the latest replacing an older one, with its row in `docs/reports/README.md`.
- Never commit unless explicitly asked.
- Keep the binary small. Prefer the simplest thing that works.
- Write plainly, without filler.
- Never use the middle dot (`·`) between words or values, in the app, the site or the docs. Use a comma, a colon or brackets instead (a semicolon in a list whose items have commas).
