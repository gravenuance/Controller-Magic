namespace ControllerMagic;

// Everything the keyboard overlay's picture depends on.
internal readonly record struct OverlayFrame(bool KeyboardMode, int Layer, int Sector, int Slot)
{
    public static OverlayFrame Of(ControllerPoller poller) =>
        new(poller.KeyboardMode, poller.KeyboardLayer, poller.CurrentSector, poller.SlotIndex);
}

// Where the wheel's tiles sit, shared by painting and by working out what a change redraws.
internal static class WheelTiles
{
    private const float TileSize = 40f;
    private const float BaseRadius = 120f; // a bit farther out
    private const float RingGap = 4f;

    // Room around a tile for its outline and antialiased edge.
    public const int Margin = 3;

    public static RectangleF Bounds(int sector, int index, float cx, float cy)
    {
        double angleRad = (90.0 + sector * 45.0) * Math.PI / 180.0;

        float inner = BaseRadius + index * (TileSize + RingGap);
        float outer = inner + TileSize;
        float midR = (inner + outer) / 2f;

        float x = cx + (float)(midR * Math.Cos(angleRad));
        float y = cy - (float)(midR * Math.Sin(angleRad));

        return new RectangleF(x - TileSize / 2f, y - TileSize / 2f, TileSize, TileSize);
    }
}

// Repainting the 500x500 layered overlay is costly, so the frame timer only asks for one when
// the displayed state differs from what was last painted.
internal sealed class OverlayRepaintGate
{
    private OverlayFrame? _painted;

    public bool NeedsRepaint(OverlayFrame current) => _painted != current;

    public void Painted(OverlayFrame frame) => _painted = frame;

    // What a repaint to `current` redraws: null for the whole window, else the highlighted tile
    // that lost the highlight and the one that gained it (none when nothing visible moved).
    public IReadOnlyList<Rectangle>? DirtyAreas(OverlayFrame current, float cx, float cy)
    {
        if (_painted is not { } previous
            || !current.KeyboardMode
            || previous.KeyboardMode != current.KeyboardMode
            || previous.Layer != current.Layer)
            return null;

        var areas = new List<Rectangle>(2);
        AddHotTile(areas, previous, cx, cy);
        AddHotTile(areas, current, cx, cy);
        return areas;
    }

    private static void AddHotTile(List<Rectangle> areas, OverlayFrame frame, float cx, float cy)
    {
        if (frame.Sector < 0)
            return;

        var tile = WheelTiles.Bounds(frame.Sector, frame.Slot, cx, cy);
        var area = Rectangle.FromLTRB(
            (int)Math.Floor(tile.Left) - WheelTiles.Margin,
            (int)Math.Floor(tile.Top) - WheelTiles.Margin,
            (int)Math.Ceiling(tile.Right) + WheelTiles.Margin,
            (int)Math.Ceiling(tile.Bottom) + WheelTiles.Margin);
        if (!areas.Contains(area))
            areas.Add(area);
    }
}
