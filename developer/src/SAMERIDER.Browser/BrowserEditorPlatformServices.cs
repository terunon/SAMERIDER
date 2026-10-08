using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using SAMERIDER.Core.Services;
using SAMERIDER.Editor;

namespace SAMERIDER.Browser;

internal sealed class BrowserEditorPlatformServices : IEditorPlatformServices
{
    private const int MaximumPngBytes = 128 * 1024 * 1024;
    private const long MaximumLegacyProjectAssetBytes = 512L * 1024 * 1024;
    private readonly Dictionary<string, IStorageFile> _pendingOutputs = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _displayNames = new(StringComparer.Ordinal);

    public string WorkspaceDirectory { get; } = CreateWorkspaceDirectory();
    public bool SupportsSavingExistingProject => false;
    public bool CanOpenAssetLocation => false;
    public StringComparison PathComparison => StringComparison.Ordinal;
    public bool IsCommandModifierPressed(KeyModifiers modifiers) =>
        modifiers.HasFlag(KeyModifiers.Control) || modifiers.HasFlag(KeyModifiers.Meta);

    public async Task<string?> PickProjectPathAsync(IStorageProvider storageProvider)
    {
        var files = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "SAMERIDERプロジェクトを開く", AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("SAMERIDERプロジェクト") { Patterns = ["*.json", "*.samerider"] }]
        });
        var file = files.FirstOrDefault();
        if (file is null) return null;
        var projectPath = await StageFileAsync(file);
        if (!Path.GetExtension(file.Name).Equals(".json", StringComparison.OrdinalIgnoreCase)) return projectPath;

        try
        {
            var projectBytes = await File.ReadAllBytesAsync(projectPath);
            var project = ProjectStore.Deserialize(projectBytes);
            long totalAssetBytes = 0;
            long totalImagePixels = 0;
            foreach (var asset in project.Assets)
            {
                var assetName = Path.GetFileName(asset.RelativePath);
                var imageFiles = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = $"参照画像を選択: {assetName}", AllowMultiple = false,
                    FileTypeFilter = [new FilePickerFileType("PNG画像") { Patterns = ["*.png"] }]
                });
                var imageFile = imageFiles.FirstOrDefault();
                if (imageFile is null)
                {
                    await ReleaseTemporaryFileAsync(projectPath);
                    return null;
                }
                if (!Path.GetFileName(imageFile.Name).Equals(assetName, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"「{asset.RelativePath}」には「{assetName}」を選択してください（選択されたファイル: {imageFile.Name}）。");
                var stagedAssetPath = ProjectValidator.ResolveAssetPath(Path.GetDirectoryName(projectPath)!, asset.RelativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(stagedAssetPath)!);
                var stagedAsset = await StageFileAtPathAsync(imageFile, stagedAssetPath, MaximumPngBytes);
                totalAssetBytes += new FileInfo(stagedAsset).Length;
                if (totalAssetBytes > MaximumLegacyProjectAssetBytes)
                    throw new InvalidDataException("従来形式プロジェクトの画像合計サイズが大きすぎます。");
                var (width, height) = SpriteImageService.GetPngSize(await File.ReadAllBytesAsync(stagedAsset));
                if (width <= 0 || height <= 0 || width > 16_384 || height > 16_384)
                    throw new InvalidDataException($"画像サイズが上限を超えています: {asset.RelativePath}");
                totalImagePixels = checked(totalImagePixels + (long)width * height);
                if (totalImagePixels > ProjectValidator.MaximumSheetPixelCount)
                    throw new InvalidDataException("ブラウザーで開ける画像の合計画素数は6,400万画素までです。");
                if (width != asset.Width || height != asset.Height)
                    throw new InvalidDataException($"記録された画像サイズと実際のサイズが一致しません: {asset.RelativePath}");
            }
        }
        catch
        {
            await ReleaseTemporaryFileAsync(projectPath);
            throw;
        }

        return projectPath;
    }

    public async Task<string?> PickPngPathAsync(IStorageProvider storageProvider)
    {
        var files = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "PNG画像を取り込む", AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("PNG画像") { Patterns = ["*.png"] }]
        });
        var file = files.FirstOrDefault();
        return file is null ? null : await StageFileAsync(file);
    }

    public async Task<string?> PickSavePathAsync(IStorageProvider storageProvider, string title, string suggestedFileName, string[] extensions)
    {
        var isProject = extensions.Contains(".samerider", StringComparer.OrdinalIgnoreCase);
        var extension = isProject ? ".samerider" : extensions[0];
        var suggestedName = Path.ChangeExtension(suggestedFileName, extension);
        var file = await storageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title, SuggestedFileName = suggestedName, DefaultExtension = extension.TrimStart('.'),
            FileTypeChoices = [new FilePickerFileType(isProject ? "SAMERIDERプロジェクトbundle" : "PNG画像") { Patterns = [$"*{extension}"] }]
        });
        if (file is null) return null;
        var path = Path.Combine(WorkspaceDirectory, "pending", Guid.NewGuid().ToString("N") + extension);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        _pendingOutputs.Add(path, file);
        _displayNames.Add(path, file.Name);
        return path;
    }

    public async Task<string> StageFileAsync(IStorageFile file)
    {
        var extension = Path.GetExtension(file.Name);
        var inputDirectory = Path.Combine(WorkspaceDirectory, "input", Guid.NewGuid().ToString("N"));
        var sourceFileName = Path.GetFileName(file.Name);
        if (string.IsNullOrWhiteSpace(sourceFileName)) throw new InvalidDataException("ファイル名を取得できませんでした。");
        var path = Path.Combine(inputDirectory, sourceFileName);
        Directory.CreateDirectory(inputDirectory);
        try
        {
            await StageFileAtPathAsync(file, path, extension.Equals(".samerider", StringComparison.OrdinalIgnoreCase)
                ? ProjectBundleService.MaximumBundleBytes
                : MaximumPngBytes);
        }
        catch
        {
            Directory.Delete(inputDirectory, recursive: true);
            throw;
        }
        return path;
    }

    public async Task CommitFileAsync(string path)
    {
        if (!_pendingOutputs.TryGetValue(path, out var file)) return;
        try
        {
            await using var source = File.OpenRead(path);
            await using var destination = await file.OpenWriteAsync();
            await source.CopyToAsync(destination);
        }
        finally
        {
            _pendingOutputs.Remove(path);
            _displayNames.Remove(path);
            if (File.Exists(path)) File.Delete(path);
        }
    }

    public string GetDisplayName(string path) => _displayNames.GetValueOrDefault(path, Path.GetFileName(path));

    public Task ReleaseTemporaryFileAsync(string path)
    {
        var inputDirectory = Path.GetDirectoryName(path);
        if (inputDirectory is not null && inputDirectory.StartsWith(Path.Combine(WorkspaceDirectory, "input") + Path.DirectorySeparatorChar, StringComparison.Ordinal) &&
            Directory.Exists(inputDirectory))
        {
            var inputPrefix = inputDirectory + Path.DirectorySeparatorChar;
            foreach (var stagedPath in _displayNames.Keys.Where(stagedPath => stagedPath.StartsWith(inputPrefix, StringComparison.Ordinal)).ToArray())
                _displayNames.Remove(stagedPath);
            Directory.Delete(inputDirectory, recursive: true);
        }
        return Task.CompletedTask;
    }

    private async Task<string> StageFileAtPathAsync(IStorageFile file, string path, long maximumBytes)
    {
        await using var source = await file.OpenReadAsync();
        try
        {
            await using var destination = File.Create(path);
            var buffer = new byte[81920];
            long copied = 0;
            int read;
            while ((read = await source.ReadAsync(buffer)) > 0)
            {
                copied = checked(copied + read);
                if (copied > maximumBytes) throw new InvalidDataException($"ファイルが大きすぎます: {file.Name}");
                await destination.WriteAsync(buffer.AsMemory(0, read));
            }
        }
        catch
        {
            File.Delete(path);
            throw;
        }

        _displayNames[path] = file.Name;
        return path;
    }

    private static string CreateWorkspaceDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "SAMERIDER_Browser");
        Directory.CreateDirectory(path);
        return path;
    }

    public async Task OpenExternalUrlAsync(TopLevel topLevel, Uri uri)
    {
        if (!await topLevel.Launcher.LaunchUriAsync(uri))
            throw new InvalidOperationException("ブラウザーでリンクを開けませんでした。");
    }

    public Task OpenAssetLocationAsync(string path) => Task.CompletedTask;
}
