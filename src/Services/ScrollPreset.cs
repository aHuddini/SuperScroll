using System;
using System.Collections.Generic;
using System.Linq;

namespace SuperScroll.Services
{
    // A named point in the three-dimensional space the sliders describe.
    //
    // The three settings interact - smoothness decides how it arrives, the other two multiply into
    // how far it goes - so useful combinations are not obvious from the sliders alone. Presets are
    // the shortest route to "show me what this can feel like".
    public class ScrollPreset
    {
        public string Name { get; private set; }
        public string Blurb { get; private set; }
        public double Smoothing { get; private set; }
        public double LinesPerNotch { get; private set; }
        public double LineHeightPixels { get; private set; }

        // Slider steps are 0.05 / 0.5 / 4, so exact equality would be fine - but settings arrive
        // from deserialized JSON too, and a stored double that has been through a round-trip is
        // not guaranteed to compare equal to the literal it came from.
        private const double Tolerance = 0.001;

        private ScrollPreset(string name, string blurb, double smoothing, double lines, double lineHeight)
        {
            Name = name;
            Blurb = blurb;
            Smoothing = smoothing;
            LinesPerNotch = lines;
            LineHeightPixels = lineHeight;
        }

        // The sentinel shown when the sliders sit somewhere no preset describes. Selecting it does
        // nothing - it is a readout, not a destination. Without it the dropdown would keep showing
        // whichever preset was picked last, quietly claiming settings the user has since changed.
        public static readonly ScrollPreset Custom = new ScrollPreset("Custom", "Your own values.", 0, 0, 0);

        public bool IsCustom => ReferenceEquals(this, Custom);

        public static readonly IReadOnlyList<ScrollPreset> All = new List<ScrollPreset>
        {
            // First, because it is the one whose numbers were arrived at by someone actually
            // scrolling a library rather than by reasoning about the curve.
            new ScrollPreset("Huddini Flow", "Long, weighted travel. Six lines at 136px covers a lot of ground per notch, eased just firmly enough not to feel loose.", 0.30, 6, 136),

            new ScrollPreset("Playnite Familiar", "Windows' own three lines, gently eased. Changes the feel without changing distances you already know.", 0.25, 3, 48),
            new ScrollPreset("Glide", "Floats a long way after each notch. Best on a high-refresh display, where the extra frames show.", 0.12, 3, 48),
            new ScrollPreset("Snappy", "Arrives quickly and stops. Closest to Playnite's jump while still being continuous.", 0.45, 4, 48),
            new ScrollPreset("Near Instant", "Barely animated. For when smoothing is not the point and pixel scrolling is.", 0.85, 3, 48),
            new ScrollPreset("Grid Sweep", "Big steps for cover-art grids, where one row is tall and three lines barely moves.", 0.28, 4, 120),
            new ScrollPreset("Dense List", "Short steps for compact text lists. Keeps a notch to roughly one screen line.", 0.35, 3, 24),
        };

        // Everything the dropdown shows, Custom last so it reads as the fallback it is.
        public static IReadOnlyList<ScrollPreset> AllWithCustom
        {
            get { return All.Concat(new[] { Custom }).ToList(); }
        }

        public bool Matches(double smoothing, double lines, double lineHeight)
        {
            if (IsCustom) return false;
            return Math.Abs(Smoothing - smoothing) < Tolerance
                && Math.Abs(LinesPerNotch - lines) < Tolerance
                && Math.Abs(LineHeightPixels - lineHeight) < Tolerance;
        }

        public static ScrollPreset FindMatch(double smoothing, double lines, double lineHeight)
        {
            return All.FirstOrDefault(p => p.Matches(smoothing, lines, lineHeight)) ?? Custom;
        }

        // Distance one notch travels under this preset - the number the two multiplied settings
        // actually produce, surfaced so the dropdown can show it without the reader doing the sum.
        public double PixelsPerNotch
        {
            get { return LinesPerNotch * LineHeightPixels; }
        }

        public override string ToString() { return Name; }
    }
}
