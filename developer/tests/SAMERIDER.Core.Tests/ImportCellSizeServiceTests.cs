using SAMERIDER.Core.Services;

namespace SAMERIDER.Core.Tests;

public sealed class ImportCellSizeServiceTests
{
    [Theory]
    [InlineData(1083, 1448, 361, 362)]
    [InlineData(1448, 1083, 362, 361)]
    public void SuggestsNearSquareCellsForPortraitAndLandscapeImages(int imageWidth, int imageHeight, int expectedWidth, int expectedHeight)
    {
        Assert.Equal((expectedWidth, expectedHeight), ImportCellSizeService.Suggest(imageWidth, imageHeight));
    }

    [Fact]
    public void PreservesExactGcdCellSizeWhenTheGridIsWithinLimit()
    {
        Assert.Equal((362, 362), ImportCellSizeService.Suggest(1448, 1086));
    }

    [Fact]
    public void UsesWholeImageWhenOnlyOneSquareCellIsSuggested()
    {
        Assert.Equal((1024, 1024), ImportCellSizeService.Suggest(1024, 1024));
    }

    [Fact]
    public void SuggestedGridNeverExceedsMaximumCellCount()
    {
        var (cellWidth, cellHeight) = ImportCellSizeService.Suggest(10_000, 1_001);

        Assert.True(cellWidth > 0);
        Assert.True(cellHeight > 0);
        Assert.InRange((long)(10_000 / cellWidth) * (1_001 / cellHeight), 1, ProjectValidator.MaximumGridCellCount);
    }

    [Theory]
    [InlineData(0, 1, "imageWidth")]
    [InlineData(1, 0, "imageHeight")]
    [InlineData(-1, 2, "imageWidth")]
    [InlineData(2, -1, "imageHeight")]
    public void RejectsNonPositiveImageDimensions(int width, int height, string parameterName)
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => ImportCellSizeService.Suggest(width, height));

        Assert.Equal(parameterName, exception.ParamName);
    }
}
