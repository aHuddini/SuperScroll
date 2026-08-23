using System;
using System.Linq;
using System.Reflection;
using SuperScroll.Common;

namespace SuperScroll.Services
{
    // Lowers Playnite's controller key-repeat timings.
    //
    // Everything else in this plugin works through public WPF surface. This does not, so it is
    // opt-in, off by default, and written to fail closed.
    //
    // The numbers it targets, from Playnite/Input/GameController.cs:
    //
    //     private readonly int resendDelay = 700;   // pause before a held direction repeats at all
    //     private readonly int resendRate  = 80;    // interval once repeating
    //
    // 700ms before a held direction does anything is the single most-felt delay in Fullscreen
    // navigation, and it is not adjustable from Playnite's settings or its SDK.
    //
    // Most of the route there is public: PlayniteApplication.Current is a public static, and its
    // GameController property is public too. Only the last hop needs reflection - the two private
    // readonly ints on GameControllerManager. Reflection can write a readonly INSTANCE field on
    // .NET Framework, and because these are readonly rather than const the compiler reads them at
    // runtime instead of inlining them, so a write actually takes effect.
    //
    // Safety rules this follows, because it is reaching into another application's state:
    //   - every hop is verified before it is used; a rename anywhere means no patch, not a crash
    //   - both fields are checked to be Int32 before writing
    //   - the original values are captured first and restored on disable or shutdown
    //   - nothing here ever throws outward
    public class InputRepeatPatcher
    {
        private const string ApplicationTypeName = "Playnite.PlayniteApplication";
        private const string DelayFieldName = "resendDelay";
        private const string RateFieldName = "resendRate";

        private readonly FileLogger _fileLogger;

        private object _controllerManager;
        private FieldInfo _delayField;
        private FieldInfo _rateField;
        private int _originalDelay;
        private int _originalRate;
        private bool _patched;

        public InputRepeatPatcher(FileLogger fileLogger)
        {
            _fileLogger = fileLogger;
        }

        public bool IsPatched => _patched;

        // Returns true when the requested values are in place. False means Playnite kept its own,
        // which is the safe outcome and never an error worth surfacing to the user beyond the log.
        public bool Apply(int delayMs, int rateMs)
        {
            try
            {
                if (!Resolve()) return false;

                if (!_patched)
                {
                    _originalDelay = (int)_delayField.GetValue(_controllerManager);
                    _originalRate = (int)_rateField.GetValue(_controllerManager);
                    _fileLogger?.Lifecycle($"[InputRepeat] Playnite defaults captured: delay={_originalDelay}ms rate={_originalRate}ms");
                }

                _delayField.SetValue(_controllerManager, delayMs);
                _rateField.SetValue(_controllerManager, rateMs);
                _patched = true;

                _fileLogger?.Lifecycle($"[InputRepeat] applied: delay={delayMs}ms rate={rateMs}ms (controller navigation only — a physical keyboard repeats at the Windows typematic rate, which no extension controls)");
                return true;
            }
            catch (Exception ex)
            {
                _fileLogger?.Lifecycle($"[InputRepeat] could NOT apply, Playnite keeps its own timings: {ex.Message}");
                return false;
            }
        }

        // Puts Playnite's own numbers back. Called when the toggle is switched off and on shutdown,
        // so the change never outlives the session that asked for it.
        public void Restore()
        {
            try
            {
                if (!_patched || _controllerManager == null) return;

                _delayField.SetValue(_controllerManager, _originalDelay);
                _rateField.SetValue(_controllerManager, _originalRate);
                _patched = false;

                _fileLogger?.Lifecycle($"[InputRepeat] restored: delay={_originalDelay}ms rate={_originalRate}ms");
            }
            catch (Exception ex)
            {
                _fileLogger?.Warn($"[InputRepeat] restore failed: {ex.Message}");
            }
        }

        // Every step is checked; the first surprise stops the whole thing rather than guessing at
        // the next hop.
        //
        // Deliberately re-walked on every Apply rather than cached for the process lifetime.
        // Playnite constructs GameControllerManager lazily and can build a new one - SetupInputs
        // only creates it once SDL has finished initialising, and that happens on a path the
        // startup sequence does not await. A cached reference can therefore be either absent when
        // first asked for, or stale later, and both look identical from the outside: the sliders
        // move and nothing changes.
        private bool Resolve()
        {
            var appType = AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => { try { return a.GetType(ApplicationTypeName, false); } catch { return null; } })
                .FirstOrDefault(t => t != null);

            if (appType == null)
            {
                _fileLogger?.Lifecycle($"[InputRepeat] {ApplicationTypeName} not found — cannot patch");
                return false;
            }

            var currentProp = appType.GetProperty("Current", BindingFlags.Public | BindingFlags.Static);
            var app = currentProp?.GetValue(null);
            if (app == null)
            {
                _fileLogger?.Debug("[InputRepeat] PlayniteApplication.Current unavailable");
                return false;
            }

            var controllerProp = app.GetType().GetProperty("GameController", BindingFlags.Public | BindingFlags.Instance);
            var manager = controllerProp?.GetValue(app);
            if (manager == null)
            {
                // Normal on Desktop mode, and briefly during Fullscreen startup before the manager
                // is constructed - so this is a "not yet", not a failure.
                _fileLogger?.Lifecycle("[InputRepeat] no GameController yet (Desktop mode, or SDL still initialising) — will retry");
                return false;
            }

            var managerType = manager.GetType();
            var delay = managerType.GetField(DelayFieldName, BindingFlags.NonPublic | BindingFlags.Instance);
            var rate = managerType.GetField(RateFieldName, BindingFlags.NonPublic | BindingFlags.Instance);

            // Name AND type. A future Playnite could keep the names and change the units to a
            // TimeSpan, and writing an int into that would be the kind of failure that looks like
            // a controller bug rather than a plugin bug.
            if (delay == null || delay.FieldType != typeof(int) ||
                rate == null || rate.FieldType != typeof(int))
            {
                _fileLogger?.Warn($"[InputRepeat] {managerType.Name} does not expose {DelayFieldName}/{RateFieldName} as Int32 — this Playnite version is not supported, leaving it alone");
                return false;
            }

            _controllerManager = manager;
            _delayField = delay;
            _rateField = rate;
            return true;
        }
    }
}
