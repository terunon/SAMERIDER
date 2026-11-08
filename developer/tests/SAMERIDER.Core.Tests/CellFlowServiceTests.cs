using SAMERIDER.Core.Models;
using SAMERIDER.Core.Services;

namespace SAMERIDER.Core.Tests;

public sealed class CellFlowServiceTests
{
    private static SpriteProject Project(int columns = 3, int rows = 2) => new()
    {
        Title = "flow", CellWidth = 2, CellHeight = 2, Columns = columns, Rows = rows,
        Assets = [new SpriteAsset { RelativePath = "assets/source.png", Width = 8, Height = 8 }],
        Cells = Enumerable.Range(0, columns * rows).Select(index => new SpriteCell
        {
            X = index % columns,
            Y = index / columns,
            AssetPath = "assets/source.png",
            SourceRect = new PixelRect(0, 0, 2, 2),
            DisplayName = $"cell-{index}",
            OffsetX = index,
            Hue = index
        }).ToList()
    };

    [Fact]
    public void InsertAtLastSlotWrapsAndAddsRow()
    {
        var project = Project(columns: 2, rows: 2);
        var result = CellFlowService.CreateTransformed(project, new HashSet<(int X, int Y)> { (1, 1) }, insert: true);

        Assert.Equal((2, 3), (result.Columns, result.Rows));
        Assert.Equal((0, 2), PositionOf(result, "cell-3"));
        Assert.Equal((0, 1), PositionOf(result, "cell-2"));
        Assert.Equal((2, 2), (project.Columns, project.Rows));
        Assert.Equal((1, 1), PositionOf(project, "cell-3"));
        Assert.True(CellFlowService.CanTransform(project, new HashSet<(int X, int Y)> { (1, 1) }, insert: true));
    }

    [Fact]
    public void CanTransformAllowsShiftWithinExistingBlankCapacity()
    {
        var project = Project(columns: 3, rows: 2);
        project.Cells.RemoveAll(cell => cell.DisplayName is "cell-4" or "cell-5");
        var selection = new HashSet<(int X, int Y)> { (0, 0) };

        Assert.True(CellFlowService.CanTransform(project, selection, insert: true));
        var result = CellFlowService.CreateTransformed(project, selection, insert: true);
        Assert.Equal((1, 0), PositionOf(result, "cell-0"));
        Assert.Equal(2, result.Rows);
    }

    [Fact]
    public void TrailingBlankSelectionOnlyEnablesDeleteWhenRowsCanBeTrimmed()
    {
        var project = Project(columns: 2, rows: 3);
        project.Cells.RemoveAll(cell => cell.DisplayName != "cell-0");
        var trailingBlank = new HashSet<(int X, int Y)> { (1, 2) };

        Assert.False(CellFlowService.CanTransform(project, trailingBlank, insert: true));
        Assert.True(CellFlowService.CanTransform(project, trailingBlank, insert: false));
        var trimmed = CellFlowService.CreateTransformed(project, trailingBlank, insert: false);
        Assert.Equal(1, trimmed.Rows);
        Assert.False(CellFlowService.CanTransform(trimmed, new HashSet<(int X, int Y)> { (1, 0) }, insert: false));
    }

    [Fact]
    public void InsertMultiplePositionsUsesOriginalIndicesAndCountsBlankSlots()
    {
        var project = Project(columns: 3, rows: 2);
        project.Cells.RemoveAll(cell => cell.DisplayName is "cell-1" or "cell-3");
        var result = CellFlowService.CreateTransformed(project,
            new HashSet<(int X, int Y)> { (1, 0), (0, 1) }, insert: true);

        Assert.Equal(3, result.Rows);
        Assert.Equal((0, 0), PositionOf(result, "cell-0"));
        Assert.Equal((0, 1), PositionOf(result, "cell-2"));
        Assert.Equal((0, 2), PositionOf(result, "cell-4"));
        Assert.Equal((1, 2), PositionOf(result, "cell-5"));
    }

    [Fact]
    public void DeleteSelectedPositionsShiftsLeftAcrossRowsAndKeepsDimensions()
    {
        var project = Project(columns: 3, rows: 2);
        var result = CellFlowService.CreateTransformed(project,
            new HashSet<(int X, int Y)> { (1, 0), (0, 1) }, insert: false);

        Assert.Equal((3, 2), (result.Columns, result.Rows));
        Assert.Equal(4, result.Cells.Count);
        Assert.Equal((0, 0), PositionOf(result, "cell-0"));
        Assert.Equal((1, 0), PositionOf(result, "cell-2"));
        Assert.Equal((2, 0), PositionOf(result, "cell-4"));
        Assert.Equal((0, 1), PositionOf(result, "cell-5"));
    }

    [Fact]
    public void SparseCellsKeepTheirMetadataWhenShifted()
    {
        var project = Project(columns: 2, rows: 2);
        project.Cells.RemoveAll(cell => cell.DisplayName == "cell-1");

        var result = CellFlowService.CreateTransformed(project, new HashSet<(int X, int Y)> { (0, 0) }, insert: true);
        var shifted = result.Cells.Single(cell => cell.DisplayName == "cell-2");
        Assert.Equal((1, 1), (shifted.X, shifted.Y));
        Assert.Equal(2, shifted.OffsetX);
        Assert.Equal(2, shifted.Hue);
        Assert.Equal(3, result.Rows);
        Assert.NotSame(project.Cells.Single(cell => cell.DisplayName == "cell-2"), shifted);
    }

    [Fact]
    public void DeleteTrimsMultipleEmptyBottomRowsButKeepsInteriorBlankRows()
    {
        var project = Project(columns: 2, rows: 5);
        project.Cells.RemoveAll(cell => cell.DisplayName is not ("cell-0" or "cell-4"));

        var result = CellFlowService.CreateTransformed(project, new HashSet<(int X, int Y)> { (1, 4) }, insert: false);

        Assert.Equal(3, result.Rows);
        Assert.Equal((0, 0), PositionOf(result, "cell-0"));
        Assert.Equal((0, 2), PositionOf(result, "cell-4"));
        Assert.True(CellFlowService.CanTransform(project, new HashSet<(int X, int Y)> { (1, 4) }, insert: false));
    }

    [Fact]
    public void DeletingBottomRowShrinksGridWithoutMutatingOriginal()
    {
        var project = Project(columns: 3, rows: 2);
        var result = CellFlowService.CreateTransformed(project,
            new HashSet<(int X, int Y)> { (0, 1), (1, 1), (2, 1) }, insert: false);
        Assert.Equal((3, 1), (result.Columns, result.Rows));
        Assert.Equal(3, result.Cells.Count);
        Assert.Equal(2, project.Rows);
        Assert.Equal(6, project.Cells.Count);
    }

    [Fact]
    public void DeletingFinalCellLeavesAtLeastOneRow()
    {
        var project = Project(columns: 2, rows: 4);
        project.Cells.RemoveAll(cell => cell.DisplayName != "cell-7");

        var result = CellFlowService.CreateTransformed(project, new HashSet<(int X, int Y)> { (1, 3) }, insert: false);

        Assert.Equal(1, result.Rows);
        Assert.Empty(result.Cells);
        Assert.True(CellFlowService.CanTransform(project, new HashSet<(int X, int Y)> { (1, 3) }, insert: false));
    }

    [Fact]
    public void InsertRejectsGridAndSheetLimitsWithoutMutatingProject()
    {
        var gridLimited = Project(columns: 100, rows: 100);
        var snapshot = gridLimited.DeepClone();
        Assert.Throws<InvalidDataException>(() => CellFlowService.CreateTransformed(gridLimited,
            new HashSet<(int X, int Y)> { (0, 0) }, insert: true));
        Assert.False(CellFlowService.CanTransform(gridLimited, new HashSet<(int X, int Y)> { (0, 0) }, insert: true));
        Assert.Equal(snapshot.Rows, gridLimited.Rows);
        Assert.Equal(snapshot.Cells.Select(cell => (cell.X, cell.Y)), gridLimited.Cells.Select(cell => (cell.X, cell.Y)));

        var sheetLimited = Project(columns: 2, rows: 1);
        sheetLimited.CellHeight = 16_384;
        Assert.Throws<InvalidDataException>(() => CellFlowService.CreateTransformed(sheetLimited,
            new HashSet<(int X, int Y)> { (0, 0) }, insert: true));
        Assert.Equal(1, sheetLimited.Rows);
    }

    [Fact]
    public void InvalidDimensionsAndPositionsThrowClearly()
    {
        var invalid = Project();
        invalid.Columns = 0;
        Assert.Throws<InvalidDataException>(() => CellFlowService.CreateTransformed(invalid, new HashSet<(int X, int Y)>(), true));

        var project = Project();
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => CellFlowService.CreateTransformed(project,
            new HashSet<(int X, int Y)> { (-1, 0) }, true));
        Assert.Contains("範囲外", exception.Message);
    }

    private static (int X, int Y) PositionOf(SpriteProject project, string name)
    {
        var cell = project.Cells.Single(candidate => candidate.DisplayName == name);
        return (cell.X, cell.Y);
    }
}
