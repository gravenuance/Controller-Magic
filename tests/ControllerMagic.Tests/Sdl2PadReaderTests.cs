using ControllerMagic;
using Xunit;

namespace ControllerMagic.Tests;

public class Sdl2PadReaderTests
{
    [Theory]
    [InlineData("XInput#0")]
    [InlineData("XInput#3")]
    public void IsXInputBacked_SdlXInputPath_IsLeftToXInputReader(string path)
    {
        Assert.True(Sdl2PadReader.IsXInputBacked(path));
    }

    [Theory]
    [InlineData(@"\\?\HID#{00001124-0000-1000-8000-00805f9b34fb}_VID&0002054c_PID&0ce6#9&1a2b3c4d&0&0000#{4d1e55b2-f16f-11cf-88cb-001111000030}")]
    [InlineData("")]
    [InlineData(null)]
    public void IsXInputBacked_HidOrMissingPath_IsReadBySdl(string? path)
    {
        Assert.False(Sdl2PadReader.IsXInputBacked(path));
    }
}
