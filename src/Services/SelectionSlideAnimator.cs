using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SuperScroll.Common;

namespace SuperScroll.Services
{
    // Makes a selection-driven jump LOOK smooth without moving the scroll offset.
    //
    // The offset is the thing WPF guards. Playnite scrolls to keep the selected item visible, and
    // any attempt to animate by moving the offset away from that item gets undone immediately -
    // bring-into-view fires again, jumps back, and the list bounces. That was the first attempt and
    // it is unfixable by tuning, because WPF is enforcing a rule rather than reacting to a value.
    //
    // So the offset is left exactly where Playnite put it - correct, final, unargued-with - and the
    // CONTENT is translated backwards by the same distance and eased to zero. The view is already
    // where it should be; the pixels merely catch up. Nothing re-triggers, because from WPF's point
    // of view nothing moved.
    //
    // A RenderTransform is the right tool for this: it is composited, affects no layout, and cannot
    // perturb scroll state or item realization.
    public class SelectionSlideAnimator
    {
        // Beyond this, sliding would expose more blank space than a virtualized panel has realized
        // rows to fill, so a large jump lands instantly instead. Page Up/Down and jump-to-letter
        // deliberately fall on the far side of this line.
        private const double MaxSlidePixels = 320.0;

        private readonly ScrollViewer _scrollViewer;
        private readonly Func<double> _getSmoothing;
        private readonly FileLogger _fileLogger;

        private TranslateTransform _transform;
        private FrameworkElement _content;
        private bool _running;
        private long _lastTicks;
        private long _holdUntilTicks;   // while set, the band is held rather than springing back
        private double _bounceTarget;   // where the band is stretching TO, eased into rather than snapped
        private double _bounceRaw;      // uncapped total pushed, so the limit is a curve and not a wall
        private double _springVelocity; // carried into the release, so arriving fast returns fast

        public SelectionSlideAnimator(ScrollViewer scrollViewer, Func<double> getSmoothing, FileLogger fileLogger)
        {
            _scrollViewer = scrollViewer;
            _getSmoothing = getSmoothing;
            _fileLogger = fileLogger;
        }

        // verticalChange is what the ScrollViewer just moved by. Translating the content by the
        // SAME sign puts it back where it visually was, so easing to zero replays that distance.
        public bool Slide(double verticalChange)
        {
            if (Math.Abs(verticalChange) > MaxSlidePixels) return false;
            if (!EnsureTransform()) return false;

            // Accumulated, not assigned: several navigation moves can land inside one animation,
            // and each should extend the distance still owed rather than discard what is pending.
            var pending = _transform.Y + verticalChange;

            // Clamped so a burst of held-key moves cannot build an offset larger than the panel has
            // realized rows to cover.
            _transform.Y = ScrollPolicy.Clamp(pending, -MaxSlidePixels, MaxSlidePixels);
            _holdUntilTicks = 0;
            _bounceTarget = 0;
            _bounceRaw = 0;
            _springVelocity = 0;

            Start();
            return true;
        }

        // Pushes the content against the end of the list and lets it spring back.
        //
        // Uses the same transform and the same easing loop as the slide, because it is the same
        // motion: something is displaced and eases to zero. Only the way the displacement is
        // arrived at differs - a slide takes the whole distance at once, a bounce accumulates it
        // against rising resistance.
        public void Bounce(double deltaPixels)
        {
            if (!EnsureTransform()) return;

            // Sets a TARGET and lets the loop ease into it, rather than assigning the transform
            // outright. Assigning it produced a static jump: the displacement appeared in one
            // frame, so the stretch was never seen - only the release was. Easing both directions
            // is what makes it read as elastic in both.
            // Arriving at speed should return at speed. Seeding velocity from the push is what
            // makes a hard arrival snap back harder than a gentle one, rather than every release
            // looking identical regardless of how it started.
            _springVelocity += deltaPixels * 2.0;

            _bounceRaw = ScrollPolicy.AccumulateOverscroll(_bounceRaw, deltaPixels);
            _bounceTarget = ScrollPolicy.DisplacementFor(_bounceRaw, ScrollPolicy.MaxOverscrollPixels);

            // Hold it while input keeps arriving, and only spring back once it stops.
            //
            // Without this the band decays toward zero between every notch while each new notch
            // re-stretches it, so a continuous push produces rapid alternation rather than a
            // stretch - it reads as a flicker, not a rubber band. Holding is what makes it feel
            // like resistance being maintained rather than repeatedly overcome.
            _holdUntilTicks = DateTime.UtcNow.Ticks +
                (long)(ScrollPolicy.OverscrollHoldMs * TimeSpan.TicksPerMillisecond);

            Start();
        }

        public void Cancel()
        {
            Stop();
            _holdUntilTicks = 0;
            _bounceTarget = 0;
            _bounceRaw = 0;
            _springVelocity = 0;
            if (_transform != null) _transform.Y = 0;
        }

        private bool EnsureTransform()
        {
            if (_transform != null && _content != null && _content.IsLoaded) return true;

            _content = FindPresenter(_scrollViewer);
            if (_content == null) return false;

            // Reuse an existing translate if the theme already put one there, rather than
            // overwriting whatever it was doing with it.
            _transform = _content.RenderTransform as TranslateTransform;
            if (_transform == null)
            {
                if (_content.RenderTransform != null && !(_content.RenderTransform is MatrixTransform))
                {
                    // Something non-trivial owns the transform. Leave it alone; a wrong guess here
                    // would visibly distort the theme's own presentation.
                    _fileLogger?.Debug(() => $"[Slide] {_content.GetType().Name} already has a {_content.RenderTransform.GetType().Name}; not sliding");
                    _content = null;
                    return false;
                }

                _transform = new TranslateTransform();
                _content.RenderTransform = _transform;
            }

            return true;
        }

        private void Start()
        {
            if (_running) return;
            _running = true;
            _lastTicks = DateTime.UtcNow.Ticks;
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
                if (_transform == null || _content == null || !_content.IsLoaded)
                {
                    Stop();
                    return;
                }

                var now = DateTime.UtcNow.Ticks;
                var elapsedMs = (now - _lastTicks) / (double)TimeSpan.TicksPerMillisecond;
                _lastTicks = now;

                var smoothing = ScrollPolicy.EffectiveSmoothing(_getSmoothing(), elapsedMs);
                var holding = _holdUntilTicks > 0 && now < _holdUntilTicks;

                double next;

                if (_bounceRaw != 0 || _springVelocity != 0)
                {
                    if (holding)
                    {
                        // Still being pushed: ease out toward the stretch the curve says this much
                        // pushing earns, and keep the spring's velocity in step so the release
                        // starts from the motion already happening rather than from a standstill.
                        next = ScrollPolicy.Step(_transform.Y, _bounceTarget, smoothing, elapsedMs);
                        _springVelocity = elapsedMs > 0 ? (next - _transform.Y) / (elapsedMs / 1000.0) : 0;
                    }
                    else
                    {
                        // Released. A critically damped spring rather than more easing - it starts
                        // from the velocity above, accelerates, and settles without overshooting.
                        _holdUntilTicks = 0;
                        _bounceRaw = 0;
                        _bounceTarget = 0;

                        next = _transform.Y;
                        ScrollPolicy.SpringStep(ref next, ref _springVelocity, elapsedMs);

                        if (ScrollPolicy.SpringAtRest(next, _springVelocity))
                        {
                            _transform.Y = 0;
                            _springVelocity = 0;
                            Stop();
                            return;
                        }

                        _transform.Y = next;
                        return;
                    }
                }
                else
                {
                    next = ScrollPolicy.Step(_transform.Y, _bounceTarget, smoothing, elapsedMs);
                }

                _transform.Y = next;

                if (!holding && Math.Abs(next) < ScrollPolicy.SettleThresholdPixels)
                {
                    // Land on exactly zero. A residual fraction of a pixel left on a RenderTransform
                    // blurs text on the whole list until something else happens to clear it.
                    _transform.Y = 0;
                    Stop();
                }
            }
            catch
            {
                if (_transform != null) _transform.Y = 0;
                Stop();
            }
        }

        // The element that actually holds the scrolled content. ItemsPresenter for a list;
        // otherwise whatever the ScrollContentPresenter is presenting.
        private static FrameworkElement FindPresenter(DependencyObject root)
        {
            var scp = FindChild<ScrollContentPresenter>(root);
            if (scp == null) return null;

            var items = FindChild<ItemsPresenter>(scp);
            if (items != null) return items;

            return scp.Content as FrameworkElement;
        }

        private static T FindChild<T>(DependencyObject parent) where T : DependencyObject
        {
            if (parent == null) return null;

            var count = VisualTreeHelper.GetChildrenCount(parent);
            for (var i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                var typed = child as T;
                if (typed != null) return typed;

                var found = FindChild<T>(child);
                if (found != null) return found;
            }
            return null;
        }
    }
}
