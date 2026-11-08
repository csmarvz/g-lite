# Report rate 0x8061

Newer G mice that go above 1000 Hz have 0x8061 (extended report rate) instead of 0x8060. G-Lite only knows 0x8060, so on those mice it shows no report rate. From OpenLogi (see [openlogi.md](openlogi.md)), which takes it from Logi Options+ 2.8. No recording of a G mouse with 0x8061 yet.

## Which mice

Not confirmed from a report yet. Likely the mice sold with 2k, 4k or 8k Hz: PRO X SUPERLIGHT 2, PRO 2 LIGHTSPEED, G502 X PLUS and LIGHTSPEED, and later. Check `Features` in their reports.

Some may have both 0x8060 and 0x8061. Prefer 0x8061 when it's there.

## Functions

| Fn | Arguments | Reply | Use |
|---|---|---|---|
| 0 | `<connection> 00 00` | Rate mask at byte 1 | Rates the mouse supports on that connection |
| 1 | `00 00 00` | Rate mask at byte 1 | Rates available on the connection in use now |
| 2 | `<connection> 00 00` | Rate index at byte 0 | Current rate on that connection |
| 3 | `<rate index> 00 00` | | Set the rate on the connection in use |

- Connection: 0 wired (USB), 1 LIGHTSPEED.
- The rate index and the mask bits share one table. Byte 0 of the fn 0 and fn 1 replies isn't part of the mask.

| Index | Mask bit | Rate | Interval |
|---|---|---|---|
| 0 | 0x01 | 125 Hz | 8 ms |
| 1 | 0x02 | 250 Hz | 4 ms |
| 2 | 0x04 | 500 Hz | 2 ms |
| 3 | 0x08 | 1000 Hz | 1 ms |
| 4 | 0x10 | 2000 Hz | 500 µs |
| 5 | 0x20 | 4000 Hz | 250 µs |
| 6 | 0x40 | 8000 Hz | 125 µs |

- The mouse sends an event on fn 0 when its rate changes on the mouse itself. Re-read with fn 2.
- 0x8060 for comparison: fn 0 gives a mask of milliseconds (bit n is n+1 ms), fn 1 the current interval in ms, fn 2 sets it in ms. That's what `GMouse` does.

## Which connection to read

fn 1 answers for the connection in use without asking which one it is. For fn 2, pass 1 when the mouse is behind a receiver and 0 when it's wired (`Found.Link`). Or read both and show the one matching fn 1's list.

## Onboard mode

On the G Pro Wireless in onboard mode, 0x8060 fn 2 is refused, and the rate only changes on Save to mouse, through byte 0 of the profile (the interval in ms). With 0x8061:

- It's not known whether fn 3 is refused in onboard mode too. Try it, and fall back to saving as with 0x8060.
- Byte 0 of the profile holds whole milliseconds, so it can't hold 2000 Hz or more. Where those mice keep the rate in the profile is unknown. Their profile format may differ too. Until a report shows the sector, offer rates above 1000 Hz as live only, and save 1000 Hz or less as now.

## Code

| File | Change |
|---|---|
| `app/Mouse.cs` | `State.Rates` and `Rate` from 0x8061 when present, else 0x8060. `SetRate` picks the feature. A rate table for the indexes. |
| `app/PageMouse.cs` | The rate list already comes from `State.Rates`. Show 2000, 4000 and 8000 Hz. Mark a rate the profile can't hold. |
| `app/Report.cs` | Add 0x8061 fn 0, 1 and 2 to `Reads` (all only read). Show fn 0 for both connections, fn 1, and fn 2 for both. Name it "report rate". |
| `app/Fake.cs` | `FakeMouse` with 0x8061 as an option, for the demo and self-tests. |
| `app/SelfTest.cs` | Mask and index decoding, and the feature choice. |

## Order

1. Add the reads to `Report`. Ask owners of those mice for a report (they're common, so an issue or a Reddit post should get one).
2. Build live rate changes against the fake.
3. Check on a real mouse: fn 3 in onboard mode, and where the profile keeps the rate.
