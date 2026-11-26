# Services

Logitech's other software can take over a device's lighting and undo G-Lite's changes. Settings has a Services box (`ServicesBox` in `app/Services.cs`) with a row for each, its status (read every 3 s while shown), and Stop or Start. A last row shows whether Windows Dynamic Lighting is on, with a button to its Windows settings page.

The idea is G-Helper's list of Asus services (GPL: the idea only, no code).

## What G-Lite looks for

Seen on a PC with G HUB and LGS installed (2026-11-23), and Logi Options+ (2026-11-26):

| Row | Services | Programs | Startup |
|---|---|---|---|
| G HUB | `LGHUBUpdaterService` (automatic) | names starting `lghub` or `logitechg_` (`logitechg_discord.exe`, its Discord link) | Run entries mentioning `lghub` |
| Logitech Gaming Software | `LogiRegistryService` (automatic) | names starting `LCore` | "Launch LCore" in the all-users Run key, `LCore.exe /minimized` |
| Logi Options+ | `OptionsPlusUpdaterService` (automatic, shown as "Logi Options+") | names starting `logioptionsplus` (`logioptionsplus.exe`, `_agent`, `_appbroker`, `_updater`) | none: no Run entry or scheduled task, so its service seems to start the agent |
| Logitech LampArray Service | `logi_lamparray_service` (automatic) | | |

The LampArray service hands Logitech devices to Windows Dynamic Lighting. Dynamic Lighting itself is a setting (`HKCU\Software\Microsoft\Lighting`, `AmbientLightingEnabled`), not a service; G-Lite only reads it, since changing the value directly may not take effect until Windows reads it again.

A row is Running when one of its services isn't stopped or one of its programs runs, Stopped when a service or a startup entry is found, otherwise Not installed. Stopped doesn't look at the startup entries: a stopped G HUB whose own start-with-Windows setting is on will still start at sign-in.

## Stop and Start

Stop:

- sets each service to start only when asked (`sc config <name> start= demand`) and stops it
- turns off its startup entries the way Task Manager's Startup apps does: the Run entry stays, and `Explorer\StartupApproved\Run` (`Run32` for 32-bit entries) gets `03 00 00 00` and the time it was turned off (FILETIME, 8 bytes)
- ends its programs

Tried on that PC (2026-11-23): after Stop and a restart, nothing came back. Opening G HUB starts its service again but leaves it on Manual, and LCore runs without its service; after another restart both stayed stopped. So Manual is enough; G-Helper sets Asus services to Disabled because Asus software turns them back on. G HUB keeps a `LGHUB` value of its own (`01`, not Task Manager's 02 or 03) under the user's `StartupApproved\Run` with no Run entry: it seems to add its entry only when its own start-with-Windows setting is on. Logi Options+ (2026-11-26): Stop left its service on Manual and stopped, and ended its programs; not yet seen after a restart. The keys it had diverted on an MX keyboard stay dead until the keyboard reconnects, so G-Lite gives them back after Stop ([mx-key-bindings.md](mx-key-bindings.md)).

Start sets the services back to automatic and starts them, and turns the entries back on (`02` and 11 zero bytes). It doesn't open G HUB or LCore: they start at the next sign-in, or when opened.

So G-Lite keeps nothing: Start puts back what the installers set. A service its owner had set to Manual comes back as Automatic. Without G-Lite, Task Manager (Startup apps) and Services undo it too.

## Administrator

Services and the all-users startup entries need an administrator. G-Lite runs itself elevated (`GLite.exe --services stop|start <ids>`, the Windows prompt), which does them and ends the programs, and returns a bit for each software that failed (in `All`'s order), 0 when everything worked; the G-Lite that started it logs which. The elevated G-Lite doesn't log or read settings itself: an administrator can write next to the exe where the user's G-Lite couldn't, so it could find another `log.txt`. When it partly fails, the user's own startup entries are still turned off and the programs ended. The ids pick from `LogitechSoftware.All`, so the elevated G-Lite can't be pointed at other services. The user's own startup entries are changed by the G-Lite that runs as the user afterwards, since an elevated G-Lite can run as another account. One prompt per click, so stopping three rows takes three. A row whose software has nothing needing an administrator (no service, no all-users entry) asks for nothing.

The demo shows made-up states and changes nothing.
