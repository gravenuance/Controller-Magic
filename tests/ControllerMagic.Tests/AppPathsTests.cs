using ControllerMagic;
using Xunit;

namespace ControllerMagic.Tests;

public class AppPathsTests
{
    private const string LocalAppData = @"C:\Users\someone\AppData\Local";

    private static ResolvedDirectory Resolve(string? value) =>
        AppPaths.Cache(name => name == AppPaths.CacheDirVariable ? value : null, LocalAppData);

    [Fact]
    public void Cache_VariableSet_UsesIt()
    {
        var result = Resolve(@"D:\Cache\ControllerMagic");

        Assert.Equal(@"D:\Cache\ControllerMagic", result.Path);
        Assert.Null(result.Warning);
    }

    [Fact]
    public void Cache_VariableUnset_FallsBackToLocalAppDataWithoutWarning()
    {
        var result = Resolve(null);

        Assert.Equal(@"C:\Users\someone\AppData\Local\ControllerMagic", result.Path);
        Assert.Null(result.Warning);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Cache_VariableBlank_FallsBackWithoutWarning(string value)
    {
        var result = Resolve(value);

        Assert.Equal(@"C:\Users\someone\AppData\Local\ControllerMagic", result.Path);
        Assert.Null(result.Warning);
    }

    [Fact]
    public void Cache_VariableRelative_FallsBackAndWarns()
    {
        var result = Resolve(@"cache\here");

        Assert.Equal(@"C:\Users\someone\AppData\Local\ControllerMagic", result.Path);
        Assert.Contains(AppPaths.CacheDirVariable, result.Warning, StringComparison.Ordinal);
    }

    [Fact]
    public void Cache_VariableWithSurroundingSpaces_IsTrimmed()
    {
        var result = Resolve(@"  D:\Cache\ControllerMagic  ");

        Assert.Equal(@"D:\Cache\ControllerMagic", result.Path);
    }
}
