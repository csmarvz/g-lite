# On-board memory slots

A spec for picking which on-board profile a keyboard runs and saves into, from G-Lite. Built on 2026-11-20, except Reload (the footer still says Revert) and asking before a switch drops unsaved changes (an M key on the keyboard can't ask either). First target: the G915 TKL, whose report and tests are in [G815-G915.md](G815-G915.md). It applies to any device whose on-board profiles G-Lite can already save (0x8100, format 4 on the G915).

Mockups: the G-Lite design canvas "G915 Onboard Profiles", design E (private).

## What the keyboard has

- The G915 TKL holds three profiles, at sectors 0001–0003, all enabled in the directory (sector 0000: `00 01 01 FF 00 02 01 FF 00 03 01 FF`). Each has a factory copy at 0101–0103.
- Its M1–M3 keys switch between them: each profile maps M1–M3 to `90 0D FF 01`…`03`.
- 0x8100 fn 4 reads the running profile's sector, fn 3 `<sector hi> <lo>` switches to one. The keyboard stays in onboard mode (fn 2 reads 1).
- G-Lite calls them slots and names them by their key: M1, M2, M3 (sector N is MN). Nothing else is shown: not the profile's name (all read PROFILE_NAME_DEFAULT) nor its effect.

## Screen

The slots are a segmented picker in the footer, left of Revert and as tall: M1, M2, M3, the one the keyboard runs picked (first built right of the device name, moved on 2026-11-20). The footer, on the right, left to right:

1. **Reload**, which is Revert renamed. Same behaviour as Revert today: it reads the keyboard again (with – as the G-Lite preset: releases the live lighting and shows what's saved). Not built: the button still says Revert.
2. **Save to M1**: the primary button, named after the slot shown.

The status line stays on the left. The preset row's label is **G-Lite preset** (done on every screen), so it isn't mistaken for the keyboard's slots.

## Behaviour

- **The picker always shows the slot the keyboard runs.** G-Lite reads it at load (0x8100 fn 4). Nothing is kept in `settings.txt`: if someone presses M2 on the keyboard, the screen shows M2 at its next load.
- **Picking a slot switches the keyboard to it**, like pressing its M key (fn 3), and loads that slot's lighting and idle settings into the screen. A live lighting not saved is dropped without asking, as an M key on the keyboard drops it (asking was the first idea), and Save stays off until the new slot is read.
- **Save to Mn** writes the slot shown, which is the one running: the same as Save to keyboard did (backup, write, read back, load with fn 3, give the lighting back; on the G815, then leave onboard mode and come back, since it doesn't reload the profile it runs: see [G815-G915.md](G815-G915.md)).
- **Reload** doesn't change slots.
- Backups stay per device. A backup restores into its own sector, and only while the keyboard runs that slot (as today).

## Open questions

- The G815 sends an 0x8100 event when an M key switches the profile: the screen follows at once (tried on a real one on 2026-11-20). The G915 and G915 TKL haven't been tried; G-Lite also reads fn 4 when the window is activated, so the screen follows either way ([G815-plan.md](G815-plan.md) step 2).
- Devices with more or fewer profiles: list as many slots as the directory has enabled. A device whose profiles have no M keys (mice) would need another name than Mn.
