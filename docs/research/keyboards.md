# Keyboard protocol research

Research date 2026-11-08. [Existing keyboard notes](../device-research.md), [G915](../G915.md) and [older effects](../keyboards-8070.md) remain useful. This file adds broader model coverage and distinguishes confirmed feature reports from family assumptions.

## Older RGB keyboards: exact product table

Extracted from [g810-led's Keyboard.h](https://github.com/MatMoul/g810-led/blob/efa3c35e74f01a2ed2346320da8bad411bfe8ed8/src/classes/Keyboard.h), with [pinned snapshot](snapshots.md). g810-led is GPL-3.0; facts only.

| Product | USB PID | Useful reference route |
|---|---|---|
| G213 Prodigy | C336 | 8070 zone effects; five regions, not per-key RGB |
| G410 Atlas Spectrum | C330 | g810-led, keyleds, older per-key protocol |
| G413 Carbon | C33A | g810-led and libratbag; single-color limitations |
| G512 Carbon | C342 | g810-led's named product entry |
| G512 RGB / G513 | C33C | OpenLogi recording says G512, libratbag/g810-led call it G513; retain alias |
| G610 Orion | C333, C338 | g810-led; intensity rather than arbitrary RGB |
| G810 Orion Spectrum | C331, C337 | g810-led/keyleds, per-key lighting |
| G815 | C33F | g810-led and actual Solaar capture; newer 8071/8081 route |
| G910 Spark / Spectrum | C32B, C335 | g810-led; two PIDs, do not retain C335 alone |
| PRO keyboard | C339 | g810-led, keyleds and libratbag |

This table does not claim every non-G815 board exposes exactly the same feature versions. [keyleds](https://github.com/keyleds/keyleds), GPL-3.0, is especially useful for 8080 key enumeration, host animation, device descriptors and layouts. [g810-led-resources](https://github.com/MatMoul/g810-led-resources) holds separate keyboard captures, rather than forcing protocol conclusions from a README support list.

For G910 G-keys, [JSubelj/g910-gkey-macro-support](https://github.com/JSubelj/g910-gkey-macro-support) and its wiki explain disabling the default F-key behavior, consuming G-key reports and emitting host key events. This is a different feature from lighting and from onboard macro storage. It is useful even if G-Lite initially implements lighting only.

The C33C alias is a concrete naming collision, not resolved by choosing one application's label. Existing OpenLogi fixtures contain a G512 recording with no 8100/8101. A libratbag keyboard definition does not independently prove onboard profiles on that same device revision.

## Original G915 / G913 and G815 / G813

| Family | USB | WPID | Bluetooth | Receiver evidence |
|---|---|---|---|---|
| G915 full-size | C33E | 407C | B354 | OpenRGB C541 and shared C547 |
| G915 TKL | C343 | 408E | B35F | OpenRGB C545 and shared C547 |
| G815 | C33F | — | — | Wired |
| G813 | C232 | — | — | OpenRGB detector; regional-family lead |

References: [Solaar feature captures](solaar-feature-evidence.md) and [OpenRGB detector](https://github.com/CalcProgrammer1/OpenRGB/blob/029758e2260208c6de06eefc7106cd5c462c2310/Controllers/LogitechController/LogitechControllerDetect.cpp). C547 is shared by other keyboards, including the G515. OpenRGB's detector explicitly probes the paired name and sends the G915 X family elsewhere.

Original G915 and G815 reports have `8071`, `8081`, `8040`, `8100`, `8060`, `8010`, `8020` and `8030`; they have no listed `8101`. The original G915 TKL capture also advertises G/M/MR feature IDs, even though its physical layout differs. A feature advertisement must not create nonexistent physical controls in the UI.

The G915 capture filename ending **407E** actually reports **407C** in its contents. This is a filename typo, not a second verified WPID. The [generated table](solaar-feature-evidence.md) retains the source filename but uses its labeled transport IDs.

The hardware profile format remains a capture requirement before saving. The existing G-Lite doc describes format 4 as G915-like; [mouse research](mice.md) adds evidence of format 4 on an original SUPERLIGHT mouse, so this label cannot be used as a device-type discriminator.

## G915 X is a separate family

Two MIT-licensed focused references were inspected:

- [TheMorpheus407/g915x-heatmap](https://github.com/TheMorpheus407/g915x-heatmap), especially [PROTOCOL.md](https://github.com/TheMorpheus407/g915x-heatmap/blob/50e72459d4e03f1aff7fff41b99f749545ea7b0a/PROTOCOL.md): an author-verified **C356** path, `8071` effects and `8081` per-key batches/commit, G-key/media/logo IDs, host ownership and capture extraction.
- [joshman1019/g915x-rgb](https://github.com/joshman1019/g915x-rgb): author-tested **C359**, 118-address lighting implementation and host profile service. The README's “no existing tool supports it” claim is historical and is not used as a current support fact.

These references refer to different PIDs and have transport wording that needs care. The heatmap document says index FF for its wireless unit and 01 for wired, the reverse of the usual raw receiver convention. This may reflect the Linux virtual HID node and routing level; it must not replace a Windows receiver probe rule without a capture.

[OpenRGB #4461](https://gitlab.com/CalcProgrammer1/OpenRGB/-/issues/4461) is a capture lead cited by the heatmap author. [Solaar #3137](https://github.com/pwr-Solaar/Solaar/issues/3137) is cited for edge/media LED ownership errors. Current [OpenRGB unified HID++ controller](https://github.com/CalcProgrammer1/OpenRGB/tree/029758e2260208c6de06eefc7106cd5c462c2310/Controllers/LogitechController/LogitechHIDPP20Controller) contains G915 X handling, so old claims that only a single small project can control it have aged.

[theo-antonescu/logi-ghub-linux](https://github.com/theo-antonescu/logi-ghub-linux), GPL-2.0-or-later, reports actual testing on **G915 X LIGHTSPEED TKL** and describes `1B05` full-key customization and `8101` profile management instead of original G915 `8010`/`8100`. It claims host lighting works but key remapping/onboard keyboard profiles are incomplete. This is evidence for keeping the families separate, not a complete 8101 schema.

Map full-size wired, full-size LIGHTSPEED, and TKL LIGHTSPEED separately. C356/C359 here are reference-reported identifiers; exact retail variant, WPID and Bluetooth association still need a real G-Lite report. Do not infer all variant IDs from one report.

## G515 mechanical and RAPID

| Model / connection | Evidence | Relevant distinction |
|---|---|---|
| G515 LIGHTSPEED TKL | Solaar capture: USB C355, WPID 40B4, BLE B389; G-Lite tested receiver C547 | 8071v4, 8081, 8040, 8101 |
| G515 TKL wired | logirgb hardware log: C358 | Separate wired product; 8101-based persistent lighting documented |
| G515 RAPID TKL | [logirgb issue search](https://github.com/mevdschee/logirgb/issues?q=Rapid), [RGB.NET PR #447](https://github.com/DarthAffe/RGB.NET/pull/447) | New analog-switch model; do not assign the mechanical G515 file layout |

MIT [logirgb](https://github.com/mevdschee/logirgb) and its [research directory](https://github.com/mevdschee/logirgb/tree/4282d82e00ffb5bd79f96088500a930ca32bc05f/research) provide actual G515 C358 and G502 C08B observations plus mirrors of Logitech feature PDFs. [logitech-g515-onboard](https://github.com/federico-duppa/logitech-g515-onboard), GPL-3.0, is the focused 8101 file-layout reference already used by G-Lite. Respect the existing factory/user file separation and preserve unknown bytes.

The report distinguishes `8071` **version 4** on G515 from older G915 versions. For persistence, idle and software ownership, use the existing [lighting-control notes](../lighting-control.md), but validate each operation on the actual feature version. A PC-side “Save profile” in a small RGB utility may only save its JSON file; it is not evidence of onboard persistence.

## Non-backlit G613

Solaar's report identifies **WPID 4065 / BLE B34F**, battery `1000`, `8010`, `8060`, `1B04`, `1BC0`, keyboard layout/disable and host-switch features. There is no listed RGB feature. It is a useful expansion target for battery/G-key behavior, not a lighting page. Its Bluetooth and LIGHTSPEED modes can have different configurable functions.

## Legacy G-key / LCD generation

| Product | IDs from local LGS | Main references | What to map |
|---|---|---|---|
| G11 | C225 | libg15, g15tools | G/M keys, monochrome backlight; no LCD |
| G13 gameboard | C21C | ecraven/g13, libg15 | RGB backlight, joystick, G/M/L keys, monochrome LCD |
| G15 v1 / v2 | C222 / C227 | libg15, g15daemon, g15tools | 160×43 monochrome LCD, extra keys, fixed-color backlight |
| G19 / G19s | C229 | G19Linux/libg19, Gnome15 leads | 320×240 color LCD, RGB backlight, key events |
| G510 / G510s | C22D / C22E | libg15 forks, Arch threads | LCD and G-keys require separate validation; integrated audio is another interface |
| G103 | C24B | LGS manifests | Extra keys; protocol capture still needed |
| G105 / COD | C248 | LGS manifests, LED SDK capability notes | Single-color backlight and G-key route; no generic 8071 assumption |
| G110 | C22B | LGS manifests, libg15 forks | Red/blue backlight plus G-keys and separate audio |
| G710 / G710+ | C24D in local manifest | LGS, LED capability references | White intensity and key groups; verify exact revision and G-key route |

[netfab/libg15](https://github.com/netfab/libg15) is GPL-2.0, forked from a Gnome15 variant. Inspect its device table and report functions rather than assuming its README list is exhaustive. [ecraven/g13](https://github.com/ecraven/g13) has a focused userspace driver and LCD/input mapping; its README places files without copyright notices in the public domain and identifies MIT-style notices on some files; inspect each file before reuse. [g15tools on SourceForge](https://sourceforge.net/projects/g15tools/) is GPL-2.0 and includes rendering/daemon history. [G19Linux](https://sourceforge.net/projects/g19linux/) is explicitly inactive, GPL-2.0, with separate libg19/libg19draw/g19d repositories.

Historical libraries use libusb and may claim entire interfaces. Their existence does not prove a Windows userspace transport can coexist with the normal keyboard driver. Record GET/SET_REPORT versus interrupt/control endpoint access separately from key decoding.

## Newer-model backlog

G713, G715, PRO X wired/hot-swap, PRO X TKL LIGHTSPEED, PRO X 60, G515 RAPID TKL, G512 X 75 and G512 X 98 need distinct entries and hardware reports. [Solaar #3277](https://github.com/pwr-Solaar/Solaar/issues/3277) is a PRO X RAPID enumeration lead. G512 X 75/98 are named in an [official G HUB firmware note](https://support.logi.com/hc/en-au/articles/43643050172055-Logitech-G-HUB); this establishes product support, not feature IDs. Avoid web searches for bare “G713” or “G715”: they often return ASUS laptop models or Arm GPU names rather than Logitech keyboards.

G413 SE and G413 TKL SE must be distinguished from C33A G413 Carbon. A similar name and white backlight do not prove programmable lighting or software support. Confirm against the model's official support/manual before adding a control path.

## PRO X TKL RAPID direct capture follow-up

[Gabriellaines/RigDeck's hardware document](https://github.com/gabriellaines/rigdeck/blob/b9a89b3d8a141922c54b4ba6de3eaae1bcf980e6/docs/protocols/logitech-pro-x-tkl-rapid.md), GPL-3.0, was inspected directly after discovery through logirgb. It records **USB 046D:C35B**, wired interface 2, index FF, firmware U1 70.04 build 0020, HID++ 4.2. The recorded feature list includes **8071v4, 8081v0, 8040v0, 8101v0, 1B05v1, 1B08v0**, keyboard layout/disable and control-list features.

This gives a concrete new device row, rather than just the earlier Solaar enumeration issue. Its reported observations include:

- 8101 files tagged 1B08 for per-key actuation/rapid trigger, 1B05 for key customization, and 8101 for profiles; CRC-32 accompanies file operations.
- Analog key IDs follow a physical matrix order and do **not** match USB HID usages or RGB LED IDs. Do not reuse the RGB layout map for switch-depth settings.
- 1B08 function 2 enables rapid trigger; function 3 controls a key-depth stream. The author measured dropped depth events while typing quickly, so it is diagnostic telemetry rather than a complete reliable key-state feed.
- A single-key actuation write was experimentally observed live, but the stored directory/profile was unchanged and a profile switch reset the value. Therefore “commit” in this file API is not by itself proof of permanent saving.
- Host per-key lighting needs an ownership handshake and frame commit. Media/brightness LED IDs differ from the original G915. Profile changes can wipe host settings; a background service reapplies them.

Do not transfer G-Lite's mechanical G515 persistent-lighting encoder wholesale into this analog keyboard just because both have 8101. Preserve feature-tagged files and the separate matrix/key/LED identifier spaces.

## Legacy USB identity cross-check

The pinned [USB ID registry](usb-id-registry.md) adds component labels C221 (G11/G15 keyboard), C223 (hub), C226/C227 (G15 refresh), C228 (G19 keyboard), C229 (G19 macro component), C22A/C22B (G110 keyboard/G-keys), C231 (G13 virtual mouse) and C2AB (G13 joystick). These are useful discovery leads, not a replacement for a full descriptor capture.

It also labels **C232 “Gaming Virtual Keyboard,” conflicting with OpenRGB's G813 detector label**. Keep the G813 association as an implementation claim only until actual hardware identity establishes whether the PID is a virtual component, shared value or bad reference label. The registry's C53D “G631 Keyboard” is another unresolved-name lead, not enough evidence to invent a supported G631 protocol.
