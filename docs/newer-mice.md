# Newer G mice (0x2202 and 0x8061)

The PRO X SUPERLIGHT 2 family, PRO X 2 and other recent G mice report DPI through 0x2202 and often the report rate through 0x8061, where the older ones use 0x2201 and 0x8060. G-Lite opens them on the mouse screen. Nothing here has been tried on a real mouse in G-Lite. Not every G502 X: a G502 X LIGHTSPEED (409F) has 0x2201 and 0x8060 and profile format 3, like the older mice (see [G502X.md](G502X.md)).

Sources: [Featherweight](https://github.com/Ad1FPS/Featherweight) (MIT, read and wrote format 7 on a PRO X SUPERLIGHT 2, `docs/ONBOARD_PROFILES.md`), [logitech-onboard-memory](https://github.com/marco-vrinssen/logitech-onboard-memory) (MIT, `lomm`, wrote format 7 on a PRO X 2 DEX), [OpenLogi](https://github.com/AprilNEA/OpenLogi) (from Logi Options+), [logi-cli](https://github.com/meech-io/logi-cli) (MIT, tested on a PRO X 2 DEX), Lghub's `docs/protocol.md`, and the notes in [report-rate-8061.md](report-rate-8061.md), [research/mice.md](research/mice.md) and [research/more-references.md](research/more-references.md#profile-format-7).

## What G-Lite does

- DPI list: 0x2202 fn 2 `<sensor> <axis> <page>`, 13 bytes per page after a 3-byte echo, the same words as 0x2201 (a value, or `0xE000 | step` between two values), until a zero word. More than 1000 values are thinned to multiples of 50.
- DPI now: fn 5 `<sensor>` gives X at bytes 1–2, default X at 3–4, Y at 5–6, default Y at 7–8, lift-off at 9.
- Set DPI: fn 6 `<sensor> <X> <Y> <lift-off>`. Y is X when fn 1 byte 2 bit 0 says the sensor has its own Y, else 0. Lift-off is sent back as read. logi-cli found a zero Y or lift-off is ignored on the DEX; OpenLogi says both must be zero when the sensor lacks them. Sending back what was read fits both.
- Report rate: 0x8061 fn 1 gives the rates as bits in byte 1 (bit n is 125 Hz << n), fn 2 `<connection>` the index now (1 LIGHTSPEED, 0 wired; G-Lite asks 0 for a mouse on its cable, which answers at index 0xFF, and 1 behind a receiver), fn 3 `<index>` sets it.
- From the active profile, when its format is 6 to 8: the DPI stages (five records of 5 bytes from offset 4, X and Y little endian, then lift-off; stages outside the mouse's DPI range drop the whole list, in case the layout isn't this one) and the buttons (4 bytes each from 0x30, the same records as the older mice). Switching stage is 0x8100 fn 0xC, as on the older mice.
- Save to mouse and Restore the factory profile on formats 6 to 8 (below).

## Format 7

The PRO X SUPERLIGHT 2 (Featherweight's fn 0 reply: `01 07 01 05 01 05 10 00 FF 0A 04`, five profiles, one factory profile, five buttons, 16 sectors of 255 bytes) and the PRO X 2 DEX.

| Offset | Content |
|---|---|
| 0x00 | Report rate over the receiver, a 0x8061 index (0–6 for 125–8000 Hz) |
| 0x01 | Report rate on the cable, the same index |
| 0x02 | Default DPI stage |
| 0x03 | DPI shift stage |
| 0x04 | Five stages of 5 bytes: X LE16, Y LE16, lift-off (1 low, 2 medium, 3 high) |
| 0x25 | Bunny-hop prevention timeout in 10 ms units, 0 or FF off (lomm-web, GFlick) |
| 0x2C, 0x2E | Sleep and power-off timeouts in seconds, little endian: a PRO X 2 DEX and a PRO 2 LIGHTSPEED read 60/300 and 65535/300 that way (PeriHub issues #32, #33). OMM's builders write them big endian; lomm reads LE16. G-Lite doesn't write them |
| 0x30 | 12 buttons of 4 bytes |
| 0x70 | The G-shift layer |
| 0xA0 | Name, UTF-16LE |
| 0xD0 | LED records of 11 bytes |
| 0xFD | CRC-16 CCITT over the rest, big endian |

- The rate bytes: Featherweight wrote 04 to byte 0 of a SUPERLIGHT 2's active profile and its live rate over the receiver went to 2000 Hz, then back to 1000 Hz with 03. lomm-web, GOMM, OpenGMice and superstrike-studio also put the receiver's rate at 0 and the cable's at 1. lomm says the opposite; with both bytes at 03 on its DEX, it couldn't have told them apart. Featherweight's own labels (byte 1 default stage, 2 shift, 3 active) read its dump the other way: byte 1 was 03, a stage that didn't exist, which fits a 1000 Hz rate.
- Writing: fn 6 `<sector> 00 00 00 FF`, fn 7 for each 16 bytes (the last one 15 bytes and a 00), fn 8, then read back. Featherweight did this on the active profile (0001) and a disabled one (0005); both read back byte for byte, and the mouse used the new DPI straight away, with no fn 3. The factory sectors read with no valid CRC.
- **What G-Lite writes** (`MouseProtocol.BuildWide`): the edited stage's X and Y (only when its DPI changed; a Y set apart from X keeps its ratio), the default stage (byte 2), the rate byte of the link in use (`State.Wired`: 1 on the cable, 0 behind the receiver) and the buttons. Everything else is kept as read, the LED records too: a light set on a format 7 mouse that has one stays live, and the status line says the save won't keep it. A profile with a bad CRC shows as damaged and isn't saved. A backup comes first, the sector is read back after, then fn 3 reloads the profile, as on the older mice. `State.WideFormat` is formats 6 to 8 (below).
- **Restore the factory profile** copies 0x0101 over the active profile, keeping its name, with a new CRC, then picks its default stage. G-Lite first checks the factory profile reads as this layout (both rate bytes 0–6, at least one DPI stage, all in the mouse's range, and the default stage one of them) and refuses otherwise, showing its first bytes. Restore isn't offered when 0x8100 fn 0 byte 4 counts no factory profile, as in OMM. OMM does the same copy, from 0x0100 + n, which is 0x0101 on a mouse with one factory profile, like the SUPERLIGHT 2.
- **DPI:** these mice refuse 0x2202 fn 6 while they run their profile (error 5, seen by logi-cli and OpenGMice), so `HidppMouse.SendDpi` only picks the stage, and an edited stage's DPI waits for the save ("DPI changes when saved to the mouse"). The report rate waits for the save too.
- **Formats 6 and 8** are saved the same way. OMM's builders ([research/omm/](research/omm/README.md)) put every byte `BuildWide` writes in the same place: format 6 lacks bunny hopping at 0x25, format 8 (a SUPERSTRIKE, per superstrike-studio, which calls it a PRO X3) adds analog buttons (HITS) at 0x26–0x2B. Both have 12 button slots (48 bytes from 0x30), as format 7 does, so G-Lite reads at most 12. Neither has been saved on a real mouse in G-Lite. For format 6 there is only OMM's code, lomm (which writes formats 6 and up but was tried on format 7) and lomm-web's offsets.
- **Not done:** lift-off, the shift stage, bunny-hop prevention, timeouts, the G-shift layer, lighting (the SUPERLIGHT 2 has none).

## Format 4: the original PRO X SUPERLIGHT

Not one of the newer mice: it has 0x2201, 0x8060 and a hidden 0x8070 (Solaar's report of a PRO X Wireless, WPID 4093, USB C094; the mouse names itself PRO X Wireless). Its profiles are format 4, which libratbag calls the G915's, but a mouse's format 4 has format 3's layout up to 0xD0.

- Sources: [GFlick](https://github.com/R4ULtv/gflick) (MIT) wrote 255 bytes to a disabled profile (0005) of an original SUPERLIGHT and read them back with a valid CRC, changed the name, checked it, restored the original bytes, and turned the profile off and on in the directory. Its encoder treats formats 1 to 5 as one layout: byte 0 the report interval in ms (one rate for both links), 1 the default stage, 2 the shift stage, five LE16 stages from 3, timeouts at 28 and 30, buttons from 32, name at 160. [onboard-lite](https://github.com/ZhichenJin/onboard-lite) (MIT, C#) reads and saves the same layout on the same mouse; its README says reading and saving were checked on one, wired and over LIGHTSPEED.
- **What G-Lite writes** (`State.ClassicFormat`): the same as on formats 2 and 3 (`MouseProtocol.Build`: the edited stage, the default stage, the report interval, the buttons), never the LED records from 0xD0. onboard-lite writes OMM's lighting into 0xD0 and 0xDB, plus power-save copies at 0xE6 and 0xF1 and flags at 0xFC on 0x8071 mice; that part isn't checked by a second source, so a light set on a format 4 mouse stays live and the status line says the save won't keep it.
- Lighting: the SUPERLIGHT's 0x8070 is flagged internal and hidden (root fn 0, byte 1 bits 5 and 6) and refuses fn 1 with error 5. It's the status light. G-Lite shows no lighting for a 0x8070 flagged both (`Hidpp.Hidden`).
- Kept as on formats 2 and 3: backup first, read back, fn 3 to reload, a bad CRC counts as damaged, Restore the factory profile copies 0x0101 whole, its LED records too (its layout is the same, per onboard-lite, which builds a blank profile from it). Not kept: setting up a mouse that runs its factory profile (`MouseProtocol.Setup` stays on formats 2 and 3: OMM marks a profile on with 00 in the Superlight's directory, where G-Lite writes 01).
- A G502 X wired (C099) reports format 4 in dukaci/driver-mouse-g502x and format 3 in omalogi (see [research/more-references.md](research/more-references.md)). Either way it gets this save; no source has written format 4 on one.
- A G903 HERO reports format 4 too, with 12 buttons (fn 0 `01 04 01 05 01 0C 10 00 FF 0A 04`, from shefben/open_logitech_GHUB's test vectors, confirmed on a real G903 HERO in PeriHub issue #115: WPID 4087, USB C091, MPM 23.01.0013, its directory in G HUB's form `00 01 01 FF`, `00 02 00 FF`; see [research/more-references.md](research/more-references.md#profile-format-7)). It gets this save as well. It lights through 0x8071 and has no 0x8070 (PeriHub #115, libratbag #774), so G-Lite shows it no lighting, like the G502 X PLUS. Whether the original G903 (C086) has 0x8070 isn't known. No source has written its profile; the layout is the one GFlick, onboard-lite, g502-hub and libratbag give for formats 1 to 5. A report would check it.

## Format 5: the G502 X PLUS

Format 3's layout up to 0xD0, with a write counter at 0x12 (2 bytes) and the power-save and power-off timeouts at 0x1C and 0x1E, as on format 4; then four 0x8071 LED records at 0xD0, 0xDB, 0xE6 and 0xF1 (active and power save for two clusters) and flags at 0xFC. Sources: OMM's builders ([research/omm/](research/omm/README.md)), gnexus's G502 X PLUS dumps and GOMM, which checked a G502 X PLUS against OMM 2.7 (see [research/more-references.md](research/more-references.md)).

- **What G-Lite writes** (`State.ClassicFormat`): the same as on format 4: the edited stage, the default stage, the report interval and the buttons. The interval is in whole ms, so on formats 2 to 5 G-Lite offers no rate above 1000 Hz. The write counter, the timeouts and the LED records stay as read. Restore the factory profile copies 0x0101, keeping the profile's name, as on format 4.
- The G502 X PLUS lights through 0x8071 and has no 0x8070, so G-Lite shows no lighting for it.
- None has been saved on a real mouse in G-Lite.
- This is the G502 X PLUS on its older firmware. After its firmware update the G502 X family reports format 10 (below), which G-Lite only reads.

## Later

- **Format 4 lighting, saved** (a G502 X wired on format 4; the G903 HERO lights through 0x8071 instead): OMM's builders confirm what onboard-lite writes, 0x8070 records at 0xD0 and 0xDB and power-save copies at 0xE6 and 0xF1, which was the missing second source. It needs the save to build those records, as `Build` does on formats 2 and 3, plus the power-save copies.
- **Format 10** (a G502 X after its firmware update; OMM calls it ARTANIS_POST_DFU): format 1's layout with byte 0 FF, the rate indexes at 0x14 (receiver) and 0x15 (cable), 13 buttons from 0x20, a Y table at 0x56 and the G-shift layer at 0x60 (see [research/omm/README.md](research/omm/README.md)). G-Lite shows no stages or buttons for it: read at format 7's offsets, bytes of two neighbouring stages can make a value inside the mouse's range. First read its stages (X from 3, as on format 1) and buttons; writing waits for a report from a mouse with that firmware, since OMM migrates format 10 profiles and writes them back as soon as it opens the mouse.

## Next

A report and one save tried on a SUPERLIGHT 2, a DEX, a SUPERSTRIKE, an original SUPERLIGHT or a G502 X PLUS. The report shows the profile sectors, so the layout above can be checked against another mouse.
