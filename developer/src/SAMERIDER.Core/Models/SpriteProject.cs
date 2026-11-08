namespace SAMERIDER.Core.Models;

public sealed class SpriteProject
{
    private string? _importFolderName;

    public const int CurrentFormatVersion = 3;

    public int FormatVersion { get; set; } = CurrentFormatVersion;
    public required string Title { get; set; } = "SAMERIDER";
    public string ImportFolderName
    {
        get => string.IsNullOrEmpty(_importFolderName) ? Title : _importFolderName;
        set => _importFolderName = value;
    }
    public int CellWidth { get; set; } = 64;
    public int CellHeight { get; set; } = 64;
    public int OriginalCellWidth { get; set; }
    public int OriginalCellHeight { get; set; }
    [System.Text.Json.Serialization.JsonIgnore]
    public bool HasSheetResize => OriginalCellWidth > 0 && OriginalCellHeight > 0;
    public int Columns { get; set; } = 8;
    public int Rows { get; set; } = 4;
    public List<SpriteAsset> Assets { get; set; } = [];
    public List<SpriteCell> Cells { get; set; } = [];

    public SpriteProject DeepClone() => new()
    {
        FormatVersion = FormatVersion,
        Title = Title,
        ImportFolderName = ImportFolderName,
        CellWidth = CellWidth,
        CellHeight = CellHeight,
        OriginalCellWidth = OriginalCellWidth,
        OriginalCellHeight = OriginalCellHeight,
        Columns = Columns,
        Rows = Rows,
        Assets = Assets.Select(asset => new SpriteAsset
        {
            RelativePath = asset.RelativePath,
            Width = asset.Width,
            Height = asset.Height,
            SequenceId = asset.SequenceId,
            SequenceIndex = asset.SequenceIndex,
            SequenceDisplayName = asset.SequenceDisplayName
        }).ToList(),
        Cells = Cells.Select(cell => new SpriteCell
        {
            X = cell.X,
            Y = cell.Y,
            AssetPath = cell.AssetPath,
            SourceRect = new PixelRect(cell.SourceRect.X, cell.SourceRect.Y, cell.SourceRect.Width, cell.SourceRect.Height),
            SourceClipRect = cell.SourceClipRect is { } sourceClipRect
                ? new PixelRect(sourceClipRect.X, sourceClipRect.Y, sourceClipRect.Width, sourceClipRect.Height)
                : null,
            OffsetX = cell.OffsetX,
            OffsetY = cell.OffsetY,
            Hue = cell.Hue, Saturation = cell.Saturation, Brightness = cell.Brightness, Contrast = cell.Contrast,
            ColorInversions = cell.ColorInversions,
            DisplayName = cell.DisplayName
        }).ToList()
    };
}
