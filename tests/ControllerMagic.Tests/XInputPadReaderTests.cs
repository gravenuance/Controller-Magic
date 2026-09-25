using ControllerMagic;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace ControllerMagic.Tests;

public class XInputPadReaderTests
{
    private readonly FakeTimeProvider _clock = new();
    private readonly bool[] _connected = new bool[4];
    private readonly int[] _reads = new int[4];
    private readonly XInputPadReader _reader;

    public XInputPadReaderTests()
    {
        _reader = new XInputPadReader(ReadSlot, _clock);
    }

    private bool ReadSlot(int slot, out PadState pad)
    {
        _reads[slot]++;
        pad = _connected[slot] ? new PadState { IsConnected = true, LeftThumbX = (short)(slot + 1) } : default;
        return _connected[slot];
    }

    [Fact]
    public void NoController_EmptySlotsAreProbedOncePerSecondNotEveryTick()
    {
        for (int tick = 0; tick < 100; tick++)
        {
            Assert.False(_reader.TryReadAny(out _));
            _clock.Advance(TimeSpan.FromMilliseconds(8));
        }

        // 800 ms of ticks: the first probe only.
        Assert.All(_reads, count => Assert.Equal(1, count));
    }

    [Fact]
    public void PadPluggedIntoAnEmptySlot_IsFoundWithinASecond()
    {
        _reader.TryReadAny(out _);
        _connected[2] = true;

        _clock.Advance(TimeSpan.FromMilliseconds(999));
        bool foundEarly = _reader.TryReadAny(out _);
        _clock.Advance(TimeSpan.FromMilliseconds(1));
        bool found = _reader.TryReadAny(out var pad);

        Assert.False(foundEarly);
        Assert.True(found);
        Assert.Equal(3, pad.LeftThumbX);
        Assert.Equal(2, _reader.LastSlot);
    }

    [Fact]
    public void ConnectedPad_IsReadEveryTickAndOtherSlotsAreLeftAlone()
    {
        _connected[1] = true;

        for (int tick = 0; tick < 50; tick++)
        {
            Assert.True(_reader.TryReadAny(out _));
            _clock.Advance(TimeSpan.FromSeconds(1));
        }

        Assert.Equal(50, _reads[1]);
        Assert.Equal(1, _reads[0]);
        Assert.Equal(0, _reads[2]);
        Assert.Equal(0, _reads[3]);
    }

    [Fact]
    public void PadLostFromItsSlot_IsNotifiedImmediately()
    {
        _connected[0] = true;
        Assert.True(_reader.TryReadAny(out _));

        _connected[0] = false;

        Assert.False(_reader.TryReadAny(out _));
    }

    [Fact]
    public void ExcludedSlot_IsNeverRead()
    {
        _connected[0] = true;
        _connected[3] = true;

        Assert.True(_reader.TryReadAny(out var pad, excludedSlots: 1 << 0));

        Assert.Equal(0, _reads[0]);
        Assert.Equal(4, pad.LeftThumbX);
        Assert.Equal(3, _reader.LastSlot);
    }

    [Fact]
    public void SlotNoLongerExcluded_IsReadStraightAway()
    {
        _connected[0] = true;
        _reader.TryReadAny(out _, excludedSlots: 1 << 0);

        Assert.True(_reader.TryReadAny(out _, excludedSlots: 0));
        Assert.Equal(0, _reader.LastSlot);
    }
}
