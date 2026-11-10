# Changelog

## [0.0.16-alpha] - 2026-11-10

### Changed

- Hovering the tray icon shows a small table: G-Lite's CPU and memory use, each device with its battery, and a new version when there is one. The version number is no longer in the tray tooltip.
- A battery icon next to the battery level in the tray popup, the device list and the device screen. It turns amber at 20% and under.
- The device list is a little wider. The device screen no longer shows when it was read.

## [0.0.15-alpha] - 2026-11-10

### Added

- A Buttons view on the mouse screen, for mice with onboard profiles: pick what each button does (mouse buttons, DPI, scrolling, profiles, media keys, a keystroke, or nothing), and Save to mouse. Press a button on the mouse and its row lights up, so you can tell which one is which. The left button always stays Left click. Presets keep the buttons too, and Revert brings them back.

### Changed

- Mouse presets keep the report rate as the mouse has it, and Revert brings it back. Before, a preset saved without touching the rate didn't keep it.
- Invert scrolling on mice whose wheel can do it (0x2121), like the G502 LIGHTSPEED. The mouse forgets it when it turns off, so G-Lite checks every 5 seconds and sets it again.

### Fixed

- On the mouse screen, Save to mouse stayed on after Revert or picking None, although nothing was left to save.
- A report could stop after the firmware line, though it was complete. The G512 X 75's firmware name ends with a zero byte, which hid the rest of the report in the window and the copy. The firmware now shows in full, and the report drops such characters. Keyboard reports now also read the user and factory profile files, not only the directories, to help add new keyboards.

## [0.0.14-alpha] - 2026-11-10

### Added

- Save to mouse on mice that only have their factory settings, like a G305 never set up with G HUB. The first save adds a profile next to the factory one, which stays as it was; the report rate can then be changed on save. Each step is in the logs.

### Fixed

- Replies from the device could be taken for another program's, if it talked to the same device at the same moment. A report could then list every feature as 0100. Each call now has its own software ID.

## [0.0.13-alpha] - 2026-11-10

### Added

- The G510 and G510s show up, on the info page. Their report lists the keyboard's HID collections and reads its feature reports, to help add them. G-Lite sends them nothing.

## [0.0.12-alpha] - 2026-11-09

### Changed

- Profiles are now presets, kept on this PC. The list holds None and your presets; None, and deleting a preset, bring back what the device has saved. Changes to a preset you leave without Update are kept until G-Lite quits, and come back when you pick it again. Once you change something, the loaded preset gets a dot; Update saves the change into it, and the … menu has Save as new preset, Rename and Delete. As on the keyboard is now Revert, next to Save: with a preset loaded it brings back the preset, with None it brings back what the device has saved. The refresh button is gone: the screen reads the device each time you open it.

## [0.0.11-alpha] - 2026-11-09

### Changed

- Lighting you change on the G515 without saving now stays while G-Lite runs, also when you minimize it or open another device. The keyboard gets its saved lighting back when G-Lite quits, with As on the keyboard, and while the screen is off or locked, so it can dim and sleep on its own. The unsaved lighting comes back when the screen does. While it shows, G-Lite follows its Dim after, Dim to and Turn off after settings itself, counting from your last key press or mouse move on the PC, since the keyboard doesn't dim or turn off on its own while G-Lite controls its lighting.

### Fixed

- Clicking the tray icon while G-Lite was minimized hid it, and the next click left it off screen. It now brings the window back.
- After the keyboard was turned off and on, changing its lighting failed with HID++ error 0x05. G-Lite now takes control of the lighting again, and checks every 3 seconds while unsaved lighting is shown, so it comes back by itself after the keyboard is turned off and on.

## [0.0.10-alpha] - 2026-11-09

### Fixed

- Moving or using the window no longer stutters while the Screen effect runs: the screen is read on a background thread.

## [0.0.9-alpha] - 2026-11-09

### Changed

- G-Lite uses less memory: it lists devices without WMI and reads the key layouts without the JSON serializer, so it loads 19 fewer DLLs. It hands unused memory back to Windows when its window is hidden. The tray menu shows the memory Task Manager shows, not the shared Windows and .NET DLLs.

## [0.0.8-alpha] - 2026-11-09

### Added

- The G915 and G915 TKL, untested: effects, colour, speed, direction, brightness and profiles, on the same screen as the G515, with a picture of each keyboard. The logo follows the keys. G-Lite can't save to these keyboards yet, so it remembers your lighting and sets it again when G-Lite starts and when you open the keyboard. As on the keyboard gives the keyboard its own lighting back.
- Older G devices plugged in with USB that speak HID++ 1.0 now show up, and their report lists their registers and profile pages.
- Screen, a lighting effect that follows your monitor, on keyboards with per-key lighting (G515, G915, untested on the G915). Each zone on the screen gives its average colour to its keys. Pick the monitor, move and resize the zones on a picture of the screen, add up to five, and click or drag across keys to give them to a zone. Color boost, brightness, update rate (100 to 1000 ms) and smooth changes. It pauses, giving the keyboard its own lighting back, on battery below 20% and while the screen is off or locked. It runs only while G-Lite does, isn't saved to the keyboard, and comes back when G-Lite starts. Profiles keep it with its zones.
- The G515's picture follows its layout: ISO keyboards get the <> key, the key next to Enter and the tall Enter.
- The tray menu shows G-Lite's CPU and memory use, updated every second while it's open, and lists your devices with their battery. Clicking one opens G-Lite on it.
- The report lists the per-key lighting ids a keyboard has (0x8081), with its layout, so the Screen effect can be added to more keyboards.
- The report lists the device's product IDs, read from the device (like USB C088 · LIGHTSPEED 4079).
- The G9x Laser Mouse: colour and brightness, DPI, the DPI stages of the active profile and the report rate, set live. Save to mouse writes them into the active profile, after a backup, and Backups restores one.

## [0.0.7-alpha] - 2026-11-07

### Added

- Logs, in the sidebar. G-Lite keeps a log of when it starts, the devices it finds and loses, your saves and restores, its backups, every command that changes a device, with the device's answer, and errors. Logs shows it, with calls in a row to the same function on one line. Copy copies the lines shown, Open folder shows `log.txt`, and Clear empties it. At 512 KB the log moves to `log.old.txt`.

### Changed

- Buttons, swatches, toggles, menus and the device list show when they're pressed, not only on hover.
- Every control can be reached with Tab. Space or Enter presses a button, the arrow keys move a choice or a slider, and Page Up and Page Down move a slider by 10. Esc closes a report. A focus ring shows when you use the keyboard.
- The picked option slides to its place and toggles slide. Neither moves when Windows animations are off.
- A problem in the status line, like a half-written save on the keyboard, comes with its own button, such as Restore backup or Open settings, instead of text you click.
- The screen of a device G-Lite doesn't support yet names what the device has instead of listing feature codes. The codes are in the report.
- Grabbing a slider's thumb keeps it under the pointer instead of jumping.
- The mouse's report rate is marked "on save" instead of explained in a paragraph.
- The report's Copy report button is now just Copy, like in Logs.

## [0.0.6-alpha] - 2026-11-07

### Added

- Report, at the top of every device screen, and Create report on a device G-Lite doesn't support yet. It reads what the device supports, without changing anything on it, and shows it as text. Copy report puts it on the clipboard, and Open GitHub issue starts a new issue with it. It holds no serial number, no name you gave the device and no Windows path.

### Changed

- The Settings button shows a gear, and the Report and Backups labels are centred like the other buttons.

### Fixed

- On the G515, previewing the lighting saved on the keyboard, or leaving the screen after a preview, now shows the saved lighting right away instead of up to a minute later.

## [0.0.5-alpha] - 2026-11-06

### Added

- A mouse screen: DPI, DPI stages, logo lighting, report rate and profiles, with a live picture of the mouse. Changes apply right away, and Save to mouse keeps them in the mouse's profile. Moving the DPI slider changes the stage you're on, and saving makes it the stage the mouse starts on.
- G-Lite backs up the mouse's profile before each save. Backups restores one of them or the factory profile, and a damaged profile offers to restore the last good backup.
- A headset screen: sidetone, how long it waits before turning off, the battery and profiles. Test sidetone turns the microphone on for 30 seconds so you can hear yourself.
- G-Lite backs up the keyboard's lighting before each save, keeping the first backup and the last ten. Backups, next to the refresh button, restores one of them or the factory lighting.
- When the lighting saved on the keyboard is half written, G-Lite offers to restore the last good backup instead of only refusing.

### Changed

- A change made while G-Lite is still reading a device is no longer undone when the reading finishes.
- The mouse wheel moves the slider under the pointer: 5% a notch, or one DPI step (50 on the G Pro Wireless).
- A headset with its own wireless receiver shows as LIGHTSPEED, not USB.

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
