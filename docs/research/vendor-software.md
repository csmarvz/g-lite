# G HUB, Logitech Gaming Software and Onboard Memory Manager

Research date 2026-11-08. Vendor software is a source of product metadata and observable behavior, not reusable source code or assets. This pass did not install G HUB/OMM, run their services, read the user's G HUB database, or modify an installation.

## Official references and their limits

| Resource | What it establishes | Limitation |
|---|---|---|
| [G HUB official release notes](https://marketplace.logi.com/releasenotes/ghub/en) | The rendered page labels 2026.7.992920, dated 2026-10-12, as latest | The text extraction shows many section headings without their body; do not invent the missing new-device lists |
| [Logitech support G HUB entry](https://support.logi.com/hc/en-au/articles/43643050172055-Logitech-G-HUB) | Names G512 X 75/98 firmware fixes and a LIGHTSPEED receiver firmware fix; carries LGS retirement notice | The page's specific download entry is an older macOS build, not a universal latest-version record |
| [OMM support entry](https://support.logi.com/hc/en-us/articles/360059641133-Onboard-Memory-Manager) | Official landing point and current support-site notices | Dynamic body did not expose a current Windows version/download in this pass |
| [Developer Alliance / SDKs](https://www.logitechg.com/en-us/programs/developer-alliance) | LED illumination, steering wheel, GamePanel and G-key integrations | An application integration SDK is not the raw hardware protocol |
| [Partner Developer Lab](https://www.logitechg.com/en-za/programs/partner-developer-lab) | Explicitly says G HUB is needed for its SDKs | Useful for capability expectations, not a standalone G HUB replacement |
| [G915 TKL quick-start manual](https://www.logitech.com/assets/65920/g915-g913-tkl-qsg.pdf) | Physical controls/layout reference already used by the repo | Does not describe 8100 memory or write safety |
| [G915 X official specification](https://support.logi.com/hc/en-gb/articles/25605873768343-Specification-G915-X) | Official wired-product identity and accessories | No control-protocol schema |

The older official “What products are supported by G HUB” article at `360023192454` was inaccessible through the web tool. The [2021 Reddit reproduction](https://www.reddit.com/r/LogitechG/comments/r9so0e/g_hub_supported_devices_list/) is a historical breadth lead, not a current authoritative support list.

Logitech's current support notice says LGS downloads and documentation were scheduled to be removed on **2026-10-31**. That date has passed as of this research. The notice does not prove every legacy download host is already unavailable; the existing repository's direct LGS x64 URL was not downloaded or executed in this pass. Keep [local factual manifest notes](lgs-manifest-inventory.md) rather than promising that link will continue to work.

## G HUB's device database and depot system

MIT [OpenGHub `depot.rs`](https://github.com/Slyvan25/OpenGHub/blob/55822ca9e739c85e596217c110cc971b11386fba/src-tauri/src/depot.rs) is a valuable new metadata reference. Its author traces the format to a G HUB 39.1 ProgramData tree plus network capture:

- `C:\ProgramData\LGHUB\current.json` lists per-build depots, URLs, sizes and hashes.
- `core/data/devices/devices_NNNN.json` maps interface IDs such as `046d_<pid>` to a model ID, display name, depot and lighting descriptors.
- Device depots can contain `metadata.json`, zone/button geometry, capability/UI definitions and separate firmware metadata.
- The author documents a public unencrypted depot container and separately recognizes encrypted entries. Only public plaintext metadata is relevant here; do not infer unannounced hardware from encrypted filenames.

No ProgramData LGHUB tree was present at the checked location in this environment, so **this pass did not extract an official G HUB catalog**. The parser is a reproducible route for a later factual inventory when an existing installation is available. Do not describe the OpenGHub README's broad claims as if every device had been validated.

Useful distinction: a G HUB `modelId`, a registry interface ID, a USB PID, a wireless PID, a receiver PID, a UI slot identifier and a physical button number are different things. Store relationships with provenance, not one interchangeable string.

Product artwork and masks remain Logitech's property. Geometry/capability observations may be useful reference facts, but this research adds no copied images, depots or vendor code to G-Lite.

## G HUB user settings, imports and local telemetry

[gabfv/logitech-g-hub-settings-extractor](https://github.com/gabfv/logitech-g-hub-settings-extractor), MIT, documents extracting a JSON blob from SQLite `settings.db`. [A-Bomb/ghubMacroEditor](https://github.com/A-Bomb/ghubMacroEditor) is an additional macro/schema lead. Both manipulate G HUB state; they are not hardware drivers. Their existence does not guarantee schema stability across G HUB releases.

[OpenGHub profile documentation](https://github.com/Slyvan25/OpenGHub/blob/55822ca9e739c85e596217c110cc971b11386fba/docs/profiles.md) and source describe importing per-game profiles, DPI, rate, lighting and assignments from this database. They explicitly skip integrations without equivalents. Treat integration actions, host macro playback, executable matching and onboard assignments as separate map capabilities.

[LGSTrayEx GHubManager.cs](https://github.com/strain08/LGSTrayEx/blob/d43905bd911e30bbfa1a030f6d5eb617f08874ec/LGSTrayCore/Managers/GHubManager.cs), GPL-3.0, implements a local **ws://localhost:9010** client with device/battery messages and subscriptions. Its [native HID path](https://github.com/strain08/LGSTrayEx/tree/d43905bd911e30bbfa1a030f6d5eb617f08874ec/LGSTrayHID) is the route that can work independently of G HUB. A websocket-derived battery feature must be labeled **G HUB dependent**, even if a tray utility looks independent.

[xcogito/Logitech_GHub_Fix](https://github.com/xcogito/Logitech_GHub_Fix) is a version-specific settings-schema incident report. It is useful for understanding failure modes and backup expectations, not evidence that every large settings.db is corrupt. No repair scripts from these projects were run.

## LGS local manifest evidence

The repository already has [LGS notes](../lgs.md). This pass expands them with [51 physical-device manifest entries](lgs-manifest-inventory.md), read from installed `Resources/**/Device_Manifest.xml`, without copying the proprietary XML files.

Important discoveries:

- Manifest models can be aliases: G100/G100s/G90 and color variants share C247; G19/G19s share C229.
- G302's manifest also contains a C07E probe, but G402 independently owns C07E in other references. Do not add a spurious G302 alias from that one file.
- Some older G devices use page FF80 or no HID probe at all. Their UI presence in LGS says nothing about HID++ 2.0 support.
- Arx and LCD emulators have pseudo-VID/PIDs; they were excluded from the physical USB inventory.
- G510 C22D/C22E is more than a retail suffix distinction: libg15's table labels the two modes without/with audio enabled.

The previous notes' host XML profiles, hardware profiles and G HUB handoff list remain separate reference sources. Nothing from `%LOCALAPPDATA%` user profiles was needed for this pass.

## OMM as a reference, without relying on it at runtime

OMM is useful for observing persistent mouse behavior independently of G HUB's application integration layer. [OpenMouse's profile research](https://github.com/OpenMouse-Project/mouse-protocol/blob/b62a1cce2b63698ac00595efdb0067ecd54e34a0/docs/logitech-onboard-profiles.md) says it recovered numeric format builders from OMM 2.6.1749 and later compared some with real hardware. G-Lite should keep its own implementation and require matching device dumps before supporting another layout.

“OMM supports this mouse” and “G-Lite can safely write its profiles” are separate claims. OMM can contain model-specific knowledge that a generic 8100 parser does not have. This pass did not decompile or distribute OMM binaries.

## Suggested map capabilities for vendor comparisons

Track: basic input; battery source; live DPI/rate/lighting; onboard settings; button remapping; G-shift; simple onboard key bindings; onboard macros; host macro engine; app-specific profile switching; per-key streaming; built-in lighting effects; power/idle policy; sidetone; device EQ versus host audio processing; LCD rendering; wheel configuration; force-feedback driver; firmware update; and SDK/game integration. A device can support a subset without being a full G HUB replacement.
