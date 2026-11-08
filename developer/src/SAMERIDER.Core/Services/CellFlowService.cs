using SAMERIDER.Core.Models;

namespace SAMERIDER.Core.Services;

public static class CellFlowService
{
    public static bool CanTransform(SpriteProject project, IReadOnlySet<(int X, int Y)> positions, bool insert)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(positions);
        if (positions.Count == 0 || project.Columns <= 0 || project.Rows <= 0) return false;
        var gridSize = (long)project.Columns * project.Rows;
        if (gridSize > ProjectValidator.MaximumGridCellCount) return false;
        if (positions.Any(position => position.X < 0 || position.Y < 0 || position.X >= project.Columns || position.Y >= project.Rows)) return false;

        var selected = positions.Select(position => (long)position.Y * project.Columns + position.X).Order().ToArray();
        var maximumOccupiedIndex = -1L;
        var hasChanges = false;
        foreach (var cell in project.Cells)
        {
            if (cell.X < 0 || cell.Y < 0 || cell.X >= project.Columns || cell.Y >= project.Rows) return false;
            var index = (long)cell.Y * project.Columns + cell.X;
            if (!insert && Array.BinarySearch(selected, index) >= 0)
            {
                hasChanges = true;
                continue;
            }
            var shift = insert
                ? LowerBound(selected, index + 1)
                : LowerBound(selected, index);
            var transformedIndex = insert ? index + shift : index - shift;
            if (transformedIndex != index) hasChanges = true;
            maximumOccupiedIndex = Math.Max(maximumOccupiedIndex, transformedIndex);
        }

        var transformedRows = insert
            ? Math.Max(project.Rows, maximumOccupiedIndex < 0 ? 1 : (int)(maximumOccupiedIndex / project.Columns) + 1)
            : Math.Max(1, maximumOccupiedIndex < 0 ? 1 : (int)(maximumOccupiedIndex / project.Columns) + 1);
        var dimensionsChange = transformedRows != project.Rows;
        return (hasChanges || dimensionsChange) && (!insert || CanFitSheet(project, project.Columns, transformedRows));
    }

    public static SpriteProject CreateTransformed(SpriteProject project, IReadOnlySet<(int X, int Y)> positions, bool insert)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(positions);
        if (project.Columns <= 0 || project.Rows <= 0)
            throw new InvalidDataException("セルグリッドの分割数は正の値にしてください。");

        var originalSlotCount = (long)project.Columns * project.Rows;
        if (originalSlotCount > ProjectValidator.MaximumGridCellCount)
            throw new InvalidDataException("セルグリッドが上限を超えています。");

        foreach (var (x, y) in positions)
        {
            if (x < 0 || y < 0 || x >= project.Columns || y >= project.Rows)
                throw new ArgumentOutOfRangeException(nameof(positions), $"選択位置がセルグリッドの範囲外です（{x + 1}, {y + 1}）。");
        }

        var selectedIndices = positions
            .Select(position => (long)position.Y * project.Columns + position.X)
            .Order()
            .ToArray();

        var result = project.DeepClone();
        if (selectedIndices.Length == 0) return result;

        var expandedRows = project.Rows;

        for (var index = result.Cells.Count - 1; index >= 0; index--)
        {
            var cell = result.Cells[index];
            var originalIndex = (long)cell.Y * project.Columns + cell.X;
            var selectedPosition = Array.BinarySearch(selectedIndices, originalIndex);
            if (!insert && selectedPosition >= 0)
            {
                result.Cells.RemoveAt(index);
                continue;
            }
            var selectedBeforeOrAt = LowerBound(selectedIndices, originalIndex + (insert ? 1 : 0));
            var shiftedIndex = insert
                ? originalIndex + selectedBeforeOrAt
                : originalIndex - selectedBeforeOrAt;
            cell.X = (int)(shiftedIndex % project.Columns);
            cell.Y = (int)(shiftedIndex / project.Columns);
            expandedRows = Math.Max(expandedRows, cell.Y + 1);
        }

        if (insert)
        {
            if (expandedRows != project.Rows && !CanFitSheet(project, project.Columns, expandedRows))
                throw new InvalidDataException("セルの挿入後にスプライトシートのサイズ上限を超えます。");
            result.Rows = expandedRows;
        }
        else
        {
            result.Rows = Math.Max(1, result.Cells.Count == 0 ? 1 : result.Cells.Max(cell => cell.Y) + 1);
        }
        ProjectValidator.Validate(result);
        return result;
    }

    private static int LowerBound(long[] values, long target)
    {
        var low = 0;
        var high = values.Length;
        while (low < high)
        {
            var middle = low + (high - low) / 2;
            if (values[middle] < target) low = middle + 1;
            else high = middle;
        }
        return low;
    }

    private static bool CanFitSheet(SpriteProject project, int columns, int rows)
    {
        var width = (long)project.CellWidth * columns;
        var height = (long)project.CellHeight * rows;
        return columns > 0 && rows > 0 &&
            (long)columns * rows <= ProjectValidator.MaximumGridCellCount &&
            width <= 16_384 && height <= 16_384 && width * height <= ProjectValidator.MaximumSheetPixelCount &&
            CellEditService.CanRestoreSize(project, columns, rows);
    }
}
