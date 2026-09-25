using ControllerMagic;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace ControllerMagic.Tests;

public class AppLogTests
{
    private static string TempPath() => Path.Combine(Path.GetTempPath(), $"cm-log-{Guid.NewGuid():N}.log");

    [Fact]
    public void FormatEntry_UsesInjectedClock_NotWallClock()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.Parse("2026-01-02T03:04:05Z"));
        var log = new AppLog(TempPath(), clock);

        string entry = log.FormatEntry(LogLevel.Warning, "something happened", null);

        Assert.Contains("2026-01-02", entry, StringComparison.Ordinal);
        Assert.Contains("[Warning]", entry, StringComparison.Ordinal);
        Assert.Contains("something happened", entry, StringComparison.Ordinal);
    }

    [Fact]
    public void FormatEntry_WithException_IncludesExceptionDetails()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var log = new AppLog(TempPath(), clock);
        var ex = new InvalidOperationException("boom");

        string entry = log.FormatEntry(LogLevel.Error, "failed to do the thing", ex);

        Assert.Contains("[Error]", entry, StringComparison.Ordinal);
        Assert.Contains("failed to do the thing", entry, StringComparison.Ordinal);
        Assert.Contains("InvalidOperationException", entry, StringComparison.Ordinal);
        Assert.Contains("boom", entry, StringComparison.Ordinal);
    }

    [Fact]
    public void Warning_WritesAPersistedLineToDisk()
    {
        string path = TempPath();
        var log = new AppLog(path, new FakeTimeProvider(DateTimeOffset.UtcNow));

        try
        {
            log.Warning("disk write test");

            Assert.True(File.Exists(path));
            Assert.Contains("disk write test", File.ReadAllText(path), StringComparison.Ordinal);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void RotateIfNeeded_FileUnderLimit_LeavesFileInPlace()
    {
        string path = TempPath();
        File.WriteAllText(path, "small");
        var log = new AppLog(path, new FakeTimeProvider(DateTimeOffset.UtcNow));

        try
        {
            log.RotateIfNeeded();

            Assert.True(File.Exists(path));
            Assert.Equal("small", File.ReadAllText(path));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void RotateIfNeeded_FileOverLimit_ArchivesAndClearsCurrentPath()
    {
        string path = TempPath();
        // One byte over the 5MB rotation threshold.
        File.WriteAllBytes(path, new byte[5 * 1024 * 1024 + 1]);
        var clock = new FakeTimeProvider(DateTimeOffset.Parse("2026-06-15T12:00:00Z"));
        var log = new AppLog(path, clock);
        string expectedArchive = $"{path}.20260615120000.old";

        try
        {
            log.RotateIfNeeded();

            Assert.False(File.Exists(path));
            Assert.True(File.Exists(expectedArchive));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
            if (File.Exists(expectedArchive)) File.Delete(expectedArchive);
        }
    }

    [Fact]
    public void RotateIfNeeded_KeepsOnlyTheNewestArchives()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"cm-logdir-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, "app.log");
        string[] older = ["20260101000000", "20260201000000", "20260301000000", "20260401000000"];
        foreach (string stamp in older)
            File.WriteAllText($"{path}.{stamp}.old", stamp);
        File.WriteAllText(Path.Combine(dir, "unrelated.old"), "keep");
        File.WriteAllBytes(path, new byte[5 * 1024 * 1024 + 1]);
        var log = new AppLog(path, new FakeTimeProvider(DateTimeOffset.Parse("2026-06-15T12:00:00Z")));

        try
        {
            log.RotateIfNeeded();

            string[] remaining = Directory.GetFiles(dir, "app.log.*.old")
                .Select(Path.GetFileName)
                .Order(StringComparer.Ordinal)
                .ToArray()!;
            Assert.Equal(
                ["app.log.20260301000000.old", "app.log.20260401000000.old", "app.log.20260615120000.old"],
                remaining);
            Assert.True(File.Exists(Path.Combine(dir, "unrelated.old")));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
