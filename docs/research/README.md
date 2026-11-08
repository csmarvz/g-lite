# Logitech G device research

Research date: **2026-11-08**. This directory is a source-backed expansion map for G-Lite, not a supported-device list. No application code was changed, no device commands were sent, and no firmware or user profiles were modified.

## Start here

| Document | Contents |
|---|---|
| [Device map](device-map.md) | Product families, identifiers, protocol route, strongest evidence and remaining gaps |
| [Source catalog](sources.md) | Repositories, protocol documents, capture collections and licenses |
| [Mouse protocols](mice.md) | HID++ 1.0, classic HID++, newer DPI/rate features, G300/G600, profile formats and Superstrike |
| [Keyboard protocols](keyboards.md) | Legacy LCD keyboards, older RGB boards, G915/G915 X/G515 and newer models |
| [Headsets and ASTRO](headsets.md) | Device/capability declarations, HID++, Centurion and ASTRO transport boundaries |
| [Wheels and other devices](wheels-and-other.md) | G25/G27/G29/G920/G923/PRO/RS50, POWERPLAY, G560 and flight controls |
| [G HUB, LGS and OMM](vendor-software.md) | Official documentation, device metadata, settings formats and SDK limits |
| [Additional source queue](additional-source-leads.md) | Secondary links from logirgb, explicitly not independently audited |
| [Community leads](community.md) | Reddit, Hacker News, SourceForge, forums and issue threads, with evidence quality |
| [Corrections and next captures](next-captures.md) | Conflicting claims, current code coverage gaps and specific evidence still needed |
| [libratbag inventory](libratbag-device-inventory.md) | 53 G/MX518 device definition files, exact match selectors and quirks |
| [Solaar capture inventory](solaar-feature-evidence.md) | 27 checked-in G-device reports and observed feature sets |
| [LGS manifest inventory](lgs-manifest-inventory.md) | 51 physical-device manifest entries, including aliases and revisions |
| [USB ID registry cross-check](usb-id-registry.md) | 79 identifier/name records, including legacy component and mode IDs; no protocol claims |
| [Pinned source snapshots](snapshots.md) | Exact commits for 27 inspected reference checkouts |
| [More sources](more-sources.md) | Second pass: the G9x flash write, profile formats 1–5, headset EQ, Centurion, G560, G600 |
| [GitHub search results](github-search.md) | 260 more repos from model and topic searches, unread, with licence and activity |

The combined map currently contains **136 rows** for products, aliases, variants, components and unresolved backlog groups. That is not a count of supported or independently tested devices.

## Evidence rules

- **Capture** means a checked-in diagnostic report or an author-described hardware capture. It proves what that unit/firmware/connection exposed, not every sibling model.
- **Implementation** means a source file contains the route or device declaration. It is useful evidence but does not mean it was tested on Windows or in G-Lite.
- **Vendor** means an official manual, manifest or release note. A marketing capability does not reveal its wire protocol.
- **Lead** means an issue, forum report, project claim or incomplete reference that needs confirmation.
- **Inference** is explicitly labeled. A related product name, sensor or LIGHTSPEED logo is insufficient proof of protocol compatibility.

Unless a different VID is shown, Logitech IDs use **046D**. Keep **USB PID**, **receiver USB PID**, **wireless PID (WPID)**, **Bluetooth PID**, and **HID++ device index** in different fields. Linux can expose a receiver child with its WPID as a USB match selector. A shared C539/C547/C54D receiver does not identify the paired product by itself.

Record feature **ID and version**, transport, firmware, profile format, sector size, and capabilities independently. Feature indexes are discovered at runtime and are not stable IDs. A model may have several hardware revisions and region names.

## Most useful findings

1. There is a large HID++ mouse expansion path, but G-Lite's `2201`-only predicate excludes newer `2202` mice. Newer `8061` rates and profile formats also require separate handling.
2. Recorded G502 X and G502 X PLUS devices still have `8060`; do not assign `8061` from the X name or price tier.
3. G300/G300s and G600 use proprietary feature reports. There is MIT-licensed reference code in libratbag; they are not generic HID++ mice.
4. G915 X lighting has concrete small-project references. It must stay separate from the original G915's `8100` profile route; newer keyboards can use `8101`.
5. PRO X 2 headset variants span HID++ and Centurion. ASTRO A50 Gen 5 uses a third protocol; Gen 4 can use VID 9886.
6. The strongest breadth sources are device definition tables and hardware reports. The strongest depth sources are focused projects with captures. Their claims sometimes conflict; [the open questions](next-captures.md) preserve those conflicts.
7. Logitech's support site now carries a notice saying LGS downloads/documentation were scheduled for removal on **2026-10-31**. Preserve factual manifest inventories and stable references; the old download URL in the repository is not a guarantee of continued availability.

## Scope and limitations

This pass inspected the repository and sibling libratbag/OpenLogi checkouts, fetched 25 additional public reference repositories into the system temporary directory, searched web/issue/forum sources, and read installed LGS device manifests. It did not install or run the researched applications. The existing sibling repositories were not pulled or changed. Public source links and pinned commits make the notes usable without those temporary checkouts.

There is no claim that every Logitech G product has been found, that all IDs are exhaustive, or that the listed features work in G-Lite. New and undocumented products remain in the backlog with unknown identifiers instead of guessed values. This repository's MIT rule remains in force: take protocol facts from GPL/AGPL references, never their code or assets.
