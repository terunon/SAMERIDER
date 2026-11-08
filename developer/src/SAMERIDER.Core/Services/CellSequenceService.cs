using System.Text.RegularExpressions;
using SAMERIDER.Core.Models;

namespace SAMERIDER.Core.Services;

/// <summary>Describes the image data to apply to an adjacent frame without changing cell placement or edits.</summary>
public sealed record CellSequenceFrame(string AssetPath, PixelRect SourceRect, string DisplayName);

public static class CellSequenceService
{
    private static readonly Regex GifFrameName = new(@"^(?<prefix>.+)_(?<index>[0-9]{3,})(?<collision>_[0-9]+)?\.png$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex NumberedDisplayName = new(@"^(?<prefix>.+)_(?<index>[0-9]+)$", RegexOptions.CultureInvariant);

    /// <summary>Finds the previous (-1) or next (+1) imported image in the current cell's source sequence.</summary>
    public static bool TryGetNeighbor(SpriteProject project, SpriteCell cell, int direction, out CellSequenceFrame? frame)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(cell);
        if (direction is not (-1 or 1)) throw new ArgumentOutOfRangeException(nameof(direction));
        frame = null;

        var asset = project.Assets.FirstOrDefault(candidate => PathsEqual(candidate.RelativePath, cell.AssetPath));
        if (asset is null) return false;

        if (asset.SequenceId is not null)
            return TryGetMetadataGifNeighbor(project, asset, direction, out frame);
        if (GifFrameName.IsMatch(Path.GetFileName(cell.AssetPath)) && TryGetGifNeighbor(project, cell, direction, out frame))
            return true;

        // A single PNG asset is the sequence identity. Compute all row-major frames from its original
        // source grid, not from project.Cells (which can omit deleted frames) or the resized cell size.
        if (Path.GetExtension(asset.RelativePath).Equals(".png", StringComparison.OrdinalIgnoreCase) &&
            IsGridCell(cell.SourceRect, asset.Width, asset.Height))
        {
            var tileWidth = cell.SourceRect.Width;
            var tileHeight = cell.SourceRect.Height;
            if (tileWidth <= 0 || tileHeight <= 0 || cell.SourceRect.X % tileWidth != 0 || cell.SourceRect.Y % tileHeight != 0) return false;
            var columns = asset.Width / tileWidth;
            var rows = asset.Height / tileHeight;
            var count = (long)columns * rows;
            var currentIndex = (long)(cell.SourceRect.Y / tileHeight) * columns + cell.SourceRect.X / tileWidth;
            var targetIndex = currentIndex + direction;
            if (columns <= 0 || rows <= 0 || targetIndex < 0 || targetIndex >= count) return false;
            var displayNameMatch = NumberedDisplayName.Match(cell.DisplayName);
            if (!displayNameMatch.Success || !int.TryParse(displayNameMatch.Groups["index"].Value, out var displayIndex) ||
                displayIndex != currentIndex + 1) return false;
            var x = (int)(targetIndex % columns) * tileWidth;
            var y = (int)(targetIndex / columns) * tileHeight;
            var padding = displayNameMatch.Groups["index"].Value.Length;
            var name = $"{displayNameMatch.Groups["prefix"].Value}_{(targetIndex + 1).ToString().PadLeft(padding, '0')}";
            frame = new CellSequenceFrame(asset.RelativePath, new PixelRect(x, y, tileWidth, tileHeight), name);
            return true;
        }

        return TryGetGifNeighbor(project, cell, direction, out frame);
    }

    private static bool TryGetMetadataGifNeighbor(SpriteProject project, SpriteAsset asset, int direction, out CellSequenceFrame? frame)
    {
        frame = null;
        var sequenceId = asset.SequenceId;
        if (sequenceId is null || asset.SequenceIndex is not > 0) return false;
        var candidates = project.Assets.Where(item => string.Equals(item.SequenceId, sequenceId, StringComparison.Ordinal))
            .OrderBy(item => item.SequenceIndex).ToArray();
        if (candidates.Length < 2 || candidates.Any(item => item.SequenceIndex is null) ||
            candidates.Select(item => item.SequenceIndex).Distinct().Count() != candidates.Length ||
            candidates.Where((item, index) => item.SequenceIndex != index + 1).Any() ||
            candidates.Select(item => (item.Width, item.Height)).Distinct().Count() != 1) return false;
        var current = Array.FindIndex(candidates, item => PathsEqual(item.RelativePath, asset.RelativePath));
        var target = current + direction;
        if (current < 0 || target < 0 || target >= candidates.Length) return false;
        var next = candidates[target];
        frame = new CellSequenceFrame(next.RelativePath, new PixelRect(0, 0, next.Width, next.Height),
            next.SequenceDisplayName ?? Path.GetFileNameWithoutExtension(next.RelativePath));
        return true;
    }

    private static bool TryGetGifNeighbor(SpriteProject project, SpriteCell cell, int direction, out CellSequenceFrame? frame)
    {
        frame = null;
        var match = GifFrameName.Match(Path.GetFileName(cell.AssetPath));
        var currentAsset = project.Assets.FirstOrDefault(asset => PathsEqual(asset.RelativePath, cell.AssetPath));
        if (!match.Success || cell.SourceRect.X != 0 || cell.SourceRect.Y != 0 ||
            currentAsset is null || cell.SourceRect.Width != currentAsset.Width || cell.SourceRect.Height != currentAsset.Height)
            return false;
        var prefix = match.Groups["prefix"].Value;
        // Restrict membership to inventory assets with the same exact directory and basename prefix.
        var directory = Path.GetDirectoryName(cell.AssetPath)?.Replace('\\', '/') ?? string.Empty;
        var candidates = project.Assets.Select(asset => (Asset: asset, Match: GifFrameName.Match(Path.GetFileName(asset.RelativePath))))
            .Where(item => item.Match.Success &&
                string.Equals(Path.GetDirectoryName(item.Asset.RelativePath)?.Replace('\\', '/') ?? string.Empty, directory, StringComparison.Ordinal) &&
                string.Equals(item.Match.Groups["prefix"].Value, prefix, StringComparison.Ordinal))
            .Select(item => (item.Asset, Index: int.TryParse(item.Match.Groups["index"].Value, out var index) ? index : -1))
            .Where(item => item.Index > 0).OrderBy(item => item.Index).ToArray();
        // Imported GIFs are one-based, contiguous, and have uniform dimensions. Gaps/collisions make
        // basename-only identification ambiguous, so do not offer a potentially unrelated neighbor.
        if (candidates.Length < 2 || candidates.Select(item => item.Index).Distinct().Count() != candidates.Length ||
            candidates.Select(item => (item.Asset.Width, item.Asset.Height)).Distinct().Count() != 1 ||
            candidates.Where((item, index) => item.Index != index + 1).Any()) return false;
        var current = Array.FindIndex(candidates, item => PathsEqual(item.Asset.RelativePath, cell.AssetPath));
        var target = current + direction;
        if (current < 0 || target < 0 || target >= candidates.Length) return false;
        var next = candidates[target].Asset;
        frame = new CellSequenceFrame(next.RelativePath, new PixelRect(0, 0, next.Width, next.Height), Path.GetFileNameWithoutExtension(next.RelativePath));
        return true;
    }

    private static bool IsGridCell(PixelRect rect, int width, int height) => rect.X >= 0 && rect.Y >= 0 &&
        rect.Width > 0 && rect.Height > 0 && rect.X + (long)rect.Width <= width && rect.Y + (long)rect.Height <= height;

    private static bool PathsEqual(string left, string right) => string.Equals(left.Replace('\\', '/'), right.Replace('\\', '/'), StringComparison.Ordinal);
}
