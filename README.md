<p align="center">
  <img src="assets/banner.png" alt="SuperScroll — smooth, pixel-accurate scrolling for Playnite" width="720">
</p>

<p align="center">
  Smooth, pixel-accurate scrolling for <a href="https://playnite.link/">Playnite</a>.
</p>

<h3 align="center">
  🖱️&nbsp;&nbsp;<a href="https://ahuddini.github.io/SuperScroll/assets/bench.html">Feel the difference right now — open the live Tuning Bench</a>&nbsp;&nbsp;🎚️
</h3>

<p align="center">
  ✨ <b>No install, no download.</b> It runs the exact easing SuperScroll uses, in your browser —<br>
  drag the sliders and scroll it against Playnite's row-at-a-time behaviour, side by side. ✨
</p>

<p align="center">
  📥 <a href="#installation">Install</a>
  &nbsp;&middot;&nbsp;
  ⚙️ <a href="#settings">Settings</a>
  &nbsp;&middot;&nbsp;
  📝 <a href="CHANGELOG.md">Changelog</a>
</p>

Playnite's lists scroll by whole rows: one wheel notch jumps one entire item, and nothing in between
is possible. SuperScroll replaces that with frame-synced motion — every notch eases into place, and
fast scrolling accelerates instead of stuttering.

## What's New - v0.1.0

### Added
- **Smooth scrolling** across every list in Playnite, including ones your theme creates.
- **Overscroll bounce.** The top and bottom of a list give a little and spring back, so an end
  feels like an edge rather than a dead stop.
- **Fullscreen navigation smoothing.** Controller and keyboard selection changes ease into place,
  with hold-repeat timings that can go below the floors Windows and Playnite impose.
- **Seven presets** and a [tuning bench](https://ahuddini.github.io/SuperScroll/assets/bench.html)
  that previews your values in a browser before you commit to them.
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
- **Ends that push back.** Scrolling past the top or bottom meets resistance and springs home
  rather than stopping flat. Each further push moves less than the one before it.
- **Fullscreen too.** Fullscreen is driven by a controller, not a wheel — moving the selection is
  what scrolls the list — so it gets its own page of settings for that path.
- **Stays out of the way.** Ctrl/Shift-wheel gestures are left to Playnite, and a list that has hit
  its end hands the wheel back to whatever contains it.

## Settings

Three pages. Everything below is off by default unless the table says otherwise, and the master
switch on the first page governs all of it.

### Mouse Wheel

| Setting | Default | What it does |
|---|---|---|
| Enable smooth scrolling | On | The master switch — it governs the Fullscreen page too. Turn off to hand scrolling back to Playnite. No restart needed. |
| Preset | Huddini Flow | Sets the three values below together. Moving any slider switches this to `Custom`. |
| Smoothness | 0.30 | How much of the remaining distance is covered each frame. Lower glides longer, higher snaps. Shared with Fullscreen. |
| Lines per notch | 6 | How far one wheel notch travels. Windows' own default is 3; SuperScroll ships 6. |
| Line height | 136 px | The pixel height one line stands for. Raise for grid views, lower for dense lists. |
| Bounce at the ends of a list | On | Rubber-bands at the top and bottom. Pushing harder moves it less. Never takes the wheel from a list sitting inside a scrollable page. |

The **Preview these settings in your browser** button opens the
[tuning bench](https://ahuddini.github.io/SuperScroll/assets/bench.html) loaded with your current
values — Playnite's scrolling on the left, yours on the right, same input.

#### Presets

| Preset | Smoothness | Lines | Line height |
|---|---|---|---|
| Huddini Flow *(shipped default)* | 0.30 | 6 | 136 px |
| Playnite Familiar | 0.25 | 3 | 48 px |
| Glide | 0.12 | 3 | 48 px |
| Snappy | 0.45 | 4 | 48 px |
| Near Instant | 0.85 | 3 | 48 px |
| Grid Sweep | 0.28 | 4 | 120 px |
| Dense List | 0.35 | 3 | 24 px |

### Fullscreen

Fullscreen is driven by a controller or the keyboard rather than the wheel, so these smooth a
different path. The wheel still follows the Mouse Wheel page in either mode.

| Setting | Default | What it does |
|---|---|---|
| Smooth controller and keyboard navigation | Off | Eases the scroll that follows a selection change. Only the content slides, so it cannot fight Playnite's "keep the selection visible" behaviour. Uses the Smoothness value from the Mouse Wheel page. |
| Hold debounce | 60 ms | Moves closer together than this land instantly — there is no time to animate between them. Page Up / Page Down always land instantly. Set 0 to animate every move. |
| Use SuperScroll's key repeat instead of Windows' | Off | Holding an arrow key otherwise waits out Windows' repeat delay, which is 500 ms by default and cannot go below 250 ms. This replaces it inside Playnite only, with no floor. |
| &nbsp;&nbsp;↳ Hold delay | 180 ms | How long a key must be held before it repeats. |
| &nbsp;&nbsp;↳ Repeat as fast as the list can keep up | Off | Waits for each move to finish drawing instead of repeating on a fixed clock. Quick with small covers, easing off where tiles are expensive. |
| &nbsp;&nbsp;↳ Interval | 45 ms | Time between repeats. Disabled while the option above is on. |
| Override Playnite's hold repeat timings (controller) | Off | Controller only — Playnite simulates arrow keys from a gamepad, so its own timings govern that input. A physical keyboard uses the Windows typematic rate instead. |
| &nbsp;&nbsp;↳ Hold delay | 300 ms | Playnite's own value is 700 ms; this only ever lowers it. |
| &nbsp;&nbsp;↳ Interval | 60 ms | Playnite's own value is 80 ms. |

### Advanced

| Setting | Default | What it does |
|---|---|---|
| Enable debug logging | Off | Writes `SuperScroll.log` to the plugin data folder. |
| Reset to defaults | — | Restores every value above. |

> The controller override is the one option that reaches into Playnite itself rather than working
> through the interface, so it may stop having an effect after a Playnite update. It changes nothing
> permanently: Playnite's values are read first and put back when you switch it off, when the plugin
> is disabled, and when Playnite closes.

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
