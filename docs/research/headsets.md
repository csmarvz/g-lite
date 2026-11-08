# Headset and ASTRO device map

Inspected 2026-11-08. Source: HeadsetControl at the [pinned snapshot](snapshots.md), plus Solaar and OpenRGB. This is upstream implementation evidence, not testing by G-Lite. HeadsetControl is GPL-3.0: use protocol facts only under this repository's rules.

## Concrete device declarations

Rows are individual implementations. Some implementations group retail products and declare capabilities at family level; a wired headset may inherit a battery capability with no physical battery. The interface selector may also vary by capability. Inspect the linked file before assigning every capability to every PID.

| Implementation | Declared product IDs | Declared capabilities | Interface selectors in source |
|---|---|---|---|
| [Logitech ASTRO A50 Gen 5](https://github.com/Sapd/HeadsetControl/blob/b2b66602755314e25ac8e97e2f7edd551bd89d85/lib/devices/logitech_astro_a50.hpp) | 0x0b1c | battery status, chatmix status, sidetone, lights, parametric equalizer, noise filter, headset volume | .usagepage = 0xff32, .usageid = 0x0074, .interface_id = 8  |
| [Logitech ASTRO A50 Gen 4](https://github.com/Sapd/HeadsetControl/blob/b2b66602755314e25ac8e97e2f7edd551bd89d85/lib/devices/logitech_astro_a50_gen4.hpp) | 0x002c | battery status, chatmix status, sidetone, sidetone status, microphone volume, noise filter, equalizer preset, equalizer, parametric equalizer | .usagepage = 0xff32, .usageid = 0x0074, .interface_id = 6  |
| [Logitech G431/G432/G433](https://github.com/Sapd/HeadsetControl/blob/b2b66602755314e25ac8e97e2f7edd551bd89d85/lib/devices/logitech_g432.hpp) | 0x0a9b, 0x0a9c, 0x0a6d | sidetone | inherited / see source |
| [Logitech G522 LIGHTSPEED](https://github.com/Sapd/HeadsetControl/blob/b2b66602755314e25ac8e97e2f7edd551bd89d85/lib/devices/logitech_g522_lightspeed.hpp) | 0x0b18 | sidetone, battery status, lights, inactive time, voice prompts, equalizer preset, equalizer, parametric equalizer, microphone mute led brightness, sidetone status | .usagepage = 0xffa0, .usageid = 0x0001, .interface_id = 3  |
| [Logitech G533](https://github.com/Sapd/HeadsetControl/blob/b2b66602755314e25ac8e97e2f7edd551bd89d85/lib/devices/logitech_g533.hpp) | 0x0a66 | sidetone, battery status, inactive time | .usagepage = 0xff00, .usageid = 0x1, .interface_id = 3 ; .usagepage = 0xff43, .usageid = 0x202, .interface_id = 3 ; .usagepage = 0xff43, .usageid = 0x0202, .interface_id = 0  |
| [Logitech G535](https://github.com/Sapd/HeadsetControl/blob/b2b66602755314e25ac8e97e2f7edd551bd89d85/lib/devices/logitech_g535.hpp) | 0x0ac4 | sidetone, battery status, inactive time | .usagepage = 0xc, .usageid = 0x1, .interface_id = 3  |
| [Logitech G633/G635/G733/G933/G935](https://github.com/Sapd/HeadsetControl/blob/b2b66602755314e25ac8e97e2f7edd551bd89d85/lib/devices/logitech_g633_g933_935.hpp) | 0x0a5c, 0x0a89, 0x0a5b, 0x0a87, 0x0ab5, 0x0afe, 0x0b1f | sidetone, battery status, lights | .usagepage = 0xff43, .usageid = 0x0202  |
| [Logitech G930](https://github.com/Sapd/HeadsetControl/blob/b2b66602755314e25ac8e97e2f7edd551bd89d85/lib/devices/logitech_g930.hpp) | 0x0a1f | sidetone, battery status | .usagepage = 0xff00, .usageid = 0x1  |
| [Logitech G PRO Series](https://github.com/Sapd/HeadsetControl/blob/b2b66602755314e25ac8e97e2f7edd551bd89d85/lib/devices/logitech_gpro.hpp) | 0x0aa7, 0x0aaa, 0x0aba, 0x0afb, 0x0afc | sidetone, battery status, inactive time | .usagepage = 0xff43, .usageid = 0x0202  |
| [Logitech G PRO X 2 LIGHTSPEED](https://github.com/Sapd/HeadsetControl/blob/b2b66602755314e25ac8e97e2f7edd551bd89d85/lib/devices/logitech_gpro_x2_lightspeed.hpp) | 0x0af7 | sidetone, battery status, inactive time, equalizer preset, equalizer | .usagepage = 0xffa0, .usageid = 0x0001, .interface_id = 3  |

## Transport boundaries

- **Classic HID++**: G533, G535, G633/G635/G733/G933/G935 and the PRO family variants in the table. Discover the feature indexes rather than transplant the hardcoded indexes from HeadsetControl. G-Lite already does runtime resolution.
- **G930**: a separate legacy implementation over page FF00/usage 1; it cannot be routed solely by G-Lite's current headset feature predicate.
- **Centurion PRO X 2, PID 0AF7**: 64-byte frames on FFA0/usage 1, prefix 51. HeadsetControl implements battery, sidetone, auto-off and EQ. Keep separate from 0AFB/0AFC HID++ variants.
- **Centurion G522, PID 0B18 receiver**: prefix 50 23; dynamic feature discovery through the bridge. OpenRGB and Solaar additionally identify 0B19 wired. HeadsetControl's declaration only lists 0B18, so its capabilities are not independent proof for 0B19.
- **ASTRO A50 Gen 5, 046D:0B1C**: report 02, 64 bytes, FF32/0074, interface 8. Not HID++ and not Centurion. Source documents battery, game/chat mix, sidetone, dock LED brightness, hardware volume, noise gate and parametric EQ as hardware-verified.
- **ASTRO A50 Gen 4**: PID 002C is under **ASTRO VID 9886**, not Logitech 046D; check the implementation's getVendorId. FF32/0074 interface 6. This can never pass G-Lite's current 046D-only discovery.

The common word LIGHTSPEED does not establish the same control transport. Name filters that accept ASTRO/A50 are insufficient when discovery rejects the device's VID, usage page or report length.

## High-value protocol files

- [HeadsetControl Centurion transport](https://github.com/Sapd/HeadsetControl/blob/b2b66602755314e25ac8e97e2f7edd551bd89d85/lib/devices/protocols/logitech_centurion_protocol.hpp): framing, transactions, discovery and battery handling.
- [Solaar Centurion](https://github.com/pwr-Solaar/Solaar/blob/e7304c4c451cc9bb4f206a914844525e67856a28/lib/logitech_receiver/centurion.py) and [namespace-specific feature IDs](https://github.com/pwr-Solaar/Solaar/blob/e7304c4c451cc9bb4f206a914844525e67856a28/lib/logitech_receiver/centurion_constants.py): bridge routing, discovery, transport separation.
- [HeadsetControl G522](https://github.com/Sapd/HeadsetControl/blob/b2b66602755314e25ac8e97e2f7edd551bd89d85/lib/devices/logitech_g522_lightspeed.hpp): battery, sidetone, RGB, idle timeout, voice prompts, microphone mute LED and 10-band parametric EQ. Its onboard playback EQ uses feature 020D; lighting uses 0621. These are **Centurion IDs**, not HID++ 2.0 equivalents.
- [HeadsetControl ASTRO A50 Gen 5](https://github.com/Sapd/HeadsetControl/blob/b2b66602755314e25ac8e97e2f7edd551bd89d85/lib/devices/logitech_astro_a50.hpp): compact command dictionary plus verified selectors.
- [HeadsetControl ASTRO extra features](https://github.com/Sapd/HeadsetControl/blob/b2b66602755314e25ac8e97e2f7edd551bd89d85/docs/ASTRO_A50_EXTRA_FEATURES.md): additional device controls and limitations.
- [ashkitten/g933-utils](https://github.com/ashkitten/g933-utils): earlier G933 reverse engineering; HeadsetControl credits it. Inspect README and scripts for battery, lighting and button events.
- [Solaar G733, G935 and G933 feature captures](solaar-feature-evidence.md): strongest source here for which HID++ features are actually present.
- [LGSTrayEx Centurion implementation](https://github.com/strain08/LGSTrayEx/tree/d43905bd911e30bbfa1a030f6d5eb617f08874ec/LGSTrayHID/Centurion): Windows-oriented telemetry transport, useful for report handling and endpoint discovery.

## What this changes in G-Lite's map

1. Use separate family keys for PRO X wireless, PRO X 2 HID++ and PRO X 2 Centurion. Do not collapse them by display name.
2. G733 has at least three implementation IDs: 0AB5, 0AFE and 0B1F; retain all, with firmware/variant unknown until captured.
3. Preserve the existing PRO X hardware finding: stored 8310 EQ did not audibly apply on the tested unit; G HUB did EQ on the PC. G522's hardware EQ does not overturn that finding because it is a different transport and implementation.
4. Record sidetone's microphone-stream prerequisite from the existing repository notes. A successful set alone is not an audible-effect test.
5. Battery-voltage percentages from a generic straight-line formula are estimates. HeadsetControl has family-specific calibration data; do not relabel estimates as reported percentages.
6. G735 0AD8 remains a research gap: [OpenLogi #1675](https://github.com/AprilNEA/OpenLogi/issues/1675) starts with the enumeration problem and explicitly asks whether the dongle is HID++. The sibling OpenLogi notes record a proprietary FF00 route. Neither establishes a reusable G-Lite implementation.

## Audio models without a verified control route here

G35 0A15, G430 0A4D (local LGS manifests); G435, G321/G325, G335, G331/G332, G230/G231, G333, G432 analog-only use, ASTRO A10/A20/A20 X/A30/A40/MixAmp/A50 X. These names are backlog entries, not support claims. Obtain the exact USB dongle/interface where applicable; analog headsets cannot be configured over USB without a controllable adapter. For A20 X, [HeadsetControl issue search](https://github.com/Sapd/HeadsetControl/issues?q=A20+X) includes a request labeled 046D:0B34, which is a lead only.
