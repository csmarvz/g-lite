# Device research

Expanded research gathered on 2026-11-08 is indexed in [research/README.md](research/README.md), with a [device map](research/device-map.md), exact upstream inventories, source links and [corrections to earlier assumptions](research/next-captures.md). The older findings below remain as historical notes; the expanded research distinguishes captured facts from implementation claims and unresolved leads.

Notes for adding more Logitech G devices (issues #3 and #11). Gathered on 2026-11-06 from Solaar dumps, the OpenRGB, Solaar and libratbag source code, and Logitech's manuals. Nothing here has been checked on a real G915 or G815 yet.

OpenRGB and Solaar are GPL, so G-Lite only takes facts from them, never code. libratbag is MIT.

## G915, G915 TKL and G815 compared with the G515

From Solaar dumps of each keyboard.

The same as the G515:

- 0x8071 RGB effects, 0x8081 per-key lighting and 0x8040 brightness. Live effects and brightness should work the same way.
- 0x8071 lists each keyboard's own effects, so the effect list can be read from the keyboard.

Different from the G515:

- Saving. The G515 saves lighting in onboard files (0x8101). The G915, G915 TKL and G815 have no 0x8101. They have onboard profiles (0x8100), like the G Pro Wireless, with sectors and a CRC.
- Idle timers. On the G515 they are in the onboard files. Where the G915 keeps them, if anywhere, is unknown.
- Battery. The G915s report a voltage (0x1001), not a percentage (0x1004). G-Lite already handles both. The G815 is wired.
- Extra features: G keys and macros (0x8010, 0x8020, 0x8030), report rate (0x8060), switching between computers (0x1814, 0x1815).

## Key layouts

None of these keyboards has a QMK, VIA or keyboard-layout-editor layout. Logitech's firmware is closed. The useful source is OpenRGB's `Controllers/LogitechController`, which maps each key to the LED id the keyboard uses.

### LED ids

- 0x8081 per-key (G815, G915, G515): a key's id is its USB HID usage minus 3, so A = 1 to Z = 26, 1 to 0 = 27 to 36, Esc = 38, Menu = 98.
  - Modifiers: usage 0xE0 to 0xE7 minus 0x78, so left Ctrl = 104 to right Win = 111. Fn shares 111.
  - ISO keys: the non-US `#` is 47, the extra key next to left Shift is 97.
  - Extras on the G915 family: Brightness 153, Play 155, Mute 156, Next 157, Previous 158, G1 to G9 = 180 to 188, Logo 210.
  - The ids of the extras change between boards. The PRO X RAPID moved Brightness, Play and Next to 150, 152 and 154.
  - Sources: `LogitechHIDPP20Controller/RGBController_LogitechHIDPP20.cpp`, `LogitechG915Controller/RGBController_LogitechG915.cpp`, `LogitechG815Controller/RGBController_LogitechG815.cpp`.
- 0x8080 per-key (G PRO keyboard, G910, G512 and G513, G810 and G610): a key is a key type and a key id.
  - Key types: 0x01 keyboard (the id is the HID usage), 0x02 consumer, 0x04 G keys, 0x10 logo, 0x40 indicators.
  - Examples: Logo 0x1001, G1 to G9 = 0x0401 to 0x0409, Game Mode 0x4002, Caps Lock indicator 0x4003.
- 0x4540 gives the layout: 1 US, 3 UK, 4 DE, 5 FR, 0x0A JP, and more. OpenRGB uses it, with whether there is a numpad, to choose ANSI or ISO and full size or TKL. The 0x8081 boards list ANSI and ISO keys together.

### Per keyboard

| Keyboard | IDs | Size | Per-key ids | Extras | Next to the G515 TKL |
|---|---|---|---|---|---|
| G915 TKL | 408E (receiver), C343 (wired) | TKL, 95 LEDs | Yes, 0x8081 | Top strip: Logo, Brightness, Game Mode, LIGHTSPEED and Bluetooth buttons. Previous, Play, Next, Mute and a volume roller at the top right. Only Logo, Brightness and the 4 media keys are lit. | Same main block. Needs one more top row. |
| G915 | 407C, C33E | Full size, 117 LEDs | Yes, 0x8081 | G1 to G5 on the left, Logo, Brightness, media keys and roller above the numpad, Game Mode, M1 to M3 and MR | Needs a numpad, a left column and a top strip |
| G815 | C33F | Full size | Yes, 0x8081 | Same LEDs as the G915, without the wireless buttons | Same layout as the G915 |
| G813 | C232 | Full size | Yes | The Asia-Pacific name for the G815 | Same layout as the G915 |
| G PRO keyboard | C339 | TKL | Yes, 0x8080 | A strip above the F keys: Logo, Caps and Scroll Lock indicators, Game Mode, Lighting. No media keys. | Close |
| G910 | C335 | Full size | Yes, 0x8080 | Logo left of Esc, G1 to G5 down the left, G6 to G9 above F1 to F4, a lit nameplate on the front edge. The M keys and media keys aren't RGB. | Needs its own layout |
| G513 | C33C | Full size | Yes, 0x8080 | Caps Lock and Game Mode indicators above the numpad | OpenRGB calls C33C "G512 RGB", libratbag calls it G513. Check on a real keyboard. |
| G213 | C336 | Full size | No, 0x8070 with 5 zones | Zones: left, middle, right, arrows and Home block, numpad | Needs a zone picture, not keys |
| G413 | C33A | Full size | No | One colour (red or white), brightness and breathing only | A plain picture is enough |
| G613 | 4065 | Full size | No | 6 G keys, no backlight | No lighting picture needed |

Manual with the G915 TKL layout: https://www.logitech.com/assets/65920/g915-g913-tkl-qsg.pdf

Best data first: G915 TKL, then G915, G815 and G813, then G PRO, G910, G513, G213, and last G413 and G613.

## Do other projects write to onboard memory?

### libratbag (MIT)

Yes. It writes 0x8100 onboard profiles, on mice and keyboards.

- `src/hidpp20.c`: `hidpp20_onboard_profiles_write_sector` sends fn 6, then fn 7 for each 16 bytes, then fn 8. `_write_profile` writes the profiles, `_write_dict` writes the directory to sector 0x0000, and `_commit` runs on every change.
- `hidpp20_onboard_profiles_validate` accepts profile format 0x04 as `PROFILE_TYPE_G915`. `data/devices/` has files for the G915, G915 TKL, G815, G513, G413, G910 and G PRO keyboard. Keyboards use the mouse profile layout (`union hidpp20_internal_profile`).
- Lighting: 0x8070 `set_zone_effect` sets byte 12 to 1 ("write to RAM and flash"), but only on devices without 0x8100. 0x8071 only goes through the onboard profile.
- Guards:
  - It checks the CRC-CCITT when reading, and falls back to the factory profile when a sector is bad.
  - It writes only sector 0x0000 and the profile sectors, never 0x0100 and up.
  - Missing: no read-back after writing, and no backup.
  - It rebuilds each sector from scratch, filled with 0xFF, so bytes it doesn't parse are lost.
- Reports: #1425 "ratbagd bricked my Logitech G700s" and #1563 (a G305 unusable after a bad report rate). In #1104, G915 owners edit G-key macros with it.

### Solaar (GPL-2)

Yes, but only from the command line, for any device with 0x8100.

- `lib/logitech_receiver/hidpp20.py`: `OnboardProfiles.write_sector` uses fn 6, 7 and 8. `OnboardProfiles.write` writes the directory, sector 0, then each profile. Used by `solaar profiles <device> <yaml>`, which loads a dump back into the device.
- It reads profile formats up to 5, which includes the G915. Keyboards get the mouse layout.
- Guards:
  - It skips a sector that already matches.
  - It refuses sectors at or above the device's sector count.
  - It checks the dump's version and device name.
  - It writes bytes it doesn't parse back as they were.
  - Missing: no read-back. `docs/capabilities.md` tells users to keep their first dump as a backup.
- Lighting:
  - 0x8071 fn 1 is sent with a trailing `01`, which looks like the persist flag.
  - 0x8070 fn 3 is sent without it.
  - The headset's onboard EQ is also saved to flash.
- Reports: no bricking reports found. #3266 is a G915 TKL F-key regression.

### OpenRGB (GPL-2.0 or later)

It never writes 0x8100 or 0x8101 memory, but it saves lighting to flash.

- For 0x8100 it only sets and gets the onboard mode. For 0x8101 it only reads. On the G515 it reads 99 bytes of the active profile, for its log.
- 0x8071 effects are always sent with persist on. The comment in `RGBController_LogitechHIDPP20.cpp` says that with persist off "the firmware appears to accept the command without actually committing".
- 0x8070 effects are temporary unless the user presses Save.
- `LogitechG915Controller.cpp` sets the persist byte on every mode change.
- `HIDPP20_QUIRK_KEEP_ONBOARD_MODE` keeps the G915 TKL in onboard mode, because host mode breaks its G keys on the F row.
- Reports: no bricking reports found. #5893 is a G515 TKL whose lighting turns off and can't be turned back on.

## What G-Lite takes from this

- No project knows a separate keyboard profile layout. libratbag and Solaar read format 4 (G915) with the mouse layout:
  - report rate at 0, DPI at 3
  - buttons and G keys at 0x20, the alternate buttons at 0x60
  - name at 0xA0
  - LED records of 11 bytes from 0xD0 (Solaar reads four of them)
  - CRC in the last two bytes
  
  This has to be checked against a real G915 dump, which "Report a device" collects.
- G-Lite's write rules are already stricter than both projects':
  - It changes only the bytes it edits in the sector it read.
  - It skips the write when nothing changed.
  - It reads back after fn 8.
  - It never writes 0x0000 or 0x0100 and up.
  - It checks the CRC and keeps backups.
- Live changes on an unsupported keyboard may not be "not saved". If the firmware ignores 0x8071 effects sent with persist off, as OpenRGB says, then live effects are also written to the effect storage. Test on a real G915 before promising "live only".

## To check on real hardware

- Whether 0x8071 effects sent with persist off show on a G915.
- The G915's profile format 4 layout, and where its idle settings live.
- Which model USB ID C33C really is.
