using SAMERIDER.Core.Models;
using SAMERIDER.Core.Services;

namespace SAMERIDER.Core.Tests;

public sealed class CellSequenceServiceTests
{
    [Fact]
    public void PngSequenceResolvesMissingRowMajorFrameFromAssetGrid()
    {
        var project = Project(assets: [("imports/sheet.png", 25, 16)], cells: [Cell("imports/sheet.png", 0, 0, 8, 8, "sheet_01")]);
        var source = project.Cells[0];

        Assert.True(CellSequenceService.TryGetNeighbor(project, source, 1, out var next));
        Assert.Equal("imports/sheet.png", next!.AssetPath);
        Assert.Equal(new PixelRect(8, 0, 8, 8), next.SourceRect);
        Assert.Equal("sheet_02", next.DisplayName);
    }

    [Fact]
    public void PngGridUsesOriginalCellSizeAndExcludesTrimmedRemainder()
    {
        var project = Project(assets: [("imports/sheet.png", 19, 17)], cells: [Cell("imports/sheet.png", 0, 0, 8, 8, "sheet_01")]);

        Assert.True(CellSequenceService.TryGetNeighbor(project, project.Cells[0], 1, out var next));
        Assert.Equal(new PixelRect(8, 0, 8, 8), next!.SourceRect);
        Assert.False(CellSequenceService.TryGetNeighbor(project, project.Cells[0], -1, out _));
    }

    [Fact]
    public void PngGridUsesSourceRectDimensionsAfterCellCanvasResize()
    {
        var project = Project(assets: [("imports/sheet.png", 20, 10)], cells: [Cell("imports/sheet.png", 0, 0, 5, 5, "sheet_01")]);

        Assert.True(CellSequenceService.TryGetNeighbor(project, project.Cells[0], 1, out var next));
        Assert.Equal(new PixelRect(5, 0, 5, 5), next!.SourceRect);
    }

    [Fact]
    public void PngSequencePreservesDisplayNameNumberPadding()
    {
        var project = Project(assets: [("imports/impact.png", 40, 10)], cells: [Cell("imports/impact.png", 10, 0, 10, 10, "Impact_002")]);

        Assert.True(CellSequenceService.TryGetNeighbor(project, project.Cells[0], 1, out var next));
        Assert.Equal("Impact_003", next!.DisplayName);
    }

    [Fact]
    public void PngSequenceRejectsDisplayNumberThatDoesNotMatchSourceRect()
    {
        var project = Project(assets: [("imports/impact.png", 40, 10)], cells: [Cell("imports/impact.png", 10, 0, 10, 10, "Impact_003")]);

        Assert.False(CellSequenceService.TryGetNeighbor(project, project.Cells[0], 1, out _));
    }

    [Fact]
    public void PngGridUsesNewImportCellSizeAfterSheetResize()
    {
        // Sheet resize changes project dimensions while preserving existing source rectangles;
        // an import made afterward is sliced at the new cell size.
        var project = Project(assets: [("imports/new-sheet.png", 20, 10)], cells: [Cell("imports/new-sheet.png", 0, 0, 10, 10, "new-sheet_01")]);
        project.CellWidth = project.CellHeight = 10;
        project.OriginalCellWidth = project.OriginalCellHeight = 8;

        Assert.True(CellSequenceService.TryGetNeighbor(project, project.Cells[0], 1, out var next));
        Assert.Equal(new PixelRect(10, 0, 10, 10), next!.SourceRect);
    }

    [Fact]
    public void GifSequenceUsesAssetInventoryAndStopsAtBoundaries()
    {
        var project = Project(assets: [("imports/run_001.png", 8, 8), ("imports/run_002.png", 8, 8), ("imports/run_003.png", 8, 8)],
            cells: [Cell("imports/run_002.png", 0, 0, 8, 8)]);

        Assert.True(CellSequenceService.TryGetNeighbor(project, project.Cells[0], -1, out var previous));
        Assert.Equal("imports/run_001.png", previous!.AssetPath);
        Assert.True(CellSequenceService.TryGetNeighbor(project, project.Cells[0], 1, out var next));
        Assert.Equal("imports/run_003.png", next!.AssetPath);
        Assert.False(CellSequenceService.TryGetNeighbor(project, Cell("imports/run_001.png", 0, 0, 8, 8), -1, out _));
        Assert.False(CellSequenceService.TryGetNeighbor(project, Cell("imports/run_003.png", 0, 0, 8, 8), 1, out _));
    }

    [Fact]
    public void DifferentGifPrefixesAndNumberingGapsAreNotGrouped()
    {
        var assets = new[] { ("imports/run_001.png", 8, 8), ("imports/other_002.png", 8, 8), ("imports/run_003.png", 8, 8) };
        var project = Project(assets, [Cell("imports/run_001.png", 0, 0, 8, 8)]);

        Assert.False(CellSequenceService.TryGetNeighbor(project, project.Cells[0], 1, out _));
    }

    [Fact]
    public void GifMetadataKeepsRepeatedImportsAsSeparateSequencesAndSurvivesClone()
    {
        var project = new SpriteProject
        {
            Title = "test", CellWidth = 8, CellHeight = 8, Columns = 2, Rows = 1,
            Assets =
            [
                GifAsset("imports/run_001.png", "first", 1), GifAsset("imports/run_002.png", "first", 2),
                GifAsset("imports/run_001_2.png", "second", 1), GifAsset("imports/run_002_2.png", "second", 2)
            ],
            Cells = [Cell("imports/run_001_2.png", 0, 0, 8, 8)]
        };

        var cloned = project.DeepClone();
        Assert.Equal("second", cloned.Assets[2].SequenceId);
        Assert.Equal(1, cloned.Assets[2].SequenceIndex);
        Assert.True(CellSequenceService.TryGetNeighbor(cloned, cloned.Cells[0], 1, out var next));
        Assert.Equal("imports/run_002_2.png", next!.AssetPath);
        Assert.Equal("run_002", next.DisplayName);
    }

    [Fact]
    public void GifMetadataBoundaryDoesNotFallBackToAnUntaggedFilenameMatch()
    {
        var taggedFirst = GifAsset("imports/run_001.png", "batch-a", 1);
        var untaggedNext = new SpriteAsset { RelativePath = "imports/run_002.png", Width = 8, Height = 8 };
        var project = Project([("imports/run_001.png", 8, 8), ("imports/run_002.png", 8, 8)],
            [Cell("imports/run_001.png", 0, 0, 8, 8)]);
        project.Assets = [taggedFirst, untaggedNext];

        Assert.False(CellSequenceService.TryGetNeighbor(project, project.Cells[0], 1, out _));
    }

    [Fact]
    public void InvalidDirectionIsRejected()
    {
        var project = Project(assets: [("imports/sheet.png", 16, 8)], cells: [Cell("imports/sheet.png", 0, 0, 8, 8)]);
        Assert.Throws<ArgumentOutOfRangeException>(() => CellSequenceService.TryGetNeighbor(project, project.Cells[0], 0, out _));
    }

    private static SpriteProject Project((string Path, int Width, int Height)[] assets, SpriteCell[] cells) => new()
    {
        Title = "test", CellWidth = 8, CellHeight = 8, Columns = 2, Rows = 1,
        Assets = assets.Select(asset => new SpriteAsset { RelativePath = asset.Path, Width = asset.Width, Height = asset.Height }).ToList(),
        Cells = cells.ToList()
    };

    private static SpriteCell Cell(string path, int x, int y, int width, int height, string? displayName = null) => new()
    {
        X = 0, Y = 0, AssetPath = path, SourceRect = new PixelRect(x, y, width, height), DisplayName = displayName ?? Path.GetFileNameWithoutExtension(path)
    };

    private static SpriteAsset GifAsset(string path, string sequenceId, int index) => new()
    {
        RelativePath = path, Width = 8, Height = 8,
        SequenceId = sequenceId, SequenceIndex = index, SequenceDisplayName = $"run_{index:000}"
    };
}
