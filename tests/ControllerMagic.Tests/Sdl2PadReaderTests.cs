using ControllerMagic;
using SDL2;
using Xunit;

namespace ControllerMagic.Tests;

public class Sdl2PadReaderTests
{
    [Fact]
    public void ReportStampIsFree_OnlyForPlayStationPads_SinceSwitchPadsPowerTheirImuForIt()
    {
        var free = Enum.GetValues<SDL.SDL_GameControllerType>().Where(Sdl2PadReader.ReportStampIsFree);

        Assert.Equal(
            [SDL.SDL_GameControllerType.SDL_CONTROLLER_TYPE_PS4, SDL.SDL_GameControllerType.SDL_CONTROLLER_TYPE_PS5],
            free.Order());
    }
}
