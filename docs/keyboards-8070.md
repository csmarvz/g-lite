# Older G keyboards

The G512 and similar G keyboards light up through 0x8070, the feature `MouseProtocol` already uses, not through 0x8071 like the G515 and G915. Supporting them is mostly a new screen around code G-Lite has. From OpenLogi's G512 recording (see [openlogi.md](openlogi.md)) and [lighting-control.md](lighting-control.md).

Status: built on the G560's screen (`ZonesPage`, for `FoundDevice.IsLedKeyboard`: kind keyboard, 0x8070, no 0x8071). It sets Off, Static, Breathe, Cycle and Wave live on all zones or each one, greys out the effects a zone doesn't list, names zones from their location (`ZoneLighting.ZoneName`: 1 Keys, 2 Logo, 6–11 Zone 1–6, else Zone n), keeps the lighting as `lighting.<device>` and sets it again on load. A keyboard with a zone key map (`ZoneLighting.Picture`, only the G213 so far) gets a live key picture. No per-key colours, no Save.

Tested on a real G213 (see [G213.md](G213.md)): fn 3 without a persistence byte lights it, Wave works with OpenRGB's layout, and the persistence byte stores nothing. The G512 and the others are still untested. The plan below (its own page, a shared `Led` class) is kept for when one of them reports.

## Per-key colours (0x8080), not built

From Lghub (`perkey.py`, read from G HUB): fn 0 gives the key types the keyboard has as bits in its first two bytes (keyboard 1, consumer 2, G key 4, button 8, logo 0x10, other keys 0x40); fn 3 sets colours, `<type u16> <count u16>` then `<key> R G B` per key, up to 14 in a 64-byte report (3 in a 20-byte one); fn 5 `<persist>` shows them. The key byte is the HID usage for keyboard keys.

## The G512

OpenLogi's recording (`fixtures/devices/g512-001`): "G512 RGB MECHANICAL GAMING KEYBOARD", wired, USB 046D:C33C, HID++ at index 0xFF.

Features that matter:

| Feature | Version | Use |
|---|---|---|
| 0x8070 colour LED effects | 7 | Effects |
| 0x8080 per-key lighting | 2 | Not used |
| 0x8060 report rate | 0 | Could reuse `MouseProtocol` |
| 0x4540 keyboard layout | 0 | ANSI or ISO, later |

No 0x8071, 0x8040 (brightness), 0x8100 or 0x8101. So:

- No brightness feature. Brightness has to be in the effect: Static scales the colour, Breathe and Cycle take an intensity byte, as `MouseProtocol.Encode` does.
- No onboard profiles. Saving would be the 0x8070 persistence byte: send the effect with 1 (RAM and EEPROM). Only on Save, never on each preview, because EEPROM wears. The G213 accepts 1 but forgets it at the next power cycle, and lists persistency capabilities 0; check a keyboard's capabilities before offering Save.

## Probably the same family

From what's known about these models, not checked:

- G513, G413 (single colour), G910 and G810 may also use 0x8070 and 0x8080. Only a report tells.
- The G213 uses 0x8070 with six zones: the whole keyboard and five areas that take Static only. See [G213.md](G213.md).
- The G815 is the wired G915: its report shows 0x8071, 0x8040 and 0x8100 like the G915 TKL, and it gets the G915 screen. See [G915.md](G915.md).

## Effects

0x8070 effect ids are the same as on the G915 (see the table in [lighting-control.md](lighting-control.md)). Each zone lists its own with fn 2. Parameter layouts known in G-Lite:

| Id | Effect | Layout |
|---|---|---|
| 0x01 | Static | Colour 0–2 |
| 0x0A | Breathe | Colour 0–2, period 3–4, intensity 6 |
| 0x03 | Cycle | Period 5–6, intensity 7 |

Wave (0x04), Starlight (0x05) and Ripple (0x0B) on 0x8070 aren't in OpenLogi. Start with the three above. Wave and Ripple may follow the G915's 0x8071 layouts in [G915.md](G915.md), to check on a real keyboard.

## Zones

fn 1 gives each zone's location: 1 primary (the keys), 2 logo, 6–11 primary 1 to 6 (a G213's five zones, say). A first version can set every zone to the same effect, as `MouseProtocol.SetLight` does for a mouse's zones.

## Code

What was built (the first plan had its own page, `PageKeyboard`, and a shared `Led` class; the G560's screen fitted instead):

| File | What |
|---|---|
| `app/FoundDevice.cs` | `FoundDevice.IsLedKeyboard`: kind keyboard, has 0x8070, no 0x8071. |
| `app/DeviceTable.cs` | `IsLedKeyboard` opens `ZonesPage`, with `FakeSpeaker { Count = 2 }` in the demo. |
| `app/ZoneLighting.cs` | `ZoneLighting`: the zones and their effects (`Encode`, with Wave), `ZoneName` from the location, `Picture` for a keyboard with a zone key map. |
| `app/ZonesPage.cs` | The screen: effect, colour, speed and brightness on all zones or one, effects a zone doesn't list greyed out, the key picture when there is one. Live only, kept as `lighting.<device>`. |
| `app/models/g213.json` | The G213's picture, its `ids` are zones (`"kind": "zones"`). |

## Reading the lighting

Without onboard profiles, G-Lite needs another way to show what's set:

- fn 14 `<zone> 01 00` reads the effect stored in EEPROM, if extended capability bit 0 is set.
- Otherwise fn 9 `<zone> 01 00` reads the colour, period and brightness, unless extended capability bit 1 is set.
- Otherwise remember it in `settings.txt`, as the G915 does.

## Order

1. Ask for a G512 or G513 report. The report already reads 0x8070 fn 0–2. Add fn 9 and fn 14 with source 1 to `Reads` (they only read).
2. Share the 0x8070 code between mouse and keyboard.
3. Build the page against the fake, live only.
4. On a real keyboard: confirm persistence 1 survives a power cycle, then add Save. It doesn't on the G213.
