using ControllerMagic;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace ControllerMagic.Tests;

public sealed class UnhandledExceptionReporterTests : IDisposable
{
    private readonly string _logPath = Path.Combine(Path.GetTempPath(), $"cm-crash-{Guid.NewGuid():N}.log");
    private readonly List<ExceptionNotice> _notices = [];

    public void Dispose()
    {
        _log?.Flush();
        if (File.Exists(_logPath)) File.Delete(_logPath);
    }

    private AppLog? _log;

    private UnhandledExceptionReporter NewReporter(Action<ExceptionNotice>? notify = null)
    {
        var clock = new FakeTimeProvider(DateTimeOffset.Parse("2026-09-25T10:00:00Z"));
        _log = new AppLog(_logPath, clock);
        return new UnhandledExceptionReporter(_log, clock, notify ?? _notices.Add);
    }

    private static InvalidOperationException Thrown(string message)
    {
        try
        {
            throw new InvalidOperationException(message);
        }
        catch (InvalidOperationException ex)
        {
            return ex;
        }
    }

    [Fact]
    public void ReportSurvivable_RepeatedExceptions_NotifiesOnlyOnce()
    {
        var reporter = NewReporter();

        reporter.ReportSurvivable(Thrown("first"));
        reporter.ReportSurvivable(Thrown("second"));

        _log!.Flush();

        Assert.Equal([ExceptionNotice.Survived], _notices);
        Assert.Contains("first", File.ReadAllText(_logPath), StringComparison.Ordinal);
    }

    [Fact]
    public void ReportFatal_FlushesTheLogBeforeTellingTheUser()
    {
        string? logAtNotice = null;
        var reporter = NewReporter(_ => logAtNotice = File.ReadAllText(_logPath));

        reporter.ReportFatal(Thrown("about to die"));

        Assert.Contains("about to die", logAtNotice, StringComparison.Ordinal);
    }

    [Fact]
    public void ReportSurvivable_ThrownAgainWhileTheNoticeIsShowing_DoesNotStackNotices()
    {
        UnhandledExceptionReporter? reporter = null;
        reporter = NewReporter(notice =>
        {
            _notices.Add(notice);
            reporter!.ReportSurvivable(Thrown("from the nested message loop"));
        });

        reporter.ReportSurvivable(Thrown("tick"));

        Assert.Equal([ExceptionNotice.Survived], _notices);
    }

    [Fact]
    public void ReportFatal_AfterASurvivableNotice_StillTellsTheUserOnce()
    {
        var reporter = NewReporter();
        reporter.ReportSurvivable(Thrown("ui"));

        reporter.ReportFatal(Thrown("fatal one"));
        reporter.ReportFatal(Thrown("fatal two"));

        Assert.Equal([ExceptionNotice.Survived, ExceptionNotice.Crashed], _notices);
        Assert.Contains("fatal two", File.ReadAllText(_logPath), StringComparison.Ordinal);
    }
}
