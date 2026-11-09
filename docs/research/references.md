# Reference catalog

Checked/discovered 2026-11-08. **Deep** means source/recording files were inspected; **lead** means discovery/readme/issue evidence only. Licenses here describe inspected project notices, not permission to import every file or its dependencies. G-Lite's rule remains: no GPL/AGPL code. [Exact commits](snapshots.md) pin the 27 local reference checkouts; links below are navigational unless commit-pinned.

## Broad protocol and device databases

| Reference | Depth / license | Best use |
|---|---|---|
| [libratbag](https://github.com/libratbag/libratbag) | Deep; MIT | 53 G/MX518 definitions, HID++ 1.0/2.0, G300/G600, quirks, profile writes |
| [Solaar](https://github.com/pwr-Solaar/Solaar) | Deep; GPL-2.0 family | Real device dumps, feature versions, Python decoders, Centurion |
| [OpenLogi](https://github.com/AprilNEA/OpenLogi) | Deep; MIT / Apache-2.0 | Typed HID++ feature documentation, fixtures, newer DPI/rate definitions |
| [OpenLogi feature docs](https://openlogi.org/hidpp/features) | Web index inspected | Feature navigation and provenance tags; not all facts hardware-verified |
| [OpenRGB](https://github.com/CalcProgrammer1/OpenRGB) | Deep; GPL-2.0-or-later | Logitech detector, layouts, older controllers, unified HID++ and receiver handling |
| [Linux HID drivers](https://github.com/torvalds/linux/tree/master/drivers/hid) | Focused web reference; GPL family | hid-logitech-hidpp, hid-logitech-dj, hid-lg4ff and IDs; transport/quirk evidence |
| [Logitech cpg-docs](https://github.com/Logitech/cpg-docs) | Deep; vendor documentation, check file terms | Official HID++ framing/root semantics; sparse feature coverage, not the complete gaming protocol |
| [Logitech protocol files at Lekensteyn](https://lekensteyn.nl/files/logitech/) | Public specification references | HID++ 1.0 receiver excerpt and HID++ 2.0 draft |
| [Solaar HID++ document pointer](https://github.com/pwr-Solaar/Solaar/blob/e7304c4c451cc9bb4f206a914844525e67856a28/docs/hidpp-documentation.txt) | Deep | Points to the public Logitech feature-document Drive folder |
| [logirgb research](https://github.com/mevdschee/logirgb/tree/4282d82e00ffb5bd79f96088500a930ca32bc05f/research) | Deep; project MIT, vendor PDFs separate | 8040/8070/8071 specs, G515/G502 hardware logs |

The [USB ID registry cross-check](usb-id-registry.md) adds 79 pinned identifier/name records, including legacy component/mode IDs. It is community identifier evidence, not an official Logitech capability list.

## Focused mouse implementations

| Reference | Depth / license | Best use |
|---|---|---|
| [logirgb](https://github.com/mevdschee/logirgb) | Deep; MIT | G502 C08B and wired G515 C358, persistent lighting |
| [OpenMouse mouse-protocol](https://github.com/OpenMouse-Project/mouse-protocol) | Deep; AGPL-3.0-or-later package notice | Numeric profile layouts, DPI/LOD/rate and OMM provenance; conflicting/stale prose needs caution |
| [linux-superstrike](https://github.com/mclol0/linux-superstrike) | Deep; MIT | Actual Superstrike identity, profiles, haptics and rate measurements |
| [superstrike-sdk](https://github.com/fawhfi/superstrike-sdk) | Deep; MIT | Windows analog-button travel telemetry, 1B0C event 1 |
| [logi-cli](https://github.com/meech-io/logi-cli) | Deep; MIT | PRO X 2 DEX, IOKit transport and onboard mutations |
| [opengcontrol](https://github.com/kasik96/opengcontrol) | Deep; README says Apache-2.0, inspect file terms | Minimal Rust CLI; default-branch README contains an ID collision |
| [opengcontrol G502 PR #1](https://github.com/kasik96/opengcontrol/pull/1) | PR description inspected; open at inspection | G502/X sector profiles and cross-model button mapping; **not merged default-branch support** |
| [orthory/lowtech](https://github.com/orthory/lowtech) | Lead; README says GPL-2.0-or-later | SUPERLIGHT 2c format-7 read/remap research; its “Bolt” receiver wording needs confirmation |
| [Ahmedxsaad/g502-hub](https://github.com/Ahmedxsaad/g502-hub) | Lead; license not checked | G502 HERO protocol.md, layered minimal implementation |
| [zeex64/openghub](https://github.com/zeex64/openghub) | Lead; license not checked | Superstrike-focused reverse-engineering notes; different project from Slyvan25/OpenGHub |
| [mayankkumar-cmd/G-Zero](https://github.com/mayankkumar-cmd/G-Zero) | Reddit-linked lead; repository not audited | Windows G304/G305 DPI utility and binding problems |
| [AngeloCore/gpx-battery](https://github.com/AngeloCore/gpx-battery) | Readme lead; license not checked | Small Windows native mouse battery utility |
| [lomoco](https://github.com/dougvj/lomoco) | Legacy lead; not audited | Early Logitech USB mouse control; do not assume all G100/G400/MX518 revisions |

## Keyboards and legacy GamePanel

| Reference | Depth / license | Best use |
|---|---|---|
| [g810-led](https://github.com/MatMoul/g810-led) | Deep; GPL-3.0 | Exact 13 PID entries, per-key/native effects and keyboard families |
| [g810-led-resources](https://github.com/MatMoul/g810-led-resources) | Linked capture collection; not fully read | USB dumps separated from source repository |
| [keyleds](https://github.com/keyleds/keyleds) | Readme/project lead; GPL-3.0 | 8080 key/zone discovery and animations |
| [logitech-g515-onboard](https://github.com/federico-duppa/logitech-g515-onboard) | Deep/reference reviewed with existing repo; GPL-3.0 | G515 8101 storage layout |
| [g915x-heatmap](https://github.com/TheMorpheus407/g915x-heatmap) | Deep; MIT | C356 8081 protocol, software ownership, LED IDs and capture pitfalls |
| [g915x-rgb](https://github.com/joshman1019/g915x-rgb) | Deep/readme/source identity; MIT | C359 per-key lighting and host profiles |
| [g910-gkey-macro-support](https://github.com/JSubelj/g910-gkey-macro-support) | Readme/wiki lead; file license not checked | G-key capture and disabling default F-key mapping |
| [LogiGSK](https://github.com/MohamadSaada/LogiGSK) | Readme lead; GPL-3.0 | Historical G410/G610/G810/G910 UI/layout and effects |
| [logitech-led-control](https://github.com/peppapig450/logitech-led-control) | Readme lead; GPL-3.0 | Rust g810-led-style native effects and profiles |
| [libg15, netfab fork](https://github.com/netfab/libg15) | Deep; GPL-2.0 | G11/G13/G15/G110/G510 device table, color, LCD and events |
| [ecraven/g13](https://github.com/ecraven/g13) | Deep/readme; public-domain files plus per-file MIT-style notices | Focused G13 input, joystick, color and LCD |
| [g15tools](https://sourceforge.net/projects/g15tools/) | Project/archive inspected; GPL-2.0 | libg15/g15render/daemon history, mailing lists and old patches |
| [G19Linux](https://sourceforge.net/projects/g19linux/) | Project inspected; GPL-2.0, inactive | libg19/libg19draw/g19d and older G19 protocol |
| [henninglive/logitech-led](https://github.com/henninglive/logitech-led/blob/master/DEVICES.md) | Capability-document lead; not a raw driver | Old SDK lighting limitations by product; recheck primary SDK/manual before asserting hardware behavior |
| [RGB.NET](https://github.com/DarthAffe/RGB.NET) | PR/release lead; file terms not checked here | New model names/layouts, G515 RAPID PR #447; identify SDK dependency before calling it direct hardware support |

## Audio, wheels and applications

| Reference | Depth / license | Best use |
|---|---|---|
| [HeadsetControl](https://github.com/Sapd/HeadsetControl) | Deep; GPL-3.0 | 10 Logitech/ASTRO implementations, PID variants, selector/capability tables |
| [g933-utils](https://github.com/ashkitten/g933-utils) | Reference/source attribution inspected; MIT | Early G933 protocol reference credited by HeadsetControl |
| [LGSTrayEx](https://github.com/strain08/LGSTrayEx) | Deep; GPL-3.0 | Windows native HID++, Centurion and separate G HUB websocket route |
| [LGSTrayBattery](https://github.com/andyvorld/LGSTrayBattery) | Project/issues lead; check terms | Older native/G HUB telemetry and new-device enumeration issues |
| [Oversteer](https://github.com/berarma/oversteer) | Deep; GPL-3.0 | Wheel IDs and config model |
| [new-lg4ff](https://github.com/berarma/new-lg4ff) | Deep/reference; GPL-2.0-derived | Legacy wheel protocol and force feedback |
| [TrueForce Linux driver](https://github.com/mescon/logitech-trueforce-linux-driver) | Deep; GPL-2.0 | RS50/PRO/G923 protocol, 64-byte reports, actual capture references |
| [Slyvan25/OpenGHub](https://github.com/Slyvan25/OpenGHub) | Deep; MIT | Broad app, G HUB device/depot metadata and profile import; breadth claims are not hardware validation |
| [logi-ghub-linux](https://github.com/theo-antonescu/logi-ghub-linux) | Readme lead; GPL-2.0-or-later | Author-tested G502 X LIGHTSPEED, G915 X TKL, G600; limitations are useful |
| [LogitechGDeviceInfo](https://github.com/infinitysky/LogitechGDeviceInfo) | Readme lead; terms not checked | Windows dynamic-feature discovery and candid unsupported-family list |
| [HaloDaemon-plugins](https://github.com/TimP4w/HaloDaemon-plugins) | Published index lead; terms not checked | Separate Logitech G560 and HID++ protocol documents |
| [hypercolor](https://github.com/hyperb1iss/hypercolor) | Compatibility index lead; terms not checked | Logitech G13/G19/G560/G600 driver-family pointers; not independent device verification |

## Vendor configuration tools and research

- [Official G HUB release notes](https://marketplace.logi.com/releasenotes/ghub/en), [official OMM landing page](https://support.logi.com/hc/en-us/articles/360059641133-Onboard-Memory-Manager), [official developer SDKs](https://www.logitechg.com/en-us/programs/developer-alliance). See [vendor notes](vendor-software.md) for actual observations and inaccessible content.
- [G HUB settings extractor](https://github.com/gabfv/logitech-g-hub-settings-extractor), MIT: SQLite/JSON representation.
- [ghubMacroEditor](https://github.com/A-Bomb/ghubMacroEditor): macro schema and assignments, license not checked.
- [G HUB configuration incident](https://github.com/xcogito/Logitech_GHub_Fix): version-specific database bloat investigation; not generalized to every installation.
- [Logitech G15 SDK mirror](https://github.com/DY357LX/Logitech-G15-SDK): historical GamePanel docs; vendor terms, no code/assets imported.

## Efficient search strategy used

First enumerate source tables and capture directories, then search exact model + PID/WPID + issue terms. Use focused implementation docs to fill protocol details. Follow useful HN/Reddit posts back to source/PRs, and record whether a claimed implementation is on the default branch. Use forums for mode/firmware quirks, not authoritative command bytes. Keep negative/ambiguous results in the backlog instead of inventing IDs. Search exact Logitech product phrases: short G-series numbers collide with laptops, GPUs and unrelated products.

## Direct follow-ups from logirgb's reference index

- [RigDeck](https://github.com/gabriellaines/rigdeck), GPL-3.0, directly inspected: unusually detailed PRO X TKL RAPID C35B capture/feature/storage/analog/lighting document. It distinguishes live file application from flash persistence. See [keyboard follow-up](keyboards.md#pro-x-tkl-rapid-direct-capture-follow-up).
- [loxsmoke/openlogi-net](https://github.com/loxsmoke/openlogi-net), MIT, directly inspected: Windows C#/.NET 10 HID++ implementation, receiver/POWERPLAY discovery, legacy read-only catalog and RGB/onboard features. Its modern runtime and dependencies are unsuitable for direct drop-in reuse in G-Lite's C# 5 target, but the Windows transport/provenance is useful.
- [Xteom/logitech_linux_controllers](https://github.com/Xteom/logitech_linux_controllers), direct README + source identity/handshake inspection; license not established in root: G560 0A78 and G815 C33F implementations. The root README understates newer G815 source coverage. Do not mistake its test fakes for capture evidence.
- [Secondary reference queue](additional-reference-leads.md): more specialized projects found in logirgb's research, with the originating source file. These are unverified leads, not a support or license claim.
