# Community threads and historical archives

Searched 2026-11-08. These are discovery and regression leads. Technical conclusions in the main map use source files, vendor records or hardware captures where possible. A post saying a device “works on Linux” often means ordinary input/audio, not configurable lighting or onboard memory.

## Hacker News

HN was searched directly and through its public Algolia index. It is useful for finding project authors and links, less useful for exact PID/packet evidence.

| Thread | Date / evidence | Useful follow-through |
|---|---|---|
| [Solaar: Linux manager for Logitech devices](https://news.ycombinator.com/item?id=42454359) | 2024-12-18; discussion inspected | Links to Solaar/Piper and separate Bolt/Unifying/LIGHTSPEED experiences; do not import commenters' receiver-capacity guesses |
| [Solaar, later submission](https://news.ycombinator.com/item?id=49359931) | 2026-08-19; index discovery | Another route to maintainers/current project discussions |
| [The Clawd Grip](https://news.ycombinator.com/item?id=48880311) | 2026-07-12; index entry; HN page retrieval failed | [Author's article](https://louisabraham.github.io/articles/clawd-grip) leads to [opengcontrol PR #1](https://github.com/kasik96/opengcontrol/pull/1); PR was still open when inspected |
| [OpenLogi comment linking opengcontrol](https://news.ycombinator.com/item?id=49357408) | 2026-08-19; index result | Same author's CLI migration and battery work; follow the PR, not the utility's default README |
| [OpenLogi comment about G502 X support](https://news.ycombinator.com/item?id=49359547) | 2026-08-19; anecdote | The inspected libratbag checkout already has G502 X definitions, so the old “not supported” recollection is not a current inventory |
| [Piper and Solaar kept breaking](https://news.ycombinator.com/item?id=49667913) | 2026-09-12; index discovery | Links [DonGeronimo7/mouse-control](https://github.com/DonGeronimo7/mouse-control); project not audited in this pass |

## Reddit

| Thread | Why keep it | Evidence limit |
|---|---|---|
| [G304/G305 HID++ / G-Zero](https://www.reddit.com/r/MouseReview/comments/1tm4bw4/i_tried_reverse_engineering_the_logitech_hid/) | Author describes C53F receiver, FF00/usage 2, long-report discovery and links G-Zero | Author conflates dynamic feature IDs with indexes; verify actual code/captures before using command claims or persistence claims |
| [G HUB supported-device list](https://www.reddit.com/r/LogitechG/comments/r9so0e/g_hub_supported_devices_list/) | Historical 2021 breadth across mice, keyboards, audio, wheels, POWERPLAY | Not a current official support list and not protocol evidence |
| [Reverse engineering G HUB](https://www.reddit.com/r/LogitechG/comments/sdzdgi/reverse_engineering_logitech_g_hub/) | Points researchers to libratbag and Linux alternatives | Mostly project discovery; no single full protocol replacement |
| [G915 G1–G5 with libratbag](https://www.reddit.com/r/linux/comments/1vwebc5/making_the_logitech_g915_g15_keys_work_using/) | Extra USB-ID/device-definition lead | Editing a match file is not proof every variant shares storage layout |
| [G PRO X wireless on Linux](https://www.reddit.com/r/linuxhardware/comments/o5yrvz/logitech_g_pro_x_wireless_headset_users_on_linux/) | HeadsetControl discovery and host virtual-surround example | Audio playback/host DSP is distinct from headset hardware EQ |
| [G733 on Linux](https://www.reddit.com/r/logitech/comments/pzuwmi/) | HeadsetControl/Solaar endpoint and feature leads | Basic audio success is not a control-protocol test |
| [PRO X headset with G935 dongle](https://www.reddit.com/r/LogitechG/comments/13ccvep/) | Interesting pairing/identity experiment | Anecdotal; do not promise interchangeable firmware or dongles |
| [G502 HERO Linux replacement](https://www.reddit.com/r/LogitechG/comments/1xfh455/ive_used_linux_for_years_and_never_found_a_proper/) | Leads to [Ahmedxsaad/g502-hub](https://github.com/Ahmedxsaad/g502-hub) | Inspect implementation/protocol notes rather than treat a launch post as verification |
| [Superstrike tray battery request](https://www.reddit.com/r/LogitechG/comments/1vyt70z/any_working_pro_x2_superstrike_taskbar_battery/) | Links LGSTrayBattery; useful unresolved-demand signal | No new protocol evidence |
| [G502 X PLUS RGB configuration conflict](https://www.reddit.com/r/LogitechG/comments/1x9kms5/logitech_g502_x_plus_rgb_bug_g_hub_configuration/) | Device/host ownership and G HUB database lead | A database workaround is not a verified hardware fix |
| [G13 and current G HUB](https://www.reddit.com/r/LogitechG/comments/1xc5k8h/is_the_g13_gamepad_still_supported_in_the_current/) | Legacy support/doc-removal concern | Use the official retirement notice and libg15/G13 source for facts |

No Reddit posts were sent and no owners were contacted. These are links for subsequent evidence gathering.

## SourceForge and forums

| Resource | Why it matters | Limits |
|---|---|---|
| [g15tools project](https://sourceforge.net/projects/g15tools/) | Historical LCD and extra-key tools, GPL-2.0 | Old project; modern Windows transport not established |
| [g15tools files](https://sourceforge.net/projects/g15tools/files/) | libg15render/g15composer/daemon archive routes | Archive presence does not establish current maintenance |
| [g15tools development mailing list](https://sourceforge.net/p/g15tools/mailman/g15tools-devel/) | Old patches/model additions may contain missing protocol details | Search exact model/report ID; archive index only inspected here |
| [G19Linux](https://sourceforge.net/projects/g19linux/) | Separate libg19/libg19draw/g19d code repositories | Explicitly inactive; GPL-2.0 |
| [G19Linux macro-plugin crash](https://sourceforge.net/p/g19linux/discussion/1114046/thread/816d3a51/) | Historical plugin names and libg19 daemon stack | Host crash report, not a firmware failure |
| [Arch G510 hardware thread](https://bbs.archlinux.org/viewtopic.php?id=132502) | LCD worked while G-keys still emitted default F/number keys; points to Gnome15 | Demonstrates why LCD and G-key support need separate map fields; old user report |
| [Arch G510 solved thread](https://bbs.archlinux.org/viewtopic.php?id=160888) | Additional G-key mapping/configuration history | Follow through to source before extracting wire behavior |
| [Deskthority G9x review](https://deskthority.net/viewtopic.php?t=7559) | Physical buttons, historical onboard/host remapping expectations | Product review, not authoritative protocol bytes |
| [Deskthority Logitech Gaming wiki](https://deskthority.net/wiki/Logitech_Gaming) | Product-name/layout discovery | Contains simplifications; use manuals/source for exact key counts |
| [Linux Mint scroll-resolution thread](https://forums.linuxmint.com/viewtopic.php?p=2740716) | Reminder that saved settings/restore policy can cause apparent hardware regressions | Not specific proof of G-series feature behavior |

## Issue threads with stronger technical value

- [Solaar #2836](https://github.com/pwr-Solaar/Solaar/issues/2836): DEX identity and extended DPI failure/capture context.
- [libratbag #1813](https://github.com/libratbag/libratbag/issues/1813): SUPERLIGHT 2c C09F request.
- [Solaar #2998](https://github.com/pwr-Solaar/Solaar/issues/2998): mixed G535/G309 environment and a G309 WPID lead; extract the correct device block.
- [Solaar #3277](https://github.com/pwr-Solaar/Solaar/issues/3277): PRO X RAPID enumeration on an older Solaar build; [RigDeck's direct hardware document](https://github.com/gabriellaines/rigdeck/blob/b9a89b3d8a141922c54b4ba6de3eaae1bcf980e6/docs/protocols/logitech-pro-x-tkl-rapid.md) supplies much better protocol evidence.
- [OpenLogi #1675](https://github.com/AprilNEA/OpenLogi/issues/1675): G735 dongle enumeration versus a potentially different transport.
- [OpenRGB #4461](https://gitlab.com/CalcProgrammer1/OpenRGB/-/issues/4461): G915 X capture trail cited by the focused heatmap implementation; upstream discussion not fully inspected here.
- [libratbag #1425](https://github.com/libratbag/libratbag/issues/1425) and [#1563](https://github.com/libratbag/libratbag/issues/1563): G700s and rate-setting failure reports; useful regression scenarios, not a blanket judgment on the library.
- [LGSTrayBattery #148](https://github.com/andyvorld/LGSTrayBattery/issues/148) and [#150](https://github.com/andyvorld/LGSTrayBattery/issues/150): G522/G733 discovery gaps; current Centurion/PID work elsewhere may supersede them.

## Search pitfalls worth retaining

Old forum support claims quickly age. New source commits can already fix the reported gap. A project that wraps OpenRGB/g810-led/G HUB adds no independent protocol evidence. AI-assisted projects can contain internally inconsistent tables; inspect bytes/captures and avoid treating many repeated READMEs as independent confirmations. Default branch, an open PR, a fork and a published release are different evidence states.
