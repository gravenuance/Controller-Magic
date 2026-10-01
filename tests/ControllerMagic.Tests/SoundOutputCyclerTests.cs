using System.Runtime.InteropServices;
using Xunit;

namespace ControllerMagic.Tests;

public sealed class SoundOutputCyclerTests
{
    private static readonly SoundOutput Headphones = new("{h}", "Headphones (USB Audio)");
    private static readonly SoundOutput Monitor = new("{m}", "LG HDR 4K (NVIDIA High Definition Audio)");
    private static readonly SoundOutput Speakers = new("{s}", "speakers (Realtek Audio)");

    private readonly FakeSoundOutputs _outputs = new();
    private readonly SoundOutputCycler _cycler;

    public SoundOutputCyclerTests()
    {
        // Out of name order, as Windows lists them by device id.
        _outputs.Devices.AddRange([Speakers, Headphones, Monitor]);
        _cycler = new SoundOutputCycler(_outputs);
    }

    [Fact]
    public void Cycle_Next_SwitchesToTheFollowingOutputByName()
    {
        _outputs.Default = Headphones.Id;

        var chosen = _cycler.Cycle(CycleDirection.Next);

        Assert.Equal(Monitor, chosen);
        Assert.Equal(Monitor.Id, _outputs.Default);
    }

    [Fact]
    public void Cycle_Previous_SwitchesToTheOutputBeforeByName()
    {
        _outputs.Default = Speakers.Id;

        var chosen = _cycler.Cycle(CycleDirection.Previous);

        Assert.Equal(Monitor, chosen);
    }

    [Fact]
    public void Cycle_NextFromTheLast_WrapsToTheFirst()
    {
        _outputs.Default = Speakers.Id;

        Assert.Equal(Headphones, _cycler.Cycle(CycleDirection.Next));
    }

    [Fact]
    public void Cycle_PreviousFromTheFirst_WrapsToTheLast()
    {
        _outputs.Default = Headphones.Id;

        Assert.Equal(Speakers, _cycler.Cycle(CycleDirection.Previous));
    }

    [Fact]
    public void Cycle_RepeatedPresses_VisitEveryOutputOnce()
    {
        _outputs.Default = Headphones.Id;

        var visited = new[] { _cycler.Cycle(CycleDirection.Next), _cycler.Cycle(CycleDirection.Next), _cycler.Cycle(CycleDirection.Next) };

        Assert.Equal([Monitor, Speakers, Headphones], visited);
    }

    [Fact]
    public void Cycle_NoDefaultOutput_NextPicksTheFirst()
    {
        _outputs.Default = null;

        Assert.Equal(Headphones, _cycler.Cycle(CycleDirection.Next));
    }

    [Fact]
    public void Cycle_DefaultIsNotAConnectedOutput_PreviousPicksTheLast()
    {
        _outputs.Default = "{gone}";

        Assert.Equal(Speakers, _cycler.Cycle(CycleDirection.Previous));
    }

    [Fact]
    public void Cycle_OnlyOneOutput_ReportsItWithoutSettingIt()
    {
        _outputs.Devices.RemoveAll(o => o != Monitor);
        _outputs.Default = Monitor.Id;

        Assert.Equal(Monitor, _cycler.Cycle(CycleDirection.Next));
        Assert.Equal(0, _outputs.SetCalls);
    }

    [Fact]
    public void Cycle_NoOutputs_ReturnsNull()
    {
        _outputs.Devices.Clear();

        Assert.Null(_cycler.Cycle(CycleDirection.Next));
    }

    [Fact]
    public void Cycle_WindowsRefuses_ReturnsNull()
    {
        _outputs.Default = Headphones.Id;
        _outputs.RefuseSet = true;

        Assert.Null(_cycler.Cycle(CycleDirection.Next));
        Assert.Equal(Headphones.Id, _outputs.Default);
    }

    private sealed class FakeSoundOutputs : ISoundOutputs
    {
        public List<SoundOutput> Devices { get; } = [];
        public string? Default { get; set; }
        public bool RefuseSet { get; set; }
        public int SetCalls { get; private set; }

        public IReadOnlyList<SoundOutput> Active() => [.. Devices];

        public string? DefaultId() => Default;

        public void SetDefault(string id)
        {
            SetCalls++;
            if (RefuseSet)
                throw Marshal.GetExceptionForHR(unchecked((int)0x80070005))!; // E_ACCESSDENIED
            Default = id;
        }
    }
}
