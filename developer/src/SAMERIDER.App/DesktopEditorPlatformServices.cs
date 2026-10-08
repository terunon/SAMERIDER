using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using SAMERIDER.Editor;

namespace SAMERIDER.App;

internal sealed class DesktopEditorPlatformServices : IEditorPlatformServices
{
    public string WorkspaceDirectory
    {
        get
        {
            var baseDirectory = AppContext.BaseDirectory;
            if (!OperatingSystem.IsMacOS()) return Path.GetFullPath(baseDirectory);
            var workspace = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SAMERIDER");
            Directory.CreateDirectory(workspace);
            return workspace;
        }
    }

    public bool SupportsSavingExistingProject => true;
    public bool CanOpenAssetLocation => true;
    public StringComparison PathComparison => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    public bool IsCommandModifierPressed(KeyModifiers modifiers) =>
        OperatingSystem.IsMacOS() ? modifiers.HasFlag(KeyModifiers.Meta) : modifiers.HasFlag(KeyModifiers.Control);

    public async Task<string?> PickProjectPathAsync(IStorageProvider storageProvider)
    {
        var startFolder = await storageProvider.TryGetFolderFromPathAsync(WorkspaceDirectory);
        var files = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "SAMERIDERプロジェクトを開く", AllowMultiple = false, SuggestedStartLocation = startFolder,
            FileTypeFilter = [new FilePickerFileType("SAMERIDERプロジェクト") { Patterns = ["*.json", "*.samerider"] }]
        });
        return files.FirstOrDefault()?.Path.LocalPath;
    }

    public async Task<string?> PickPngPathAsync(IStorageProvider storageProvider)
    {
        var files = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "PNG画像を取り込む", AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("PNG画像") { Patterns = ["*.png"] }]
        });
        return files.FirstOrDefault()?.Path.LocalPath;
    }

    public async Task<string?> PickSavePathAsync(IStorageProvider storageProvider, string title, string suggestedFileName, string[] extensions)
    {
        var suggestedExtension = Path.GetExtension(suggestedFileName).TrimStart('.');
        var startFolder = await storageProvider.TryGetFolderFromPathAsync(WorkspaceDirectory);
        var file = await storageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title, SuggestedFileName = suggestedFileName, DefaultExtension = suggestedExtension,
            SuggestedStartLocation = startFolder,
            FileTypeChoices = extensions.Select(extension => new FilePickerFileType(extension.TrimStart('.').ToUpperInvariant())
                { Patterns = [$"*{extension}"] }).ToList()
        });
        return file?.Path.LocalPath;
    }

    public Task<string> StageFileAsync(IStorageFile file) => Task.FromResult(file.Path.LocalPath);
    public Task ReleaseTemporaryFileAsync(string path) => Task.CompletedTask;
    public string GetDisplayName(string path) => Path.GetFileName(path);
    public Task CommitFileAsync(string path) => Task.CompletedTask;

    public Task OpenExternalUrlAsync(TopLevel topLevel, Uri uri)
    {
        Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        return Task.CompletedTask;
    }

    public Task OpenAssetLocationAsync(string path)
    {
        ProcessStartInfo start;
        if (OperatingSystem.IsWindows())
        {
            start = new ProcessStartInfo("explorer.exe");
            start.ArgumentList.Add($"/select,{path}");
        }
        else if (OperatingSystem.IsMacOS())
        {
            start = new ProcessStartInfo("open");
            start.ArgumentList.Add("-R");
            start.ArgumentList.Add(path);
        }
        else
        {
            start = new ProcessStartInfo("xdg-open", Path.GetDirectoryName(path)!) { UseShellExecute = true };
        }

        Process.Start(start);
        return Task.CompletedTask;
    }
}
