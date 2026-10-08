namespace SAMERIDER.Core.Models;

public sealed class SpriteCell
{
    public required int X { get; set; }
    public required int Y { get; set; }
    public required string AssetPath { get; set; }
    public required PixelRect SourceRect { get; set; }
    public int OffsetX { get; set; }
    public int OffsetY { get; set; }
    public required string DisplayName { get; set; }
}
