using Newtonsoft.Json;
using System.Collections.Generic;
using Playnite.SDK;
using Playnite.SDK.Plugins;
using SuperScroll.Services;

namespace SuperScroll
{
    public class SuperScrollSettingsViewModel : ObservableObject, ISettings
    {
        private readonly SuperScroll _plugin;
        private SuperScrollSettings _editing;

        public SuperScrollSettings Settings
        {
            get => _editing;
            set { _editing = value; OnPropertyChanged(); }
        }

        public SuperScrollSettingsViewModel(SuperScroll plugin)
        {
            _plugin = plugin;
            var saved = plugin.LoadPluginSettings<SuperScrollSettings>();
            Settings = saved ?? new SuperScrollSettings();

            // Moving a slider has to make the dropdown say "Custom". SelectedPreset is derived from
            // the three values rather than stored, so it only needs a nudge to re-read them.
            Settings.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(SuperScrollSettings.PaceRepeatToLayout) ||
                    e.PropertyName == nameof(SuperScrollSettings.EnableKeyRepeatOverride))
                {
                    OnPropertyChanged(nameof(IsRepeatIntervalEnabled));
                }

                if (e.PropertyName == nameof(SuperScrollSettings.Smoothing) ||
                    e.PropertyName == nameof(SuperScrollSettings.LinesPerNotch) ||
                    e.PropertyName == nameof(SuperScrollSettings.LineHeightPixels))
                {
                    OnPropertyChanged(nameof(SelectedPreset));
                }
            };
        }

        // The interval slider is meaningless while repeats are paced to layout - the rate is then
        // whatever the panel can sustain, not a number anyone picks. Exposed as a property rather
        // than an inverse-bool converter so the rule lives in one readable place.
        public bool IsRepeatIntervalEnabled =>
            Settings.EnableKeyRepeatOverride && !Settings.PaceRepeatToLayout;

        public IReadOnlyList<ScrollPreset> Presets => ScrollPreset.AllWithCustom;

        // Derived, never stored. A stored selection would go stale the moment a slider moved and
        // would then claim values the user had already changed - the dropdown would keep naming a
        // preset that no longer describes anything on screen.
        public ScrollPreset SelectedPreset
        {
            get => ScrollPreset.FindMatch(Settings.Smoothing, Settings.LinesPerNotch, Settings.LineHeightPixels);
            set
            {
                if (value == null || value.IsCustom) return; // Custom is a readout, not a destination

                Settings.Smoothing = value.Smoothing;
                Settings.LinesPerNotch = value.LinesPerNotch;
                Settings.LineHeightPixels = value.LineHeightPixels;
                OnPropertyChanged();
            }
        }

        // BeginEdit snapshots so Cancel can restore. A JSON round-trip rather than a hand-written
        // copy: a new setting is then covered automatically, where a hand-written clone is one more
        // place to forget.
        private string _snapshot;

        public void BeginEdit()
        {
            _snapshot = JsonConvert.SerializeObject(Settings);
        }

        public void CancelEdit()
        {
            if (_snapshot == null) return;
            JsonConvert.PopulateObject(_snapshot, Settings);
        }

        public void EndEdit()
        {
            _plugin.SavePluginSettings(Settings);
            _plugin.OnSettingsSaved(Settings);
        }

        public bool VerifySettings(out System.Collections.Generic.List<string> errors)
        {
            errors = new System.Collections.Generic.List<string>();
            return true; // every value is range-clamped in its setter, so nothing can be out of bounds here
        }

        public RelayCommand<object> OpenBenchCommand => new RelayCommand<object>(_ => _plugin.OpenTuningBench());

        public RelayCommand<object> ResetCommand => new RelayCommand<object>(_ =>
        {
            // Copies from a pristine instance rather than restating defaults. Same reason as the
            // backing-field rule in SuperScrollSettings: one source of truth for every default.
            var pristine = new SuperScrollSettings();
            Settings.EnableSmoothScrolling = pristine.EnableSmoothScrolling;
            Settings.LinesPerNotch = pristine.LinesPerNotch;
            Settings.LineHeightPixels = pristine.LineHeightPixels;
            Settings.Smoothing = pristine.Smoothing;
            Settings.EnableNavigationSmoothing = pristine.EnableNavigationSmoothing;
            Settings.NavigationDebounceMs = pristine.NavigationDebounceMs;
            Settings.EnableInputRepeatPatch = pristine.EnableInputRepeatPatch;
            Settings.RepeatDelayMs = pristine.RepeatDelayMs;
            Settings.RepeatRateMs = pristine.RepeatRateMs;
            Settings.EnableKeyRepeatOverride = pristine.EnableKeyRepeatOverride;
            Settings.KeyRepeatInitialDelayMs = pristine.KeyRepeatInitialDelayMs;
            Settings.KeyRepeatIntervalMs = pristine.KeyRepeatIntervalMs;
            Settings.PaceRepeatToLayout = pristine.PaceRepeatToLayout;
            Settings.EnableOverscrollBounce = pristine.EnableOverscrollBounce;
            Settings.EnableDebugLogging = pristine.EnableDebugLogging;
        });
    }
}
