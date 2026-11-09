# Screen effect

A lighting effect that follows the monitor, like G HUB's screen sampler (issue #19). G-Lite captures each zone of the chosen monitor, averages it and sends the colour to the zone's keys. It only runs while G-Lite runs and isn't saved to the keyboard.

## Per-key lighting (0x8081)

Checked on the G515 LIGHTSPEED TKL (ISO, Swiss) on 2026-11-09. The function layout comes from OpenLogi's `per_key_lighting.rs` (MIT).

| Function | Arguments | What it does |
|---|---|---|
| fn 0 | `00 <page> 00` | Bitmap of the lit ids: 14 bytes from byte 2, page 0 is ids 0–111, page 1 is 112–223 |
| fn 1 | `<id> R G B` up to 4 times | One colour per id |
| fn 6 | `R G B <id>…` up to 13 ids | One colour on several ids |
| fn 7 | `00 00 00 00 00` | Shows the frame (0 = not saved) |

The lighting is claimed like the live preview (0x8101 fn 6 `05`, 0x8071 fn 5 `01 03 04`) and released with 0x8071 fn 5 `01 00 00` and 0x8101 fn 6 `03`, which brings the keyboard's own lighting back.

## Key ids

An id is the USB key code minus 3: A = 01 to Z = 1A, 1 to 0 = 1B–24, Enter 25, Esc 26, Backspace 27, Tab 28, Space 29, F1–F12 37–42, arrows 4C–4F. The modifiers are E0–E7 minus 0x78: left Ctrl 68, left Shift 69, left Alt 6A, left Win 6B, right Ctrl 6C, right Shift 6D, right Alt (AltGr) 6E. On the G515, Fn is 6F, the right Win id.

The G515 reports 96 ids: 00–4F, 61–6F and FE. Checked by lighting them:

- 2F is the ISO key next to Enter (`$` on Swiss), 61 the ISO `<>` key, 62 Menu.
- 00, 2E (the US `\`), 63–67 and FE have no LED on this keyboard.

0x4540 fn 0 byte 0 gives the layout: 0D on this Swiss keyboard. G-Lite treats 0–2 as ANSI and anything above as ISO.

The G915 ids in `g915.json` and `g915-tkl.json` follow the same rule and are untested. Its logo, media keys and G keys are `--`.

## Timing and cost

- A full update (8 fn 6 calls and fn 7) took 30–39 ms over LIGHTSPEED while streaming 10 times a second, 90–195 ms when idle in between. Changing the effect through 0x8071 fn 1 instead took 223 ms.
- After the keyboard is turned off and on, it comes back in its own mode and ignores per-key frames until it's claimed again. G-Lite claims it again and sends the whole frame every 2 seconds, without logging, and retries every 5 seconds while it can't reach it.
- The keyboard didn't dim or sleep during 7 minutes of updates without typing: its idle timers don't run while G-Lite sends colours. G-Lite doesn't dim it either; the pause options cover that (screen off or locked, low battery).
- Capturing four zones shrunk to 8×8 with StretchBlt at 10 per second used 5.4% of one core on a 1920×1080 screen; copying the whole screen used 11.8%.

## Colour

Each zone's average colour gets its saturation scaled by 1 + boost / 50 around its grey, then is brightened so its strongest channel is full. A zone darker than 12 out of 255 is off. Smooth changes moves 45% of the way to the new colour on each update. Colours are sent only when a zone moved by more than 6 (sum of the three channels).
