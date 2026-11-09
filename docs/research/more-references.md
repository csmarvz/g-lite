# More references

Found on 2026-11-08 by following the leads in this directory: GitHub searches by model and topic, then a read of the most promising repos. About 165 repos turned up that the other files don't list; the ones below have protocol content. No code was run and nothing was sent to a device.

Licences matter: G-Lite is MIT. From GPL, AGPL, CC-BY-SA, PolyForm or unlicensed repos, take facts only, never code or doc text.

## G9x saving (HID++ 1.0 flash write)

The missing piece in [G9x.md](../G9x.md). cvuchener/hidpp, cvuchener/g500, libratbag and vsht/g500-control agree, and an LGS capture of a G700s save shows the same sequence.

1. `10 00 80 0F FF 00 00`: switch to the factory profile while writing.
2. `10 00 80 A1 01 00 00`: reset the upload sequence number. Before every upload.
3. Upload 256 bytes to RAM page 0:
   - First report `11 00 92 00` + `01 <page> <word offset> 00 00 <length BE16> 00 00` + the first 7 data bytes. Length in bytes, offset in 16-bit words.
   - Then `11 00 93 <seq 1, 2, …>` + 16 data bytes each. Pad the last one (g500 pads with FF).
   - Each report is acked with `10 00 50 <status> <seq> …`. Status 01 is OK. 0x90/0x91 is the same without acks.
4. Erase the page: `11 00 82 A0 02 00 00 00 00 00 <page> 00 …`. LGS also sends `01 00` at parameter bytes 10–11.
5. Copy RAM to flash: `11 00 82 A0 03 00 <src page> <src word> 00 00 <dst page> <dst word> 00 00 <length BE16> 00 …`. Flash bits can only be cleared, so erase first.
6. Second half: A1 reset, upload the next 256 bytes, copy to word offset 0x80 (no erase).
7. `10 00 80 0F 00 <directory index> 00`: load the profile again.

The CRC at bytes 510–511 is computed by the host. Never rewrite the directory (page 1).

A safer first step from g500-control: upload a profile to RAM page 0 only, then `10 00 80 0F 01 00 00` (load the profile at page 0, word 0). That changes the mouse with no flash write, and a replug undoes it.

Not checked on a G9x: that its firmware takes this exact sequence, and the size of its RAM page (400 bytes on the G500).

More G500/G9x facts:

- 0x22 angle snapping: the references disagree (02 read as on, hidpp writes 01). 0x24 lift threshold, 16 + n for n in −15…15. 0x25 unknown, default 0x10.
- Buttons: `83 <LE16>` specials: 01/02 wheel left/right, 04 DPI up, 08 DPI down, 05/09 the cycling versions, 10/20 profile next/previous, 11/21 cycling, 0x40 + (n<<8) profile n. `84` consumer usage: byte order disputed. `8F` off. Anything else is a macro address `<page> <word> 00`.
- Directory LED bits: libratbag uses `((0b111 << index) >> 2) & 7`.
- Register 0x51 LED nibbles: 1 off, 2 on, 3 blink, 4 heartbeat, 5 slow on, 6 slow off.
- A0 op 4 computes a CRC; 0x0F types: 00 by index, 01 by address, FF factory.
- libratbag reads 0x63 as LE16 X and Y at report bytes 4–7. That's payload bytes 0–3 after the 4-byte header, where G-Lite reads them. Same thing.

| Reference | Licence | Where |
|---|---|---|
| [cvuchener/hidpp](https://github.com/cvuchener/hidpp) | GPL-3.0 | `src/libhidpp/hidpp10/IMemory.cpp`, `ProfileFormatG500.cpp`, `hidpp/DeviceInfo.cpp` (C066 uses the G500 format) |
| [cvuchener/g500](https://github.com/cvuchener/g500) | GPL-3.0, docs CC-BY-SA-3.0 | `doc/hidpp10.md`, `doc/memory.md`, `doc/commands/a0-memop.md`, `doc/macro.md` |
| [libratbag](https://github.com/libratbag/libratbag) | MIT | `src/hidpp10.c` (upload ~2324–2490, erase/copy ~2255, profile write ~1820) |
| [vsht/g500-control](https://github.com/vsht/g500-control) | GPL-2.0 | `g500-control.c:787–841`, the RAM-only load |
| [DanForever/Open-Source-Gaming-Peripheral-Hub](https://github.com/DanForever/Open-Source-Gaming-Peripheral-Hub) | GPL-3.0 | `research/g700s/raw/onboard profile.txt`: the LGS capture, plus pcapng files |
| [pschmitt/aur-g9xled](https://github.com/pschmitt/aur-g9xled) | none | `g9xled.c`: register 0x57 only, nothing new |

## Capturing traffic on Windows

[USBPcap](https://github.com/desowin/usbpcap) with Wireshark, filtered on `usb.idVendor == 0x046d`, while LGS or G HUB saves. Decode with libratbag's MIT Lua dissector, [ratbag-toolbox `dissectors/hidpp-dissector.lua`](https://github.com/libratbag/ratbag-toolbox). It doesn't decode the 0x50/0x9x upload reports.

## Mice (HID++ 2.0)

| Reference | Licence | Evidence | What's new |
|---|---|---|---|
| [ZhichenJin/onboard-lite](https://github.com/ZhichenJin/onboard-lite) | MIT | PRO X Superlight (format 4), wired and LIGHTSPEED | C# 4.8 built with csc, decoded from OMM. One layout for formats 1–5. 0x8071 mice keep power-save LED copies at 0xE6/0xF1 (OMM writes half intensity) and flags at 0xFC. LED blocks: 01 fixed (p3 = 02 no ramp), 03 cycle, 0A breathing. `src/Profile.cs`, `src/Lighting.cs`, `src/Device.cs` |
| [Ahmedxsaad/g502-hub](https://github.com/Ahmedxsaad/g502-hub) | GPL-3.0 | G502 HERO C08B, format 2 | `docs/protocol.md` §3.6: format 1–5 offsets (2 shift DPI, 13–15 RGB, 16 power mode, 17 angle snap, 18–19 write counter to keep, 28–31 timeouts, 32 buttons, 96 G-shift buttons, 160 name, 208/219 LEDs, 230/241 power-save LEDs, 252 animation). §3.7 button encoding (`80 01` mouse, `80 02` key, `80 03` consumer, `90 <code>` special 00–11, `FF FF FF FF` unset). §6.2 0x2202, §5.2 0x8061. `docs/hardware-notes.md`: rewriting the active sector drops a live DPI change |
| [elberacasa/omalogi](https://github.com/elberacasa/omalogi) | unclear | G502 X C099 and LIGHTSPEED 409F, format 3 | `docs/hardware-tests.md`: factory specials are `90 xx FF 00`, keep the FF. Enabling a profile before the active one moves the current profile back by one without loading it |
| [Ruuvak/MyHUB](https://github.com/Ruuvak/MyHUB) | none | G502 X LIGHTSPEED 409F | 0x8100 events: fn0 new active sector on the profile button, fn1 new DPI index. Host mode survives a power cycle and stops onboard buttons and LEDs. fn3 on the active profile doesn't reapply the default DPI. The G502 X LIGHTSPEED has no 0x8070 or 0x8071. `crates/hidpp/tests/fixtures/frames.log` |
| [NexusVibe-Octet/gnexus](https://github.com/NexusVibe-Octet/gnexus) | Apache-2.0/MIT | G502 X PLUS dumps | Format 5. Byte 252 = 3 plays the factory animation over the LEDs, set 0 for a plain effect. Effect ids 0x0F and 0x10 unknown. `crates/logi-hidpp/tests/data/*.bin` |
| [efaraz27/g502x-control](https://github.com/efaraz27/g502x-control) | GPL-2.0 | G502 X PLUS C095 / 4099 | Live lighting needs host mode. 0x8071 fn5 `01 03 05`, fn1 effect, release `01 00 00`. 0x8081 per-LED |
| [leodbc/g502x-onboard](https://github.com/leodbc/g502x-onboard) | GPL-3.0 | G502 X LIGHTSPEED | The write-end ack isn't proof; read back. Last read window at offset 239. G HUB's "launch app" and "text" become no-ops onboard |
| [dukaci/driver-mouse-g502x](https://github.com/dukaci/driver-mouse-g502x) | none | G502 X C099, format 4 | One enabled but corrupt slot makes the mouse ignore every user slot. Current profile bit 0x100 means factory |
| [hex-dump/perihub](https://github.com/hex-dump/perihub) | check | G502 HERO tested | Windows/Linux app. Format 2 layout in `crates/perihub-core/src/hidpp/profile.rs`, device list with PIDs and WPIDs in `docs/devices.md`, G300 support |
| [Vex9th/mouse-control](https://github.com/Vex9th/mouse-control) | none | Offline tests only | Windows Go tool. `docs/LOGITECH_PROTOCOL.md`: 0x2202 fn6 `sensor X Y LOD` keeping LOD, 0x8061 mask BE16, software ids used by other tools (Linux 1, OpenRGB 7, libratbag 8, Solaar B, G HUB D) |
| [Roritharr/logitech-g502-led-off](https://github.com/Roritharr/logitech-g502-led-off) | MIT | Untested | Persistent off: 0x8070 fn3 per zone with persistence 1 |
| [xrfc/gpw-battery-tray](https://github.com/xrfc/gpw-battery-tray) | MIT | G Pro Wireless on C539 | C# tray app, 0x1001 voltage curve in `source/curves.txt` |
| [ymergame/g502-stage-mouse-macos](https://github.com/ymergame/g502-stage-mouse-macos) | MIT | — | 0x1C00 persistent remappable action, read only. Its profile code looks invented |
| [Java-Idl/universal-omm-webhid](https://github.com/Java-Idl/universal-omm-webhid) | MIT | G502 HERO C08B capture | Decompiled OMM 2.7.4428 (.NET 4.8 WPF, HID++ in the embedded `logi_nethidppio.dll`). `BUTTON_REMAPPING_SPEC.md`: OMM's button enums and a capture of a button write. AI-written; some claims are wrong (output and input on separate collections), so check each against a device |
| [marco-vrinssen/logitech-onboard-memory](https://github.com/marco-vrinssen/logitech-onboard-memory) | MIT | PRO X 2 DEX, LIGHTSPEED and wired | macOS app and CLI in Swift. Profile format 6 and up: buttons, a G-shift layer, LOD per DPI stage, wired and wireless report rates. Blocks writes to format 5 and older. `Sources/LogiHIDPP/ProfileDecoder.swift`, `ProfileEditor.swift` |
| [samyrataylor/g-manager](https://github.com/samyrataylor/g-manager) | MIT | G600 | Python, a field per profile byte: `app/devices/mouse/G600.py`, `app/fields/byte/*.py` (buttons, keys, modifiers, DPI, report rate, lighting) |
| [hwkim3330/logitech](https://github.com/hwkim3330/logitech) | none | — | WebHID onboard editor: DPI, buttons, RGB. `js/profile.js`, `js/constants.js`. Facts only |
| [RadNotRed/open-mouse-memory](https://github.com/RadNotRed/open-mouse-memory) | GPL-3.0 | — | Onboard memory manager. Facts only, not read |

## Keyboards

- [wentyss/signalrgb-logitech-g515-rapid-tkl](https://github.com/wentyss/signalrgb-logitech-g515-rapid-tkl), MIT, from G HUB captures: G515 RAPID TKL is wired PID C363. 0x8081 fn1 four keys `id R G B`, fn3 eight keys as 5-bit deltas, fn5 three ranges `start end R G B`, fn7 commit. G HUB claims with 0x8071 fn5 `01 03 07` then `01 03 05`. Busy errors above about 20 fps.
- [RalphJS/borochid-logitech-g915](https://github.com/RalphJS/borochid-logitech-g915), Apache-2.0, data only: G915 receiver C541, wired C33E, logo LED 210.
- [SignalRGB plugins](https://gitlab.com/signalrgb/signal-plugins), no licence: `Plugins/Logitech/` covers mice C081–C09D, G915 C33E/C343, receivers, and 17 older 0x8080 devices (G213, G413, G512 SE, G513, G560, G733, G810, G815, G910, G933, G935, Pro TKL and more). Good for PIDs and which feature each uses.

## Headsets and speakers

- 0x8310 EQ: no reference shows a write that's heard. On the G733 ([TheOlympusCloud/oma-logitech](https://github.com/TheOlympusCloud/oma-logitech) `docs/PROTOCOL.md`, MIT, checked on hardware) fn0 gives 10 bands ±12 dB, fn1 the frequencies, and fn3 only takes persist 2; 0 and 1 give error 02. g935-linux (PolyForm, facts only) writes persist 0 and 1 on a G935, without proof. Keeping EQ hidden is right; worth one test on the dongle: persist 0 and 1, and a power cycle after a 2.
- Working hardware EQ is only on Centurion headsets (HeadsetControl, GPL-3.0): PRO X 2 0AF7 feature 0x0636 (the PC computes biquad coefficients), G522 0B18 feature 0x020D (10 bands, ±6 dB). Centurion is 64-byte frames on usage page 0xFFA0 with a bridge feature 0x0003; other ids: battery 0x0104, auto sleep 0x0108, sidetone 0x0604, lighting 0x0621. `lib/devices/protocols/logitech_centurion_protocol.hpp`.
- Headset lighting is 0x8070 (G733 0AB5/0AFE/0B1F, G633 0A5C, G635 0A89, G933 0A5B, G935 0A87): the same fn3 G-Lite uses for mice. [felipearomani/g733-control](https://github.com/felipearomani/g733-control), MIT.
- 0x1F20 fn0 flags: 01 discharging, 03 charging, 07 full.
- G560 0A78 (MIT: [mugurc/g560-reverse-engineering](https://github.com/mugurc/g560-reverse-engineering), [AakashSehrawat95/g560-lights](https://github.com/AakashSehrawat95/g560-lights)): 0x8070 with four zones, about 7 ms between writes or they fail, boot animation off with fn5 `00 01 02`, 0x8040 brightness.

## G600 and G300

Not HID++. Feature reports on interface 1: 0xF3–0xF5 one profile each (154 bytes: RGB, effect, poll rate, four DPI ×50, 20 buttons, G-shift colour and buttons), 0xF0 active profile and DPI. MIT: [tulth/g600prog](https://github.com/tulth/g600prog), libratbag `driver-logitech-g600.c` and `driver-logitech-g300.c`.

## Details

The rest of what the reads found, by topic. Same licence rules as above.

### HID++ 1.0 (G500, G9x)

- Memory: the G500 has 19 pages, read-only factory profiles on 17 and 18. Page 0 is RAM: the first 400 bytes read as zeros at power-up, the last 112 are read-only. The G9x's RAM size isn't known.
- Uploading with 0x92/0x93 to page 2, word 0xFF, length 2 writes just the CRC. g500-control does this after its own edits.
- A0 op 4 computes a CRC: `04 00 <src page> <page count> 00 00 <dst page> <dst word>`.
- hidpp writes a page by erasing it and writing straight to flash, with no RAM step. libratbag and LGS go through RAM.
- libratbag also rewrites the directory (page 1) the same way. G-Lite won't.
- Registers (g500 `doc/hidpp10.md`): 0x00 notifications, 0x01 features, 0x54 LED intensity, 0x61 sensor, 0x64 accepts 1, 2, 3, 4, 5 and 8 ms. Error codes 0x01–0x0C are tabled there.
- Profile byte 0x03 "angle" is 0x80 in hidpp and unknown in libratbag. A stage with X = 0 ends the stage list. Stage LED bytes are LE16, 4 bits per LED.
- Name area after the buttons: "LGS02", the profile name (23 UTF-16LE characters), then 11 macro names of 17 characters.
- Macro opcodes (g500 `doc/macro.md`): 00 no-op, 01 wait for release, 02 repeat while held, 03 repeat, FF end. 20/21 key down/up, 22/23 modifiers down/up, 24 wheel. 40/41 mouse button down/up, 42 consumer (BE16), 43 delay (BE16), 44 jump, 45 jump if pressed, 60 move pointer, 61 jump if released with timeout. 0x80–0xFE are short delays.
- The receiver's 0xF1 sub 01/02 is firmware, 04 bootloader ([Lekensteyn/ltunify](https://github.com/Lekensteyn/ltunify) `registers.txt`, no licence file).
- [TimP4w/HaloDaemon-plugins](https://github.com/TimP4w/HaloDaemon-plugins) (GPL-2.0-or-later) has `logitech/lib/hidpp1.lua` and `hidpp2/onboard.lua`. Not read closely.
- HID++ 1.0 battery registers (Vex9th): 0x0D byte 0 percent, byte 2 high nibble 3/5/9 discharging, charging, full. 0x07 byte 0 levels 7/5/3/1.

### 0x8100 onboard profiles

- fn0 reply (onboard-lite): [1] format, [3] profiles, [4] ROM profiles, [5] buttons, [7–8] sector size. It edits formats 1–5 only.
- Directory enabled byte: OMM writes 00 for on and FF for off on the Superlight 1 ([rareinator/omm-rust](https://github.com/rareinator/omm-rust), MIT). The G502 X LIGHTSPEED uses FF for on. Keep the byte as read.
- Special function codes 0–17 are in onboard-lite `src/Actions.cs`. g502-hub examples: 07 DPI shift, 0A cycle profile, 0B G-shift, 0C battery, 0D select profile, 0F host. Macro opcodes and the length rule: g502-hub `docs/protocol.md` §3.8.
- fn7 replies carry a BE16 chunk counter. The counter at 0x12 in the profile is never changed by the device itself (MyHUB).
- getCurrentProfile is 1-based. Selecting a ROM profile or a disabled slot gives INVALID_ARGUMENT. Selecting the active profile does nothing (g502-hub). setCurrentProfile needs the BE16 sector; a bare index fails on ROM-only devices (opengcontrol, via ymergame).
- The report rate set is refused in onboard mode and works in host mode (g502-hub, G502 HERO), as on the G Pro Wireless.
- DPI LEDs may be 0x1300 (g502-hub, unchecked). SignalRGB also uses 0x1300 LEDControl.
- Macros (leodbc/g502x-onboard): the 5-byte jump is `60 <page BE16> <offset BE16>`. Macro pages may have no CRC. G502 X LIGHTSPEED firmware MPM 30.00.B0014: format 3, macro format 1, 2 ROM profiles.
- A HID++ 1.0 0x41 connection notice on COL01 carries the WPID (LE); byte 1 bit 0x40 is set while the link is down (MyHUB).
- MyHUB's `frames.log` also has the 0x8110 button-spy bitmap for the G502 X LIGHTSPEED.
- Button records, 4 bytes each, from OMM's enums (universal-omm-webhid), matching g502-hub §3.7:
  - `80 01 <bits BE16>` mouse button: 1 left, 2 right, 4 middle, 8 back, 0x10 forward.
  - `80 02 <modifiers> <key>` keyboard key: modifier bits 01 LCtrl, 02 LShift, 04 LAlt, 08 LWin, 0x10–0x80 the right ones; the key is the HID usage.
  - `80 03 <usage BE16>` consumer key: 00B5 previous, 00B6 next, 00CD play/pause, 00E2 mute, 00E9/00EA volume up/down.
  - `90 <function> ...` function: 01/02 tilt left/right, 03/04 next/previous DPI, 05 cycle DPI, 06 default DPI, 07 DPI shift, 08/09 next/previous profile, 0A cycle profiles, 0B G-shift, 0C battery, 0D profile n (in the last byte), 0E performance mode, 0x10/0x11 scroll down/up.
  - `00 ...` macro, `10` stop macro, `20` stop all macros, `FF FF FF FF` off.
- OMM supports HID++ 2.0 mice only: no keyboards, headsets or HID++ 1.0 mice. To map a setting without a capture: read the profile sector, change one thing in OMM, read again and compare.
- dukaci keeps slots in G HUB order G1–G11 and enables only slot 1, to avoid the corrupt-slot problem.
- 0x1C00 persistent remappable action (opengcontrol): fn1 count, fn2 `<index> FF` gives a control id, fn3 `<id BE16> FF` gives [3] action type (0 empty, 1 key, 2 mouse, 3/4 X/Y, 5/6 scroll, 7 consumer, 8 internal, 9 power), [4–5] value, [6] modifiers. Read only. Its "memory model 0, 3-page layout" with fn3/fn4 flash reads contradicts the spec: likely made up.

### Lighting

- onboard-lite picks 0x8071 over 0x8070 unless the feature is hidden (flag 0x40). 0x8070 with 0 zones means no lighting. OMM writes the power-save LED copies at half intensity and half RGB. Flags byte 0xFC = 0 means one effect per cluster.
- LED blocks (onboard-lite `src/Lighting.cs`): 01 fixed p0–2 RGB, p3 = 02. 03 cycle p5–6 period BE, p7 intensity. 0A breathing p0–2 RGB, p3–4 period BE, p5 waveform, p6 intensity. FF = never written.
- 0x8070 fn3 byte 1 is the zone's effect index from fn2, not the effect id. On the G502 HERO, index 2 is breathing and 3 cycle (g502-hub). libratbag always sends persistence 1; OpenRGB only on Save. 0x8070 fn5 is setNvConfig, fn10 clears a zone.
- Roritharr's persistent off sends the zone then a 2-byte effect id, not an index. Untested.
- 0x8070 effect ids include 08 boot and 09 demo; zone locations 1–11 ([Sys64k/logiled](https://github.com/Sys64k/logiled), MIT).
- G502 X PLUS: format 5, 4 LED slots (0/1 normal, 2/3 power save), the visible LED is slot 1 (libratbag quirk G502X_PLUS); 8 LEDs per efaraz27. Per-LED colour: 0x8081 fn0 pages 0–2 give the LED bitmap, Solaar's preparation step (fn1 with cluster FF), then fn1 `<led> R G B` and fn7 commit.
- openlogi-net packs the 0x8071 flags as `persistence | powerMode << 2` with volatile = 1. That matches [lighting-control.md](../lighting-control.md). On 0x8070, 0 is volatile.
- SignalRGB's Logitech plugin uses 0x1300, 0x2201, 0x8060, 0x8070, 0x8071, 0x8080, 0x8081 (fn1 per-key, fn7 apply), 0x8100 fn1 for onboard/host, 0x8110, 0x1B04, 0x4522, 0x8010–0x8040. Wireless ids 4053–40A2 (G715). Other plugins: G19, G600. The modern dongle plugin isn't public.

### Mice, other

- 0x2202 (Solaar, via g502-hub §6.2 and Vex9th): fn1 `<sensor>` reply [2] bit 0 separate Y, bit 1 LOD. fn2 `<sensor> <0 X / 1 Y> <page>` gives 3 header bytes then BE16 values with the same `E000|step` ranges as 0x2201, paged until `0000`, at most 32 pages. fn5 gives sensor, X, default X, Y, default Y, LOD. fn6 sets `<sensor> <X BE16> <Y BE16> <LOD>`; read LOD first and send it back. LOD 1 low, 2 medium, 3 high (openlogi-net `ExtendedDpi.cs`).
- 0x8061: index 0–6 = 8000 µs … 125 µs (gnexus `features.rs`). Event 0 means the rate changed.
- Vex9th validates each write by reading back and stops writes in onboard mode. Its battery notes for 0x1004, 0x1000, 0x1001 and 0x1F20 are careful: 0x1F20 flags 01/03/07, others unknown.
- IDs: G Pro Wireless on receiver C539 at index 1 (xrfc). G305 on C53F with 0x1000 (portyporty, no licence). POWERPLAY C53A. G502 X wired C098 and C099, LIGHTSPEED 409F. PeriHub's `docs/devices.md` lists receivers C539, C53A, C53D, C53F, C541, C543, C545, C547, C54D and many mouse PIDs.
- The G513 C33C in TeleBrady/g513-rgb uses 64-byte report 0x12 per-key writes, not 0x8071. Compare with [keyboards-8070.md](../keyboards-8070.md) when a G513 report comes.
- G915: 3 subprofiles and a BLE service UUID in borochid's data.

### Headsets

- PRO X Wireless (0ABA): gnexus `headset/mod.rs` confirms 0x1F20 and 0x8300 on usage page FF43, usage 0202, and adds 0x8310 fn0 = band count and dB range, fn1 = frequencies, 7 per call.
- G733 (oma-logitech, firmware U1 37.00.0131): 0x8310 fn0 `0A 0C 01` (10 bands, ±12 dB), frequencies 32 Hz to 16 kHz. fn2 location 0 (EEPROM) works, 1 (RAM) gives error 02. Whether a write is heard at once or after a restart is open.
- G935 (g935-linux): reads fn2 `01` active and `00` saved, writes fn3 with persist 0 or 1. g935-linux misnames 0x8070 and 0x8010 packets as "DSP"; its EQ part may still be right.
- Other EQ ids (g935-linux `probe.py`): 0x0201 headset EQ, 0x0213 EQ styles.
- PRO X 2 0x0636: fn1 `00` gives slot, band count, then frequency BE16, gain, Q per band. G HUB uses 5 bands at 80, 240, 750, 2200, 6600 Hz. fn2 writes the bands, a constant tail `05 5A E3 00 03 0E 00 02 00 00 00`, then biquad sections for playback (48 kHz) and mic (16 kHz).
- G522 0x020D fn2: `00 00 00`, then 10 bands of frequency BE16, Q × 32, gain (dB × 20 + 120) BE16. Default Q 22/32.
- Centurion framing: interface 3, usage page FFA0, 64-byte frames. Prefix 0x51 (PRO X 2) or `50 23` (G522), then length (payload + 1), flags `(fragment << 1) | more`, then `<feature index> <fn | swid>`. Bridge 0x0003 fn1 sends `<bridge index> 11 <size>` and a sub-message `00 <feature> <fn | swid>`. 56 bytes fit in the first frame, 60 in each next one. Reply byte 5 = FF means the headset refused. Bridge fn0 gives `03 E8 00 01 01` while the headset is on.
- Centurion settings: battery byte 0 percent, byte 2 = 1 or 2 charging. Sidetone fn1 `01 FF <level>` (v2) or `01 <level>` (v1). Auto sleep fn1 minutes. G522 lighting fn3 `00 00 04 00 B8 FC FF 00 AB 64` (two zones, brightness 100), zeros for off. Voice prompts fn5 `00 <on>`. Mic mute LED 0x0601 fn2.
- 0x8070 headsets: fixed indexes on the G733 and G935: 4 = 0x8070, 5 = 0x8010, 6 = 0x8310, 7 = 0x8300, 8 = 0x1F20. G733 zones 0 logo, 1 strips. G935 zones 0 logo, 1 front strip; slots 0 off, 1 fixed, 2 breathing, 3 cycle. fn 0x0E reads a zone's effect. The G935 sends 0x8070 fn 0x0C before a direct colour (n0xa/g935rgb, GPL-2.0).
- Voltage curve, G633 family (HeadsetControl): 4100 = 100 %, 3950 = 80, 3850 = 60, 3750 = 40, 3650 = 20, 3500 = 10, 3300 = 5, 3150 = 0.
- G733 auto-off: 0x1F20 fn1/fn2 from version 2. Power-button notice: `data[2] == 05 && data[4] == 02` (LogiG733Tray).

### G560

- Report `11 FF 04 3A <zone> <slot>` + parameters; persistence at byte 12. Slots 01 static, 02 cycle (5 zeros, rate BE16, brightness), 04 breathe (RGB, rate BE16, 00, brightness).
- 0x8070 fn4 `00 01` reads the boot animation, fn5 `00 01 02` turns it off, `00 01 01` on. Checked on hardware.
- 0x8040 brightness: fn1 get, fn2 set, BE16. 0x8320 and 0x8305 unknown.

### G600 and G300

- Profile report bytes: 1–3 RGB, 4 effect (0 solid, 1 breathe, 2 cycle), 5 rate 0–15 s, 11 poll rate 1000/(n+1), 12 DPI-shift DPI, 13 default DPI index, 14–17 DPI ×50 (max A4 = 8200), 31–90 20 buttons × (code, modifier, key), 91–93 G-shift colour, 94–153 G-shift buttons.
- Button codes: 11 DPI up, 12 DPI down, 13 DPI cycle, 14 mode switch, 15 DPI shift, 16 DPI default, 17 G-shift.
- 0xF0: read bits 1–2 resolution, 4–7 profile. Set resolution `F0 40|(i << 1) 00 00`, profile `F0 80|(i << 4) 00 00`.
- G300: 0xF1 active LED, 35-byte profiles.

### Not useful

hakangit/g512-keyboard-controller (OpenRGB client), geromet/g502-linux-control (ratbagd client), grumpybollocks/g910-control (keyledsctl wrapper), swylem/gpro-control (one constant), aubymori/LogiFadeFix (plays silence), szympik/logitech-gpro-x-battery-tray (G HUB websocket), mafik/logitech-g600-linux (evdev mapping), softmaxe/gbat (mouse battery only), pschmitt/aur-g9xled (0x57 only), aron-w/logi-omm-wine and zzoko/Onboard-Memory-Manager-macOS-workaround (run OMM under Wine). AI-built but with dated hardware logs: MyHUB, gnexus, g502x-onboard.

## Leads not read

From the topic search, unread: [lus/logy](https://github.com/lus/logy) (0BSD HID++ crate), [bamboostrip/logi-web-hub](https://github.com/bamboostrip/logi-web-hub) (WebHID), [ste4ik/g102-lightsync-control](https://github.com/ste4ik/g102-lightsync-control), [Lken-cmd/SuperLightBattery](https://github.com/Lken-cmd/SuperLightBattery) and [Ithilias/logitray](https://github.com/Ithilias/logitray) (Windows trays), [clovervidia/G403HID](https://github.com/clovervidia/G403HID) (C#, no licence), [Mirox921/logi-center](https://github.com/Mirox921/logi-center), [xyj-3/logitech-g-hub-supported-games](https://github.com/xyj-3/logitech-g-hub-supported-games) (G HUB data).

Skip: `destroylife535/Logitech-Onboard-Memory-Manager` and `totperla100551/Logitech-Onboard-Memory-Manager` look like download lures.
