# Branding

## The mark

Three chevrons, descending. Widths taper and the gaps between apexes shrink — 50 design units, then
34, a ratio of about 0.68. That is the same shape as the easing in `ScrollPolicy`: each frame covers
a fraction of the distance still left, so the steps get smaller as the scroll settles. The mark is
the algorithm, not a decoration of it.

The bottom chevron leads (motion travels down), so it carries full opacity and the brightest mint.
The two above it are the trail, fading back into deep green. Flipping that ramp reads as upward
motion.

## Palette

Every colour is lifted from the settings page's own palette in
[`src/Controls/SettingsResources.xaml`](../src/Controls/SettingsResources.xaml), so the icon, the
banner and the settings pane read as one thing rather than three. Nothing here is invented.

| Role | Hex | Settings resource | Where |
|---|---|---|---|
| Field top | `#1A212B` | `SsSurface` | icon plate, banner background |
| Field bottom | `#12161C` | `SsGround` | icon plate, banner background |
| Motion trail | `#1F6E5C` | `SsTrackOn` stop 0 | top of the chevron gradient |
| Motion lead | `#69DCBB` | `SsTrackOn` stop 1 | bottom of the chevron gradient |
| Text primary | `#E6EBF2` | `SsText` | wordmark |
| Text muted | `#94A3B4` | `SsTextMuted` | tagline |

The chevron gradient is the "on" track of the settings toggle, unchanged. That control already had
to express a trail running toward a bright leading edge, which is the same thing the mark says.
`SsAccent` (`#5BD1B0`) sits inside that ramp and is the single-colour stand-in when a flat green is
needed.

Chevron opacity ramps 65% -> 82% -> 100%, top to bottom.

## Assets

| File | Size | Use |
|---|---|---|
| `assets/superscroll-mark.svg` | vector | master source. No text, so it is safe to scale or recolor. |
| `icon.png` | 256×256 | shipped with the extension. `scripts/package_extension.ps1` copies it into the `.pext`. |
| `assets/banner.png` | 1200×320 | README header. |
| `assets/addon-header.png` | 1280×720 | Playnite addon listing, release pages. |

## Regenerating

```bash
powershell -ExecutionPolicy Bypass -File scripts/render_branding.ps1
```

Renders all three PNGs with `System.Drawing` — no ImageMagick, Inkscape, or npm. The geometry is
duplicated between the script's `$CHEVRONS` table and the SVG; change one and change the other.

Wordmarks are baked into the PNGs rather than set as SVG `<text>`. GitHub renders README SVGs inside
a sandboxed `<img>`, where the font falls back to whatever the viewer happens to have installed, so
a text-bearing SVG banner would shift shape per machine.

## Usage

- Do not stretch the mark; it is square.
- Below roughly 32 px, drop the plate and use the chevrons alone — the corner radius muddies.
- On a light background use the plated version. The chevrons alone are tuned for dark fields.
