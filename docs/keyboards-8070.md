# Older G keyboards

The G512 and similar G keyboards light up through 0x8070, the feature `GMouse` already uses, not through 0x8071 like the G515 and G915. Supporting them is mostly a new screen around code G-Lite has. From OpenLogi's G512 recording (see [openlogi.md](openlogi.md)) and [lighting-control.md](lighting-control.md).

Status: a first version is built on the G560's screen (`PageSpeaker`, for `Found.IsLedKeyboard`: kind keyboard, 0x8070, no 0x8071) and tested against `FakeSpeaker` with two zones only. It sets Off, Static, Breathe and Cycle live on all zones or each one, names zones from their location (1 Keys, 2 Logo, else Zone n), keeps the lighting as `lighting.<device>` and sets it again on load. No Wave, no per-key colours, no key picture, no Save. The plan below (its own page, a shared `Led` class) is kept for when a real keyboard's report is in.

To check on a real keyboard: that 0x8070 fn 3 without a persistence byte lights it (the G560 path), the zone locations, and which effects each zone lists.

## Per-key colours (0x8080), not built

From Lghub (`perkey.py`, read from G HUB): fn 0 gives the key types the keyboard has as bits in its first two bytes (keyboard 1, consumer 2, G key 4, button 8, logo 0x10, other keys 0x40); fn 3 sets colours, `<type u16> <count u16>` then `<key> R G B` per key, up to 14 in a 64-byte report (3 in a 20-byte one); fn 5 `<persist>` shows them. The key byte is the HID usage for keyboard keys.

## The G512

OpenLogi's recording (`fixtures/devices/g512-001`): "G512 RGB MECHANICAL GAMING KEYBOARD", wired, USB 046D:C33C, HID++ at index 0xFF.

Features that matter:

| Feature | Version | Use |
|---|---|---|
| 0x8070 colour LED effects | 7 | Effects |
| 0x8080 per-key lighting | 2 | Not used |
| 0x8060 report rate | 0 | Could reuse `GMouse` |
| 0x4540 keyboard layout | 0 | ANSI or ISO, later |

No 0x8071, 0x8040 (brightness), 0x8100 or 0x8101. So:

- No brightness feature. Brightness has to be in the effect: Static scales the colour, Breathe and Cycle take an intensity byte, as `GMouse.Encode` does.
- No onboard profiles. Saving is the 0x8070 persistence byte: send the effect with 1 (RAM and EEPROM). Only on Save, never on each preview, because EEPROM wears.

## Probably the same family

From what's known about these models, not checked:

- G513, G413 (single colour), G213 (zones), G910 and G810 may also use 0x8070 and 0x8080. The G213 and G910 may be older and use other features. Only a report tells.
- The G815 is the wired G915 and likely uses 0x8071 like it. If its report shows 0x8071 and 0x8040, `IsG915` should match it (wired, index 0xFF) once its name is accepted.

## Effects

0x8070 effect ids are the same as on the G915 (see the table in [lighting-control.md](lighting-control.md)). Each zone lists its own with fn 2. Parameter layouts known in G-Lite:

| Id | Effect | Layout |
|---|---|---|
| 0x01 | Static | Colour 0–2 |
| 0x0A | Breathe | Colour 0–2, period 3–4, intensity 6 |
| 0x03 | Cycle | Period 5–6, intensity 7 |

Wave (0x04), Starlight (0x05) and Ripple (0x0B) on 0x8070 aren't in OpenLogi. Start with the three above. Wave and Ripple may follow the G915's 0x8071 layouts in [G915.md](G915.md), to check on a real keyboard.

## Zones

fn 1 gives each zone's location: 1 primary (the keys), 2 logo, 6–11 primary 1 to 6 (a G213's five zones, say). A first version can set every zone to the same effect, as `GMouse.SetLight` does for a mouse's zones.

## Code

| File | Change |
|---|---|
| `app/Devices.cs` | `Found.IsLedKeyboard`: kind keyboard, has 0x8070, no 0x8071. |
| `app/Mouse.cs` | Move the 0x8070 parts of `GMouse` (zones, `Encode`, `Decode`, `SetLight`) into a small `Led` class both use. Add the persistence byte. |
| `app/PageKeyboard.cs` | Effect, colour, speed, brightness, the key picture, Save to keyboard. Smaller than `Page515`: no 0x8101 files, no idle settings, no backups (nothing is overwritten that G-Lite can't set again). |
| `app/models/g512.json` | The full-size layout. |
| `app/Fake.cs` | `FakeLedKeyboard` for the demo and self-tests. |
| `app/Program.cs` | Open the page for `IsLedKeyboard`. |

## Reading the lighting

Without onboard profiles, G-Lite needs another way to show what's set:

- fn 14 `<zone> 01 00` reads the effect stored in EEPROM, if extended capability bit 0 is set.
- Otherwise fn 9 `<zone> 01 00` reads the colour, period and brightness, unless extended capability bit 1 is set.
- Otherwise remember it in `settings.txt`, as the G915 does.

## Order

1. Ask for a G512 or G513 report. The report already reads 0x8070 fn 0–2. Add fn 9 and fn 14 with source 1 to `Reads` (they only read).
2. Share the 0x8070 code between mouse and keyboard.
3. Build the page against the fake, live only.
4. On a real keyboard: confirm persistence 1 survives a power cycle, then add Save.
