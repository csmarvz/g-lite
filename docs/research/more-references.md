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
| [R4ULtv/gflick](https://github.com/R4ULtv/gflick) | MIT | Original PRO X SUPERLIGHT (format 4): 255 bytes written to disabled profile 0005 and read back, a name change checked then the original restored, a directory off and on. SUPERLIGHT 2 and G305: live settings only | Rust. Formats 1–5 as one layout: 0 report interval in ms (one rate for both links), 1 default stage, 2 shift stage, five LE16 stages from 3, timeouts at 28 and 30, buttons from 32, name at 160. Formats 6 and 7: 0 receiver rate, 1 cable rate (0x8061 indexes), 2 default, 3 shift, stages from 4 (X, Y, lift-off 1–3), 0x25 bunny-hop timeout in 10 ms (format 7, 0 or FF off), timeouts at 44 and 46, buttons from 48. A save changes only edited fields, refuses a source with a bad CRC, checks the sector didn't change since it was read, reads back and writes the original again on a mismatch. The SUPERLIGHT's 0x8070 is internal and hidden: its status light, not shown. `crates/gflick-core/src/mouse.rs`, `docs/feature-matrix.md` |
| [ZhichenJin/onboard-lite](https://github.com/ZhichenJin/onboard-lite) | MIT | PRO X Superlight (format 4), wired and LIGHTSPEED | C# 4.8 built with csc, decoded from OMM. One layout for formats 1–5. 0x8071 mice keep power-save LED copies at 0xE6/0xF1 (OMM writes half intensity) and flags at 0xFC. LED blocks: 01 fixed (p3 = 02 no ramp), 03 cycle, 0A breathing. `src/Profile.cs`, `src/Lighting.cs`, `src/Device.cs` |
| [Ahmedxsaad/g502-hub](https://github.com/Ahmedxsaad/g502-hub) | GPL-3.0 | G502 HERO C08B, format 2 | `docs/protocol.md` §3.6: format 1–5 offsets (2 shift DPI, 13–15 RGB, 16 power mode, 17 angle snap, 18–19 write counter to keep, 28–31 timeouts, 32 buttons, 96 G-shift buttons, 160 name, 208/219 LEDs, 230/241 power-save LEDs, 252 animation). §3.7 button encoding (`80 01` mouse, `80 02` key, `80 03` consumer, `90 <code>` special 00–11, `FF FF FF FF` unset). §6.2 0x2202, §5.2 0x8061. `docs/hardware-notes.md`: rewriting the active sector drops a live DPI change |
| [elberacasa/omalogi](https://github.com/elberacasa/omalogi) | unclear | G502 X C099 and LIGHTSPEED 409F, format 3 | `docs/hardware-tests.md`: factory specials are `90 xx FF 00`, keep the FF. Enabling a profile before the active one moves the current profile back by one without loading it |
| [Ruuvak/MyHUB](https://github.com/Ruuvak/MyHUB) | none | G502 X LIGHTSPEED 409F | 0x8100 events: fn0 new active sector on the profile button, fn1 new DPI index. Host mode survives a power cycle and stops onboard buttons and LEDs. fn3 on the active profile doesn't reapply the default DPI. The G502 X LIGHTSPEED has no 0x8070 or 0x8071. `crates/hidpp/tests/fixtures/frames.log` |
| [NexusVibe-Octet/gnexus](https://github.com/NexusVibe-Octet/gnexus) | Apache-2.0/MIT | G502 X PLUS dumps | Format 5. Byte 252 = 3 plays the factory animation over the LEDs, set 0 for a plain effect. Effect ids 0x0F and 0x10 unknown. `crates/logi-hidpp/tests/data/*.bin` |
| [efaraz27/g502x-control](https://github.com/efaraz27/g502x-control) | GPL-2.0 | G502 X PLUS C095 / 4099 | Live lighting needs host mode. 0x8071 fn5 `01 03 05`, fn1 effect, release `01 00 00`. 0x8081 per-LED |
| [leodbc/g502x-onboard](https://github.com/leodbc/g502x-onboard) | GPL-3.0 | G502 X LIGHTSPEED | The write-end ack isn't proof; read back. Last read window at offset 239. G HUB's "launch app" and "text" become no-ops onboard |
| [dukaci/driver-mouse-g502x](https://github.com/dukaci/driver-mouse-g502x) | none | G502 X C099, format 4 | One enabled but corrupt slot makes the mouse ignore every user slot. Current profile bit 0x100 means factory |
| [hex-dump/perihub](https://github.com/hex-dump/perihub) | MIT | G502 HERO, G502 X, G502 HERO Wireless, G300 saved; G915 X LS lit | Windows/Linux app for several brands. See [PeriHub](#perihub) below |
| [Vex9th/mouse-control](https://github.com/Vex9th/mouse-control) | none | Offline tests only | Windows Go tool. `docs/LOGITECH_PROTOCOL.md`: 0x2202 fn6 `sensor X Y LOD` keeping LOD, 0x8061 mask BE16, software ids used by other tools (Linux 1, OpenRGB 7, libratbag 8, Solaar B, G HUB D) |
| [Roritharr/logitech-g502-led-off](https://github.com/Roritharr/logitech-g502-led-off) | MIT | Untested | Persistent off: 0x8070 fn3 per zone with persistence 1 |
| [xrfc/gpw-battery-tray](https://github.com/xrfc/gpw-battery-tray) | MIT | G Pro Wireless on C539 | C# tray app, 0x1001 voltage curve in `source/curves.txt` |
| [ymergame/g502-stage-mouse-macos](https://github.com/ymergame/g502-stage-mouse-macos) | MIT | — | 0x1C00 persistent remappable action, read only. Its profile code looks invented |
| [Java-Idl/universal-omm-webhid](https://github.com/Java-Idl/universal-omm-webhid) | MIT | G502 HERO C08B capture | Decompiled OMM 2.7.4428 (.NET 4.8 WPF, HID++ in the embedded `logi_nethidppio.dll`). `BUTTON_REMAPPING_SPEC.md`: OMM's button enums and a capture of a button write. AI-written; some claims are wrong (output and input on separate collections), so check each against a device |
| [marco-vrinssen/logitech-onboard-memory](https://github.com/marco-vrinssen/logitech-onboard-memory) | MIT | PRO X 2 DEX (format 7), LIGHTSPEED and wired | macOS app and CLI (`lomm`) in Swift. Reads and writes profile format 6 and up: DPI stages with LOD, wired and wireless rates, buttons, the G-shift layer, the name. Blocks writes to format 5 and older. Layout in the README and `Sources/LogiHIDPP/ProfileDecoder.swift`, `ProfileEditor.swift`; see [Profile format 7](#profile-format-7) |
| [lexr1/omm.py](https://github.com/lexr1/omm.py) | GPL-3.0, facts only | G502 Proteus Core C07D, G502 HERO C08B, wired | Windows Python CLI over hidapi: 0x8100 profiles to and from JSON, buttons, macros, the four LED records, onboard mode. The only reference here that writes 0x8100 macros. `libs/FeatureOnboardProfile.py`, `HidppProfile.py`, `HidppMacro.py`, `HidppConstants.py`; see [0x8100 macros](#0x8100-macros-macro-format-1) |
| [tulth/g600prog](https://github.com/tulth/g600prog) | MIT | G600 | Python over pyusb, Linux. The three profile reports to and from JSON; g-manager is a rewrite of it. See [G600 and G300](#g600-and-g300) |
| [samyrataylor/g-manager](https://github.com/samyrataylor/g-manager) | MIT | G600 | Python, a field per profile byte: `app/devices/mouse/G600.py`, `app/fields/byte/*.py`. A rewrite of tulth/g600prog with the same layout and transfers; nothing g600prog doesn't have |
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

### 0x8100 info and modes (omm.py, lomm)

- fn0 reply: [0] memory model (both refuse anything but 1), [1] profile format, [2] macro format (omm.py refuses anything but 1), [3] profiles, [4] ROM profiles, [5] buttons, [6] sector count, [7–8] sector size, [9] mechanical layout (bits 0–1 = 2: the mouse has a G-shift layer), [10] various info. A G502 HERO answers `01 02 01 05 05 0B 10 01 00 0A`: format 2, five profiles, five ROM profiles, 11 buttons, 16 sectors of 256 bytes, G-shift.
- fn1 `01` onboard mode, `02` host mode. fn2 byte 0: 1 onboard. fn3 `<sector BE16>` selects; fn4 byte 1 the current profile.
- Directory entries: `00 <sector> <enabled> 00`; omm.py reads a first byte of 01 as "profile in ROM" and `FF FF FF FF` as an empty slot.
- Profile byte 0, formats 1–5: the interval in ms (1, 2, 4, 8). When the mouse has 0x8061, omm.py reads it as an index into 125, 250, 500, 1000, 2000, 4000, 8000 Hz instead, as format 7 does (untested above 1000 Hz, its README says). Byte 2 is the DPI-shift stage, bytes 13–15 a colour (unnamed), the name 48 bytes at 0xA0, four LED records of 11 bytes at 0xD0.
- 255-byte sectors (lomm, PRO X 2 DEX): the mouse refuses a 16-byte read that crosses the end, so the tail is read at offset 239 overlapping the chunk before. `MouseProtocol.ReadSector` already does this. Writes go in 16-byte chunks, the last padded with zeros.
- lomm writes a special as `90 <id> FF 00` and Disabled as `FF FF FF FF`. Special ids 0–0x11 as in the button records above (0F host switch).

### 0x8100 macros (macro format 1)

From omm.py (GPL-3.0, facts only), which reads and writes them on the G502 HERO. The same opcodes as g502-hub's `docs/protocol.md` §3.8.

- A button record `00 <sector> 00 <offset>` (two BE16) points to a macro. omm.py writes macro sectors without a CRC; PeriHub writes one, as the G815's macro sectors have ([G815-G915.md](../G815-G915.md)), and its macros were checked on a G502 HERO.
- Opcode length from the top three bits: 0x00–0x1F 1 byte, 0x20–0x3F 2, 0x40–0x5F 3, 0x60–0x7F 5. FF ends the macro.
- 00 no-op. 01 wait for release (the rest runs on release). 02 repeat while held. 03 repeat until another profile is picked.
- 20 `<signed>` wheel, 21 `<signed>` horizontal wheel.
- 40 `<ms BE16>` delay. 41 / 42 `<bits BE16>` mouse buttons down / up (bit 0 left to bit 4 forward). 43 / 44 `<BE16>` key down / up: a HID key usage in the low byte, or a modifier in the high byte (0x01 LCtrl to 0x80 RGui, as in `80 02`).
- 60 `<sector BE16> <offset BE16>` continues elsewhere. 61 `<BE16> <BE16>` moves the pointer (signed; omm.py isn't sure which is X).
- Where they go: sectors after the profiles, two per profile by default ((sectors − profiles − 1) / profiles). A macro is cut at 11 bytes before the end of a sector, where a 60 jump continues it in the next.
- G-Lite shows such a button as Macro and keeps the record. Writing one would mean writing sectors past the profiles: not done.

### Profile format 7

From lomm (MIT), read and written on a PRO X 2 DEX over LIGHTSPEED and cable, every write read back. The rate rows are lomm's; the other sources below swap them, and [newer-mice.md](../newer-mice.md#format-7) has the layout G-Lite uses:

| Offset | Content |
|---|---|
| 0x00 | Wired report rate, an index into 125, 250, 500, 1000, 2000, 4000, 8000 Hz |
| 0x01 | Wireless report rate, the same index (checked against 0x8061) |
| 0x02–0x03 | Not decoded (the default and shift stages, per lomm-web and GOMM) |
| 0x04 | Five DPI stages of 5 bytes: X LE16, Y LE16, lift-off level |
| 0x2C | Sleep and power-off timeouts in seconds, LE16 each |
| 0x30 | 16 buttons of 4 bytes |
| 0x70 | The G-shift layer |
| 0xA0 | Name, UTF-16LE, 48 bytes |
| 0xD0 | LED records of 11 bytes (unused on a mouse without lighting) |
| last 2 | CRC-16 CCITT, BE |

- lomm applies it to every format from 6, untested below 7. Its fallback for formats 4 and 5 (buttons at 0x23) is wrong: libratbag and our G502 reports put them at 0x20.
- Stage DPI accepted: 0 (stage off) or 100–30000. lomm doesn't check the stages against 0x2202's list.
- It puts the mouse in onboard mode whenever it connects, and edits profile 1.
- It reads each link's rates with 0x8061 fn0 `<connection>`, as in [report-rate-8061.md](../report-rate-8061.md).
- [Ad1FPS/Featherweight](https://github.com/Ad1FPS/Featherweight) (MIT, PRO X SUPERLIGHT 2, `docs/ONBOARD_PROFILES.md`): fn 0 `01 07 01 05 01 05 10 00 FF 0A 04`. Wrote sectors 0001 and 0005 and read them back byte for byte; the mouse used the new DPI at once, without fn 3. Byte 0 set to 04 made the live rate over the receiver 2000 Hz: byte 0 is the receiver's rate. The last fn 7 chunk is 15 bytes and a 00, accepted. The factory sectors 0100 and 0101 have no valid CRC. Its byte 1–3 labels (default, shift, active stage) don't fit its own dump (byte 1 = 03 with one stage).
- [lnx00/lomm-web](https://github.com/lnx00/lomm-web) (MIT, WebHID, a simulator, vibecoded), `js/profile.js`: OMM's offsets for formats 1, 2/3, 4, 5, 6, 7, 8 and 10. Format 6: rate bytes 0 (receiver) and 1 (cable), default and shift stage at 2 and 3, stages at 4, DPI delta at 29, power mode 33, angle snapping 34, write counter 35, timeouts 44 and 46, buttons at 0x30, G-shift at 0x70, name at 160. Format 7 adds bunny-hop prevention at 0x25 (10 ms units, 0 off), format 8 analog buttons at 0x26. Format 10 (the G502 X family after a firmware update, per GOMM): format 1's layout with the rates at 0x14 and 0x15, 13 buttons from 32 and a Y DPI table at 0x56. It also names model ids from 0x0003 fn 0 bytes 7–12 (`40a9c09b` PRO X SUPERLIGHT 2, `b03c40b1` G309, `b02e409d` G705, `b0244085` G604).
- [Sunya0804/logitech_omm](https://github.com/Sunya0804/logitech_omm) ("GOMM", licence Other, facts only): checked against OMM 2.7 and a PRO X Wireless (format 4), G502 X PLUS (format 5) and PRO X 2 DEX (format 7). Macro opcodes 45/46 are consumer key down/up `<usage BE16>`. It reads 0x60 as a 3-byte jump, where omm.py and leodbc read 5; untested.
- [CharlExMachina/LibreHub](https://github.com/CharlExMachina/LibreHub) (no licence, facts only), G502 X PLUS: G HUB stores macros as `00 00 00 00` and writes no macro data, though the firmware runs format 1 macros. fn 8 answers error 04 when the CRC is wrong. Format 5: bytes 28–29 dim after N s, 30–31 lights off after N s (FFFF never); LED slots active-all, active-primary, inactive-all, inactive-primary; byte 252 bits 0 and 1 pick the all slot. Effect ids 0x0F Pulsar Point, 0x10 Neutron Null. Its `docs/PROTOCOL.md` covers the A50 X headset (0B0B, page FF32).
- [sabam54/OpenGMice](https://github.com/sabam54/OpenGMice) (GPL-3.0, facts only): G305 X SUPERLIGHT (B046 over Bluetooth) is format 7, with four LED records at 0xD0 (zones 0 and 1, then their passive copies). 0x2202 fn 6 is refused in onboard mode with error 05.
- [Adhamura/superstrike-studio](https://github.com/Adhamura/superstrike-studio) (MIT, PRO X3 SUPERSTRIKE, format 8): 0x22 angle snapping, 0x25 in 10 ms units (FF off), 0x26 HITS for left and right; 0x8090 surface mode, 0x1B0C HITS, 0x80E0.
- Other reads, unchecked: [HolyJoey/Lghub](https://github.com/HolyJoey/Lghub) `docs/protocol.md` (0x2240 surface tuning, 0x80E0 bunny hopping, 0x8102/0x8103 tags, Centurion features); [shefben/open_logitech_GHUB](https://github.com/shefben/open_logitech_GHUB) fn 0 vectors (G903 `01 04 01 05 01 0C 10 00 FF 0A 04`, G815 `01 04 01 03 03 08 10 01 00 36 01`); G300s tools civelasco/g300s-configurator (MIT: writes only on interface 1, FF80; 0xF1 the colour; rate codes 0 1000, 1 125, 2 250, 3 500 Hz).

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
- Transfers (g600prog, Linux): GET_REPORT `A1 01`, wValue 0x03F3–0x03F5 (feature report F3–F5), wIndex 1, 154 bytes including the report id. SET_REPORT `21 09` with the same values; g600prog waits 1.1 s after each profile. On Windows this would be HidD_GetFeature and HidD_SetFeature on interface 1 with byte 0 = F3 (inference, untried).
- Bytes 6–10 and 18–30 are unknown, zero in g600prog's sample. The poll rate is 1000 / (1 + n), so 0 is 1000 Hz. DPI bytes × 50, up to 8200.
- Button codes 00 none (then the modifier and key bytes count), 01 left, 02 right, 03 middle, 04 back, 05 forward, 06 and 07 buttons 6 and 7, then 11–17 as above. Lighting effect 00 solid, 01 pulse, 02 rainbow; rate 0–15.
- g600prog's `defaults.json` is a full set of three modes (rainbow at rate 6, 400/1200/2000/3200 DPI, the G-shift colour white). Whether it's the factory set isn't said.

### Not useful

hakangit/g512-keyboard-controller (OpenRGB client), geromet/g502-linux-control (ratbagd client), grumpybollocks/g910-control (keyledsctl wrapper), swylem/gpro-control (one constant), aubymori/LogiFadeFix (plays silence), szympik/logitech-gpro-x-battery-tray (G HUB websocket), mafik/logitech-g600-linux (evdev mapping), softmaxe/gbat (mouse battery only), pschmitt/aur-g9xled (0x57 only), aron-w/logi-omm-wine and zzoko/Onboard-Memory-Manager-macOS-workaround (run OMM under Wine). AI-built but with dated hardware logs: MyHUB, gnexus, g502x-onboard.

## PeriHub

[hex-dump/perihub](https://github.com/hex-dump/perihub) (MIT, Rust, Windows and Linux, many brands), read 2026-11-23 at commit 9202683. Its `docs/protocols/` has a page per protocol; its issues hold the device reports. Tested on hardware: saves on a G502 HERO (C08B, format 2, 256-byte sectors), a G502 X (C098, format 3), a G502 HERO Wireless over a POWERPLAY (407F, issue #28) and a G300 (C246, #21); reads of a PRO X 2 DEX (#32), PRO 2 LIGHTSPEED (#33), G303 Shroud Edition (#34) and G903 HERO (#115); lighting on one G915 X LS (#30). Written from docs, untried: the G213, the G600, HID++ 1.0 mice, format 4 and 7 saves. Nothing on the G413, G512, G513, PRO X 60 or 0x8080.

Mice:

- Format 7's layout matches G-Lite's (`logitech-onboard-profiles.md`, from libratbag #1676 and #1806), with 16 button slots at 0x30–0x6F and the G-shift layer at 0x70; format 6 is 7 without the BHOP byte. Timeouts at 0x2C and 0x2E are little endian ([newer-mice.md](../newer-mice.md)). Its PRO X 2 DEX transcript is a simulation built from reports without bytes, so it can't check `BuildWide`.
- The G903 HERO has 0x8071 and no 0x8070, and its format 4 fn 0 reply is confirmed (#115).
- On a G502 HERO, profile LED block 0 (0xD0) lights the logo (0x8070 zone 1, location 2) and block 1 the DPI lights (zone 0), not zone n in block n as libratbag has it. G-Lite writes one light to both.
- Reselecting the active profile (fn 3) keeps live changes it doesn't hold; fn 1 `02` then `01` drops them (G502 HERO).
- The G303 Shroud Edition (C097, WPID 409E) is format 3 with 255-byte sectors; CLAUDE.md's format 2 G303 is the original.
- Macro format 1, checked on a G502 HERO: `01` waits for release, `02` repeats while held, `03` repeats until the profile changes, `20` wheel, `40` delay BE16, `41`/`42` mouse buttons, `43`/`44` keys, `45`/`46` consumer keys, `60` a 5-byte jump that can cross sectors, `FF` end. Macro sectors get a CRC. It stores macros in unused sectors past the profiles, written before the profile, never over one in use.
- G300 (tested): feature reports on FF80 as the G600; one 35-byte F3–F5 report saves a profile (byte 1 colour bits, byte 2 rate code 00 1000 Hz / 01 125 / 02 250 / 03 500, bytes 3–6 DPI × 250 with 0x80 the default, byte 7 shift, nine 3-byte buttons from 8). G-Lite does the same ([G300.md](../G300.md)).
- G600: writes F3–F5 for the active profile after reading it again, then sets the slot back with `F0`; untried.
- Ids: real 409E/C097, 40A8/C09A, 40B8/C0A0, 4087/C091, 407F/C08D; libratbag's G703 4070/C087, G703 HERO 4086/C090, G705 409D/C096, G900 4053/C081, G903 4067/C086, G403 Wireless 405D/C082, G Pro Wireless 4079/C088, PRO X SUPERLIGHT 4093/C094, G502 X PLUS 4099/C095. Receiver slot errors: 0x08 empty, 0x09 or 0x04 asleep, 0x01 HID++ 1.0.
- Over Bluetooth a device may answer at index 0x00 (Solaar); G-Lite drops replies from another index. Untried.

Keyboards (`logitech-keyboard-lighting.md`, from Logitech's x8040, x8071 and x8081 specs as found in logirgb, and the G915 X LS):

- 0x8040 fn 0: byte 2 the brightness key's level count, byte 3 0x04 on/off apart, 0x10 transient, bytes 4–5 the minimum ([G815-G915.md](../G815-G915.md)).
- 0x8071 fn 3 is the startup and shutdown effect (`<0 get, 1 set> <capability BE16>`, 0x0001 and 0x0040). fn 9 plays the shutdown effect and takes the device offline: never send it. fn 14 answers error 7: there's no read of the running effect.
- 0x8071 fn 1 parameters: Static byte 3 is the transition (0 default, 1 ramp, 2 at once); Breathe byte 5 the waveform, 6 intensity; Cycle and Wave byte 1 saturation (G-Lite's 0xFF); Ripple byte 3 saturation, byte 4 = 1 skips the start animation. Wave direction capability bits: 0x0020 horizontal, 0x0040 vertical, 0x0080 centre out, 0x0100 inward, 0x0200 outward, 0x0400 reverse horizontal, 0x0800 reverse vertical, 0x1000 centre in. The G515's `DC E1` has the vertical bits, so its Up and Down could come from them rather than `KeyboardKinds.VerticalWave`.
- Host mode on the G915 X LS: 0x8101 fn 6 `05`, fn 5 `01 03 06`, then fn 8 `01 01 00` (without it the keys stayed dark the first time). Wave centre in (8) stuck on every later effect until the lights were released and claimed again (one user).
- 0x8081: a key never written shows white; per-key colours show only while the zone runs a fixed colour; the logo shows key 210's colour while a frame is shown (G915 X LS).
- Busy (error 8): retried after 30, 60 and 90 ms, as Solaar does; never seen by PeriHub.
- 0x4523 fn 0 is a write (a 16-byte bitmap, from G HUB captures): never call it to read. `Report.Reads` leaves it out.
- 0x4540: Solaar reads 1 ANSI; 2, 5, 8, 0x0B, 0x0D–0x0F ISO; 9 and 0x3E JIS ([screen.md](../screen.md)).
- The G213 and the G PRO keyboard (C339) have a 64-byte report 0x12 on interface 1 (FF43/0604) besides the 20-byte one, so the G512's 14-keys-a-report path is likely there too.

## Found on 2026-11-22

From a second research pass: source read at a pinned commit, no hardware. Facts only from the GPL ones.

- [abidr/open-ghub](https://github.com/abidr/open-ghub) (GPL-3.0-or-later, Windows Rust): original SUPERLIGHT. Writes a profile over the FF00 collection after a backup, compares the read-back byte for byte, and writes the original again when it differs. Keeps the 7-byte and 20-byte reports on separate handles, a reader each. dvgmdvgm/G-Hubick is the same commit.
- [ctnkyaumt/Rodent](https://github.com/ctnkyaumt/Rodent) (GPLv2, Windows C#, a port of Solaar): tested on a G402 (C07E) only, which has one profile sector. Stored DPI, buttons, lighting and macros (libratbag's macro format). The G402 can't stop a repeating macro once it runs (nor can G HUB). Other mice fall through to the G402's layout.
- [Nuitfanee/ClickSync](https://github.com/Nuitfanee/ClickSync) (GPLv2, WebHID): its README lists the PRO 2 LIGHTSPEED and PRO X SUPERLIGHT 2c among five tested mice. Live commands and whole-profile writes; a lead for those two layouts.
- [m3744/superstrike-macos-cli](https://github.com/m3744/superstrike-macos-cli) (MIT, macOS Go, from mclol0's linux-superstrike): writes and compares the read-back, but reports success when the read-back times out. zeex64/openghub fails instead.
- [quinn-stack/kyxen](https://github.com/quinn-stack/kyxen) (licence not checked): G815. Copies the three factory profiles into the user sectors with G1–G5 set to F13–F17, for a macro daemon on the PC. It replaces whatever the user profiles held.
- [Airp2008/lghub-linux](https://github.com/Airp2008/lghub-linux) (MIT, Rust): G102/G203 LIGHTSYNC C092 tested: live DPI, rate, lighting, picking a profile.
- [Sharper-Flow/Open-G-Hub](https://github.com/Sharper-Flow/Open-G-Hub) (Rust): reads 0x8100 and picks a profile, through WinUSB. G-Lite needs no driver change.
- [amroot/logiflick](https://github.com/amroot/logiflick): writes the active sector's DPI as the two-byte stages of formats 1–5 whatever the format, with no read-back.
- [jazzvier1/Solaar-Windows](https://github.com/jazzvier1/Solaar-Windows): Solaar on hidapi; its author tested EQ and sidetone on a G733 and a G935.
- Java-Idl/universal-omm-webhid (above): its `commitProfileToFlash` sends fn 0x0C (set the DPI stage) and reads with fn 0x0B (get the stage), so it isn't a reference for the write sequence.
- OpenMouse: sets up a blank mouse (profiles, read back, then the directory and the selected profile) from a G HUB reset capture on a PRO X3 SUPERSTRIKE; its source says it wasn't tried on a mouse never linked to G HUB.
- [MatMoul/g810-led](https://github.com/MatMoul/g810-led) (GPL-3.0, facts only): drives the G413 with 0x8070 fn 3 alone (index 0x0C on it), zone 0, effects by position (0 off, 1 colour, 2 breathing); a single-colour keyboard's level goes in red (`II0000`); Breathe's param 6 gets the period's low byte; byte 16 of fn 3 stores the effect (`-fx-store`), offered on the G413; no commit, startup mode or per-key writes on the G413 or G213; per-key groups and commit for the G410/G512/G513/G610/G810/G PRO (see [keyboards-8070.md](../keyboards-8070.md)); G815 and G910 M, MR and G-key mode calls. Nothing on Game mode.
- OpenLogi (MIT, local checkout), `crates/openlogi-device/src/write/lighting.rs`: 0x8080 per-key frames (64-byte report 0x12, mode `01`, count at bytes 6–7, 14 `(usage, R, G, B)` from byte 8, no reply waited for, then a ping), fn 5 frame end answered; a per-key write doesn't override a running 0x8070 effect. Used for the G512/G513 live effects ([keyboards-8070.md](../keyboards-8070.md)).
- [theGeekyLad/logi-control-panel](https://github.com/theGeekyLad/logi-control-panel) (ISC in package.json): runs g810-led's `g413-led` on a G413 Carbon (red value as `<val>0000`, Breathe `ff0000 2` for notifications); no protocol code of its own.
- [ie04/openrgb-presets](https://github.com/ie04/openrgb-presets) (not read): a Rust app for the G513 Carbon through OpenRGB, static lighting and reactive ripples; identifies the keyboard as 046D:C33C.
- Leads only: [Lordbooker/gkey-config](https://github.com/Lordbooker/gkey-config) (G13, G15, G510, G11 through the Linux driver; G11, G15 and G510 untested), [martynsmith/lg4l](https://github.com/martynsmith/lg4l) (old G13/G15/G19 kernel modules), [IT-NWD/G15-Daemon_Linux](https://github.com/IT-NWD/G15-Daemon_Linux), [Chryseus/x56linux](https://github.com/Chryseus/x56linux) (X56 calibration and RGB over libusb), [gmlinux/logitech-rs50-linux-driver](https://github.com/gmlinux/logitech-rs50-linux-driver), [Adiakys/SimRig](https://github.com/Adiakys/SimRig) (untested on hardware), [jazzstack/Gear-Hub](https://github.com/jazzstack/Gear-Hub) (a G502 X adapter).

## Leads not read

From the topic search, unread: [lus/logy](https://github.com/lus/logy) (0BSD HID++ crate), [bamboostrip/logi-web-hub](https://github.com/bamboostrip/logi-web-hub) (WebHID), [ste4ik/g102-lightsync-control](https://github.com/ste4ik/g102-lightsync-control), [Lken-cmd/SuperLightBattery](https://github.com/Lken-cmd/SuperLightBattery) and [Ithilias/logitray](https://github.com/Ithilias/logitray) (Windows trays), [clovervidia/G403HID](https://github.com/clovervidia/G403HID) (C#, no licence), [Mirox921/logi-center](https://github.com/Mirox921/logi-center), [xyj-3/logitech-g-hub-supported-games](https://github.com/xyj-3/logitech-g-hub-supported-games) (G HUB data).

Skip: `destroylife535/Logitech-Onboard-Memory-Manager` and `totperla100551/Logitech-Onboard-Memory-Manager` look like download lures.
