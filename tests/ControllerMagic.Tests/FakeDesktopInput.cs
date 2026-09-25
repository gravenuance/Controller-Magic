using System.Drawing;
using ControllerMagic;

namespace ControllerMagic.Tests;

// Records each SendInput batch; Accept decides how many events of the next batches Windows "inserts".
internal sealed class FakeDesktopInput : IDesktopInput
{
    public List<INPUT[]> Batches { get; } = [];

    public Func<int, uint>? Accept { get; set; }

    public Point CursorPosition { get; set; }

    public Rectangle VirtualScreen { get; set; } = new(0, 0, 1920, 1080);

    public List<Point> CursorPositionsSet { get; } = [];

    public uint Send(ReadOnlySpan<INPUT> inputs)
    {
        Batches.Add(inputs.ToArray());
        return Accept?.Invoke(inputs.Length) ?? (uint)inputs.Length;
    }

    public void SetCursorPosition(Point position) => CursorPositionsSet.Add(position);
}
