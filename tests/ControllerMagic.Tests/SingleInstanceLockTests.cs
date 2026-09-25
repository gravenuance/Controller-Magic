using ControllerMagic;
using Xunit;

namespace ControllerMagic.Tests;

public class SingleInstanceLockTests
{
    private static string UniqueName() => $"ControllerMagic-Test-{Guid.NewGuid():N}";

    [Fact]
    public void TryAcquire_WhileHeld_ReturnsNull()
    {
        string name = UniqueName();
        using var first = SingleInstanceLock.TryAcquire(name);

        using var second = SingleInstanceLock.TryAcquire(name);

        Assert.NotNull(first);
        Assert.Null(second);
    }

    [Fact]
    public void TryAcquire_AfterTheHolderIsDisposed_Succeeds()
    {
        string name = UniqueName();
        SingleInstanceLock.TryAcquire(name)!.Dispose();

        using var relaunched = SingleInstanceLock.TryAcquire(name);

        Assert.NotNull(relaunched);
    }
}
