# Lighting control

How 0x8071 (G515, G915) and 0x8070 (mice, older keyboards) decide whether an effect is live or stored, and who owns the LEDs. From OpenLogi (see [openlogi.md](openlogi.md)), which takes 0x8071 from Logitech's v4 spec and 0x8070 from Logi Options+.

## 0x8071 RGB effects

### Set effect (fn 1)

`<cluster> <effect index> <10 params> <flags>`, 13 bytes.

The flags byte:

| Bits | Meaning |
|---|---|
| 0 (0x01) | Volatile: apply now, in RAM |
| 1 (0x02) | Non-volatile: store in EEPROM |
| 2–3 | Power mode the effect is for: 0 full power, 1 power save |

- G-Lite sends 0x01: live only. On the G515 that's right, since saving goes through the 0x8101 profile. On the G915 the page says "not saved to the keyboard", which matches.
- 0x03 would apply and store. On a keyboard without 0x8101, like the G915, that may be how to save. See "Saving on the G915" below.
- Bits 2–3 = 1 would set the effect shown in power save (the dimmed state). That may be how G HUB sets the G915's idle lighting.
- OpenLogi says a volatile effect only shows while software holds control (fn 5), and the firmware drops it on power loss.

### Information (fn 0)

`<cluster> <effect> 00`. 0xFF means "all".

| Call | Reply |
|---|---|
| `FF FF 00` (device) | Cluster count at 2, non-volatile capabilities at 3–4, extended capabilities at 5–6, multi-cluster effect count at 7 |
| `<c> FF 00` (cluster) | Cluster at 0, location at 2–3, effect count at 4, display persistency at 5, effects can be stored at 6, multi-LED pattern at 7 |
| `<c> <e> 00` (effect) | Cluster at 0, effect at 1, effect id at 2–3, capabilities at 4–5, period at 6–7 |

All two-byte fields are big endian.

- `G915.Clusters` and `G515.Preview` use only the effect form. Reading the cluster form first gives the effect count without probing until an error.
- Byte 6 of the cluster reply says whether the cluster can store effects in EEPROM. Read it before offering a save that uses flag 0x02.
- Extended capabilities: bit 0 stored-effect info, bit 2 LED bin, bit 3 monochrome only, bit 4 no effect sync, bit 5 shutdown (fn 9), bit 6 cluster-changed event.
- Non-volatile capabilities: bit 0 boot-up effect, 1 demo, 2 user demo, 3 events display, 4 active dimming, 5 ramp down to off, 6 shutdown effect.

### Software control (fn 5)

`<get/set> <control> <events>`. First byte 0 reads, 1 sets.

| Byte | Bit 0 | Bit 1 | Bit 2 |
|---|---|---|---|
| Control | All clusters (needed for fn 1) | Power modes (needed for fn 8) | |
| Events | Effect sync | User activity | No-activity timeout |

- `G515.Claim` and `G915.Claim` send `01 03 04`: clusters and power modes, and the no-activity event. OpenRGB sends `01 03 07` on the G915, which only adds events. Either should work.
- Release sends `01 00 00`.
- OpenLogi reads the flags first (`00 00 00`), adds bit 0, and on exit puts back what it read. That's the polite way, in case G HUB or something else held control. G-Lite could do the same: read in `Claim`, keep the bytes, send them back in `Release`. It's not urgent.

### Power modes (fn 7, fn 8)

- fn 7 `00 00 00` reads the power-mode configuration: flags at 1–2, seconds of no activity before power save at 3–4, seconds before off at 5–6.
- fn 7 `01 <flags 2> <save 2> <off 2>` writes it. OpenLogi has the function but doesn't use it.
- fn 8 `00 00 00` reads the current power mode at byte 1: 1 full, 2 power save, 3 off. fn 8 `01 <mode> 00` sets it, and needs control bit 1.
- This looks like the G915's idle dim and sleep, which the G515 keeps in its 0x8101 profile instead. The G915 reports 0x8071 version 0, and these functions come from the v4 spec, so they may not be there. A report with fn 7 would tell.

### Saving on the G915

Two ways, to try in this order on a real keyboard:

1. Send the effect with flags 0x03 (apply and store) after checking cluster byte 6. If the keyboard shows it after a power cycle, without G-Lite running, that's saving with no profile layout to work out.
2. Write the 0x8100 profile, as planned in [G915.md](G915.md).

What could go wrong with 1: in onboard mode, the active profile's lighting may win over the stored effect. And EEPROM wears, so store only on Save, never on each preview.

## 0x8070 colour LED effects

### Set effect (fn 3)

`<zone> <effect index> <10 params> <persistence>`, 13 bytes. The persistence byte is a value, not bits like 0x8071:

| Value | Meaning |
|---|---|
| 0 | Volatile, RAM only, lost on power cycle |
| 1 | RAM and EEPROM |
| 2 | EEPROM only |

- `GMouse.SetLight` sends 12 bytes, so the persistence is 0: live only. Save to mouse writes the profile instead. That's right.
- On a device without onboard profiles, like the G512, 1 is how to save. See [keyboards-8070.md](keyboards-8070.md).

### Information

- fn 0: zone count at 0, non-volatile capabilities at 1–2, extended capabilities at 3–4.
- fn 1 `<zone> 00 00`: zone at 0, location at 1–2 (1 primary, 2 logo, 3 left, 4 right, 5 combined, 6–11 primary 1 to 6), effect count at 3, persistency capabilities at 4.
- fn 2 `<zone> <effect> 00`: zone at 0, effect at 1, effect id at 2–3, capabilities at 4–5, period at 6–7.
- `GMouse.Read` sends fn 1 and fn 2 with `FF 00` after the zone or effect. It works on the G Pro Wireless. OpenLogi sends zeros.
- Extended capabilities: bit 0 fn 14 supported, bit 1 fn 9 not supported, bit 2 LED bin, bit 3 monochrome only, bit 4 no effect sync.

### Effect ids

| Id | Effect |
|---|---|
| 0x00 | Off |
| 0x01 | Static |
| 0x02 | Breathing (older) |
| 0x03 | Cycle |
| 0x04 | Wave |
| 0x05 | Starlight |
| 0x06 | Light on press |
| 0x07 | Audio visualizer |
| 0x08 | Boot-up |
| 0x09 | Demo |
| 0x0A | Breathe |
| 0x0B | Ripple |

These are the same ids the G915 uses on 0x8071. OpenLogi only encodes Static (colour in params 0–2). `GMouse.Encode` has Static, Breathe and Cycle.

### Reading back (no need to remember the lighting)

- fn 9 `<zone> <source> 00` reads the effect settings: colour at 1–3, period at 4–5, brightness at 6, a parameter at 7. Source 0 is RAM, 1 EEPROM. Not supported when extended capability bit 1 is set.
- fn 14 `<zone> <source> 00` reads the current effect: zone at 0, effect index at 1, the 10 params at 2–11. Needs extended capability bit 0.
- fn 12 `<zone> 00 00` reads the colour shown now, at 1–3.
- fn 10 `<zone> 00 00` clears a zone back to its default.

With fn 14, a page could show what the device is really doing instead of what G-Lite remembers.

### Software control (version 1 and up)

- fn 7 reads: control at 0 (0 firmware, 1 software), sync events at 1.
- fn 8 `<control> <sync> 00` sets it. Not stored.
- OpenLogi only sends these when the root reports version 1 or later, following Options+. Not checked on hardware.
- G-Lite sets mouse effects without claiming, and it works on the G Pro Wireless. Don't add a claim unless a device ignores live effects.

## To do

1. Report: read 0x8071 cluster info, fn 5 and fn 7 (see [openlogi.md](openlogi.md), Report).
2. G915: try flags 0x03, behind Save, with a power-cycle test.
3. Claim: keep the flags read before claiming and put them back on release.
