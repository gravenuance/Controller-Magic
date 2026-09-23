using System.Globalization;

namespace ControllerMagic;

internal enum LogLevel
{
    Information,
    Warning,
    Error
}

// Persistent, leveled log for the whole app. Debug.WriteLine was the only diagnostic output
// before this - compiled out of Release builds entirely, so a real user's install never wrote
// anything but the one true unhandled crash. This is the "copy diagnostics and hand it over"
// story for a background tray utility nobody can attach a debugger to.
internal sealed class AppLog
{
    // Archive and start fresh once the log passes this size, so a long-running install can't grow
    // the file forever.
    private const long MaxSizeBytes = 5 * 1024 * 1024;

    private readonly string _path;
    private readonly TimeProvider _clock;
    private readonly Lock _writeLock = new();

    public AppLog(string path, TimeProvider clock)
    {
        _path = path;
        _clock = clock;
    }

    // Not named "Path" - would shadow System.IO.Path for every unqualified Path.* call elsewhere
    // in this class.
    public string FilePath => _path;

    // Settable only so the test run can redirect it away from the user's real log.
    public static AppLog Default { get; internal set; } = new(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ControllerMagic", "app.log"),
        TimeProvider.System);

    public void Info(string message) => Write(LogLevel.Information, message, null);
    public void Warning(string message, Exception? ex = null) => Write(LogLevel.Warning, message, ex);
    public void Error(string message, Exception? ex = null) => Write(LogLevel.Error, message, ex);

    // Pure formatting, split out from the file-writing side so it's directly testable against a
    // fixed clock without touching disk.
    internal string FormatEntry(LogLevel level, string message, Exception? ex)
    {
        string timestamp = _clock.GetUtcNow().ToString("u", CultureInfo.InvariantCulture);
        string line = $"{timestamp} [{level}] {message}";
        return ex != null ? $"{line}{Environment.NewLine}{ex}" : line;
    }

    private void Write(LogLevel level, string message, Exception? ex)
    {
        string entry = FormatEntry(level, message, ex);
        try
        {
            lock (_writeLock)
            {
                string? dir = Path.GetDirectoryName(_path);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);

                RotateIfNeeded();
                File.AppendAllText(_path, entry + Environment.NewLine + Environment.NewLine);
            }
        }
        catch
        {
            // Logging itself failing must never crash or cascade - there's nothing more to
            // safely do at this point.
        }
    }

    internal void RotateIfNeeded()
    {
        if (!File.Exists(_path))
            return;
        if (new FileInfo(_path).Length < MaxSizeBytes)
            return;

        string archivePath = $"{_path}.{_clock.GetUtcNow():yyyyMMddHHmmss}.old";
        File.Move(_path, archivePath, overwrite: true);
    }
}
