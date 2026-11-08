using SAMERIDER.Core.Services;

namespace SAMERIDER.Core.Tests;

public sealed class SpriteSequenceServiceTests
{
    [Fact]
    public void EmptySelectionReturnsWholeGridRowMajorIncludingEmptyCellPositions()
    {
        var positions = SpriteSequenceService.GetPositions(3, 2, Array.Empty<(int X, int Y)>());

        Assert.Equal(new[]
        {
            (0, 0), (1, 0), (2, 0),
            (0, 1), (1, 1), (2, 1)
        }, positions);
    }

    [Fact]
    public void FullSelectionIsUniqueAndOrderedByRowThenColumn()
    {
        (int X, int Y)[] selection = [(2, 1), (0, 1), (1, 0), (0, 0), (2, 1), (1, 1), (2, 0)];

        var positions = SpriteSequenceService.GetPositions(3, 2, selection);

        Assert.Equal(new[] { (0, 0), (1, 0), (2, 0), (0, 1), (1, 1), (2, 1) }, positions);
    }

    [Fact]
    public void SingleSelectionReturnsWholeGrid()
    {
        var positions = SpriteSequenceService.GetPositions(3, 2, [(1, 0)]);

        Assert.Equal(new[] { (0, 0), (1, 0), (2, 0), (0, 1), (1, 1), (2, 1) }, positions);
    }

    [Fact]
    public void SelectionIgnoresCoordinatesOutsideTheGrid()
    {
        var positions = SpriteSequenceService.GetPositions(2, 2, [(-1, 0), (0, 2), (1, 1), (0, 0)]);

        Assert.Equal(new[] { (0, 0), (1, 1) }, positions);
    }

    [Fact]
    public void RepeatedSinglePositionIsStillWholeGridPlayback()
    {
        Assert.Equal(new[] { (0, 0), (1, 0) }, SpriteSequenceService.GetPositions(2, 1, [(1, 0), (1, 0)]));
    }

    [Fact]
    public void NoValidSelectedPositionFallsBackToWholeGrid()
    {
        Assert.Equal(new[] { (0, 0), (1, 0) }, SpriteSequenceService.GetPositions(2, 1, [(-1, 0), (0, 1)]));
    }

    [Fact]
    public void PlaybackUsesFifteenFramesPerSecond()
    {
        Assert.Equal(15, SpriteSequenceService.FramesPerSecond);
        Assert.Equal(TimeSpan.FromSeconds(1d / 15), SpriteSequenceService.FrameDuration);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    [InlineData(-1, 2)]
    public void RejectsNonPositiveDimensions(int columns, int rows)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SpriteSequenceService.GetPositions(columns, rows, []));
    }

    [Fact]
    public void RejectsGridLargerThanProjectCellLimit()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SpriteSequenceService.GetPositions(101, 100, []));
    }
}
