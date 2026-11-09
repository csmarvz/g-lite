# Mouse protocol research

Inspected 2026-11-08. See [device IDs and driver quirks](libratbag-device-inventory.md), [actual feature reports](solaar-feature-evidence.md), and [pinned commits](snapshots.md). No mouse was accessed during this research.

## Classic HID++ 2.0 mice

[libratbag's HID++ implementation](https://github.com/libratbag/libratbag/blob/8235b5bb6032dea15901ab58a8218956f4494d08/src/hidpp20.c) and [driver](https://github.com/libratbag/libratbag/blob/8235b5bb6032dea15901ab58a8218956f4494d08/src/driver-hidpp20.c) are the broadest MIT-licensed references. G102/G203, G302/G303, G402/G403, G502, G603/G604, G703/G705, G900/G903 and PRO families have upstream definitions. The definition file establishes a candidate, not the exact feature versions or successful writes on every firmware.

The useful discovery bundle is `2201` adjustable DPI, `8060` report interval, `8070` or `8071` lighting, and `8100` onboard profiles. Some mice have no actual RGB even when a color feature appears: the G304/G305 report includes `8070`, while libratbag explicitly declares zero LEDs. Read capabilities and physical zone meaning separately.

Specific quirks worth preserving:

| Family | Evidence | Mapping consequence |
|---|---|---|
| G305/G304 | libratbag G305 quirk; Solaar WPID 4074 reports | Same observed WPID / regional names, no user RGB; record battery and performance-mode behavior |
| G602 | libratbag G602 quirk, receiver C537 versus child 402C | Legacy receiver route; do not identify the mouse from receiver PID alone |
| G502 HERO/Spectrum/LIGHTSPEED, G604, G502 X | `INDEX_OFFSET` in selected definitions | Keep profile-index conventions explicit; G-Lite's existing code already assumes particular format/index behavior |
| G502 X PLUS / G705 | `G502X_PLUS` quirk | LED records/slots differ; PLUS is not just the non-RGB X with a color picker |
| G903 HERO | Solaar capture includes both 8070v4 and 8071v0 | Prefer discovered capability behavior, not a hardcoded generation split |
| G502 X C099 / X PLUS 4099 | Solaar captures explicitly have 8060v0 | Do not claim these necessarily require extended report rate |
| G502 LIGHTSPEED → X migration | opengcontrol author's hardware account | Button order differs between products; byte-copying a profile can scramble physical assignments |

The still-open [opengcontrol G502 PR #1](https://github.com/kasik96/opengcontrol/pull/1), rather than its inspected default branch, is particularly useful for sector-addressed profiles, profile cloning, button index conversion and onboard-mode report-rate behavior. Its [author's G502 migration account](https://louisabraham.github.io/articles/clawd-grip) explains the cross-model button-order trap. Confirm the current code and license before any reuse.

## Newer mice: 2202, 8061 and independent connection rates

The checked-in Solaar **PRO X 2 40A9** capture labels USB **C09B**, has `2202v0`, `8061v0`, `8100`, and no listed `2201` or `8060`. The **Superstrike 40BD** capture labels USB **C0A8**, adds `1B0C` analog buttons and similarly uses `2202`/`8061`. See the [exact captures](solaar-feature-evidence.md).

G-Lite currently identifies mice with `Kind == mouse && Has(2201)`. These two reports establish a concrete coverage gap. `2202` must be a separate protocol route, not an alias implemented by sending old 2201 argument bytes.

Read [OpenLogi's extended DPI feature](https://github.com/AprilNEA/OpenLogi/blob/8bf29cb704f74b0a3b0b7dc7ab74d1570f20db19/crates/openlogi-hidpp/src/feature/extended_dpi.rs) and [extended report-rate feature](https://github.com/AprilNEA/OpenLogi/blob/8bf29cb704f74b0a3b0b7dc7ab74d1570f20db19/crates/openlogi-hidpp/src/feature/extended_report_rate.rs). Verify the exact path against the pinned checkout; [OpenLogi's feature index](https://openlogi.org/hidpp/features) is an additional navigation aid. The feature separates X/Y DPI and lift-off settings; `8061` uses indexes for 125, 250, 500, 1000, 2000, 4000 and 8000 Hz. Available rates depend on connection and firmware.

[meech-io/logi-cli](https://github.com/meech-io/logi-cli) provides a small MIT-licensed macOS implementation tested against **PRO X 2 DEX C0A0** and receiver C547. It documents internal errors from live setters and persistence through profiles. [Solaar #2836](https://github.com/pwr-Solaar/Solaar/issues/2836) independently records DEX USB C0A0 and WPID 40B8. [libratbag #1813](https://github.com/libratbag/libratbag/issues/1813) supplies a **SUPERLIGHT 2c C09F** lead. Treat a report-rate ceiling and a profile's ability to encode a rate as different facts.

## Onboard profile formats beyond the current implementation

[OpenMouse profile research](https://github.com/OpenMouse-Project/mouse-protocol/blob/b62a1cce2b63698ac00595efdb0067ecd54e34a0/docs/logitech-onboard-profiles.md) is unusually detailed: it traces eight numeric layout cases to OMM 2.6.1749 and includes later hardware-diff observations. The reference has AGPL licensing and its prose has an outdated top-level “unverified” status alongside hardware-confirmed subsections. Treat each claim by its local provenance, not the opening status alone.

The useful leads, **not a write specification approved for G-Lite**:

- Numeric formats 1–5 share an older base; 6–8 use another base. Do not accept a new format just because an older parser returns plausible DPI values.
- The newer base separates wireless and wired report-rate bytes at offsets `00` and `01`, represented as rate indexes rather than milliseconds.
- Five-byte DPI stage entries contain little-endian X, little-endian Y and a lift-off byte. The reference's hardware subsection places stages at `04`, `09`, `0E`, `13`, `18` and says the region extends through `1C`.
- Default and shift stage indexes move to `02`/`03`; button tables move to `30` and `70`. This differs materially from the old profile parser.
- It reports a real original SUPERLIGHT C094/WPID 4093 with **format 4**, whereas G-Lite supports writes only for format 3. Do not classify all format-4 devices as keyboards merely because libratbag names that format `PROFILE_TYPE_G915`.
- CRC is CCITT-FALSE with initial FFFF, stored big-endian in the final two sector bytes. Reference-specific valid-CRC examples are good verification fixtures for future work, but no code or binary fixture was copied here.
- Some devices reject a 16-byte read that crosses a 255-byte sector boundary. The reference recommends reading the final block from `size - 16`; capture firmware behavior before changing G-Lite.

There are unresolved contradictions. OpenMouse interprets a directory flag as “active” in one section; libratbag/Solaar and Linux Superstrike treat it as “enabled.” OpenMouse's updated format-8 lift-off discussion also needs to be read with the older tables. Do not write a directory or profile based on those ambiguous claims.

## Superstrike / analog button telemetry

[mclol0/linux-superstrike](https://github.com/mclol0/linux-superstrike), MIT, has a [dedicated reverse-engineering report](https://github.com/mclol0/linux-superstrike/blob/b62b7601c36cd19053ca86709eedcbec8c26e13d/REVERSE_ENGINEERING.md). Its identity section records receiver **C54D**, WPID **40BD**, index **01**, and 36 features. It documents profile writes, measured polling rates, haptics, actuation, rapid trigger, buttons and a `80E0` bunny-hopping feature. Its later prose qualifies the initial claim that live setters do not work, so preserve the mode and firmware with every observation.

The report contains two alleged layouts: an older “configured” interpretation and a newer pristine factory interpretation. The former gives overlapping G-shift and name regions; the latter uses five-byte DPI stages. This is a **reason to request raw sanitized sector evidence**, not to implement heuristic writing. OpenMouse's numeric format dispatch is a useful independent cross-check.

[fawhfi/superstrike-sdk](https://github.com/fawhfi/superstrike-sdk), MIT, is a complementary **Windows-tested** reference for continuous `1B0C event 1` button-travel telemetry. Its README reports roughly 60 Hz on tested firmware. These are ADC/travel readings, not calibrated force, pressure or millimetres. C54D there is the receiver ID, not a wired mouse PID.

[zeex64/openghub](https://github.com/zeex64/openghub) is another implementation lead whose protocol document covers Superstrike profiles and haptics. It was discovered through its repository/readme; its packet claims were not independently reconciled in this pass.

## HID++ 1.0 and the earlier G mice

Start with MIT [libratbag `hidpp10.c`](https://github.com/libratbag/libratbag/blob/8235b5bb6032dea15901ab58a8218956f4494d08/src/hidpp10.c) and [its HID++ 1.0 driver](https://github.com/libratbag/libratbag/blob/8235b5bb6032dea15901ab58a8218956f4494d08/src/driver-hidpp10.c). Existing G-Lite [G9x notes](../G9x.md) are already based on the local device and LGS and take precedence for that hardware.

| Family | Concrete upstream evidence | Important distinction |
|---|---|---|
| G5 C041, 2007 C049 | Separate libratbag definitions | Old protocol and model-specific resolution behavior |
| G7 C51A | hidpp10 definition | Wireless legacy mouse; not a LIGHTSPEED HID++ 2.0 slot assumption |
| G9 C048 | `ProfileType=G9`, five profiles | Different layout from G9x |
| G9x C066 / COD C249 | `ProfileType=G500`, five profiles | Existing G-Lite support is live only; metadata disagrees on DPI granularity |
| G500 C068 / G500s C24E | `ProfileType=G500`, one profile | Sensor units/ranges differ |
| G700 C06B / G700s C07C; receiver C531 | `ProfileType=G700`, five profiles | Wired/wireless routes; bricking reports exist in old upstream issues |

Do not generalize the G9x's successful short-register writes into flash support for every HID++ 1.0 mouse. Read [libratbag #1425](https://github.com/libratbag/libratbag/issues/1425) and [#1563](https://github.com/libratbag/libratbag/issues/1563) as reported failures to investigate, not proof of a universally reproducible failure mode.

## G300/G300s and G600 are another protocol

MIT [G300 driver](https://github.com/libratbag/libratbag/blob/8235b5bb6032dea15901ab58a8218956f4494d08/src/driver-logitech-g300.c): active report `F0`, profile reports `F3/F4/F5`, profile length **35** bytes, four DPI stages, 250–2500 range, three indexed profiles. The constant named PROFILE_MAX is 2 because profile indexes are 0–2; it does not mean two profiles.

MIT [G600 driver](https://github.com/libratbag/libratbag/blob/8235b5bb6032dea15901ab58a8218956f4494d08/src/driver-logitech-g600.c): active report `F0`, profile reports `F3/F4/F5`, profile length **154** bytes, three profiles, four DPI stages, 200–8200 DPI. The 41 internal button/color records include the normal and G-shift layers; that is not a claim of 41 physical buttons. Solid, breathe and cycle lighting are distinct modes.

Local LGS probes use vendor page **FF80** for these mice. OpenRGB also has a [G600-specific controller](https://github.com/CalcProgrammer1/OpenRGB/tree/029758e2260208c6de06eefc7106cd5c462c2310/Controllers/LogitechController/LogitechG600Controller); it is GPL and lighting-focused. Solaar explicitly comments G600 out as a non-HID++ device. G-Lite's 7/20-byte long-collection discovery will not automatically cover 35/154-byte feature-report exchanges.

## Remaining early mouse gaps

LGS manifests identify G100/G100s/G90 C247, G400 C245, G400s C24C, MX518 C051/C01E. The newer MX518 HERO **C08E** has its own HID++ 2.0 libratbag definition; do not collapse it into the original MX518 driver. [lomoco](https://github.com/dougvj/lomoco) is a legacy USB mouse-control research lead, not verified coverage of every one of those models. Obtain model-specific control captures for G1/G3 and any other unlisted early G mouse rather than assigning guessed IDs.

## Cross-check against OpenMouse's current implementation

The inspected [onboard-profiles.ts](https://github.com/OpenMouse-Project/mouse-protocol/blob/b62a1cce2b63698ac00595efdb0067ecd54e34a0/src/drivers/logitech/onboard-profiles.ts) is a useful second reference beside that project's prose: it explicitly separates older/newer layouts, keeps unknown-format edit capabilities absent, and records the original C094 format-4 observation. Its current LOD encoding is **Low 1 / Medium 2 / High 3**; older tables in the prose are stale. [hidpp.ts](https://github.com/OpenMouse-Project/mouse-protocol/blob/b62a1cce2b63698ac00595efdb0067ecd54e34a0/src/drivers/logitech/hidpp.ts) distinguishes 2201/2202 and 8060/8061 and notes that a 2202 device can report no LOD control (G309 is its example). Thus **2202 presence does not by itself establish adjustable lift-off distance**. These implementation choices are not independent proof of every device's hardware behavior.
