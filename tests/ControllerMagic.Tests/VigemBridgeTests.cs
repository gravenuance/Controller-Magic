using ControllerMagic;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.Xbox360;
using Nefarius.ViGEm.Client.Targets.Xbox360.Exceptions;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace ControllerMagic.Tests;

public class VigemBridgeTests
{
    private const int SlotsInUseBeforeConnect = 0b0001;

    private readonly List<string> _events = [];
    private readonly FakeTimeProvider _clock = new();

    private VigemBridge CreateBridge(FakeXbox360Controller controller) =>
        new(() => new FakeClient(_events), _ => controller, () => SlotsInUseBeforeConnect, _clock);

    [Fact]
    public void TryConnect_ConnectThrows_ReleasesTheControllerAndTheClient()
    {
        using var controller = new FakeXbox360Controller(_events) { ConnectThrows = true };
        using var bridge = CreateBridge(controller);

        Assert.False(bridge.TryConnect());

        Assert.Equal(["controller.Dispose", "client.Dispose"], _events);
        Assert.False(bridge.IsConnected);
    }

    [Fact]
    public void TryConnect_CreatingTheControllerThrows_ReleasesTheClient()
    {
        using var bridge = new VigemBridge(
            () => new FakeClient(_events), _ => throw new InvalidOperationException("no target"),
            () => SlotsInUseBeforeConnect, _clock);

        Assert.False(bridge.TryConnect());

        Assert.Equal(["client.Dispose"], _events);
    }

    [Fact]
    public void Disconnect_ReleasesTheControllerBeforeItsClient()
    {
        using var controller = new FakeXbox360Controller(_events);
        using var bridge = CreateBridge(controller);
        Assert.True(bridge.TryConnect());

        bridge.Disconnect();

        Assert.Equal(["controller.Connect", "controller.Disconnect", "controller.Dispose", "client.Dispose"], _events);
        Assert.False(bridge.IsConnected);
    }

    [Fact]
    public void SubmitReport_SubmitThrows_DisconnectsAndReleasesEverything()
    {
        using var controller = new FakeXbox360Controller(_events) { SubmitThrows = true };
        using var bridge = CreateBridge(controller);
        bridge.TryConnect();

        bridge.SubmitReport(default, includeStickAndDpad: false);

        Assert.False(bridge.IsConnected);
        Assert.Contains("controller.Dispose", _events);
        Assert.Contains("client.Dispose", _events);
    }

    [Fact]
    public void SubmitReport_SendsTheButtonsAsTheReportsBitMask()
    {
        using var controller = new FakeXbox360Controller(_events);
        using var bridge = CreateBridge(controller);
        bridge.TryConnect();

        bridge.SubmitReport(new PadState { Buttons = PadButtons.A | PadButtons.Start });

        Assert.Equal(Xbox360Button.A.Value | Xbox360Button.Start.Value, controller.SubmittedButtons);
    }

    [Fact]
    public void SubmitReport_UnchangedReport_IsNotSentAgain()
    {
        using var controller = new FakeXbox360Controller(_events);
        using var bridge = CreateBridge(controller);
        bridge.TryConnect();
        var pad = new PadState { Buttons = PadButtons.A, RightThumbX = 1234 };

        bridge.SubmitReport(pad);
        bridge.SubmitReport(pad);

        Assert.Equal(1, controller.Submits);
    }

    [Fact]
    public void SubmitReport_ChangedReport_IsSent()
    {
        using var controller = new FakeXbox360Controller(_events);
        using var bridge = CreateBridge(controller);
        bridge.TryConnect();

        bridge.SubmitReport(new PadState { RightTrigger = 10 });
        bridge.SubmitReport(new PadState { RightTrigger = 11 });

        Assert.Equal(2, controller.Submits);
    }

    [Fact]
    public void SubmitReport_OnlyTheNeutralisedLeftStickMoved_IsNotSentAgain()
    {
        using var controller = new FakeXbox360Controller(_events);
        using var bridge = CreateBridge(controller);
        bridge.TryConnect();

        bridge.SubmitReport(new PadState { LeftThumbX = 100 }, includeStickAndDpad: false);
        bridge.SubmitReport(new PadState { LeftThumbX = 20000 }, includeStickAndDpad: false);

        Assert.Equal(1, controller.Submits);
        Assert.Equal(0, controller.SubmittedLeftThumbX);
    }

    [Fact]
    public void SubmitReport_SameStickOnceNeutralisedAndOnceNot_IsSentBothTimes()
    {
        using var controller = new FakeXbox360Controller(_events);
        using var bridge = CreateBridge(controller);
        bridge.TryConnect();
        var pad = new PadState { LeftThumbX = 20000 };

        bridge.SubmitReport(pad, includeStickAndDpad: false);
        bridge.SubmitReport(pad, includeStickAndDpad: true);

        Assert.Equal(2, controller.Submits);
        Assert.Equal(20000, controller.SubmittedLeftThumbX);
    }

    [Fact]
    public void SubmitReport_AfterReconnecting_SendsTheFirstReportEvenIfUnchanged()
    {
        using var controller = new FakeXbox360Controller(_events);
        using var bridge = CreateBridge(controller);
        bridge.TryConnect();
        bridge.SubmitReport(default);
        bridge.Disconnect();

        bridge.TryConnect();
        bridge.SubmitReport(default);

        Assert.Equal(2, controller.Submits);
    }

    [Fact]
    public void ExcludedXInputSlots_NotConnected_ExcludesNothing()
    {
        using var controller = new FakeXbox360Controller(_events);
        using var bridge = CreateBridge(controller);

        Assert.Equal(0, bridge.ExcludedXInputSlots);
    }

    [Fact]
    public void ExcludedXInputSlots_BeforeViGEmReportsTheSlot_ExcludesEverySlotThatWasFreeBeforeConnecting()
    {
        using var controller = new FakeXbox360Controller(_events) { ReadUserIndex = () => throw new Xbox360UserIndexNotReportedException() };
        using var bridge = CreateBridge(controller);
        bridge.TryConnect();

        Assert.Equal(0b1110, bridge.ExcludedXInputSlots);
    }

    [Fact]
    public void ExcludedXInputSlots_OnceViGEmReportsTheSlot_ExcludesJustThatSlot()
    {
        using var controller = new FakeXbox360Controller(_events) { ReadUserIndex = () => 2 };
        using var bridge = CreateBridge(controller);
        bridge.TryConnect();

        Assert.Equal(0b0100, bridge.ExcludedXInputSlots);
    }

    [Fact]
    public void ExcludedXInputSlots_SlotNotYetReported_AsksViGEmAtALowRateNotEveryCall()
    {
        using var controller = new FakeXbox360Controller(_events) { ReadUserIndex = () => throw new Xbox360UserIndexNotReportedException() };
        using var bridge = CreateBridge(controller);
        bridge.TryConnect();

        for (int i = 0; i < 100; i++)
            _ = bridge.ExcludedXInputSlots;
        Assert.Equal(1, controller.UserIndexReads);

        _clock.Advance(TimeSpan.FromMilliseconds(100));
        _ = bridge.ExcludedXInputSlots;
        Assert.Equal(2, controller.UserIndexReads);
    }

    [Fact]
    public void ExcludedXInputSlots_SlotReported_IsRememberedWithoutAskingAgain()
    {
        using var controller = new FakeXbox360Controller(_events) { ReadUserIndex = () => 1 };
        using var bridge = CreateBridge(controller);
        bridge.TryConnect();

        for (int i = 0; i < 100; i++)
        {
            _clock.Advance(TimeSpan.FromSeconds(1));
            _ = bridge.ExcludedXInputSlots;
        }

        Assert.Equal(1, controller.UserIndexReads);
    }
}

internal sealed class FakeClient(List<string> events) : IDisposable
{
    public void Dispose() => events.Add("client.Dispose");
}

internal sealed class FakeXbox360Controller(List<string> events) : IXbox360Controller, IDisposable
{
    private byte _leftTrigger;
    private byte _rightTrigger;
    private short _leftThumbX;
    private short _leftThumbY;
    private short _rightThumbX;
    private short _rightThumbY;
    private ushort _buttonState;

    public bool ConnectThrows { get; set; }
    public bool SubmitThrows { get; set; }
    public Func<int> ReadUserIndex { get; set; } = () => 0;
    public int UserIndexReads { get; private set; }

    public int UserIndex
    {
        get
        {
            UserIndexReads++;
            return ReadUserIndex();
        }
    }

    public ref byte LeftTrigger => ref _leftTrigger;
    public ref byte RightTrigger => ref _rightTrigger;
    public ref short LeftThumbX => ref _leftThumbX;
    public ref short LeftThumbY => ref _leftThumbY;
    public ref short RightThumbX => ref _rightThumbX;
    public ref short RightThumbY => ref _rightThumbY;
    public ref ushort ButtonState => ref _buttonState;

    public int ButtonCount => 0;
    public int AxisCount => 0;
    public int SliderCount => 0;
    public bool AutoSubmitReport { get; set; }

    public event Xbox360FeedbackReceivedEventHandler? FeedbackReceived
    {
        add { }
        remove { }
    }

    public void Connect()
    {
        if (ConnectThrows)
            throw new InvalidOperationException("connect failed");
        events.Add("controller.Connect");
    }

    public void Disconnect() => events.Add("controller.Disconnect");

    public void Dispose() => events.Add("controller.Dispose");

    public int Submits { get; private set; }

    public ushort SubmittedButtons { get; private set; }

    public short SubmittedLeftThumbX { get; private set; }

    public void SubmitReport()
    {
        if (SubmitThrows)
            throw new InvalidOperationException("submit failed");
        Submits++;
        SubmittedButtons = _buttonState;
        SubmittedLeftThumbX = _leftThumbX;
    }

    public void SetButtonState(Xbox360Button button, bool pressed)
    {
    }

    public void SetAxisValue(Xbox360Axis axis, short value)
    {
        if (axis == Xbox360Axis.LeftThumbX)
            _leftThumbX = value;
    }

    public void SetSliderValue(Xbox360Slider slider, byte value)
    {
    }

    public void SetButtonsFull(ushort buttons) => _buttonState = buttons;

    public void SetButtonState(int index, bool pressed)
    {
    }

    public void SetAxisValue(int index, short value)
    {
    }

    public void SetSliderValue(int index, byte value)
    {
    }

    public void ResetReport()
    {
    }
}
