# G815 plan

The steps to finish the G815: its onboard profiles, following M1–M3, G1–G5 bindings and macros. Most of it applies to the G915 and G915 TKL too. Facts come from the reports in [reports/](reports/) and [G815-G915.md](G815-G915.md).

## What the keyboard gives us

- **Three profiles** at sectors 0001–0003. Every profile maps M1–M3 to `90 0D FF 01`…`03`, so an M key makes the keyboard switch profiles on its own, and the firmware lights the active M key.
- **G1–G5** are the profile's first 5 buttons, from 0x20: F1–F5 in profile 1 (`80 02 00 3A`…), 1–5 in profile 2, 6–0 in profile 3. The same 4-byte format as the mice (`Buttons.cs`).
- **Macros:** G HUB saved two to the G815 on 2026-11-20, and the report read them: key down `43`, key up `44`, wait `40`, go on in another sector `60`, end `FF`, in sectors of their own with a CRC. G HUB saves System copy, a media key and a mouse click as plain bindings, not macros. See [G815-G915.md](G815-G915.md), "Macros".

## Steps

### 1. Onboard profiles (built, tried on the G815)

Build the slots of [on-board-memory.md](on-board-memory.md) for the G815 and G915, with one change to its screen:

- **M1, M2, M3 in the footer**, left of Revert and as tall, as a `SegmentedPicker`.
- It shows the profile the keyboard runs (0x8100 fn 4). Picking one switches the keyboard to it (fn 3), like pressing the M key, and loads its lighting.
- Save to Mn writes the slot shown, the running one: Save to keyboard renamed.

### 2. Follow M1–M3 (built, tried on the G815)

- Mice send a 0x8100 event (fn 0) when their buttons switch profiles, and `MousePage.Heard` loads again. The keyboard screen gets the same.
- Built both ways: the event, and fn 4 read when the window is activated, which costs one call and catches a missed event.
- An M key switch releases a live lighting that isn't saved and shows the new slot's saved lighting, as the keyboard itself does. The G-Lite preset goes back to –, which means the keyboard's own lighting (picking – already releases it and reads the profile again). A preset with changes not saved keeps them as a draft, as when another preset is picked.
- The same for a switch made in G-Lite (step 1).

### 3. G1–G5 bindings (built, tried on the G815 on 2026-11-20)

A Settings | Bindings switch on the G815 screen, as on the mouse, with the mouse's rows (`ButtonRows` in `Buttons.cs`): a mouse click, a media key, Disabled, or Keystroke… (a key with Ctrl, Shift or Alt). A macro from G HUB shows as Macro and stays unless changed. Changes take effect on Save to M1, which writes the running slot's G keys with its lighting (only the G keys when the lighting wasn't changed). Presets leave the G keys out: they belong to the onboard profile (tried with presets keeping them, then left out on 2026-11-20). An unsaved change says "G keys not saved to M1". No live preview: that would need G-Lite to take over the G keys (0x8010), which the project avoids. M1–M3 and the G-shift layer at 0x60 stay as they are.

### 4. Macros

- Done: the report reads the sectors the profiles' macros point to (`macro 0004=…`, fn 5 only). Next: new reports from the G915 TKL, which has macros, and the G815, after recording a macro in G HUB.
- Decode the format: the G815's `asd` and long text gave key down, key up, wait, jump and end; libratbag's list of the other steps is in [G815-G915.md](G815-G915.md), "Macros". G HUB no longer saves macros to the G815, so the rest waits for G-Lite's own editor, tested with short macros of each step.
- An editor: a list of key down, key up and delay steps, and Record, which takes the keystrokes typed while the G-Lite window has the focus.
- Writing picks a free sector and keeps the directory and the other profiles right. New ground: backups, read back, a fake and traces before a real keyboard.

### 5. MR

Left alone. The manual's MR steps (MR, a G key, the keys, MR) record nothing on the keyboard alone: MR doesn't even light (tried on 2026-11-20). G HUB's MR records a macro on the fly by taking over MR and the G keys (0x8030, 0x8010 in software mode) and watching the keystrokes. That is button diversion, and it only works while the app runs. Record in step 4 does the same job. MR stays drawn, dark and unbound.

## Also

- **A G815 in `--demo`:** done, on `FakeG915` with three profiles (its sectors are 255 bytes, not 256). The G915 trace covers steps 1 and 2.
- **Game mode (0x4522, in the report):** which keys the Game mode key turns off. G HUB turns off the Windows and Menu keys always and lets you add keys with a standard usage. The list is lost when the keyboard loses power (tried on 2026-11-21), so G-Lite would send it each time the keyboard connects. The format is OpenLogi's, and fn 0, 1, 2 and 3 work on the G815 (tried on 2026-11-21, [G815-G915.md](G815-G915.md)). Built on 2026-11-21: a Game view on the G815 screen, where clicking a key on the picture adds it to the list or takes it off. Each change goes to the keyboard at once, and the list is kept in `settings.txt` and in presets, and sent again when the keyboard connects. "–" clears it. Tried on the G815 on 2026-11-21: the picked keys are off in Game mode, the list comes back after a replug without opening the screen, G HUB opening and closing turns the status red and back with the list still working, and a preset sets its keys. The G915 and G915 TKL wait for a try on a real one, since OpenGHub couldn't make it work on a G915.
- **Restore the factory profile:** done on 2026-11-20. On the G815 it brings back the factory lighting and G1–G5 (M1–M3 and the G-shift layer are left as they are); a backup restores the whole profile.
- **Report rate (0x8060):** the mouse code has it. Cheap, of little use on a keyboard.
- Not in this plan: per-key colours (0x8081).

## Try on the keyboard first

One session, before any screen is built:

- Done on 2026-11-20: an M key pressed with G-Lite in front, and the screen follows at once, so the G815 sends the 0x8100 event. The brightness key's 0x8040 event comes too.
- Done on 2026-11-20: a macro made in G HUB's editor and saved to G1 in onboard memory mode, then a report ([G815-G915.md](G815-G915.md), "Macros"). MR records nothing onboard. Next: macros with modifiers (Ctrl+C), a media key and a long text, to learn the other step codes.
- Done on 2026-11-21: brightness + a number key changes the lighting live only; the running profile keeps its saved lighting.
- Done on 2026-11-21: 0x8071 fn 8 (power modes) answers, mode 1, and fn 7 has no timers ([G815-G915.md](G815-G915.md)).
