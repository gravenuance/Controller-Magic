using System.Globalization;
using System.Text;
using System.Threading.Channels;

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

    // Archived logs kept after a rotation; older ones are deleted so disk use stays bounded.
    private const int ArchivesKept = 3;

    private readonly string _path;
    private readonly TimeProvider _clock;

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

    // Entries are queued and written by one background task, so a caller (the poll thread above all)
    // never waits on the disk. The queue is unbounded: the app logs state changes, not a stream.
    private readonly record struct LogItem(string? Text, TaskCompletionSource? Flushed);

    private readonly Channel<LogItem> _queue = Channel.CreateUnbounded<LogItem>(
        new UnboundedChannelOptions { SingleReader = true });

    private int _writerStarted;
    private bool _directoryReady;

    // Long enough for a slow disk, short enough that a stuck one can't hang exit.
    private static readonly TimeSpan FlushTimeout = TimeSpan.FromSeconds(3);

    // Waits until everything logged so far is on disk. For exit and the crash path, where the
    // process may end before the background writer gets there.
    // Blocking is deliberate here: there is no later point to await from.
    public void Flush()
    {
        if (Volatile.Read(ref _writerStarted) == 0)
            return;

        var flushed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Enqueue(new LogItem(null, flushed));
        flushed.Task.Wait(FlushTimeout);
    }

    private void Write(LogLevel level, string message, Exception? ex) =>
        Enqueue(new LogItem(FormatEntry(level, message, ex) + Environment.NewLine + Environment.NewLine, null));

    private void Enqueue(LogItem item)
    {
        if (Interlocked.CompareExchange(ref _writerStarted, 1, 0) == 0)
            _ = Task.Run(WriteLoopAsync);

        _queue.Writer.TryWrite(item);
    }

    private async Task WriteLoopAsync()
    {
        var reader = _queue.Reader;
        var batch = new StringBuilder();
        var flushes = new List<TaskCompletionSource>();

        while (await reader.WaitToReadAsync().ConfigureAwait(false))
        {
            while (reader.TryRead(out var item))
            {
                if (item.Text is { } text)
                    batch.Append(text);
                if (item.Flushed is { } flushed)
                    flushes.Add(flushed);
            }

            WriteToDisk(batch.ToString());
            batch.Clear();

            foreach (var flushed in flushes)
                flushed.TrySetResult();
            flushes.Clear();
        }
    }

    private void WriteToDisk(string text)
    {
        if (text.Length == 0)
            return;

        try
        {
            if (!_directoryReady)
            {
                string? dir = Path.GetDirectoryName(_path);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);
                _directoryReady = true;
            }

            RotateIfNeeded();
            File.AppendAllText(_path, text);
        }
        catch
        {
            // Logging itself failing must never crash or cascade - there's nothing more to
            // safely do at this point. The directory is checked again next time.
            _directoryReady = false;
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
        DeleteOldArchives();
    }

    // The fixed-width UTC timestamp in each archive name makes ordinal order chronological.
    private void DeleteOldArchives()
    {
        string? dir = Path.GetDirectoryName(Path.GetFullPath(_path));
        if (dir == null)
            return;

        string[] archives = Directory.GetFiles(dir, $"{Path.GetFileName(_path)}.*.old");
        Array.Sort(archives, StringComparer.Ordinal);
        foreach (string stale in archives.AsSpan(0, Math.Max(0, archives.Length - ArchivesKept)))
            File.Delete(stale);
    }
}
