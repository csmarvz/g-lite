# Contributing

Thanks for helping. Most help doesn't need any code.

## Without code

- **Your device isn't supported, or says Untested.** Open it in G-Lite, click **Report**, then **Open GitHub issue**. The report only reads from the device. It leaves out serial numbers and paths.
- **Something went wrong.** Open an issue with what you did, what you expected and what happened. Add the lines from **Logs** around that time.
- **A device marked Untested works for you.** Say so in an issue, with a report. It's how a device loses Untested.

## With code

You need Windows. Nothing else to install: the build uses the C# compiler that ships with Windows (.NET Framework 4.8).

```
app\build.cmd
app\build\GLite.exe --demo
```

`build.cmd` runs the self-test first and stops if it fails, then builds `app\build\GLite.exe`. It also closes any running G-Lite and copies the new exe to `%LOCALAPPDATA%\GLite`. `--demo` runs with pretend devices, so you can try every screen without the hardware.

### Rules

- C# 5 only: no `$""` strings, no `?.`, no `=>` members, no `nameof`. No NuGet packages or other dependencies.
- Keep the exe small and G-Lite light when idle. Prefer the simplest thing that works.
- Use full, readable names, so the code says what it does. Comments are for what it can't say: why it's done this way, a decision and what it ruled out, or where a fact comes from (a report, a doc, another project). Keep them short, and change or remove them when the code changes.
- The repo is MIT. Don't copy code from GPL projects. Facts learned from them, like which bytes do what, are fine.
- Never write to a device without a backup first. Never write a factory profile.

### Tests

The self-test checks encodings against bytes from real devices, and replays traces: scripted clicks on a screen with a pretend device, compared with `app\tests\<device>.txt`. If your change is meant to change what's sent, run `build\selftest.exe --record` from `app`, check the diff of `app\tests` and commit it with your change. Add a check or a trace for anything new.

### Adding a device

Read [docs/adding-a-device.md](docs/adding-a-device.md) first. Start from a report of the real device, put what you learn in `docs/`, and build on a pretend device made from the report's bytes.

### Pull requests

- One feature or fix per pull request.
- Add a line under **Unreleased** in [CHANGELOG.md](CHANGELOG.md), and update the device table in the README if support changed.
- Say what you tested, and on which device.
