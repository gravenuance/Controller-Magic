using System.Drawing;
using ControllerMagic;
using Xunit;

namespace ControllerMagic.Tests;

public class InputEmulatorTests
{
    private const ushort VkShift = 0x10;
    private const ushort VkControl = 0x11;
    private const ushort VkLeft = 0x25;

    private readonly FakeDesktopInput _sink = new();
    private readonly InputEmulator _input;

    public InputEmulatorTests()
    {
        _input = new InputEmulator(_sink);
    }

    [Fact]
    public void SetLeftButtonState_RejectedPress_IsSentAgainNextTime()
    {
        _sink.Accept = _ => 0;
        _input.SetLeftButtonState(true);
        _sink.Accept = null;
        _input.SetLeftButtonState(true);

        Assert.Equal(2, _sink.Batches.Count);
        Assert.All(_sink.Batches, b => Assert.Equal(InputEmulator.MOUSEEVENTF_LEFTDOWN, b[0].U.mi.dwFlags));
    }

    [Fact]
    public void SetLeftButtonState_RejectedRelease_IsSentAgainNextTime()
    {
        _input.SetLeftButtonState(true);
        _sink.Accept = _ => 0;
        _input.SetLeftButtonState(false);
        _sink.Accept = null;
        _input.SetLeftButtonState(false);

        Assert.Equal(3, _sink.Batches.Count);
        Assert.Equal(InputEmulator.MOUSEEVENTF_LEFTUP, _sink.Batches[2][0].U.mi.dwFlags);
    }

    [Fact]
    public void SetLeftButtonState_AcceptedPress_IsNotRepeated()
    {
        _input.SetLeftButtonState(true);
        _input.SetLeftButtonState(true);

        Assert.Single(_sink.Batches);
    }

    [Fact]
    public void SendKeyWithModifier_SendsModifierAndKeyAsOneBatch()
    {
        _input.SendKeyWithModifier(VkShift, VkLeft);

        var batch = Assert.Single(_sink.Batches);
        Assert.Collection(
            batch,
            i => AssertKey(i, VkShift, up: false),
            i => AssertKey(i, VkLeft, up: false),
            i => AssertKey(i, VkLeft, up: true),
            i => AssertKey(i, VkShift, up: true));
    }

    [Fact]
    public void LeftClickWithModifier_SendsModifierAndClickAsOneBatch()
    {
        _input.LeftClickWithModifier(VkControl);

        var batch = Assert.Single(_sink.Batches);
        Assert.Collection(
            batch,
            i => AssertKey(i, VkControl, up: false),
            i => AssertMouse(i, InputEmulator.MOUSEEVENTF_LEFTDOWN),
            i => AssertMouse(i, InputEmulator.MOUSEEVENTF_LEFTUP),
            i => AssertKey(i, VkControl, up: true));
    }

    [Fact]
    public void MouseWheelVertical_Downward_SendsTheNegativeDelta()
    {
        _input.MouseWheelVertical(-120);

        var input = Assert.Single(Assert.Single(_sink.Batches));
        AssertMouse(input, InputEmulator.MOUSEEVENTF_WHEEL);
        Assert.Equal(-120, unchecked((int)input.U.mi.mouseData));
    }

    [Fact]
    public void Input_HasTheNativeLayoutSendInputChecks()
    {
        // SendInput rejects every event when cbSize isn't sizeof(INPUT): 40 bytes on x64, 28 on x86.
        Assert.Equal(IntPtr.Size == 8 ? 40 : 28, System.Runtime.InteropServices.Marshal.SizeOf<INPUT>());
    }

    // Regression: absolute SendInput moves landed 1px up/left most of the time, and each move
    // started from there, so a slight push right crept upwards.
    [Fact]
    public void MoveMouse_PutsTheCursorExactlyOnTheCursorPlusTheStep()
    {
        _sink.CursorPosition = new Point(500, 300);

        _input.MoveMouse(1, -1);

        Assert.Equal([new Point(501, 299)], _sink.CursorPositionsSet);
        Assert.Empty(_sink.Batches);
    }

    [Fact]
    public void MoveMouse_PastTheVirtualScreensEdge_StopsAtTheEdge()
    {
        _sink.VirtualScreen = new Rectangle(-1920, -200, 3840, 1280);
        _sink.CursorPosition = new Point(1915, -195);

        _input.MoveMouse(50, -50);

        Assert.Equal([new Point(1919, -200)], _sink.CursorPositionsSet);
    }

    [Fact]
    public void MoveMouse_KeepsTheDisplayAwake()
    {
        _input.MoveMouse(3, 4);

        Assert.Equal(1, _sink.KeepDisplayAwakeCalls);
    }

    private static void AssertKey(INPUT input, ushort vk, bool up)
    {
        Assert.Equal(InputEmulator.INPUT_KEYBOARD, input.type);
        Assert.Equal(vk, input.U.ki.wVk);
        Assert.Equal(up ? InputEmulator.KEYEVENTF_KEYUP : 0u, input.U.ki.dwFlags);
    }

    private static void AssertMouse(INPUT input, uint flags)
    {
        Assert.Equal(InputEmulator.INPUT_MOUSE, input.type);
        Assert.Equal(flags, input.U.mi.dwFlags);
    }
}
