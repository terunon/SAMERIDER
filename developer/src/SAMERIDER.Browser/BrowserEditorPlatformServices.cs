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
    private const long MaximumUnityMetaBytes = 32L * 1024 * 1024;
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
        FilePickerFileType[] projectFileTypes =
        {
            new("SAMERIDERプロジェクト (*.samerider)")
            {
                Patterns = ["*.samerider"],
                MimeTypes = ["application/octet-stream"]
            },
            new("JSONプロジェクト (*.json)")
            {
                Patterns = ["*.json"],
                MimeTypes = ["application/json"]
            }
        };
        var files = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "SAMERIDERプロジェクトを開く", AllowMultiple = false,
            SuggestedFileType = projectFileTypes[0],
            FileTypeFilter = projectFileTypes
        });
        var file = files.FirstOrDefault();
        if (file is null) return null;
        var projectExtension = Path.GetExtension(file.Name);
        if (!projectExtension.Equals(".json", StringComparison.OrdinalIgnoreCase) &&
            !projectExtension.Equals(".samerider", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(".samerider または .json ファイルを選択してください。");
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
                var pngFileType = new FilePickerFileType("PNG画像") { Patterns = ["*.png"], MimeTypes = ["image/png"] };
                var imageFiles = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = $"参照画像を選択: {assetName}", AllowMultiple = false,
                    SuggestedFileType = pngFileType,
                    FileTypeFilter = [pngFileType]
                });
                var imageFile = imageFiles.FirstOrDefault();
                if (imageFile is null)
                {
                    await ReleaseTemporaryFileAsync(projectPath);
                    return null;
                }
                if (!Path.GetFileName(imageFile.Name).Equals(assetName, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"「{asset.RelativePath}」には「{assetName}」を選択してください（選択されたファイル: {imageFile.Name}）。");
                if (!Path.GetExtension(imageFile.Name).Equals(".png", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("参照画像にはPNGファイルを選択してください。");
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

    public async Task<string?> PickImagePathAsync(IStorageProvider storageProvider)
    {
        FilePickerFileType[] imageFileTypes =
        {
            new("PNG画像 (*.png)") { Patterns = ["*.png"], MimeTypes = ["image/png"] },
            new("GIFアニメーション (*.gif)") { Patterns = ["*.gif"], MimeTypes = ["image/gif"] }
        };
        var files = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "画像を取り込む", AllowMultiple = false,
            SuggestedFileType = imageFileTypes[0],
            FileTypeFilter = imageFileTypes
        });
        var file = files.FirstOrDefault();
        if (file is null) return null;
        var extension = Path.GetExtension(file.Name);
        if (!extension.Equals(".png", StringComparison.OrdinalIgnoreCase) &&
            !extension.Equals(".gif", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("PNGまたはGIFファイルを選択してください。");
        return await StageFileAsync(file);
    }

    public async Task<string?> PickUnityMetaPathAsync(IStorageProvider storageProvider, string pngFileName)
    {
        var expectedFileName = pngFileName + ".meta";
        var path = await PickSavePathAsync(storageProvider, "Unity用.metaを書き出す", expectedFileName, [".meta"]);
        if (path is null) return null;

        if (!_pendingOutputs.TryGetValue(path, out var file))
            throw new InvalidOperationException(".meta保存先の一時ファイルを取得できませんでした。");

        try
        {
            try
            {
                await StageFileAtPathAsync(file, path, MaximumUnityMetaBytes);
            }
            catch (Exception exception) when (exception is NotSupportedException or IOException or UnauthorizedAccessException or System.Runtime.InteropServices.JavaScript.JSException)
            {
                // Browser save handles may not expose the existing destination for reading.
                // JS-backed storage providers can also surface a missing new file as a JS exception;
                // an empty seed lets metadata generation proceed, and CommitFileAsync reports write failures.
                await using var emptyFile = File.Create(path);
                _displayNames[path] = file.Name;
            }
        }
        catch
        {
            await ReleaseTemporaryFileAsync(path);
            throw;
        }

        return path;
    }

    public async Task<string?> PickSavePathAsync(IStorageProvider storageProvider, string title, string suggestedFileName, string[] extensions)
    {
        var isProject = extensions.Contains(".samerider", StringComparer.OrdinalIgnoreCase);
        var isUnityMeta = extensions.Contains(".meta", StringComparer.OrdinalIgnoreCase);
        var extension = isProject ? ".samerider" : extensions[0];
        var suggestedName = isUnityMeta ? suggestedFileName : Path.ChangeExtension(suggestedFileName, extension);
        var outputFileType = new FilePickerFileType(isProject ? "SAMERIDERプロジェクトbundle" : isUnityMeta ? "Unityメタデータ" : "PNG画像")
        {
            Patterns = [$"*{extension}"],
            MimeTypes = [isProject ? "application/octet-stream" : isUnityMeta ? "text/plain" : "image/png"]
        };
        var file = await storageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title, SuggestedFileName = suggestedName, DefaultExtension = extension.TrimStart('.'),
            SuggestedFileType = outputFileType,
            FileTypeChoices = [outputFileType]
        });
        if (file is null) return null;
        if (extensions.Contains(".png", StringComparer.OrdinalIgnoreCase) &&
            !ProjectValidator.IsSafePngOutputFileName(file.Name))
            throw new InvalidDataException("PNGの出力ファイル名に使用できない文字または予約名が含まれています。");
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
        if (_pendingOutputs.Remove(path))
        {
            _displayNames.Remove(path);
            if (File.Exists(path)) File.Delete(path);
            return Task.CompletedTask;
        }

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
