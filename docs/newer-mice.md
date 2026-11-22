# Newer G mice (0x2202 and 0x8061)

The PRO X SUPERLIGHT 2 family, PRO X 2 and other recent G mice report DPI through 0x2202 and often the report rate through 0x8061, where the older ones use 0x2201 and 0x8060. G-Lite opens them on the mouse screen. Nothing here has been tried on a real mouse in G-Lite. Not every G502 X: a G502 X LIGHTSPEED (409F) has 0x2201 and 0x8060 and profile format 3, like the older mice (see [G502X.md](G502X.md)).

Sources: [Featherweight](https://github.com/Ad1FPS/Featherweight) (MIT, read and wrote format 7 on a PRO X SUPERLIGHT 2, `docs/ONBOARD_PROFILES.md`), [logitech-onboard-memory](https://github.com/marco-vrinssen/logitech-onboard-memory) (MIT, `lomm`, wrote format 7 on a PRO X 2 DEX), [OpenLogi](https://github.com/AprilNEA/OpenLogi) (from Logi Options+), [logi-cli](https://github.com/meech-io/logi-cli) (MIT, tested on a PRO X 2 DEX), Lghub's `docs/protocol.md`, and the notes in [report-rate-8061.md](report-rate-8061.md), [research/mice.md](research/mice.md) and [research/more-references.md](research/more-references.md#profile-format-7).

## What G-Lite does

- DPI list: 0x2202 fn 2 `<sensor> <axis> <page>`, 13 bytes per page after a 3-byte echo, the same words as 0x2201 (a value, or `0xE000 | step` between two values), until a zero word. More than 1000 values are thinned to multiples of 50.
- DPI now: fn 5 `<sensor>` gives X at bytes 1–2, default X at 3–4, Y at 5–6, default Y at 7–8, lift-off at 9.
- Set DPI: fn 6 `<sensor> <X> <Y> <lift-off>`. Y is X when fn 1 byte 2 bit 0 says the sensor has its own Y, else 0. Lift-off is sent back as read. logi-cli found a zero Y or lift-off is ignored on the DEX; OpenLogi says both must be zero when the sensor lacks them. Sending back what was read fits both.
- Report rate: 0x8061 fn 1 gives the rates as bits in byte 1 (bit n is 125 Hz << n), fn 2 `<connection>` the index now (1 LIGHTSPEED, 0 wired; G-Lite asks 0 for a mouse on its cable, which answers at index 0xFF, and 1 behind a receiver), fn 3 `<index>` sets it.
- From the active profile, when its format is 6 or more: the DPI stages (five records of 5 bytes from offset 4, X and Y little endian, then lift-off; stages outside the mouse's DPI range drop the whole list, in case the layout isn't this one) and the buttons (4 bytes each from 0x30, the same records as the older mice). Switching stage is 0x8100 fn 0xC, as on the older mice.
- Save to mouse on format 7 (below).

## Format 7

The PRO X SUPERLIGHT 2 (Featherweight's fn 0 reply: `01 07 01 05 01 05 10 00 FF 0A 04`, five profiles, one factory profile, five buttons, 16 sectors of 255 bytes) and the PRO X 2 DEX.

| Offset | Content |
|---|---|
| 0x00 | Report rate over the receiver, a 0x8061 index (0–6 for 125–8000 Hz) |
| 0x01 | Report rate on the cable, the same index |
| 0x02 | Default DPI stage |
| 0x03 | DPI shift stage |
| 0x04 | Five stages of 5 bytes: X LE16, Y LE16, lift-off (1 low, 2 medium, 3 high) |
| 0x2C, 0x2E | Sleep and power-off timeouts in seconds, LE16 |
| 0x30 | 16 buttons of 4 bytes |
| 0x70 | The G-shift layer |
| 0xA0 | Name, UTF-16LE |
| 0xD0 | LED records of 11 bytes |
| 0xFD | CRC-16 CCITT over the rest, big endian |

- The rate bytes: Featherweight wrote 04 to byte 0 of a SUPERLIGHT 2's active profile and its live rate over the receiver went to 2000 Hz, then back to 1000 Hz with 03. lomm-web, GOMM, OpenGMice and superstrike-studio also put the receiver's rate at 0 and the cable's at 1. lomm says the opposite; with both bytes at 03 on its DEX, it couldn't have told them apart. Featherweight's own labels (byte 1 default stage, 2 shift, 3 active) read its dump the other way: byte 1 was 03, a stage that didn't exist, which fits a 1000 Hz rate.
- Writing: fn 6 `<sector> 00 00 00 FF`, fn 7 for each 16 bytes (the last one 15 bytes and a 00), fn 8, then read back. Featherweight did this on the active profile (0001) and a disabled one (0005); both read back byte for byte, and the mouse used the new DPI straight away, with no fn 3. The factory sectors read with no valid CRC.
- **What G-Lite writes** (`MouseProtocol.BuildWide`): the edited stage's X and Y, the default stage (byte 2), the rate byte of the link in use (`State.Wired`: 1 on the cable, 0 behind the receiver) and the buttons. Everything else is kept as read. A backup comes first, the sector is read back after, then fn 3 reloads the profile, as on the older mice. `State.WideFormat` is format 7 only.
- **DPI:** these mice refuse 0x2202 fn 6 while they run their profile (error 5, seen by logi-cli and OpenGMice), so `HidppMouse.SendDpi` only picks the stage, and an edited stage's DPI waits for the save ("DPI changes when saved to the mouse"). The report rate waits for the save too.
- **Not done:** factory restore (the factory profile's layout isn't known), lift-off, the shift stage, timeouts, the G-shift layer, lighting (the SUPERLIGHT 2 has none), formats 6, 8 and 10 (lomm-web, GOMM and superstrike-studio describe them; see [research/more-references.md](research/more-references.md)).

## Next

An owner's report and one save tried on a SUPERLIGHT 2 or DEX. The report shows the profile sectors, so the layout above can be checked against a third mouse.
