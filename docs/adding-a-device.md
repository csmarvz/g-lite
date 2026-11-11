# Adding a device or a feature

What works, learned while adding the G305, G502 LIGHTSPEED, G510 and G512 X. Follow it in order, and add to it when something new comes up.

## 1. Read before writing

- Get a report from the device first (Report on its screen or info page). It lists the features, and every read G-Lite knows.
- A feature G-Lite doesn't read yet: add its read-only functions to `Report.Reads` and a line in `Report.Section`, so every later report has them. Only functions known to only read.
- With the device plugged in here, a read-only report can be run from a copy of the exe, without the user: load `GLite.exe` with reflection in Windows PowerShell (not pwsh), `Devices.Scan()`, pick the device, `new Hidpp(path, index, shortPath)`, `Report.Build`. Pass arguments as `[object[]]` and the `Found` as `.psobject.BaseObject`. Don't run it while the user makes a report in the app: both read the same device at the same time.
- Text from a device can hold zero bytes (the G512 X's firmware name is "U1" and a 0x00). A zero byte hides everything after it in a Windows text box, so cut device strings at the first byte that isn't printable.
- Note the facts in `docs/` (the device's ids, its feature list, what each byte means) with the sources. Keep the report there too, like `docs/G512X.md`.
- Keyboards that share the features can still lay out their onboard files differently: the G512 X 75's profile is 111 bytes and one byte off from the G515's. Compare them byte by byte before allowing Save, and start with live lighting when they differ. A file's size is byte 9 of its directory entry.

## 2. Check what the user can see before building a screen

- A feature the device reports isn't always one that does something visible. The G502 LIGHTSPEED reports two light zones, but zone 0 lights nothing. Before building UI, send the change once (a one-off script, or a hidden build) and ask the user to watch the device.
- A feature can work on the device and still be wrong on Windows. High-resolution scrolling (0x2121 bit 1) made the G502's wheel about 8 times too fast. Try it with the user before adding a switch.
- Settings the device forgets when it turns off (like the wheel mode) need G-Lite to set them again: a timer on the device's screen that reads, and writes only when needed, catching timeouts so an asleep device logs nothing.
- To learn what an unknown event or bit means, run a listener script in the background that stops on a file (`stop.flag`) rather than a timer, and let the user press things in a known order, with a picture of the order. Button spy on the G502 was mapped this way in one go.
- Names the device doesn't give (which zone is which, which button is G7) need the user to look. Don't put a guess in the UI.

## 3. Build it behind the traces

- Make a fake from the real report's bytes (`FakeMouse.G305()` is the pattern), and add a trace in `app/Traces.cs` that drives the screen through the new feature.
- Existing traces must not change, except state lines the feature adds. Check with `diff` against `git show HEAD:app/tests/<device>.txt`, ignoring the new state, and say what changed.
- A device that works like one already supported is one `Support` entry. A new protocol is a driver (like `IMouse`) and an entry. A keyboard is a `Keyboards` entry (key map, preset prefix, saves or not) passed to `Page515`.
- A fake can often be an existing one with the report's lists: `FakeG915.G512X()` is the G915 fake with the G512 X's effect ids.

## 4. Build, then check it's really running

- When the traces change, `build.cmd` stops at the self-test and doesn't build `GLite.exe`. After `--record`, run `build.cmd` again, and check the exe's time in `%LOCALAPPDATA%\GLite` before telling the user it's running.
- Restart G-Lite with its window open so the user can try it.

## 5. Ship

- README: add the device to Supported devices, "Partial" with what's missing when it's not complete.
- CHANGELOG: an Unreleased entry. CLAUDE.md: what the next person needs to know.
