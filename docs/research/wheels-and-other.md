# Wheels, POWERPLAY, speakers and flight controls

Research date: 2026-11-08. These extend the Logitech G/LGS product map but require device classes beyond the present mouse/keyboard/headset pages. Source presence is not G-Lite support.

## Wheel identities and protocol split

Identifiers below come from [Oversteer's wheel_ids.py](https://github.com/berarma/oversteer/blob/74c748436a1dff2e42a340b581db0d0e4e0cafb2/oversteer/wheel_ids.py), local LGS manifests and [mescon's protocol specification](https://github.com/mescon/logitech-trueforce-linux-driver/blob/08b4d134e8d99d1c07df2f2f23e3614fddc19abd/docs/PROTOCOL_SPECIFICATION.md). See [pinned snapshots](snapshots.md).

| Product / mode | USB VID:PID | Strongest route |
|---|---|---|
| G25 | 046D:C299 | lg4ff / new-lg4ff |
| G27 | 046D:C29B | lg4ff / new-lg4ff |
| G29 | 046D:C24F | lg4ff / new-lg4ff; also in LGS |
| G920 | 046D:C262 | HID++ wheel force feedback; also in LGS |
| G923 PlayStation/PC | 046D:C266 | Separate from Xbox variant; new-lg4ff / newer TrueForce project |
| G923 Xbox/PC | 046D:C26E | HID++ wheel route / newer TrueForce project |
| PRO Racing PlayStation/PC | 046D:C268 | Direct-drive HID++ config plus dedicated FFB interface |
| PRO Racing Xbox/PC | 046D:C272 | Same broad class; dynamic feature discovery |
| RS50 PlayStation/PC | 046D:C276 | Direct-drive protocol and 64-byte config replies |
| Driving Force / Formula EX | 046D:C294 | Legacy compatibility identity; not necessarily the real model |
| Driving Force Pro | 046D:C298 | Legacy lg4ff |
| Driving Force GT | 046D:C29A | Legacy lg4ff |
| MOMO Racing / second variant | 046D:C295 / CA03 | Legacy lg4ff/Oversteer tables |
| Speed Force Wireless | 046D:C29C | Legacy lg4ff/Oversteer tables |
| WingMan Formula Force / GP | 046D:C291 / C293 | Legacy wheel tables |
| WingMan Formula GP | 046D:C20E | Legacy input table; check actual FFB capability |

Wheel mode changes can re-enumerate the same physical device under an older product's PID. Map native identity, compatibility identity and selected mode separately. A PRO PID can also represent an RS50 in PRO compatibility mode according to the TrueForce project's hardware documentation.

## Useful implementations

- [berarma/new-lg4ff](https://github.com/berarma/new-lg4ff): GPL-2.0-derived kernel driver for traditional Logitech wheels; range, force effects, pedals, LEDs, mode-switching and device quirks.
- [berarma/oversteer](https://github.com/berarma/oversteer): GPL-3.0 UI/model and wheel profile reference. Its Linux sysfs writes are not a Windows transport implementation.
- [Linux hid-lg4ff](https://github.com/torvalds/linux/blob/master/drivers/hid/hid-lg4ff.c), [hid-logitech-hidpp](https://github.com/torvalds/linux/blob/master/drivers/hid/hid-logitech-hidpp.c), and [hid-ids](https://github.com/torvalds/linux/blob/master/drivers/hid/hid-ids.h): primary upstream protocol/device references.
- [mescon/logitech-trueforce-linux-driver](https://github.com/mescon/logitech-trueforce-linux-driver): GPL-2.0 reference for RS50, PRO and G923, including [configuration protocol](https://github.com/mescon/logitech-trueforce-linux-driver/blob/08b4d134e8d99d1c07df2f2f23e3614fddc19abd/docs/PROTOCOL_SPECIFICATION.md) and [TrueForce protocol](https://github.com/mescon/logitech-trueforce-linux-driver/blob/08b4d134e8d99d1c07df2f2f23e3614fddc19abd/docs/TRUEFORCE_PROTOCOL.md). This is a community implementation; distinguish verified captures from its architectural claims.
- [Slyvan25/OpenGHub wheel documentation](https://github.com/Slyvan25/OpenGHub/blob/55822ca9e739c85e596217c110cc971b11386fba/docs/wheels.md): MIT application reference covering wheel discovery/configuration. Cross-check actual wire behavior against the focused driver projects before treating its breadth claims as verified coverage.
- [RustyRaptor/Logitech-FF-Linux](https://github.com/RustyRaptor/Logitech-FF-Linux): historical G29/G920 split, useful provenance; prefer current upstream code for new work.

The direct-drive project documents RS50 accepting a long request but replying with report **12 (64 bytes)**. Its PRO configuration includes subdevices at indexes 01/02/05. G-Lite's current 7/20-byte collection filter cannot be assumed to discover or read this traffic. Its report reader also cannot safely apply headset-style battery interpretation to arbitrary wheel features.

Map configuration (range, centering, gain, damping, pedal curves, profiles, LEDs) separately from real-time force feedback and TrueForce game integration. Implementing a settings panel does not replace Logitech's Windows wheel driver or proprietary SDK path.

## POWERPLAY and POWERPLAY 2

Original POWERPLAY has receiver USB **C53A** and the mat's wireless/virtual identity **405F**. [libratbag's definition](https://github.com/libratbag/libratbag/blob/8235b5bb6032dea15901ab58a8218956f4494d08/data/devices/logitech-g-powerplay.device) explicitly uses **device index 7**, while G-Lite's receiver loop probes only slots 1–6. The paired mouse and the mat are different endpoints; do not call 405F a physical USB receiver PID.

[OpenRGB POWERPLAY controller](https://github.com/CalcProgrammer1/OpenRGB/blob/029758e2260208c6de06eefc7106cd5c462c2310/Controllers/LogitechController/LogitechGLightsyncController/RGBController_LogitechGPowerPlay.cpp) and [detector](https://github.com/CalcProgrammer1/OpenRGB/blob/029758e2260208c6de06eefc7106cd5c462c2310/Controllers/LogitechController/LogitechControllerDetect.cpp) are useful for logo lighting and receiver routing. In its legacy path the source marks this route Linux-only; that matters when assessing Windows access.

POWERPLAY 2 is a separate backlog product. This pass did not verify a PID or control feature set for it. Do not inherit original POWERPLAY's lighting, receiver or endpoint behavior from branding.

## G560 speakers

OpenRGB's source identifies USB **046D:0A78**, with its unified HID++ detector accepting FF43 collections and old controller history providing an additional reference. [OpenRGB Logitech controllers](https://github.com/CalcProgrammer1/OpenRGB/tree/029758e2260208c6de06eefc7106cd5c462c2310/Controllers/LogitechController) and [HaloDaemon-plugins](https://github.com/TimP4w/HaloDaemon-plugins) (`logitech_g560` plugin/protocol) are concrete source leads. The latter was located through its published protocol index, not fully audited here.

Separate the four physical LIGHTSYNC zones, host screen/audio sampling, physical speaker controls, and Windows audio processing. Lighting reference code is not evidence for a fully independent EQ/DTS implementation. Capture USB descriptors, feature set, zone enumeration and the live effect/ownership exchange before mapping writable features.

## Flight products and other LGS-era devices

OpenRGB has an [X56 controller](https://github.com/CalcProgrammer1/OpenRGB/tree/029758e2260208c6de06eefc7106cd5c462c2310/Controllers/LogitechController/LogitechX56Controller) and detector entries for **VID 0738**, joystick **2221** and throttle **A221**. This is outside the 046D vendor filter and outside standard HID++ according to that detector. Treat stick and throttle as separate devices; their lighting is distinct from axis/button configuration.

For X52/X52 Pro, X55, X56 revisions, flight panels, pedals, yokes, F310/F510/F710 and older G940/G-series joysticks, ordinary USB input support does not prove a configurable G-Lite control path. They remain an explicitly separate research backlog. Saitek/Mad Catz/Logitech branding and VIDs can differ across revisions. Do not label all of them HID++ because Logitech now sells them.

## G560 / G815 additional direct source

[Xteom/logitech_linux_controllers](https://github.com/Xteom/logitech_linux_controllers) was subsequently inspected directly: `g560ctl/src/logitech_lighting/devices/g560/config.py` identifies 0A78; `g560ctl/src/logitech_lighting/devices/g815/keyboard.py` identifies C33F and dynamically resolves 8081/8071/8100/8010. Its G815 module documents claim/release and profile reload, while the root README still lists only G560. This is another example where source has more coverage than the landing page. License terms were not established from a root license file in this pass.
