# Corrections, unresolved questions and next captures

Research date 2026-11-08. These are documentation findings and future evidence requests. No implementation changes or device probing were performed.

## Corrections to earlier notes and common reference claims

| Earlier / tempting assumption | Better evidence | Map treatment |
|---|---|---|
| G502 X family likely needs 8061 | Actual Solaar G502 X C099 and X PLUS 4099 reports contain 8060v0 | Mark the existing [8061 note](../report-rate-8061.md) as speculative for those products; use per-firmware feature discovery |
| No G mouse 8061 recording exists | Solaar has PRO X 2 40A9 and Superstrike 40BD captures | Record 2202/8061 as observed for those units |
| Format 4 means G915 keyboard | OpenMouse reports original SUPERLIGHT C094/WPID4093 format 4 | Numeric format and device type are independent |
| Original G915 has a second WPID 407E | Contents of the 407E-named Solaar file say 407C | Ignore the erroneous filename ID |
| G213 C366 | Its capture contents, descriptor and OpenRGB say C336 | Use C336, retain typo only in source filename |
| C33C uniquely identifies G513 | OpenLogi recording labels it G512 | Keep G512/G513 alias collision and firmware/name evidence |
| G302 can be C07E | One LGS manifest probes it; other references identify G402 C07E | Flag collision; do not auto-classify G302 from this probe |
| C092 is G305 | opengcontrol default README says so; libratbag/OpenRGB say G203 LIGHTSYNC | Reject that README mapping; use actual mouse WPID/name behind receiver |
| opengcontrol default branch has the G502 implementation described in the blog | PR #1 remained open at inspection | Link PR/fork explicitly; don't claim merged support |
| C22D/C22E necessarily mean G510 versus G510s | libg15 labels them audio-disabled/audio-enabled G510 modes | Keep both IDs and audio mode; do not assign retail suffix solely from PID |
| C547 identifies one specific keyboard/mouse | OpenRGB handles several products behind it | Receiver identity separate from paired-device identity |
| PRO X 2 headsets all use 8300/1F20 HID++ | HeadsetControl has separate 0AF7 Centurion implementation | Split by variant/transport |
| ASTRO A50 is one Logitech HID++ class | Gen 4 uses 9886:002C; Gen 5 046D:0B1C report 02 | Different VID/protocol routes |
| Superstrike profile “configured layout” is safe to write generically | Two focused references disagree; one printed table overlaps name/G-shift regions | Require sanitized raw sectors + format + before/after captures |
| 8101 fn9 called “commit” proves flash persistence | RigDeck's actual PRO X RAPID experiment says those writes were live and directory unchanged | Separate apply/activate/file staging/flash persistence and retest after power cycle |
| C232 uniquely identifies a retail G813 | OpenRGB calls it G813, USB registry calls it Gaming Virtual Keyboard | Treat as contested identity until a real descriptor/report resolves it |
| Every new app is independent evidence | Several wrappers/forks derive from Solaar/OpenRGB/g810-led | Record provenance/lineage; don't count copies as independent confirmations |

Existing notes were preserved; this dated research directory supersedes only the specific assumptions above. Hardware findings already recorded by G-Lite remain the strongest evidence for its tested units.

## Current repository discovery and control gaps

These come from reading `app/Devices.cs`, `app/Mouse.cs` and `app/Report.cs`, not running them:

- `Found.IsMouse` requires 2201. Known 2202-only captures establish a real missing family.
- `GMouse` sets DPI with 2201 and rate with 8060. Newer argument layouts/rate indexes require separate dispatch.
- Mouse writable profiles are gated to format 3. That restriction should stay until other formats are validated, even if upstream libraries accept them.
- Device discovery only considers Logitech VID 046D, collections with output length 7 or 20, and requires a long collection. This is insufficient evidence of discovery for Centurion, ASTRO VID9886, X56 VID0738, custom G300/G600 feature reports or modern wheel replies.
- Receiver enumeration probes slots 1–6. libratbag's POWERPLAY mat explicitly uses index 7.
- Battery through 1001/1F20 is converted with a straight-line estimate. Preserve “approximate”; 1F20 behavior differs by device class/feature version.
- Name/type routing accepts some G/PRO/ASTRO names, but that cannot recover devices excluded at the transport layer.
- `Report.Reads` presently has 2201/8060, not 2202/8061/1B08/1B0C. The feature list still helps identify those devices, but it does not decode their state.
- The report reader authorizes by function number only. A getter/setter multiplexed on an argument needs an argument-sensitive read policy before it can safely join a general report. Existing lighting notes already call out 8071 fn5/fn7.

## Prioritized hardware evidence

Priority here means research payoff relative to G-Lite's existing implementation, not market share.

| Order | Hardware family | Exact evidence to request | What it resolves |
|---|---|---|---|
| 1 | G502 HERO/LIGHTSPEED/X PLUS, G403/G703/G903, G304/G305 | G-Lite report, separate USB/receiver IDs, firmware, active profile descriptor and sanitized sector | Largest classic mouse cluster, LED slots, profile-index quirks and mode behavior |
| 2 | SUPERLIGHT 2 / DEX / 2c, PRO 2 | Full feature/version list, 2202 sensor capabilities, 8061 per-link masks/current values, 8100 descriptor + one original sector | Extended DPI/rate path and correct profile-format association |
| 3 | G915/G915 TKL/G815 | Both wired/receiver reports, cluster/effect enumeration, 8100 format/size + active sector, persistence observation after power cycle | Safely extend original-keyboard lighting/storage |
| 4 | G512/G513/G410/G610/G810/G910 | Exact PID + product string + 4540 layout, 8070 capabilities, 8080 block enumeration, 8100/8101 presence | Older keyboard screen/capability separation, C33C aliases |
| 5 | G733/G935/G535/PRO X | Dongle descriptors, report lengths, feature versions, sidetone range and mic-open test, actual sleep policy | Expand headset coverage without assuming one battery/auto-off behavior |
| 6 | G915 X, PRO X TKL RAPID, G515 variants | Exact retail variant/PIDs, feature/version list, 8101 metadata and read-only files; sanitized single-change G HUB capture | New keyboard profile management; analog settings/storage separate from RGB |
| 7 | G300/G300s/G600 | HID descriptor and feature-report read captures; original profile bytes | New custom report transport, not generic HID++ |
| 8 | G522 / PRO X 2 0AF7 | FFA0 descriptors, framing/bridge discovery, read-only capabilities, hardware EQ observation | Centurion transport and feature namespace |
| 9 | POWERPLAY | Receiver C53A descriptors plus endpoint/index-7 query evidence | Mat endpoint separate from mouse |
| 10 | G13/G15/G19/G510 and G5/G7/G500/G700 | Exact revision and interface descriptors, bounded known reads only, original settings snapshots | Legacy device-class transport and model-specific profiles |
| 11 | G560, X56, wheels / ASTRO | VID/PID + interfaces + read-only protocol identity first | Additional product classes beyond current G-Lite architecture |

These are future requests, not instructions to an owner to run random writes. Capture a single known UI action in the vendor app, retain request and reply, and compare before/after state. Do not execute scripts from discovered projects just to collect evidence.

## Minimal capture record for the map

Record date, retail model/revision/region, OS, firmware, USB/receiver/WPID/Bluetooth IDs, connection, HID page/usage/interface and report lengths, feature IDs/versions, profile format and size, current mode and active profile, reference tool/commit, and observed result. Do not publish serial numbers, unit IDs, friendly names, device-instance paths, account data or application profiles containing private macros.

For persistence questions record three separate observations: immediate visible behavior, reconnect behavior and full power-cycle behavior. A successful HID acknowledgment or byte-for-byte readback establishes neither audible EQ application nor nonvolatile persistence by itself.

## Unresolved protocol questions

- Which precise models/firmwares use 8100 numeric formats 4–8, and which fields vary by layout or link?
- Does the 8100 directory flag mean enabled for all implementations? One OpenMouse paragraph infers active; other references disagree.
- Are lift-off encodings and stage-enable bytes consistent across actual Superstrike revisions? Read the reference's later corrections before using its older summary tables.
- Which G915 X PIDs correspond to wired/full-size/TKL/LIGHTSPEED and which virtual node produced the unconventional index observations?
- Which 8071 ownership mode allows firmware idle behavior while host colors are visible on each keyboard? Treat logirgb's results as its tested-device observations, not a global answer.
- Which newer 8101 writes are temporary file application, which activate stored files, and which actually modify user flash?
- Can G735 use an existing Centurion or HID++-like transport, or does it need a separate implementation?
- Which legacy keyboard control interfaces are available through standard Windows HID without replacing the normal keyboard driver?
- Which new products are configurable only through host processing versus hardware state? Blue VO!CE, surround audio and app integrations need independent treatment.

## Scope left open

Unknown IDs remain for several modern keyboard/entry-level audio/flight variants. G HUB device depots were documented as a reference route but not available locally to enumerate. The [secondary reference queue](additional-reference-leads.md) is intentionally retained for future work; its entries have not all been audited. This is a substantial research pass, not a claim of exhaustive reverse engineering.
