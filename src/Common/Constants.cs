namespace SuperScroll.Common
{
    public static class Constants
    {
        #region Scrolling

        // Windows' own wheel default is 3 lines per notch, so matching it means the plugin changes
        // how scrolling FEELS without changing how far it goes.
        // Huddini Flow: the shipped preset, arrived at by scrolling a real library rather
        // than by reasoning about the curve.
        public const double DefaultLinesPerNotch = 6.0;
        public const double MinLinesPerNotch = 1.0;
        public const double MaxLinesPerNotch = 12.0;

        // Playnite's list rows are far taller than a text line. This is the notional "line" the
        // notch multiplier applies to, tuned so a default notch clears roughly one grid row.
        public const double DefaultLineHeightPixels = 136.0;
        public const double MinLineHeightPixels = 8.0;
        public const double MaxLineHeightPixels = 200.0;

        // Fraction of the remaining distance covered per 60Hz frame. Higher is snappier, lower is
        // floatier. 0.25 lands a notch in about six frames - quick enough to feel responsive,
        // gradual enough to read as motion rather than a jump.
        public const double DefaultSmoothing = 0.30;
        public const double MinSmoothing = 0.05;
        public const double MaxSmoothing = 0.95;

        // Space kept between a newly selected item and the viewport edge when controller or
        // keyboard navigation scrolls to it. Zero would park the selection flush against the edge,
        // which hides whatever comes next and makes the list feel like it ends there.
        public const double NavigationEdgePadding = 24.0;

        #endregion

        #region Input repeat patch

        // Playnite's own numbers, from Playnite/Input/GameController.cs. Kept here as the
        // fallback the sliders start from, and as the record of what "unpatched" means.
        public const int PlayniteStockRepeatDelayMs = 700;
        public const int PlayniteStockRepeatRateMs = 80;

        public const int DefaultRepeatDelayMs = 300;
        public const int MinRepeatDelayMs = 100;
        public const int MaxRepeatDelayMs = 700;   // never above stock: this feature only reduces

        public const int DefaultRepeatRateMs = 60;
        public const int MinRepeatRateMs = 20;     // below this a held direction outruns the list
        public const int MaxRepeatRateMs = 200;

        #endregion


        #region Key repeat override

        // Windows' SPI_SETKEYBOARDDELAY only accepts an index of 0-3 (250/500/750/1000ms), so its
        // floor is 250ms and it is system-wide. Driving the repeat ourselves has neither limit -
        // these bounds are chosen for usability, not because the platform imposes them.
        public const double DefaultKeyRepeatInitialDelayMs = 180;
        public const double MinKeyRepeatInitialDelayMs = 50;
        public const double MaxKeyRepeatInitialDelayMs = 600;

        public const double DefaultKeyRepeatIntervalMs = 45;
        public const double MinKeyRepeatIntervalMs = 15;   // below this a held key outruns the eye
        public const double MaxKeyRepeatIntervalMs = 250;

        // Floor for layout-paced repeats. Without one, a cheap layout pass lets the dispatcher
        // callback run several times inside a single rendered frame, moving the selection further
        // than anything is drawn - fast in a way nobody can follow or stop.
        public const double LayoutPacedFloorMs = 16.0;

        #endregion

        #region Logging

        public const string LogFileName = "SuperScroll.log";
        public const long MaxLogBytes = 2 * 1024 * 1024;

        #endregion
    }
}
