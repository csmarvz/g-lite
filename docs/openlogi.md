# OpenLogi

What G-Lite can take from [OpenLogi](https://github.com/aprilnea/openlogi), and where it's written up.

- A Rust replacement for Logitech Options+ (buttons, DPI, SmartShift, Flow), for MX devices first. Created in May 2026.
- Dual-licensed MIT and Apache 2.0, so its code could be reused, unlike Solaar and OpenRGB (GPL). We still write our own: G-Lite is C# 5 and its code is much smaller.
- Cloned at `C:\Users\csmar\dev\openlogi`, commit `8bf29cb` (8 Nov 2026). Pull to update.
- Many of its facts come from static analysis of Logi Options+ 2.8 or from Logitech's 0x8071 v4 spec (linked in `crates/openlogi-hidpp/src/feature/rgb_effects.rs`). They are not all checked on hardware. Where OpenLogi says a fact is unchecked, these docs say so too.

## The docs

| Doc | For | What |
|---|---|---|
| [lighting-control.md](lighting-control.md) | G515, G915, mice | The persistence byte of 0x8070 and 0x8071, software control, power modes |
| [report-rate-8061.md](report-rate-8061.md) | Newer G mice | Report rates up to 8000 Hz |
| [keyboards-8070.md](keyboards-8070.md) | G512, G513, G413, G815 | Older G keyboards that light up through 0x8070 |
| [G915.md](G915.md) | G915, G915 TKL | Updated with the facts above |

## Where to look in OpenLogi

| Path | What |
|---|---|
| `crates/openlogi-hidpp/src/feature/` | One file per HID++ feature: functions, argument and reply bytes |
| `crates/openlogi-hidpp/src/feature/rgb_effects.rs`, `rgb_effects/types.rs` | 0x8071 |
| `crates/openlogi-hidpp/src/feature/color_led_effects.rs`, `color_led_effects/types.rs` | 0x8070 |
| `crates/openlogi-hidpp/src/feature/extended_report_rate.rs` | 0x8061 |
| `crates/openlogi-hidpp/src/feature/onboard_profiles.rs` | 0x8100, read only |
| `crates/openlogi-hidpp/src/feature/per_key_lighting.rs` | 0x8080 and 0x8081 |
| `crates/openlogi-device/src/write/lighting/rgb.rs` | How they claim 0x8071, set a colour and hand control back |
| `fixtures/devices/<device>/cases/*.json` | Recorded request and reply bytes from real devices |

Recorded devices: G512, G915, and MX devices (Anywhere 3S, Creative Keypad, Ergo, Keys S, Master 2S, 3, 3S, 4, Mechanical Mini). No G mouse and no headset.

## Small facts that don't need their own doc

### Onboard profiles (0x8100)

OpenLogi only reads profiles, checked on a G502 LIGHTSPEED. It agrees with `GMouse` and adds:

- fn 0 reply: memory model at 0, profile format at 1, macro format at 2, profile count at 3, factory profile count at 4, button count at 5, sector count at 6, sector size at 7–8 (big endian). The G502 LIGHTSPEED answers `01 03 01 05 01 0B 10 00 FF`: format 3, five profiles, 11 buttons, sector size 255.
- The directory (sector 0x0000, or 0x0100 if that's blank) is 4 bytes per entry: sector (big endian), enabled, reserved. 0xFFFF ends it.
- The name is at 0xA0, 48 bytes of UTF-16, in profile formats 2 to 5. G HUB writes `PROFILE_NAME_DEFAULT` into an unnamed profile.
- fn 1 sets the mode until the next power cycle: 1 onboard, 2 host.
- fn 3 makes a profile current: `00 <index> 00`, the index 1-based.

So the name and the LED records may sit at the same place in formats 2, 4 and 5 too. `GMouse` only takes format 3. Accept another format only after a report shows its sector.

### Battery (0x1004)

G-Lite already reads it. OpenLogi adds that fn 1's byte 3 is external power, byte 4 fast charging (version 4 and up) and byte 5 a removable battery (version 5 and up). Not needed.

### Headsets

OpenLogi only recognises the G735 (dongle 046D:0AD8) by its USB id. Its notes say that dongle speaks its own protocol on usage page 0xFF00, not HID++, and that nobody has documented it. Nothing for G-Lite.

## Report

The report already reads 0x8060, 0x8070 fn 0–2 and 0x8071 fn 0 per effect. To make reports answer the questions in these docs, add:

| Feature | Call | Gives |
|---|---|---|
| 0x8071 | fn 0 `FF FF 00` | Cluster count, capabilities |
| 0x8071 | fn 0 `<cluster> FF 00` | Cluster location, effect count, whether effects can be stored |
| 0x8071 | fn 5 `00 00 00` | Software control and event flags (fn 5 with first byte 0 only reads) |
| 0x8071 | fn 7 `00 00 00` | Power-save and off timeouts (version 4 only, see lighting-control.md) |
| 0x8061 | fn 0 `00`, fn 0 `01`, fn 1, fn 2 `00`, fn 2 `01` | Supported and current report rates |

`Report.Reads` lists functions, not arguments. fn 5 and fn 7 of 0x8071 also write when the first byte is 1. Before adding them, let `Reads` also check the first argument, so `Report` can't send a set.
