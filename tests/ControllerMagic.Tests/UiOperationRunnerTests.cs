using ControllerMagic;
using Xunit;

namespace ControllerMagic.Tests;

public sealed class UiOperationRunnerTests : IDisposable
{
    private readonly string _logPath = Path.Combine(Path.GetTempPath(), $"cm-ui-{Guid.NewGuid():N}.log");
    private readonly AppLog _log;
    private readonly UiOperationRunner _runner;

    public UiOperationRunnerTests()
    {
        _log = new AppLog(_logPath, TimeProvider.System);
        _runner = new UiOperationRunner(_log);
    }

    public void Dispose()
    {
        _runner.Dispose();
        _log.Flush();
        File.Delete(_logPath);
    }

    [Fact]
    public async Task RunAsync_Failure_IsLoggedAndReportedInsteadOfThrown()
    {
        string? shown = null;

        await _runner.RunAsync("install drivers", _ => throw new InvalidOperationException("boom"), text => shown = text);

        _log.Flush();

        Assert.NotNull(shown);
        Assert.Contains("install drivers", await File.ReadAllTextAsync(_logPath, TestContext.Current.CancellationToken), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_CancelledByCancelAll_IsSilent()
    {
        string? shown = null;

        var run = _runner.RunAsync("wait", ct => Task.Delay(Timeout.Infinite, ct), text => shown = text);
        _runner.CancelAll();
        await run.ConfigureAwait(true);

        Assert.Null(shown);
        Assert.False(File.Exists(_logPath));
    }

    [Fact]
    public async Task RunAsync_AfterCancelAll_GetsAFreshToken()
    {
        _runner.CancelAll();
        bool cancelled = true;

        await _runner.RunAsync("next", ct =>
        {
            cancelled = ct.IsCancellationRequested;
            return Task.CompletedTask;
        }, _ => { });

        Assert.False(cancelled);
    }
}
