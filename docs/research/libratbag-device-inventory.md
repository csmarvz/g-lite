# libratbag device inventory

Extracted from the inspected MIT-licensed checkout on 2026-11-08; see [snapshot](snapshots.md). A driver declaration is upstream evidence, not a G-Lite hardware test. All identifiers below are verbatim **match selectors**; a `usb:046d:40xx` selector can identify a Linux receiver child using a wireless product ID, rather than a physical USB product. Do not turn these into a Windows USB allowlist without separating transport IDs. DeviceIndex overrides belong to this driver and need independent checking on Windows.

| Device / upstream file | Match selectors | Driver | Declared type | Overrides / quirks |
|---|---|---|---|---|
| [Logitech G Powerplay ](https://github.com/libratbag/libratbag/blob/8235b5bb6032dea15901ab58a8218956f4494d08/data/devices/logitech-g-powerplay.device) | `usb:046d:405f` | `hidpp20` | other | deviceindex=7 |
| [Logitech G PRO Keyboard ](https://github.com/libratbag/libratbag/blob/8235b5bb6032dea15901ab58a8218956f4494d08/data/devices/logitech-g-pro-keyboard.device) | `usb:046d:c339` | `hidpp20` | keyboard | — |
| [Logitech G Pro Wireless ](https://github.com/libratbag/libratbag/blob/8235b5bb6032dea15901ab58a8218956f4494d08/data/devices/logitech-g-pro-wireless.device) | `usb:046d:4079;usb:046d:c088` | `hidpp20` | mouse | deviceindex=1 |
| [Logitech G Pro X Wireless Superlight ](https://github.com/libratbag/libratbag/blob/8235b5bb6032dea15901ab58a8218956f4494d08/data/devices/logitech-g-pro-x-wireless-superlight.device) | `usb:046d:4093;usb:046d:c094` | `hidpp20` | mouse | — |
| [Logitech Gaming Mouse G Pro ](https://github.com/libratbag/libratbag/blob/8235b5bb6032dea15901ab58a8218956f4494d08/data/devices/logitech-g-pro.device) | `usb:046d:c085;usb:046d:c08c` | `hidpp20` | mouse | — |
| [Logitech Gaming Mouse G102/G103/G203 ](https://github.com/libratbag/libratbag/blob/8235b5bb6032dea15901ab58a8218956f4494d08/data/devices/logitech-g102-g203.device) | `usb:046d:c084;usb:046d:c092;usb:046d:c09d` | `hidpp20` | mouse | — |
| [Logitech G300 ](https://github.com/libratbag/libratbag/blob/8235b5bb6032dea15901ab58a8218956f4494d08/data/devices/logitech-g300.device) | `usb:046d:c246` | `logitech_g300` | mouse | upstream says converted / not vetted |
| [Logitech Gaming Mouse G302 ](https://github.com/libratbag/libratbag/blob/8235b5bb6032dea15901ab58a8218956f4494d08/data/devices/logitech-g302.device) | `usb:046d:c07f` | `hidpp20` | mouse | — |
| [Logitech G303 Shroud Edition ](https://github.com/libratbag/libratbag/blob/8235b5bb6032dea15901ab58a8218956f4494d08/data/devices/logitech-g303-shroud.device) | `usb:046d:c097` | `hidpp20` | mouse | — |
| [Logitech Gaming Mouse G303 ](https://github.com/libratbag/libratbag/blob/8235b5bb6032dea15901ab58a8218956f4494d08/data/devices/logitech-g303.device) | `usb:046d:c080` | `hidpp20` | mouse | — |
| [Logitech Gaming Mouse G305 ](https://github.com/libratbag/libratbag/blob/8235b5bb6032dea15901ab58a8218956f4494d08/data/devices/logitech-g305.device) | `usb:046d:4074` | `hidpp20` | mouse | leds=0; quirk=G305 |
| [Logitech G402 Gaming Mouse ](https://github.com/libratbag/libratbag/blob/8235b5bb6032dea15901ab58a8218956f4494d08/data/devices/logitech-g402.device) | `usb:046d:c07e` | `hidpp20` | mouse | — |
| [Logitech G403 Hero ](https://github.com/libratbag/libratbag/blob/8235b5bb6032dea15901ab58a8218956f4494d08/data/devices/logitech-g403-hero.device) | `usb:046d:c08f` | `hidpp20` | mouse | — |
| [Logitech G403 Wireless ](https://github.com/libratbag/libratbag/blob/8235b5bb6032dea15901ab58a8218956f4494d08/data/devices/logitech-g403-wireless.device) | `usb:046d:405d;usb:046d:c082` | `hidpp20` | mouse | — |
| [Logitech G403 ](https://github.com/libratbag/libratbag/blob/8235b5bb6032dea15901ab58a8218956f4494d08/data/devices/logitech-g403.device) | `usb:046d:c083` | `hidpp20` | mouse | — |
| [Logitech G413 ](https://github.com/libratbag/libratbag/blob/8235b5bb6032dea15901ab58a8218956f4494d08/data/devices/logitech-g413.device) | `usb:046d:c33a` | `hidpp20` | keyboard | — |
| [Logitech G5 ](https://github.com/libratbag/libratbag/blob/8235b5bb6032dea15901ab58a8218956f4494d08/data/devices/logitech-g5-2007.device) | `usb:046d:c049` | `hidpp10` | mouse | dpilist=400;800;1600;2000; profiles=1; profiletype=G500 |
| [G5 ](https://github.com/libratbag/libratbag/blob/8235b5bb6032dea15901ab58a8218956f4494d08/data/devices/logitech-g5.device) | `usb:046d:c041` | `hidpp10` | mouse | dpilist=400;800;1600;2000 |
| [Logitech G500 ](https://github.com/libratbag/libratbag/blob/8235b5bb6032dea15901ab58a8218956f4494d08/data/devices/logitech-g500.device) | `usb:046d:c068` | `hidpp10` | mouse | dpirange=0:5700@23.53; profiletype=G500; profiles=1 |
| [Logitech G500s ](https://github.com/libratbag/libratbag/blob/8235b5bb6032dea15901ab58a8218956f4494d08/data/devices/logitech-g500s.device) | `usb:046d:c24e` | `hidpp10` | mouse | dpirange=0:8200@50; profiletype=G500; profiles=1 |
| [Logitech G502 Hero Wireless ](https://github.com/libratbag/libratbag/blob/8235b5bb6032dea15901ab58a8218956f4494d08/data/devices/logitech-g502-hero-wireless.device) | `usb:046d:407f;usb:046d:c08d` | `hidpp20` | mouse | quirk=INDEX_OFFSET |
| [Logitech G502 Hero ](https://github.com/libratbag/libratbag/blob/8235b5bb6032dea15901ab58a8218956f4494d08/data/devices/logitech-g502-hero.device) | `usb:046d:c08b` | `hidpp20` | mouse | quirk=INDEX_OFFSET |
| [Logitech G502 Proteus Core ](https://github.com/libratbag/libratbag/blob/8235b5bb6032dea15901ab58a8218956f4494d08/data/devices/logitech-g502-proteus-core.device) | `usb:046d:c07d` | `hidpp20` | mouse | — |
| [Logitech G502 Proteus Spectrum ](https://github.com/libratbag/libratbag/blob/8235b5bb6032dea15901ab58a8218956f4494d08/data/devices/logitech-g502-proteus-spectrum.device) | `usb:046d:c332` | `hidpp20` | mouse | quirk=INDEX_OFFSET |
| [Logitech G502 X PLUS ](https://github.com/libratbag/libratbag/blob/8235b5bb6032dea15901ab58a8218956f4494d08/data/devices/logitech-g502-x-plus.device) | `usb:046d:4099;usb:046d:c095` | `hidpp20` | mouse | quirk=G502X_PLUS;INDEX_OFFSET |
| [Logitech G502 X Wireless ](https://github.com/libratbag/libratbag/blob/8235b5bb6032dea15901ab58a8218956f4494d08/data/devices/logitech-g502-x-wireless.device) | `usb:046d:c098;usb:046d:409f` | `hidpp20` | mouse | quirk=INDEX_OFFSET |
| [Logitech G502 X ](https://github.com/libratbag/libratbag/blob/8235b5bb6032dea15901ab58a8218956f4494d08/data/devices/logitech-g502-x.device) | `usb:046d:c099` | `hidpp20` | mouse | quirk=INDEX_OFFSET |
| [Logitech G513 ](https://github.com/libratbag/libratbag/blob/8235b5bb6032dea15901ab58a8218956f4494d08/data/devices/logitech-g513.device) | `usb:046d:c33c` | `hidpp20` | keyboard | — |
| [Logitech G600 ](https://github.com/libratbag/libratbag/blob/8235b5bb6032dea15901ab58a8218956f4494d08/data/devices/logitech-g600.device) | `usb:046d:c24a` | `logitech_g600` | mouse | upstream says converted / not vetted |
| [Logitech G602 ](https://github.com/libratbag/libratbag/blob/8235b5bb6032dea15901ab58a8218956f4494d08/data/devices/logitech-g602.device) | `usb:046d:402c` | `hidpp20` | mouse | deviceindex=1; quirk=G602 |
| [Logitech G603 ](https://github.com/libratbag/libratbag/blob/8235b5bb6032dea15901ab58a8218956f4494d08/data/devices/logitech-g603.device) | `usb:046d:406c;bluetooth:046d:b01c` | `hidpp20` | mouse | — |
| [Logitech G604 ](https://github.com/libratbag/libratbag/blob/8235b5bb6032dea15901ab58a8218956f4494d08/data/devices/logitech-g604.device) | `usb:046d:4085;bluetooth:046d:b024` | `hidpp20` | mouse | quirk=INDEX_OFFSET |
| [Logitech G635 ](https://github.com/libratbag/libratbag/blob/8235b5bb6032dea15901ab58a8218956f4494d08/data/devices/logitech-g635.device) | `usb:046d:0a89` | `hidpp20` | mouse | buttons=0 |
| [Logitech G7 ](https://github.com/libratbag/libratbag/blob/8235b5bb6032dea15901ab58a8218956f4494d08/data/devices/logitech-g7.device) | `usb:046d:c51a` | `hidpp10` | mouse | profiles=1 |
| [Logitech G700 ](https://github.com/libratbag/libratbag/blob/8235b5bb6032dea15901ab58a8218956f4494d08/data/devices/logitech-g700-wireless.device) | `usb:046d:c531` | `hidpp10` | mouse | dpirange=0:5700@23.53; profiletype=G700; deviceindex=1; profiles=5 |
| [Logitech G700 ](https://github.com/libratbag/libratbag/blob/8235b5bb6032dea15901ab58a8218956f4494d08/data/devices/logitech-g700.device) | `usb:046d:c06b` | `hidpp10` | mouse | dpirange=0:5700@23.53; profiletype=G700; profiles=5 |
| [Logitech G700s ](https://github.com/libratbag/libratbag/blob/8235b5bb6032dea15901ab58a8218956f4494d08/data/devices/logitech-g700s.device) | `usb:046d:c07c` | `hidpp10` | mouse | dpirange=0:8200@50; profiletype=G700; profiles=5 |
| [Logitech G703 Hero ](https://github.com/libratbag/libratbag/blob/8235b5bb6032dea15901ab58a8218956f4494d08/data/devices/logitech-g703-hero.device) | `usb:046d:4086;usb:046d:c090` | `hidpp20` | mouse | — |
| [Logitech G703 ](https://github.com/libratbag/libratbag/blob/8235b5bb6032dea15901ab58a8218956f4494d08/data/devices/logitech-g703.device) | `usb:046d:c087;usb:046d:4070` | `hidpp20` | mouse | — |
| [Logitech G705 ](https://github.com/libratbag/libratbag/blob/8235b5bb6032dea15901ab58a8218956f4494d08/data/devices/logitech-g705.device) | `usb:046d:c096;usb:046d:409d;bluetooth:046d:b02e` | `hidpp20` | mouse | quirk=G502X_PLUS |
| [Logitech G733 Gaming Headset ](https://github.com/libratbag/libratbag/blob/8235b5bb6032dea15901ab58a8218956f4494d08/data/devices/logitech-g733.device) | `usb:046d:0ab5;usb:046d:0afe` | `hidpp20` | mouse | buttons=0 |
| [Logitech G815 ](https://github.com/libratbag/libratbag/blob/8235b5bb6032dea15901ab58a8218956f4494d08/data/devices/logitech-g815.device) | `usb:046d:c33f` | `hidpp20` | keyboard | — |
| [Logitech G9 ](https://github.com/libratbag/libratbag/blob/8235b5bb6032dea15901ab58a8218956f4494d08/data/devices/logitech-g9.device) | `usb:046d:c048` | `hidpp10` | mouse | dpilist=200;400;600;800;1000;1200;1400;1600;1800;2000;2200;2400;2600;2800;3000;3200; profiletype=G9; profiles=5; leds=1 |
| [Logitech G900 ](https://github.com/libratbag/libratbag/blob/8235b5bb6032dea15901ab58a8218956f4494d08/data/devices/logitech-g900.device) | `usb:046d:c081;usb:046d:4053` | `hidpp20` | mouse | — |
| [Logitech G903 Hero ](https://github.com/libratbag/libratbag/blob/8235b5bb6032dea15901ab58a8218956f4494d08/data/devices/logitech-g903-hero.device) | `usb:046d:c091;usb:046d:4087` | `hidpp20` | mouse | — |
| [Logitech G903 ](https://github.com/libratbag/libratbag/blob/8235b5bb6032dea15901ab58a8218956f4494d08/data/devices/logitech-g903.device) | `usb:046d:c086;usb:046d:4067` | `hidpp20` | mouse | — |
| [Logitech G910 ](https://github.com/libratbag/libratbag/blob/8235b5bb6032dea15901ab58a8218956f4494d08/data/devices/logitech-g910.device) | `usb:046d:c335` | `hidpp20` | keyboard | deviceindex=ff |
| [Logitech G915 TKL ](https://github.com/libratbag/libratbag/blob/8235b5bb6032dea15901ab58a8218956f4494d08/data/devices/logitech-g915-tkl.device) | `usb:046d:c343;bluetooth:046d:b35f` | `hidpp20` | keyboard | deviceindex=1 |
| [Logitech G915 ](https://github.com/libratbag/libratbag/blob/8235b5bb6032dea15901ab58a8218956f4494d08/data/devices/logitech-g915.device) | `usb:046d:c33e` | `hidpp20` | keyboard | deviceindex=1 |
| [Logitech G935 ](https://github.com/libratbag/libratbag/blob/8235b5bb6032dea15901ab58a8218956f4494d08/data/devices/logitech-g935.device) | `usb:046d:0a87` | `hidpp20` | other | — |
| [Logitech G9x [Call of Duty MW3 Edition] ](https://github.com/libratbag/libratbag/blob/8235b5bb6032dea15901ab58a8218956f4494d08/data/devices/logitech-g9x-Call-of-Duty-MW3-Edition.device) | `usb:046d:c249` | `hidpp10` | mouse | dpirange=0:5700@23.53; profiletype=G500; profiles=5 |
| [G9x [Original] ](https://github.com/libratbag/libratbag/blob/8235b5bb6032dea15901ab58a8218956f4494d08/data/devices/logitech-g9x-Original.device) | `usb:046d:c066` | `hidpp10` | mouse | dpirange=200:5700@50; profiletype=G500; profiles=5; leds=1 |
| [MX518 ](https://github.com/libratbag/libratbag/blob/8235b5bb6032dea15901ab58a8218956f4494d08/data/devices/logitech-MX518.device) | `usb:046d:c08e` | `hidpp20` | mouse | — |

## Interpretation traps

- G635 and G733 are declared `mouse` in these files; this is driver plumbing, not the actual product category.
- G303 Shroud has only C097 in this snapshot; obtain its receiver/WPID separately.
- G915 / TKL contain device-index overrides; do not use the override as proof of the index on every connection.
- G9, G9x, G500, G700 and G5 use HID++ 1.0, with multiple profile layouts. G300 and G600 have separate drivers.
- G502 X PLUS has G502X_PLUS plus INDEX_OFFSET quirks. G705 shares the former. A family name alone does not establish matching LED storage.
- G9x Original and COD give different DPI metadata. Prefer the existing real-device [G9x notes](../G9x.md) for G-Lite behavior.
