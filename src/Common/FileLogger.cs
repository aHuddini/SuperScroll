using System;
using System.IO;

namespace SuperScroll.Common
{
    // Writes to SuperScroll.log next to the extension, gated behind the debug-logging setting.
    //
    // Same two-logger split UniPlaySong uses: Playnite's own ILogger goes to extension.log and is
    // reserved for things a user might report, while this one carries the high-frequency detail
    // that would otherwise drown it. Scroll input fires dozens of times a second, so "off unless
    // asked for" is not a nicety here.
    public class FileLogger
    {
        private readonly string _logPath;
        private readonly Func<bool> _isEnabled;
        private readonly object _lock = new object();

        public FileLogger(string directory, Func<bool> isEnabled)
        {
            _logPath = Path.Combine(directory, Constants.LogFileName);
            _isEnabled = isEnabled ?? (() => false);
        }

        public void Info(string message) => Write("INFO", message);
        public void Warn(string message) => Write("WARN", message);
        public void Error(string message) => Write("ERROR", message, always: true);

        // Always written, regardless of the debug setting. Reserved for one-shot facts a user has
        // to be able to check when something "does nothing" - notably whether the Playnite input
        // patch took. Asking someone to enable logging, restart, and reproduce is a poor answer to
        // "is this even running?".
        public void Lifecycle(string message) => Write("INFO", message, always: true);

        public void Debug(string message) => Write("DEBUG", message);

        // Deferred overload: the string is only built if logging is on. Worth having on a path
        // that runs per wheel event, where formatting a message nobody reads is pure waste.
        public void Debug(Func<string> message)
        {
            if (!IsEnabled()) return;
            Write("DEBUG", message());
        }

        private bool IsEnabled()
        {
            try { return _isEnabled(); } catch { return false; }
        }

        private void Write(string level, string message, bool always = false)
        {
            if (!always && !IsEnabled()) return;

            try
            {
                lock (_lock)
                {
                    RollIfTooLarge();
                    File.AppendAllText(_logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{level}] {message}{Environment.NewLine}");
                }
            }
            catch
            {
                // A logger that throws is worse than a logger that misses a line.
            }
        }

        private void RollIfTooLarge()
        {
            try
            {
                var info = new FileInfo(_logPath);
                if (!info.Exists || info.Length < Constants.MaxLogBytes) return;

                var old = _logPath + ".old";
                if (File.Exists(old)) File.Delete(old);
                File.Move(_logPath, old);
            }
            catch { }
        }
    }
}
