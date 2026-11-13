# Newer G mice (0x2202 and 0x8061)

The PRO X SUPERLIGHT 2 family, PRO X 2, G502 X and other recent G mice report DPI through 0x2202 and often the report rate through 0x8061, where the older ones use 0x2201 and 0x8060. G-Lite opens them on the mouse screen. Nothing here has been tried on a real mouse.

Sources: [OpenLogi](https://github.com/AprilNEA/OpenLogi) (from Logi Options+), [logi-cli](https://github.com/meech-io/logi-cli) (MIT, tested on a PRO X 2 DEX), Lghub's `docs/protocol.md`, and the notes in [report-rate-8061.md](report-rate-8061.md) and [research/mice.md](research/mice.md).

## What G-Lite does

- DPI list: 0x2202 fn 2 `<sensor> <axis> <page>`, 13 bytes per page after a 3-byte echo, the same words as 0x2201 (a value, or `0xE000 | step` between two values), until a zero word. More than 1000 values are thinned to multiples of 50.
- DPI now: fn 5 `<sensor>` gives X at bytes 1–2, default X at 3–4, Y at 5–6, default Y at 7–8, lift-off at 9.
- Set DPI: fn 6 `<sensor> <X> <Y> <lift-off>`. Y is X when fn 1 byte 2 bit 0 says the sensor has its own Y, else 0. Lift-off is sent back as read. logi-cli found a zero Y or lift-off is ignored on the DEX; OpenLogi says both must be zero when the sensor lacks them. Sending back what was read fits both.
- Report rate: 0x8061 fn 1 gives the rates as bits in byte 1 (bit n is 125 Hz << n), fn 2 `<connection>` the index now (1 LIGHTSPEED, 0 wired; G-Lite asks 0 for a mouse on its cable, which answers at index 0xFF, and 1 behind a receiver), fn 3 `<index>` sets it.
- DPI stages: read from the active profile when its format is 6 or more: five records of 5 bytes from offset 4 (X and Y little endian, lift-off). Stages outside the mouse's DPI range drop the whole list, in case the layout isn't this one. Switching stage is 0x8100 fn 0xC, as on the older mice.

## What it doesn't do

- Save to mouse. Formats 6 and up move the buttons to 0x30 and 0x70 and keep the rate as an index; logi-cli and OpenMouse disagree on where the default stage and the wired rate sit. `State.Writable` needs format 3, so these mice are never written.
- Buttons and lighting from the profile.
- A DPI that isn't a saved stage, on mice that refuse it. On the PRO X 2 DEX, logi-cli found that fn 6 and the rate setter answer with error 5 while the mouse runs its onboard profile: only the profile's values count. G-Lite then says the mouse refused. The report rate is greyed out while a mouse runs its onboard profile, as on the G Pro Wireless.

## Next

An owner's report: it has 0x2202, 0x8061 and the profile sectors. With two reports that agree on the layout, Save to mouse can follow, with a backup first as on the older mice.
