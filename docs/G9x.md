# G9x support

How to add the G9x Laser Mouse, and with it other HID++ 1.0 mice. Facts come from libratbag (MIT, cloned at `C:\Users\csmar\dev\libratbag`, commit `8235b5b`): `src/hidpp10.c`, `src/hidpp10.h`, `data/devices/logitech-g9x-Original.device`. Each one marked "checked" was read from a real G9x (USB 046D:C066, firmware 58.02 build 0018), with reads only.

Status: live support is built. G-Lite shows the G9x on the mouse screen: colour and brightness (or off), DPI, the stages of the active profile, and the report rate, all set live through registers. Save to mouse writes them into the active profile page, after a backup. The report lists the registers and the profile pages. See also [lgs.md](lgs.md) for what LGS knows about the G9x.

## The device

- USB 046D:C066, wired. HID++ 1.0 on interface MI_01: short reports (7 bytes) on COL03, long (20 bytes) on COL04, both usage page 0xFF00.
- Device index 0x00. Index 0xFF answers error 08 (unknown device). Checked.
- No HID++ 2.0: the root ping (`10 00 00 1D ...`) answers error 01. Checked.
- No name register. The name comes from the USB product string, "G9x Laser Mouse".
- libratbag: DPI 200 to 5700 in steps of 50, five profiles, one LED, profile type G500. The DPI part looks wrong; see DPI.

## HID++ 1.0 messages

| Sub-id | Request | Reply |
|---|---|---|
| 0x80 | Set short register: `10 <idx> 80 <reg> p0 p1 p2` | `10 <idx> 80 <reg> ...` |
| 0x81 | Get short register: `10 <idx> 81 <reg> p0 p1 p2` | `10 <idx> 81 <reg> 3 bytes` |
| 0x82 | Set long register: `11 <idx> 82 <reg> 16 bytes` | short `10 <idx> 82 <reg> ...` |
| 0x83 | Get long register: `10 <idx> 83 <reg> p0 p1 p2` | long `11 <idx> 83 <reg> 16 bytes` |
| 0x8F | Error: `10 <idx> 8F <sub> <reg> <code> 00` | |

Error codes: 01 invalid sub-id, 02 invalid address, 03 invalid value, 07 busy, 08 unknown device, 0A unavailable, 0B invalid parameter. Errors and short replies come on the short collection, long replies on the long one. Read both, with one pending read per collection (a new read per call loses replies).

## Registers

| Register | Get | Set | Checked value |
|---|---|---|---|
| short 0x0F | Current profile: `00 <index>` (index type), `01 <page> <offset>` (address type), `FF` (factory) | Same bytes | `00 00 00`, profile 0 |
| short 0x51 | LED state: one nibble per LED, low nibble first. 0 no change, 1 off, 2 on, 3 blink, 4 heartbeat, 5 slow on, 6 slow off. On the G9x these are the four DPI LEDs, and they show the current stage's LED pattern from the profile | Same | `22 12 00` on the 800 stage, whose pattern is `22 12` |
| short 0x54 | LED brightness: one nibble per LED, in 10% steps, 0 = no change | Same | error 02, not on the G9x |
| short 0x57 | LED colour: R G B | Same. Changes the colour only; 0x51 turns it on and off | `FF 00 00`, red |
| short 0x61 | Sensor: byte 0 surface reflectivity, the rest unknown | | changes as the mouse moves |
| long 0x63 | Current DPI: X at 0–1, Y at 2–3, little endian, raw units. Bytes 4–5 unknown (`80 01`). The rest of the reply is whatever the mouse sent last (the end of the last page read), not part of the register | long set: bytes 0–5, zeros after | `88 00 88 00 80 01` |
| short 0x64 | Report interval in ms | Same | `02`, 500 Hz |
| short 0xF1 | Firmware: param 01 gives major and minor, 02 the build | | `58 02`, `00 18` |
| long 0xA2 | Read flash: params `<page> <offset / 2>`, 16 bytes | | see Profiles |
| short 0x01, 0x78, 0xD0, 0xD2 | Unknown | | `00 00 00`, `00 00 FF`, `00 00 00`, `00 00 BF` |

libratbag's set for 0x64 sends sub-id 0x81 (get), so it never changes the rate. Use 0x80.

## DPI

The four stages in the profile are raw 17, 34, 68 and 136. libratbag's G9x file says 200 to 5700 in steps of 50, which maps raw `n` to `200 + 50n` and makes 136 into 7000, above the G9x's 5700. That's wrong for the G9x.

Checked with the DPI buttons: 0x63 steps through 17, 34, 68 and 136 and back, the four stages of the active profile, and bytes 4–5 stay `80 01`. So the register and the profile use the same raw units, and the fifth (blank) stage is skipped.

Checked against Logitech Gaming Software 9.04.49: it shows the same profile as 400, 800, 1600 and 3200 dpi. So `dpi = raw × 400 / 17` (about 23.5 dpi per unit), not libratbag's 50.

Checked at the top: LGS set to 5700 stores raw 242 (`00 F2`), and 0x63 reads `F2 00 F2 00`. 5700 × 17 / 400 = 242.25, so LGS rounds the raw value down or to the nearest. It's linear from 200 to 5700.

- Raw from dpi: `round(dpi × 17 / 400)`.
- Dpi from raw: `raw × 400 / 17`, rounded to the nearest 50. That gives 400, 800, 1600, 3200 and 5700 for 17, 34, 68, 136 and 242.
- The lowest, 200, is raw 8.5. LGS shows Profile 2's raw 9 as 200 (and 85 as 2000), so rounding to the nearest 50 matches LGS. Profile 5 stores raw 8 for its first stage, which also rounds to 200. Either is fine when writing 200; use 8 or 9 consistently.

Saving from LGS rewrote all three profile pages, not only the one changed. Unused stages got LED bytes where they had `00 00` (`12 22` or `12 21`), and on page 2 the default stage went from 2 to 1 and the report interval from 2 to 1 ms.

LGS also shows: report rates 125, 200, 250, 333, 500 and 1000 (intervals 8, 5, 4, 3, 2 and 1 ms), five profile slots of which three are on (the directory lists three), the profile colours red, green and blue, and the buttons as decoded under Profiles (scroll left and right, DPI up and down, forward, back).

## Profiles

Flash is in pages of 512 bytes. Page 0 is RAM, page 1 the directory, pages 2 to 31 profiles. Each page ends with a CRC-16 (CCITT, start 0xFFFF, big endian) over the first 510 bytes, the same as `GMouse.Crc`. Checked on pages 1 to 4.

### Directory (page 1)

3 bytes per profile: page, offset (in words), LED pattern. 0xFF ends the list. Checked: `02 00 01`, `03 00 03`, `04 00 07`, so three profiles on pages 2, 3 and 4. The rest of the page holds macros and a name in UTF-16 (from Logitech Gaming Software).

### Profile page (G500 layout)

| Offset | Size | What | Checked (page 2) |
|---|---|---|---|
| 0x00 | 3 | LED colour R G B | `FF 00 00` (pages 3 and 4: green, blue) |
| 0x03 | 1 | Unknown | `80` |
| 0x04 | 5 × 6 | DPI stages: X (2, big endian), Y (2, big endian), 4 LED nibbles (1 off, 2 on) | `00 11 00 11 22 11`, then 0x22, 0x44, 0x88, then a blank stage |
| 0x22 | 1 | Angle snapping | `01` |
| 0x23 | 1 | Default stage | `02` |
| 0x24 | 2 | Unknown | `10 10` |
| 0x26 | 1 | Report interval in ms | `02` (page 3: `01`) |
| 0x27 | 13 × 3 | Buttons | see below |
| 0x4E | 425 | `LGS02`, then the profile name (23 UTF-16 characters) and 11 macro names (17 characters each), zero-filled. 0xFF if never set | After LGS's Restore Defaults: `LGS02` and "Profile 1" |
| 0x1F7 | 7 | 0xFF | |
| 0x1FE | 2 | CRC | |

libratbag reads the DPI stages as little endian. On the G9x they are big endian: `00 11` is 17. Register 0x63 is little endian.

Buttons, 3 bytes each:

| Type | Meaning |
|---|---|
| `81 <flags 2, little endian>` | Mouse button: 0x0001 left, 0x0002 right, 0x0004 middle, 0x0008 back, 0x0010 forward, up to 0x0800 |
| `82 <modifiers> <key>` | Keyboard key |
| `83 <special 2>` | 0x01 wheel left, 0x02 wheel right, 0x04 DPI up, 0x08 DPI down, 0x10 profile up, 0x20 profile down (and cycle variants 0x05, 0x09, 0x11, 0x21) |
| `84 <usage 2>` | Consumer control |
| `8F 00 00` | Off |
| other | Macro: `<page> <offset in words>` (page 4 has `01 66 00`) |

### Factory defaults

What LGS's Restore Defaults wrote (checked):

- Five profile pages, 2 to 6, in the order of LGS's Profile 1 to 5, coloured red, green, blue, magenta and orange (`FF 9B 00`). Only pages 2, 3 and 4 are in the directory, so profiles 4 and 5 are off. A profile that's off keeps its page.
- Profile 1: 400, 800, 1600 and 3200 dpi, starting on 800 (default stage 1), 500 Hz, angle snapping on.
- Profile 2: three stages, raw 9, 34 and 85 (800 and 2000 dpi for the last two), 1000 Hz. Profile 5: raw 8, 34 and 85, 1000 Hz. 9 and 8 are both near 200 dpi (8.5), so LGS's display for these two answers how it rounds.
- Page 6 has `FF 81` at 0x22–0x23 where the others have angle snapping and the default stage. Not understood.

## Saving

Not like the 0x8100 mice. libratbag:

1. Switches to the factory profile (0x0F `FF`) so a button press can't switch profiles midway.
2. Uploads the first 256 bytes to RAM: 0xA1 `01` resets, then long messages with sub-id 0x92 (first, with a header: `01 <page> <offset/2> 00 00 <size, 2 BE> 00 00`) and 0x93 (the rest), each answered by 0x50.
3. Erases the profile page: long set 0xA0 `02 00 00 00 00 00 <page>`.
4. Copies RAM to flash: long set 0xA0 `03 00 <src page> <src off/2> 00 00 <dst page> <dst off/2> 00 00 <size, 2 BE>`.
5. Uploads and copies the second 256 bytes the same way.
6. Switches back to the profile (0x0F `00 <index>`).

An LGS capture of a G700s save shows the same sequence byte for byte, and cvuchener's hidpp and g500 agree; see [more references](research/more-references.md#g9x-saving-hid-10-flash-write). The 0x50 ack is `10 00 50 <status> <seq>`, status 01 for OK. The second half is copied to word offset 0x80 without a second erase. The CRC is the host's job.

A test that writes no flash: upload a page to RAM page 0 only, then 0x0F `01 00 00` loads the profile from RAM. g500-control does this for live changes. If the G9x shows the uploaded profile, the upload works, and a replug undoes it.

An erase that is cut off leaves a blank page. Before any of this: a backup of the page, as for the mice, and a safety rule like the others: never write page 0 or 1, only pages the directory lists.

## Other mice this covers

From libratbag's device files:

| Mouse | USB | Profile layout |
|---|---|---|
| G9x (and Call of Duty MW3) | C066, C249 | G500 |
| G500, G500s | C068, C24E | G500 |
| G5 (2nd edition) | C049 | G500 |
| G700, G700s | C06B, C07C, receiver C531 | G700: other DPI and button layout |
| G9 | C048 | G9: 1-byte DPI stages, 10 buttons |

The G700 is wireless behind its own receiver, so it isn't at index 0x00. Leave it for later.

## Code

| File | Change |
|---|---|
| `app/Devices.cs` | Done: the scan finds HID++ 1.0 devices at index 0x00 (`Found.Hidpp10`, `Found.ShortPath`). `Found.IsG9x`: Hidpp10, PID C066 or C249. |
| `app/Keyboard.cs` | Done: `Hidpp.Register(subId, register, parameters)` sends HID++ 1.0 register calls on the short collection (long sets on the long one) and reads replies from both. Sets are logged as `register XX`. |
| `app/Mouse.cs` | Done: `G9x` reads the colour, DPI, rate and the active profile's stages, and sets colour, DPI and rate live. `G9x.Build` puts the DPI (in the chosen stage, made the default), the rate and the colour into the page; `G9x.Store` writes it as above and reads it back. It refuses any page the directory doesn't point to as the active profile, and pages 0 and 1. |
| `app/PageMouse.cs` | Done: the mouse screen, with the G9x's effects (Off, Static), Save to mouse and Backups. No factory restore: the G9x has no factory profile page to copy. |
| `app/Fake.cs` | Done: `FakeG9x` with registers and pages, uploads to RAM, erase and copy, for `--demo` and the self-tests. |
| `app/SelfTest.cs` | Done: DPI conversion, reading, setting, saving (the exact erase and copy calls, other pages untouched) and the report. |
| `app/Report.cs` | Done: `Report.Registers` lists the registers, the directory and each profile page. |

## Order

1. Done: the DPI unit is 400/17 dpi, linear from 200 to 5700, shown rounded to the nearest 50 as LGS does.
2. Done: the report has the directory and profile pages.
3. Done. Checked on the real mouse: report rate (125 and 500 Hz), DPI (400 to 5700, and the stages), colour (white and red), Off (`00 00 00` turns the LED off) and dimmed colours.
4. Built. Checked on the real mouse: 0xA1 then the 0x92/0x93 upload of 256 bytes to RAM page 0, 17 reports, each acked with status 01; RAM read back the same, flash untouched. Then a full save from G-Lite on 2026-11-08: backup, 0x0F `FF`, erase and copy of page 2 in two halves, 0x0F `00 00`, read back matched, and the mouse kept the settings after a replug.
