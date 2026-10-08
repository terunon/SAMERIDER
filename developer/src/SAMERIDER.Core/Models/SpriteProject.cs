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
        Columns = Columns,
        Rows = Rows,
        Assets = Assets.Select(asset => new SpriteAsset
        {
            RelativePath = asset.RelativePath,
            Width = asset.Width,
            Height = asset.Height
        }).ToList(),
        Cells = Cells.Select(cell => new SpriteCell
        {
            X = cell.X,
            Y = cell.Y,
            AssetPath = cell.AssetPath,
            SourceRect = new PixelRect(cell.SourceRect.X, cell.SourceRect.Y, cell.SourceRect.Width, cell.SourceRect.Height),
            OffsetX = cell.OffsetX,
            OffsetY = cell.OffsetY,
            DisplayName = cell.DisplayName
        }).ToList()
    };
}
