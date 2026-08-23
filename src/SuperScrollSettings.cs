using System;
using System.Collections.Generic;
using Playnite.SDK;
using SuperScroll.Common;
using SuperScroll.Services;

namespace SuperScroll
{
    public class SuperScrollSettings : ObservableObject
    {
        // Defaults live on the backing fields and nowhere else. A reset copies from a pristine
        // instance, so changing a default means editing one line here - the rule UniPlaySong
        // arrived at after its per-tab reset handlers drifted out of sync with the real defaults.
        private bool enableSmoothScrolling = true;
        private double linesPerNotch = Constants.DefaultLinesPerNotch;
        private double lineHeightPixels = Constants.DefaultLineHeightPixels;
        private double smoothing = Constants.DefaultSmoothing;
        private bool enableNavigationSmoothing = false;
        private double navigationDebounceMs = ScrollPolicy.DefaultNavigationDebounceMs;
        private bool enableInputRepeatPatch = false;
        private int repeatDelayMs = Constants.DefaultRepeatDelayMs;
        private int repeatRateMs = Constants.DefaultRepeatRateMs;
        private bool enableKeyRepeatOverride = false;
        private double keyRepeatInitialDelayMs = Constants.DefaultKeyRepeatInitialDelayMs;
        private double keyRepeatIntervalMs = Constants.DefaultKeyRepeatIntervalMs;
        private bool paceRepeatToLayout = false;
        private bool enableDebugLogging = false;

        public bool EnableSmoothScrolling
        {
            get => enableSmoothScrolling;
            set { enableSmoothScrolling = value; OnPropertyChanged(); }
        }

        // How many notional lines one wheel notch travels.
        public double LinesPerNotch
        {
            get => linesPerNotch;
            set
            {
                linesPerNotch = Math.Round(Clamp(value, Constants.MinLinesPerNotch, Constants.MaxLinesPerNotch), 1);
                OnPropertyChanged();
            }
        }

        // The pixel height one "line" stands for.
        public double LineHeightPixels
        {
            get => lineHeightPixels;
            set
            {
                lineHeightPixels = Math.Round(Clamp(value, Constants.MinLineHeightPixels, Constants.MaxLineHeightPixels), 0);
                OnPropertyChanged();
            }
        }

        // Fraction of the remaining distance covered per 60Hz frame.
        public double Smoothing
        {
            get => smoothing;
            set
            {
                smoothing = Math.Round(Clamp(value, Constants.MinSmoothing, Constants.MaxSmoothing), 2);
                OnPropertyChanged();
            }
        }

        // Fullscreen: smooth the scroll that follows a controller or keyboard selection change.
        public bool EnableNavigationSmoothing
        {
            get => enableNavigationSmoothing;
            set { enableNavigationSmoothing = value; OnPropertyChanged(); }
        }

        // Navigations closer together than this are treated as a held direction and land instantly.
        public double NavigationDebounceMs
        {
            get => navigationDebounceMs;
            set
            {
                navigationDebounceMs = Math.Round(Clamp(value, ScrollPolicy.MinNavigationDebounceMs, ScrollPolicy.MaxNavigationDebounceMs), 0);
                OnPropertyChanged();
            }
        }

        // Reaches into Playnite's own controller repeat timings. Off by default - it is the one
        // feature here that touches the host application's internals rather than WPF's surface.
        public bool EnableInputRepeatPatch
        {
            get => enableInputRepeatPatch;
            set { enableInputRepeatPatch = value; OnPropertyChanged(); }
        }

        // Pause before a held direction begins repeating. Playnite's own value is 700ms.
        public int RepeatDelayMs
        {
            get => repeatDelayMs;
            set
            {
                repeatDelayMs = (int)Clamp(value, Constants.MinRepeatDelayMs, Constants.MaxRepeatDelayMs);
                OnPropertyChanged();
            }
        }

        // Interval between repeats once it starts. Playnite's own value is 80ms.
        public int RepeatRateMs
        {
            get => repeatRateMs;
            set
            {
                repeatRateMs = (int)Clamp(value, Constants.MinRepeatRateMs, Constants.MaxRepeatRateMs);
                OnPropertyChanged();
            }
        }

        // Replaces Windows' auto-repeat for navigation keys with our own timing.
        public bool EnableKeyRepeatOverride
        {
            get => enableKeyRepeatOverride;
            set { enableKeyRepeatOverride = value; OnPropertyChanged(); }
        }

        // How long a key must be held before it starts repeating. Windows' floor is 250ms.
        public double KeyRepeatInitialDelayMs
        {
            get => keyRepeatInitialDelayMs;
            set
            {
                keyRepeatInitialDelayMs = Math.Round(Clamp(value, Constants.MinKeyRepeatInitialDelayMs, Constants.MaxKeyRepeatInitialDelayMs), 0);
                OnPropertyChanged();
            }
        }

        // Interval between repeats once it starts.
        public double KeyRepeatIntervalMs
        {
            get => keyRepeatIntervalMs;
            set
            {
                keyRepeatIntervalMs = Math.Round(Clamp(value, Constants.MinKeyRepeatIntervalMs, Constants.MaxKeyRepeatIntervalMs), 0);
                OnPropertyChanged();
            }
        }

        // Repeat as fast as the list can actually realize its tiles, instead of on a fixed clock.
        public bool PaceRepeatToLayout
        {
            get => paceRepeatToLayout;
            set { paceRepeatToLayout = value; OnPropertyChanged(); }
        }

        public bool EnableDebugLogging
        {
            get => enableDebugLogging;
            set { enableDebugLogging = value; OnPropertyChanged(); }
        }

        // Clamped in the setters rather than in the UI, because settings arrive from three places -
        // the slider, deserialized JSON, and a reset - and only one of those goes through a control
        // that could enforce a range.
        private static double Clamp(double v, double min, double max)
        {
            if (v < min) return min;
            if (v > max) return max;
            return v;
        }
    }
}
