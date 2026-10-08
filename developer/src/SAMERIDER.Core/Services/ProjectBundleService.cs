using System.IO.Compression;
using SAMERIDER.Core.Models;

namespace SAMERIDER.Core.Services;

public sealed record ProjectBundle(SpriteProject Project, IReadOnlyDictionary<string, byte[]> Assets);

public static class ProjectBundleService
{
    private const string ProjectEntryName = "project.json";
    private const long MaximumAssetBytes = 128L * 1024 * 1024;
    private const long MaximumBundleAssetBytes = 512L * 1024 * 1024;
    public const int MaximumBundleBytes = 640 * 1024 * 1024;

    public static byte[] CreateBundle(SpriteProject project, IReadOnlyDictionary<string, byte[]> assets)
    {
        ProjectValidator.Validate(project);
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            var projectEntry = archive.CreateEntry(ProjectEntryName, CompressionLevel.Optimal);
            using (var entryStream = projectEntry.Open())
            {
                var projectJson = ProjectStore.SerializeToUtf8Bytes(project);
                if (projectJson.Length > 16 * 1024 * 1024)
                    throw new InvalidDataException("プロジェクトデータが大きすぎます。");
                entryStream.Write(projectJson);
            }

            long totalAssetBytes = 0;
            foreach (var asset in project.Assets)
            {
                if (!assets.TryGetValue(asset.RelativePath, out var bytes))
                    throw new InvalidDataException($"プロジェクト画像が見つかりません: {asset.RelativePath}");
                ValidateAssetBytes(asset, bytes);
                totalAssetBytes += bytes.LongLength;
                if (totalAssetBytes > MaximumBundleAssetBytes)
                    throw new InvalidDataException("プロジェクト画像の合計サイズが大きすぎます。");

                var entry = archive.CreateEntry(asset.RelativePath, CompressionLevel.Optimal);
                using var entryStream = entry.Open();
                entryStream.Write(bytes);
            }
        }

        if (output.Length > MaximumBundleBytes)
            throw new InvalidDataException("プロジェクトbundleが大きすぎます。");
        return output.ToArray();
    }

    public static ProjectBundle OpenBundle(byte[] bundleBytes)
    {
        ArgumentNullException.ThrowIfNull(bundleBytes);
        if (bundleBytes.LongLength > MaximumBundleBytes)
            throw new InvalidDataException("プロジェクトbundleが大きすぎます。");
        using var input = new MemoryStream(bundleBytes, writable: false);
        using var archive = new ZipArchive(input, ZipArchiveMode.Read);
        if (archive.Entries.Count > ProjectValidator.MaximumGridCellCount + 1)
            throw new InvalidDataException("プロジェクトbundle内のファイル数が上限を超えています。");
        var entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.Ordinal);
        var windowsEntryNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in archive.Entries)
        {
            if (entry.FullName.EndsWith("/", StringComparison.Ordinal) ||
                !entries.TryAdd(entry.FullName, entry) || !windowsEntryNames.Add(entry.FullName))
                throw new InvalidDataException("プロジェクトbundle内のファイル名が重複または不正です。");
        }

        if (!entries.TryGetValue(ProjectEntryName, out var projectEntry))
            throw new InvalidDataException("プロジェクトbundleにproject.jsonがありません。");
        var projectJson = ReadEntryBytes(projectEntry, 16L * 1024 * 1024,
            "プロジェクトデータが大きすぎます。");
        var project = ProjectStore.Deserialize(projectJson);
        var assets = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var windowsAssetPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long totalAssetBytes = 0;
        foreach (var asset in project.Assets)
        {
            if (!windowsAssetPaths.Add(asset.RelativePath))
                throw new InvalidDataException("Windows上で衝突する画像ファイルのパスが含まれています。");
            if (!entries.TryGetValue(asset.RelativePath, out var entry))
                throw new InvalidDataException($"プロジェクト画像がbundle内にありません: {asset.RelativePath}");
            var remainingAssetBytes = MaximumBundleAssetBytes - totalAssetBytes;
            var maximumEntryBytes = Math.Min(MaximumAssetBytes, remainingAssetBytes);
            if (entry.Length > maximumEntryBytes)
                throw new InvalidDataException("プロジェクト画像の合計サイズが大きすぎます。");
            var bytes = ReadEntryBytes(entry, maximumEntryBytes,
                $"画像ファイルが大きすぎます: {asset.RelativePath}");
            totalAssetBytes += bytes.LongLength;
            ValidateAssetBytes(asset, bytes);
            assets.Add(asset.RelativePath, bytes);
        }

        if (entries.Count != assets.Count + 1)
            throw new InvalidDataException("プロジェクトbundleに未参照のファイルがあります。");

        ProjectValidator.Validate(project);
        return new ProjectBundle(project, assets);
    }

    private static byte[] ReadEntryBytes(ZipArchiveEntry entry, long maximumBytes, string tooLargeMessage)
    {
        if (entry.Length < 0 || entry.Length > maximumBytes)
            throw new InvalidDataException(tooLargeMessage);

        using var input = entry.Open();
        // Do not allocate the declared size up front: ZIP metadata is untrusted and
        // a small bundle can claim a very large uncompressed length.
        using var output = new MemoryStream((int)Math.Min(entry.Length, 1024 * 1024));
        var buffer = new byte[81920];
        long totalBytes = 0;
        int bytesRead;
        while ((bytesRead = input.Read(buffer, 0, buffer.Length)) > 0)
        {
            if (bytesRead > maximumBytes - totalBytes)
                throw new InvalidDataException(tooLargeMessage);
            output.Write(buffer, 0, bytesRead);
            totalBytes += bytesRead;
        }

        if (totalBytes != entry.Length)
            throw new InvalidDataException("プロジェクトbundle内のファイルサイズ情報が一致しません。");
        return output.ToArray();
    }

    private static void ValidateAssetBytes(SpriteAsset asset, byte[] bytes)
    {
        if (bytes.LongLength > MaximumAssetBytes)
            throw new InvalidDataException($"画像ファイルが大きすぎます: {asset.RelativePath}");
        var (width, height) = SpriteImageService.GetPngSize(bytes);
        if (width <= 0 || height <= 0 || width > 16_384 || height > 16_384 ||
            (long)width * height > ProjectValidator.MaximumSheetPixelCount)
            throw new InvalidDataException($"画像サイズが上限を超えています: {asset.RelativePath}");
        if (asset.Width != width || asset.Height != height)
            throw new InvalidDataException($"記録された画像サイズと実際のサイズが一致しません: {asset.RelativePath}");
    }
}
