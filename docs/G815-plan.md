# G815 plan

The steps to finish the G815: its onboard profiles, following M1–M3, G1–G5 bindings and macros. Most of it applies to the G915 and G915 TKL too. Facts come from the reports in [reports/](reports/) and [G815-G915.md](G815-G915.md).

## What the keyboard gives us

- **Three profiles** at sectors 0001–0003. Every profile maps M1–M3 to `90 0D FF 01`…`03`, so an M key makes the keyboard switch profiles on its own, and the firmware lights the active M key.
- **G1–G5** are the profile's first 5 buttons, from 0x20: F1–F5 in profile 1 (`80 02 00 3A`…), 1–5 in profile 2, 6–0 in profile 3. The same 4-byte format as the mice (`MouseButtons.cs`).
- **Macros:** none on the G815 yet. The G915 TKL's G-shift layer points to `00 04 00 00`, `00 04 00 02`…: sector 0004 and offsets. No report has read a macro sector yet, so the format isn't known.

## Steps

### 1. Onboard profiles

Build the slots of [on-board-memory.md](on-board-memory.md) for the G815 and G915, with one change to its screen:

- **M1, M2, M3 in the header**, right of the device name, as a `SegmentedPicker` in the Effect picker's style. It replaces the spec's footer dropdown.
- It shows the profile the keyboard runs (0x8100 fn 4). Picking one switches the keyboard to it (fn 3), like pressing the M key, and loads its lighting.
- Save to Mn writes the slot shown. Save to keyboard already writes the active profile.

### 2. Follow M1–M3

- Mice send a 0x8100 event (fn 0) when their buttons switch profiles, and `MousePage.Heard` loads again. The keyboard screen gets the same.
- If the G815 sends no event, read fn 4 when the window opens or gets the focus.
- An M key switch releases a live lighting that isn't saved and shows the new slot's saved lighting, as the keyboard itself does.

### 3. G1–G5 bindings

The mouse's button editor (`MouseButtons.Bindings`) on the keyboard screen: a key, a key with modifiers, a media key, nothing, or a macro (shown, not editable, until step 4). Saving writes the slot's buttons. M1–M3 and the G-shift layer at 0x60 stay as they are.

### 4. Macros

- The report reads the macro sectors (fn 5 only, so it fits `Report.Reads`). Ask the G915 TKL owner, who has macros, and the G815 owner for new reports, after recording a macro in G HUB.
- Decode the format. libratbag (MIT) describes the onboard macro format: use it to check.
- An editor: a list of key down, key up and delay steps, and Record, which takes the keystrokes typed while the G-Lite window has the focus.
- Writing picks a free sector and keeps the directory and the other profiles right. New ground: backups, read back, a fake and traces before a real keyboard.

### 5. MR

Left alone. G HUB's MR records a macro on the fly by taking over MR and the G keys (0x8030, 0x8010 in software mode) and watching the keystrokes. That is button diversion, and it only works while the app runs. Record in step 4 does the same job. MR stays drawn, dark and unbound.

## Also

- **A G815 in `--demo`:** a fake with 256-byte sectors and three profiles, and traces. Needed for steps 1–4, since the owner can test only now and then.
- **Game mode (0x4522, in the report):** which keys the Game mode key turns off. G HUB turns off the Win key by default and lets you pick others. Check the format first.
- **Restore a whole factory profile**, bindings included, once G-Lite changes them. Today it restores the lighting only.
- **Report rate (0x8060):** the mouse code has it. Cheap, of little use on a keyboard.
- Not in this plan: per-key colours (0x8081).

## Ask the owner first

One session, before any screen is built:

- Press M1–M3 with `log.txt` on: does the 0x8100 event come?
- A report with the macro sectors, after recording a macro in G HUB.
- Does 0x8071 fn 8 (power modes) answer? (already a "To check" in [G815-G915.md](G815-G915.md))
