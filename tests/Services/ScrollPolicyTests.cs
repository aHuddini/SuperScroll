using System;
using NUnit.Framework;
using SuperScroll.Common;
using SuperScroll.Services;

namespace SuperScroll.Tests.Services
{
    // The scroll curve, asserted directly. Feel is subjective; these are the properties that have
    // to hold for it to feel like anything at all.
    [TestFixture]
    public class ScrollPolicyTests
    {
        private const double Frame60 = 1000.0 / 60.0;

        // --- ShouldHandle: when to take the wheel, and when to give it back ---

        [Test]
        public void ShouldHandle_ScrollableContentMidway_TakesTheWheel()
        {
            Assert.IsTrue(ScrollPolicy.ShouldHandle(true, extentHeight: 5000, viewportHeight: 800, currentOffset: 400, wheelDelta: -120));
        }

        [Test]
        public void ShouldHandle_Disabled_Declines()
        {
            Assert.IsFalse(ScrollPolicy.ShouldHandle(false, 5000, 800, 400, -120));
        }

        [Test]
        public void ShouldHandle_ContentFitsViewport_Declines()
        {
            // Nothing to scroll. Handling it would swallow the event from a scrollable parent.
            Assert.IsFalse(ScrollPolicy.ShouldHandle(true, extentHeight: 500, viewportHeight: 800, currentOffset: 0, wheelDelta: -120));
        }

        [Test]
        public void ShouldHandle_AtTopScrollingUp_DeclinesSoItBubbles()
        {
            Assert.IsFalse(ScrollPolicy.ShouldHandle(true, 5000, 800, currentOffset: 0, wheelDelta: 120));
        }

        [Test]
        public void ShouldHandle_AtBottomScrollingDown_DeclinesSoItBubbles()
        {
            Assert.IsFalse(ScrollPolicy.ShouldHandle(true, 5000, 800, currentOffset: 4200, wheelDelta: -120));
        }

        [Test]
        public void ShouldHandle_AtBottomScrollingUp_StillTakesIt()
        {
            Assert.IsTrue(ScrollPolicy.ShouldHandle(true, 5000, 800, currentOffset: 4200, wheelDelta: 120));
        }

        // --- Delta conversion ---

        [Test]
        public void DeltaToPixels_OneNotch_IsOneStep()
        {
            Assert.AreEqual(-144.0, ScrollPolicy.DeltaToPixels(-ScrollPolicy.WheelDelta, 144.0), 1e-9);
        }

        [Test]
        public void DeltaToPixels_PartialDelta_ScalesProportionally()
        {
            // Precision touchpads report fractions of a notch. Rounding these to a whole notch is
            // exactly what makes touchpad scrolling lurch.
            Assert.AreEqual(-36.0, ScrollPolicy.DeltaToPixels(-30, 144.0), 1e-9);
        }

        [Test]
        public void StepForNotch_ZeroOrNegativeInputs_FallBackRatherThanCollapse()
        {
            Assert.Greater(ScrollPolicy.StepForNotch(0, 48), 0);
            Assert.Greater(ScrollPolicy.StepForNotch(3, 0), 0);
        }

        // --- Target accumulation: the property that makes fast scrolling accelerate ---

        [Test]
        public void AccumulateTarget_SecondNotchExtendsTheFirst()
        {
            var t1 = ScrollPolicy.AccumulateTarget(0, -144, 0, 4000);
            var t2 = ScrollPolicy.AccumulateTarget(t1, -144, 0, 4000);

            Assert.AreEqual(144, t1, 1e-9);
            Assert.AreEqual(288, t2, 1e-9, "a second notch mid-flight must extend the journey, not restart it");
        }

        [Test]
        public void AccumulateTarget_ClampsToBounds()
        {
            Assert.AreEqual(4000, ScrollPolicy.AccumulateTarget(3900, -1000, 0, 4000), 1e-9);
            Assert.AreEqual(0, ScrollPolicy.AccumulateTarget(50, 1000, 0, 4000), 1e-9);
        }

        // --- Easing ---

        [Test]
        public void Step_MovesTowardTargetWithoutOvershooting()
        {
            var next = ScrollPolicy.Step(current: 0, target: 1000, smoothingPerFrame: 0.25, elapsedMs: Frame60);

            Assert.Greater(next, 0);
            Assert.Less(next, 1000, "easing must never overshoot the target");
            Assert.AreEqual(250, next, 1.0, "one 60Hz frame at 0.25 should cover a quarter of the distance");
        }

        [Test]
        public void Step_ConvergesAndTerminates()
        {
            double current = 0;
            const double target = 1000;

            for (var i = 0; i < 600; i++)
            {
                current = ScrollPolicy.Step(current, target, 0.25, Frame60);
                if (ScrollPolicy.HasSettled(current, target)) break;
            }

            Assert.IsTrue(ScrollPolicy.HasSettled(current, target), "the animation must actually finish, not approach forever");
        }

        [Test]
        public void Step_WithinSettleThreshold_LandsExactly()
        {
            Assert.AreEqual(1000.0, ScrollPolicy.Step(999.8, 1000, 0.25, Frame60), 1e-9);
        }

        [Test]
        public void Step_WorstCaseMovementIsStillNonZero()
        {
            // Why Step needs no forward-progress guard. The stall everyone reaches for a guard
            // against - eased movement rounding to nothing while frame callbacks keep firing -
            // is ruled out by the constants: anything under SettleThresholdPixels returns the
            // target outright, so the smallest movement possible is that threshold times
            // MinSmoothing. This pins that product above zero, so if either constant is ever
            // retuned into a stall the suite says so instead of the scrolling quietly sticking.
            var justAboveThreshold = 1000 - (ScrollPolicy.SettleThresholdPixels + 1e-6);
            var next = ScrollPolicy.Step(justAboveThreshold, 1000, ScrollPolicy.MinSmoothing, Frame60);

            Assert.Greater(next - justAboveThreshold, 0.0, "the slowest possible frame must still make progress");
            Assert.Less(next, 1000);
        }

        // --- Framerate independence: the same gesture must feel the same on any monitor ---

        [Test]
        public void ScaleSmoothing_AtReferenceRate_IsUnchanged()
        {
            Assert.AreEqual(0.25, ScrollPolicy.ScaleSmoothing(0.25, Frame60), 1e-6);
        }

        [Test]
        public void ScaleSmoothing_ShorterFrame_MovesLessPerFrame()
        {
            var at144 = ScrollPolicy.ScaleSmoothing(0.25, 1000.0 / 144.0);
            Assert.Less(at144, 0.25);
            Assert.Greater(at144, 0);
        }

        [Test]
        public void SameElapsedTime_ConvergesEquallyAtAnyFramerate()
        {
            // The real guarantee: 100ms of scrolling covers the same ground at 60Hz and 144Hz.
            double a = 0, b = 0;
            const double target = 1000;

            for (var i = 0; i < 6; i++) a = ScrollPolicy.Step(a, target, 0.25, Frame60);              // ~100ms
            for (var i = 0; i < 14; i++) b = ScrollPolicy.Step(b, target, 0.25, 1000.0 / 144.0);      // ~97ms

            Assert.AreEqual(a, b, 25.0, "framerate must not change how far a given gesture travels");
        }

        [Test]
        public void ScaleSmoothing_LongStall_IsCappedRatherThanTeleporting()
        {
            // A GC pause or a dropped frame should not snap the view to its destination.
            var factor = ScrollPolicy.ScaleSmoothing(0.25, 5000);
            Assert.Less(factor, 1.0);
        }

        [Test]
        public void ScaleSmoothing_ClampsOutOfRangeInput()
        {
            Assert.LessOrEqual(ScrollPolicy.ScaleSmoothing(5.0, Frame60), 1.0);
            Assert.Greater(ScrollPolicy.ScaleSmoothing(-1.0, Frame60), 0.0);
        }

        [Test]
        public void Clamp_HandlesInvertedBounds()
        {
            Assert.AreEqual(10, ScrollPolicy.Clamp(500, 10, 5), 1e-9);
        }

        // --- UI-thread starvation (EML video, ImageRotater background swaps) ---
        //
        // Rendering fires on the UI thread, so when something else is busy there the frames the
        // animation lands on become long and irregular. Distance is already corrected for elapsed
        // time; what these cover is spending FEWER frames while starved, because every frame during
        // a stall is a visibly misplaced position.

        [Test]
        public void EffectiveSmoothing_NormalFrame_LeavesTheUserSettingAlone()
        {
            Assert.AreEqual(0.25, ScrollPolicy.EffectiveSmoothing(0.25, Frame60), 1e-9);
        }

        [Test]
        public void EffectiveSmoothing_StarvedFrame_EasesHarder()
        {
            var starved = ScrollPolicy.EffectiveSmoothing(0.25, 120);
            Assert.Greater(starved, 0.25, "a starved frame should finish sooner, not glide longer");
            Assert.AreEqual(ScrollPolicy.StarvedSmoothingFloor, starved, 1e-9);
        }

        [Test]
        public void EffectiveSmoothing_NeverLowersASnappySetting()
        {
            // Someone who chose 0.8 wants it snappy; a stall is not a reason to slow them down.
            Assert.AreEqual(0.8, ScrollPolicy.EffectiveSmoothing(0.8, 200), 1e-9);
        }

        [Test]
        public void StarvedAnimation_FinishesInFewerFramesThanNormal()
        {
            // The property that matters: fewer frames on screen during a stall means fewer
            // irregularly spaced positions to perceive as judder.
            Assert.Less(FramesToSettle(0.25, 120), FramesToSettle(0.25, Frame60));
        }

        private static int FramesToSettle(double smoothing, double frameMs)
        {
            double cur = 0;
            const double target = 1000;
            var frames = 0;

            while (frames < 2000 && !ScrollPolicy.HasSettled(cur, target))
            {
                cur = ScrollPolicy.Step(cur, target, ScrollPolicy.EffectiveSmoothing(smoothing, frameMs), frameMs);
                frames++;
            }
            return frames;
        }

        // --- Controller / keyboard navigation ---

        [Test]
        public void ShouldAnimateNavigation_IsolatedPress_Animates()
        {
            Assert.IsTrue(ScrollPolicy.ShouldAnimateNavigation(true, 500, 90));
        }

        [Test]
        public void ShouldAnimateNavigation_HeldDirection_LandsInstantly()
        {
            Assert.IsFalse(ScrollPolicy.ShouldAnimateNavigation(true, 20, 60));
        }

        // Playnite's real repeat rates, read from Playnite/Input/GameController.cs:
        //   resendDelay = 700ms (pause before a held direction repeats at all)
        //   resendRate  =  80ms (interval once repeating)
        // Windows keyboard repeat is far faster, ~31ms at its default rate.
        private const double PlayniteControllerRepeatMs = 80;
        private const double WindowsKeyboardRepeatMs = 31;

        [Test]
        public void DefaultDebounce_LetsAHeldControllerActuallyAnimate()
        {
            // The regression this pins. A 90ms default sat ABOVE Playnite's 80ms controller repeat,
            // so every repeat counted as "held", every held move jumped, and the smoothing setting
            // did nothing visible while a direction was held - which reads as the plugin ignoring
            // its own settings.
            Assert.IsTrue(
                ScrollPolicy.ShouldAnimateNavigation(true, PlayniteControllerRepeatMs, ScrollPolicy.DefaultNavigationDebounceMs),
                "a held controller repeats every 80ms and must still animate at the default debounce");
        }

        [Test]
        public void DefaultDebounce_StillCatchesKeyboardRepeat()
        {
            // The other side of the same boundary: keyboard repeat really is too fast to animate.
            Assert.IsFalse(
                ScrollPolicy.ShouldAnimateNavigation(true, WindowsKeyboardRepeatMs, ScrollPolicy.DefaultNavigationDebounceMs),
                "keyboard repeat at ~31ms should land instantly rather than chase");
        }

        [Test]
        public void PatchedRepeatRate_StillAnimatesAtTheDefaultDebounce()
        {
            // The two features have to compose. Overriding Playnite's repeat makes held navigation
            // FASTER, which pushes it toward the debounce boundary - and a patched rate below the
            // debounce would silently stop held navigation animating, reintroducing the exact
            // complaint the debounce default was retuned to fix.
            Assert.IsTrue(
                ScrollPolicy.ShouldAnimateNavigation(true, Constants.DefaultRepeatRateMs, ScrollPolicy.DefaultNavigationDebounceMs),
                "the shipped repeat-rate default must not fall below the shipped debounce default");
        }

        [Test]
        public void RepeatPatchNeverRaisesPlayniteDelayAboveStock()
        {
            // This feature exists to reduce a delay. A maximum above Playnite's own value would let
            // it make navigation worse than not installing the plugin at all.
            Assert.LessOrEqual(Constants.MaxRepeatDelayMs, Constants.PlayniteStockRepeatDelayMs);
        }

        [Test]
        public void DefaultDebounce_SitsBetweenTheTwoRealRepeatRates()
        {
            // States the invariant outright, so retuning the default without checking it against
            // both input paths fails here rather than in someone's living room.
            Assert.Greater(ScrollPolicy.DefaultNavigationDebounceMs, WindowsKeyboardRepeatMs);
            Assert.Less(ScrollPolicy.DefaultNavigationDebounceMs, PlayniteControllerRepeatMs);
        }

        [Test]
        public void ShouldAnimateNavigation_FirstMoveOfTheSession_Animates()
        {
            Assert.IsTrue(ScrollPolicy.ShouldAnimateNavigation(true, double.MaxValue, 90));
        }

        [Test]
        public void ShouldAnimateNavigation_ZeroDebounce_AlwaysAnimates()
        {
            Assert.IsTrue(ScrollPolicy.ShouldAnimateNavigation(true, 1, 0));
        }

        [Test]
        public void ShouldAnimateNavigation_Disabled_NeverAnimates()
        {
            Assert.IsFalse(ScrollPolicy.ShouldAnimateNavigation(false, 5000, 90));
        }

        // --- Bring-into-view geometry ---

        [Test]
        public void BringIntoViewDelta_AlreadyFramed_DoesNotMove()
        {
            // The important zero. Playnite raises bring-into-view on selection changes that need no
            // scrolling; treating those as movement would drag the list under a sideways move.
            Assert.AreEqual(0, ScrollPolicy.BringIntoViewDelta(200, 40, 800, 24), 1e-9);
        }

        [Test]
        public void BringIntoViewDelta_AboveViewport_ScrollsUpToThePadding()
        {
            // Item 10px from the top with 24px padding wanted: move up 14px.
            Assert.AreEqual(-14, ScrollPolicy.BringIntoViewDelta(10, 40, 800, 24), 1e-9);
        }

        [Test]
        public void BringIntoViewDelta_BelowViewport_ScrollsDownToThePadding()
        {
            // Bottom at 820 in an 800 viewport with 24 padding: move down to clear 776.
            Assert.AreEqual(44, ScrollPolicy.BringIntoViewDelta(780, 40, 800, 24), 1e-9);
        }

        [Test]
        public void BringIntoViewDelta_ItemTallerThanViewport_AlignsItsTop()
        {
            // Cannot be framed, so do what every scroller does: align the top and let it overflow.
            Assert.AreEqual(300, ScrollPolicy.BringIntoViewDelta(300, 900, 800, 24), 1e-9);
        }

        [Test]
        public void BringIntoViewDelta_PaddingLargerThanTheGap_IsClampedNotInverted()
        {
            // An edge padding wider than the spare room would otherwise demand a scroll in both
            // directions at once, and the item would oscillate between two "corrected" positions.
            var delta = ScrollPolicy.BringIntoViewDelta(itemTop: 30, itemHeight: 90, viewportHeight: 100, edgePadding: 500);
            Assert.AreEqual(25, delta, 1e-9);
        }

        [Test]
        public void BringIntoViewDelta_NoViewport_DoesNothing()
        {
            Assert.AreEqual(0, ScrollPolicy.BringIntoViewDelta(100, 40, 0, 24), 1e-9);
        }

        [Test]
        public void IsStarvedFrame_SeparatesHealthyFramesFromLateOnes()
        {
            Assert.IsFalse(ScrollPolicy.IsStarvedFrame(Frame60));
            Assert.IsFalse(ScrollPolicy.IsStarvedFrame(1000.0 / 30));   // 33ms, still fine
            Assert.IsTrue(ScrollPolicy.IsStarvedFrame(120));            // EML decoding a video
        }

        [Test]
        public void ShouldForceSettle_OnlyAfterSustainedStarvation()
        {
            Assert.IsFalse(ScrollPolicy.ShouldForceSettle(0));
            Assert.IsFalse(ScrollPolicy.ShouldForceSettle(ScrollPolicy.MaxStarvedMs - 1));
            Assert.IsTrue(ScrollPolicy.ShouldForceSettle(ScrollPolicy.MaxStarvedMs));
        }

        [Test]
        public void TheCeilingCountsStarvedTimeOnly_SoASlowGlideIsNeverCutShort()
        {
            // The reason this ceiling is fed starved time rather than total elapsed time. At the
            // lowest smoothing a perfectly healthy scroll glides for seconds - which is what that
            // setting is FOR - so a total-time ceiling would truncate it and make the slowest
            // preset unusable. Only frames that actually arrived late may count toward giving up.
            var healthyGlideMs = FramesToSettle(ScrollPolicy.MinSmoothing, Frame60) * Frame60;

            Assert.Greater(healthyGlideMs, ScrollPolicy.MaxStarvedMs,
                "a healthy slow glide outlasts the ceiling, which is exactly why total time must not be the trigger");
            Assert.IsFalse(ScrollPolicy.ShouldForceSettle(0),
                "and with no late frames, nothing accumulates toward it");
        }
    }
}
