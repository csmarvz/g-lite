# G-Lite
Lightweight G Hub alternative.

> [!WARNING]
> **G-Lite is experimental.** It writes to your device's onboard memory, and a bug could leave a device in a bad state or brick it. It never touches the factory profile and checks every write, but it has only been tested on one keyboard. Use it at your own risk.

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="docs/screenshot-dark.png">
  <img src="docs/screenshot-light.png" alt="G-Lite showing a G515 TKL with the Wave effect, a mouse and a headset in the sidebar" width="820">
</picture>

## Supported devices

| Device | Connection | What G-Lite can do |
|---|---|---|
| G515 LIGHTSPEED TKL | LIGHTSPEED receiver | Lighting effects, colour, speed, direction, brightness, idle dimming and turn-off, profiles, saving to the keyboard |

Other Logitech G devices show up in the sidebar with their connection, battery and firmware, but G-Lite can't change their settings yet. Devices that could come next are listed in [#11](https://github.com/csmarvz/g-lite/issues/11).

## Sources

- [logirgb](https://github.com/mevdschee/logirgb) by Maurits van der Schee. The G515 feature set the app copies, and the protocol notes and hardware logs in its `research` folder, including copies of Logitech's HID++ 2.0 feature documents (0x8040, 0x8070, 0x8071).
- [logirgb: Logitech G515 and G502 RGB on Linux](https://www.tqdev.com/2026-logirgb-logitech-g515-g502-rgb-linux/), the article introducing logirgb.
- [logitech-g515-onboard](https://github.com/federico-duppa/logitech-g515-onboard) by Federico De Malmayne Duppa. The onboard profile and file layout (0x8100/0x8101) used to make the lighting persist on the keyboard.
- [G-Helper](https://github.com/seerge/g-helper), the model for the look and feel of the tray panel.
