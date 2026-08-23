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

### Notes

- `ScrollPolicy.Step` has no forward-progress guard, and that is deliberate. The stall it would
  protect against is ruled out by the constants: anything under `SettleThresholdPixels` returns the
  target outright, so the smallest movement possible is `SettleThresholdPixels * MinSmoothing` =
  0.025px. A test pins that product above zero so retuning either constant into a stall fails the
  suite.
