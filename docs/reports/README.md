# Device reports

Reports from G-Lite's Report button, kept as they were sent, so the facts in the other docs can be checked against the bytes. A report leaves out the unit ID (the serial), the friendly name and the device path. Only the latest report of each device is kept. Reports from before 0.0.31 separate values with a middle dot, as G-Lite did then.

| File | Device | Firmware | Date | Used in |
|---|---|---|---|---|
| [g515-40b4.txt](g515-40b4.txt) | G515 LIGHTSPEED TKL (40B4, USB C355), Swiss layout, through its receiver | MPK 25.02, build 0013 | 2026-11-21 | `G515Profile` in `app/KeyboardLighting.cs` |
| [g915tkl-408e.txt](g915tkl-408e.txt) | G915 TKL (408E, USB C343), Swiss layout, through its receiver | MPK 14.02, build 0022 | 2026-11-17 | [G815-G915.md](../G815-G915.md) |
| [g815-c33f.txt](g815-c33f.txt) | G815 (USB C33F), Swiss layout | U1 31.02, build 0018 | 2026-11-21 | [G815-G915.md](../G815-G915.md) |
| [g512x75-c35f.txt](g512x75-c35f.txt) | G512 X 75 (USB C35F) | U1 81.05, build 0020 | 2026-11-11 | [G512X.md](../G512X.md) |
| [g413-c33a.txt](g413-c33a.txt) | G413 Silver (USB C33A), Swiss layout | U1 14.00, build 0008 | 2026-11-22 | [G413.md](../G413.md) |
| [g213-c336.txt](g213-c336.txt) | G213 (USB C336) | U1 09.00, build 0006 | 2026-11-15 | [G213.md](../G213.md) |
| [g510s-c22d.txt](g510s-c22d.txt) | G510s (USB C22D), no HID++ | | 2026-11-14 | [G510.md](../G510.md) |
| [gprowireless-4079.txt](gprowireless-4079.txt) | G Pro Wireless (4079, USB C088), through its receiver | MPM 15.02, build 0026 | 2026-11-21 | `MouseProtocol` in `app/MouseProtocol.cs` |
| [g502lightspeed-407f.txt](g502lightspeed-407f.txt) | G502 LIGHTSPEED (407F, USB C08D), through its receiver | MPM 17.00, build 0008 | 2026-11-10 | `Bindings` and `ButtonSpy` in `app/MouseButtons.cs` |
| [g305-4074.txt](g305-4074.txt) | G305 (4074), through its receiver | RQM 68.02, build 0021 | 2026-11-19 | `FakeMouse.G305` in `app/Fake.cs`, [adding-a-device.md](../adding-a-device.md), [G305.md](../G305.md) |
| [proxwireless-0aba.txt](proxwireless-0aba.txt) | PRO X Wireless headset, through its dongle (USB 0ABA) | U1 12.01, build 0203 | 2026-11-18 | `HeadsetProtocol` in `app/HeadsetProtocol.cs` |
| [g560-0a78.txt](g560-0a78.txt) | G560 speakers (USB 0A78) | U1 22.03, build 0023 | 2026-11-11 | [G560.md](../G560.md) |
| [k295-4023.txt](k295-4023.txt) | K270/K295 "Wireless Keyboard" (4023), through its receiver | RQK 49.00, build 0029 | 2026-11-19 | [K295.md](../K295.md) |
| [mxkeyss-b378.txt](mxkeyss-b378.txt) | MX Keys S (Bluetooth B378), Swiss layout | RBK 81.02, build 0017 | 2026-11-17 | [other-devices.md](../other-devices.md) |
| [mxmechanicalmini-b367.txt](mxmechanicalmini-b367.txt) | MX Mechanical Mini (Bluetooth B367), Swiss layout, after Logi Options+ updated its firmware | RBK 72.03, build 0014 | 2026-11-26 | [other-devices.md](../other-devices.md) |
| [mxcreativekeypad-c354.txt](mxcreativekeypad-c354.txt) | MX Creative Keypad (USB C354) | U1 66.00, build 0017 | 2026-11-12 | [other-devices.md](../other-devices.md) |
| [g502x-409f.txt](g502x-409f.txt) | G502 X LIGHTSPEED (409F, USB C098), through its receiver | MPM 30.00, build 0014 | 2026-11-19 | [G502X.md](../G502X.md) |
