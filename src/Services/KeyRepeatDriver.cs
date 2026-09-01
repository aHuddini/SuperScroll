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
        private DependencyObject _ownerList;   // the list the hold started in

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

            self.RelaxListThrottle(e.OriginalSource as DependencyObject);

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
            _ownerList = FindListBoxEx(Keyboard.FocusedElement as DependencyObject);

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
            _ownerList = null;
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

                    // Layout-paced runs the timer at the frame floor and gates each tick on tile
                    // availability; the fixed mode uses the interval the user chose.
                    _timer.Interval = TimeSpan.FromMilliseconds(
                        settings.PaceRepeatToLayout
                            ? Constants.LayoutPacedFloorMs
                            : ScrollPolicy.Clamp(settings.KeyRepeatIntervalMs,
                                Constants.MinKeyRepeatIntervalMs, Constants.MaxKeyRepeatIntervalMs));
                }

                // Focus left the list this hold started in. Without this the repeat carries on
                // firing arrow keys at whatever now has focus - a filter checkbox - which is the
                // "gets bugged out after holding a while" failure: CanAdvanceYet finds no list, so
                // it answers "nothing to wait for" and every tick keeps driving the wrong element.
                if (_ownerList != null &&
                    FindListBoxEx(Keyboard.FocusedElement as DependencyObject) != _ownerList)
                {
                    if (!ReturnFocusToSelection(_ownerList))
                    {
                        _fileLogger?.Lifecycle("[KeyRepeat] focus left the list and could not be returned — stopping the repeat rather than driving keys elsewhere");
                        StopTimer();
                        return;
                    }
                    _fileLogger?.Debug(() => "[KeyRepeat] focus returned to the list mid-hold");
                }

                // Layout-paced: skip this tick if the panel has not realized the current
                // selection's tile yet. Skipping costs one frame; overrunning costs your place.
                if (settings.PaceRepeatToLayout && !CanAdvanceYet())
                {
                    _fileLogger?.Debug(() => "[KeyRepeat] waiting for the tile to be realized");
                    return;
                }

                RaiseKeyDown(_heldKey);
            }
            catch (Exception ex)
            {
                _fileLogger?.Debug(() => $"[KeyRepeat] tick failed: {ex.Message}");
                StopTimer();
            }
        }

        // Lowers Playnite's own per-list navigation throttle.
        //
        // ListBoxEx holds `keyRepeatTimer` at Interval = 150 and does `if (ignoreKeyRepeat) {
        // e.Handled = true; return; }`, so ANY navigation key arriving within 150ms of the last is
        // discarded - including keys the user pressed by hand. That is the reported "doesn't
        // respect how fast I am pressing": pressing quickly is simply not possible, because two
        // presses inside 150ms become one.
        //
        // This was found earlier and deliberately left alone, on the grounds that navigating faster
        // reaches unrealized tiles sooner and that throttle was the only thing preventing it. That
        // reasoning no longer applies: CanAdvanceYet now gates on whether the tile actually exists,
        // which is the precise protection Playnite was approximating with a blunt 150ms wait. With
        // the accurate check in place the blunt one only costs presses.
        //
        // Only ever lowered, once per list, and the timer belongs to a control rather than to the
        // system - it dies with the list, so there is nothing global to restore.
        private void RelaxListThrottle(DependencyObject source)
        {
            try
            {
                var list = FindListBoxEx(source);
                if (list == null) return;
                if (ThrottledLists.TryGetValue(list, out _)) return;
                ThrottledLists.Add(list, Boxed);

                var field = list.GetType().GetField("keyRepeatTimer",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                var timer = field?.GetValue(list) as System.Timers.Timer;
                if (timer == null)
                {
                    _fileLogger?.Lifecycle($"[KeyRepeat] {list.GetType().Name} exposes no keyRepeatTimer — this Playnite version throttles differently, leaving it alone");
                    return;
                }

                if (timer.Interval <= Constants.LayoutPacedFloorMs) return;

                _fileLogger?.Lifecycle($"[KeyRepeat] {list.GetType().Name} throttle lowered from {timer.Interval:F0}ms to {Constants.LayoutPacedFloorMs:F0}ms so fast presses are not discarded");
                timer.Interval = Constants.LayoutPacedFloorMs;
            }
            catch (Exception ex)
            {
                _fileLogger?.Debug(() => $"[KeyRepeat] could not relax the list throttle: {ex.Message}");
            }
        }

        private static readonly object Boxed = new object();
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<DependencyObject, object> ThrottledLists =
            new System.Runtime.CompilerServices.ConditionalWeakTable<DependencyObject, object>();

        // Paces repeats to tile availability rather than to a clock.
        //
        // The first version of this posted each repeat with Dispatcher.BeginInvoke at
        // DispatcherPriority.Loaded and froze the application outright. Loaded sits ABOVE Input in
        // the dispatcher's order, so a callback that re-posts at Loaded from inside a Loaded
        // callback never lets the queue fall to Input. Keystrokes are then never processed, KeyUp
        // never arrives, the held key never clears, and the loop runs forever with the UI thread
        // fully occupied. A floor check that re-posted instead of waiting made it spin even harder.
        //
        // So no priority games. The existing timer keeps running at Input priority - which yields
        // by construction - and each tick simply asks the question that actually matters: does the
        // selected item have a realized container yet? Playnite's FullscreenTilePanel prepares one
        // row either side of the viewport during Measure/Arrange, so a null container means the
        // panel has not caught up and this tick should pass rather than move the selection past
        // what exists. That is the same condition ListBoxEx.FocusSelected fails on when focus is
        // lost, checked before causing it rather than after.
        private bool CanAdvanceYet()
        {
            try
            {
                var list = FindListBoxEx(Keyboard.FocusedElement as DependencyObject);
                if (list == null) return true;   // not a Playnite tile list; nothing to wait for

                var selector = list as System.Windows.Controls.Primitives.Selector;
                var items = list as ItemsControl;
                if (selector?.SelectedItem == null || items == null) return true;

                return items.ItemContainerGenerator.ContainerFromItem(selector.SelectedItem) != null;
            }
            catch
            {
                return true; // never let this check be the reason navigation stops
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

            // The selection did not move and focus stayed put: this key is pushing against the end
            // of the list. Bounce it, so an end feels like an edge rather than a dead key.
            if (indexAfter == indexBefore && indexBefore >= 0)
            {
                BounceAtEnd(owner);
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

        // Nudges the list when navigation has run out of items in that direction.
        //
        // Reuses the same transform animator the wheel bounce uses, found from the list's own
        // ScrollViewer, so keyboard and wheel produce the same motion at the same edge rather than
        // two flourishes that almost match.
        private void BounceAtEnd(DependencyObject list)
        {
            try
            {
                var settings = _getSettings?.Invoke();
                if (settings?.EnableOverscrollBounce != true) return;

                var sv = FindAncestorScrollViewer(list);
                if (sv == null) return;

                var up = _heldKey == Key.Up || _heldKey == Key.PageUp;
                ScrollEnhancer.BounceFor(sv, up ? ScrollPolicy.MaxOverscrollPixels : -ScrollPolicy.MaxOverscrollPixels);
            }
            catch { }
        }

        private static ScrollViewer FindAncestorScrollViewer(DependencyObject start)
        {
            var current = start;
            var hops = 0;
            while (current != null && hops++ < 40)
            {
                var sv = current as ScrollViewer;
                if (sv != null) return sv;
                current = System.Windows.Media.VisualTreeHelper.GetParent(current);
            }

            // Not an ancestor of the list, but inside its template.
            return FindChildScrollViewer(start);
        }

        private static ScrollViewer FindChildScrollViewer(DependencyObject parent)
        {
            if (parent == null) return null;
            var count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent);
            for (var i = 0; i < count; i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
                var sv = child as ScrollViewer;
                if (sv != null) return sv;
                var found = FindChildScrollViewer(child);
                if (found != null) return found;
            }
            return null;
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
