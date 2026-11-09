# Capturing what LGS and G HUB send

An idea for later: a tool that records the HID++ traffic between LGS or G HUB and a device, and prints it in a readable form. It would show exactly what a vendor app sends for a feature G-Lite doesn't have yet.

## Why a driver is needed

A normal Windows app can't see what another app writes to a USB device. [USBPcap](https://desowin.org/usbpcap/) is a signed capture driver. It comes with the Wireshark installer or on its own, and needs a reboot after install. G-Lite would only use it, never ship it. The driver is GPL-2.0 and `USBPcapCMD` is BSD-2-Clause.

Without a driver, G-Lite could only listen: a HID device sends its input reports to every handle that has it open, so G-Lite would see the device's replies while LGS works, but not the requests. HID++ 2.0 replies don't echo the request's arguments, so that's only half the picture. It's enough for HID++ 1.0 acks.

## The tool

A console tool in `tools/`, separate from GLite.exe so the app stays small. C# 5, built with the same csc, no dependencies.

1. **Record.** Find the USB root hub the device is on, run `USBPcapCMD.exe -d \\.\USBPcapN -o capture.pcap` while you do one thing in LGS or G HUB, then stop.
2. **Decode.** Read the pcap. USBPcap packets carry a header with the device address, endpoint and transfer type. HID++ output reports go as interrupt OUT or SET_REPORT control transfers, replies as interrupt IN. Keep reports starting with 0x10, 0x11 or 0x12, and pair each request with its reply.
   - HID++ 2.0: name the feature of each index, learned from the root lookups (feature 0x0000 fn 0) and the feature-set reads (0x0001) in the capture. When the capture starts after those, take the names from a G-Lite report of the same device.
   - HID++ 1.0: name the sub-ids (0x80–0x83 registers, 0x92/0x93 uploads, 0x50 acks, 0x8F errors).
   - Print it in the log's format: time, device, `fn` or `register`, bytes, result.
3. **Summarise memory writes.** Put together what was written (0x8100 fn 6–8, 0x8101 file writes, HID++ 1.0 uploads with 0xA0 copies) into whole sectors or pages, and show a diff with what was there before, when the capture also has a read of it.

## First captures to take

| Capture | For |
|---|---|
| LGS remapping one G9x button, then saving | Issue #16, button encoding on the G9x |
| LGS switching the G9x profile | Issue #17 |
| G HUB saving lighting on a G915 | Saving on the G915 (see [G915.md](G915.md)) |
| G HUB changing the EQ on the PRO X Wireless | Whether G HUB ever writes the headset's EQ |

One action per capture, and note what was clicked, so the bytes map to one change.

## References

- libratbag's Wireshark dissector, MIT: [ratbag-toolbox `dissectors/hidpp-dissector.lua`](https://github.com/libratbag/ratbag-toolbox). It decodes HID++ 1.0 and 2.0 in Wireshark, but not the 0x50/0x9x upload reports. Useful to check the tool's output.
- An LGS capture of a G700s save, with pcapng files: [DanForever/Open-Source-Gaming-Peripheral-Hub `research/g700s/`](https://github.com/DanForever/Open-Source-Gaming-Peripheral-Hub) (GPL-3.0, facts only). A test input for the decoder.
- [more-sources.md](research/more-sources.md#capturing-traffic-on-windows).
