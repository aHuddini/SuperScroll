using System;

namespace SuperScroll.Services
{
    // The arithmetic behind smooth scrolling, with no ScrollViewer, no dispatcher and no clock.
    //
    // Kept pure on purpose. Scroll feel is tuning work - step sizes, easing rates, how a fling
    // decays - and tuning is guesswork unless the numbers can be asserted directly. Everything
    // here is a static function of its inputs, so the curve can be tested at a hundred frames a
    // second without a window ever opening.
    public static class ScrollPolicy
    {
        // A wheel notch reports 120 units. Windows' own "lines per notch" default is 3.
        public const int WheelDelta = 120;
        public const int DefaultLinesPerNotch = 3;

        // Below this, the remaining distance is invisible and the animator should land and stop
        // rather than spend frames converging on a fraction of a pixel.
        public const double SettleThresholdPixels = 0.5;

        // Smoothing is expressed as the fraction of the remaining distance covered in 16.67ms
        // (one frame at 60Hz). Framerate independence comes from ScaleSmoothing below.
        public const double ReferenceFrameMs = 1000.0 / 60.0;

        public const double MinSmoothing = 0.05;
        public const double MaxSmoothing = 0.95;

        // --- Starvation handling ---
        //
        // CompositionTarget.Rendering fires once per rendered frame ON THE UI THREAD. When
        // something else is busy on that thread - Extra Metadata Loader decoding a video,
        // ImageRotater swapping a background - frames stop arriving every 16ms and start arriving
        // at 40, 80, 200ms, irregularly.
        //
        // Distance is already handled: ScaleSmoothing works from real elapsed time, so a 100ms
        // frame covers the ground six short frames would have. What it cannot fix is WHERE the
        // view is allowed to land, because a position can only be shown on a frame that happens.
        // Irregular frames mean irregularly spaced positions, and that reads as judder however
        // good the easing is.
        //
        // Playnite's own scrolling hides this by needing no frames at all - an instant jump cannot
        // stutter. So the honest goal while starved is not to animate better, it is to animate for
        // less time: finish in three ugly frames rather than fifteen.

        // A frame this long means the UI thread is contended - under 25fps.
        public const double StarvedFrameMs = 40.0;

        // While starved, ease at least this hard regardless of the user's setting.
        public const double StarvedSmoothingFloor = 0.5;

        // Ceiling on how long an animation may spend STARVED before it gives up and lands.
        //
        // Starved time, not total time - a distinction a test forced. At the lowest smoothing
        // setting a perfectly healthy scroll glides for about 2.5 seconds, which is exactly what
        // that setting is for. A ceiling on total elapsed time would have truncated it and made
        // the slowest preset unusable, while a ceiling on starved time only fires when frames are
        // genuinely not arriving.
        public const double MaxStarvedMs = 400.0;

        // How far one wheel notch should move the view, in pixels.
        public static double StepForNotch(double linesPerNotch, double lineHeightPixels)
        {
            if (linesPerNotch <= 0) linesPerNotch = DefaultLinesPerNotch;
            if (lineHeightPixels <= 0) lineHeightPixels = 16.0;
            return linesPerNotch * lineHeightPixels;
        }

        // Wheel delta -> pixels. Fractional deltas matter: precision touchpads and tilt wheels
        // report values well under 120, and rounding them up to a whole notch is what makes
        // touchpad scrolling feel like it is lurching.
        public static double DeltaToPixels(int wheelDelta, double stepPixels)
        {
            return (wheelDelta / (double)WheelDelta) * stepPixels;
        }

        // Where the animation should be heading after this wheel event.
        //
        // The new delta is added to the pending TARGET, not to the current position. That single
        // choice is what makes fast repeated notches accelerate instead of restarting: each notch
        // extends a journey already in progress rather than cancelling it.
        public static double AccumulateTarget(double currentTarget, double deltaPixels, double minOffset, double maxOffset)
        {
            return Clamp(currentTarget - deltaPixels, minOffset, maxOffset);
        }

        // Per-frame easing toward the target. Exponential, so it starts quickly and settles softly.
        //
        // Scaled by real elapsed time rather than assuming a fixed frame length, because WPF's
        // render loop follows the monitor: the same constant would produce a different feel at
        // 144Hz than at 60Hz, and a stutter would produce a different feel again.
        public static double ScaleSmoothing(double smoothingPerFrame, double elapsedMs)
        {
            smoothingPerFrame = Clamp(smoothingPerFrame, MinSmoothing, MaxSmoothing);
            if (elapsedMs <= 0) return smoothingPerFrame;
            if (elapsedMs > 250) elapsedMs = 250; // a stall should not teleport the view

            var frames = elapsedMs / ReferenceFrameMs;
            return 1.0 - Math.Pow(1.0 - smoothingPerFrame, frames);
        }

        // The next offset. Returns the target exactly once the remainder stops being visible, so
        // the animator has a clean stopping condition instead of an asymptote it never reaches.
        public static double Step(double current, double target, double smoothingPerFrame, double elapsedMs)
        {
            var remaining = target - current;
            if (Math.Abs(remaining) < SettleThresholdPixels) return target;

            // No forward-progress guard here, deliberately. The obvious worry is a remainder small
            // enough that eased movement rounds to nothing, stalling the animation while a frame
            // callback keeps firing - but it cannot happen: anything under SettleThresholdPixels
            // returned above, so the smallest movement this line can produce is
            // SettleThresholdPixels * MinSmoothing = 0.025px, which is non-zero. A guard for it
            // would be unreachable code, and unreachable code that looks defensive is worse than
            // none - it implies a hazard that the constants already rule out.
            var factor = ScaleSmoothing(smoothingPerFrame, elapsedMs);
            return current + remaining * factor;
        }

        public static bool HasSettled(double current, double target)
        {
            return Math.Abs(target - current) < SettleThresholdPixels;
        }

        // Raises smoothing while the UI thread is starved, so the animation needs fewer frames to
        // finish and there are fewer irregular positions to see. Never LOWERS the user's setting -
        // somebody who chose 0.8 wants it snappy, and a stall is not a reason to make it glide.
        public static double EffectiveSmoothing(double smoothingPerFrame, double elapsedMs)
        {
            if (elapsedMs < StarvedFrameMs) return smoothingPerFrame;
            return Math.Max(smoothingPerFrame, StarvedSmoothingFloor);
        }

        // True when this frame arrived too late to be part of a smooth animation.
        public static bool IsStarvedFrame(double elapsedMs)
        {
            return elapsedMs >= StarvedFrameMs;
        }

        // The ceiling. Adaptive smoothing shortens a bad stretch; this ends one that is not
        // recovering. Fed accumulated STARVED time, so a deliberately slow glide is never cut off.
        public static bool ShouldForceSettle(double starvedMs)
        {
            return starvedMs >= MaxStarvedMs;
        }

        // Whether this ScrollViewer is worth taking over: it must actually be scrollable, and
        // there must be somewhere to go in the direction asked for. Returning false lets the
        // event bubble so a nested list hands off to its parent exactly as WPF intends.
        public static bool ShouldHandle(bool enabled, double extentHeight, double viewportHeight, double currentOffset, int wheelDelta)
        {
            if (!enabled) return false;
            if (wheelDelta == 0) return false;
            if (viewportHeight <= 0) return false;
            if (extentHeight <= viewportHeight) return false; // nothing to scroll

            var maxOffset = extentHeight - viewportHeight;
            if (wheelDelta > 0 && currentOffset <= 0) return false;             // already at the top
            if (wheelDelta < 0 && currentOffset >= maxOffset) return false;     // already at the bottom

            return true;
        }

        // --- Controller / keyboard navigation ---
        //
        // Fullscreen is not driven by the wheel. A D-pad press or an arrow key moves the SELECTION,
        // and Playnite scrolls to follow by calling ScrollIntoView - a different code path that the
        // wheel handler never sees. Smoothing it means intercepting the bring-into-view request and
        // easing to the offset it asked for, rather than intercepting input.
        //
        // Which creates a problem the wheel does not have: a held direction repeats, and animating
        // every repeat leaves the view chasing a selection that has already moved on.
        //
        // The repeat rates, read out of Playnite's own source rather than assumed - Playnite
        // synthesizes keyboard input from the controller (Playnite/Input/GameController.cs, the
        // keyboardMap sending VK_DOWN and friends), and throttles it itself:
        //
        //   resendDelay = 700ms   the pause before a held direction repeats AT ALL
        //   resendRate  = 80ms    the interval once it starts repeating
        //
        // Both are private readonly, referenced nowhere else, and absent from the SDK - so they
        // are Playnite's to decide and nothing here can change them.
        //
        // That 80ms is why the debounce default is what it is. An earlier 90ms default sat ABOVE
        // the controller repeat rate, so every repeat counted as "held", every held move jumped,
        // and the smoothing setting appeared to do nothing at all while a direction was held.
        // 60ms sits between the two real rates:
        //
        //   controller repeat  80ms  >= 60  ->  animates   (smoothing is visible while holding)
        //   keyboard repeat   ~31ms   <  60  ->  jumps     (genuinely too fast to animate)
        //
        // 80ms is also comfortably longer than an animation needs at ordinary smoothing, so
        // consecutive repeats read as continuous motion rather than as a chase.

        public const double DefaultNavigationDebounceMs = 60.0;
        public const double MinNavigationDebounceMs = 0.0;
        public const double MaxNavigationDebounceMs = 300.0;

        // msSinceLastNavigation is double.MaxValue for the first navigation of a session.
        public static bool ShouldAnimateNavigation(bool enabled, double msSinceLastNavigation, double debounceMs)
        {
            if (!enabled) return false;
            if (debounceMs <= MinNavigationDebounceMs) return true;   // 0 disables the shortcut entirely
            return msSinceLastNavigation >= debounceMs;
        }

        // How far the view must move to bring a rectangle into view, given where that rectangle sits
        // relative to the viewport's top edge.
        //
        // Returns a signed offset DELTA, and zero when the item is already comfortably visible -
        // that zero matters more than it looks. Playnite raises bring-into-view on selection changes
        // that need no scrolling at all, and treating those as movement would drag the list around
        // under a user who only moved sideways.
        public static double BringIntoViewDelta(double itemTop, double itemHeight, double viewportHeight, double edgePadding)
        {
            if (viewportHeight <= 0) return 0;

            // A tall item cannot be framed; align its top and let the rest overflow, which is what
            // every scroll implementation does and what the user expects to see.
            if (itemHeight >= viewportHeight) return itemTop;

            var pad = Clamp(edgePadding, 0, Math.Max(0, (viewportHeight - itemHeight) / 2));

            if (itemTop < pad) return itemTop - pad;                                   // above the top edge
            var itemBottom = itemTop + itemHeight;
            if (itemBottom > viewportHeight - pad) return itemBottom - (viewportHeight - pad); // below the bottom

            return 0; // already framed
        }

        // --- Overscroll bounce ---
        //
        // The rubber band: pushing against the end of a list moves it a little, and pushing harder
        // moves it progressively less. That resistance is the whole effect - a linear overscroll
        // that simply stops at a cap reads as a bug, because the list appears to break free and
        // then jam. Diminishing returns reads as elastic.

        public const double MaxOverscrollPixels = 48.0;

        // What ONE wheel notch contributes to the RAW push, before the curve reduces it.
        //
        // Fixed, not derived from the user's scroll step. Deriving it was wrong: at the shipped
        // preset a notch is worth 816px, so even a small fraction of it saturated the whole band on
        // the very first notch and the bounce appeared as an instant jump to its limit. The bounce
        // is a tactile constant - how hard the end of a list pushes back should not change because
        // someone prefers longer scrolls.
        // Scaled with MaxOverscrollPixels rather than tuned independently. The curve's shape is
        // set by the ratio between the two, so changing only the limit would alter how much of the
        // band one notch consumes and quietly change the feel as well as the distance.
        public const double OverscrollPixelsPerNotch = 31.0;



        // How long the band stays stretched after the last push before springing back. Long enough
        // to outlast the gap between wheel notches during a continuous scroll (which is well under
        // 100ms), short enough that the release still feels like a direct response to stopping.
        public const double OverscrollHoldMs = 120.0;

        // How long ExtentHeight must hold still before the bottom of a list is treated as a real
        // boundary. A virtualized panel refines its extent as tiles realize, so the bottom bound
        // moves during exactly the approach that would otherwise trigger a bounce against it.
        public const double ExtentSettleMs = 100.0;

        // The rubber band, in two parts: what has been pushed, and how far that shows.
        //
        // Separated because the first is unbounded and the second is not. The earlier version
        // clamped the displacement itself, which made the limit a WALL - once the band reached
        // 72px, further pushing produced exactly zero movement and it read as a hard stop rather
        // than as resistance. macOS never stops; it just gives progressively less.
        //
        // AccumulateOverscroll therefore keeps an uncapped running total of what has been pushed,
        // and DisplacementFor maps that through an asymptotic curve which approaches the limit
        // without ever arriving. Every push still moves something - eventually by a fraction of a
        // pixel, which is exactly how a real band behaves.

        public static double AccumulateOverscroll(double currentRaw, double deltaPixels)
        {
            var next = currentRaw + deltaPixels;

            // Pushing the other way releases without resistance, and stops at rest rather than
            // continuing into the opposite band.
            if (Math.Sign(next) != Math.Sign(currentRaw) && Math.Abs(currentRaw) > 0.001) return 0;
            return next;
        }

        // d = max * (1 - 1 / (|raw| / max + 1)), signed.
        //
        // raw = max      -> 0.50 * max
        // raw = 3 * max  -> 0.75 * max
        // raw = infinity -> max, approached but never reached
        // WebKit's rubber-band coefficient. Its formula is
        //     b(x, d, c) = (1 - 1 / ((x * c / d) + 1)) * d
        // and 0.55 is the value Safari and UIScrollView use. Lower means stiffer - the same push
        // shows less - so this is the single number that decides how taut the band feels, separate
        // from how far it can ever travel.
        public const double RubberBandCoefficient = 0.55;

        public static double DisplacementFor(double raw, double maxPixels)
        {
            if (maxPixels <= 0) return 0;

            var magnitude = Math.Abs(raw);
            if (magnitude < 0.001) return 0;

            var eased = maxPixels * (1.0 - (1.0 / (((magnitude * RubberBandCoefficient) / maxPixels) + 1.0)));
            return eased * Math.Sign(raw);
        }

        // --- Release: a critically damped spring ---
        //
        // Not exponential decay, which is what this used before and what most "smooth scroll"
        // implementations reach for. The two look similar plotted and feel different in the hand:
        //
        //   Exponential  - fastest at the instant of release, then a long trailing tail. Reads as
        //                  the band being let go of.
        //   Spring       - starts at rest, accelerates, then settles. Reads as the band PULLING
        //                  the content back, which is what a stretched band actually does.
        //
        // Critically damped specifically (damping = 2*sqrt(k*m), m = 1): the fastest return that
        // never overshoots. Underdamped would wobble past zero, which no rubber band does; over-
        // damped would crawl.
        // Chosen by simulating the settle time rather than by feel: 650 returns from a full
        // stretch in ~370ms, which is the range macOS bounce-back sits in. 120 took 733ms and read
        // as sluggish; much above 800 and the release stops being a motion and becomes a snap.
        public const double SpringStiffness = 650.0;

        // Below these, the spring has arrived as far as anyone can see.
        public const double SpringRestPixels = 0.3;
        public const double SpringRestVelocity = 4.0;

        // Semi-implicit Euler: velocity updated from acceleration first, then position from the NEW
        // velocity. Explicit Euler (position first) injects energy at large timesteps and can make a
        // damped spring gain amplitude instead of losing it - a wobble that grows rather than
        // settles, on exactly the dropped frames where it is most visible.
        public static void SpringStep(ref double position, ref double velocity, double elapsedMs)
        {
            if (elapsedMs <= 0) return;
            if (elapsedMs > 64) elapsedMs = 64;   // a stall must not integrate one huge step

            var dt = elapsedMs / 1000.0;
            var damping = 2.0 * Math.Sqrt(SpringStiffness);

            var acceleration = (-SpringStiffness * position) - (damping * velocity);

            velocity += acceleration * dt;
            position += velocity * dt;
        }

        public static bool SpringAtRest(double position, double velocity)
        {
            return Math.Abs(position) < SpringRestPixels && Math.Abs(velocity) < SpringRestVelocity;
        }

        // Whether a wheel event at the end of a list should bounce rather than be passed upward.
        // Only when the list can scroll at all - content that fits its viewport has no end to push
        // against, and bouncing it would be movement where the user expects none.
        public static bool ShouldBounce(bool enabled, double extentHeight, double viewportHeight, double currentOffset, int wheelDelta)
        {
            if (!enabled) return false;
            if (wheelDelta == 0) return false;
            if (viewportHeight <= 0) return false;
            if (extentHeight <= viewportHeight) return false;

            var maxOffset = extentHeight - viewportHeight;
            if (wheelDelta > 0 && currentOffset <= 0.5) return true;              // pushing past the top
            if (wheelDelta < 0 && currentOffset >= maxOffset - 0.5) return true;  // pushing past the bottom

            return false;
        }

        public static double Clamp(double value, double min, double max)
        {
            if (max < min) return min;
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }
    }
}
