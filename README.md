# G-Lite

A small Windows app that controls the lighting, DPI and other settings of Logitech G keyboards, mice, headsets and lights, without G HUB. One file of about 400 KB, nothing to install, and next to no CPU when idle.

[![Download alpha version](https://img.shields.io/badge/Download-alpha%20version-2ea44f?style=for-the-badge)](https://github.com/csmarvz/g-lite/releases/latest)

> [!WARNING]
> **G-Lite is experimental.** It writes to your device's onboard memory, and a bug could leave a device in a bad state or brick it. It never touches the factory profile, checks every write and backs up what's on a device before each save, but it has only been tested on a few devices. Use it at your own risk.

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="docs/screenshot-dark.png">
  <img src="docs/screenshot-light.png" alt="G-Lite showing a G515 TKL with the Wave effect, a mouse and a headset in the sidebar" width="820">
</picture>

## How to use

You need Windows 10 (version 1903 or later) or Windows 11.

1. Quit G HUB, and stop it from starting with Windows. Both apps control the same devices and would undo each other's changes.
2. On Windows 11, turn off **Dynamic Lighting** (Settings > Personalization > Dynamic Lighting), for the same reason. G-Lite warns you if it's on.
3. Download `GLite.exe` from the [latest release](https://github.com/csmarvz/g-lite/releases/latest) and double-click it.
4. G-Lite finds your Logitech G devices and lists them on the left. Click one to change its settings.
5. Click **Save to keyboard** or **Save to mouse** to keep your changes on the device.
6. To go back to the factory settings, or to an earlier save, use **Backups** at the top of a keyboard or mouse screen.

If Windows says it protected your PC, click **More info**, then **Run anyway**. G-Lite isn't signed yet. To check that your copy is the one GitHub built, run `Get-FileHash GLite.exe` in PowerShell and compare the result with `checksums.txt` on the release page.

**How long your changes last.** The line at the bottom of each screen tells you. There are three cases:

- **Saved to the device** (the G515 and mice, with **Save to keyboard** or **Save to mouse**): stays on the device, even without G-Lite or on another PC.
- **Set on the device, not saved** (the G915, G560, Litra colours and the untested keyboards): stays until the device sleeps or turns off. G-Lite sets it again when it starts.
- **Run by G-Lite** (the Screen effect, and the Litra's Breathe, Cycle and Wave): keeps going while G-Lite runs, in the tray too, and stops if you quit it.

## Supported devices

### Keyboards

| Device | Connection | Status | What G-Lite can do |
|---|---|---|---|
| G515 LIGHTSPEED TKL | LIGHTSPEED receiver | Works | Lighting effects, colour, speed, direction, brightness, idle dimming and turn-off, profiles, saving to the keyboard, backups and restoring them, factory lighting |
| G915, G915 TKL and G815 | LIGHTSPEED receiver or USB | Untested | Lighting effects, colour, speed, direction, brightness and profiles. G-Lite sets your lighting again when it starts, but can't save it to the keyboard yet. See [docs/G915.md](docs/G915.md). |
| G512 X 75 | USB | Untested | Lighting effects, colour, speed, direction, brightness and profiles, on the keys and the light bar, together or each with its own effect. Not yet: saving to the keyboard, the knobs and actuation. See [docs/G512X.md](docs/G512X.md). |
| G915 X, G713, G715, PRO X TKL, PRO X 60, G515 RAPID TKL, G512 X 98 and other newer G keyboards | LIGHTSPEED receiver or USB | Untested | Lighting effects, colour, speed, direction, brightness, the Screen effect and profiles. Not saved to the keyboard. The key picture is picked from the keyboard's name (TKL, 60%, 75%, full size) and may not match. |
| G512, G513, G PRO keyboard, G810, G910, G410, G413, G213 and other older G keyboards | USB | Untested | Off, Static, Breathe and Cycle with colour, speed and brightness, on all zones or each one (keys, logo), and profiles. Not saved to the keyboard. Not yet: Wave, per-key colours, a key picture. See [docs/keyboards-8070.md](docs/keyboards-8070.md). |

### Mice

| Device | Connection | Status | What G-Lite can do |
|---|---|---|---|
| G Pro Wireless | LIGHTSPEED receiver | Works | DPI and DPI stage, logo lighting, report rate, profiles, saving to the mouse, backups and restoring them, factory profile |
| G305 LIGHTSPEED | LIGHTSPEED receiver | Works | DPI and DPI stage, lighting of the status light, report rate, profiles, saving to the mouse (the first save adds a profile next to the factory one), backups and restoring them, factory profile |
| G502 LIGHTSPEED | LIGHTSPEED receiver | Partial | DPI and DPI stage, lighting (both zones get the same effect), report rate, profiles, saving to the mouse, backups and restoring them, factory profile, invert scrolling, what each button does. Not yet: macros and the G-shift layer. |
| PRO X SUPERLIGHT 2, PRO X 2, G502 X and other newer G mice | LIGHTSPEED receiver or USB | Untested | DPI, switching between the DPI stages saved on the mouse, and the report rate up to 8000 Hz. Some of these mice only accept their saved DPI stages. Not yet: saving to the mouse, buttons. See [docs/newer-mice.md](docs/newer-mice.md). |

### Headsets

| Device | Connection | Status | What G-Lite can do |
|---|---|---|---|
| PRO X Wireless | Its own LIGHTSPEED receiver | Works | Sidetone, turning off when idle, battery, profiles |
| G735, PRO X 2 LIGHTSPEED, G522, A50 X and other G headsets | USB | Report only | Listed in the sidebar so their owner can send a report. Nothing can be changed yet. |

### Speakers

| Device | Connection | Status | What G-Lite can do |
|---|---|---|---|
| G560 Gaming Speaker | USB | Untested | Lighting effects, colour, speed, brightness and profiles, on all four zones or each one (bottom left, bottom right, top left, top right). Not saved to the speaker. Not yet: the audio visualizer effect, the equalizer. See [docs/G560.md](docs/G560.md). |

### Lights

| Device | Connection | Status | What G-Lite can do |
|---|---|---|---|
| Litra Beam LX, Beam and Glow | USB | Untested | Front light on and off, brightness and colour temperature. On the Beam LX, the back light: on and off, brightness, Static, Breathe, Cycle, Wave and the Screen effect, with a colour for all seven zones or each one, and a live picture of the light. Profiles. See [docs/Litra.md](docs/Litra.md). |

**Works** means tried on the real device. **Untested** means built from documentation and owners' reports, and not yet confirmed by an owner. Devices that aren't saved to are set again by G-Lite each time it starts.

Other Logitech devices show up in the sidebar with their connection, battery and firmware, but G-Lite can't change their settings yet. Devices that could come next are listed in [#11](https://github.com/csmarvz/g-lite/issues/11).

## FAQ

**Can I use G-Lite and G HUB at the same time?**
Not well. Both control the same devices, so each would undo the other's changes. Quit G HUB while you use G-Lite.

**What happens to my settings if I stop using G-Lite?**
Whatever you saved to a keyboard or mouse stays on it, and works without any app. Settings that aren't saved to the device (the Untested keyboards, the G560, the Litra) go back to the device's own when it restarts.

**What if a save goes wrong?**
G-Lite backs up what's on the device before every save and reads back everything it writes. If a save stops halfway, the device's screen offers to restore the backup. You can also restore any backup, or the factory profile, from **Backups**.

**Does it work with my device?**
Open G-Lite: if your device is listed with a screen of settings, yes. If it shows only its info, click **Report** and send the report in an issue, and it may be added.

## Get help

Open an [issue](https://github.com/csmarvz/g-lite/issues). Say what you did and what happened, and add the device's report (**Report** on its screen) and the lines from **Logs** around that time.

## Your files

G-Lite has no installer. Everything it keeps is in plain text files you can open, copy or delete:

- **`settings.txt`**, next to `GLite.exe`: your presets, the lighting it sets again on each start, and your choices in Settings. If that folder can't be written to, it goes in `%LOCALAPPDATA%\GLite` instead.
- **`log.txt`**, next to `settings.txt`: what G-Lite did and when, like the devices it found, what it sent to them and updates. **Logs** in the sidebar shows it. When it reaches 512 KB, it becomes `log.old.txt` and a new one starts.
- **Backups**, in `%LOCALAPPDATA%\GLite\backups`: a copy of what was on your keyboard or mouse before each save, one folder per device. G-Lite keeps the first one and the last ten. **Backups** on a device's screen restores them.

To remove G-Lite, quit it from the tray, turn off **Run on startup** in Settings, and delete these files.

## Privacy

G-Lite never sends your data or how you use it anywhere. There are no accounts, no analytics and no tracking.

- A device report stays on your PC. It only leaves when you copy it and paste it somewhere yourself. **Open GitHub issue** opens a new issue in your browser with the report filled in, and nothing is sent until you submit it.
- The only time G-Lite goes online is to check GitHub for a new version when it starts, and to download one. You can turn that off in Settings.

## Project status

Alpha. The devices marked Works have been tried on real hardware; everything else needs owners to try it and send a report. What changed in each version is in the [changelog](CHANGELOG.md).

## Contributing

Reports from device owners help the most, and need no code. To build G-Lite, add a device or send a pull request, see [CONTRIBUTING.md](CONTRIBUTING.md).

## Credits

- [logirgb](https://github.com/mevdschee/logirgb) by Maurits van der Schee. The G515 feature set the app copies, and the protocol notes and hardware logs in its `research` folder, including copies of Logitech's HID++ 2.0 feature documents (0x8040, 0x8070, 0x8071).
- [logirgb: Logitech G515 and G502 RGB on Linux](https://www.tqdev.com/2026-logirgb-logitech-g515-g502-rgb-linux/), the article introducing logirgb.
- [logitech-g515-onboard](https://github.com/federico-duppa/logitech-g515-onboard) by Federico De Malmayne Duppa. The onboard profile and file layout (0x8100/0x8101) used to make the lighting persist on the keyboard.
- [Solaar](https://github.com/pwr-Solaar/Solaar), [OpenRGB](https://github.com/CalcProgrammer1/OpenRGB) and [libratbag](https://github.com/libratbag/libratbag). Facts about the G915: its ids, features, lighting clusters and effect parameters. No code is taken from them. See [docs/G915.md](docs/G915.md).
- [Lghub](https://github.com/HolyJoey/Lghub) (MIT), G HUB reverse-engineered for Linux. How G HUB talks to Logitech G headsets, mice, keyboards and other devices, over USB, LIGHTSPEED, Unifying, Bolt and Bluetooth. G HUB's key-to-LED table for the G915's logo, G keys and media keys comes from it.
- [logi-cli](https://github.com/meech-io/logi-cli) (MIT) by Dimitri Brooks and [OpenLogi](https://github.com/AprilNEA/OpenLogi). The newer mice: 0x2202 DPI, 0x8061 report rate and where the DPI stages sit in their onboard profile.
- [G-Helper](https://github.com/seerge/g-helper), the model for the look and feel of the tray panel.

## License

MIT. See [LICENSE](LICENSE).

G-Lite isn't made, endorsed or supported by Logitech. Logitech, Logitech G, G HUB, LIGHTSPEED and Litra are trademarks of Logitech.
