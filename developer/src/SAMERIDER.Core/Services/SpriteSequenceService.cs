namespace SAMERIDER.Core.Services;

/// <summary>Provides deterministic playback settings and cell ordering for a sprite sequence preview.</summary>
public static class SpriteSequenceService
{
    public const int FramesPerSecond = 15;
    public static readonly TimeSpan FrameDuration = TimeSpan.FromSeconds(1d / FramesPerSecond);

    /// <summary>
    /// Returns unique in-grid positions in row-major order. Zero or one valid selected cell means the complete grid,
    /// including positions without an image; invalid selected positions are ignored.
    /// </summary>
    public static (int X, int Y)[] GetPositions(
        int columns,
        int rows,
        IReadOnlyCollection<(int X, int Y)> selection)
    {
        ArgumentNullException.ThrowIfNull(selection);
        if (columns <= 0) throw new ArgumentOutOfRangeException(nameof(columns), "列数は1以上で指定してください。");
        if (rows <= 0) throw new ArgumentOutOfRangeException(nameof(rows), "行数は1以上で指定してください。");
        if ((long)columns * rows > ProjectValidator.MaximumGridCellCount)
            throw new ArgumentOutOfRangeException(nameof(columns), "グリッドのセル数が上限を超えています。");

        var selected = selection
            .Where(position => position.X >= 0 && position.X < columns && position.Y >= 0 && position.Y < rows)
            .Distinct()
            .OrderBy(position => position.Y)
            .ThenBy(position => position.X)
            .ToArray();
        if (selected.Length > 1) return selected;

        var all = new (int X, int Y)[columns * rows];
        var index = 0;
        for (var y = 0; y < rows; y++)
        for (var x = 0; x < columns; x++)
            all[index++] = (x, y);
        return all;
    }
}
