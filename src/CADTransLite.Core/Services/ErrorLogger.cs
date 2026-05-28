// Services/ErrorLogger.cs
// Full-featured runtime logger for CADTrans Lite.
// Writes to E:\CADTrans Lite\log\ — one file per day, auto-purge after 30 days.

namespace CADTransLite.Core.Services;

/// <summary>
/// Log severity level.
/// </summary>
public enum LogLevel
{
    Info,
    Warn,
    Error
}

/// <summary>
/// Thread-safe, singleton runtime logger.
/// Captures ALL operational events — not just errors — for post-mortem debugging.
/// Writes one log file per day to the configured directory.
/// </summary>
public sealed class ErrorLogger
{
    private static readonly Lazy<ErrorLogger> _instance = new(() => new ErrorLogger());
    private readonly string _logDir;
    private readonly Lock _lock = new();

    /// <summary>Default log directory.</summary>
    public const string DefaultLogDir = @"E:\CADTrans Lite\log";

    /// <summary>Singleton instance.</summary>
    public static ErrorLogger Instance => _instance.Value;

    /// <summary>Current log directory.</summary>
    public string LogDir => _logDir;

    public ErrorLogger() : this(DefaultLogDir) { }

    public ErrorLogger(string logDir)
    {
        _logDir = logDir;
        try { Directory.CreateDirectory(_logDir); } catch { /* best-effort */ }
    }

    // ── Convenience methods ──────────────────────────────────────────────

    /// <summary>Logs an informational message.</summary>
    public void Info(string category, string message)
        => Write(LogLevel.Info, category, message, null);

    /// <summary>Logs a warning message.</summary>
    public void Warn(string category, string message)
        => Write(LogLevel.Warn, category, message, null);

    /// <summary>Logs an error message.</summary>
    public void Error(string category, string message)
        => Write(LogLevel.Error, category, message, null);

    /// <summary>Logs an exception with ERROR level.</summary>
    public void Error(string category, Exception ex)
        => Write(LogLevel.Error, category, ex.Message, ex);

    // ── Legacy compatibility ─────────────────────────────────────────────

    /// <summary>Legacy: Log(category, Exception) → now writes as ERROR.</summary>
    public void Log(string category, Exception ex)
        => Write(LogLevel.Error, category, ex.Message, ex);

    /// <summary>Legacy: Log(category, string) → now writes as ERROR.</summary>
    public void Log(string category, string message)
        => Write(LogLevel.Error, category, message, null);

    // ── Core write ───────────────────────────────────────────────────────

    private void Write(LogLevel level, string category, string message, Exception? ex)
    {
        try
        {
            var now = DateTime.Now;
            var fileName = $"run_{now:yyyy-MM-dd}.log";
            var path = Path.Combine(_logDir, fileName);

            var levelStr = level switch
            {
                LogLevel.Info => "INFO ",
                LogLevel.Warn => "WARN ",
                LogLevel.Error => "ERROR",
                _ => "?????"
            };

            using var writer = new StreamWriter(path, append: true, System.Text.Encoding.UTF8);
            lock (_lock)
            {
                writer.WriteLine($"[{now:yyyy-MM-dd HH:mm:ss.fff}] [{levelStr}] [{category}] {message}");

                if (ex is not null)
                {
                    writer.WriteLine($"  Type: {ex.GetType().FullName}");
                    writer.WriteLine($"  Stack: {ex.StackTrace ?? "(no stack trace)"}");
                    if (ex.InnerException is not null)
                    {
                        writer.WriteLine($"  → Inner: {ex.InnerException.Message}");
                        writer.WriteLine($"  → InnerStack: {ex.InnerException.StackTrace ?? ""}");
                    }
                }

                writer.WriteLine("---");
            }
        }
        catch
        {
            // Logging must never throw.
        }
    }

    /// <summary>
    /// Truncates a string for safe logging (avoids huge log entries from API responses).
    /// </summary>
    public static string Truncate(string? text, int maxLength = 300)
    {
        if (string.IsNullOrEmpty(text)) return "(empty)";
        return text.Length <= maxLength ? text : text[..maxLength] + $"…({text.Length} chars)";
    }

    /// <summary>
    /// Cleans up log files older than the specified number of days.
    /// </summary>
    public void PurgeOldLogs(int keepDays = 30)
    {
        try
        {
            var cutoff = DateTime.Now.AddDays(-keepDays);
            foreach (var file in Directory.GetFiles(_logDir, "run_*.log"))
            {
                if (File.GetCreationTime(file) < cutoff)
                {
                    try { File.Delete(file); } catch { /* best-effort */ }
                }
            }
            // Also clean up legacy error_*.log files
            foreach (var file in Directory.GetFiles(_logDir, "error_*.log"))
            {
                if (File.GetCreationTime(file) < cutoff)
                {
                    try { File.Delete(file); } catch { /* best-effort */ }
                }
            }
        }
        catch { /* best-effort */ }
    }
}
