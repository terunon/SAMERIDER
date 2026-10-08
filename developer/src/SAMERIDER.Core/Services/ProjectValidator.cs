using SAMERIDER.Core.Models;

namespace SAMERIDER.Core.Services;

public static class ProjectValidator
{
    public const string ImportRootDirectoryName = "SAMERIDER_Import";
    public const int MaximumGridCellCount = 10_000;
    public const long MaximumSheetPixelCount = 64_000_000;

    private static readonly char[] InvalidFileNameCharacters = ['<', '>', ':', '"', '/', '\\', '|', '?', '*'];

    public static bool IsSafeFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName) || fileName is "." or ".." ||
            fileName.Any(character => char.IsControl(character) || InvalidFileNameCharacters.Contains(character)) ||
            fileName.EndsWith(' ') || fileName.EndsWith('.')) return false;
        var deviceName = Path.GetFileNameWithoutExtension(fileName);
        return !deviceName.Equals("CON", StringComparison.OrdinalIgnoreCase) &&
            !deviceName.Equals("PRN", StringComparison.OrdinalIgnoreCase) &&
            !deviceName.Equals("AUX", StringComparison.OrdinalIgnoreCase) &&
            !deviceName.Equals("NUL", StringComparison.OrdinalIgnoreCase) &&
            !System.Text.RegularExpressions.Regex.IsMatch(deviceName, "^(COM|LPT)[1-9]$", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);
    }

    public static bool IsSafePngOutputFileName(string fileName) =>
        IsSafeFileName(fileName) && fileName.All(character => character <= 0x7E);

    public static void Validate(SpriteProject project, string? projectDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (project.FormatVersion != SpriteProject.CurrentFormatVersion)
            throw new InvalidDataException($"未対応のプロジェクト形式バージョンです: {project.FormatVersion}");
        if (!IsSafeFileName(project.Title))
            throw new InvalidDataException("スプライトシート名にファイル名として使用できない文字が含まれています。");
        if (!IsSafeFileName(project.ImportFolderName))
            throw new InvalidDataException("Importフォルダ名が正しくありません。");
        if (project.CellWidth <= 0 || project.CellHeight <= 0 || project.Columns <= 0 || project.Rows <= 0)
            throw new InvalidDataException("セルサイズと分割数は正の値にしてください。");
        if (project.Assets is null || project.Cells is null)
            throw new InvalidDataException("プロジェクトの画像データとセルデータの形式が正しくありません。");
        if (project.Assets.Count > MaximumGridCellCount)
            throw new InvalidDataException("プロジェクト内の画像ファイル数が上限を超えています。");
        var sheetWidth = (long)project.CellWidth * project.Columns;
        var sheetHeight = (long)project.CellHeight * project.Rows;
        if ((long)project.Columns * project.Rows > MaximumGridCellCount || sheetWidth > 16_384 || sheetHeight > 16_384 || sheetWidth * sheetHeight > MaximumSheetPixelCount)
            throw new InvalidDataException("スプライトシートが大きすぎます。セルサイズまたは分割数を小さくしてください。");

        var assets = new HashSet<string>(StringComparer.Ordinal);
        foreach (var asset in project.Assets)
        {
            var parts = asset.RelativePath.Split('/');
            var isLegacyAssetPath = parts.Length == 2 && parts[0] == "assets" && IsSafeFileName(parts[1]);
            var isImportAssetPath = parts.Length == 3 && parts[0] == ImportRootDirectoryName && parts[1] == project.ImportFolderName && IsSafeFileName(parts[2]);
            if ((!isLegacyAssetPath && !isImportAssetPath) || Path.IsPathRooted(asset.RelativePath) || asset.RelativePath.Contains('\\'))
                throw new InvalidDataException($"安全でない画像ファイルのパスです: {asset.RelativePath}");
            if (!assets.Add(asset.RelativePath))
                throw new InvalidDataException($"画像ファイルのパスが重複しています: {asset.RelativePath}");
            if (asset.Width <= 0 || asset.Height <= 0)
                throw new InvalidDataException($"画像サイズが正しくありません: {asset.RelativePath}");
            if (projectDirectory is not null)
            {
                var assetPath = ResolveAssetPath(projectDirectory, asset.RelativePath);
                if (!File.Exists(assetPath)) throw new FileNotFoundException($"プロジェクトの画像ファイルが見つかりません: {asset.RelativePath}");
                var (width, height) = SpriteImageService.GetPngSize(assetPath);
                if (asset.Width != width || asset.Height != height)
                    throw new InvalidDataException($"記録された画像サイズと実際のサイズが一致しません: {asset.RelativePath}");
            }
        }

        var occupied = new HashSet<(int X, int Y)>();
        foreach (var cell in project.Cells)
        {
            if (cell.X < 0 || cell.Y < 0 || cell.X >= project.Columns || cell.Y >= project.Rows)
                throw new InvalidDataException($"セルが分割範囲外です（{cell.X + 1}, {cell.Y + 1}）。");
            if (!occupied.Add((cell.X, cell.Y)))
                throw new InvalidDataException($"同じセルに複数の画像が割り当てられています（{cell.X + 1}, {cell.Y + 1}）。");
            if (!assets.Contains(cell.AssetPath))
                throw new InvalidDataException($"セルが未登録の画像を参照しています: {cell.AssetPath}");
            if (cell.SourceRect.Width != project.CellWidth || cell.SourceRect.Height != project.CellHeight)
                throw new InvalidDataException($"セルの切り出し範囲と現在のセルサイズが一致しません（{cell.X + 1}, {cell.Y + 1}）。");
            if ((long)cell.SourceRect.X + cell.SourceRect.Width is > int.MaxValue or < int.MinValue ||
                (long)cell.SourceRect.Y + cell.SourceRect.Height is > int.MaxValue or < int.MinValue)
                throw new InvalidDataException($"セルの切り出し範囲が画像外です（{cell.X + 1}, {cell.Y + 1}）。");
            if (string.IsNullOrWhiteSpace(cell.DisplayName))
                throw new InvalidDataException("セル名を空にすることはできません。");
        }
    }

    public static string ResolveAssetPath(string projectDirectory, string relativePath)
    {
        var root = Path.GetFullPath(projectDirectory);
        var resolved = Path.GetFullPath(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        var relative = Path.GetRelativePath(root, resolved);
        if (Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new InvalidDataException("画像ファイルのパスがプロジェクトフォルダーの外を指しています。");
        return resolved;
    }
}
