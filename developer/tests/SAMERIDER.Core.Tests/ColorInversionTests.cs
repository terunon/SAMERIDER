using SAMERIDER.Core.Models;
using SAMERIDER.Core.Services;
using SkiaSharp;

namespace SAMERIDER.Core.Tests;

public sealed class ColorInversionTests
{
    [Fact]
    public void BrightnessInversionNegatesRgbAndPreservesAlpha()
    {
        using var source = CreateBitmap(new SKColor(210, 80, 25, 160), SKColors.Transparent);

        using var result = CellEditService.AdjustColors(source, 0, 0, 0, 0, ColorInversion.Brightness);

        var inverted = result.GetPixel(0, 0);
        Assert.InRange(Math.Abs(inverted.Red - (255 - source.GetPixel(0, 0).Red)), 0, 1);
        Assert.InRange(Math.Abs(inverted.Green - (255 - source.GetPixel(0, 0).Green)), 0, 1);
        Assert.InRange(Math.Abs(inverted.Blue - (255 - source.GetPixel(0, 0).Blue)), 0, 1);
        Assert.Equal(source.GetPixel(0, 0).Alpha, inverted.Alpha);
        Assert.Equal((byte)0, result.GetPixel(1, 0).Alpha);
    }

    [Fact]
    public void HueInversionMatchesAddingHalfTurn()
    {
        using var source = CreateBitmap(new SKColor(190, 65, 120, 220));

        using var inverted = CellEditService.AdjustColors(source, 37, 12, -8, 16, ColorInversion.Hue);
        using var rotated = CellEditService.AdjustColors(source, 217, 12, -8, 16);

        AssertColorsNear(rotated.GetPixel(0, 0), inverted.GetPixel(0, 0), 1);
    }

    [Fact]
    public void SaturationInversionComplementsHslSaturationAndPreservesAlpha()
    {
        using var source = CreateBitmap(
            new SKColor(128, 128, 128, 255),
            new SKColor(255, 0, 0, 255),
            new SKColor(170, 85, 85, 255),
            new SKColor(180, 90, 90, 128));

        using var result = CellEditService.AdjustColors(source, 0, 0, 0, 0, ColorInversion.Saturation);

        AssertColorsNear(new SKColor(255, 0, 0, 255), result.GetPixel(0, 0), 2);
        AssertColorsNear(new SKColor(128, 128, 128, 255), result.GetPixel(1, 0), 2);
        AssertColorsNear(new SKColor(213, 43, 43, 255), result.GetPixel(2, 0), 2);
        Assert.Equal((byte)128, result.GetPixel(3, 0).Alpha);
    }

    [Fact]
    public void SaturationInversionPreservesBlackWhiteAndTransparency()
    {
        using var source = CreateBitmap(SKColors.Black, SKColors.White, SKColors.Transparent);
        using var result = CellEditService.AdjustColors(source, 0, 0, 0, 0, ColorInversion.Saturation);
        for (var x = 0; x < 3; x++) Assert.Equal(source.GetPixel(x, 0), result.GetPixel(x, 0));
    }

    [Fact]
    public void BrightnessInversionComplementsAfterContrastAndBrightness()
    {
        using var source = CreateBitmap(new SKColor(180, 70, 25, 192));

        using var normal = CellEditService.AdjustColors(source, 18, 25, 30, 45);
        using var inverted = CellEditService.AdjustColors(source, 18, 25, 30, 45, ColorInversion.Brightness);

        var expected = normal.GetPixel(0, 0);
        var actual = inverted.GetPixel(0, 0);
        Assert.InRange(Math.Abs(actual.Red - (255 - expected.Red)), 0, 1);
        Assert.InRange(Math.Abs(actual.Green - (255 - expected.Green)), 0, 1);
        Assert.InRange(Math.Abs(actual.Blue - (255 - expected.Blue)), 0, 1);
        Assert.Equal(expected.Alpha, actual.Alpha);
    }

    [Fact]
    public void BrightnessAndContrastInversionsCancelAtDefaults()
    {
        using var source = CreateBitmap(new SKColor(180, 70, 25, 192));

        using var result = CellEditService.AdjustColors(source, 0, 0, 0, 0,
            ColorInversion.Brightness | ColorInversion.Contrast);

        AssertColorsNear(source.GetPixel(0, 0), result.GetPixel(0, 0), 1);
    }

    [Fact]
    public void RejectsUnknownInversionFlagsBeforeAdjusting()
    {
        using var source = CreateBitmap(new SKColor(10, 20, 30, 255));
        var invalid = (ColorInversion)16;

        Assert.Throws<ArgumentOutOfRangeException>(() => CellEditService.CreateColorFilter(0, 0, 0, 0, invalid));
        Assert.Throws<ArgumentOutOfRangeException>(() => CellEditService.AdjustColors(source, 0, 0, 0, 0, invalid));
    }

    [Fact]
    public void ContrastInversionHappensBeforeBrightnessAdjustment()
    {
        using var source = CreateBitmap(new SKColor(100, 110, 120, 255));
        using var contrast = CellEditService.AdjustColors(source, 0, 0, 10, 0, ColorInversion.Contrast);
        using var brightness = CellEditService.AdjustColors(source, 0, 0, 10, 0, ColorInversion.Brightness);
        AssertColorsNear(new SKColor(181, 171, 161), contrast.GetPixel(0, 0), 1);
        AssertColorsNear(new SKColor(130, 120, 110), brightness.GetPixel(0, 0), 1);
    }

    public static IEnumerable<object[]> InversionCombinations()
    {
        for (var mask = 0; mask <= (int)ColorInversion.All; mask++) yield return [(ColorInversion)mask];
    }

    [Theory]
    [MemberData(nameof(InversionCombinations))]
    public void EveryCombinationMatchesPreviewExportAndPersistsNonDestructively(ColorInversion flags)
    {
        using var source = CreateBitmap(new SKColor(160, 100, 60, 200), SKColors.Transparent);
        var original = source.GetPixel(0, 0);
        var project = new SpriteProject
        {
            Title = "inversion", CellWidth = 2, CellHeight = 1, Columns = 1, Rows = 1,
            Assets = [new SpriteAsset { RelativePath = "assets/source.png", Width = 2, Height = 1 }],
            Cells = [new SpriteCell { X = 0, Y = 0, AssetPath = "assets/source.png",
                SourceRect = new PixelRect(0, 0, 2, 1), DisplayName = "source", ColorInversions = flags }]
        };
        var undo = project.DeepClone();
        var loaded = ProjectStore.Deserialize(ProjectStore.SerializeToUtf8Bytes(project));
        Assert.Equal(flags, loaded.Cells[0].ColorInversions);
        Assert.Equal(flags != ColorInversion.None, loaded.Cells[0].HasColorAdjustment);
        using var preview = CellEditService.AdjustColors(source, 0, 0, 0, 0, flags);
        using var exported = SpriteImageService.Export(loaded, new Dictionary<string, SKBitmap> { ["assets/source.png"] = source });
        using var thumbnail = SpriteImageService.RenderCellThumbnail(loaded, loaded.Cells[0], source, 512);
        AssertColorsNear(preview.GetPixel(0, 0), exported.GetPixel(0, 0), 1);
        AssertColorsNear(preview.GetPixel(0, 0), thumbnail.GetPixel(0, 0), 1);
        Assert.Equal(0, exported.GetPixel(1, 0).Alpha);
        project.Cells[0].ColorInversions = ColorInversion.None;
        Assert.False(project.Cells[0].HasColorAdjustment);
        Assert.Equal(flags, undo.Cells[0].ColorInversions);
        Assert.Equal(original, source.GetPixel(0, 0));
        loaded.Cells[0].ColorInversions = (ColorInversion)16;
        Assert.Throws<InvalidDataException>(() => ProjectValidator.Validate(loaded));
    }

    private static SKBitmap CreateBitmap(params SKColor[] colors)
    {
        var bitmap = new SKBitmap(colors.Length, 1, SKColorType.Bgra8888, SKAlphaType.Premul);
        for (var x = 0; x < colors.Length; x++) bitmap.SetPixel(x, 0, colors[x]);
        return bitmap;
    }

    private static void AssertColorsNear(SKColor expected, SKColor actual, int tolerance)
    {
        Assert.InRange(Math.Abs(expected.Red - actual.Red), 0, tolerance);
        Assert.InRange(Math.Abs(expected.Green - actual.Green), 0, tolerance);
        Assert.InRange(Math.Abs(expected.Blue - actual.Blue), 0, tolerance);
        Assert.InRange(Math.Abs(expected.Alpha - actual.Alpha), 0, tolerance);
    }
}
