# Logitech G device expansion map

Research date **2026-11-08**. This is a research inventory, not a compatibility promise. Rows can group regional aliases or revisions; do not count aliases, receiver endpoints and compatibility modes as distinct retail products. “Unknown” means this pass did not verify the value.

Evidence keys link to the exact source inventories: **[R]** libratbag implementation, **[S]** Solaar capture, **[L]** local vendor manifest, **[K]** keyboard source evidence, **[H]** headset implementation, **[W]** wheel/other source evidence. “Lead” is weaker issue/readme evidence. All IDs are hex; default VID 046D. Dashes do not prove a transport is unsupported.

[R]: libratbag-device-inventory.md
[S]: solaar-feature-evidence.md
[L]: lgs-manifest-inventory.md
[K]: keyboards.md
[H]: headsets.md
[W]: wheels-and-other.md

## Mice: HID++ 2.0 family

USB/WPID separation uses labeled capture IDs where available. R-only 40xx selectors are shown as WPID/virtual candidates because Linux receiver children use those IDs; confirm their raw Windows route.

| Product | USB PID | WPID / virtual candidate | Other transport | Evidence / next distinction |
|---|---|---|---|---|
| G102 / G103 / G203 family | C084, C092, C09D | — | — | [R]; split Prodigy/LIGHTSYNC revisions before selecting zone behavior |
| G302 | C07F | — | — | [R], [L]; C07E appears as extra LGS probe but belongs to G402 in other evidence |
| G303 Daedalus Apex | C080 | — | — | [R] |
| G303 Shroud Edition | C097 | Unknown here | — | [R]; receiver ID absent from this definition |
| G304 / G305 | — | 4074 | Receiver PID not established by this table | [R], [S]; regional names; no physical RGB despite color feature |
| G309 LIGHTSPEED | Unknown here | 40B1, issue-reported | Bluetooth needs separate capture | [Solaar #2998](https://github.com/pwr-Solaar/Solaar/issues/2998), lead |
| G402 | C07E | — | — | [R], [L] |
| G403 wired / Prodigy | C083 | — | — | [R], [L] |
| G403 wireless | C082 wired mode | 405D | Receiver shared-family route | [R] |
| G403 HERO | C08F | — | — | [R] |
| G502 Proteus Core | C07D | — | — | [R], [S] |
| G502 Proteus Spectrum | C332 | — | — | [R], [S]; INDEX_OFFSET |
| G502 HERO / SE HERO | C08B | — | — | [R], [S]; real logirgb hardware; INDEX_OFFSET |
| G502 LIGHTSPEED | C08D | 407F | C539 receiver in PR/capture references | [R], [S]; INDEX_OFFSET |
| G502 X wired | C099 | — | — | [R], [S]; capture has 2201 + 8060 |
| G502 X LIGHTSPEED | C098 | 409F | Shared C547 receiver reported in focused PR | [R]; INDEX_OFFSET |
| G502 X PLUS | C095 | 4099 | Receiver separately identified | [R], [S]; 8071/8081, LED-slot quirk, still 8060 in capture |
| G602 | — | 402C | C537 receiver in LGS | [R], [L]; G602 quirk |
| G603 | — | 406C | BLE B01C | [R] |
| G604 | — | 4085 | BLE B024 | [R], [S]; INDEX_OFFSET; host-switch features |
| G703 | C087 | 4070 | — | [R], [S] |
| G703 HERO | C090 | 4086 | — | [R] |
| G705 | C096 | 409D | BLE B02E | [R]; G502X_PLUS LED quirk |
| G900 | C081 | 4053 | C539 receiver in LGS | [R], [L] |
| G903 | C086 | 4067 | — | [R] |
| G903 HERO | C091 | 4087 | — | [R], [S]; both 8070 and 8071 in capture |
| PRO wired / HERO | C085, C08C | — | — | [R]; split sensor generation |
| PRO Wireless | C088 | 4079 | LIGHTSPEED | [R]; G-Lite tested unit in existing repo |
| PRO X SUPERLIGHT (original) | C094 | 4093 | LIGHTSPEED | [R], [S]; capture name PRO X Wireless; profile-format-4 report in OpenMouse |
| PRO 2 LIGHTSPEED | C09A | Unknown here | LIGHTSPEED | [Linux HID++ table](https://github.com/torvalds/linux/blob/master/drivers/hid/hid-logitech-hidpp.c), implementation entry |
| PRO X SUPERLIGHT 2 | C09B | 40A9 | LIGHTSPEED | [S]; capture name PRO X 2, 2202/8061 |
| PRO X SUPERLIGHT 2 DEX | C0A0 | 40B8 | Receiver C547 in logi-cli | [Solaar #2836](https://github.com/pwr-Solaar/Solaar/issues/2836), [logi-cli](https://github.com/meech-io/logi-cli); capture + focused implementation |
| PRO X SUPERLIGHT 2c | C09F | Unknown here | Receiver identity needs checking | [libratbag #1813](https://github.com/libratbag/libratbag/issues/1813), lead |
| PRO X 2 SUPERSTRIKE | C0A8 | 40BD | Receiver C54D | [S], [linux-superstrike](https://github.com/mclol0/linux-superstrike); 2202/8061/1B0C, separate profile format |
| PRO X 3 SUPERSTRIKE | C0A9, source-reported | Unknown here | Unknown here | [OpenMouse profile doc](https://github.com/OpenMouse-Project/mouse-protocol/blob/b62a1cce2b63698ac00595efdb0067ecd54e34a0/docs/logitech-onboard-profiles.md), lead only; require independent identity/capture |
| MX518 HERO / Legendary | C08E | — | — | [R]; separate from original MX518 |

Per-product feature capabilities beyond the named captures remain unverified. Use [mouse protocol notes](mice.md), not a single “all G mice” branch.

## Mice: earlier protocols

| Product | USB / receiver PID | Candidate route | Evidence / distinction |
|---|---|---|---|
| G1 / G3 | Unknown in this pass | Model-specific legacy | Backlog; no guessed IDs |
| G5 original | C041 | HID++ 1.0 | [R] |
| G5 2007 | C049 | HID++ 1.0 | [R]; different profile metadata |
| G7 | C51A | HID++ 1.0 legacy wireless | [R] |
| G9 | C048 | HID++ 1.0, G9 profile layout | [R], [L] |
| G9x | C066 | HID++ 1.0, G500-type profile | [R], [L]; existing live G-Lite support |
| G9x COD MW3 | C249 | HID++ 1.0, G500-type profile | [R], [L] |
| G100 / G100s / G90 | C247 | Legacy route still unverified here | [L]; several aliases/colors share ID |
| G300 / G300s | C246 | Proprietary FF80, feature reports | [R], [L]; F0/F3/F4/F5, 35-byte profile |
| G400 | C245 | Legacy FF80 probe | [L] |
| G400s | C24C | Legacy FF80 probe | [L] |
| G500 | C068 | HID++ 1.0, G500 profile | [R], [L] |
| G500s | C24E | HID++ 1.0, G500 profile | [R], [L] |
| G600 | C24A | Proprietary FF80, feature reports | [R], [L]; 154-byte profile, G-shift table |
| G700 | C06B USB / C531 receiver | HID++ 1.0, G700 profile | [R], [L] |
| G700s | C07C USB / C531 receiver | HID++ 1.0, G700 profile | [R], [L]; same receiver PID does not distinguish it from G700 |
| MX518 original / 1600 | C051 / C01E | Earlier USB mouse protocol | [L]; not C08E HERO |

## Keyboards and gameboards

| Product / variant | IDs verified in the cited source | Protocol/capability route | Evidence |
|---|---|---|---|
| G11 | USB C225 | Legacy extra keys/backlight; no LCD | [L], [K] |
| G13 | USB C21C | Legacy gameboard, RGB, LCD and joystick | [L], [K] |
| G15 v1 | USB C222 | Legacy mono LCD/G-keys | [L], [K] |
| G15 v2 | USB C227 | Legacy mono LCD/G-keys, different report flag | [L], [K] |
| G19 / G19s | USB C229 | Legacy color LCD/G-keys/RGB | [L], [K] |
| G103 | USB C24B | Legacy; wire protocol unresolved | [L] |
| G105 / COD | USB C248 | Legacy G-keys, fixed-color lighting | [L], [K] |
| G110 | USB C22B | Legacy G-keys, red/blue lighting, separate audio | [L], [K] |
| G213 | USB C336 | 8070 five-zone effects | [S], [K] |
| G310 | Not extracted here | Legacy/early RGB candidate | Installed LGS resource directory only; capture needed |
| G410 | USB C330 | Older per-key route | [K] |
| G413 Carbon | USB C33A | Single-color effects/intensity | [R], [K] |
| G413 SE / TKL SE | Unknown here | Separate product; programmable control unverified | Backlog; do not inherit C33A capabilities |
| G510 / G510s | USB C22D / C22E | Legacy LCD/G-keys/color; IDs also reflect audio mode | [L], [K] |
| G512 Carbon | USB C342 | Older RGB route | [K] |
| G512 RGB / G513 | USB C33C | 8070/8080 in G512 recording; naming collision | [R], [K] |
| G515 LIGHTSPEED TKL | USB C355 / WPID 40B4 / BLE B389 / receiver C547 | 8071v4, 8081, 8040, 8101 | [S], [K]; existing tested G-Lite model |
| G515 TKL wired | USB C358 | 8071, 8040, 8101 | [logirgb](https://github.com/mevdschee/logirgb), hardware log |
| G515 RAPID TKL | Unknown here | Analog-switch model, distinct capture needed | [K], RGB.NET/logirgb issue leads |
| G610 Orion Brown / Red | USB C333 / C338 | Per-key intensity, not arbitrary RGB | [K] |
| G613 | WPID 4065 / BLE B34F | Battery/G-keys/host-switch, no listed RGB | [S], [K] |
| G713 | Unknown here | Separate wired Aurora product | Backlog |
| G715 | Unknown here | Separate wireless Aurora product | Backlog; receiver response alone does not establish keyboard protocol version |
| G710 / G710+ | USB C24D in manifest | Legacy single-color controls | [L], [K]; verify retail variant |
| G810 | USB C331 / C337 | Older per-key RGB route | [K] |
| G813 | C232 in OpenRGB, identity contested | Regional G815-family implementation lead | [K]; USB registry calls C232 Gaming Virtual Keyboard, so do not auto-classify |
| G815 | USB C33F | 8071, 8081, 8040, 8100 | [R], [S], [K] |
| G910 Spark / Spectrum | USB C32B / C335 | Older RGB/G-key route | [L], [R], [K] |
| G915 / regional G913 candidate | USB C33E / WPID 407C / BLE B354 | 8071, 8081, 8040, 8100 | [S], [K]; original family only; regional alias needs descriptor confirmation |
| G915 TKL / regional G913 TKL candidate | USB C343 / WPID 408E / BLE B35F | Same discovered feature family, different layout | [S], [K] |
| G915 X full-size variants | C356 / C359 source reports; variant mapping unresolved | 8071/8081 lighting; newer storage route must be read | [K], two focused implementations; do not collapse the two IDs |
| G915 X LIGHTSPEED TKL | Unknown here | 8071/8081 host lighting, reported 1B05/8101 | [logi-ghub-linux](https://github.com/theo-antonescu/logi-ghub-linux), author-tested implementation claim |
| PRO keyboard | USB C339 | Older per-key route | [R], [K] |
| PRO X keyboard / hot-swap | Unknown here | Separate revision | Backlog |
| PRO X TKL LIGHTSPEED | Unknown here | Newer keyboard feature discovery required | Backlog |
| PRO X TKL RAPID | USB C35B | 8071v4/8081/8040/8101; 1B08 analog settings and stream | [RigDeck hardware document](https://github.com/gabriellaines/rigdeck/blob/b9a89b3d8a141922c54b4ba6de3eaae1bcf980e6/docs/protocols/logitech-pro-x-tkl-rapid.md); capture + verified experiments |
| PRO X 60 | Unknown here | Newer compact keyboard / KEYCONTROL | Backlog |
| G512 X 75 / G512 X 98 | Unknown here | Newer rapid-trigger/SOCD models | [Official G HUB note](https://support.logi.com/hc/en-au/articles/43643050172055-Logitech-G-HUB); vendor support evidence only |

## Headsets / ASTRO

The control endpoint may be the USB DAC or wireless dongle rather than the headset itself. These are actual implementation IDs, not proof that the same product has the same route over Bluetooth or analog cable. See [capability selectors and exceptions](headsets.md).

| Product / implementation variant | USB VID:PID | Route | Evidence |
|---|---|---|---|
| G330 | 046D:0A17 | Registry-only USB headset identity; controls unknown | [USB ID registry](usb-id-registry.md) |
| G35 | 046D:0A15 | Legacy audio/control, not mapped here | [L] |
| G430 | 046D:0A4D | Legacy USB adapter/control, not mapped here | [L] |
| G431 | 046D:0A9B | Sidetone implementation | [H] |
| G432 | 046D:0A9C | Sidetone implementation | [H] |
| G433 | 046D:0A6D | Sidetone implementation | [H] |
| G522 LIGHTSPEED | 046D:0B18 receiver / 0B19 wired | Centurion, prefix 50 23 | [H]; wired ID also in OpenRGB/Solaar; validate wired capabilities separately |
| G533 | 046D:0A66 | HID++ with capability-specific selectors | [H] |
| G535 | 046D:0AC4 | HID++ / different collection selectors | [S], [H] |
| G633 | 046D:0A5C | Classic HID++ headset family | [L], [H] |
| G635 | 046D:0A89 | Classic HID++ headset family | [R], [H] |
| G733 | 046D:0AB5 / 0AFE / 0B1F | Classic HID++ headset family | [R], [S], [H]; three source IDs |
| G735 | 046D:0AD8, OpenLogi-reported | Proprietary/unknown route | [OpenLogi #1675](https://github.com/AprilNEA/OpenLogi/issues/1675) + existing notes; no verified implementation here |
| G930 | 046D:0A1F | Legacy HID++-style dedicated implementation | [L], [H] |
| G933 | 046D:0A5B | Classic HID++ headset family | [L], [S], [H] |
| G935 | 046D:0A87 | Classic HID++ headset family | [R], [S], [H] |
| PRO USB family | 046D:0AA7 | HID++ adapter implementation | [H]; do not advertise battery on wired-only hardware |
| PRO X variants | 046D:0AAA / 0ABA | HID++ family | [H]; G-Lite tested 0ABA wireless dongle in existing repo |
| PRO X 2 HID++ variants | 046D:0AFB / 0AFC | HID++ family | [H]; retail/connection association needs capture |
| PRO X 2 Centurion variant | 046D:0AF7 | Centurion 64-byte prefix 51 | [H]; separate feature namespace |
| ASTRO A50 Gen 4 implementation | **9886:002C** | ASTRO proprietary FF32, interface 6 | [H]; VID differs from Logitech |
| ASTRO A50 Gen 5 | 046D:0B1C | Proprietary report 02 / FF32, interface 8 | [H]; not HID++, not Centurion |
| ASTRO A20 X | 046D:0B34, issue-reported | Unknown in this pass | [HeadsetControl issue search](https://github.com/Sapd/HeadsetControl/issues?q=A20+X), lead |
| ASTRO A50 X | Unknown here | Centurion mentioned by A50 Gen 5 source; not verified here | [H], backlog |
| G435 / G321 / G325 / G335 / G331 / G332 / G230 / G231 / G333 | Unknown here | USB/Bluetooth/analog varies; control unknown | Backlog, not assumed HID++ |
| ASTRO A10 / A20 / A30 / A40 / MixAmp variants | Unknown here | Analog/USB/base-station routes vary | Backlog; variant + VID/PID first |

## Wheels, speaker, mat and flight

| Product / variant | Verified IDs | Route / distinction | Evidence |
|---|---|---|---|
| G25 | 046D:C299 | Legacy lg4ff | [W] |
| G27 | 046D:C29B | Legacy lg4ff | [W] |
| G29 | 046D:C24F; C260 registry PS4 label | Legacy lg4ff; capture the actual selected mode | [L], [W], [USB registry](usb-id-registry.md) |
| G920 | 046D:C262 | HID++ force-feedback wheel | [L], [W] |
| G923 PlayStation | 046D:C266 | Distinct from Xbox; compatibility mode can change PID | [W] |
| G923 Xbox | 046D:C26E | HID++ wheel | [W] |
| PRO Racing PlayStation | 046D:C268 | Direct-drive config/FFB | [W] |
| PRO Racing Xbox | 046D:C272 | Direct-drive; RS50 compatibility mode can share ID | [W] |
| RS50 PlayStation | 046D:C276 | Config replies 64 bytes; separate FFB interface | [W] |
| Driving Force / Pro / GT | 046D:C294 / C298 / C29A | Legacy / compatibility modes | [W] |
| MOMO / variant | 046D:C295 / CA03 | Legacy | [W] |
| Speed Force Wireless | 046D:C29C | Legacy | [W] |
| WingMan Formula family | 046D:C291 / C293 / C20E | Different input/FFB capabilities | [W] |
| POWERPLAY original | Receiver 046D:C53A; mat WPID 405F | Mat endpoint index 7 in libratbag | [R], [W] |
| POWERPLAY 2 | Unknown here | Distinct product, capabilities unverified | Backlog |
| G560 speakers | 046D:0A78 | HID++ lighting; audio processing separate | [W] |
| X56 joystick / throttle | **0738:2221 / A221** | Separate non-HID++ lighting controller | [W] |
| G940 Flight System | 046D:C287, registry-only | Separate flight protocol; capabilities unverified | [USB registry](usb-id-registry.md) |
| X52 / X52 Pro / X55 / other X56 revisions | Unknown here | Separate flight-device backlog | [W] |

## Fields to preserve when transferring this map

For each row keep retail name, regional alias, hardware revision, USB PID, receiver PID, WPID, Bluetooth PID, device index, transport selector (interface/page/usage/report lengths), firmware, feature IDs **and versions**, profile format/size, live capability, onboard capability, host-service dependency, source URL/commit, evidence level and last verification date. Unknown fields should remain unknown. Existing G-Lite support status belongs in a separate field from upstream implementation evidence.
