using System.IO;

namespace LabelDesigner.Services;

/// <summary>
/// Minimal, dependency-free, thread-safe rolling-file logger. Writes one file per day under
/// %APPDATA%\LabelDesigner\logs. Deliberately never throws — logging must not be able to take
/// down the always-on print listener. Use this so a shop can answer "why didn't this print?"
/// without attaching a debugger.
/// </summary>
public static class LogService
{
    private static readonly object _lock = new();
    private static string _dir => Path.Combine(AppConfig.DataDir, "logs");

    public static string Directory => _dir;

    public static void Info(string message)  => Write("INFO",  message, null);
    public static void Warn(string message)  => Write("WARN",  message, null);
    public static void Error(string message, Exception? ex = null) => Write("ERROR", message, ex);

    private static void Write(string level, string message, Exception? ex)
    {
        try
        {
            lock (_lock)
            {
                System.IO.Directory.CreateDirectory(_dir);
                var file = Path.Combine(_dir, $"label-{DateTime.Now:yyyyMMdd}.log");
                var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}";
                if (ex != null) line += Environment.NewLine + "    " + ex;
                File.AppendAllText(file, line + Environment.NewLine);
            }
        }
        catch
        {
            // Never let logging failures propagate.
        }
    }
}
