using ControllerMagic;
using Xunit;

namespace ControllerMagic.Tests;

public class VolumeStepperTests
{
    // Windows' default step is 2%.
    private sealed class FakeVolume : ISystemVolume
    {
        public float Scalar { get; set; } = 0.5f;
        public bool Muted { get; set; }
        public Exception? Fail { get; set; }
        public int OpenEndpoints { get; set; }

        public IVolumeEndpoint Open()
        {
            OpenEndpoints++;
            return new FakeEndpoint(this);
        }
    }

    private sealed class FakeEndpoint(FakeVolume volume) : IVolumeEndpoint
    {
        public void Step(VolumeStep step)
        {
            if (volume.Fail is not null)
                throw volume.Fail;
            volume.Scalar = Math.Clamp(volume.Scalar + (step == VolumeStep.Up ? 0.02f : -0.02f), 0f, 1f);
        }

        public bool IsMuted() => volume.Muted;

        public void SetMuted(bool muted) => volume.Muted = muted;

        public float Level() => volume.Scalar;

        public void Dispose() => volume.OpenEndpoints--;
    }

    private static List<VolumeLevel?> StepAndCollect(FakeVolume volume, VolumeStep step)
    {
        var reported = new List<VolumeLevel?>();
        new VolumeStepper(volume).Step(step, reported.Add);
        return reported;
    }

    [Theory]
    [InlineData(VolumeStep.Up, 52)]
    [InlineData(VolumeStep.Down, 48)]
    internal void Step_ReportsTheNewLevelOnceAndReleasesTheEndpoint(VolumeStep step, int expectedPercent)
    {
        var volume = new FakeVolume();

        var reported = StepAndCollect(volume, step);

        Assert.Equal([new VolumeLevel(expectedPercent, Muted: false)], reported);
        Assert.Equal(0, volume.OpenEndpoints);
    }

    // As the keyboard volume-up key does.
    [Fact]
    public void Step_UpWhileMuted_Unmutes()
    {
        var volume = new FakeVolume { Muted = true };

        var reported = StepAndCollect(volume, VolumeStep.Up);

        Assert.False(volume.Muted);
        Assert.Equal([new VolumeLevel(52, Muted: false)], reported);
    }

    [Fact]
    public void Step_DownWhileMuted_StaysMuted()
    {
        var reported = StepAndCollect(new FakeVolume { Muted = true }, VolumeStep.Down);

        Assert.Equal([new VolumeLevel(48, Muted: true)], reported);
    }

    [Fact]
    public void Step_WindowsRefuses_ReportsNullAndReleasesTheEndpoint()
    {
        var volume = new FakeVolume { Fail = new UnauthorizedAccessException("refused") };

        var reported = StepAndCollect(volume, VolumeStep.Up);

        Assert.Equal([null], reported);
        Assert.Equal(0, volume.OpenEndpoints);
    }

    [Fact]
    public void Step_NoPlaybackDevice_ReportsNull()
    {
        var volume = new FakeVolume { Fail = new InvalidOperationException("no default playback device") };

        Assert.Equal([null], StepAndCollect(volume, VolumeStep.Down));
    }

    // Regression: the level was reported after the lock, so overlapping repeats could show an older level last.
    [Fact]
    public async Task Step_ANewStepWaitsUntilThePreviousOneHasReported()
    {
        var stepper = new VolumeStepper(new FakeVolume());
        var order = new List<int>();
        var firstReporting = new TaskCompletionSource();
        var releaseFirst = new TaskCompletionSource();
        var ct = TestContext.Current.CancellationToken;

        var first = Task.Run(() => stepper.Step(VolumeStep.Up, level =>
        {
            firstReporting.SetResult();
            releaseFirst.Task.Wait(ct);
            lock (order) order.Add(level!.Value.Percent);
        }), ct);
        await firstReporting.Task.WaitAsync(ct);
        var second = Task.Run(() => stepper.Step(VolumeStep.Up, level => { lock (order) order.Add(level!.Value.Percent); }), ct);
        await Task.Delay(100, ct);
        bool secondFinishedEarly = second.IsCompleted;
        releaseFirst.SetResult();
        await Task.WhenAll(first, second).WaitAsync(ct);

        Assert.False(secondFinishedEarly);
        Assert.Equal([52, 54], order);
    }

    [Theory]
    [InlineData(0, false, SoundToast.Volume0Glyph, "Volume 0%")]
    [InlineData(1, false, SoundToast.Volume1Glyph, "Volume 1%")]
    [InlineData(33, false, SoundToast.Volume1Glyph, "Volume 33%")]
    [InlineData(34, false, SoundToast.Volume2Glyph, "Volume 34%")]
    [InlineData(66, false, SoundToast.Volume2Glyph, "Volume 66%")]
    [InlineData(67, false, SoundToast.Volume3Glyph, "Volume 67%")]
    [InlineData(100, false, SoundToast.Volume3Glyph, "Volume 100%")]
    [InlineData(40, true, SoundToast.MutedGlyph, "Muted (40%)")]
    public void VolumeCue_ShowsTheLevelAndAMatchingSpeaker(int percent, bool muted, string glyph, string text)
    {
        var level = new VolumeLevel(percent, muted);

        Assert.Equal(glyph, SoundToast.VolumeGlyph(level));
        Assert.Equal(text, SoundToast.VolumeText(level));
    }

    [Fact]
    public void VolumeCue_Failure_SaysSoWithAPlainSpeaker()
    {
        Assert.Equal(SoundToast.SpeakerGlyph, SoundToast.VolumeGlyph(null));
        Assert.Equal("Couldn't change the volume", SoundToast.VolumeText(null));
    }
}
