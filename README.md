# Bouncing Clock Screensaver

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
![.NET 8](https://img.shields.io/badge/.NET-8-512BD4)
![Platform: Windows](https://img.shields.io/badge/platform-Windows-0078D6)

A Windows screensaver: a large glowing digital clock (with optional date and
logo) that bounces around the screen DVD-logo style, one independent instance
per monitor, each correctly scaled for its own DPI.

Built with C# / .NET 8 WinForms.

<!-- Add a screenshot or short GIF here, e.g.: -->
<!-- ![Screenshot](docs/screenshot.png) -->

## Project layout

| File | Purpose |
|---|---|
| `Program.cs` | Entry point; parses the `/s`, `/c`, `/c:HWND`, `/p HWND` command-line contract and dispatches to the right mode. Also installs a global exception handler so a bug can't leave a crash dialog stuck on an unattended machine. |
| `ScreensaverForm.cs` | The actual bouncing clock. One instance per monitor in full-screen mode; the same class is reparented into a foreign HWND for the live preview thumbnail. |
| `SettingsForm.cs` | The `/c` configuration dialog. |
| `AppSettings.cs` | The settings POCO. |
| `SettingsManager.cs` | Loads/saves settings as JSON under `%APPDATA%\BouncingClockScreensaver\settings.json`. |
| `NativeMethods.cs` | P/Invoke declarations for reparenting into the preview window. |
| `FontUtils.cs` | Heuristic monospace-font detection for the "Show only monospaced fonts" filter. |
| `app.manifest` | OS-compatibility manifest (DPI awareness is instead configured via the `ApplicationHighDpiMode` project property — declaring it in both places is a build warning). |

## How the requirements are met

- **Command-line contract**: `Program.ParseMode` handles `/s`, `/c`, `/c:HWND`,
  `/p HWND`, and no-args-means-`/c`, accepting both `/` and `-` prefixes.
- **Multi-monitor**: `RunScreensaver()` creates one `ScreensaverForm` per
  `Screen.AllScreens` entry, each confined to bouncing within its own
  `screen.Bounds` — they never cross into another monitor's space.
- **Per-monitor DPI**: the project sets `ApplicationHighDpiMode=PerMonitorV2`,
  so each form gets true per-monitor DPI awareness. Font sizes are specified
  in points (not pixels), which GDI+ already renders at physically consistent
  size per monitor DPI — no manual DPI math needed for text. The Graphics
  object used to measure text for bounce-box physics is sourced from the
  window itself (`CreateGraphics()`) rather than a fixed-DPI bitmap, so the
  physics box matches what's actually painted on that monitor.
- **Preview (`/p`)**: `ScreensaverForm` reparents itself into the given HWND
  (`SetParent` + `WS_CHILD`) and renders into a "virtual" canvas the size of
  the primary monitor, scaled down to fit the tiny preview rect — the same
  technique classic Windows screensavers use, so the miniature is a true
  scaled-down simulation rather than a static thumbnail.
- **Settings persistence**: JSON file in `%APPDATA%`, shared by the config
  dialog and the running screensaver.
- **Exit on input**: full-screen mode hides the cursor, records the starting
  cursor position, and exits on any key press, mouse click/wheel, or mouse
  movement past an ~8px threshold (to ignore DPI-scaling jitter).

## Settings

The config dialog (`/c`) is organized into five tabs: **Time**, **Date**,
**Logo**, **Layout**, and **General**.

- **Time**: show/hide, a custom format string (see below), font, size,
  color, bold/italic, and justification (Left / Center / Right).
- **Date**: show/hide, a custom format string (see below), font, size,
  color, bold/italic, and justification (Left / Center / Right).
- **Logo**: show/hide, image file, a size slider (-10..+10, 0 = default —
  each step is an 8% size adjustment off the auto-derived base size, itself
  `0.8x` the time font's point size), and justification (Left / Center /
  Right).
- **Layout**: pick the top-to-bottom stacking order of whichever elements
  are enabled — any order of Time/Date/Logo is allowed (e.g. Date, Logo,
  Time), via a reorderable list with Move Up / Move Down. Combined with each
  element's own justification, this lets you build layouts like "date at
  the top, left-justified; logo in the middle, centered; time at the
  bottom, right-justified."
- **General**: movement speed, background color, monospaced-font filter —
  unchanged.

### Custom Time/Date format strings

Both the Time and Date fields accept a **.NET custom date/time format
string** — the same family of idea as the Win32 `GetTimeFormatEx`/day-
month-year picture formats the settings dialog's "?" help buttons reference,
but implemented with .NET's own formatter rather than a reimplementation of
the Win32 grammar. Tokens are case-sensitive (`dd` ≠ `DD`); anything that
isn't a recognized token (spaces, commas, brackets, dashes, pipes, literal
text) passes through unchanged, so formats and literal text can be freely
mixed, e.g.:

```
ddd, dd MMMM yyyy | [yyyy-MM-dd]   ->   Sun, 16 August 2026 | [2026-08-16]
```

Each format field has a live preview showing today's date/time rendered
with the current format, turning red with "(invalid format)" if the string
doesn't parse — and if an invalid format is ever saved anyway (e.g. a
hand-edited settings file), the screensaver falls back to its built-in
default format rather than crashing.

Any combination of time/date/logo can be shown or hidden independently —
the block's layout (`ScreensaverForm.ComputeLayout`) stacks whichever of
the three are actually enabled, in the order chosen on the Layout tab, so
e.g. logo+date-only or time-only all lay out correctly. To prevent visible
jitter as the clock ticks (or as month/weekday names change length), the
reserved width for each field is computed from the *worst-case* rendering
across a representative spread of values, not just the current instant.

> **Upgrading from an older version**: `ShowSeconds` and `LogoPlacement`
> (Above/Between/Below) have been replaced by custom format strings and the
> Layout tab's element order, respectively. A settings file saved by an
> older version will have those specific values ignored and reset to the
> new defaults (Time/Logo/Date order, `HH:mm:ss` format) the first time you
> open Settings or run the screensaver — this is expected, not an error.

## Open items called out in the spec (intentionally out of scope for v1)

- Background is solid-color only; no background image support.
- No preset/theme system.

## Building

Requires the .NET 8 SDK.

```bash
dotnet build -c Release
```

## Producing the installable `.scr`

Two options, trading install simplicity against file size:

**Self-contained (recommended for distributing to a machine without .NET
installed)** — bundles the whole runtime, ~160MB, no dependencies:

```bash
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish
copy publish\BouncingClockScreensaver.exe publish\BouncingClockScreensaver.scr
```

**Framework-dependent (smaller, ~200KB)** — requires the [.NET 8 Desktop
Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) to already be
installed on the target machine:

```bash
dotnet publish -c Release -o publish-fx
copy publish-fx\BouncingClockScreensaver.exe publish-fx\BouncingClockScreensaver.scr
```

## Installing

Copy the `.scr` file to `C:\Windows\System32` (requires admin), then it
appears in **Settings → Personalization → Lock screen → Screen saver
settings** as "BouncingClockScreensaver". Alternatively, right-click the
`.scr` file and choose **Install**, or **Test** to preview it immediately.

## Testing without installing

Run the exe/`.scr` directly with the mode flags:

```bash
BouncingClockScreensaver.exe /c      # open the settings dialog
BouncingClockScreensaver.exe /s      # run full-screen (Esc / mouse / key to exit)
```

(`/p <hwnd>` is only meaningful when launched by the Windows shell with a real
preview-window handle, so it's not practical to invoke by hand.)

## Contributing

Issues and pull requests are welcome. There's no CI pipeline — before
submitting a change, please confirm `dotnet build -c Release` still succeeds
with 0 warnings/errors and that `/c` and `/s` both still run correctly.

## License

MIT — see [LICENSE](LICENSE). Created by David McCabe.
