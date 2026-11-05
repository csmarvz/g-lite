# Changelog

## [0.0.4-alpha] - 2026-11-05

### Added

- G-Lite finds your Logitech G devices over LIGHTSPEED, Unifying and Bolt receivers, USB and Bluetooth, and lists them in a sidebar with their connection and battery. Devices that are paired but asleep stay in the list. The list updates when a device is plugged in or out, or when you click Scan for devices.
- Devices G-Lite can't change yet show their connection, battery, firmware and features. When nothing is found, a screen says what to try and links to reporting your device.
- Profiles: save the current lighting under a name, pick it again from the dropdown, or delete it. "As on the keyboard" goes back to what's saved on the keyboard.
- A live picture of the keyboard shows the lighting you're setting.
- A dark, light or system theme, in Settings.
- The GL logo, as the app and tray icon.

### Changed

- The panel is now a window, with your devices on the left and the selected device on the right. Settings and Quit are in the sidebar. The window is no longer always on top.
- Every lighting option stays visible. The ones the effect doesn't use are greyed out.
- A custom colour keeps its own swatch, next to the + that picks a new one.
- G-Lite scales with the display, including when it moves to a screen with a different scale.
- Run on startup follows the switch in Task Manager's Startup apps, where G-Lite shows with com.csmarvz as its publisher.

## [0.0.3-alpha] - 2026-11-05

### Added

- The panel has a taskbar button. Closing it with ✕ sends it to the tray, and the first time this happens a notice says G-Lite is still running there.
- After an update, the panel says which version it updated to.
- Updating works in folders that need administrator rights: Windows asks for permission, and G-Lite keeps running without it afterwards.
- Windows 11 rounds the corners of the panel, Settings and the demo keyboard.

### Changed

- Starting G-Lite by hand opens the panel. Starting with Windows keeps it in the tray, and an automatic update leaves the panel open or closed as it was.
- The status line only takes space when it has something to show.
- Settings are saved in `settings.txt` next to `GLite.exe`, or in `%LOCALAPPDATA%\GLite` if that folder can't be written. Settings changed in earlier versions go back to their defaults.
- The demo keyboard opens only when you click Show keyboard, next to the panel.

## [0.0.2-alpha] - 2026-11-05

### Changed

- No changes to the app. This release is for testing the update from 0.0.1-alpha.

## [0.0.1-alpha] - 2026-11-05

### Added

- A tray panel that sets the lighting on a Logitech G515 LIGHTSPEED TKL without G HUB: Off, Static, Breathe, Cycle, Wave and Ripple, with colour, speed, direction and brightness.
- Idle dim and sleep timeouts, and Run on startup.
- Changes preview live and are written to the keyboard only when you click Save to keyboard.
- A warning when Windows Dynamic Lighting is on, with a link to its settings.
- Checks GitHub for a new version at launch, without delaying it, and shows it in the panel with Update and Skip this version. Update checks and auto-update can be turned on or off, and Check update now checks straight away.
- `--demo` runs the app with a simulated keyboard, shown in its own window, which has three games played on the keys: Blocks, Snake and Mines.
