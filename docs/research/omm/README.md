# Onboard Memory Manager 2.7.4428, read from the binary

Read 2026-11-22 from `OnboardMemoryManager_2.7.4428.exe`, Logitech's own tool for mouse onboard profiles. No mouse was used and OMM wasn't run. Everything here is a fact about the protocol or about what OMM does, in our words; no Logitech code is copied.

## What OMM is made of

- A .NET Framework 4.8 WPF app. Its own code (the device list, read and save flow, UI) is IL.
- The HID++ work is in `logi_nethidppio.dll`, a C++/CLI library embedded as a resource (`OnboardMemoryManager.embeddeddlls.logi_nethidppio.dll`). Its managed layer (namespace `logi.hidppio`: `Feature8100`, `OnboardProfile`, `Feature8070`, `Feature8071`, ...) is IL. The packets, the CRC and the profile layouts are native x64 code.
- Embedded with it: MahApps.Metro, ControlzEx, the VC++ runtime and 20 `Resources_<language>.dll`.

## Files here

| File | What |
|---|---|
| `il-dump.ps1` | Dumps an assembly's IL method by method and saves its embedded resources. Uses only the metadata reader in .NET (PowerShell 7); loads nothing from the file. Run it on the exe, then on the extracted `logi_nethidppio.dll`. |
| `layouts.py` | Reads the native code of `logi_nethidppio.dll` and prints every profile format's layout: each component's offset, size, id and constructor arguments. The docstring says how. |
| `layouts.txt` | Its output for 2.7.4428. |

The IL dumps themselves (about 200,000 lines) and the extracted DLLs aren't kept: they are Logitech's code, which an MIT repo can't redistribute. The two scripts regenerate them from the exe in a few seconds:

```
pwsh -NoProfile -File il-dump.ps1 OnboardMemoryManager_2.7.4428.exe out\omm
pwsh -NoProfile -File il-dump.ps1 out\omm\res_OnboardMemoryManager.embeddeddlls.logi_nethidppio.dll out\hidppio
python -I layouts.py out\omm\res_OnboardMemoryManager.embeddeddlls.logi_nethidppio.dll
```

## Profile layouts

The library builds a profile out of components, each at a fixed offset, chosen by the format byte of the 0x8100 fn 0 reply (byte 1). `layouts.txt` has the full list; in short:

| Bytes | 1 | 2, 3 | 4 | 5 | 6 | 7 | 8 | 10 |
|---|---|---|---|---|---|---|---|---|
| 0x00 report interval in ms | ✓ | ✓ | ✓ | ✓ | | | | ✓ |
| 0x01 default stage, 0x02 shift stage, 0x03 five LE16 stages | ✓ | ✓ | ✓ | ✓ | | | | ✓ |
| 0x0D colour (3), 0x10 power mode, 0x11 angle snapping | ✓ | ✓ | ✓ | ✓ | | | | ✓ |
| 0x12 write counter, 0x1C power-save timeout, 0x1E power-off timeout (2 bytes each, big endian) | | | ✓ | ✓ | | | | ✓ |
| 0x14 rate index over the receiver, 0x15 on the cable, 0x16 DPI delta (4) | | | | | | | | ✓ |
| 0x20 buttons, 0x60 G-shift layer, 16 × 4 bytes | ✓ | ✓ | ✓ | ✓ | | | | 13 buttons (0x20–0x53), 16 G-shift |
| 0x56 five LE16 Y values | | | | | | | | ✓ |
| 0xA0 name, 48 bytes | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |
| 0xD0 logo, 0xDB side: 0x8070 records of 11 bytes | | ✓ | ✓ | | | | | |
| 0xE6 logo, 0xF1 side: power-save copies | | | ✓ | | | | | |
| 0xD0, 0xDB, 0xE6, 0xF1: 0x8071 cluster 0 active, cluster 1 active, cluster 0 passive, cluster 1 passive; 0xFC lighting flags | | | | ✓ | ✓ | ✓ | ✓ | ✓ |

Formats 6, 7 and 8:

| Offset | Content |
|---|---|
| 0x00 | Report rate over the receiver, an index (0 to 6: 125, 250, 500, 1000, 2000, 4000, 8000 Hz) |
| 0x01 | Report rate on the cable, the same index |
| 0x02 | Default stage, 0x03 shift stage, 0x04 five stages of 5 bytes: X LE16, Y LE16, lift-off |
| 0x1D | DPI delta, 4 bytes (the library prints it as "Delta X" and "Delta Y") |
| 0x21 | Power mode, 0x22 angle snapping |
| 0x23 | Write counter (BE16) |
| 0x25 | Bunny hopping, formats 7 and 8: units of 10 ms, 0 or FF off |
| 0x26 | Analog buttons, format 8: 6 bytes |
| 0x2C, 0x2E | Power-save and power-off timeouts in seconds (BE16) |
| 0x30, 0x70 | 12 buttons, 12 G-shift buttons, 4 bytes each |
| 0xA0 | Name |
| 0xD0–0xFC | 0x8071 slots and flags as in the table above |

- The constructor arguments fit the stage tables: `dpi_v1` (default +0, shift +1, table +2, 5 stages) and `dpi_v6` (the same, 5-byte entries with X at +0, Y at +2, lift-off at +4).
- Format 9 doesn't exist: the dispatcher throws "Profile format not supported", as for anything above 10.
- The library's names for the formats: 1 PROTOSS_HYJAL, 2 LOGAN, 3 HEAT, 4 HARPY, 5 HOST_LAYER, 6 BAYMAX, 7 BUNNY_HOPPING, 8 ANALOG_BUTTONS, 10 ARTANIS_POST_DFU.
- The write counter and the timeouts derive from a `big_endian_two_bytes_component` class (RTTI). lomm reads the timeouts as LE16; OMM's class says big endian.
- The CRC, the read and write chunk sizes and the directory's byte layout are native code that `layouts.py` doesn't decode.

### How this compares with the other sources

- It agrees with lomm-web, GOMM and onboard-lite ([more-references.md](../more-references.md#profile-format-7)), which also took OMM's layouts. This is the first read of the builders themselves.
- Byte 0 of formats 6 to 8 is the receiver's rate, byte 1 the cable's (component ids 20 and 21), as Featherweight found and G-Lite uses. lomm has them swapped.
- Format 4 has 0x8070 records at 0xD0 and 0xDB and power-save copies at 0xE6 and 0xF1, as onboard-lite writes, but no flags byte at 0xFC.

## fn 0 reply, as the library names it

In order: memory model (1), profile format, macro format (1), profile count, factory (ROM) profile count, button count, sector count, sector size (16 bits), mechanical layout (G-shift, DPI shift and the number of M keys, each 0 unknown, 1 no, 2 yes), connection (1 corded, 2 cordless, 4 both), a flag "device does not clear sectors when firmware update performed", and a sector count rule (0 legacy, 1 restricted). The bit packing of the last bytes is native.

## What OMM does with a mouse

### Which devices

- No model list. A device qualifies if it is a mouse (device type 2) on HID++ 2.0 or later and has 0x0003, 0x0005, 0x2201 or 0x2202, and 0x8100.
- The model id comes from 0x0003. A table of model ids gives display names (G402, G302, G303, G900, G403, G102/G203, Pro, G903, G703, G502 HERO, G502 Wireless, MX518, G603, G305, Pro Wireless, G604, G705, G309, PRO X SUPERLIGHT 2, ...); otherwise 0x0005's name is used.
- Lighting is turned off for the G603, G305 and G604 model ids.
- Connection type: transport 2 or 3 is Bluetooth; USB without a parent is the cable; with a parent, LIGHTSPEED.
- Receivers: a static list of 27 PIDs is excluded (the values sit in a data blob the IL dump doesn't show). C53A is named the Powerplay receiver.

### On connect

1. fn 0 (profiles info), then the directory. A directory that fails its CRC reads as empty; if nothing is enabled, entry 0 counts as enabled.
2. Each listed profile. One that fails its CRC is replaced by factory profile 0x0100 + n (0x0101 if n is above the factory count), or by a default the library builds when there are no factory profiles. A profile the library can't parse marks the format unsupported.
3. DPI ranges (0x2201 fn 1, or 0x2202 lists and capabilities), report rates (0x8061 for each link and the actual list), battery, current DPI, onboard mode and active profile. The onboard mode is polled every 3 s.
4. It listens for 0x8100's profile-changed and stage-changed events.

Format 10 profiles are migrated on load and written back at once: if byte 0 isn't FF, the ms interval (1, 2, 4, 8) becomes index 3, 2, 1, 0 in both 0x14 and 0x15, byte 0 is set to FF, and any Y of FFFF becomes X with lift-off 2. So just opening a G502 X with that firmware in OMM writes to it.

### Save

1. Each changed profile goes to its sector (fn 6, 7, 8 inside the library). An error shows a message and the next profile still goes.
2. The directory, only if it changed. Checks: no entry at sector 0, none at or past the sector count, no sector twice, between 1 and `profile_count` entries, at least one enabled (entry 0 is enabled when none is).
3. If the mouse is in onboard mode and the profile is the active one: fn 3 with its sector to reload it, then fn 0x0C to put back the stage that was in use.

OMM never changes the onboard mode (its host-mode methods have no callers). It never writes the write counter, the timeouts, angle snapping or the name: those keep what was read.

### Restore defaults

Shown only when the factory profile count is above 0. It reads factory profile 0x0100 + n (0x0101 when n is above the count), migrates it if it is format 10, writes it to the user sector and rewrites the directory. No fn 3 afterwards. So the factory profiles share the user profiles' layout, format 7 included.

### Directory

OMM pads its list to `profile_count` with placeholder entries. A new profile takes the lowest unused sector from 1 to `profile_count`; trailing placeholders are never written. Turning a profile on or off writes the directory at once; picking one activates it at once.

## Settings

- **DPI.** A stage is valid when it falls in one of the sensor's ranges and is a multiple of the step; OMM rounds to the nearest valid value. Stage 0 is off. X and Y separately, and lift-off, only when 0x2202 reports them. Defaults: 800 DPI, Y = X, lift-off 2. The default lift-off comes from the factory profile.
- **Report rate.** Without 0x8061: byte 0 in ms, offering 125, 250, 500, 1000 Hz (a stored 0 means 125 only). With 0x8061: the two index bytes, offering only what each link's capability mask allows. Hidden over Bluetooth.
- **Lighting.** 0x8071 first, then 0x8070 unless it is hidden (flag 0x40) or has no zones. Effects shown: 0 off, 1 fixed, 3 cycle and 10 breathing on both; 4 colour wave, 15, 16, 21, 22 and 24 on 0x8071 only. A change writes the active slots and their power-save or passive copies (period 5000 ms, intensity 100 active and 50 passive; fixed uses colour animation 2 and half the RGB for power save; default colour 00 A9 E0). The flags byte at 0xFC is 3 for an effect that spans both clusters, else 0.
- **Bunny hopping** (0x80E0): byte × 10 ms, 100 to 1000 ms in the UI, default 100.
- **Analog buttons** (0x1B0C): per button an actuation point, rapid-trigger sensitivity and on/off, and a haptic level. OMM turns on 0x1B0C's monitoring mode every 54 s to show the key travel live. Its share code ("HITS code") is Base64 of a digit string: `00`, then for each button 2 digits actuation, 2 sensitivity, 1 on/off, 2 haptics.
- **Battery.** 0x1004, else 0x1001 (voltage turned into a percentage by one of two curves, chosen by model), else 0x1000.

## Button records

4 bytes, as in [more-references.md](../more-references.md#0x8100-onboard-profiles):

- `80 01 <bits BE16>` mouse button (1 left, 2 right, 4 middle, 8 back, 0x10 forward); `80 02 <modifiers> <key>`; `80 03 <usage BE16>` consumer key.
- `90 <function>`: 1 and 2 tilt left and right, 3 and 4 next and previous DPI, 5 cycle DPI, 6 default DPI, 7 DPI shift, 8 and 9 next and previous profile, 0x0A cycle profiles, 0x0B G-shift, 0x0C battery, 0x0D profile n, 0x0E performance mode, 0x0F host button, 0x10 and 0x11 scroll down and up.
- `00` macro (OMM can't edit one), `10` stop macro, `20` stop all.
- OMM doesn't let buttons 0 and 1 change. A button set to G-shift locks its own G-shift slot to G-shift. The profile functions are hidden on a mouse with one profile.

## Effect parameters

Byte positions in the 0x8070 and 0x8071 effect parameters, the same for both features. 16-bit values are high byte first.

| Id | Effect | Parameters |
|---|---|---|
| 1 | Fixed | RGB 0–2, colour animation 3 (0 default, 1 ramp, 2 none) |
| 2 | Breathing | RGB 0–2, speed 3 |
| 3 | Cycle | period 5–6, intensity 7 |
| 4 | Colour wave | start RGB 0–2, end RGB 3–5, period 6 (low) and 9 (high), direction 7, intensity 8 |
| 5 | Starlight | sky RGB 0–2, star RGB 3–5 |
| 6 | Light on press | press RGB 0–2, release RGB 3–5, delay 6–7 |
| 10 | Breathing (waveform) | RGB 0–2, period 3–4, waveform 5 (1 sine, 2 square, 3 triangle, 4 sawtooth, 5 shark fin, 6 exponential), intensity 6 |
| 13 | KITT | colour 0–2, overlap % 3, period 4–5, background 6–8, brightness 9 |
| 14 | Colour decomposition | period 6–7, brightness 8 |
| 15, 16 | SnipePulse, NeuralWave (cyan and pink) | period 5–6, intensity 7 |
| 17, 18 | The same, with colours | background 0–2, colour 3–5, period 6–7, intensity 8 |
| 20 | HSV breathing | hue 0, saturation 1, value 2, period 6–7 |
| 21 | Colour cycle (configurable) | saturation 1, period 6–7, intensity 8 |
| 22 | Colour wave (configurable) | saturation 1, period 6–7, intensity 8, direction 9 |
| 23 | Ripple (configurable) | RGB 0–2, saturation 3, skip start-up 4, period 6–7 |
| 24, 25 | Smooth star breathing, smooth wave | colour 1 0–2, colour 2 3–5, period 6–7, intensity 8 |

Ids 7 (audio), 8 (boot), 9 (demo), 11 (ripple), 12 (stored custom) and 19 (host streaming) have no parameters in the library.

## For G-Lite

- Formats 6 and 8 have format 7's layout everywhere `MouseProtocol.BuildWide` writes (bytes 0, 1, 2, the stages at 4 and the buttons from 0x30); format 8 only adds bytes 0x25–0x2B. Making them writable is a matter of trusting this table, and a report.
- Factory restore on format 7: OMM copies 0x0100 + n into the user sector, so the factory profiles have the same layout. Featherweight saw no valid CRC on them, so a copy would need a new CRC.
- Format 4's LED records are now known from two sources (OMM and onboard-lite).
- Format 10 (the G502 X family after a firmware update) is format 1's layout with the rate indexes at 0x14 and 0x15, 13 buttons and a Y table at 0x56, and byte 0 FF once migrated. G-Lite treats it as unknown today.

## Still unknown

The CRC and chunk sizes as OMM sends them (G-Lite's own are tested), the directory's enabled byte as OMM writes it ([more-references.md](../more-references.md#0x8100-onboard-profiles) has 00 on the SUPERLIGHT and FF on the G502 X LIGHTSPEED), the 27 excluded receiver PIDs, and which models use Centurion (the library has CentppFeature 0x0000, 0x0003, 0x0101, 0x0104, 0x0600, 0x0620 and 0x0635, but no device names).
