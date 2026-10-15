using SkiaSharp;
using SAMERIDER.Core.Models;
using SAMERIDER.Core.Services;

namespace SAMERIDER.Core.Tests;

public sealed class SpriteImageServiceTests
{
    [Theory]
    [MemberData(nameof(Anchors))]
    public void ResizeCropUsesAllNineAnchors(ResizeAnchor anchor, int expectedX, int expectedY)
    {
        var project = ProjectWithCell(120, 120, 2, 2, new PixelRect(10, 20, 120, 120));
        SpriteImageService.ResizeCells(project, 100, 100, anchor);
        Assert.Equal(new PixelRect(10 + expectedX, 20 + expectedY, 100, 100), project.Cells[0].SourceRect);
    }

    [Theory]
    [MemberData(nameof(Anchors))]
    public void ResizePaddingUsesAllNineAnchors(ResizeAnchor anchor, int expectedX, int expectedY)
    {
        var project = ProjectWithCell(100, 100, 2, 2, new PixelRect(10, 20, 100, 100));
        SpriteImageService.ResizeCells(project, 120, 120, anchor);
        Assert.Equal(new PixelRect(10 - expectedX, 20 - expectedY, 120, 120), project.Cells[0].SourceRect);
    }

    public static IEnumerable<object[]> Anchors()
    {
        yield return [ResizeAnchor.TopLeft, 0, 0];
        yield return [ResizeAnchor.Top, 10, 0];
        yield return [ResizeAnchor.TopRight, 20, 0];
        yield return [ResizeAnchor.Left, 0, 10];
        yield return [ResizeAnchor.Center, 10, 10];
        yield return [ResizeAnchor.Right, 20, 10];
        yield return [ResizeAnchor.BottomLeft, 0, 20];
        yield return [ResizeAnchor.Bottom, 10, 20];
        yield return [ResizeAnchor.BottomRight, 20, 20];
    }

    [Fact]
    public void ImportSplitsIntegerSizedSheetAndKeepsAlphaAndNames()
    {
        using var workspace = new TemporaryDirectory();
        var source = Path.Combine(workspace.Path, "walk.png");
        using (var bitmap = new SKBitmap(8, 4))
        {
            bitmap.Erase(SKColors.Transparent);
            bitmap.SetPixel(0, 0, SKColors.Blue);
            using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
            File.WriteAllBytes(source, data.ToArray());
        }
        var project = new SpriteProject { Title = "walk", CellWidth = 4, CellHeight = 2, Columns = 4, Rows = 2 };
        var result = SpriteImageService.ImportPng(source, workspace.Path, project, 1, 0);

        Assert.Equal(4, result.Cells.Count);
        Assert.Equal(new[] { "walk_01", "walk_02", "walk_03", "walk_04" }, result.Cells.Select(cell => cell.DisplayName));
        Assert.Equal((4, 2), (result.RequiredColumns, result.RequiredRows));
        Assert.True(File.Exists(Path.Combine(workspace.Path, ProjectValidator.ImportRootDirectoryName, "walk", "walk.png")));
        using var rendered = SpriteImageService.RenderCell(project, result.Cells[0], workspace.Path);
        Assert.Equal(SKColors.Blue, rendered.GetPixel(0, 0));
        Assert.Equal((byte)0, rendered.GetPixel(1, 0).Alpha);
    }

    [Fact]
    public void ImportRejectsNonIntegerImageSizeAndNonAsciiName()
    {
        using var workspace = new TemporaryDirectory();
        var project = new SpriteProject { Title = "safe", CellWidth = 4, CellHeight = 4, Columns = 4, Rows = 4 };
        var wrongSize = WritePng(workspace.Path, "wrong.png", 5, 4);
        Assert.Throws<InvalidDataException>(() => SpriteImageService.ImportPng(wrongSize, workspace.Path, project, 0, 0));
        var nonAscii = WritePng(workspace.Path, "キャラ.png", 4, 4);
        Assert.Throws<InvalidDataException>(() => SpriteImageService.ImportPng(nonAscii, workspace.Path, project, 0, 0));
        var single = WritePng(workspace.Path, "idle.png", 4, 4);
        Assert.Equal("idle.png", SpriteImageService.ImportPng(single, workspace.Path, project, 0, 0).Cells[0].DisplayName);
    }

    [Fact]
    public void OffsetIsClippedAndAppliedInTheExpectedDirection()
    {
        using var workspace = new TemporaryDirectory();
        var path = Path.Combine(workspace.Path, "assets", "one.png"); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using (var source = new SKBitmap(2, 1))
        {
            source.SetPixel(0, 0, SKColors.Blue); source.SetPixel(1, 0, SKColors.Red);
            using var data = source.Encode(SKEncodedImageFormat.Png, 100); File.WriteAllBytes(path, data.ToArray());
        }
        var project = new SpriteProject
        {
            Title = "offset", CellWidth = 2, CellHeight = 1, Columns = 1, Rows = 1,
            Assets = [new SpriteAsset { RelativePath = "assets/one.png", Width = 2, Height = 1 }],
            Cells = [new SpriteCell { X = 0, Y = 0, AssetPath = "assets/one.png", SourceRect = new PixelRect(0, 0, 2, 1), DisplayName = "one", OffsetX = 1 }]
        };
        using var rendered = SpriteImageService.Export(project, workspace.Path);
        Assert.Equal((byte)0, rendered.GetPixel(0, 0).Alpha);
        Assert.Equal(SKColors.Blue, rendered.GetPixel(1, 0));
    }

    [Fact]
    public void FindCommonOpaqueBoundsHonorsAlphaThreshold()
    {
        using var workspace = new TemporaryDirectory();
        var path = Path.Combine(workspace.Path, "assets", "alpha.png");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using (var source = new SKBitmap(2, 2))
        {
            source.Erase(SKColors.Transparent);
            source.SetPixel(1, 0, new SKColor(255, 0, 0, 5));
            source.SetPixel(0, 1, SKColors.Blue);
            using var data = source.Encode(SKEncodedImageFormat.Png, 100);
            File.WriteAllBytes(path, data.ToArray());
        }
        var project = new SpriteProject
        {
            Title = "alpha", CellWidth = 2, CellHeight = 2, Columns = 1, Rows = 1,
            Assets = [new SpriteAsset { RelativePath = "assets/alpha.png", Width = 2, Height = 2 }],
            Cells = [new SpriteCell { X = 0, Y = 0, AssetPath = "assets/alpha.png", SourceRect = new PixelRect(0, 0, 2, 2), DisplayName = "alpha" }]
        };

        Assert.Equal(new PixelRect(0, 0, 2, 2), SpriteImageService.FindCommonOpaqueBounds(project, workspace.Path));
        Assert.Equal(new PixelRect(0, 1, 1, 1), SpriteImageService.FindCommonOpaqueBounds(project, workspace.Path, alphaThreshold: 5));
        Assert.Throws<ArgumentOutOfRangeException>(() => SpriteImageService.FindCommonOpaqueBounds(project, workspace.Path, alphaThreshold: 256));
    }

    [Fact]
    public void RemoveLowAlphaPixelsClearsPixelsBelowThresholdOnly()
    {
        using var source = new SKBitmap(3, 1, SKColorType.Bgra8888, SKAlphaType.Premul);
        source.SetPixel(0, 0, new SKColor(200, 100, 50, 49));
        source.SetPixel(1, 0, new SKColor(200, 100, 50, 50));
        source.SetPixel(2, 0, new SKColor(200, 100, 50, 255));
        var sourceBelowThreshold = source.GetPixel(0, 0);
        var sourceAtThreshold = source.GetPixel(1, 0);
        var sourceOpaque = source.GetPixel(2, 0);

        using var result = SpriteImageService.RemoveLowAlphaPixels(source, alphaThreshold: 50);

        Assert.Equal(new SKColor(0, 0, 0, 0), result.GetPixel(0, 0));
        Assert.Equal(sourceAtThreshold, result.GetPixel(1, 0));
        Assert.Equal(sourceOpaque, result.GetPixel(2, 0));
        Assert.Equal(sourceBelowThreshold, source.GetPixel(0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => SpriteImageService.RemoveLowAlphaPixels(source, alphaThreshold: 256));
    }

    [Fact]
    public void RemoveAdjacentCellBleedLinesClearsOnlyIsolatedEdgeLines()
    {
        using var source = new SKBitmap(8, 4, SKColorType.Bgra8888, SKAlphaType.Premul);
        source.Erase(SKColors.Transparent);
        for (var x = 0; x < 4; x++) source.SetPixel(x, 0, SKColors.Red);
        for (var y = 0; y < 4; y++) source.SetPixel(4, y, SKColors.Blue);
        source.SetPixel(1, 2, SKColors.Green);
        source.SetPixel(5, 2, SKColors.Blue);
        source.SetPixel(5, 0, SKColors.Yellow);
        source.SetPixel(5, 1, SKColors.Yellow);

        using var result = SpriteImageService.RemoveAdjacentCellBleedLines(source, cellWidth: 4, cellHeight: 4, edgeWidth: 2);

        for (var x = 0; x < 4; x++) Assert.Equal((byte)0, result.GetPixel(x, 0).Alpha);
        for (var y = 0; y < 4; y++) Assert.Equal(SKColors.Blue, result.GetPixel(4, y));
        Assert.Equal(SKColors.Green, result.GetPixel(1, 2));
        Assert.Equal(SKColors.Yellow, result.GetPixel(5, 0));
        Assert.Equal(SKColors.Yellow, result.GetPixel(5, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => SpriteImageService.RemoveAdjacentCellBleedLines(source, 4, 4, -1));
    }

    [Fact]
    public async Task ProjectJsonRoundTripsWithRequiredCamelCaseFields()
    {
        using var workspace = new TemporaryDirectory();
        var path = Path.Combine(workspace.Path, "walk.json");
        var project = new SpriteProject { Title = "walk", CellWidth = 20, CellHeight = 16, Columns = 3, Rows = 2 };
        await ProjectStore.SaveAsync(project, path);
        var json = await File.ReadAllTextAsync(path);
        Assert.Contains("\"formatVersion\"", json);
        var loaded = await ProjectStore.LoadAsync(path);
        Assert.Equal(project.Title, loaded.Title);
        Assert.Equal((project.CellWidth, project.CellHeight, project.Columns, project.Rows),
            (loaded.CellWidth, loaded.CellHeight, loaded.Columns, loaded.Rows));
    }

    [Fact]
    public async Task MultiCellAssetSurvivesSaveReloadAndPngExport()
    {
        using var workspace = new TemporaryDirectory();
        var input = WritePng(workspace.Path, "sheet.png", 4, 2);
        using (var bitmap = SKBitmap.Decode(input)!)
        {
            bitmap.SetPixel(0, 0, SKColors.Blue);
            bitmap.SetPixel(2, 0, SKColors.Red);
            using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100); File.WriteAllBytes(input, data.ToArray());
        }
        var project = new SpriteProject { Title = "sheet", CellWidth = 2, CellHeight = 2, Columns = 2, Rows = 1 };
        var imported = SpriteImageService.ImportPng(input, workspace.Path, project, 0, 0);
        project.Assets.Add(new SpriteAsset { RelativePath = imported.AssetPath, Width = imported.Width, Height = imported.Height });
        project.Cells.AddRange(imported.Cells);
        var projectPath = Path.Combine(workspace.Path, "sheet.json");
        await ProjectStore.SaveAsync(project, projectPath);

        var reopened = await ProjectStore.LoadAsync(projectPath);
        Assert.Equal(project.Cells.Select(cell => cell.SourceRect), reopened.Cells.Select(cell => cell.SourceRect));
        using var exported = SpriteImageService.Export(reopened, workspace.Path);
        Assert.Equal(SKColors.Blue, exported.GetPixel(0, 0));
        Assert.Equal(SKColors.Red, exported.GetPixel(2, 0));
        Assert.Equal((4, 2), (exported.Width, exported.Height));
    }

    private static string WritePng(string directory, string name, int width, int height)
    {
        var path = Path.Combine(directory, name);
        using var bitmap = new SKBitmap(width, height); bitmap.Erase(SKColors.Transparent);
        using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100); File.WriteAllBytes(path, data.ToArray());
        return path;
    }

    private static SpriteProject ProjectWithCell(int width, int height, int columns, int rows, PixelRect rect) => new()
    {
        Title = "test", CellWidth = width, CellHeight = height, Columns = columns, Rows = rows,
        Assets = [new SpriteAsset { RelativePath = "assets/a.png", Width = rect.X + rect.Width + 10, Height = rect.Y + rect.Height + 10 }],
        Cells = [new SpriteCell { X = 0, Y = 0, AssetPath = "assets/a.png", SourceRect = rect, DisplayName = "frame" }]
    };

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "SAMERIDER.Tests", Guid.NewGuid().ToString("N"));
        public TemporaryDirectory() => Directory.CreateDirectory(Path);
        public void Dispose() => Directory.Delete(Path, true);
    }
}
