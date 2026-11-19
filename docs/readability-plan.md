# Readability plan

Goal: a codebase that people and agents can read, search and change without first decoding it. Names say what a thing is, lines fit on a screen, magic offsets have names, and each file holds one subject.

The work is split into steps. Each step changes code names only, never behaviour.

## Rules for every step

- Nothing changes on the wire or on disk. Settings keys (`seen.`, `battery.`, `lighting.`…), log text, preset text and backup files stay byte for byte the same.
- C# 5 only (see `CLAUDE.md`). No new dependencies.
- One category per step. After each step:
  1. Search for the old name (`Grep -w`) and check nothing is left: code, `CLAUDE.md`, `docs/`, `CONTRIBUTING.md`.
  2. Compile `build\selftest.exe` and run it (the first two commands of `app\build.cmd`; the rest of the script kills the running G-Lite and replaces the installed one, which a rename step doesn't need). Run the full `app\build.cmd` once at the end of each numbered step, so the app itself compiles too. The traces in `app/tests` record bytes and screen state, not code names, so they must pass **without** `--record`. If one differs, the step changed behaviour: find out why before going on.
  3. Read the diff once for wrong replacements: a name inside a string, a WinForms member with the same name (`Control.Text`, `Form.Shown`…), two classes that shared a member name.
  4. Tick the step here.
- Rename by symbol, not by plain text: check each hit. Short names (`F`, `C`, `Do`, `Say`) also match unrelated words.
- `CLAUDE.md` names many classes and methods: update it in the same step.

## Step 1: rename

### 1a. Classes

UI controls (`app/Ui.cs`):

| Now | New |
|---|---|
| `Ctl` | `DrawnControl` |
| `Btn` | `FlatButton` |
| `Seg` | `SegmentedPicker` |
| `Drop` | `Dropdown` |
| `DevItem` | `DeviceListItem` |
| `SubLine` | `DeviceSubtitle` |
| `Box` | `RoundedBox` |
| `Field` | `TextField` |
| `Brand` | `TitleLogo` |
| `MenuLook` | `MenuRenderer` |
| `MenuColors` | `MenuColorTable` |
| `Look` (enum) | `ButtonStyle` |

Device protocols:

| Now | New |
|---|---|
| `GMouse` | `MouseProtocol` |
| `GHeadset` | `HeadsetProtocol` |
| `GSpeaker` | `ZoneLighting` |
| `G515` | `G515Profile` |
| `G915` | `G915Lighting` |
| `G9x` | `G9xRegisters` |
| `G510` | `G510Lighting` |
| `Litra` | `LitraProtocol` |
| `HeadSet` | `HeadsetSettings` |
| `Mic` | `MicrophoneCapture` |

Others:

| Now | New |
|---|---|
| `Found` | `FoundDevice` |
| `Probe` | `ScanProbe` |
| `Watch` | `DeviceWatcher` |
| `Other` | `OtherDevice` |
| `OtherSet` | `OtherSettings` |
| `PageOther` | `PageOtherDevice` |
| `Support` | `DeviceTable` |
| `Refused` | `RefusedException` |
| `HalfWritten` | `HalfWrittenException` |
| `EffectDef` | `EffectInfo` |
| `Model` | `KeyLayout` |
| `Keyboards` | `KeyboardKinds` |
| `Win` | `WindowHelpers` |
| `Devices.Port` | `HidPort` |
| `PageOther.Part` | `SettingBox` |

Kept: `Hidpp`, `Ui`, `Theme`, the `Page*` and `Fake*` classes, model names (`G9xModel`, `G5Mouse`…). The nested `State` (in `MouseProtocol`, `HeadsetProtocol`, `OtherDevice`) and `G515Profile.Files` stay: qualified, they already read well, and `MouseProtocol.MouseState` would repeat itself. Members that share an old class name keep it (`G9xModel.G9x`, `KeyboardKinds.G515`, `DeviceTable.Other`, the `Watch()` and `Refused()` methods, the trace methods `G515()`…).

### 1b. Methods

Done. "Where" uses the new class names.

| Old | Where | New |
|---|---|---|
| `F(id)` | `Hidpp` | `FeatureIndex(id)` |
| `F(float)` | `ScreenSetup` | `FormatNumber(float)` |
| `C(int)` | `Theme` | `FromHex(int)` (`Rgb` was taken) |
| `Px(...)` | `WindowHelpers` | `PixelFont(...)` |
| `Fn(...)` | `Log` | `FirstWord(...)` |
| `Esc(...)` | `LogsPage` | `EscapeRtf(...)` |
| `U16(...)` | `G515Profile`, `MouseProtocol`, `OtherDevice` | `ReadUInt16BigEndian(...)` |
| `Def(id)` | `G515Profile`, `MouseProtocol`, `ZoneLighting` | `EffectById(id)` |
| `Do(...)` | `DevicePage` | `RunOnDevice(...)` (the traces' `Script.Do` stays) |
| `Did(...)` | `DevicePage` | `LogUserAction(...)` |
| `Say(...)` | `DevicePage` and pages | `ShowStatus(...)` |
| `Post(...)` | `DevicePage` | `ShowStatusLater(...)` |
| `Calm()` | `DevicePage` and pages | `ClearStatus()` |
| `Use(text)` | `DevicePage` and pages | `ApplyPreset(text)` |
| `Sync()` | pages, `ColorRow`, `KeyboardView` | `RefreshControls()` |
| `Who` | `Hidpp`, `DevicePage`, `MainWindow` | `DeviceLabel` (`Log.Line.Who`, the log's column, stays) |
| `Fire(...)` | `Hidpp` | `SendWithoutReply(...)` |
| `Quiet(...)` / `QuietFeature(...)` | `Hidpp` | `CallUnlogged(...)` / `SetFeatureUnlogged(...)` |
| `Ask(...)` | `Report.Reader` | `ReadOnlyCall(...)` |
| `Seal(p)` | `MouseProtocol` | `WriteChecksum(p)` |
| `Good(f)` / `Check(f)` / `Put(...)` | `G515Profile` | `FilesAreValid(f)` / `CheckFiles(f)` / `WriteFiles(...)` |
| `Put(v)` | `Slider` | `SetValue(v)` |
| `Put(text)` | `ReportView` | `CopyToClipboard(text)` |
| `Of(files)` | `Backups` | `FromFiles(files)` |
| `Fold(lines)` | `Log` | `MergeRepeatedSends(lines)` |
| `Lit(...)` | `KeyboardView` | `EffectColorAt(...)` |
| `Lit(zone)` | `PageSpeaker` | `LightOfZone(zone)` |
| `Shown(...)` (static, a colour) | `ZoneLighting` | `ColorAt(...)` |
| `One(text)` | `ZoneLighting` | `ParseLight(text)` |
| `Old(...)` | `Devices` | `ProbeHidpp10(...)` |
| `Plain(...)` | `Devices` | `FindPlainHid(...)` |
| `Plain(...)` | `Report` | `PlainHidReport(...)` |
| `Caps(...)` | `Devices` | `ReadHidCaps(...)` |
| `Front(h)` | `LitraProtocol` | `FrontLightFeature(h)` |
| `Raw(dpi)` | `G9xModel`, `G5Mouse` | `DpiToRaw(dpi)` |
| `Near(values, v)` | `Page515`, `PageMouse` | `Nearest(values, v)` |
| `Near(k)` | `Mines` | `MinesAround(k)` |
| `Jump(v)` | `DrawnControl` | `SetValueWithoutAnimation(v)` |
| `Ring(...)` | `DrawnControl` | `DrawFocusRing(...)` |
| `Shade(label, on)` | `DevicePage` | `SetDimmed(label, on)` |
| `Shade(...)` | `DeviceList` | `DrawEdgeFade(...)` |
| `Moved()` / `Gone()` | `TrayTip` | `PointerMoved()` / `Dismiss()` |
| `Head(...)` | `Report` | `WriteHeader(...)` |
| `Hint(...)` | `PageOtherDevice` | `AddHint(...)` |
| `Keep()` | `MicrophoneCapture` | `RequeueBuffers()` |
| `Limit(...)` | `OtherDevice` | `KeepSupported(...)` |
| `Aware()` | `WindowHelpers` | `EnableDpiAwareness()` |
| `Make(n)` | `Logo` | `MakeIcon(n)` |
| `Make(f, type)` | `MainWindow` | `MakePage(f, type)` (the `DeviceTable.Make` field stays) |
| `Wide()` | `Traces` | `TraceWideDpiMouse()` |
| `Fits(found)` | `OtherDevice` | `Supports(found)` (`Blocks.Fits` stays) |
| `Named(...)` | `KeyboardKinds` | `LoadLayout(...)` |
| `Newer(a, b)` | `Updater` | `IsNewerVersion(a, b)` |
| `Clean(...)` | `Log` | `OneLine(...)` |
| `Clean(text)` / `Clean()` | `Traces` | `NormalizeForTrace(text)` / `DeleteTestFiles()` |

Kept: `Same` (several classes; it reads well), `Scan`, `Load`, `Save`, `Read`, `Write`, `Send`, `Call`, `Crc32`, `Hsv`, `Iso`, the fake builders named after models (`G305()`, `Hero()`…).

### 1c. Fields and properties

Done.

| Old | Where | New |
|---|---|---|
| `Wpid` | `FoundDevice` | `WirelessId` |
| `Approx` | `FoundDevice` | `BatteryFromVoltage` |
| `Coarse` | `FoundDevice` | `BatteryGoodOrLowOnly` |
| `Short` | `FoundDevice` | `ShortName` (`FakeG9x.Short()` stays) |
| `Slug` | `FoundDevice` | `SettingsKey` |
| `Vendor` | `FoundDevice` | `HasVendorCollection` |
| `Sub` | `FoundDevice`, `DeviceListItem` | `Subtitle` |
| `Long`, `Short` | `HidPort` | `LongPath`, `ShortPath` |
| `Idx`, `fs`, `rbuf` | `Hidpp` | `DeviceIndex`, `longStream`, `longBuffer` |
| `Swid`, `lng`, `sht`, `lbuf`, `sbuf` | `ScanProbe` | `SoftwareId`, `longStream`, `shortStream`, `longBuffer`, `shortBuffer` |
| `MinP`, `MaxP`, `DefP` | `EffectInfo` | `MinPeriod`, `MaxPeriod`, `DefaultPeriod` |
| `Bg`, `Side`, `Btn`, `BtnHover`, `BtnDown`, `AccentBg`, `Bed` | `Theme` | `Background`, `Sidebar`, `Button`, `ButtonHover`, `ButtonPressed`, `AccentBackground`, `PictureBackground` |
| `Semi` | `Ui` | `Semibold` |
| `Look` | `FlatButton` | `Style` |
| `Off` | `HeadsetSettings`, `FakeHeadset` | `AutoOffMinutes` |
| `H`, `S` | `Report.Reader` | `Device`, `Output` |
| `W`, `H`, `px`, `py` | `Game`, `Blocks` | `Columns`, `Rows`, `pieceX`, `pieceY` |
| `W`, `H`, `SideW`, `SideIn` | `MainWindow` | `WindowWidth`, `WindowHeight`, `SidebarWidth`, `SidebarInnerWidth` |
| `Fx` | `PageMouse` | `Effects` |
| `Mark`, `Marks` | `Zone`, `ScreenSetup` | `MarkColor`, `MarkColors` |
| `Check`, `Raw` | `Updater` | `CheckForUpdates`, `RawUrl` |
| `U`, `Head`, `bw`, `bh` | `KeyboardView` | `KeyUnit`, `HeaderHeight`, `boardWidth`, `boardHeight` |
| `uw, uh, ox, oy, unit` | `KeyPicture` | `widthInKeys, heightInKeys, offsetX, offsetY, keySize` |
| `prm, barPrm`, `rnd` | `KeyPicture` | `parameters, barParameters`, `random` |

Renamed afterwards: `K` (the UI scale, in `Ui` and `DrawnControl`) to `DpiScale`.

Kept: `Latency.Ms`, the P/Invoke struct fields (`Caps.Mid`, `Pid`…, named after the Win32 ones). Short private fields not listed here are left for 1d with the locals.

### 1d. Parameters and locals (done)

Every file, method by method: `r, s, p, b, l, g, d, o, h, pr, pp, feat`… became full names. The common ones:

| Was | Now |
|---|---|
| `h` (a `Hidpp`) | `hidpp` (`G9xRegisters` and `Report` already used `device`, kept) |
| `feat` (the byte the device uses for a feature) | `featureIndex`; a local holding one feature's index is named after it (`dpiFeature`, `rateFeature`…) or `feature` |
| `p`, `r` (call bytes) | `parameters`, `reply` |
| `f` (a `FoundDevice`) | `found` or `device`; a `G515Profile.Files` is `files` |
| `g` | `graphics` |
| `l` (a lighting) | `lighting`, `light` or `edited` (the one an edit changes) |
| `b` (a brightness snapshot on the G515 screen) | `level` |
| `n = ++seq` (a page's sequence at a send or load) | `startedAt` |
| `cur`, `seq` (page fields) | `current`, `sequence` |
| `xxxBtn`, `xxxSeg`, `xxxDrop`, `dirs`, `animTimer` | `xxxButton`, `xxxPicker`, `xxxDropdown`, `directions`, `animationTimer` |

Kept: `i`, `j` as loop counters; `x`, `y`, `dx`, `dy` as coordinates; `(s, e)` in event handlers and `e` in `OnPaint(PaintEventArgs e)` and the like; `ex` in `catch`; `fn` for the HID++ function number (the docs' term); `id`, `ok`, `hz`, `on`, `to`, `at`, which are words.

A local or parameter can hide a field of the same name without a compiler error (unlike a local in an enclosing scope, which C# 5 refuses). Three renames did that and were caught and fixed: `ReportView.Read` (a lambda parameter hid the `text` field; it's `report`), `ColorRow.RefreshControls` (`color = color;`; the parameter is `newColor`) and `MainWindow.MakePage` (the demo's fallback `fake` field; the local is `demoDevice`). The check that found them compares each member's names before and after: a name a member gained that it already used is listed for review.

## Step 2: break up long lines

About 480 lines were over 150 characters (864 over 140 after step 1's longer names). Target: none over 140, except golden byte strings in `SelfTest.cs` and `Fake.cs`, where one value per line is easier to compare.

2a, line breaks (done): whitespace only, checked file by file by comparing tokens with the commit before, whitespace ignored (a comment counts as its words, so it can be rewrapped). 18 lines are left over 140, each a string that stays whole: golden byte strings, and long UI texts such as the info page's. The exe came out the same size. 2b, rewrites (done): brackets around the `&&` group in the 13 statements that mixed `&&` and `||` (C# already binds `&&` tighter, so only the reading changes); `if` chains for the two ternaries that were nested in a condition (`Page515.Unsaved`, the backlight mode in `OtherDevice`); a named local for the four long `foreach` sequences, and `SelfTest.LayoutFiles` for the list two loops shared. The 67 flat ternary chains (`a ? x : b ? y : z`) stay: after 2a each has one condition per line, which reads like an `else if` list, and none nests inside a branch.

- LINQ chains: one call per line.
- Nested ternaries: an `if` chain or a small method.
- Object initializers (`new Found { … }`): one property per line, or a few per line grouped by meaning.
- Long `foreach` headers (`Devices.Plain`): pull the sequence into a named local first.
- Mixed `&&` and `||` (`GMouse.Unwritten`): add brackets.

## Step 3: name the profile offsets

Replace raw offsets with constants next to the code that reads them, with the source in a comment where `CLAUDE.md` or `docs/` has one:

- Mouse profile (`GMouse`): report interval 0x00, default stage 0x01, stages 0x03, name 0xA0 (0x30 bytes), LED records 0xD0 of 11 bytes, CRC in the last two bytes, button records from 0x20 of 4 bytes, G-shift from 0x60.
- G915 profile (format 4): lighting records 0xD0, idle times 0x1C.
- G515 onboard files: whatever offsets `G515` encodes.
- HID caps (`Devices.Caps`): `UsagePage = caps[1]`, `OutputReportLength = caps[3]`, or a `HIDP_CAPS` struct.

## Step 4: split big files

One subject per file. Keep the current file names where they still fit.

- `Ui.cs` (23 classes): `Theme.cs`, `Ui.cs` (drawing helpers, `Icons`), `Controls.cs` (`DrawnControl`, `RoundedBox`, `FlatButton`, `SegmentedPicker`, `Swatch`, `Toggle`, `Dropdown`, `TextField`, `Slider`, `StatusLine`), `DeviceList.cs` (`DeviceList`, `DeviceListItem`, `DeviceSubtitle`, `TitleLogo`), `Tray.cs` (`TrayTip`, `MenuRow`, `MenuRenderer`, `MenuColorTable`).
- `Page515.cs`: `KeyPicture` to `KeyPicture.cs`, `KeyboardKinds` to `KeyboardKinds.cs`.
- `Fake.cs`: `KeyLayout` to `KeyLayout.cs`, `KeyboardView` to `KeyboardView.cs`; the fakes stay together.
- `Mouse.cs`: `MouseProtocol`, the `G9x` family and `Bindings`/`ButtonSpy` in separate files.
- `Keyboard.cs`: `Hidpp` to `Hidpp.cs`.
- `Pages.cs`: `ReportView` and `LogsPage` to their own files.
- `Program.cs`: `DeviceTable` to `DeviceTable.cs`.

`app\build.cmd` lists the source files by name, twice (the self-test and the app; the app leaves out `SelfTest.cs` and `Traces.cs`): add each new file to both lists, or replace them with a list kept in one place. Update the file list in `CLAUDE.md`.

## Progress

- [x] 1a. Classes
- [x] 1b. Methods
- [x] 1c. Fields and properties
- [x] 1d. Parameters and locals (file by file)
- [x] 2a. Long lines: line breaks
- [x] 2b. Long lines: rewrites (ternaries, brackets, named locals)
- [ ] 3. Profile offsets
- [ ] 4. Split files
