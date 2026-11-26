# Changelog

## [Unreleased]

### Added

- MX Mechanical Mini and MX Keys S: a picture of the keyboard, lit while its backlight is on, as bright as its level and with its effect (Contrast, Breathing, Waves, Reaction to key presses, Random), with ISO's Enter when the keyboard's layout is ISO. Tried on a real Mini.
- MX keyboards: the backlight's levels are numbered 0 (off) to 7, as in Logi Options+.
- Reports read how each 0x1B04 control reports now (fn 2): diverted or remapped by an app like Logi Options+.
- MX keyboards and mice: keys Logi Options+ had taken over (diverted) work again once it's stopped, from Settings or before G-Lite started; until now they did nothing until the device reconnected. Tried on an MX Mechanical Mini.
- MX keyboards: the backlight's effects (Static, Contrast, Breathing, Waves, Reaction, Random) and battery saving, where the keyboard has them, and a Keys section: F1–F12 as media keys or standard keys, and turning off Caps Lock, Num Lock, Scroll Lock, Insert or the Windows key. The MX Mechanical Mini's effects, F1–F12 and disabled keys were tried on it. The effect and F1–F12 aren't set again when the keyboard comes back, since its own keys change them.
- A backlight setting the keyboard doesn't have is left out once it's read (the MX Mechanical Mini on its first firmware has no automatic mode, levels or fade delays).
- Settings: Logi Options+ in the Services box, stopped and started like G HUB (its service and its programs), since it sets the MX devices' settings again over G-Lite's.

### Changed

- The G560 is marked Works: it was tried on real speakers.

### Fixed

- Easy-Switch: after switching, the Computer picker shows the computer the device went to, not the one it left, and the device is read again when it comes back.
- MX keyboards: turning Automatic off sets the level the Level slider shows, not the one the room's light had set, level 0 (off) too.
- MX Keys S: F1–F12 as media keys does something: it's set for this computer by its Easy-Switch number, since the MX Keys S ignores the "computer in use" G-Lite sent. Reports show the setting for each computer.

## [0.1.0-beta] - 2026-11-25

G-Lite leaves alpha. Every screen is redesigned like Settings: the device's picture at the top, then its settings one per row, under section titles, with only the rows the device has. The window can be resized, has Windows' own title bar, and an accent colour.

### Added

- The window can be resized and maximized, down to its usual size. Each screen keeps its width, centred beside the sidebar.
- Settings: an accent colour, G-Lite's green, your Windows accent (in the shade Windows uses for light or dark) or a colour you pick. Click Custom again to pick another.
- Settings' scroll bar can be dragged, and a click beside it jumps there. Every screen's scroll bar works the same.
- Pictures for the G PRO keyboard, G410, G810 and G910, lit as their zones are, and for the G560 speakers: each of the four lights in its colour, the front ones under the grilles and the back ones as the glow behind. Click a key or a light to pick its zone. With a picture, the G PRO, G410, G810 and G910 also get the live effects (with per-key colours) and the Game view (with Game mode), experimental.
- Mice are drawn in their own shape: the G Pro Wireless, the PRO X SUPERLIGHTs and the rest of the PRO family with their wheel, side buttons and the light on the palm; the PRO X 2 DEX in its right-handed shape; the G502s in theirs, with the logo and the DPI lights lit.
- G510 and G510s: a picture of the keyboard lit in its colour, and two new views. M1, M2 and M3 each have their own lighting and G key bindings, as in Logitech Gaming Software: pick one in the footer and its light comes on. Bindings sets what G1 to G18 do; G-Lite keeps them but doesn't act on the G keys yet, nor on the keyboard's M keys. Game shows whether the keyboard's Game mode switch is on.
- PRO X Wireless: an equalizer, experimental. The headset's ten bands (32 Hz to 16 kHz, 12 dB up or down) as it stores them, a row each, and Flat. G-Lite writes them to what the headset plays and to its memory (persistence 1, per Logitech's spec), to the first only while you drag; the headset hasn't been heard to apply its equalizer yet. Presets keep it.
- G300 and G300s: the active profile's four DPI levels (250 to 2500), report rate, colour (one of eight) and buttons, saved to the mouse with a backup and read back. Nothing changes until you save, since the G300 can't take a change live; if the mouse's profile button switched profiles in the meantime, Save refuses rather than write the wrong one. Built from PeriHub's notes, which saved on a G300 (MIT); not yet tried on a real G300 in G-Lite.
- G515: a Game view to pick the keys the Game button turns off, as on the G815. Unlike the G815, the G515 leaves Windows and Menu on in Game mode, so they can be picked too. The keyboard forgets the list when turned off, so G-Lite sends it again whenever it connects, even after a quick off and on. Presets keep it. Tried on a real G515 TKL.
- G915 X and G915 X TKL: their Game view can now pick keys, the G515's way. Experimental: not tried on one yet.
- G512 X 75: a Game view to pick the keys Game mode turns off, the G515's way. Experimental: not tried on one yet. Its picture shows the two knobs right of F12, greyed out.
- G515: its picture shows the LIGHTSPEED, Bluetooth and Game buttons and the two status lights above the F keys.
- G515, G915 TKL, G512 X 75 and other keyboards with RGB effects: Wave goes Up and Down when the keyboard says it can (its effect info's direction bits), as on the G815. Not yet tried on these keyboards.
- The demo has the G213, G513, G PRO, G810, G910 and G410 keyboards.

### Changed

- Every device screen follows Settings' layout: the device's picture at the top, without a box, then one setting per row with a short hint, under section titles, a scroll bar when the window is too short, and the footer right after the last row. A row that doesn't apply now is greyed; a row the device doesn't have is left out.
  - Keyboards (G515, G915, G815, G512 X 75, PRO X 60 and the other RGB keyboards): the Bindings view lists the G keys a row each, and the Game view picks its keys on the picture at the top, with the count and Clear below.
  - Zone keyboards (G213, G413, G512, G513, G PRO, G810, G910, G410) and the G560: Zone, Effect, Color, Direction, Brightness and Speed a row each. The speakers pick their effect from a list like the keyboards.
  - Mice: Sensitivity first (DPI stage, DPI, Report rate, Invert scrolling), then Lighting. The Bindings view lists the buttons a row each.
  - Headsets: Sound (Sidetone, Test sidetone) and Power (Turn off after). The battery shows under the name; the firmware line is gone (it's in the report).
  - Litra: Front light and Back light; the light's own effects are a row with Try.
  - MX Master, MX Keys and MX Creative Keypad: a section per feature. Easy-Switch is left out when only one computer is paired.
- Screen effect, on every device: its settings come right after Effect while it runs, its Intensity is now called Color boost, its monitor preview is larger, and adding a zone makes every zone an equal strip, the new one at the right.
- Controls look the same everywhere: one size for toggles, dropdowns, pickers and sliders, and a slider whose setting doesn't apply sits at 0.
- The mouse wheel scrolls the screen past a slider; click or Tab to a slider to move it with the wheel.
- Sliders feel live on every device: while you drag one, G-Lite sends the change at most every 120 ms, where it used to wait until you stopped moving. On the keyboard screens, a drag updates only the values beside the sliders, and the key picture follows with each send, so the slider itself moves smoothly.
- The window has Windows' own title bar, in the sidebar's colour on Windows 11 (dark or light on Windows 10), with Windows' minimize, maximize and close buttons and snapping.
- Report and Backups sit next to the device's name. The line above a screen's footer is gone, and the sidebar lists the devices without a DEVICES heading, saying "Scanning devices…" while it looks.
- Every row in Settings has the same height, Services' too, and Services lists the software without a heading row or Stop all: each row has its own Stop or Start.
- Scrolling is smoother: the content moves in one piece. The device list scrolls without fading its edges.

### Fixed

- G515 and the other RGB keyboards: moving Speed or Intensity during a live effect no longer flashes Save to keyboard on.
- Opening G-Lite from the tray no longer flashes white while the screen draws.
- Clicking Logs while it's open no longer reloads the whole log.
- The window is moved by its title bar only: dragging inside it, even a slider pulled too far, no longer moves it. Where you put it is kept when it opens from the tray.
- Speed sliders show the speed they set: a period read back could show one percent lower, and Ripple's speed couldn't go past 50% with the arrow keys.
- G515: each preview no longer looks up the effect's index again (up to 16 calls), so a drag sends faster.
- Keyboards that report layout code 2 (Solaar reads it as ISO) get the ISO picture; JIS codes too, whose Enter has ISO's shape. G-Lite's reports so far have only 01 (ANSI) and 0D (ISO).
- A device that answers "busy" (error 8) to a setting is asked again after 30, 60 and 90 ms, as Solaar does.
- Mice: after restoring a backup or the factory profile, the mouse leaves its onboard mode and comes back, which drops live changes the restored profile doesn't hold (seen by PeriHub on a G502 HERO).
- Bluetooth devices that answer at index 0x00 are understood (per Solaar; not yet tried).

## [0.0.38-alpha] - 2026-11-23

### Added

- Settings: a Services box listing G HUB, Logitech Gaming Software and the Logitech LampArray Service (which hands Logitech devices to Windows Dynamic Lighting), whether each runs, with Stop and Start, and Stop all. Stop ends their programs, stops their services and keeps them from starting with Windows; Start puts back what their installers set, so it works even without G-Lite's settings, and Task Manager or Services can undo it too. Windows asks for administrator rights once per click. A last row shows whether Windows Dynamic Lighting is on, with a button to its settings.
- Settings scrolls with the mouse wheel when it doesn't fit the window, with a thin bar at the right showing where you are.
- The site and README answer whether you need to uninstall G HUB or Logitech Gaming Software (no: stop them in Settings).

### Changed

- G413: Game mode keys work on the keyboard (tried on a real one), so its Game view no longer says experimental. The G512's and G513's still does.
- Keyboards on the zones screen (G413, G213, G512, G513 and the others with 0x8070): the effects are a dropdown listing only those the keyboard, or the zone picked, takes, instead of a row with the others greyed out.
- G413: the Audio effect works on the keyboard. Its colour row now says Brightness works within the keyboard's own level (Fn+F7), which scales whatever G-Lite sends and which G-Lite can't read or set.

## [0.0.37-alpha] - 2026-11-22

### Added

- G413 (Silver, white backlight): a picture of its keys, with its caps lock and Game mode lights. The keyboard says its LEDs have one colour, so its screen has no colour swatches: Brightness sets the level, as G HUB's black-to-white slider does, and G-Lite always sends white. Off, Static and Breathe, the effects it lists; Breathe's brightness is now sent as a dimmer white, since the keyboard ignored the intensity G-Lite sent (not yet tried on the keyboard). No zone row, since it has one zone. Its name shows as "G413 Silver".
- G413 and other keyboards on the zones screen: an Audio effect, the lighting following the PC's sound, like G HUB's audio visualizer (one level for the whole keyboard). Not yet tried on a keyboard.
- G413: Game mode keys (Fn+F8), picked on a Game view as on the G815, kept in presets. Experimental, not yet tried on the keyboard.
- G512 and G513, experimental: a picture of their keys, G-Lite's live effects (Starlight, Echo, Audio, Rain, Fire, Aurora) drawn key by key, picked from the Effect list next to the keyboard's own, and Game mode keys (Fn+F8) on a Game view. Built from what g810-led and OpenLogi know about these keyboards' per-key lighting; not yet tried on either keyboard. A report, and a try of Rain, would confirm it.

### Fixed

- Keyboards without a cable (a G515 over LIGHTSPEED): live effects failed with "HID++ error 0x08", and Aurora was choppy. The keyboard said it was busy because frames came faster than it could show them. G-Lite now waits and tries again when the keyboard is busy, sends a live effect 10 times a second instead of 25 without a cable, and at most 40 keys a frame there, the most changed first. On USB nothing changes.
- If the keyboard stays busy, G-Lite gives the lighting back to the keyboard before taking it again. If it's still stuck, turning the keyboard off and on fixes it.

## [0.0.36-alpha] - 2026-11-22

### Added

- G502 X PLUS (onboard profile format 5): Save to mouse, like the original PRO X SUPERLIGHT: DPI stages, report rate and buttons, with a backup first and the profile read back after writing, and Restore the factory profile. Its lighting records, write counter and timeouts are left as they are. Not yet tried on a real mouse in G-Lite: the layout comes from OMM's own profile code, and GOMM checked it against OMM on this mouse. Only on the firmware with profile format 5.
- Mice with onboard profile format 6 or 8 (superstrike-studio found format 8 on a SUPERSTRIKE): Save to mouse, as on the PRO X SUPERLIGHT 2. OMM's profile code puts everything G-Lite writes in the same place as on format 7. Not yet tried on a real mouse in G-Lite.
- PRO X SUPERLIGHT 2, PRO X 2 DEX and other mice with profile format 6, 7 or 8: Restore the factory profile. It copies the factory profile over the active one, keeping the profile's name, after checking it reads as this layout. OMM's own restore also copies a factory profile over the user one. Not yet tried on a real mouse in G-Lite.

### Changed

- Mice with profile format 6 to 8 show at most 12 buttons, the slots their profile has.
- Mice with profile format 9 or higher (a G502 X after its latest firmware update reports format 10) show no DPI stages from their profile: read with format 7's layout, they could come out wrong.
- Restore the factory profile is only offered when the mouse says it has one.
- Mice whose onboard profile keeps the report rate in milliseconds (profile formats 2 to 5) only offer rates up to 1000 Hz, the fastest such a profile can hold.
- Mice whose lighting G-Lite can't drive yet (0x8071, like the G502 X PLUS) say "Lighting (not supported yet)" instead of "not supported on this mouse".

## [0.0.35-alpha] - 2026-11-22

### Added

- PRO X SUPERLIGHT 2 and PRO X 2 DEX (onboard profile format 7): Save to mouse. It writes the DPI stages, the default stage, the report rate (for the receiver or the cable, whichever the mouse is on) and the buttons, and leaves the rest of the profile as it was. A backup is made first and the profile is read back after writing. These mice only take their saved DPI stages, so a stage's new DPI reaches the mouse when you save. Not yet tried on a real mouse in G-Lite: the layout and the way to write it come from Featherweight (on a SUPERLIGHT 2) and logitech-onboard-memory (on a DEX), which both checked their writes on the mouse. No factory restore on these mice yet.
- Original PRO X SUPERLIGHT (onboard profile format 4; it calls itself PRO X Wireless): Save to mouse, like the G Pro Wireless: DPI stages, report rate and buttons, with a backup first and the profile read back after writing. Its lighting record is left as it is. Not yet tried on a real mouse in G-Lite: GFlick and onboard-lite wrote this layout on the mouse and read it back. The SUPERLIGHT's light settings are gone from its screen: they drove its status light, which the mouse marks as hidden.
- Newer mice: the Bindings view shows the buttons saved in the profile.
- G600: its screen, read only for now. It shows the active mode's DPI levels, report rate, lighting and its 20 buttons. The G600 doesn't speak HID++: G-Lite reads its feature reports, as libratbag does on Linux. Not yet tried on a real G600: a report would confirm Windows lets G-Lite read them.
- G915 and G915 TKL: the Game view to pick the keys the Game mode key turns off, as on the G815, marked experimental: not tried on one yet, and another project found a G915 kept every key working.
- G915 TKL and G915: a Bindings view, experimental, as on the G815: the G915 TKL's F1–F12 (its G keys) and the G915's G1–G5, saved to the active onboard profile. It only shows when the keyboard reports the button slots expected. Restore the factory profile now brings back their factory bindings too.
- G915 X and G915 X TKL: a Game view that shows the keyboard but can't pick keys yet; they set their Game mode keys through a feature G-Lite doesn't know.
- G915 X and G915 X TKL: their own key pictures, from projects that lit every key on the keyboard: G1–G5 down the left and G6–G9 above F1–F4 on the full size, the media keys, the logo and the brightness key. Still not saved to the keyboard.
- Demo: an original PRO X SUPERLIGHT, a PRO X 2 DEX, a G600, a G915 X and a G915 X TKL. The G915, G915 TKL, G915 X and G915 X TKL are listed together, in that order, and the demo G815 shows its live effects.

### Changed

- G815, G915, G915 TKL, G915 X and G915 X TKL: their key pictures look like the keyboard. The M keys (G6–G9 on the G915 X), Game mode, lighting and media keys are round (in the Game view with their symbols printed on them), the logo is G-Lite's GL in its colour, and the volume roller sits above the media keys. The G915s also show their LIGHTSPEED and Bluetooth buttons, and the G915's numpad + and Enter are two rows tall.
- Mice and the G815's G keys: a button set to no action in G HUB now shows as Disabled.

## [0.0.34-alpha] - 2026-11-21

### Added

- G815: a Game view to pick the keys the Game mode key turns off, on top of the Windows and Menu keys, which are always off. Click a key on the picture to add it or take it off (keys you can pick light up under the pointer); the change reaches the keyboard at once. The Game mode key is outlined on the picture, with its joystick icon. The keyboard forgets the list when it loses power, so G-Lite sends it again whenever the keyboard connects. Presets keep it, and – clears it. The keys show what they type with your Windows keyboard layout.
- G815: Wave also goes up and down, or through every direction (All).
- Keyboards with per-key lighting: live effects that G-Lite draws while it runs, like the Screen effect: Starlight, Echo (keys light up as you type, anywhere in Windows), Audio (bars that follow the sound playing on the PC, turning from your colour to another as they rise, white to red; Solid keeps your colour), Rain, Fire and Aurora. Intensity sets each one's density, height, spread or colour mix. Audio, Rain and Fire go up, down, left, right, out from the centre or in to it. They take a few percent of one CPU core, and pause while the screen is off or locked and, on a wireless keyboard, on low battery, unless you turn that off with the Pause switches beside Intensity. The effects run by G-Lite are marked Live in the list.
- G815: an Intensity slider for Breathe, Cycle and Wave. It shows the intensity the profile has, and Save keeps the one set.
- Reports read the lighting's control, power settings and power mode (0x8071).

### Changed

- Mice and the G815: the Buttons view is now called Bindings.
- G915, G915 TKL and G815: pressing an M key while the Screen effect or a live effect runs stops it, and the keyboard shows that profile's lighting. The running profile's own M key tells G-Lite nothing; to go back to its lighting, click that profile's M button in G-Lite.
- Dropdowns: the picked row is filled green instead of checked.
- Screen effect: Color boost is now called Intensity.
- Keyboards: the effect is picked from a dropdown, which leaves room for more effects. The G815's Brightness slider still stops at its five levels, now without tick marks.
- G512 X 75: Per zone and Keys or Light bar moved to the Lighting box's title row.
- G915, G915 TKL and G815: while G HUB or another app has taken the keyboard off its onboard profiles, the status line says so with a red dot and Save is off, since the save would fail. When the app lets go, the screen reads the keyboard again and M1–M3 come back.

## [0.0.33-alpha] - 2026-11-20

### Added

- Mice: the screen follows the mouse's own DPI button: the stage and DPI change as you press it, and a profile picked with the profile button is read again. The mouse tells G-Lite through its onboard profiles feature (0x8100).
- G815: its own key picture, from Logitech's drawing: the logo above the G keys, M1–M3, MR and Game mode (which don't light and stay dark), the brightness and media keys where they sit, a right Win key in place of the G915's Fn, and a two-row numpad + and Enter.
- Reports read the sectors that onboard profiles point to as macros, so the macro format can be learned, and list the other sectors as unused or unreadable.
- G915, G915 TKL and G815: M1, M2 and M3, next to Revert, show the onboard profile it runs, and switch it. Pressing an M key on the keyboard is followed: the screen shows that profile's lighting and the G-Lite preset goes back to –. Save to keyboard is now Save to M1 (or M2, M3). A switch drops a lighting set live and not saved, without asking, as the M keys do.
- A G815 in the demo.
- G815: a Buttons view for G1–G5, as on the mice: a key with Ctrl, Shift or Alt, a media key, a mouse click, or nothing. They take effect once saved: Save to M1 writes them into that profile. Presets hold the lighting only. Backups give back the G keys with the lighting, and Restore the factory lighting becomes Restore the factory profile, which gives back the factory G keys too.
- Keyboards: the Brightness slider follows the keyboard's own brightness key.
- G815: the Brightness slider stops at the five levels the keyboard has (0, 12, 25, 50 and 100%), marked on the track. Other values dropped to the level below while the slider showed them.

### Changed

- G815: marked as working, after lighting, Save and Restore, M1–M3, the brightness key and G1–G5 were tried on a real keyboard.
- The preset row is labelled G-Lite preset, so it isn't mistaken for a device's onboard profiles, and no preset shows as – instead of None.
- G815: the Lighting box takes the whole width, since the keyboard has no idle settings to show beside it.
- The mouse's Buttons view is now shared with the keyboard screen; it works as before.

### Fixed

- The status line at the bottom of a screen is never cut without a way to read it: a longer text shows whole in a tooltip, and when a button beside it would leave too little room, the text itself takes the click instead.
- G815: after Save to keyboard or Restore the factory lighting, the keys run the new lighting at once. The keyboard kept running its old copy of the profile until an M key was pressed; G-Lite now makes it read the profile again by leaving onboard mode and coming back, which shows as a faint flash of the M key.
- G915, G915 TKL and G815: Dim after and Turn off after no longer offer Never. Saved as never, the G915 TKL dimmed and turned off at once.
- G815: Save to keyboard works without idle times. The G815 is wired and has none, so Save used to refuse; its screen no longer shows Dim after, Dim to and Turn off after.
- G305 (and G304): the lighting is set live and kept by G-Lite, not saved in the mouse's profile, which the mouse ignores (saved, the light went off). G-Lite sets it again when the screen loads and when the mouse is switched off and on, with G-Lite in the tray too.
- Mice: after Restore the factory profile (or a backup), the mouse goes to the restored profile's default DPI stage. The G305 kept the DPI set before, so the screen showed it as a custom stage.
- The log now shows a read the device refuses, with its error, as well as one it doesn't answer. A failed read could leave a screen half loaded with nothing in the log to say why.

## [0.0.32-alpha] - 2026-11-17

### Added

- G915, G915 TKL and G815: Save to keyboard, with Dim after, Dim to and Turn off after. The lighting goes into the keyboard's onboard profile, so it stays without G-Lite. G-Lite backs up the profile first, and Backups can restore an earlier one or the factory lighting. Tried on a G915 TKL.
- MX keyboards: how long the backlight stays on before it fades, with your hands away, with your hands near and while plugged in (Fades after, from 5 s to 1 h). Like the other settings, your choice is kept and sent again when the keyboard comes back.

### Changed

- MX keyboards: the note under Backlight now says the keys light up at the next keypress only when the keyboard is plugged in. On battery, the MX Keys S lights at once and its hand sensor works.
- The G915 TKL is marked Works: it was tried on a real one.

### Fixed

- G915 and G915 TKL: after the keyboard turned itself off, G-Lite's lighting changes didn't show until it was turned off and on. G-Lite now wakes its lighting with each change. Tested on a G915 TKL.
- G915, G915 TKL and the other keyboards G-Lite sets live: the picture shows the ISO layout on ISO keyboards, and the G915 TKL's brightness and media keys sit where they are on the keyboard.

## [0.0.31-alpha] - 2026-11-17

### Changed

- No more dots between words: headings, status lines, menus and the device report use commas, colons and brackets instead, like "Lighting (as on the keyboard)" and "Set by G-Lite, not saved to the keyboard".
- The demo (`--demo`) shows each device's real connection (LIGHTSPEED, USB, Bolt) instead of "Demo", and no "demo" next to the title.
- The G213 is marked Works and the G512 X 75 Partial: both were tried on real ones.
- MX keyboards: while the backlight is off, Automatic shows as off. Under Backlight, the screen says the keys light up when you next type: turned on, the MX Keys S stays dark until a key is pressed.
- Switches that can't be changed look faded, like the sliders.
- MX devices: the status line no longer repeats that buttons, gestures and per-app settings stay in Logi Options+.

### Fixed

- Litra Beam LX and G560: presets now keep the Screen effect and its areas. Turning the Screen effect on, or changing its areas, monitor, colour boost or update rate, enables Update; before, it stayed greyed out.
- Picking a preset while the Screen effect runs also sets its update rate and shows its monitor.
- Two backups taken in the same millisecond no longer share a file name, so the second can't replace the first.

## [0.0.30-alpha] - 2026-11-16

### Added

- Save to mouse on the G502 HERO, G303 and G502 Proteus Spectrum. Their onboard profiles have the same layout as the G Pro Wireless's, so G-Lite saves them the same way, with a backup first and a read-back after. A HERO that has only ever run its factory profile gets its first profile set up, as the G305 does. Untested on a real mouse.
- The G5, G9, G700 and G700s get the mouse screen: DPI and report rate, set while G-Lite runs, not saved to the mouse. The G700 and G700s on their cable or through their own receiver, where they used to show as asleep. Untested on real mice.
- The G500 and G500s get the mouse screen: DPI and DPI stage (up to 8200 on the G500s), report rate, profiles and Save to mouse, the same way as the G9x. Untested on a real mouse.

### Fixed

- MX keyboards: after the brightness keys had been used, G-Lite showed Automatic backlight as off, and changing the backlight could leave it dark. It now shows Automatic, shows the level that's lit, and turning Automatic off keeps that level (or a middle one) instead of turning the backlight off.
- MX mice: the SmartShift threshold is kept when the mouse turns off and on again, instead of going back to its old value (or turning SmartShift back on).
- G9x, G500 and G500s: saving only ever writes the mouse's own profile pages, never its factory ones; a profile list is read only up to its end; and if a save fails halfway, G-Lite puts the profile back as it was instead of leaving a half-written one.
- Litra Beam LX: Try effect's two-colour effect uses the Speed slider's period instead of 50 ms.
- A device's firmware version is the one it runs, when it has more than one.
- Every keyboard picture now has an ISO version, shown when the keyboard reports an ISO layout: the G915, G815, G512 X and the other newer keyboards used to show the US layout. The ISO Enter is drawn as one L-shaped key instead of two.
- The G213's picture matches the keyboard: the numpad's + and Enter are two rows tall, the game mode and light buttons sit above Print Screen and Pause, mute and the volume rocker above the numpad, and play/pause, stop, back and forward in the function row.
- The G213's light button turns the backlight off inside the keyboard, where G-Lite can't see or undo it. Its screen now says to press that button when the keys stay dark.
- On a mouse running its factory profile, buttons set to DPI shift, DPI up, DPI down or other built-in functions show their name instead of "Special 07". Cycle profile, Default DPI, Scroll up and Scroll down are named too, and the G502 HERO's buttons get their names (Sniper, G7, G8…).
- Try effect on the Litra Beam LX sends the smooth colour cycle and smooth colour wave the way the keyboards take them. Before, the light ignored them. Untested on a real light.

## [0.0.29-alpha] - 2026-11-16

### Changed

- With no preset picked, the button next to the presets is now **New…**, which saves the current settings as a new preset, instead of a disabled Update. The preset list is a little narrower, so on a mouse Cancel no longer runs into the Settings and Buttons switch while you name a preset.

### Fixed

- The preset you picked on a device is no longer forgotten when G-Lite quits. It's picked again at the next start, in the tray too, and set on the device if it no longer has it: a G515 or mouse goes back to the lighting saved on it when the preset wasn't saved. If you changed the settings after picking it, your changes are kept and the preset shows its dot.

## [0.0.28-alpha] - 2026-11-15

### Added

- Older G mice and keyboards are listed even when they don't answer G-Lite's usual protocol, so a report can be sent: the G5, G9, G9x, G500, G500s, G700, G700s, G300, G400, G400s, G100s, G600 and MX518 mice, and the G11, G13, G15, G19, G103, G105, G110 and G710 keyboards. They get the info page for now. Untested on real devices.

### Changed

- The tray icon's hover popup looks more like Windows' own tray tooltips: smaller corners, and the tooltip colours of the taskbar's theme. It now sits over the icon and stays put while the mouse moves over it, instead of following the cursor.

## [0.0.27-alpha] - 2026-11-15

### Added

- The G213 ("Gaming Keyboard G213") is recognised and gets a lighting screen: the whole keyboard, or each of its five areas (left to right) in Static. Effects a zone can't do are greyed out. Tested on a real G213.
- The G213 gets Wave, with its direction, and a key picture that shows each zone's lighting. Click a key to pick its zone. It can't store lighting, so G-Lite sets it again at each start.
- The G510 and G510s get a screen: the backlight Off, Static, Breathe or Cycle, its colour, brightness and speed, and presets. Breathe and Cycle are run by G-Lite, so they stop when it quits. Built from its report, untested on a real keyboard.
- Devices on a USB cable without a battery, like the G213, show a USB symbol where the battery goes: in the sidebar, the tray menu, the tray popup and on the device's screen.

### Fixed

- Try effect on the Litra Beam LX no longer fails with HID++ error 0x05: G-Lite now takes control of the light's effects before sending one, and gives it back on Back to G-Lite's colours.
- On the G515, G915, G512 X and other per-key keyboards, a preset now shows it was changed, and Update turns on, after changing the brightness or the Screen effect's settings (monitor, areas, color boost, update rate, smoothing, pauses). Trying to save a preset under a name that's taken now says to use Update.

## [0.0.26-alpha] - 2026-11-13

### Fixed

- A headset charging over USB is no longer listed a second time as its "Battery Charger".
- While a headset charges, its screen and its report show the same battery estimate as the sidebar, instead of a higher one read from the voltage alone.

## [0.0.25-alpha] - 2026-11-13

### Added

- The log shows the updates: each check's result, the download and its checksum, replacing the exe, failures, Skip and Restart, and removing the old exe at the next start.
- Logitech devices that aren't G devices (MX mice and keyboards, the MX Creative Keypad and others) get a screen for their settings: pointer speed, SmartShift, inverted scrolling, the thumb wheel's direction, the backlight, brightness and Easy-Switch, which asks before it switches. What you change is set again when the device comes back. Untested on a real device.
- Reports read SmartShift (0x2110, 0x2111), the thumb wheel (0x2150), the backlight (0x1982) and Easy-Switch (0x1814, and 0x1815 without the computers' names).
- Devices paired to a Bolt receiver are listed: its pairings are read the way Bolt keeps them. Untested on a real Bolt receiver.

## [0.0.24-alpha] - 2026-11-13

### Added

- Older G keyboards that light up through 0x8070 (G512, G513, G PRO keyboard, G810, G910, G410, G413, G213) get a lighting screen: Off, Static, Breathe and Cycle, on all zones or each one, with presets. Live only, and untested on a real keyboard.
- Newer G mice (PRO X SUPERLIGHT 2, PRO X 2, G502 X and others with 0x2202) open on the mouse screen: DPI, the DPI stages saved on the mouse and the report rate up to 8000 Hz. Nothing is saved to them yet. Untested on a real mouse.
- Other G keyboards with 0x8071 lighting (G915 X, G713, G715, PRO X TKL, PRO X 60, G512 X 98 and more) get live lighting, with a key picture picked from their name. Untested on real keyboards.
- G headsets G-Lite can't talk to yet (G735, PRO X 2, G522, A50 X and others) show up in the sidebar, so a report can be sent.
- The G815 opens on the G915's screen.
- The G915's logo, brightness key, media keys and G keys have their light ids, so the key picture and the Screen effect reach them.
- Reports read the newer mice's DPI (0x2202) and report rate (0x8061).
- The Litra screen has a Live on the light picture, like the keyboards and mice: the front light's warmth and brightness and the back light's seven zones, with the running effect. A click on a zone picks it.
- The Litra Beam LX has Try effect, which runs the light's own effects one at a time, to tell which ones light the back light. Reports read where each of its lighting groups sits.

### Changed

- The G560's zones are named Bottom left, Bottom right, Top left and Top right, as checked on real speakers.
- The G515 RAPID TKL no longer gets Save to keyboard: its onboard profile isn't known to match the G515's, so it gets live lighting.
- Report rates of 1000 Hz and up read 1k, 2k, 4k and 8k.
- A selected option that is greyed out is easier to read.
- The Litra's status line says its effects stop if you quit G-Lite, not when you close it: closing only hides G-Lite in the tray.

### Fixed

- The Litra and speaker screens could hit an error when the device dropped out while a colour was being sent.
- On a mouse G-Lite can't save to, moving the DPI slider renamed the current DPI stage button.

## [0.0.23-alpha] - 2026-11-12

### Added

- An Update screen with the new version, its date and what changed since yours. An Update available row in the sidebar opens it, and so does Check for updates in Settings.
- Updates show their progress, and a failed one says why and offers Try again.
- With `--demo`, the update check finds a pretend version, so the Update screen can be tried without installing anything.

### Changed

- G-Lite no longer restarts by itself after an update, automatic ones included. The sidebar shows Restart G-Lite, and until you click it the old version keeps running. The next start runs the new one anyway.
- The device list scrolls when it doesn't fit, and fades at an edge with more devices beyond it.

## [0.0.22-alpha] - 2026-11-12

### Added

- G512 X 75: the keys and the light bar can have their own effect. Turn on Lighting per zone, then pick Keys or Light bar, or click them in the picture. Turning it off gives the bar the keys' effect again. Ripple is for the keys only. The Screen effect still covers both.

### Changed

- The window is 50 px taller, to make room for more settings.

### Fixed

- Switches missed a second click made quickly after the first, which Windows counts as a double-click.
- The G560 and Litra screens kept running the Screen effect (and the Litra its animated effects) after the device was unplugged.
- A device on a receiver that didn't give its name in time was listed under the receiver's name.

## [0.0.21-alpha] - 2026-11-12

### Added

- Litra Beam LX, Beam and Glow: the front light's power, brightness and colour temperature, and on the Beam LX the back light's power, brightness, colours (all seven zones or each one), Breathe, Cycle and Wave (run by G-Lite while it's open) and the Screen effect. Not checked on a real light yet.
- G560 Gaming Speaker: the Screen effect, with four screen areas (one per zone), brightness, colour boost, update rate, smoothing and a pause when the screen is off or locked. The speakers show with a speaker icon.
- G512 X 75: the light bar, as 33 segments under the keys, so the Screen effect lights it too. Its order isn't checked on a real keyboard yet.
- K270/K295: the keyboard shows up, by its wireless id. Its battery shows as full or low, since that's all it reports. A switch uses F1–F12 as media keys.

### Changed

- G512 X 75: the right column is Del, Print Screen, PgUp and PgDn, as G HUB shows it.
- Keyboard Screen effect: the screen picture has a frame, and the pause switches are named "Pause on low battery" and "Pause when screen off or locked".
- Every Logitech device that answers G-Lite shows up in the sidebar, not only G devices, so anyone can send a report for theirs. Devices that aren't G devices only get the info page.

## [0.0.20-alpha] - 2026-11-11

### Changed

- New tray icon: the slanted GL without the disc, sharp at every scaling, white on a dark taskbar and dark on a light one. It switches by itself when the taskbar's colour changes.

## [0.0.19-alpha] - 2026-11-11

### Added

- G560 Gaming Speaker: lighting effects, colour, speed, brightness and presets, live on all four zones at once or on each one. Not checked on real speakers yet.
- The F310, F510 and F710 gamepads show up in D mode (DirectInput), with a report that lists their buttons, sticks and outputs. Nothing on them can be changed yet. In X mode Windows hides them from G-Lite.

### Changed

- Reports read the equalizer's band count and frequencies too.

## [0.0.18-alpha] - 2026-11-11

### Added

- Charging shows as a spark in the battery icon. A device that only reports its voltage (like the G Pro Wireless) shows a level estimated from the charging voltage, keeps the level it had when you plugged it in and counts up from there, and shows 100% when it says it's full.
- A moon with stars replaces the word asleep for a device that doesn't answer.
- G512 X 75: lighting effects, colour, speed, direction, brightness and presets, live on the keys and the light bar. Not saved to the keyboard yet: its onboard files are laid out differently from the G515's. The key picture is built from its layout and not checked on a real keyboard yet.

### Changed

- Devices update live: the receivers say when a device wakes up or goes to sleep, and a device's battery message updates it when charging starts or stops. Every battery is read once a minute and when you hover the tray icon.
- The battery level comes before its icon, and the icons line up on the right in the device list, the tray menu and the hover popup.
- Keyboard reports read the onboard profiles at the size the keyboard gives, so a G512 X 75 report holds its whole 111-byte profile.

### Fixed

- A keyboard on its cable showed twice, once on the cable and once asleep on its receiver.

## [0.0.17-alpha] - 2026-11-10

### Added

- Any mouse button can be changed now, the left one too. While no button is set to Left click, Save to mouse and Update are off, so you can't lose your click.
- Names for the G Pro Wireless buttons: both side pairs and the DPI button underneath.

### Changed

- The tray menu is rounded and lists each device with its battery on the right, then Scan for devices, Settings (opens the window on the settings) and Quit G-Lite. CPU and memory use are in the hover popup.
- A pressed mouse button shows a green band behind its name in the Buttons view, like the selected device.
- The battery level shows without the ~, and the device list is wide enough for 100%.

### Fixed

- The keyboard kept showing the last Screen frame after switching to another effect or pressing Revert, when that lighting matched the saved one.
- The tray menu closed when the hover popup appeared over it.
- The headset screen said it matched the headset when the headset hadn't answered. It now says so, with Try again.

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
- The report lists the device's product IDs, read from the device (like USB C088, LIGHTSPEED 4079).
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
