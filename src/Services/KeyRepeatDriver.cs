using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using SuperScroll.Common;

namespace SuperScroll.Services
{
    // Replaces Windows' keyboard auto-repeat with our own, for navigation keys only.
    //
    // Why this exists rather than SystemParametersInfo. Windows exposes typematic settings through
    // SPI_SETKEYBOARDDELAY, but that takes an INDEX of 0-3 - 250, 500, 750 or 1000ms - so 250ms is
    // the floor of that API, and it is system-wide besides. It is not, however, a floor on what an
    // application can do, because the OS repeat can simply be discarded and replaced.
    //
    // Every auto-repeat WPF delivers is flagged: KeyEventArgs.IsRepeat is false for the first press
    // and true for every OS repeat after it. Marking the repeats handled swallows them, leaving a
    // timer here free to raise navigation at any interval - well under 250ms if asked.
    //
    // Scoped by construction, which the SPI route could never be: nothing outside this process
    // changes, so there is nothing to restore and nothing that can outlive a crash.
    public class KeyRepeatDriver
    {
        private static bool _classHandlersRegistered;
        private static readonly object RegistrationLock = new object();
        private static KeyRepeatDriver _active;

        private readonly Func<SuperScrollSettings> _getSettings;
        private readonly FileLogger _fileLogger;
        private bool _enabled;

        private DispatcherTimer _timer;
        private Key _heldKey;
        private bool _inInitialDelay;
        private long _lastRaiseTicks;

        public KeyRepeatDriver(Func<SuperScrollSettings> getSettings, FileLogger fileLogger)
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
                if (_classHandlersRegistered) return;

                // On Window rather than on individual controls: Playnite rebuilds its view on mode
                // and theme changes, and a preview handler at the window sees every key on its way
                // down regardless of which control ends up focused.
                EventManager.RegisterClassHandler(typeof(Window), Keyboard.PreviewKeyDownEvent,
                    new KeyEventHandler(OnPreviewKeyDown), handledEventsToo: true);
                EventManager.RegisterClassHandler(typeof(Window), Keyboard.PreviewKeyUpEvent,
                    new KeyEventHandler(OnPreviewKeyUp), handledEventsToo: true);

                _classHandlersRegistered = true;
                _fileLogger?.Info("Key-repeat class handlers registered (process-wide, one time)");
            }
        }

        public void Detach()
        {
            _enabled = false;
            StopTimer();
            if (ReferenceEquals(_active, this)) _active = null;
        }

        private static void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            var self = _active;
            if (self == null || !self._enabled) return;

            var settings = self._getSettings?.Invoke();
            if (settings?.EnableKeyRepeatOverride != true) return;

            if (!IsNavigationKey(e.Key)) return;

            // Typing must never be touched. A held arrow key inside a search box is cursor
            // movement, and driving our own repeat there would fight the text editor.
            if (IsTextEntryFocused()) return;

            if (e.IsRepeat)
            {
                // The OS repeat. Swallow it - the timer below is what repeats now, at the rate the
                // user asked for rather than the four Windows offers.
                e.Handled = true;
                return;
            }

            self.StartRepeat(e.Key, settings);
        }

        private static void OnPreviewKeyUp(object sender, KeyEventArgs e)
        {
            var self = _active;
            if (self == null) return;
            if (e.Key != self._heldKey) return;
            self.StopTimer();
            self._heldKey = Key.None;   // also ends a layout-paced loop, which has no timer to stop
        }

        private void StartRepeat(Key key, SuperScrollSettings settings)
        {
            StopTimer();

            _heldKey = key;
            _inInitialDelay = true;

            // Input priority, not the DispatcherTimer default of Background: a repeat that queues
            // behind rendering arrives late and irregularly, which is the very thing being fixed.
            _timer = new DispatcherTimer(DispatcherPriority.Input)
            {
                Interval = TimeSpan.FromMilliseconds(
                    ScrollPolicy.Clamp(settings.KeyRepeatInitialDelayMs,
                        Constants.MinKeyRepeatInitialDelayMs, Constants.MaxKeyRepeatInitialDelayMs))
            };
            _timer.Tick += OnTick;
            _timer.Start();
        }

        private void StopTimer()
        {
            if (_timer == null) return;
            _timer.Stop();
            _timer.Tick -= OnTick;
            _timer = null;
            _heldKey = Key.None;
        }

        private void OnTick(object sender, EventArgs e)
        {
            try
            {
                // The key was released, or focus moved somewhere that types. Either way stop
                // driving it - a repeat outliving its keypress would move the selection on its own.
                if (!Keyboard.IsKeyDown(_heldKey) || IsTextEntryFocused())
                {
                    StopTimer();
                    return;
                }

                var settings = _getSettings?.Invoke();
                if (settings?.EnableKeyRepeatOverride != true)
                {
                    StopTimer();
                    return;
                }

                if (_inInitialDelay)
                {
                    _inInitialDelay = false;

                    if (settings.PaceRepeatToLayout)
                    {
                        // Hand off to the layout-paced loop and stop the clock entirely - from here
                        // the panel decides the rate, not a slider.
                        var key = _heldKey;
                        StopTimer();
                        _heldKey = key;
                        RaiseKeyDown(key);
                        QueueLayoutPacedRepeat();
                        return;
                    }

                    // First repeat has landed; switch from the hold delay to the repeat interval.
                    _timer.Interval = TimeSpan.FromMilliseconds(
                        ScrollPolicy.Clamp(settings.KeyRepeatIntervalMs,
                            Constants.MinKeyRepeatIntervalMs, Constants.MaxKeyRepeatIntervalMs));
                }

                RaiseKeyDown(_heldKey);
            }
            catch (Exception ex)
            {
                _fileLogger?.Debug(() => $"[KeyRepeat] tick failed: {ex.Message}");
                StopTimer();
            }
        }

        // Schedules the next repeat for AFTER the layout pass that realizes the tiles this one
        // just asked for.
        //
        // This is the fix the focus-escape bug actually wants, rather than the recovery beside it.
        // Playnite's FullscreenTilePanel.GetVisibleRange realizes exactly one row either side of
        // the viewport - the `- computedColumns` and `Rows + 1` terms - and it does so during
        // Measure/Arrange. A fixed-interval repeat can therefore move the selection twice before
        // layout runs once, putting the new item two rows out, past that window;
        // ContainerFromItem then returns null and focus is orphaned. Playnite's own 150ms throttle
        // avoids the race by being slower than any layout pass.
        //
        // Posting at Loaded priority runs after layout has completed, so the containers this repeat
        // needs exist before the next one is issued. The rate becomes whatever the panel can
        // actually sustain: quick when tiles are cheap, self-limiting when they are not, and never
        // ahead of what is drawn.
        private void QueueLayoutPacedRepeat()
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null) return;

            // DispatcherPriority.Loaded sits below Render and above Input, so it fires once the
            // layout pass triggered by the move has finished.
            dispatcher.BeginInvoke(new Action(LayoutPacedTick), DispatcherPriority.Loaded);
        }

        private void LayoutPacedTick()
        {
            try
            {
                if (_heldKey == Key.None) return;
                if (!Keyboard.IsKeyDown(_heldKey) || IsTextEntryFocused())
                {
                    _heldKey = Key.None;
                    return;
                }

                var settings = _getSettings?.Invoke();
                if (settings?.EnableKeyRepeatOverride != true || !settings.PaceRepeatToLayout)
                {
                    _heldKey = Key.None;
                    return;
                }

                // Frame floor. A trivial layout pass can complete several times inside one rendered
                // frame, which would move the selection further than anything is drawn.
                var now = DateTime.UtcNow.Ticks;
                var sinceMs = (now - _lastRaiseTicks) / (double)TimeSpan.TicksPerMillisecond;
                if (sinceMs < Constants.LayoutPacedFloorMs)
                {
                    QueueLayoutPacedRepeat();
                    return;
                }

                RaiseKeyDown(_heldKey);
                QueueLayoutPacedRepeat();
            }
            catch (Exception ex)
            {
                _fileLogger?.Debug(() => $"[KeyRepeat] layout-paced tick failed: {ex.Message}");
                _heldKey = Key.None;
            }
        }

        // Re-raises the key as a fresh KeyDown through WPF's input pipeline, so whatever handles
        // navigation normally handles this identically - no assumptions about how the host moves
        // its selection, which differs between Playnite's own lists and a theme's.
        private void RaiseKeyDown(Key key)
        {
            var before = Keyboard.FocusedElement as DependencyObject;
            var owner = FindListBoxEx(before);
            var indexBefore = SelectedIndexOf(owner);

            var source = PresentationSource.FromVisual(Application.Current?.MainWindow);
            if (source == null) return;

            var args = new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, key)
            {
                RoutedEvent = Keyboard.KeyDownEvent
            };
            _lastRaiseTicks = DateTime.UtcNow.Ticks;
            InputManager.Current.ProcessInput(args);

            DiagnoseJump(owner, before, indexBefore);
        }

        // Distinguishes the two ways a held key can lose your place, because they need different
        // fixes and look identical from the sofa.
        //
        //   Focus escaped   - WPF directional navigation walked focus out of the list at its end,
        //                     onto the next thing in tab order (the navigation bar).
        //
        //   Selection reset - Playnite put it back to the first item itself. ListBoxEx subscribes
        //                     FocusSelected() to the tile panel's InternalChildrenGenerated, and
        //                     FocusSelected contains:
        //                         if (SelectedItem == null && Items.Count > 0) SelectedItem = Items[0];
        //                     Fast navigation regenerates virtualized containers constantly, so if
        //                     SelectedItem is momentarily null when that fires, the list jumps to
        //                     the top. Nothing here can prevent that - it is Playnite reacting to
        //                     its own virtualization - but the log says plainly which one happened.
        private void DiagnoseJump(DependencyObject owner, DependencyObject before, int indexBefore)
        {
            if (owner == null) return;

            var indexAfter = SelectedIndexOf(owner);
            var after = Keyboard.FocusedElement as DependencyObject;
            var stillInList = after != null && FindListBoxEx(after) == owner;

            // A move of more than one step that lands on the first item is not navigation.
            if (indexAfter == 0 && indexBefore > 1)
            {
                _fileLogger?.Lifecycle($"[KeyRepeat] SELECTION RESET: index {indexBefore} -> 0 while holding. " +
                                       "This is Playnite's ListBoxEx.FocusSelected reacting to virtualized tiles being " +
                                       "regenerated (SelectedItem was null), not focus moving. Raise the repeat interval to reduce it.");
                return;
            }

            if (!stillInList)
            {
                var name = after == null ? "nothing" : after.GetType().Name;
                var recovered = ReturnFocusToSelection(owner);
                _fileLogger?.Lifecycle($"[KeyRepeat] FOCUS ESCAPED to {name} at index {indexBefore} — {(recovered ? "returned to the list" : "could not return, container not realized")}");
                return;
            }

            _fileLogger?.Debug(() => $"[KeyRepeat] index {indexBefore} -> {indexAfter}");
        }

        // Puts focus back on the container of whatever is currently selected.
        //
        // The log said what was actually happening, and it was neither guess: focus escapes to a
        // CheckBoxEx MID-list - at index 12, 27, 35, 39 - nowhere near either end, so it is not
        // overshoot and not directional navigation. It is virtualization losing a race.
        //
        // ListBoxEx.FocusSelected does:
        //     var selItem = ItemContainerGenerator.ContainerFromItem(SelectedItem) as FrameworkElement;
        //     if (selItem != null && !selItem.IsFocused) { selItem.Focus(); selItem.BringIntoView(); }
        // Driving repeats faster than the tile panel realizes containers means ContainerFromItem
        // returns null, so nothing takes focus. The old focused container has meanwhile been
        // recycled out of the tree, and WPF hands focus to the next focusable element it can find -
        // a filter checkbox. Playnite's own 150ms list throttle exists to keep this race from being
        // run at all, which is a good reason not to have lowered it.
        //
        // So this re-runs the same recovery on the CURRENT selection rather than restoring the
        // element that had focus before - that one is gone, which is the whole problem. Returns
        // false when the container still is not realized; the caller keeps repeating either way,
        // because stopping is what jammed navigation last time.
        private static bool ReturnFocusToSelection(DependencyObject list)
        {
            try
            {
                var selector = list as System.Windows.Controls.Primitives.Selector;
                if (selector == null) return false;

                var items = list as ItemsControl;
                var selected = selector.SelectedItem;
                if (items == null || selected == null) return false;

                var container = items.ItemContainerGenerator.ContainerFromItem(selected) as FrameworkElement;
                if (container != null)
                {
                    container.Focus();
                    return true;
                }

                // Not realized yet. Focusing the list itself is still better than leaving focus on
                // a checkbox: ListBoxEx subscribes FocusSelected to its own GotFocus, so it will
                // put focus on the right container as soon as one exists.
                var listElement = list as IInputElement;
                if (listElement != null)
                {
                    Keyboard.Focus(listElement);
                }
                return false;
            }
            catch
            {
                return false;
            }
        }

        private static int SelectedIndexOf(DependencyObject list)
        {
            var selector = list as System.Windows.Controls.Primitives.Selector;
            return selector?.SelectedIndex ?? -1;
        }

        // The focus-restoring guard that used to live here has been removed, not disabled.
        //
        // It restored focus and stopped the repeat whenever the focused element left the owning
        // list. That fires spuriously: while a virtualized panel regenerates tiles, focus lands
        // briefly on a container that is not yet parented under the same ListBoxEx, so a perfectly
        // ordinary move looked like an escape. The repeat then stopped and would not resume, which
        // reads as navigation going one way and then jamming.
        //
        // Restoring focus is also the wrong shape if the cause turns out to be a selection reset
        // rather than focus movement - putting focus back on an element whose selection Playnite
        // has already changed underneath it would fight the host. DiagnoseJump above says which of
        // the two is actually happening; the fix follows the answer rather than preceding it.

        // Matched by name rather than by type reference: ListBoxEx lives in Playnite.FullscreenApp,
        // which this plugin does not link against and which is absent entirely in Desktop mode.
        private static DependencyObject FindListBoxEx(DependencyObject start)
        {
            var current = start;
            var hops = 0;
            while (current != null && hops++ < 40)
            {
                if (current.GetType().Name == "ListBoxEx") return current;
                current = System.Windows.Media.VisualTreeHelper.GetParent(current)
                          ?? LogicalTreeHelper.GetParent(current);
            }
            return null;
        }

        private static bool IsNavigationKey(Key key)
        {
            switch (key)
            {
                case Key.Up:
                case Key.Down:
                case Key.Left:
                case Key.Right:
                case Key.PageUp:
                case Key.PageDown:
                    return true;
                default:
                    return false;
            }
        }

        private static bool IsTextEntryFocused()
        {
            var focused = Keyboard.FocusedElement;
            if (focused is TextBoxBase) return true;
            if (focused is PasswordBox) return true;

            var combo = focused as ComboBox;
            return combo != null && combo.IsEditable;
        }
    }
}
