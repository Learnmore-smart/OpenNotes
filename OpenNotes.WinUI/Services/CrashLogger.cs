using System;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace Caelum.Services
{
    /// <summary>
    /// Last-resort crash journal. Appends one timestamped entry per event to
    /// %LOCALAPPDATA%\Caelum\logs\crash-yyyyMMdd-HHmmss.log so a stowed WinRT
    /// exception (WER 0xC000027B) leaves a managed stack behind. Every path
    /// is defensive — logging must never throw inside a crash handler, and
    /// entries append so two faults in the same second share a file.
    /// </summary>
    internal static class CrashLogger
    {
        private const int MaxFiles = 20;
        private static readonly object Sync = new object();

        /// <summary>Logs an exception with type, message, stack, and the flattened inner chain.</summary>
        public static void Log(string source, Exception exception)
        {
            try
            {
                if (exception is AggregateException aggregate)
                    exception = aggregate.Flatten();
                Write(source, exception?.ToString() ?? "(no managed exception object)");
            }
            catch (Exception formatFailure)
            {
                // A throwing ToString()/Flatten() must not kill the journal —
                // record the type name as a last resort.
                Write(source, $"{exception?.GetType().Name ?? "unknown"}"
                    + $" (formatting faulted: {formatFailure.GetType().Name})");
            }
        }

        /// <summary>Logs a free-form detail line (e.g. a dodged fault, not a thrown exception).</summary>
        public static void Log(string source, string detail)
            => Write(source, detail ?? string.Empty);

        private static void Write(string source, string body)
        {
            lock (Sync)
            {
                try
                {
                    var dir = Path.Combine(ProductInfo.GetDataDirectory(), "logs");
                    Directory.CreateDirectory(dir);
                    var now = DateTime.Now;
                    File.AppendAllText(
                        Path.Combine(dir, $"crash-{now:yyyyMMdd-HHmmss}.log"),
                        $"[{now:yyyy-MM-dd HH:mm:ss.fff}] {source}\n{body}\n\n");
                    Prune(dir);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[CrashLogger] write failed: {ex}");
                }
            }
        }

        /// <summary>Best-effort cap: keep only the newest <see cref="MaxFiles"/> crash logs.</summary>
        private static void Prune(string dir)
        {
            try
            {
                var files = new DirectoryInfo(dir).GetFiles("crash-*.log");
                foreach (var file in files
                    .OrderBy(f => f.Name, StringComparer.Ordinal)
                    .Take(Math.Max(0, files.Length - MaxFiles)))
                {
                    file.Delete();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CrashLogger] prune failed: {ex}");
            }
        }
    }
}
