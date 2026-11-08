namespace SAMERIDER.Core.Models;

public sealed class SpriteAsset
{
    public required string RelativePath { get; set; }
    public required int Width { get; set; }
    public required int Height { get; set; }
    public string? SequenceId { get; set; }
    public int? SequenceIndex { get; set; }
    public string? SequenceDisplayName { get; set; }
}
