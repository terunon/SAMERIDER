namespace SAMERIDER.Core.Services;

public static class ImportCellSizeService
{
    public static (int CellWidth, int CellHeight) Suggest(int imageWidth, int imageHeight)
    {
        if (imageWidth <= 0) throw new ArgumentOutOfRangeException(nameof(imageWidth));
        if (imageHeight <= 0) throw new ArgumentOutOfRangeException(nameof(imageHeight));

        var divisor = GreatestCommonDivisor(imageWidth, imageHeight);
        var exactColumns = imageWidth / divisor;
        var exactRows = imageHeight / divisor;
        if ((long)exactColumns * exactRows <= ProjectValidator.MaximumGridCellCount)
            return (divisor, divisor);

        var maximumColumns = Math.Min(imageWidth, ProjectValidator.MaximumGridCellCount);
        for (var columns = 1; columns <= maximumColumns; columns++)
        {
            var roundedRows = Math.Round((double)imageHeight * columns / imageWidth, MidpointRounding.AwayFromZero);
            if (roundedRows <= 0 || roundedRows > imageHeight) continue;
            var rows = (int)roundedRows;
            if ((long)columns * rows > ProjectValidator.MaximumGridCellCount) continue;

            var cellWidth = imageWidth / columns;
            var cellHeight = imageHeight / rows;
            if (cellWidth <= 0 || cellHeight <= 0 || imageWidth / cellWidth != columns || imageHeight / cellHeight != rows)
                continue;

            var smallerDimension = Math.Min(cellWidth, cellHeight);
            if (Math.Abs((long)cellWidth - cellHeight) <= Math.Max(1d, smallerDimension * 0.01d))
                return (cellWidth, cellHeight);
        }

        return (imageWidth, imageHeight);
    }

    private static int GreatestCommonDivisor(int left, int right)
    {
        while (right != 0)
        {
            var remainder = left % right;
            left = right;
            right = remainder;
        }
        return left;
    }
}
