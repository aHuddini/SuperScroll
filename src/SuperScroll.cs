using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Playnite.SDK;
using Playnite.SDK.Events;
using Playnite.SDK.Plugins;
using SuperScroll.Common;
using SuperScroll.Controls;
using SuperScroll.Services;

namespace SuperScroll
{
    public class SuperScroll : GenericPlugin
    {
        private static readonly ILogger Logger = LogManager.GetLogger();

        private readonly SuperScrollSettingsViewModel _settingsViewModel;
        private readonly FileLogger _fileLogger;
        private ScrollEnhancer _enhancer;
        private InputRepeatPatcher _repeatPatcher;
        private KeyRepeatDriver _keyRepeatDriver;

        public override Guid Id { get; } = Guid.Parse("994c1b41-d13e-45cc-abae-e2b898b00db3");

        public SuperScroll(IPlayniteAPI api) : base(api)
        {
            _settingsViewModel = new SuperScrollSettingsViewModel(this);

            _fileLogger = new FileLogger(
                GetPluginUserDataPath(),
                () => _settingsViewModel.Settings?.EnableDebugLogging == true);

            Properties = new GenericPluginProperties { HasSettings = true };
        }

        public override ISettings GetSettings(bool firstRunSettings) => _settingsViewModel;

        public override UserControl GetSettingsView(bool firstRunSettings) => new SettingsView();

        public override void OnApplicationStarted(OnApplicationStartedEventArgs args)
        {
            try
            {
                // Attached here rather than in the constructor: the class handler is cheap to
                // register but the first wheel event needs a live visual tree behind it, and
                // plugin construction happens before the main window exists.
                _enhancer = new ScrollEnhancer(() => _settingsViewModel.Settings, _fileLogger);
                _enhancer.Attach();

                _repeatPatcher = new InputRepeatPatcher(_fileLogger);
                ApplyRepeatPatch();

                _keyRepeatDriver = new KeyRepeatDriver(() => _settingsViewModel.Settings, _fileLogger);
                _keyRepeatDriver.Attach();

                Logger.Info($"SuperScroll v{GetVersion()} loaded");
                _fileLogger.Info($"Started — smoothing={_settingsViewModel.Settings.Smoothing}, " +
                                 $"linesPerNotch={_settingsViewModel.Settings.LinesPerNotch}, " +
                                 $"lineHeight={_settingsViewModel.Settings.LineHeightPixels}");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "SuperScroll failed to start");
            }
        }

        public override void OnApplicationStopped(OnApplicationStoppedEventArgs args)
        {
            try
            {
                _enhancer?.Detach();
                _enhancer = null;

                // Always restore, patched or not. Playnite's timings must not outlive the session
                // that changed them - a stale value surviving a plugin disable would look like a
                // Playnite bug with nothing pointing back here.
                _repeatPatcher?.Restore();
                _repeatPatcher = null;

                _keyRepeatDriver?.Detach();
                _keyRepeatDriver = null;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "SuperScroll failed to stop cleanly");
            }
        }

        // Called by the settings view model after a save, so a changed value takes effect without
        // a restart. The enhancer reads settings live through a delegate, so there is nothing to
        // re-wire - but a stale settings OBJECT would be invisible here, which is the trap
        // UniPlaySong hit when a save swapped the whole instance underneath its consumers.
        public void OnSettingsSaved(SuperScrollSettings settings)
        {
            _fileLogger.Info($"Settings saved — enabled={settings.EnableSmoothScrolling}, " +
                             $"smoothing={settings.Smoothing}, linesPerNotch={settings.LinesPerNotch}");

            // Re-applied on save so the sliders take effect without a restart, and so switching the
            // toggle off puts Playnite's own numbers back immediately.
            ApplyRepeatPatch();
        }

        // Applies or restores Playnite's controller repeat timings to match the current settings.
        private void ApplyRepeatPatch()
        {
            if (_repeatPatcher == null) return;

            var settings = _settingsViewModel.Settings;
            if (settings?.EnableInputRepeatPatch == true)
            {
                _repeatPatcher.Apply(settings.RepeatDelayMs, settings.RepeatRateMs);
            }
            else if (_repeatPatcher.IsPatched)
            {
                _repeatPatcher.Restore();
            }
        }

        // Writes the bench out beside the plugin's data and opens it, with the current settings
        // carried in the URL fragment so it shows what the reader actually has configured.
        //
        // Rewritten every time rather than written once: the file is a snapshot of an embedded
        // resource, and a stale copy left over from an older version would quietly preview
        // behaviour the plugin no longer has.
        public void OpenTuningBench()
        {
            try
            {
                var path = Path.Combine(GetPluginUserDataPath(), "bench.html");

                var asm = System.Reflection.Assembly.GetExecutingAssembly();
                var name = asm.GetManifestResourceNames()
                    .FirstOrDefault(n => n.EndsWith("bench.html", StringComparison.OrdinalIgnoreCase));

                if (name == null)
                {
                    Logger.Error("Tuning bench resource missing from the assembly");
                    return;
                }

                using (var stream = asm.GetManifestResourceStream(name))
                using (var file = File.Create(path))
                {
                    stream.CopyTo(file);
                }

                var s = _settingsViewModel.Settings;

                // InvariantCulture, deliberately: a comma decimal separator would arrive as an
                // unparseable value and silently fall back to the defaults, which is the one thing
                // this feature exists to avoid.
                var fragment = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "#s={0:0.00}&l={1:0.#}&h={2:0}", s.Smoothing, s.LinesPerNotch, s.LineHeightPixels);

                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = new Uri(path).AbsoluteUri + fragment,
                    UseShellExecute = true
                });

                _fileLogger.Info($"Tuning bench opened with {fragment}");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to open the tuning bench");
            }
        }

        public string GetVersion()
        {
            try
            {
                var asm = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
                return $"{asm.Major}.{asm.Minor}.{asm.Build}";
            }
            catch
            {
                return "unknown";
            }
        }

        public override IEnumerable<TopPanelItem> GetTopPanelItems() => new List<TopPanelItem>();
    }
}
