# Splitting device screens from device drivers

A plan, not started. Each found device gets a `DevicePage` (or `InfoPage`) as soon as the scan finds it, and the page lives until the device goes. That page is two things in one class: the screen you see and the code that keeps the device working. So every device has its whole screen built, hidden, even if it's never opened.

## Why change it

- A screen has dozens of controls, and each is a Windows window. With the demo's ~30 devices that's well over a thousand windows.
- A DPI change (moving the window to a monitor with another scaling) scales every control of every screen, and Windows tells every one of those windows about the new DPI first. Measured going from 100% to 150%: 1.7 s with the demo, 150 ms with two devices. About half is Windows' notices, the rest `Scale()` (0.6 s with the demo) and fonts (0.2 s).
- Memory and startup grow the same way.

Hidden controls aren't painted, so the cost isn't drawing. It's that they exist.

## What a device needs without its screen

What `DevicePage` and its subclasses do while hidden (`app/Pages.cs`):

- The work queue and the connection (`RunOnDevice`, `Queue`, `Opened`, `ReopensWhenFound`, the lost connection reopened by a scan).
- Sending the saved state again after a scan or a reconnect (`LoadToSendAgain`, `SendsAgain`, `Reconnected`), like the keyboard's Game mode keys and the media keys on `InfoPage`.
- The found device and its battery (`SetFound`).
- The device's events (`Heard` on `KeyboardPage` and `MousePage`).
- Presets: what's saved, what's loaded, and applying one (`Current`, `ApplyPreset`, `Settings` keys).
- What runs on its own: the keyboard's live effects and Screen effect, its idle watch, `Presence`; the zones screen's live, Audio and Screen effects; the G510's Breathe and Cycle; the Litra's animation and Screen effect.

The rest is the screen: the controls, `RefreshControls`, the status line, menus, `Shown` and `Hidden`.

## The split

- A device object per found device, made by `DeviceTable` like the pages are now, holding everything in the list above. It has no controls and isn't a `Control`. It raises events when its state changes (read, saved, failed, battery).
- A screen per device, made the first time the device is picked and attached to its device object. It reads the device object's state to fill its controls and calls it for every change. Closing G-Lite to the tray, or a device going, can drop its screen.
- `MainWindow` keeps device objects in place of `pages`, and builds screens on demand. Only built screens are scaled on a DPI change.

## Order

1. Start with the smallest screens (`HeadsetPage`, `OtherDevicePage`) to settle the shape of the device object and its events.
2. Then `LightPage`, `G510Page` (three views, the M keys), `ZonesPage`, `MousePage`, and `KeyboardPage` last (the largest: live effects, Game mode, G keys, onboard profiles).
3. Each step keeps the traces in `app/tests/` identical: the messages sent can't change. The traces drive screens on fakes, so `Traces.cs` builds a device object and its screen together.

## What it doesn't change

The protocol code (`Hidpp`, `KeyboardLighting`, `MouseProtocol` and the others) stays as it is. Only where the page code lives moves.
