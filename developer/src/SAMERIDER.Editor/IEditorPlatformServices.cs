using Avalonia.Platform.Storage;
using Avalonia.Controls;
using Avalonia.Input;

namespace SAMERIDER.Editor;

public interface IEditorPlatformServices
{
    string WorkspaceDirectory { get; }
    bool SupportsSavingExistingProject { get; }
    bool CanOpenAssetLocation { get; }
    StringComparison PathComparison { get; }
    bool IsCommandModifierPressed(KeyModifiers modifiers);
    Task<string?> PickProjectPathAsync(IStorageProvider storageProvider);
    Task<string?> PickPngPathAsync(IStorageProvider storageProvider);
    Task<string?> PickSavePathAsync(IStorageProvider storageProvider, string title, string suggestedFileName, string[] extensions);
    Task<string> StageFileAsync(IStorageFile file);
    Task ReleaseTemporaryFileAsync(string path);
    string GetDisplayName(string path);
    Task CommitFileAsync(string path);
    Task OpenExternalUrlAsync(TopLevel topLevel, Uri uri);
    Task OpenAssetLocationAsync(string path);
}
