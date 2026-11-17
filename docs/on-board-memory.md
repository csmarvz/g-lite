# On-board memory slots

A spec for picking which on-board profile a keyboard runs and saves into, from G-Lite. Not built yet. First target: the G915 TKL, whose report and tests are in [G915.md](G915.md). It applies to any device whose on-board profiles G-Lite can already save (0x8100, format 4 on the G915).

Mockups: the G-Lite design canvas "G915 Onboard Profiles", design E (private to the owner).

## What the keyboard has

- The G915 TKL holds three profiles, at sectors 0001–0003, all enabled in the directory (sector 0000: `00 01 01 FF 00 02 01 FF 00 03 01 FF`). Each has a factory copy at 0101–0103.
- Its M1–M3 keys switch between them: each profile maps M1–M3 to `90 0D FF 01`…`03`.
- 0x8100 fn 4 reads the running profile's sector, fn 3 `<sector hi> <lo>` switches to one. The keyboard stays in onboard mode (fn 2 reads 1).
- G-Lite calls them slots and names them by their key: M1, M2, M3 (sector N is MN). Nothing else is shown: not the profile's name (all read PROFILE_NAME_DEFAULT) nor its effect.

## Screen

Everything happens in the footer, on the right, left to right:

1. **The slot dropdown.** The chip icon, then two lines: "On-board memory slot" in small grey text, and the slot below it, like `M1`. 40 px tall, so the footer line stays at 673 like every other screen. Opened, it lists M1, M2 and M3, with "current" next to the slot the keyboard runs.
2. **Reload**, which is Revert renamed. Same behaviour as Revert today: it reads the keyboard again (with None: releases the live lighting and shows what's saved).
3. **Save to M1**: the primary button, named after the slot shown.

The status line stays on the left. The preset row's label becomes **G-Lite preset**, so it isn't mistaken for the keyboard's slots. The key picture box shrinks a little (190 px tall instead of 209) to leave room.

## Behaviour

- **The dropdown always shows the slot the keyboard runs.** G-Lite reads it at load (0x8100 fn 4). Nothing is kept in `settings.txt`: if someone presses M2 on the keyboard, the screen shows M2 at its next load.
- **Picking a slot switches the keyboard to it**, like pressing its M key (fn 3), and loads that slot's lighting and idle settings into the screen. With unsaved changes (a live lighting not saved), G-Lite asks first, since switching replaces them.
- **Save to Mn** writes the slot shown, which is the one running: the same as Save to keyboard today (backup, write, read back, load with fn 3, give the lighting back).
- **Reload** doesn't change slots.
- Backups stay per device. A backup restores into its own sector, and only while the keyboard runs that slot (as today).

## Open questions

- Whether the G915 sends an event when an M key switches the profile, so the screen could follow without a reload. Not seen yet.
- Devices with more or fewer profiles: list as many slots as the directory has enabled. A device whose profiles have no M keys (mice) would need another name than Mn.
