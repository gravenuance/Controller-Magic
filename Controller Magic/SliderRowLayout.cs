namespace ControllerMagic;

// Horizontal placement of one Settings slider row: name, slider, value, preview, left to right.
internal readonly record struct SliderRowColumns(int LabelX, int SliderX, int SliderWidth, int ReadoutX, int ReadoutWidth, int VizX);

internal static class SliderRowLayout
{
    // Laid out from the right, so the value and preview never move under a long name; only the slider gives way.
    public static SliderRowColumns Compute(int contentLeft, int contentWidth, int labelWidth, int readoutWidth, int vizWidth, int gap)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(contentWidth);
        ArgumentOutOfRangeException.ThrowIfNegative(labelWidth);
        ArgumentOutOfRangeException.ThrowIfNegative(readoutWidth);
        ArgumentOutOfRangeException.ThrowIfNegative(vizWidth);

        int vizX = contentLeft + contentWidth - vizWidth;
        int readoutX = vizX - gap - readoutWidth;
        int sliderX = contentLeft + labelWidth + gap;
        return new SliderRowColumns(contentLeft, sliderX, Math.Max(0, readoutX - gap - sliderX), readoutX, readoutWidth, vizX);
    }
}
