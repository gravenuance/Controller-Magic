using ControllerMagic;

namespace ControllerMagic.Tests;

// Records each SendInput batch; Accept decides how many events of the next batches Windows "inserts".
internal sealed class RecordingInputSink : IInputSink
{
    public List<INPUT[]> Batches { get; } = [];

    public Func<int, uint>? Accept { get; set; }

    public uint Send(ReadOnlySpan<INPUT> inputs)
    {
        Batches.Add(inputs.ToArray());
        return Accept?.Invoke(inputs.Length) ?? (uint)inputs.Length;
    }
}
