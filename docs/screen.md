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

0x4540 fn 0 byte 0 gives the layout: 0D on this Swiss keyboard. G-Lite treats 0 and 1 as ANSI and anything above as ISO (`KeyLayout.IsIso`): Solaar reads 2 as ISO, and 9 and 0x3E as JIS, whose Enter has ISO's shape (PeriHub's `docs/protocols/keyboard-layouts.md`). A German G915 X LS answers 04, which Solaar calls AZERTY, so only the shape can be trusted. G-Lite's reports have only 01 (ANSI) and 0D (ISO).

The G915 ids in `g915.json` and `g915-tkl.json` follow the same rule. The Screen effect worked on a real G915 TKL (Swiss, also 0D); the full-size G915's are untested.

## Adding a keyboard

A report of the keyboard lists its layout (0x4540) and its lit ids (`lit ids=` under 0x8081). With a photo of the keyboard, that gives most of its layout file: letters, digits, F keys and modifiers follow the rule above. Extra keys (G keys, media keys, logo) need a test that lights one id at a time.

## Timing and cost

- A full update (8 fn 6 calls and fn 7) took 30–39 ms over LIGHTSPEED while streaming 10 times a second, 90–195 ms when idle in between. Changing the effect through 0x8071 fn 1 instead took 223 ms.
- Live effects send more per frame: single keys go four to a message (fn 1). Counted on a TKL-shaped grid at 10 frames a second, a frame takes about 3 messages for Starlight, 5 for Rain, 12 for Fire and 23 for Aurora, which changes nearly every key every frame; rounding colours coarser barely changes that.
- On a real G515 (2026-11-22), Rain at a frame every 40 ms got fn 7 answered with error 08, busy, and looked choppy. G-Lite now sends fn 7 again every 10 ms while the keyboard is busy (up to 20 times, logged the first time; it once took 2), sends a live effect's frame every 100 ms on a keyboard without a cable, and at most 40 keys a frame there (about the 9 messages of a Screen frame), the most changed first. A keyboard on USB keeps 40 ms and every key; the G815 was smooth.
- The same G515 twice got stuck answering busy to every frame, even after being claimed again: once after choppy Rain, once after G-Lite was killed during a frame. Turning it off and on fixed it. After a busy frame G-Lite now gives the lighting back to the keyboard before claiming it again; whether that clears it isn't known yet.
- After the keyboard is turned off and on, it comes back in its own mode and ignores per-key frames until it's claimed again. G-Lite claims it again and sends the whole frame every 2 seconds, without logging, and retries every 5 seconds while it can't reach it.
- The keyboard didn't dim or sleep during 7 minutes of updates without typing: its idle timers don't run while G-Lite sends colours. G-Lite doesn't dim it either; the pause options cover that (screen off or locked, low battery).
- Capturing four zones shrunk to 8×8 with StretchBlt at 10 per second used 5.4% of one core on a 1920×1080 screen; copying the whole screen used 11.8%.

## Colour

Each zone's average colour gets its saturation scaled by 1 + boost / 50 around its grey, then is brightened so its strongest channel is full. A zone darker than 12 out of 255 is off. Smooth changes moves 45% of the way to the new colour on each update. Colours are sent only when a zone moved by more than 6 (sum of the three channels).
