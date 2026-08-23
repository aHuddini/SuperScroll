# SuperScroll

Smooth, pixel-accurate scrolling for [Playnite](https://playnite.link/).

Playnite's lists scroll by whole rows: one wheel notch jumps one entire item, and nothing in between
is possible. SuperScroll replaces that with frame-synced motion — every notch eases into place, and
fast scrolling accelerates instead of stuttering.

## What's New - v0.1.0

### Added
- **Smooth scrolling** across every list in Playnite, including ones your theme creates.
- **Tuning controls** — smoothness, distance per notch, and line height.
- Works with large libraries: list virtualization stays on.

## Features

- **Applies everywhere.** Game lists, grids, sidebars, settings panes, and any list a theme adds
  later. Nothing to configure per view.
- **Keeps virtualization.** A common way to get pixel scrolling in WPF is to switch content
  scrolling off, which forces every row in the library to be built. SuperScroll changes the scroll
  *unit* instead, so a 10,000-game library scrolls as smoothly as a 10-game one.
- **Framerate independent.** The same flick travels the same distance on a 60Hz laptop panel and a
  144Hz monitor.
- **Stays out of the way.** Ctrl/Shift-wheel gestures are left to Playnite, and a list that has hit
  its end hands the wheel back to whatever contains it.

## Settings

| Setting | Default | What it does |
|---|---|---|
| Enable smooth scrolling | On | Turn off to hand the wheel back to Playnite. No restart needed. |
| Smoothness | 0.25 | Fraction of the remaining distance covered each frame. Lower glides longer, higher snaps. |
| Lines per notch | 3 | How far one wheel notch travels. Matches the Windows default. |
| Line height | 48 px | The pixel height one line stands for. Raise for grid views, lower for dense lists. |
| Enable debug logging | Off | Writes `SuperScroll.log` to the plugin data folder. |

## Requirements

- Playnite 10 (SDK 6.16+)
- Windows with .NET Framework 4.6.2

## Installation

Download the `.pext` from Releases and open it with Playnite.

## Building from source

```bash
dotnet clean -c Release
dotnet build -c Release
powershell -ExecutionPolicy Bypass -File scripts/package_extension.ps1
```

## License

MIT — see [LICENSE](LICENSE).
