# Local LGS manifest inventory

Read-only extraction on 2026-11-08 from the installed Logitech Gaming Software `Resources/**/Device_Manifest.xml`. These proprietary files are **not copied** into this repository. The table records model names, distinct VID/PID pairs and HID probe selectors only. It does not establish HID++ version, working Windows access, or onboard profile format.

This supplements [existing LGS notes](../lgs.md). Hash-named resource folders are included because model names can reveal additional devices.

| Resource folder | Manifest model | VID:PID pairs | HID usage page / usage |
|---|---|---|---|
| `G100s` | Logitech.Gaming.Mouse.G100 | 046D:C247 | no HID probe extracted |
| `G100s` | Logitech.Gaming.Mouse.G100s | 046D:C247 | no HID probe extracted |
| `G100s` | Logitech.Gaming.Mouse.G90 | 046D:C247 | no HID probe extracted |
| `G100s` | Logitech.Gaming.Mouse.Black.G90 | 046D:C247 | no HID probe extracted |
| `G100s` | Logitech.Gaming.Mouse.White.G90 | 046D:C247 | no HID probe extracted |
| `G103` | Logitech.Gaming.Keyboard.G103 | 046D:C24B | 0xff00/(unspecified) |
| `G105` | Logitech.Gaming.Keyboard.G105 | 046D:C248 | 0xff00/(unspecified) |
| `G105` | Logitech.Gaming.Keyboard.G105COD | 046D:C248 | 0xff00/(unspecified) |
| `G11` | Logitech.Gaming.Keyboard.G11 | 046D:C225 | 0xff00/(unspecified) |
| `G110` | Logitech.Gaming.Keyboard.G110 | 046D:C22B | 0xff00/(unspecified) |
| `G13` | Logitech.Gaming.LeftHandedController.G13 | 046D:C21C | 0xff00/(unspecified) |
| `G15` | Logitech.Gaming.Keyboard.G15.v1 | 046D:C222 | 0xff00/(unspecified) |
| `G15` | Logitech.Gaming.Keyboard.G15.v2 | 046D:C227 | 0xff00/(unspecified) |
| `G19s` | Logitech.Gaming.Keyboard.G19 | 046D:C229 | 0xff00/(unspecified) |
| `G19s` | Logitech.Gaming.Keyboard.G19s | 046D:C229 | 0xff00/(unspecified) |
| `G29` | Logitech.Gaming.Wheel.G29 | 046D:C24F | no HID probe extracted |
| `G300s` | Logitech.Gaming.Mouse.G300 | 046D:C246 | 0xFF80/(unspecified) |
| `G300s` | Logitech.Gaming.Mouse.G300s | 046D:C246 | 0xFF80/(unspecified) |
| `G302` | Logitech.Gaming.Mouse.G302 | 046D:C07E, 046D:C07F | 0xFF00/1, 0xFF00/2 |
| `G303` | Logitech.Gaming.Mouse.G303.Daedalus.Apex | 046D:C080 | 0xFF00/1, 0xFF00/2 |
| `G35` | Logitech.Gaming.Headset.G35 | 046D:0A15 | 0xc/0x1 |
| `G400s` | Logitech.Gaming.Mouse.G400 | 046D:C245 | 0xFF80/(unspecified) |
| `G400s` | Logitech.Gaming.Mouse.G400s | 046D:C24C | 0xFF80/(unspecified) |
| `G402` | Logitech.Gaming.Mouse.G402 | 046D:C07E | 0xFF00/1, 0xFF00/2 |
| `G403W` | Logitech.Gaming.Mouse.G403 | 046D:C083 | 0xFF00/1, 0xFF00/2 |
| `G430` | Logitech.Gaming.Headset.G430 | 046D:0A4D | unspecified |
| `G500s` | Logitech.Gaming.Mouse.G500 | 046D:C068 | 0xFF00/1, 0xFF00/2 |
| `G500s` | Logitech.Gaming.Mouse.G500s | 046D:C24E | 0xFF00/1, 0xFF00/2 |
| `G502` | Logitech.Gaming.Mouse.G502 | 046D:C07D | 0xFF00/1, 0xFF00/2 |
| `G502RGB` | Logitech.Gaming.Mouse.G502RGB | 046D:C332 | 0xFF00/1, 0xFF00/2 |
| `G510s` | Logitech.Gaming.Keyboard.G510 | 046D:C22D, 046D:C22E | 0xff00/(unspecified) |
| `G510s` | Logitech.Gaming.Keyboard.G510s | 046D:C22D, 046D:C22E | 0xff00/(unspecified) |
| `G600` | Logitech.Gaming.Mouse.G600 | 046D:C24A | 0xFF80/(unspecified) |
| `G600` | Logitech.Gaming.Mouse.G600 | 046D:C24A | 0xFF80/(unspecified) |
| `G602` | Logitech.Gaming.Mouse.G602 | 046D:C537 | 0xFF00/1, 0xFF00/2, 0xFF80/(unspecified) |
| `G633` | Logitech.Gaming.Spectrum.Headset.G633 | 046D:0A5C | 0xb/0x1 |
| `G700s` | Logitech.Gaming.Mouse.G700 | 046D:C531 | 0xFF00/1, 0xFF00/2 |
| `G700s` | Logitech.Gaming.Mouse.G700 | 046D:C06B | 0xFF00/1, 0xFF00/2 |
| `G700s` | Logitech.Gaming.Mouse.G700s | 046D:C531 | 0xFF00/1, 0xFF00/2 |
| `G700s` | Logitech.Gaming.Mouse.G700s | 046D:C07C | 0xFF00/1, 0xFF00/2 |
| `G710` | Logitech.Gaming.Keyboard.G710 | 046D:C24D | 0xff00/(unspecified) |
| `G900` | Logitech.Gaming.Mouse.G900 | 046D:C539 | 0xFF00/1, 0xFF00/2 |
| `G910` | Logitech.Gaming.Keyboard.G910 | 046D:C32B | 0xFF00/(unspecified) |
| `G920` | Logitech.Gaming.Wheel.G920 | 046D:C262 | no HID probe extracted |
| `G930` | Logitech.Gaming.Headset.G930 | 046D:0A1F | 0x000C/(unspecified), 0xff00/(unspecified) |
| `G933` | Logitech.Gaming.Spectrum.Headset.G933 | 046D:0A5B | 0xb/0x1 |
| `G9X` | Logitech.Gaming.Mouse.G9 | 046D:C048 | 0xFF00/1, 0xFF00/2 |
| `G9X` | Logitech.Gaming.Mouse.G9XCOD | 046D:C249 | 0xFF00/1, 0xFF00/2 |
| `G9X` | Logitech.Gaming.Mouse.G9X | 046D:C066 | 0xFF00/1, 0xFF00/2 |
| `MX518` | Logitech.Gaming.Mouse.MX518 | 046D:C051 | no HID probe extracted |
| `MX518` | Logitech.Gaming.Mouse.MX518_1600 | 046D:C01E | no HID probe extracted |

51 physical-device manifest model entries. Multiple entries can describe aliases or revisions; do not count them as 51 distinct retail devices. The Arx virtual device and LCD emulators have nonstandard pseudo-VID/PIDs and were excluded. No settings, profiles or identifiers from the user's own devices were read.

The G302 manifest contains a C07E probe as well as C07F. C07E is independently identified as G402 by libratbag and the G402 manifest. Treat the extra G302 probe as an upstream collision, not proof of a second G302 PID.
