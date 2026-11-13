# Litra Glow, Beam and Beam LX

A Litra Beam LX owner sent a report from G-Lite 0.0.23 (below) and says it works well. The Beam and Glow are untested. Earlier, that owner said G-Lite didn't find the light. G HUB shows Power, Overall brightness and a Screen Sampler with 7 samples (Leftmost, Left, Mid-left, Middle, Mid-right, Right, Rightmost).

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

The owner's report (firmware ILL 01.02) shows 0x8071 v4 with two clusters, which G-Lite only uses through Try effect (below): cluster 0 lists effects 0x01, 0x0A and 0x15, cluster 1 lists 0x13, 0x16 and 0x18. So Breathe, Cycle and Wave are run by G-Lite: `Litra.Frame` works out the seven colours at a time, and the light's screen sends a frame every 50 ms while the effect is on, only when it changed. They keep running while G-Lite is in the tray and stop when it quits. Breathe fades the zone colours, Cycle turns all zones through the hues, Wave spreads the hues across the zones. Speed sets the period, 1 to 20 s.

G-Lite keeps the back light as `lighting.<device>` (on, brightness, 7 colours, effect, period) once it's been changed, and sets it again when the light's screen loads. The Screen effect has seven areas side by side (`Litra.ScreenZones`), sent as one frame with `Litra.Colors`.

## The Beam LX's report

Firmware ILL 01.02 build 0073, USB 046D:C903, Bluetooth B903. 0x8040 and 0x8081 sit at indexes 0x0A and 0x0C, as litra-rs hard-codes.

```
01 0001 v2  02 0003 v5  03 0005 v0  04 0011 v0  05 1602 v0  06 1990 v1  07 1EB0 v0  08 1803 v0
09 1807 v1  0A 8040 v1  0B 8071 v4  0C 8081 v0  0D 00C2 v0
1990  on=00  brightness info=05 00 1E 01 90 00 01 0F  brightness=00 1E
      temperature info=05 0A 8C 19 64 00 64 0F  temperature=13 88
8040  info=00 64 64 1F 00 01  now=00 64
8071  cluster 0 effect 0=00 00 00 01 00 05   effect 1=00 01 00 0A C0 27 13 88   effect 2=00 02 00 15 C0 05 13 88
      cluster 1 effect 0=01 00 00 13 00 01   effect 1=01 01 00 16 D4 B1 13 88   effect 2=01 02 00 18 00 15 0B B8
8081  page 0=00 00 FE  lit ids=01 02 03 04 05 06 07
```

- Front light: 30 to 400 lumen in steps of 1, 2700 to 6500 K in steps of 100.
- 0x8071 has two clusters. With G HUB's effect ids (Lghub's `docs/protocol.md`), cluster 0 lists Static (0x01), Breathe (0x0A) and smooth colour cycle (0x15, 21); cluster 1 lists streaming (0x13, 19), smooth colour wave (0x16, 22) and a two-colour signature effect (0x18, 24). Streaming is the mode for setting zones one by one, which is what 0x8081 does, so cluster 1 is probably the seven back zones. What cluster 0 lights isn't known.

## Try effect

On a light with 0x8071, the back light box has Try effect. Its menu lists every effect of every cluster, read when the screen loads (0x8071 fn 0 `<cluster> <effect> 00`, byte 3 the id). Picking one stops G-Lite's own animation and sends 0x8071 fn 1 `<cluster> <effect index> <10 parameter bytes> 01`, with the parameters from Lghub's notes:

- Static: `R G B 02`
- Breathe: `R G B <period u16> 00 64`
- Colour cycle and smooth colour cycle: `00 × 5 <period u16> 64`
- Colour wave and smooth colour wave: `00 × 6 <period low> 01 64 <period high>`
- Two colours: `R G B R2 G2 B2 00 32 64 00` (zone 1's colour and zone 7's, 50 ms as G HUB sends it, 100%)
- Others: `R G B`, the rest zeros

The colour is zone 1's and the period is the Speed slider's. The status line asks what lit up, and each pick is logged. Back to G-Lite's colours, or any change on the screen, sends G-Lite's own colours again and restarts its animation.

The last byte is the persist flag G-Lite sends to the G915. Whether the light keeps the effect after a power cycle isn't known; G-Lite sets its own colours again when the screen loads anyway. Reports read each cluster's location (fn 0 `<cluster> FF 00`, bytes 2–3).

## Next

- Ask the owner to try each entry and say what lit up: the back light, the front light, something else or nothing, and whether Back to G-Lite's colours brings the colours back.
- Then run Breathe, Cycle and Wave with the light's own effects on the cluster that is the back light, as on the G915. They would keep running without G-Lite and need no frames. Try effect goes away then.

## Sources

- timrogers/litra-rs (MIT): the bytes, in `src/lib.rs`, and issues #133, #188, #234, PR #185 (back light).
- "hid: Support for Litra Glow", a Linux kernel patch (Dec 2022, GPL-2.0, never merged): 0x1990's functions and events. Facts only.
- abergmeier/litra_glow_linux: the Glow's parsed HID descriptor.
- lnovelli/litra-lx README: set all 7 zones.
- Solaar (GPL-2.0): names 0x1990 ILLUMINATION; its 0x8040 functions match. Facts only.
