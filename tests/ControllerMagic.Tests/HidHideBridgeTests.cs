using ControllerMagic;
using Xunit;

namespace ControllerMagic.Tests;

public class HidHideBridgeTests
{
    [Theory]
    [InlineData("XInput#0", true)]
    [InlineData("XInput#3", true)]
    [InlineData(@"\\?\HID#{00001124-0000-1000-8000-00805f9b34fb}_VID&0002054c_PID&0ce6#9&1a7a84ed&0&0000#{4d1e55b2-f16f-11cf-88cb-001111000030}", false)]
    [InlineData("", false)]
    public void IsXInputPlaceholderPath_DetectsSdlsXInputBackendPath(string path, bool expected)
    {
        Assert.Equal(expected, HidHideBridge.IsXInputPlaceholderPath(path));
    }
}
