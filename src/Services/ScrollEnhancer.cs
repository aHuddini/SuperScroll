using System;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SuperScroll.Common;

namespace SuperScroll.Services
{
    // Takes over the mouse wheel for every ScrollViewer in the application.
    //
    // The mechanism is a single class handler rather than a walk of the visual tree. Playnite
    // rebuilds its view wholesale on theme change, on Desktop/Fullscreen switches and whenever a
    // view is re-templated, so anything found by walking has to be re-found - and every miss is a
    // list that silently scrolls the old way. A class handler is registered against the ScrollViewer
    // TYPE, so it applies to every instance that will ever exist, including ones created minutes
    // later by a theme this plugin has never seen.
    //
    // The catch, stated plainly because it shapes the design: class handlers cannot be
    // unregistered. Registration therefore happens once per process and Detach flips a flag the
    // handler reads. Turning the plugin off leaves the handler installed and inert, which costs a
    // predicate on each wheel event and nothing else.
    public class ScrollEnhancer
    {
        private static bool _classHandlerRegistered;
        private static readonly object RegistrationLock = new object();

        // Weak keys: an animator must never be the reason a discarded view stays alive.
        private static readonly ConditionalWeakTable<ScrollViewer, ScrollAnimator> Animators =
            new ConditionalWeakTable<ScrollViewer, ScrollAnimator>();

        private static ScrollEnhancer _active;

        private readonly Func<SuperScrollSettings> _getSettings;
        private readonly FileLogger _fileLogger;
        private bool _enabled;

        public ScrollEnhancer(Func<SuperScrollSettings> getSettings, FileLogger fileLogger)
        {
            _getSettings = getSettings;
            _fileLogger = fileLogger;
        }

        public void Attach()
        {
            _active = this;
            _enabled = true;

            lock (RegistrationLock)
            {
                if (_classHandlerRegistered) return;

                EventManager.RegisterClassHandler(
                    typeof(ScrollViewer),
                    UIElement.PreviewMouseWheelEvent,
                    new MouseWheelEventHandler(OnPreviewMouseWheel),
                    handledEventsToo: true);

                // Fullscreen's controller and keyboard navigation never touches the wheel: it moves
                // the selection, and Playnite calls BringIntoView on the newly selected item
                // (Playnite.FullscreenApp/Controls/ListBoxEx.cs).
                //
                // Intercepting RequestBringIntoView does not work, and it is worth recording why.
                // ScrollContentPresenter sits BETWEEN a list item and its ScrollViewer, so the
                // bubbling event reaches it first; it scrolls immediately and marks the event
                // handled. A class handler on ScrollViewer therefore runs after the jump has already
                // happened - and without handledEventsToo it never runs at all. Registering on
                // ScrollContentPresenter instead does not help either, because same-type class
                // handlers run in registration order and WPF's own is registered first.
                //
                // So this watches the RESULT rather than the request. ScrollChanged reports both the
                // new offset and the delta, which is enough to reconstruct where the view was and
                // animate that distance properly. It also covers every programmatic scroll, not just
                // the ones raised through bring-into-view.
                EventManager.RegisterClassHandler(
                    typeof(ScrollViewer),
                    ScrollViewer.ScrollChangedEvent,
                    new ScrollChangedEventHandler(OnScrollChanged));

                _classHandlerRegistered = true;
                _fileLogger?.Info("ScrollViewer class handlers registered (wheel + scroll-changed, process-wide, one time)");
            }
        }

        public void Detach()
        {
            _enabled = false;
            if (ReferenceEquals(_active, this)) _active = null;
        }

        private static void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            var self = _active;
            if (self == null || !self._enabled) return;

            var scrollViewer = sender as ScrollViewer;
            if (scrollViewer == null) return;

            var settings = self._getSettings?.Invoke();
            if (settings == null || !settings.EnableSmoothScrolling) return;

            // Modifier-held wheel is somebody else's gesture - zoom, horizontal pan, grid resize.
            // Taking it would break the host's own shortcuts.
            if (Keyboard.Modifiers != ModifierKeys.None) return;

            if (!ScrollPolicy.ShouldHandle(
                    enabled: true,
                    extentHeight: scrollViewer.ExtentHeight,
                    viewportHeight: scrollViewer.ViewportHeight,
                    currentOffset: scrollViewer.VerticalOffset,
                    wheelDelta: e.Delta))
            {
                // Deliberately left unhandled so it bubbles: a list already at its end should hand
                // the wheel to whatever contains it, which is what WPF does by default.
                return;
            }

            self.EnsurePixelScrolling(scrollViewer);

            var lineHeight = settings.LineHeightPixels > 0
                ? settings.LineHeightPixels
                : Constants.DefaultLineHeightPixels;

            var step = ScrollPolicy.StepForNotch(settings.LinesPerNotch, lineHeight);
            var pixels = ScrollPolicy.DeltaToPixels(e.Delta, step);

            var animator = Animators.GetValue(scrollViewer, sv => new ScrollAnimator(sv, () => CurrentSmoothing(self)));
            animator.AddDelta(pixels);

            // Ours now. Without this the ScrollViewer also applies its own jump and the two fight,
            // producing exactly the stutter this plugin exists to remove.
            e.Handled = true;
        }

        // When the last navigation happened, for the held-direction debounce. Per ScrollViewer,
        // because two lists navigated in turn are not a repeat of each other.
        private static readonly ConditionalWeakTable<ScrollViewer, SelectionSlideAnimator> Sliders =
            new ConditionalWeakTable<ScrollViewer, SelectionSlideAnimator>();

        private static readonly ConditionalWeakTable<ScrollViewer, StrongBox<long>> LastNavTicks =
            new ConditionalWeakTable<ScrollViewer, StrongBox<long>>();

        private static void OnScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            var self = _active;
            if (self == null || !self._enabled) return;

            var scrollViewer = sender as ScrollViewer;
            if (scrollViewer == null) return;

            var settings = self._getSettings?.Invoke();
            if (settings == null || !settings.EnableSmoothScrolling) return;
            if (!settings.EnableNavigationSmoothing) return;

            // Nothing moved vertically. ScrollChanged also fires for horizontal movement and for
            // extent changes as virtualized rows realize, and neither is a navigation.
            if (Math.Abs(e.VerticalChange) < 0.5) return;
            if (scrollViewer.ViewportHeight <= 0) return;

            var animator = Animators.GetValue(scrollViewer, sv => new ScrollAnimator(sv, () => CurrentSmoothing(self)));

            // Our own animation frames arrive here too. Reacting to them would restart the
            // animation on every frame it renders - a loop that never settles.
            if (animator.IsApplyingInternally) return;

            // A move that lands where the animation was already heading is that animation being
            // carried out by something else (a wheel notch we handed off, a settle), not a new one.
            if (Math.Abs(scrollViewer.VerticalOffset - animator.TargetOffset) < 0.5) return;

            var from = e.VerticalOffset - e.VerticalChange;
            var to = e.VerticalOffset;

            var now = DateTime.UtcNow.Ticks;
            var stamp = LastNavTicks.GetValue(scrollViewer, _ => new StrongBox<long>(0));
            var sinceMs = stamp.Value == 0
                ? double.MaxValue
                : (now - stamp.Value) / (double)TimeSpan.TicksPerMillisecond;
            stamp.Value = now;

            var animate = ScrollPolicy.ShouldAnimateNavigation(true, sinceMs, settings.NavigationDebounceMs);

            // The measured gap between navigations - the only way to tell a held direction from a
            // series of presses, and the only way to check what Playnite's repeat is ACTUALLY
            // delivering rather than what its constants say it should.
            self._fileLogger?.Debug(() => string.Format(
                "[Nav] gap={0} debounce={1}ms -> {2}, {3:F0}px -> {4:F0}px",
                sinceMs == double.MaxValue ? "first" : $"{sinceMs:F0}ms",
                settings.NavigationDebounceMs,
                animate ? "animate" : "jump",
                from, to));

            // A held direction lands instantly - there is no time to show an animation between
            // repeats, and trying would leave the content permanently behind the selection.
            if (!animate) return;

            // Smooth it by translating the CONTENT, never by moving the scroll offset.
            //
            // Moving the offset was the first attempt and it bounced: scrolling back puts the
            // selected item off-screen, WPF's bring-into-view fires again and jumps forward, and
            // the two fight every frame. WPF is enforcing "the selection stays visible" rather than
            // reacting to a value, so no amount of tuning wins that argument.
            //
            // Leaving the offset exactly where Playnite put it and easing a RenderTransform to zero
            // sidesteps it entirely: the view is already correct as far as WPF is concerned, and
            // only the pixels are catching up. Nothing re-triggers because nothing moved.
            var slider = Sliders.GetValue(scrollViewer, sv => new SelectionSlideAnimator(sv, () => CurrentSmoothing(self), self._fileLogger));
            slider.Slide(e.VerticalChange);
        }

        private static double CurrentSmoothing(ScrollEnhancer self)
        {
            var settings = self._getSettings?.Invoke();
            return settings?.Smoothing ?? Constants.DefaultSmoothing;
        }

        // Switches the owning ItemsControl from item-based to pixel-based scrolling.
        //
        // This is the difference between "smoother" and actually smooth. By default a Playnite list
        // scrolls in whole ITEMS: VerticalOffset counts rows, so the smallest possible movement is
        // one entire row and no amount of easing can produce anything between. Pixel mode makes the
        // offset a real distance, which is what gives the animator something to interpolate.
        //
        // Deliberately NOT done by setting CanContentScroll=false. That also yields pixel offsets,
        // but it turns virtualization off with it - every row in the library gets realized, and a
        // large collection stops being scrollable at all. ScrollUnit=Pixel keeps virtualization and
        // changes only the unit.
        private void EnsurePixelScrolling(ScrollViewer scrollViewer)
        {
            try
            {
                if (GetPixelScrollApplied(scrollViewer)) return;
                SetPixelScrollApplied(scrollViewer, true); // set first: a failure should not retry every notch

                if (!scrollViewer.CanContentScroll) return; // already pixel-based, nothing to change

                var itemsControl = FindAncestor<ItemsControl>(scrollViewer);
                if (itemsControl == null) return;

                if (VirtualizingPanel.GetScrollUnit(itemsControl) == ScrollUnit.Pixel) return;

                VirtualizingPanel.SetScrollUnit(itemsControl, ScrollUnit.Pixel);
                _fileLogger?.Debug($"Pixel scrolling enabled for {itemsControl.GetType().Name}");
            }
            catch (Exception ex)
            {
                _fileLogger?.Debug($"Could not enable pixel scrolling: {ex.Message}");
            }
        }

        private static readonly DependencyProperty PixelScrollAppliedProperty =
            DependencyProperty.RegisterAttached(
                "PixelScrollApplied", typeof(bool), typeof(ScrollEnhancer), new PropertyMetadata(false));

        private static bool GetPixelScrollApplied(DependencyObject d) => (bool)d.GetValue(PixelScrollAppliedProperty);
        private static void SetPixelScrollApplied(DependencyObject d, bool v) => d.SetValue(PixelScrollAppliedProperty, v);

        private static T FindAncestor<T>(DependencyObject start) where T : DependencyObject
        {
            var current = start;
            while (current != null)
            {
                current = VisualTreeHelper.GetParent(current);
                var typed = current as T;
                if (typed != null) return typed;
            }
            return null;
        }
    }
}
