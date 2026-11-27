---
name: screenshots
description: Retake G-Lite's screenshots for the site (site/img) and the README (docs/screenshot-dark.png, docs/screenshot-light.png). Use when the user asks to redo, update or add screenshots or pictures of the app.
---

# Screenshots

`take.ps1` in this folder takes every picture from the installed demo, `%LOCALAPPDATA%\GLite\GLite.exe --demo --few`: a G515 TKL, a G Pro Wireless, a headset and a Litra Beam LX in the sidebar, nothing else.

1. Build first (`app\build.cmd`) so the installed exe is the current one. Closing GLite to build is fine.
2. Run `powershell -ExecutionPolicy Bypass -File .claude\skills\screenshots\take.ps1 -Out <scratchpad>\shots`. It takes about 40 s; don't touch the window meanwhile. It closes the AppData GLite before and after.
3. Look at every picture (Read) before copying: the right screen, no tooltip or open menu, the effect animated.
4. Copy them:
   - `keyboard.png` (Wave), `keyboard-live.png` (Aurora), `mouse.png`, `light.png`, `settings.png` to `site/img/`
   - `keyboard.png` to `docs/screenshot-dark.png`, `keyboard-light.png` to `docs/screenshot-light.png`
5. If the size changed, update every `width`/`height` of the `<img>` in `site/index.html` (now 1384x1277). Check the site in headless Edge at 1400 and 600 px wide.

## How they look, and why

The user rejected earlier sets for these, so keep them:

- Cropped to the window as Windows shows it (DWM's extended frame bounds), not PrintWindow's whole rectangle: that kept the invisible resize borders, which came out black.
- Rounded corners (8 px at 100%), a 1 px border (`3A3A3A` dark, `D4D4D4` light) and a soft shadow, on a transparent PNG (`compose.ps1`).
- At 150% so they stay readable once the site shrinks them: the window is sized 800x800 at 100%, then `dpi.ps1` sends WM_DPICHANGED for 144 dpi with 1326x1200, nearly square and low enough that no screen ends in an empty gap.
- Every picture different: the headline is the keyboard with Wave, "Every key, live" the keyboard with Aurora.

## The helpers

All target the GLite running from AppData, so a copy run from `app\build` is left alone. They post window messages, never move the real mouse.

- `find.ps1 <name> [type]`: a control's window handle by its UI Automation name, optionally its type (`Button`, `ComboBox`); a row's title has the same name as its control, hence the type.
- `press.ps1 <handle>`: Space to a control (a sidebar item, a button, a dropdown).
- `key.ps1 <handle> <virtual key> [times]`: a key to a control, as Left/Right (0x25/0x27) on the Theme picker.
- `resize.ps1 <width> <height>`, `dpi.ps1 -Dpi -FromDpi [-Width -Height]`: the window's size and scale.
- `compose.ps1 -Out <file> [-Border <hex>]`: captures and composes one picture.

For a new picture, add a line to `take.ps1`: `Select-Item "<sidebar name>"`, then whatever the screen needs, then `Save-Shot "<file>.png"`.
