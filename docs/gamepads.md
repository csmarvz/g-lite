# F310, F510 and F710 gamepads

Asked for by a user. Nothing here is checked on a real pad yet.

## What G-Lite sees

- **X mode (XInput, the default and the mode Logitech recommends for games):** nothing. Windows drives the pad with its Xbox controller driver (`xusb`), not as a HID device, so the HID scan never meets it. The only way in would be the XInput API: buttons, rumble and, for wireless pads, a battery level (`XInputGetBatteryInformation`). Telling a Logitech pad from an Xbox pad there needs the vendor and product id, which only the undocumented `XInputGetCapabilitiesEx` (xinput1_4, ordinal 108) gives.
- **D mode (DirectInput):** a plain HID joystick, with no vendor-defined collection and no HID++. G-Lite lists it with `PlainHid`, like the G510, and its Report (`Report.Plain`) shows each HID collection, its report lengths and every feature report read with `HidD_GetFeature`. That report tells us whether there's anything to talk to.

## Ids (vendor 046D)

| Pad | X mode | D mode |
|---|---|---|
| F310 | C21D | C216 (shared with the older Dual Action, not confirmed) |
| F510 | C21E | C218 (the kernel calls it RumblePad 2) |
| F710 | C21F | C219 (the kernel calls it RumblePad 2, second id) |

The F710 talks to its own 2.4 GHz nano receiver, not a Unifying or LIGHTSPEED one. Its receiver's id wasn't found.

## What can be set

- X/D: a hardware switch (bottom on the F310 and F510, front on the F710).
- Mode button (F710): swaps the D-pad and the left stick, lit while swapped. No software is named for it.
- In D mode the triggers are buttons, not analog.
- Vibration (F510, F710): Logitech says D mode needs its software for it. The Linux kernel drives it with HID output reports: on C219 report 0x42 `00 <left> <right>`; on C218 an init report 0xF3, then 0x51 with the weak motor at byte 2 and the strong one at byte 4.
- Button mapping was done by Logitech Profiler and later Logitech Gaming Software, on the PC, not on the pad. No source documents a command that changes the pad.
- Deadzones and battery level over HID: not found.

So on the pad itself there's little to change. What G-Lite could add: listing the pads in both modes, the F710's battery (X mode, through XInput), and a vibration test.

## Next

1. A report of a pad in D mode (switch on D, open it in G-Lite, Report). It lists every button and value the pad reports (the parsed HID descriptor), its output reports (where rumble goes) and its current input.
2. If there's nothing in it, decide whether listing X-mode pads with the F710's battery through XInput is worth it.

## Sources

- [xpad.c](https://github.com/torvalds/linux/blob/master/drivers/input/joystick/xpad.c): the X-mode ids.
- [hid-ids.h](https://github.com/torvalds/linux/blob/master/drivers/hid/hid-ids.h), [hid-lg.c](https://github.com/torvalds/linux/blob/master/drivers/hid/hid-lg.c), [hid-lgff.c](https://github.com/torvalds/linux/blob/master/drivers/hid/hid-lgff.c), [hid-lg2ff.c](https://github.com/torvalds/linux/blob/master/drivers/hid/hid-lg2ff.c): the D-mode ids and the rumble reports (GPL, facts only).
- [SDL_GameControllerDB](https://github.com/gabomdq/SDL_GameControllerDB/blob/master/gamecontrollerdb.txt): F510 and F710 names on C218 and C219.
- Logitech support: [F310 mode switch](https://support.logi.com/hc/en-us/articles/360023398693-Using-the-input-mode-switch-on-the-Gamepad-F310-to-select-DirectInput-or-XInput), [F510 mode switch](https://support.logi.com/hc/en-us/articles/360023233454-Using-the-input-mode-switch-on-the-Rumble-Gamepad-F510-to-select-DirectInput-or-XInput), [F710 mode switch](https://support.logi.com/hc/en-us/articles/360023400073-Using-the-input-mode-switch-on-the-F710-to-select-DirectInput-or-XInput), [F710 Mode button](https://support.logi.com/hc/en-us/articles/360023399653-Using-the-Mode-button-on-my-F710-gamepad), [F710 triggers in D mode](https://support.logi.com/hc/en-us/articles/360023236194-Analog-triggers-in-DirectInput-D-mode-on-the-Gamepad-F710).
