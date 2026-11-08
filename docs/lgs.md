# Logitech Gaming Software

Research update, 2026-11-08: Logitech's [support notice](https://support.logi.com/hc/en-au/articles/43643050172055-Logitech-G-HUB) says official LGS downloads/documentation were scheduled for removal on 2026-10-31. The historical direct download below was not downloaded or revalidated in this pass. A broader [local manifest inventory](research/lgs-manifest-inventory.md) and [vendor-software references](research/vendor-software.md) preserve the factual device information.

What LGS 9.04.49 (the last version, May 2022) tells us. Read from its install files, without copying them. Facts about what LGS shows or writes on the G9x (DPI values, Restore Defaults) come from running it and are in [G9x.md](G9x.md). LGS is Logitech's: take facts only, no files, images or code.

- Install: `C:\Program Files\Logitech Gaming Software`. The Windows download on Logitech's support page is the 32-bit build, which refuses to run on 64-bit Windows. The 64-bit one is `https://download01.logi.com/web/ftp/pub/techsupport/gaming/LGS_9.04.49_x64_Logitech.exe`.
- It runs in the background (`LCore.exe`) and talks to the mice it supports, so quit it before testing G-Lite on the same device.
- Devices that G HUB supports are handed to G HUB: `%LOCALAPPDATA%\Logitech\Logitech Gaming Software\ghubDevices.json` lists their PIDs (C084, C332, C07E, 4079 and others).

## Device manifests

One XML file per family: `Resources\<Model>\Manifest\Device_Manifest.xml`. They hold:

- How LGS finds the device: `hid_probe` with usage page 0xFF00, usage 1 (short) and 2 (long), PID and VID.
- The backlight: `type="g9x"` (G9 family only) or `rgb`, with a default colour. Newer devices have `colorLEDEffects` instead.
- Pointer: whether onboard (hardware) mode is the default, the default DPI table and report rate.
- Default actions for each button, in host mode and in onboard mode.

They don't say whether a device is HID++ 1.0 or 2.0, and don't give DPI steps, ranges or profile counts.

### G9x

From `Resources\G9X\Manifest\Device_Manifest.xml`, which covers the G9 (C048), G9x (C066) and G9x Call of Duty (C249). G-Lite supports the two G9x only: the G9 uses another profile layout (see [G9x.md](G9x.md)).

- Probes on usage page 0xFF00, usages 1 and 2: the same short and long collections G-Lite uses.
- Backlight type `g9x`, default colour `#FF9B00` (orange, as on profile 5 after a reset).
- Buttons 1 to 5 and 7 to 10. There is no button 6; 7 and 8 are the wheel tilts.
- Host mode defaults: left, right, middle, back, forward, scroll left, scroll right, DPI up, DPI down.
- Onboard mode defaults: the same, except the tilts are back and forward.
- LGS's own default DPI table: 500, 1000, 1500, 2500, starting on the second; 500 Hz. The mouse's factory profile 1 is 400, 800, 1600, 3200 instead (see [G9x.md](G9x.md)).
- LGS keeps its G9x settings in `%LOCALAPPDATA%\Logitech\Logitech Gaming Software\settings.json`, with three backlight colours `M1` to `M3`.

## Devices LGS knows

From the manifests (name, USB PID):

G9 C048 · G9x C066 · G9x COD C249 · MX518 C051 · MX518 (1600) C01E · G100, G100s, G90 C247 · G300, G300s C246 · G302 C07F · G303 C080 · G400 C245 · G400s C24C · G402 C07E · G403 C083 · G500 C068 · G500s C24E · G502 C07D · G502 RGB C332 · G600 C24A · G602 C537 · G700 C06B, C531 · G700s C07C, C531 · G900 C539 · G11 C225 · G13 C21C · G15 C222, C227 · G19, G19s C229 · G103 C24B · G105 C248 · G110 C22B · G510, G510s C22D, C22E · G710 C24D · G810 C331, C337 · G910 C32B · G35 0A15 · G430 0A4D · G930 0A1F · G633 0A5C · G933 0A5B · G29 C24F · G920 C262.

## Button actions

The actions LGS offers for a button (for issue #16):

- Mouse: left, right, middle, back, forward, scroll up, down, left and right.
- DPI: up, down, shift (hold for a lower DPI), default DPI, cycle.
- Profile: switch profile. G-shift and mode switch on some devices.
- Media: play/pause, next, previous. Battery level.
- Keystrokes and key combinations, text blocks, macros (recorded keys and mouse buttons with delays).
- Lighting on keyboards: cycle effect, cycle brightness.

LGS profiles on the PC are XML in `%LOCALAPPDATA%\Logitech\Logitech Gaming Software\profiles\`: a name, an executable to match for automatic game detection, macros, and button assignments per device. `Resources\Profile\GameDatabase.xml` lists 645 games by executable or registry key.

## SDKs

`SDK\` has only DLLs (LED, G-key, Arx, LCD, steering wheel), no headers or license. They work only through a running LGS. Not useful to G-Lite.
