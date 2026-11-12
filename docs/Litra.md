# Litra Glow, Beam and Beam LX

Untested on a real light. A Litra Beam LX owner said G-Lite didn't find it. G HUB shows Power, Overall brightness and a Screen Sampler with 7 samples (Leftmost, Left, Mid-left, Middle, Mid-right, Right, Rightmost).

## Why G-Lite missed it

The scan found its HID++ interface, then dropped it at the end because the name doesn't look like a G device (`Found.LooksG`). The scan now keeps every Logitech HID++ device with a name. Devices that aren't G devices (or a Litra) only get the info page, so the report is the only thing that talks to them, and it only reads.

## Ids and transport

- VID 046D. Glow C900, Beam C901, Beam LX C903. litra-rs also lists B901 for the Beam.
- One USB interface with two collections: Consumer Control (report ids 1 and 2, probably the buttons) and vendor page 0xFF43 with report id 0x11 only (20-byte HID++ long reports, no short ones). From the Glow's descriptor; the LX's is the same size.
- Device index 0xFF.
- Feature indexes differ between models and firmware (the front light is at 0x04 on the Glow and Beam, 0x06 on the Beam LX), so G-Lite looks them up.
- If the light has no 0x0005, its name comes from the USB product string ("Litra Beam LX").

## Front light: 0x1990 Illumination

| fn | what | bytes |
|---|---|---|
| 0 | get on/off | byte 0 bit 0 |
| 1 | set on/off | `01` or `00` |
| 2 | brightness info | byte 0 flags; min, max, resolution as big-endian u16 at bytes 1, 3, 5 |
| 3 | get brightness | big-endian u16, lumen |
| 4 | set brightness | big-endian u16, lumen |
| 7 | colour temperature info | same layout as fn 2 |
| 8 | get colour temperature | big-endian u16, kelvin |
| 9 | set colour temperature | big-endian u16, kelvin |

Ranges in litra-rs: Glow 20–250 lm, Beam and Beam LX 30–400 lm, 2700–6500 K in steps of 100. G-Lite reads fn 2 and fn 7 and falls back to 30–400 lm and 2700–6500 K when the answer looks wrong.

The light sends events when its buttons change it: event 0 on/off, 1 brightness, 2 colour temperature (from the kernel patch). G-Lite doesn't listen to them yet; it reads the light when its screen opens.

G-Lite doesn't set the front light again on start: the light keeps its own state and its buttons change it.

## Back light (Beam LX): 0x8040 and 0x8081

The indexes litra-rs hard-codes (0x0A and 0x0C) match the functions of 0x8040 and 0x8081. Not checked with a feature lookup on a real light yet; the report will show.

- 0x8040: fn 1 gets the brightness (big-endian u16), fn 2 sets it (`00 <percent>`, 1–100), fn 3 gets on/off (some firmware answers error 07), fn 4 sets it (`01` or `00`).
- 0x8081: 7 zones, ids 1–7. fn 1 sets up to 4 zones per message (`<id> R G B`, unused entries `FF 00 00 00`), then fn 7 `00 00 01 00 00` shows the frame. litra-rs keeps every channel at 1 or more. The litra-lx README says to set all 7 zones every time, so G-Lite always does.
- Which zone is leftmost isn't known.

No effects are known on the light itself (no 0x8070 or 0x8071), so Breathe, Cycle and Wave are run by G-Lite: `Litra.Frame` works out the seven colours at a time, and the light's screen sends a frame every 50 ms while the effect is on, only when it changed. They stop when G-Lite closes. Breathe fades the zone colours, Cycle turns all zones through the hues, Wave spreads the hues across the zones. Speed sets the period, 1 to 20 s.

G-Lite keeps the back light as `lighting.<device>` (on, brightness, 7 colours, effect, period) once it's been changed, and sets it again when the light's screen loads. The Screen effect has seven areas side by side (`Litra.ScreenZones`), sent as one frame with `Litra.Colors`.

## To do after the owner's report

- `PageLight.sent` and `framed` are written on the device queue and read or changed on the UI thread (`ScreenOff` sets `sent.Colors`, `Lost` sets `sent = null` while a queued `Apply` may read it). The worst case is a null reference caught by `Do`: an error in the log and the device reopened. Fix it together with whatever the report changes, for example by giving `Apply` its own copy of `sent` or doing all of it on the queue.

## Sources

- timrogers/litra-rs (MIT): the bytes, in `src/lib.rs`, and issues #133, #188, #234, PR #185 (back light).
- "hid: Support for Litra Glow", a Linux kernel patch (Dec 2022, GPL-2.0, never merged): 0x1990's functions and events. Facts only.
- abergmeier/litra_glow_linux: the Glow's parsed HID descriptor.
- lnovelli/litra-lx README: set all 7 zones.
- Solaar (GPL-2.0): names 0x1990 ILLUMINATION; its 0x8040 functions match. Facts only.
