using System.ComponentModel;
using ControllerMagic;
using Xunit;

namespace ControllerMagic.Tests;

public class ProcessNameCacheTests
{
    private readonly List<int> _lookups = [];
    private Func<int, string> _resolve;
    private readonly ProcessNameCache _cache;

    public ProcessNameCacheTests()
    {
        _resolve = pid => $"proc{pid}";
        _cache = new ProcessNameCache(pid =>
        {
            _lookups.Add(pid);
            return _resolve(pid);
        });
    }

    [Fact]
    public void SameWindowAndProcess_IsLookedUpOnce()
    {
        string? first = _cache.NameFor(hWnd: 10, pid: 1);
        string? second = _cache.NameFor(hWnd: 10, pid: 1);

        Assert.Equal("proc1", first);
        Assert.Equal("proc1", second);
        Assert.Single(_lookups);
    }

    [Fact]
    public void AnotherWindowOrProcess_IsLookedUpAgain()
    {
        _cache.NameFor(hWnd: 10, pid: 1);
        _cache.NameFor(hWnd: 11, pid: 1);
        string? reusedHandle = _cache.NameFor(hWnd: 11, pid: 2);

        Assert.Equal("proc2", reusedHandle);
        Assert.Equal([1, 1, 2], _lookups);
    }

    public static TheoryData<Exception> LookupFailures() =>
    [
        new ArgumentException("process gone"),
        new InvalidOperationException("process exited"),
        new Win32Exception(5),
    ];

    [Theory]
    [MemberData(nameof(LookupFailures))]
    public void LookupFails_ReturnsNullAndDoesNotRetryTheSameWindow(Exception failure)
    {
        _resolve = _ => throw failure;

        string? first = _cache.NameFor(hWnd: 10, pid: 1);
        string? second = _cache.NameFor(hWnd: 10, pid: 1);

        Assert.Null(first);
        Assert.Null(second);
        Assert.Single(_lookups);
    }
}
