# MX keyboards: key bindings (not done)

Logi Options+ lets the MX Mechanical Mini's F4 to Vol+ keys and Home, End, Page Up and Page Down do something else: a keyboard shortcut, an app, a media action, nothing. G-Lite doesn't, because the keyboard can't do it on its own: Options+ diverts the key to software and acts on it itself. G-Lite never diverts a button (CLAUDE.md). This page keeps what was learned, so it can be built later without the keyboard at hand.

## Why it needs diversion

0x1B04 (special keys and mouse buttons, v5 on the Mini) lists 32 controls. Each has a group (byte 6) and a group mask (byte 7) naming the groups it can be remapped to inside the keyboard (fn 3's bytes 3–4). On the Mini every control is group 0 with mask 0: no firmware remap. What's left is diverting: a diverted control no longer does its own action, and the keyboard sends an event instead, which an app turns into the action. That's how Options+ does it (seen: its bound F4, and its own actions such as Dictation and Emoji, read as diverted).

## 0x1B04

Facts from Solaar (names and layouts only; it's GPL) and Logitech's x1B04 spec:

- **fn 0**: b0 the number of controls (`20` on the Mini).
- **fn 1** `<index>`: b0–1 the control id (CID), b2–3 the task id (TID, its default action), b4 flags, b5 the position (1 to 12 for F1–F12, 0 for the others), b6 group, b7 group mask, b8 more flags. Flags (b4 | b8 << 8): 0x01 mouse button, 0x02 an F key, 0x04 non-standard (a hot key), 0x08 Fn-sensitive, 0x10 reprogrammable, 0x20 divertable, 0x40 persistently divertable, 0x80 virtual, 0x100 raw XY, 0x200 force raw XY, 0x400 analytics key events, 0x800 raw wheel.
- **fn 2** `<CID>`: how it reports now: b0–1 the CID, b2 mapping flags (0x01 diverted, 0x04 persistently diverted, 0x10 raw XY diverted, 0x40 force raw XY diverted), b3–4 the CID it's remapped to (0 or itself: not remapped), b5 more mapping flags (0x01, that is 0x100: analytics key events reporting; 0x04, 0x400: raw wheel). Only reads: the report shows it as `reporting <n>`.
- **fn 3** `<CID> <flags> <remap CID>`: sets it. Each flag has a valid bit one above it, and only flags whose valid bit is set change: `00 E2 03 00 00` diverts F4 (diverted 0x01, its valid bit 0x02), `00 E2 02 00 00` stops (valid, diverted off). A remap CID of `00 00` keeps the mapping. Persistent diversion is 0x04 with valid 0x08; the Mini's controls don't offer it (no 0x40 flag).
- **Event 0** (diverted buttons, spec x1B04): sent when a diverted control is pressed or released, b0–7 up to four CIDs held down now (2 bytes each, `00 00` after the last). Pressing F4 alone sends `00 E2 00 00 …`, releasing it `00 00 …`. Not yet seen on the Mini.

A temporary diversion lasts until the keyboard resets or reconnects (turned off, out of range, Easy-Switch away and back): an app must set it again each time the keyboard connects, as Options+ does. While the app isn't running, a diverted key does nothing.

## The Mini's controls

Read on 2026-11-26 (firmware 72.03), after Logi Options+ had run with F4 set to a keyboard shortcut. fn 1 from `docs/reports/mxmechanicalmini-b367.txt`, fn 2 read on the keyboard. Names from Solaar's tables.

| fn 1 index | CID | Name | TID | Flags | Key | fn 2 (b2, b3–4, b5) |
|---|---|---|---|---|---|---|
| 0 | 00D1 | Host switch 1 | AE | 0A: F key, Fn-sensitive | F1 | `00`, `00 00`, `00` |
| 1 | 00D2 | Host switch 2 | AF | 0A | F2 | `00`, `00 00`, `00` |
| 2 | 00D3 | Host switch 3 | B0 | 0A | F3 | `00`, `00 00`, `00` |
| 3 | 00E2 | Backlight down | C1 | 3A: F key, Fn-sensitive, reprogrammable, divertable | F4 | `01` diverted (its shortcut), `00 E2` |
| 4 | 00E3 | Backlight up | C2 | 3A | F5 | `00`, `00 E3` |
| 5 | 0103 | Dictation | D8 | 3A | F6 | `01` diverted, `01 03` |
| 6 | 0108 | Emoji | DD | 3A | F7 | `01` diverted, `01 08` |
| 7 | 010A | Screen capture | DF | 3A | F8 | `01` diverted, `01 0A` |
| 8 | 011C | Mute microphone | F1 | 3A | F9 | `01` diverted, `01 1C` |
| 9 | 00D4 | Search | B1 | 3A | F10 | `01` diverted, `00 D4` |
| 10 | 0141 | Play/Pause | FD (not in Solaar's table) | 3A | F11 | `00`, `01 41` |
| 11 | 00E7 | Mute | 03 | 3A | F12 | `01` diverted, `00 E7` |
| 12 | 00E8 | Volume down | 02 | 34: hot key, reprogrammable, divertable | Vol- | `01` diverted, `00 E8` |
| 13 | 00E9 | Volume up | 01 | 34 | Vol+ | `01` diverted, `00 E9` |
| 14 | 010B | Grave accent | E0 | 30: reprogrammable, divertable | `` ` `` | `00`, `00 00` |
| 15 | 010C | Tab | E1 | 30 | Tab | `00`, `00 00` |
| 16 | 010D | Caps Lock | E2 | 30 | Caps Lock | `00`, `00 00` |
| 17 | 010E | Left Shift | E3 | 30 | Shift | `00`, `00 00` |
| 18 | 010F | Left Control | E4 | 30 | Ctrl | `00`, `00 00` |
| 19 | 0110 | Left Option/Start | E5 | 30 | Win | `00`, `00 00` |
| 20 | 0111 | Left Command/Alt | E6 | 30 | Alt | `00`, `00 00` |
| 21 | 0112 | Right Command/Alt | E7 | 30 | right Alt | `00`, `00 00` |
| 22 | 013C | Right Option/Start | F8 | 30 | right Ctrl | `00`, `00 00` |
| 23 | 0115 | Right Shift | EA | 30 | right Shift | `00`, `00 00` |
| 24 | 011E | Backslash | F3 | 30 | `\` | `00`, `00 00` |
| 25 | 0117 | Delete | EC | 30 | Del | `00`, `00 00` |
| 26 | 0118 | Home | ED | 30 | Home | `00`, `00 00`, `01` analytics |
| 27 | 0119 | End | EE | 30 | End | `00`, `00 00`, `01` analytics |
| 28 | 011A | Page Up | EF | 30 | Page Up | `01` diverted, `00 00`, `01` analytics |
| 29 | 011B | Page Down | F0 | 30 | Page Down | `01` diverted, `00 00`, `01` analytics |
| 30 | 00DE | F Lock | 62 (do nothing) | 00 | Fn and Esc | `00`, `00 00` |
| 31 | 0034 | Fn | 62 | 00 | Fn | `00`, `00 00` |

All have b8 `04` (0x400: analytics key events), except Fn. The Options+ screen offers F4 to Vol+ and Home to Page Down for bindings; the other reprogrammable keys (Tab, Caps Lock, the modifiers) are likely for its Mac and Windows layouts. Easy-Switch (F1–F3), F Lock and Fn can't be diverted.

## Left by Options+

With Options+ stopped, its diversions stay until the keyboard reconnects: on 2026-11-26 F4, F6 to F10, F12, Vol-, Vol+, Page Up and Page Down read as diverted with no app to act on them. Turning the keyboard off and on clears them.

G-Lite gives them back (`OtherDevice.ReleaseDiverted`), which isn't diverting: for each divertable control that reads as diverted, fn 3 `<CID>` with the valid bit of each diversion flag that's on and the flag off (`02` for a plain diversion), remap `00 00`. It does it when a device with 0x1B04 is found while Options+ is installed but not running, and on every such device when Settings stops Options+; the screen says how many keys work again. Tried on the Mini (2026-11-26): the 11 keys read as not diverted afterwards (the remap bytes read `00 00` from then on) and did their own actions again. Page Up and Page Down keep their analytics reporting (b5), which G-Lite leaves alone. While Options+ runs, G-Lite leaves its diversions alone.

## Building it

Most of it can be built and tested on a fake keyboard:

1. `FakeOther` answers 0x1B04 fn 0 to fn 3 from the table above (keeping what fn 3 sets) and can send event 0.
2. On connect, G-Lite reads fn 1 for every control, diverts the bound ones (fn 3, divert with its valid bit), and listens for event 0 on the device's input reports. A press of a bound CID runs its action: a shortcut through `SendInput`, a media key, an app. A CID in the event that wasn't there before is a press; one that's gone is a release.
3. It sets the diversion again each time the keyboard connects (0x41 or a scan), and undoes it (fn 3 `02`) when the binding is removed or G-Lite quits.
4. A trace drives it: bind F4, check fn 3 `00 E2 03 00 00`, send event 0 `00 E2`, check the action, unbind, check fn 3 `00 E2 02 00 00`.

What only the keyboard can confirm: event 0's bytes on a real press, that F4 does nothing while diverted, and that a reconnect clears it.
