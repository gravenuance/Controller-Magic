namespace ControllerMagic;

// Buttons still held when input resumes after a pause belong to whatever had the pad meanwhile, so
// each stays hidden until it's been let go once - otherwise resuming reads it as a fresh press.
internal sealed class HeldButtonGate
{
    private bool _capturePending;
    private PadButtons _suppressed;

    public void SuppressHeld() => _capturePending = true;

    public PadButtons Filter(PadButtons raw)
    {
        if (_capturePending)
        {
            _capturePending = false;
            _suppressed = raw;
        }
        else
        {
            _suppressed &= raw;
        }

        return raw & ~_suppressed;
    }
}
