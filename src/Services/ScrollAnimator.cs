using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SuperScroll.Services
{
    // Drives one ScrollViewer toward a target offset, one rendered frame at a time.
    //
    // Frame-synced rather than timer-driven. A DispatcherTimer fires on its own schedule and its
    // ticks drift against the compositor, so some frames get two updates and some get none - which
    // reads as micro-stutter no amount of easing can hide. CompositionTarget.Rendering fires once
    // per frame the moment WPF is about to draw, so every update lands on a frame that is actually
    // shown.
    //
    // No Storyboard either: ScrollViewer.VerticalOffset is read-only, so animating it means an
    // attached proxy property and a fresh Storyboard per wheel event. That allocates during the
    // one interaction that must never allocate, and restarting a Storyboard mid-flight throws away
    // the velocity the user just built up.
    public class ScrollAnimator
    {
        private readonly ScrollViewer _scrollViewer;
        private readonly Func<double> _getSmoothing;

        private double _targetOffset;
        private bool _running;
        private long _lastTicks;
        private double _starvedMs;   // time spent on late frames, for the give-up ceiling
        private double _lastExtent;  // to notice the estimated bottom bound still moving
        private long _extentMovedTicks;

        public ScrollAnimator(ScrollViewer scrollViewer, Func<double> getSmoothing)
        {
            _scrollViewer = scrollViewer;
            _getSmoothing = getSmoothing;
            _targetOffset = scrollViewer.VerticalOffset;
        }

        public double TargetOffset => _targetOffset;

        // Extends the journey rather than restarting it - see ScrollPolicy.AccumulateTarget.
        // Returns the part of the delta that could NOT be used because the list ran out, in the
        // same units and sign it was given. Zero in the ordinary case.
        //
        // That leftover used to be discarded, and discarding it is what made arriving at an end
        // during a fast scroll feel like hitting something. The view halted at the boundary while
        // still moving quickly, and the bounce then began from nothing on the following notch -
        // two separate motions where there should be one. Handing the excess to the band instead
        // means the speed you arrive with is the speed that stretches it.
        public double AddDelta(double deltaPixels)
        {
            var maxOffset = Math.Max(0, _scrollViewer.ExtentHeight - _scrollViewer.ViewportHeight);

            // Re-sync to the live offset when idle. The view can move without us - keyboard, a
            // scrollbar drag, ScrollIntoView on selection change - and a stale target would yank
            // the view back to wherever the last fling ended.
            if (!_running) _targetOffset = _scrollViewer.VerticalOffset;

            var desired = _targetOffset - deltaPixels;
            var clamped = ScrollPolicy.Clamp(desired, 0, maxOffset);

            _targetOffset = clamped;
            Start();

            // The bottom bound is an ESTIMATE while the bottom bound is being approached, and that
            // asymmetry is why arrival misbehaved there and never at the top. Zero is exact and
            // never moves; maxOffset is ExtentHeight - ViewportHeight, and a virtualized panel
            // refines ExtentHeight as tiles realize. Clamping against a stale, too-small maxOffset
            // reports overflow before the end has really been reached - so the band stretches while
            // the view still has somewhere to go, and the two move at once.
            //
            // So overflow is withheld while the extent is still settling. A notch or two of
            // momentum is lost in that window; a bounce fired against a boundary that then moves is
            // visible every time.
            var extentNow = _scrollViewer.ExtentHeight;
            if (Math.Abs(extentNow - _lastExtent) > 0.5)
            {
                _lastExtent = extentNow;
                _extentMovedTicks = DateTime.UtcNow.Ticks;
            }

            var sinceExtentMovedMs = (DateTime.UtcNow.Ticks - _extentMovedTicks) / (double)TimeSpan.TicksPerMillisecond;
            if (sinceExtentMovedMs < ScrollPolicy.ExtentSettleMs) return 0;

            return -(desired - clamped);
        }

        // True while this animator is the one writing the offset. The navigation watcher uses it to
        // tell its own frames apart from a scroll somebody else caused - without it, every frame we
        // render looks like a fresh external jump and the animation re-triggers itself forever.
        public bool IsApplyingInternally { get; private set; }

        // Rewinds to where the view was, then animates forward to where something else just put it.
        //
        // This is how programmatic scrolls get smoothed. WPF's ScrollContentPresenter sits between
        // a list item and its ScrollViewer, so it handles RequestBringIntoView first and jumps
        // immediately; by the time any plugin handler on the ScrollViewer runs, the offset has
        // already moved and there is nothing left to intercept. Rather than fight for the event,
        // this lets the jump happen and undoes it within the same frame, then covers the same
        // distance as an animation. The rewind is never rendered - the frame it would appear in is
        // the frame the animation starts.
        public void CatchUp(double fromOffset, double toOffset)
        {
            var maxOffset = Math.Max(0, _scrollViewer.ExtentHeight - _scrollViewer.ViewportHeight);

            IsApplyingInternally = true;
            try { _scrollViewer.ScrollToVerticalOffset(ScrollPolicy.Clamp(fromOffset, 0, maxOffset)); }
            finally { IsApplyingInternally = false; }

            _targetOffset = ScrollPolicy.Clamp(toOffset, 0, maxOffset);
            Start();
        }

        // Retargets to an absolute offset, for navigation rather than wheel input.
        //
        // Assignment, not accumulation - and that is the whole difference between the two entry
        // points. A wheel notch is a request for MORE distance, so it adds. A selection change is a
        // request to be at ONE place, and the place a held direction asked for two repeats ago is
        // already wrong. Adding those would send the view sailing past the selection.
        public void AnimateTo(double offset)
        {
            var maxOffset = Math.Max(0, _scrollViewer.ExtentHeight - _scrollViewer.ViewportHeight);
            _targetOffset = ScrollPolicy.Clamp(offset, 0, maxOffset);
            Start();
        }

        // Lands on an offset immediately, skipping the animation. What a held direction gets, so
        // the list stays attached to the input instead of trailing it.
        public void JumpTo(double offset)
        {
            var maxOffset = Math.Max(0, _scrollViewer.ExtentHeight - _scrollViewer.ViewportHeight);
            _targetOffset = ScrollPolicy.Clamp(offset, 0, maxOffset);
            Stop();
            IsApplyingInternally = true;
            try { _scrollViewer.ScrollToVerticalOffset(_targetOffset); }
            finally { IsApplyingInternally = false; }
        }

        // Abandons the animation and adopts wherever the view is now. Used when something else
        // takes control, so the two do not fight over the offset.
        public void Cancel()
        {
            Stop();
            _targetOffset = _scrollViewer.VerticalOffset;
        }

        private void Start()
        {
            if (_running) return;
            _running = true;
            _lastTicks = DateTime.UtcNow.Ticks;
            _starvedMs = 0;
            CompositionTarget.Rendering += OnRendering;
        }

        private void Stop()
        {
            if (!_running) return;
            _running = false;
            CompositionTarget.Rendering -= OnRendering;
        }

        private void OnRendering(object sender, EventArgs e)
        {
            try
            {
                // A ScrollViewer that has left the tree can still be referenced by a pending frame
                // callback. Unhooking here is what stops a theme switch leaking an animator per
                // discarded view.
                if (!_scrollViewer.IsLoaded)
                {
                    Stop();
                    return;
                }

                var now = DateTime.UtcNow.Ticks;
                var elapsedMs = (now - _lastTicks) / (double)TimeSpan.TicksPerMillisecond;
                _lastTicks = now;

                // Accumulate only LATE frames. A healthy frame means the stall passed, so the
                // budget resets rather than creeping up over a long, deliberate glide.
                if (ScrollPolicy.IsStarvedFrame(elapsedMs)) _starvedMs += elapsedMs;
                else _starvedMs = 0;

                var current = _scrollViewer.VerticalOffset;

                // Clamp every frame, not just on input. Extent changes under us constantly as
                // virtualized rows realize and de-realize, and a target that was valid when the
                // wheel turned can be past the end by the time we get there.
                var maxOffset = Math.Max(0, _scrollViewer.ExtentHeight - _scrollViewer.ViewportHeight);
                _targetOffset = ScrollPolicy.Clamp(_targetOffset, 0, maxOffset);

                // The ceiling. A scroll that has been running this long is being dragged out by
                // something else on the UI thread, and finishing it beats drifting toward the
                // target through frames the user is watching stutter.
                if (ScrollPolicy.HasSettled(current, _targetOffset) ||
                    ScrollPolicy.ShouldForceSettle(_starvedMs))
                {
                    IsApplyingInternally = true;
                    try { _scrollViewer.ScrollToVerticalOffset(_targetOffset); }
                    finally { IsApplyingInternally = false; }
                    Stop();
                    return;
                }

                // Ease harder while frames are arriving slowly, so there are fewer irregular
                // positions to notice. Distance is already time-corrected inside ScaleSmoothing;
                // this is about spending fewer frames, not covering more ground.
                var smoothing = ScrollPolicy.EffectiveSmoothing(_getSmoothing(), elapsedMs);

                var next = ScrollPolicy.Step(current, _targetOffset, smoothing, elapsedMs);

                IsApplyingInternally = true;
                try { _scrollViewer.ScrollToVerticalOffset(next); }
                finally { IsApplyingInternally = false; }
            }
            catch
            {
                // A frame callback that throws takes the render loop with it. Nothing here is
                // worth that, so a bad frame ends the animation instead.
                Stop();
            }
        }
    }
}
