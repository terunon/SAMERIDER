namespace SAMERIDER.Core.Models;

public sealed class SpriteCell
{
    public required int X { get; set; }
    public required int Y { get; set; }
    public required string AssetPath { get; set; }
    public required PixelRect SourceRect { get; set; }
    public int OffsetX { get; set; }
    public int OffsetY { get; set; }
    public float Hue { get; set; }
    public float Saturation { get; set; }
    public float Brightness { get; set; }
    public float Contrast { get; set; }
    [System.Text.Json.Serialization.JsonIgnore]
    public bool HasColorAdjustment => Hue != 0 || Saturation != 0 || Brightness != 0 || Contrast != 0;
    public required string DisplayName { get; set; }
}
