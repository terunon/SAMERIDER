using SAMERIDER.Core.Models;
using SAMERIDER.Core.Services;
using SkiaSharp;

namespace SAMERIDER.Core.Tests;

public sealed class CellEditServiceTests
{
    private static SpriteProject Project() => new()
    {
        Title = "edit", CellWidth = 2, CellHeight = 1, Columns = 1, Rows = 1,
        Assets = [new SpriteAsset { RelativePath = "assets/source.png", Width = 2, Height = 1 }],
        Cells = [new SpriteCell { X = 0, Y = 0, AssetPath = "assets/source.png", SourceRect = new PixelRect(0, 0, 2, 1), DisplayName = "source" }]
    };

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OverlayTintPreservesColorOrderAlphaAndThumbnailConsistency(bool adjusted)
    {
        var project = Project();
        var cell = project.Cells[0];
        if (adjusted) { cell.Hue = 35; cell.Saturation = -20; cell.Brightness = 10; }
        using var source = new SKBitmap(2, 1, SKColorType.Bgra8888, SKAlphaType.Premul);
        source.SetPixel(0, 0, new SKColor(160, 80, 30, 200));
        source.SetPixel(1, 0, SKColors.Transparent);
        var tint = new SKColor(30, 90, 255, 128);
        using var untinted = SpriteImageService.RenderCell(project, cell, source);
        using var full = SpriteImageService.RenderCell(project, cell, source, tint);
        using var thumb = SpriteImageService.RenderCellThumbnail(project, cell, source, 512, tint: tint);
        var original = untinted.GetPixel(0, 0);
        var actual = full.GetPixel(0, 0);
        Assert.InRange(Math.Abs(actual.Red - (original.Red * .72f + tint.Red * .28f)), 0, 3);
        Assert.InRange(Math.Abs(actual.Green - (original.Green * .72f + tint.Green * .28f)), 0, 3);
        Assert.InRange(Math.Abs(actual.Blue - (original.Blue * .72f + tint.Blue * .28f)), 0, 3);
        Assert.InRange(Math.Abs(actual.Alpha - original.Alpha * tint.Alpha / 255f), 0, 1);
        Assert.Equal(actual, thumb.GetPixel(0, 0));
        Assert.Equal(0, full.GetPixel(1, 0).Alpha);
        Assert.Equal(0, thumb.GetPixel(1, 0).Alpha);
    }

    [Fact]
    public void ResizeUsesNearestNeighborAndReleasePreservesSourceAndColorEdits()
    {
        var project = Project();
        using var source = new SKBitmap(2, 1);
        source.SetPixel(0, 0, SKColors.Red);
        source.SetPixel(1, 0, SKColors.Blue);
        var originalRect = project.Cells[0].SourceRect;
        CellEditService.ResizeSheet(project, 4, 2);
        using (var output = SpriteImageService.RenderCell(project, project.Cells[0], source))
        {
            Assert.Equal((4, 2), (output.Width, output.Height));
            for (var y = 0; y < 2; y++)
            {
                Assert.Equal(SKColors.Red, output.GetPixel(0, y));
                Assert.Equal(SKColors.Red, output.GetPixel(1, y));
                Assert.Equal(SKColors.Blue, output.GetPixel(2, y));
                Assert.Equal(SKColors.Blue, output.GetPixel(3, y));
            }
        }
        project.Cells[0].Saturation = -100;
        CellEditService.ResetSheetSize(project);
        Assert.False(project.HasSheetResize);
        Assert.Equal((2, 1), (project.CellWidth, project.CellHeight));
        Assert.Equal(originalRect, project.Cells[0].SourceRect);
        Assert.Equal(-100, project.Cells[0].Saturation);
        Assert.Equal(SKColors.Red, source.GetPixel(0, 0));
    }

    [Fact]
    public void GridExpansionCannotMakeResizeReleaseImpossible()
    {
        var project = Project();
        project.CellWidth = 4096;
        CellEditService.ResizeSheet(project, 64, 1);
        Assert.False(CellEditService.CanRestoreSize(project, 256, 1));
        project.Columns = 256;
        Assert.Throws<InvalidDataException>(() => ProjectValidator.Validate(project));
    }

    [Theory]
    [InlineData(5, 1, 4)]
    [InlineData(6, 1, 8)]
    [InlineData(5, 2, 6)]
    [InlineData(5, 4, 5)]
    [InlineData(1, 3, 4)]
    public void SnapFindsNearestCellSizeWithFourDivisibleSheet(int size, int count, int expected)
    {
        var result = CellEditService.SnapCellSize(size, count);
        Assert.Equal(expected, result);
        Assert.Equal(0, result * count % 4);
    }

    [Fact]
    public void ColorPreviewMatchesExportAndKeepsAlphaAndOriginalPixels()
    {
        var project = Project();
        project.Cells[0].Saturation = -100;
        using var source = new SKBitmap(2, 1, SKColorType.Bgra8888, SKAlphaType.Premul);
        source.SetPixel(0, 0, new SKColor(240, 60, 30, 128));
        source.SetPixel(1, 0, SKColors.Transparent);
        var original = source.GetPixel(0, 0);
        using var preview = CellEditService.AdjustColors(source, 0, -100, 0, 0);
        using var rendered = SpriteImageService.Export(project, new Dictionary<string, SKBitmap> { ["assets/source.png"] = source });
        Assert.Equal(preview.GetPixel(0, 0), rendered.GetPixel(0, 0));
        var gray = rendered.GetPixel(0, 0);
        Assert.InRange(Math.Abs(gray.Red - gray.Green), 0, 1);
        Assert.InRange(Math.Abs(gray.Green - gray.Blue), 0, 1);
        Assert.Equal(original.Alpha, gray.Alpha);
        Assert.Equal(0, rendered.GetPixel(1, 0).Alpha);
        Assert.Equal(original, source.GetPixel(0, 0));
        project.Cells[0].Saturation = 0;
        using var reset = SpriteImageService.RenderCell(project, project.Cells[0], source);
        Assert.Equal(original, reset.GetPixel(0, 0));
    }

    [Fact]
    public void BrightnessContrastAndHueAffectRgbWithoutChangingOpacity()
    {
        using var source = new SKBitmap(1, 1, SKColorType.Bgra8888, SKAlphaType.Premul);
        source.SetPixel(0, 0, new SKColor(200, 20, 10, 128));
        using var bright = CellEditService.AdjustColors(source, 0, 0, 100, 0);
        Assert.Equal(new SKColor(255, 255, 255, 128), bright.GetPixel(0, 0));
        using var flat = CellEditService.AdjustColors(source, 0, 0, 0, -100);
        var gray = flat.GetPixel(0, 0);
        Assert.InRange(gray.Red, (byte)126, (byte)129);
        Assert.Equal(gray.Red, gray.Green);
        Assert.Equal(gray.Green, gray.Blue);
        using var hue = CellEditService.AdjustColors(source, 90, 0, 0, 0);
        Assert.NotEqual(source.GetPixel(0, 0), hue.GetPixel(0, 0));
        Assert.Equal(128, hue.GetPixel(0, 0).Alpha);
    }

    [Fact]
    public void BothEditsSurviveCloningAndProjectSerializationWithoutMutatingSnapshot()
    {
        var project = Project();
        var undo = project.DeepClone();
        CellEditService.ResizeSheet(project, 4, 3);
        project.Cells[0].Hue = 90;
        project.Cells[0].Brightness = 20;
        project.Cells[0].Contrast = -30;
        var clone = project.DeepClone();
        var loaded = ProjectStore.Deserialize(ProjectStore.SerializeToUtf8Bytes(clone));
        Assert.True(loaded.HasSheetResize);
        Assert.Equal((2, 1), (loaded.OriginalCellWidth, loaded.OriginalCellHeight));
        Assert.Equal(90, loaded.Cells[0].Hue);
        Assert.Equal(20, loaded.Cells[0].Brightness);
        Assert.Equal(-30, loaded.Cells[0].Contrast);
        Assert.False(undo.HasSheetResize);
        Assert.False(undo.Cells[0].HasColorAdjustment);
        CellEditService.ResetSheetSize(loaded);
        Assert.Equal(90, loaded.Cells[0].Hue);
    }
}
