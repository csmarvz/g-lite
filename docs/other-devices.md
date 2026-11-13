# Other Logitech devices

Logitech devices that aren't G devices (MX mice and keyboards, the MX Creative Keypad and others) get a screen for their settings, when they report at least one of the features below other than Easy-Switch. A device with none of them keeps the info page. Buttons, gestures, per-app settings, Flow, the keypad's key images and plugins stay in Logi Options+, and G-Lite never diverts a button (0x1B04).

Untested on a real device. The bytes come from Solaar's settings (facts only, it's GPL), OpenLogi's captures of a real MX Master 3S, MX Master 3, MX Anywhere 3S, MX Keys S and MX Mechanical Mini (`fixtures/devices/*/cases`), and the MX Creative Keypad's report.

## What the screen does

Each setting is sent live when it changes, on its own: one the device refuses is named in the status line, goes back to what the device has, isn't remembered and doesn't stop the others. Only the settings you change are kept (a preset counts for the settings it changes; Revert goes back to what was kept when the screen loaded), as `device.<device>` in `settings.txt`, and sent again each time the screen loads (at scan, and when the device wakes), so changes made with the device's keys or in Logi Options+ to other settings stay. With the backlight on Automatic, the level isn't sent again. Presets are `profile.<device>.<name>`. Switching computers isn't kept or in presets.

| Box | Feature | Can be changed when |
|---|---|---|
| Brightness (Screens on a device that isn't a mouse or keyboard, like the keypad) | 0x8040 | the device has it |
| Pointer: Speed | 0x2201 | it lists more than one DPI |
| Scroll wheel: SmartShift, Switch to free spin at | 0x2111, else 0x2110 | the device has one |
| Scroll wheel: Invert scrolling | 0x2121 | fn 0 says the wheel can |
| Thumb wheel: Invert | 0x2150 | the device has it |
| Backlight: on, Automatic, Level | 0x1982 | Automatic needs both mode bits, Level needs manual mode and fn 2 |
| Easy-Switch | 0x1814, names from 0x1815 | more than one computer (else the box is hidden). With 0x1815, an unpaired computer can't be picked; without it, the question says how to come back from an empty one |

## The bytes

Function numbers, then parameters. Multi-byte values are big-endian unless noted.

- **0x8040 brightness**: fn 0 b0–1 the maximum (100 on the keypad), fn 1 b0–1 now, fn 2 `<hi> <lo>` sets it. Same as the G515.
- **0x2201 DPI**: fn 1 `00` lists the DPIs (a value with the top 3 bits set is a step: 200–8000 in steps of 50 on the MX Master 3S is `00C8 E032 1F40`), fn 2 `00` gives now at b1–2 and the default at b3–4 (used when now is 0), fn 3 `00 <hi> <lo>` sets it.
- **0x2110 SmartShift**: fn 0 gives b0 the mode (1 free spin, 2 ratchet), b1 the threshold, b2 the default. fn 1 `<mode> <threshold> <default>` sets them, and **0 in a byte leaves it**. G-Lite sends `00 <threshold> 00`, so the mode the wheel's button sets isn't touched.
- **0x2111 SmartShift, enhanced**: fn 0 b1 the default threshold, fn 1 the status like 0x2110 fn 0 (b2 the torque), fn 2 `00 <threshold> 00` sets the threshold.
  - The threshold is the wheel speed, in quarter turns a second, at which it switches to free spin. 255 means it never does: that's SmartShift off. G-Lite offers 1–50, like Solaar.
- **0x2121 scroll wheel**: fn 0 b1 bit 3 says it can invert. fn 1 b0: bit 0 diverts the wheel to software, bit 1 high resolution, bit 2 invert. fn 2 sets b0, with b1–2 sent back as read. G-Lite reads fn 1 just before writing and changes bit 2 only, so a diversion Logi Options+ set since isn't undone.
- **0x2150 thumb wheel**: fn 1 gives b0 diverted, b1 bit 0 inverted. fn 2 `<diverted> <inverted> 00` sets both, so G-Lite reads fn 1 just before and sends the diverted byte back as read. Its resolution can't be changed.
- **0x1982 backlight** (little-endian):
  - fn 0: b0 bit 0 on, b1 options (bits 3–4 the mode: 1 automatic, 2 set by the keys, 3 set by software), b2 what's supported (0x08 automatic, 0x20 set by software), b5 the level, b6–11 three durations in units of 5 s, b12–13 a fourth one.
  - fn 1 `<on> <options> FF <level> <durations>` writes the whole configuration, and the keyboard keeps it. G-Lite reads fn 0 just before, sends the durations back as read, sets the mode in bits 3–4, and sends the level only in mode 3 (else 0), like Solaar. With no level to send in mode 3, it sends fn 0's b5 back.
  - fn 2: b0 the number of levels, b1 the level lit right now (it fades when your hands leave), b2 the status. G-Lite takes the level from fn 0's b5, the stored setting, like Solaar.
  - The MX Keys S (v3) reports `01 1D 3D …` and 8 levels. The MX Mechanical Mini (v2) supports neither mode bit, so it only gets on and off.
- **0x1814 Easy-Switch**: fn 0 gives b0 the number of computers and b1 the current one, counted from 0. fn 1 `<computer>` switches, and the device leaves without answering: G-Lite sends it with `Hidpp.Fire`, which doesn't wait. It asks first.
- **0x1815 hosts info**: fn 0 b0 bit 0 says names can be read. fn 1 `<computer>` gives b1 1 when paired and b4 the name's length. fn 3 `<computer> <offset>` gives up to 14 bytes of the name (UTF-8) from b2. The names stay out of reports: they often carry the owner's name.

## Not yet

- **F1–F12 as media keys** on the MX Keys and MX Mechanical: they have 0x40A3, not the K295's 0x40A0.
- **Smooth scrolling** (0x2121 bit 1): set alone, Windows scrolls many times too fast (about 8 times on the G502 LIGHTSPEED; the MX Master 3S reports 15 steps a notch). Making it work hasn't been tried yet.
- The backlight's durations (how long it stays on), SmartShift's torque (0x2111), Options+'s host names for computers it hasn't seen.
- **Sending again after a quick reconnect.** Remembered settings are sent when the screen is made: at the first scan, and after the device was seen asleep. A device that drops and comes back between two scans isn't sent them again, so one it forgets (like inverted scrolling) stays lost until G-Lite starts or the device sleeps. The mouse screen checks inverted scrolling every 5 s for this; this screen doesn't yet.
- **Bolt receivers** are read through register 0xB5 sub-register 0x5N (pairing), from Solaar, untested: no Bolt receiver has been tried with G-Lite.
