# SuperScroll Developer Guide

Playnite extension for smooth scrolling. C# / .NET 4.6.2 / WPF.

## Build

```bash
dotnet clean -c Release
dotnet build -c Release
powershell -ExecutionPolicy Bypass -File scripts/package_extension.ps1
```

Always run all three in order. Version comes from `version.txt`; the packaging script stamps it into
`extension.yaml` and `AssemblyInfo.cs`, so those are outputs, not inputs.

## Architecture

Entry point: `src/SuperScroll.cs` (`GenericPlugin`). Three pieces do the work:

- **`ScrollPolicy`** — all the arithmetic, pure and static. No ScrollViewer, no dispatcher, no clock.
  Every tuning decision lives here so it can be asserted in a test rather than eyeballed.
- **`ScrollAnimator`** — one per ScrollViewer, driven by `CompositionTarget.Rendering`. Holds the
  target offset and eases toward it.
- **`ScrollEnhancer`** — a single `ScrollViewer` class handler that intercepts the wheel for every
  list in the application, plus the switch to pixel-based scrolling.

Two loggers, the UniPlaySong split: Playnite's `ILogger` → `extension.log` for things a user would
report; `FileLogger` → `SuperScroll.log`, gated behind Enable Debug Logging, for the high-frequency
detail. Scroll input fires dozens of times a second, so that gate is load-bearing.

## The two decisions that matter

**Class handler, not a visual-tree walk.** Playnite rebuilds its view on theme change, on
Desktop/Fullscreen switches and on re-templating. Anything found by walking has to be re-found, and
every miss is a list that silently scrolls the old way. `EventManager.RegisterClassHandler` binds to
the *type*, so it covers instances created later by themes this plugin has never seen.
Caveat, by design: class handlers cannot be unregistered. Registration is once per process and
`Detach()` flips a flag the handler reads.

**`ScrollUnit.Pixel`, not `CanContentScroll=false`.** Both give pixel offsets. Only the first keeps
virtualization. Turning virtualization off realizes every row in the library, which on a large
collection is far worse than the jerky scrolling it set out to fix.

## Conventions

- PascalCase public, `_camelCase` private fields, `I`-prefix interfaces
- Single-line `//` comments. XML docs only for public APIs needing `<param>`/`<returns>`
- Constants in `src/Common/Constants.cs`, `#region`-grouped
- Settings defaults live on the backing fields in `SuperScrollSettings.cs` and nowhere else; reset
  copies from a pristine instance
- Settings pages use **explicit** styles. A Playnite theme's implicit styles apply to anything that
  leaves a property unset, which is how a settings page ends up cropped on a theme nobody tested

## Verification

Build + test + package after every change. Never claim done without verified output.
