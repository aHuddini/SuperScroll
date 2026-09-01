# Changelog

All notable changes to SuperScroll will be documented in this file.

## [0.1.0] - 2026-08-22

### Added

- **Smooth scrolling for every ScrollViewer in Playnite.** A single class handler registered against
  the `ScrollViewer` type intercepts `PreviewMouseWheel` application-wide. Chosen over a visual-tree
  walk because Playnite rebuilds its view on theme change, on Desktop/Fullscreen switches and on
  re-templating — anything found by walking has to be re-found, and every miss is a list that
  silently keeps the old behaviour. Class handlers cannot be unregistered, so registration happens
  once per process and `Detach()` flips a flag the handler reads.
- **Pixel-unit scrolling via `VirtualizingPanel.ScrollUnit`.** Playnite's lists scroll in whole items
  by default, so `VerticalOffset` counts rows and the smallest possible movement is one entire row —
  no amount of easing produces anything between. Switching the owning `ItemsControl` to
  `ScrollUnit.Pixel` makes the offset a real distance. Deliberately not `CanContentScroll=false`,
  which also yields pixel offsets but disables virtualization and realizes every row in the library.
- **Frame-synced animation.** `ScrollAnimator` runs off `CompositionTarget.Rendering`, one update per
  drawn frame. A `DispatcherTimer` drifts against the compositor — some frames get two updates, some
  none — which reads as micro-stutter that easing cannot hide. `Storyboard` was also ruled out:
  `ScrollViewer.VerticalOffset` is read-only, so animating it needs an attached proxy and a fresh
  Storyboard per wheel event, allocating during the one interaction that must not allocate.
- **Accumulating targets.** A wheel delta is added to the pending *target*, not the current position,
  so a second notch mid-flight extends the journey instead of restarting it. This is what makes fast
  scrolling accelerate rather than stutter.
- **Framerate independence.** `ScaleSmoothing` converts a per-60Hz-frame constant into the actual
  elapsed frame time, so a gesture covers the same ground at 60Hz and 144Hz. Elapsed time is capped
  at 250ms so a GC pause cannot teleport the view.
- Settings for smoothness, lines per notch and line height, with a reset that copies from a pristine
  settings instance rather than restating defaults.
- **Overscroll bounce at the ends of a list.** The arithmetic lives in `ScrollPolicy` with the rest
  of the tuning, so it is asserted rather than eyeballed. `AccumulateOverscroll` resists each
  further push; `DisplacementFor` applies WebKit's 0.55 rubber-band coefficient, so the band
  approaches its limit without ever reaching it and no push is ever refused outright;
  `SpringStep` integrates the release. The spring step is clamped, because a stalled frame
  delivering a large elapsed time would otherwise compound velocity and throw the list off screen.
  `ShouldBounce` decides whether an end was genuinely reached — content that fits in the viewport
  has no end to push against, and a list nested in a scrollable page still hands the wheel up. On
  by default.
- **Fullscreen navigation smoothing.** Fullscreen is driven by a controller rather than a wheel, so
  moving the selection is what scrolls the list. The scroll that follows a selection change is
  eased, leaving the list where Playnite put it and sliding only the content, so it cannot fight
  Playnite's "keep the selection visible" behaviour. A hold debounce lands very fast repeats
  instantly, since animating them leaves the content trailing a selection that has already moved on.
- **Hold repeat timings below the platform floors.** Holding an arrow key waits out the Windows
  typematic delay, which offers only 250/500/750/1000 ms and applies to every application;
  SuperScroll replaces it inside Playnite with no floor. Separately, Playnite's own repeat timings
  govern controller input, because Playnite simulates arrow keys from a gamepad rather than reading
  it directly. Both are off by default, and the controller override reads Playnite's values first
  and restores them when switched off, when the plugin is disabled, and when Playnite closes.
- **Presets.** Seven named combinations of the three wheel values, with the selection derived from
  the current values rather than stored, so moving any slider reports `Custom` without extra
  bookkeeping.
- **Tuning bench.** `OpenTuningBench` writes the embedded `bench.html` out beside the plugin data
  with the current settings in the URL fragment and opens it, so the comparison starts from the
  reader's actual configuration rather than defaults they would have to reproduce by hand. The same
  file is served publicly through GitHub Pages and linked from the README.

### Changed

- **Shipped defaults are now the "Huddini Flow" preset:** lines per notch 3 → 6, line height
  48 → 136 px, smoothness 0.25 → 0.30. Arrived at by scrolling a real library rather than by
  reasoning about the curve. A notch now travels 816 px where it previously travelled 144, so the
  feel is substantially different; `Playnite Familiar` restores the old values.

### Notes

- `ScrollPolicy.Step` has no forward-progress guard, and that is deliberate. The stall it would
  protect against is ruled out by the constants: anything under `SettleThresholdPixels` returns the
  target outright, so the smallest movement possible is `SettleThresholdPixels * MinSmoothing` =
  0.025px. A test pins that product above zero so retuning either constant into a stall fails the
  suite.
