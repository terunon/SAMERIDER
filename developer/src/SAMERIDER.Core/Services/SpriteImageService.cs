using SkiaSharp;
using SAMERIDER.Core.Models;

namespace SAMERIDER.Core.Services;

public sealed record ImportedSprite(string AssetPath, int Width, int Height, IReadOnlyList<SpriteCell> Cells, int RequiredColumns, int RequiredRows);

public static class SpriteImageService
{
    private const int QuantizationBinCount = 1 << 19;

    private struct ColorBinStats
    {
        public ulong Count;
        public ulong Red;
        public ulong Green;
        public ulong Blue;
        public ulong Alpha;
    }

    private readonly record struct ColorBox(int Start, int Count, int Channel, int Range, ulong Weight);

    private sealed class BinChannelComparer(int channel) : IComparer<int>
    {
        public int Compare(int left, int right) => GetBinChannel(left, channel).CompareTo(GetBinChannel(right, channel));
    }

    private static int GetBinChannel(int index, int channel) => channel switch
    {
        0 => (index >> 15) & 0xF,
        1 => (index >> 10) & 0x1F,
        2 => (index >> 5) & 0x1F,
        _ => index & 0x1F
    };

    private static int GetColorBinIndex(byte red, byte green, byte blue, byte alpha) =>
        (alpha >> 4 << 15) | (red >> 3 << 10) | (green >> 3 << 5) | (blue >> 3);

    public static (int Width, int Height) GetPngSize(string path)
    {
        if (!string.Equals(Path.GetExtension(path), ".png", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("対応している画像形式はPNGのみです。選択したファイルの拡張子はPNGではありません。");
        if (!HasPngSignature(path))
            throw new InvalidDataException("ファイルの拡張子はPNGですが、PNG形式のデータではありません。ファイルが破損している可能性があります。");
        using var codec = SKCodec.Create(path) ?? throw new InvalidDataException("PNGヘッダーを解析できません。画像が破損しているか、対応していないPNG形式の可能性があります。");
        if (codec.EncodedFormat != SKEncodedImageFormat.Png)
            throw new InvalidDataException($"画像デコーダーがPNG以外の形式として認識しました（{codec.EncodedFormat}）。PNGとして保存し直してください。");
        return (codec.Info.Width, codec.Info.Height);
    }

    public static (int Width, int Height) GetPngSize(ReadOnlyMemory<byte> pngBytes)
    {
        using var data = SKData.CreateCopy(pngBytes.Span);
        using var codec = SKCodec.Create(data) ?? throw new InvalidDataException("PNGヘッダーを解析できません。画像が破損しているか、対応していないPNG形式の可能性があります。");
        if (codec.EncodedFormat != SKEncodedImageFormat.Png)
            throw new InvalidDataException($"画像デコーダーがPNG以外の形式として認識しました（{codec.EncodedFormat}）。PNGとして保存し直してください。");
        return (codec.Info.Width, codec.Info.Height);
    }

    public static (SKBitmap Bitmap, float ScaleX, float ScaleY) DecodeForPreview(string path, int maximumDimension)
    {
        if (maximumDimension <= 0) throw new ArgumentOutOfRangeException(nameof(maximumDimension));
        var (originalWidth, originalHeight) = GetPngSize(path);
        var scale = Math.Min(1f, (float)maximumDimension / Math.Max(originalWidth, originalHeight));
        var targetWidth = Math.Max(1, (int)MathF.Round(originalWidth * scale));
        var targetHeight = Math.Max(1, (int)MathF.Round(originalHeight * scale));
        var info = new SKImageInfo(targetWidth, targetHeight, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var codec = SKCodec.Create(path) ?? throw new InvalidDataException("PNG画像のデコーダーを初期化できません。画像が破損している可能性があります。");
        var bitmap = new SKBitmap(info);
        SKCodecResult result;
        try
        {
            result = codec.GetPixels(info, bitmap.GetPixels());
        }
        catch
        {
            bitmap.Dispose();
            throw;
        }
        if (result == SKCodecResult.Success)
            return (bitmap, (float)bitmap.Width / originalWidth, (float)bitmap.Height / originalHeight);

        bitmap.Dispose();
        if (result != SKCodecResult.InvalidScale)
            throw new InvalidDataException(GetDecodeFailureMessage(result));

        return DecodeAtOriginalSizeAndScale(path, info, originalWidth, originalHeight);
    }

    public static ImportedSprite ImportPng(string sourcePath, string projectDirectory, SpriteProject project, int startX, int startY, bool trimRightAndBottom = false)
    {
        ProjectValidator.Validate(project);
        var originalName = Path.GetFileName(sourcePath);
        if (!ProjectValidator.IsSafeFileName(originalName))
            throw new InvalidDataException("画像ファイル名に使用できない文字が含まれています。");
        if (startX < 0 || startY < 0 || startX >= project.Columns || startY >= project.Rows)
            throw new ArgumentOutOfRangeException(nameof(startX), "配置先のセルが分割範囲外です。");

        var (imageWidth, imageHeight) = GetPngSize(sourcePath);
        var assetsDirectory = Path.Combine(projectDirectory, ProjectValidator.ImportRootDirectoryName, project.ImportFolderName);
        Directory.CreateDirectory(assetsDirectory);
        var uniqueFileName = GetUniqueFileName(assetsDirectory, originalName);
        var destination = Path.Combine(assetsDirectory, uniqueFileName);
        try
        {
            File.Copy(sourcePath, destination, false);
            var relativePath = $"{ProjectValidator.ImportRootDirectoryName}/{project.ImportFolderName}/{uniqueFileName}";
            return SlicePng(originalName, relativePath, imageWidth, imageHeight, project, startX, startY, trimRightAndBottom);
        }
        catch
        {
            try { File.Delete(destination); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            throw;
        }
    }

    public static ImportedSprite SlicePng(ReadOnlyMemory<byte> pngBytes, string originalName, SpriteProject project,
        int startX, int startY, bool trimRightAndBottom = false)
    {
        ProjectValidator.Validate(project);
        if (!ProjectValidator.IsSafeFileName(originalName) || !string.Equals(Path.GetExtension(originalName), ".png", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("画像ファイル名に使用できない文字が含まれているか、PNGファイルではありません。");
        if (startX < 0 || startY < 0 || startX >= project.Columns || startY >= project.Rows)
            throw new ArgumentOutOfRangeException(nameof(startX), "配置先のセルが分割範囲外です。");
        var (imageWidth, imageHeight) = GetPngSize(pngBytes);
        var uniqueName = $"{Path.GetFileNameWithoutExtension(originalName)}_{Guid.NewGuid():N}.png";
        var relativePath = $"{ProjectValidator.ImportRootDirectoryName}/{project.ImportFolderName}/{uniqueName}";
        return SlicePng(originalName, relativePath, imageWidth, imageHeight, project, startX, startY, trimRightAndBottom);
    }

    private static ImportedSprite SlicePng(string originalName, string relativePath, int imageWidth, int imageHeight,
        SpriteProject project, int startX, int startY, bool trimRightAndBottom)
    {
        if (!trimRightAndBottom && (imageWidth % project.CellWidth != 0 || imageHeight % project.CellHeight != 0))
            throw new InvalidDataException($"画像サイズは現在のセルサイズの整数倍にしてください。\n画像: {imageWidth} × {imageHeight} px\nセル: {project.CellWidth} × {project.CellHeight} px");

        var tileColumns = imageWidth / project.CellWidth;
        var tileRows = imageHeight / project.CellHeight;
        if (tileColumns == 0 || tileRows == 0)
            throw new InvalidDataException("セルサイズが画像より大きいため、セルを作成できません。");
        if ((long)tileColumns * tileRows > ProjectValidator.MaximumGridCellCount)
            throw new InvalidDataException("一度に取り込めるセル数を超えています。");

        var baseName = Path.GetFileNameWithoutExtension(originalName);
        var cells = new List<SpriteCell>(tileColumns * tileRows);
        for (var y = 0; y < tileRows; y++)
        for (var x = 0; x < tileColumns; x++)
        {
            var sequence = y * tileColumns + x + 1;
            var displayName = tileColumns == 1 && tileRows == 1 ? originalName : $"{baseName}_{sequence:00}";
            cells.Add(new SpriteCell
            {
                X = startX + x,
                Y = startY + y,
                AssetPath = relativePath,
                SourceRect = new PixelRect(x * project.CellWidth, y * project.CellHeight, project.CellWidth, project.CellHeight),
                DisplayName = displayName
            });
        }
        return new ImportedSprite(relativePath, imageWidth, imageHeight, cells,
            Math.Max(project.Columns, startX + tileColumns), Math.Max(project.Rows, startY + tileRows));
    }

    public static void ResizeCells(SpriteProject project, int newWidth, int newHeight, ResizeAnchor anchor)
    {
        if (newWidth <= 0 || newHeight <= 0) throw new ArgumentOutOfRangeException(nameof(newWidth));
        var (anchorX, anchorY) = GetAnchorPosition(anchor);
        var next = project.DeepClone();
        foreach (var cell in next.Cells)
        {
            var rect = cell.SourceRect;
            var deltaX = (int)Math.Floor((rect.Width - newWidth) * anchorX);
            var deltaY = (int)Math.Floor((rect.Height - newHeight) * anchorY);
            cell.SourceRect = new PixelRect(rect.X + deltaX, rect.Y + deltaY, newWidth, newHeight);
        }
        next.CellWidth = newWidth;
        next.CellHeight = newHeight;
        ProjectValidator.Validate(next);
        CopyProject(next, project);
    }

    public static unsafe PixelRect? FindCommonOpaqueBounds(SpriteProject project, string projectDirectory, CancellationToken cancellationToken = default)
    {
        ProjectValidator.Validate(project, projectDirectory);
        var left = project.CellWidth;
        var top = project.CellHeight;
        var right = 0;
        var bottom = 0;
        var hasOpaquePixels = false;

        foreach (var assetGroup in project.Cells.GroupBy(cell => cell.AssetPath, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var assetPath = ProjectValidator.ResolveAssetPath(projectDirectory, assetGroup.Key);
            using var source = SKBitmap.Decode(assetPath) ?? throw new InvalidDataException($"画像ファイルを読み込めません: {assetGroup.Key}");
            foreach (var cell in assetGroup)
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var rendered = RenderCell(project, cell, source);
                var pixels = (byte*)rendered.GetPixels().ToPointer();
                var cellLeft = project.CellWidth;
                var cellTop = project.CellHeight;
                var cellRight = 0;
                var cellBottom = 0;
                var isSolidColorCell = true;
                var hasReferenceColor = false;
                byte referenceBlue = 0;
                byte referenceGreen = 0;
                byte referenceRed = 0;
                byte referenceAlpha = 0;
                for (var y = 0; y < rendered.Height; y++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var row = pixels + y * rendered.RowBytes;
                    for (var x = 0; x < rendered.Width; x++)
                    {
                        var pixel = row + x * 4;
                        var blue = pixel[0];
                        var green = pixel[1];
                        var red = pixel[2];
                        var alpha = pixel[3];
                        if (alpha == 0)
                        {
                            isSolidColorCell = false;
                            continue;
                        }
                        if (!hasReferenceColor)
                        {
                            referenceBlue = blue;
                            referenceGreen = green;
                            referenceRed = red;
                            referenceAlpha = alpha;
                            hasReferenceColor = true;
                        }
                        else if (blue != referenceBlue || green != referenceGreen || red != referenceRed || alpha != referenceAlpha)
                        {
                            isSolidColorCell = false;
                        }
                        cellLeft = Math.Min(cellLeft, x);
                        cellTop = Math.Min(cellTop, y);
                        cellRight = Math.Max(cellRight, x + 1);
                        cellBottom = Math.Max(cellBottom, y + 1);
                    }
                }
                if (isSolidColorCell || cellRight <= cellLeft || cellBottom <= cellTop) continue;
                left = Math.Min(left, cellLeft);
                top = Math.Min(top, cellTop);
                right = Math.Max(right, cellRight);
                bottom = Math.Max(bottom, cellBottom);
                hasOpaquePixels = true;
            }
        }

        return hasOpaquePixels ? new PixelRect(left, top, right - left, bottom - top) : null;
    }

    public static SKBitmap RenderCell(SpriteProject project, SpriteCell? cell, string projectDirectory, SKColor? tint = null)
    {
        if (cell is null) return RenderCell(project, null, (SKBitmap?)null, tint);
        var asset = ProjectValidator.ResolveAssetPath(projectDirectory, cell.AssetPath);
        using var source = SKBitmap.Decode(asset) ?? throw new InvalidDataException($"画像ファイルを読み込めません: {cell.AssetPath}");
        return RenderCell(project, cell, source, tint);
    }

    public static SKBitmap RenderCell(SpriteProject project, SpriteCell? cell, SKBitmap? source, SKColor? tint = null)
    {
        var output = new SKBitmap(project.CellWidth, project.CellHeight, SKColorType.Bgra8888, SKAlphaType.Premul);
        output.Erase(SKColors.Transparent);
        if (cell is null || source is null) return output;
        var sourceRect = cell.SourceRect;
        if (tint is null)
        {
            using var canvas = new SKCanvas(output);
            using var paint = new SKPaint();
            using var image = SKImage.FromBitmap(source);
            DrawCell(canvas, image, cell, 1f, paint);
            return output;
        }
        for (var y = 0; y < project.CellHeight; y++)
        for (var x = 0; x < project.CellWidth; x++)
        {
            var sx = (long)sourceRect.X + x - cell.OffsetX;
            var sy = (long)sourceRect.Y + y - cell.OffsetY;
            if (sx < 0 || sy < 0 || sx >= source.Width || sy >= source.Height) continue;
            var color = source.GetPixel((int)sx, (int)sy);
            if (tint is { } shade)
            {
                const float colorBlend = 0.28f;
                color = new SKColor(
                    (byte)(color.Red + (shade.Red - color.Red) * colorBlend),
                    (byte)(color.Green + (shade.Green - color.Green) * colorBlend),
                    (byte)(color.Blue + (shade.Blue - color.Blue) * colorBlend),
                    (byte)(color.Alpha * shade.Alpha / 255));
            }
            output.SetPixel(x, y, color);
        }
        return output;
    }

    public static SKBitmap RenderCellThumbnail(SpriteProject project, SpriteCell cell, SKBitmap source, int maximumSize,
        float sourceScaleX = 1f, float sourceScaleY = 1f, SKColor? tint = null)
    {
        var scale = Math.Min(1f, Math.Min((float)maximumSize / project.CellWidth, (float)maximumSize / project.CellHeight));
        var width = Math.Max(1, (int)MathF.Round(project.CellWidth * scale));
        var height = Math.Max(1, (int)MathF.Round(project.CellHeight * scale));
        var output = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
        output.Erase(SKColors.Transparent);
        using var canvas = new SKCanvas(output);
        using var paint = new SKPaint();
        using var image = SKImage.FromBitmap(source);
        DrawCell(canvas, image, cell, scale, paint, sourceScaleX: sourceScaleX, sourceScaleY: sourceScaleY);
        canvas.Flush();
        if (tint is { } shade)
        {
            for (var y = 0; y < output.Height; y++)
            for (var x = 0; x < output.Width; x++)
            {
                var color = output.GetPixel(x, y);
                output.SetPixel(x, y, new SKColor(
                    (byte)(color.Red + (shade.Red - color.Red) * 0.28f),
                    (byte)(color.Green + (shade.Green - color.Green) * 0.28f),
                    (byte)(color.Blue + (shade.Blue - color.Blue) * 0.28f),
                    (byte)(color.Alpha * shade.Alpha / 255)));
            }
        }
        return output;
    }

    public static SKBitmap Export(SpriteProject project, string projectDirectory, IProgress<int>? cellProgress = null)
    {
        ProjectValidator.Validate(project, projectDirectory);
        var output = new SKBitmap(project.CellWidth * project.Columns, project.CellHeight * project.Rows, SKColorType.Bgra8888, SKAlphaType.Premul);
        try
        {
            output.Erase(SKColors.Transparent);
            using var canvas = new SKCanvas(output);
            using var paint = new SKPaint();
            var completedCells = 0;
            var totalCells = project.Cells.Count;
            var reportInterval = Math.Max(1, totalCells / 100);
            foreach (var assetGroup in project.Cells.GroupBy(cell => cell.AssetPath, StringComparer.Ordinal))
            {
                var assetPath = ProjectValidator.ResolveAssetPath(projectDirectory, assetGroup.Key);
                using var source = SKBitmap.Decode(assetPath) ?? throw new InvalidDataException($"画像ファイルを読み込めません: {assetGroup.Key}");
                using var image = SKImage.FromBitmap(source);
                foreach (var cell in assetGroup)
                {
                    var tileLeft = cell.X * project.CellWidth;
                    var tileTop = cell.Y * project.CellHeight;
                    canvas.Save();
                    canvas.ClipRect(new SKRect(tileLeft, tileTop, tileLeft + project.CellWidth, tileTop + project.CellHeight));
                    DrawCell(canvas, image, cell, 1f, paint, tileLeft, tileTop);
                    canvas.Restore();
                    completedCells++;
                    if (completedCells == totalCells || completedCells % reportInterval == 0) cellProgress?.Report(completedCells);
                }
            }
            canvas.Flush();
            return output;
        }
        catch
        {
            output.Dispose();
            throw;
        }
    }

    public static SKBitmap Export(SpriteProject project, IReadOnlyDictionary<string, SKBitmap> sourceImages,
        IProgress<int>? cellProgress = null)
    {
        ProjectValidator.Validate(project);
        foreach (var asset in project.Assets)
        {
            if (!sourceImages.TryGetValue(asset.RelativePath, out var source))
                throw new InvalidDataException($"プロジェクト画像が見つかりません: {asset.RelativePath}");
            if (source.Width != asset.Width || source.Height != asset.Height)
                throw new InvalidDataException($"記録された画像サイズと実際のサイズが一致しません: {asset.RelativePath}");
        }

        var output = new SKBitmap(project.CellWidth * project.Columns, project.CellHeight * project.Rows,
            SKColorType.Bgra8888, SKAlphaType.Premul);
        try
        {
            output.Erase(SKColors.Transparent);
            using var canvas = new SKCanvas(output);
            using var paint = new SKPaint();
            var completedCells = 0;
            var totalCells = project.Cells.Count;
            var reportInterval = Math.Max(1, totalCells / 100);
            foreach (var assetGroup in project.Cells.GroupBy(cell => cell.AssetPath, StringComparer.Ordinal))
            {
                using var image = SKImage.FromBitmap(sourceImages[assetGroup.Key]);
                foreach (var cell in assetGroup)
                {
                    var tileLeft = cell.X * project.CellWidth;
                    var tileTop = cell.Y * project.CellHeight;
                    canvas.Save();
                    canvas.ClipRect(new SKRect(tileLeft, tileTop, tileLeft + project.CellWidth, tileTop + project.CellHeight));
                    DrawCell(canvas, image, cell, 1f, paint, tileLeft, tileTop);
                    canvas.Restore();
                    completedCells++;
                    if (completedCells == totalCells || completedCells % reportInterval == 0)
                        cellProgress?.Report(completedCells);
                }
            }
            canvas.Flush();
            return output;
        }
        catch
        {
            output.Dispose();
            throw;
        }
    }

    public static SKBitmap ExportSelectedCells(
        SKBitmap fullSheet,
        int cellWidth,
        int cellHeight,
        IReadOnlyCollection<(int X, int Y)> selectedPositions,
        CancellationToken cancellationToken = default)
    {
        if (cellWidth <= 0 || cellHeight <= 0) throw new ArgumentOutOfRangeException(nameof(cellWidth));
        if (selectedPositions.Count == 0) throw new ArgumentException("少なくとも1セルを選択してください。", nameof(selectedPositions));

        var minX = selectedPositions.Min(position => position.X);
        var minY = selectedPositions.Min(position => position.Y);
        var maxX = selectedPositions.Max(position => position.X);
        var maxY = selectedPositions.Max(position => position.Y);
        var outputWidth = checked((maxX - minX + 1) * cellWidth);
        var outputHeight = checked((maxY - minY + 1) * cellHeight);
        if (minX < 0 || minY < 0 || (long)(maxX + 1) * cellWidth > fullSheet.Width || (long)(maxY + 1) * cellHeight > fullSheet.Height)
            throw new ArgumentOutOfRangeException(nameof(selectedPositions), "選択位置がスプライトシートの範囲外です。");

        var output = new SKBitmap(outputWidth, outputHeight, SKColorType.Bgra8888, SKAlphaType.Premul);
        try
        {
            output.Erase(SKColors.Transparent);
            using var canvas = new SKCanvas(output);
            foreach (var position in selectedPositions.Distinct())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var source = new SKRect(position.X * cellWidth, position.Y * cellHeight,
                    (position.X + 1) * cellWidth, (position.Y + 1) * cellHeight);
                var destination = new SKRect((position.X - minX) * cellWidth, (position.Y - minY) * cellHeight,
                    (position.X - minX + 1) * cellWidth, (position.Y - minY + 1) * cellHeight);
                canvas.DrawBitmap(fullSheet, source, destination);
            }
            canvas.Flush();
            return output;
        }
        catch
        {
            output.Dispose();
            throw;
        }
    }

    public static void SavePng(SKBitmap bitmap, string path)
    {
        if (!ProjectValidator.IsSafePngOutputFileName(Path.GetFileName(path)))
            throw new InvalidDataException("出力ファイル名には半角文字を使用してください。ファイル名に使えない記号や全角文字は使用できません。");
        if (!string.Equals(Path.GetExtension(path), ".png", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("スプライトシートの拡張子は .png にしてください。");
        using var stream = File.Create(path);
        using var data = EncodePng(bitmap);
        data.SaveTo(stream);
    }

    public static SKData EncodePng(SKBitmap bitmap) =>
        bitmap.Encode(SKEncodedImageFormat.Png, 100) ?? throw new InvalidOperationException("スプライトシートをPNG形式に変換できませんでした。");

    public static unsafe SKBitmap Quantize(SKBitmap source, int maximumColors, CancellationToken cancellationToken = default)
    {
        if (maximumColors is < 2 or > 256) throw new ArgumentOutOfRangeException(nameof(maximumColors));
        if (source.ColorType != SKColorType.Bgra8888 || source.AlphaType != SKAlphaType.Premul)
            throw new ArgumentException("減色対象はBgra8888/Premul形式である必要があります。", nameof(source));

        var histogram = new ColorBinStats[QuantizationBinCount];
        var bins = new List<int>(Math.Min(QuantizationBinCount, source.Width * source.Height));
        var sourcePixels = (byte*)source.GetPixels().ToPointer();
        for (var y = 0; y < source.Height; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var row = sourcePixels + y * source.RowBytes;
            for (var x = 0; x < source.Width; x++)
            {
                var pixel = row + x * 4;
                var alpha = pixel[3];
                var red = alpha == 0 ? (byte)0 : (byte)Math.Min(255, (pixel[2] * 255 + alpha / 2) / alpha);
                var green = alpha == 0 ? (byte)0 : (byte)Math.Min(255, (pixel[1] * 255 + alpha / 2) / alpha);
                var blue = alpha == 0 ? (byte)0 : (byte)Math.Min(255, (pixel[0] * 255 + alpha / 2) / alpha);
                var index = GetColorBinIndex(red, green, blue, alpha);
                ref var stats = ref histogram[index];
                if (stats.Count == 0) bins.Add(index);
                stats.Count++;
                stats.Red += red;
                stats.Green += green;
                stats.Blue += blue;
                stats.Alpha += alpha;
            }
        }

        if (bins.Count == 0)
        {
            var transparent = new SKBitmap(source.Width, source.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
            transparent.Erase(SKColors.Transparent);
            return transparent;
        }

        var boxes = new List<ColorBox> { FindColorBox(bins, histogram, 0, bins.Count) };
        while (boxes.Count < maximumColors)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var splitBox = -1;
            ulong bestScore = 0;
            for (var index = 0; index < boxes.Count; index++)
            {
                var box = boxes[index];
                if (box.Range == 0 || box.Count < 2) continue;
                var score = box.Weight * (uint)box.Range;
                if (score <= bestScore) continue;
                bestScore = score;
                splitBox = index;
            }
            if (splitBox < 0) break;

            var selectedBox = boxes[splitBox];
            bins.Sort(selectedBox.Start, selectedBox.Count, new BinChannelComparer(selectedBox.Channel));
            var halfWeight = (selectedBox.Weight + 1) / 2;
            ulong accumulated = 0;
            var leftCount = 0;
            while (leftCount < selectedBox.Count - 1 && accumulated < halfWeight)
            {
                accumulated += histogram[bins[selectedBox.Start + leftCount]].Count;
                leftCount++;
            }
            var rightCount = selectedBox.Count - leftCount;
            var left = FindColorBox(bins, histogram, selectedBox.Start, leftCount);
            var right = FindColorBox(bins, histogram, selectedBox.Start + leftCount, rightCount);
            boxes[splitBox] = left;
            boxes.Add(right);
        }

        var palette = new SKColor[boxes.Count];
        var binToPalette = new byte[QuantizationBinCount];
        for (var paletteIndex = 0; paletteIndex < boxes.Count; paletteIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var box = boxes[paletteIndex];
            ulong count = 0, red = 0, green = 0, blue = 0, alpha = 0;
            for (var index = box.Start; index < box.Start + box.Count; index++)
            {
                var bin = bins[index];
                ref var stats = ref histogram[bin];
                count += stats.Count;
                red += stats.Red;
                green += stats.Green;
                blue += stats.Blue;
                alpha += stats.Alpha;
                binToPalette[bin] = (byte)paletteIndex;
            }
            palette[paletteIndex] = new SKColor((byte)(red / count), (byte)(green / count), (byte)(blue / count), (byte)(alpha / count));
        }

        var result = new SKBitmap(source.Width, source.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        try
        {
            var destinationPixels = (byte*)result.GetPixels().ToPointer();
            for (var y = 0; y < source.Height; y++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var sourceRow = sourcePixels + y * source.RowBytes;
                var destinationRow = destinationPixels + y * result.RowBytes;
                for (var x = 0; x < source.Width; x++)
                {
                    var sourcePixel = sourceRow + x * 4;
                    var alpha = sourcePixel[3];
                    var red = alpha == 0 ? (byte)0 : (byte)Math.Min(255, (sourcePixel[2] * 255 + alpha / 2) / alpha);
                    var green = alpha == 0 ? (byte)0 : (byte)Math.Min(255, (sourcePixel[1] * 255 + alpha / 2) / alpha);
                    var blue = alpha == 0 ? (byte)0 : (byte)Math.Min(255, (sourcePixel[0] * 255 + alpha / 2) / alpha);
                    var color = palette[binToPalette[GetColorBinIndex(red, green, blue, alpha)]];
                    var outputPixel = destinationRow + x * 4;
                    outputPixel[0] = (byte)((color.Blue * color.Alpha + 127) / 255);
                    outputPixel[1] = (byte)((color.Green * color.Alpha + 127) / 255);
                    outputPixel[2] = (byte)((color.Red * color.Alpha + 127) / 255);
                    outputPixel[3] = color.Alpha;
                }
            }
            return result;
        }
        catch
        {
            result.Dispose();
            throw;
        }
    }

    private static ColorBox FindColorBox(List<int> bins, ColorBinStats[] histogram, int start, int count)
    {
        Span<int> minimum = stackalloc int[4] { int.MaxValue, int.MaxValue, int.MaxValue, int.MaxValue };
        Span<int> maximum = stackalloc int[4] { int.MinValue, int.MinValue, int.MinValue, int.MinValue };
        ulong weight = 0;
        for (var index = start; index < start + count; index++)
        {
            var bin = bins[index];
            weight += histogram[bin].Count;
            for (var channel = 0; channel < 4; channel++)
            {
                var value = GetBinChannel(bin, channel);
                minimum[channel] = Math.Min(minimum[channel], value);
                maximum[channel] = Math.Max(maximum[channel], value);
            }
        }
        var splitChannel = 0;
        var range = -1;
        for (var channel = 0; channel < 4; channel++)
        {
            var channelRange = maximum[channel] - minimum[channel];
            if (channelRange <= range) continue;
            splitChannel = channel;
            range = channelRange;
        }
        return new ColorBox(start, count, splitChannel, Math.Max(0, range), weight);
    }

    private static string GetDecodeFailureMessage(SKCodecResult result) => result switch
    {
        SKCodecResult.IncompleteInput => "PNG画像データが途中で終わっています。ファイルが不完全または破損している可能性があります。",
        SKCodecResult.ErrorInInput => "PNG画像データの読み取り中にデコードエラーが発生しました。ファイルが破損している可能性があります。",
        SKCodecResult.InvalidInput => "PNG画像の内容が無効です。ファイルが破損しているか、PNGとして正しく保存されていない可能性があります。",
        SKCodecResult.InvalidConversion => "PNG画像の色形式をアプリで扱える形式に変換できません。画像編集ソフトで一般的なRGBA/RGB PNGとして保存し直してください。",
        SKCodecResult.InvalidScale => "元サイズでPNG画像をデコードできませんでした。",
        SKCodecResult.InvalidParameters => "PNGデコーダーが画像サイズまたはピクセル形式を受け付けませんでした。画像サイズや形式を変更して再度お試しください。",
        SKCodecResult.CouldNotRewind => "PNG画像データを先頭から読み直せませんでした。ファイルを閉じてから再度お試しください。",
        SKCodecResult.InternalError => "PNGデコーダー内部でエラーが発生しました。画像を別のPNG形式で保存し直してください。",
        _ => $"PNGデコードに失敗しました（デコーダー結果: {result}）。画像が破損しているか、未対応のPNG形式の可能性があります。"
    };

    private static (SKBitmap Bitmap, float ScaleX, float ScaleY) DecodeAtOriginalSizeAndScale(
        string path, SKImageInfo targetInfo, int originalWidth, int originalHeight)
    {
        using var codec = SKCodec.Create(path)
            ?? throw new InvalidDataException("元サイズでPNG画像を読み込むためのデコーダーを初期化できません。");
        var sourceInfo = new SKImageInfo(originalWidth, originalHeight, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var sourceBitmap = new SKBitmap(sourceInfo);
        var result = codec.GetPixels(sourceInfo, sourceBitmap.GetPixels());
        if (result != SKCodecResult.Success)
            throw new InvalidDataException($"プレビュー用の縮小デコードと元サイズのデコードの両方に失敗しました。{Environment.NewLine}原因: {GetDecodeFailureMessage(result)}");

        var scaledBitmap = sourceBitmap.Resize(targetInfo, new SKSamplingOptions(SKFilterMode.Linear))
            ?? throw new InvalidDataException("元サイズではPNGを読み込めましたが、プレビュー画像への縮小に失敗しました。");
        return (scaledBitmap, (float)scaledBitmap.Width / originalWidth, (float)scaledBitmap.Height / originalHeight);
    }

    private static bool HasPngSignature(string path)
    {
        if (!string.Equals(Path.GetExtension(path), ".png", StringComparison.OrdinalIgnoreCase)) return false;
        Span<byte> signature = stackalloc byte[8];
        using var stream = File.OpenRead(path);
        try { stream.ReadExactly(signature); }
        catch (EndOfStreamException) { return false; }
        return signature.SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
    }

    private static string GetUniqueFileName(string directory, string name)
    {
        var stem = Path.GetFileNameWithoutExtension(name);
        var extension = Path.GetExtension(name);
        var candidate = name;
        var index = 2;
        while (File.Exists(Path.Combine(directory, candidate))) candidate = $"{stem}_{index++}{extension}";
        return candidate;
    }

    private static void DrawCell(SKCanvas canvas, SKImage source, SpriteCell cell, float scale, SKPaint paint, float originX = 0f, float originY = 0f,
        float sourceScaleX = 1f, float sourceScaleY = 1f)
    {
        var sourceRect = cell.SourceRect;
        var sourceBounds = new SKRect(sourceRect.X * sourceScaleX, sourceRect.Y * sourceScaleY,
            (sourceRect.X + sourceRect.Width) * sourceScaleX, (sourceRect.Y + sourceRect.Height) * sourceScaleY);
        var destinationLeft = originX + cell.OffsetX * scale;
        var destinationTop = originY + cell.OffsetY * scale;
        var destination = new SKRect(destinationLeft, destinationTop,
            destinationLeft + sourceRect.Width * scale, destinationTop + sourceRect.Height * scale);
        canvas.DrawImage(source, sourceBounds, destination, new SKSamplingOptions(SKFilterMode.Linear), paint);
    }

    private static (double X, double Y) GetAnchorPosition(ResizeAnchor anchor) => anchor switch
    {
        ResizeAnchor.TopLeft => (0, 0), ResizeAnchor.Top => (0.5, 0), ResizeAnchor.TopRight => (1, 0),
        ResizeAnchor.Left => (0, 0.5), ResizeAnchor.Center => (0.5, 0.5), ResizeAnchor.Right => (1, 0.5),
        ResizeAnchor.BottomLeft => (0, 1), ResizeAnchor.Bottom => (0.5, 1), ResizeAnchor.BottomRight => (1, 1),
        _ => throw new ArgumentOutOfRangeException(nameof(anchor))
    };

    private static void CopyProject(SpriteProject source, SpriteProject target)
    {
        target.FormatVersion = source.FormatVersion; target.Title = source.Title;
        target.CellWidth = source.CellWidth; target.CellHeight = source.CellHeight;
        target.Columns = source.Columns; target.Rows = source.Rows;
        target.Assets = source.Assets; target.Cells = source.Cells;
    }
}
