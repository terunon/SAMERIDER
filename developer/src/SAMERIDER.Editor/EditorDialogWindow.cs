using Avalonia.Controls;

namespace SAMERIDER.Editor;

internal interface IEditorDialogPresenter
{
    Task<object?> ShowDialogAsync(EditorDialogWindow dialog);
    void CloseDialog(EditorDialogWindow dialog, object? result);
}

internal sealed class EditorDialogWindow
{
    private IEditorDialogPresenter? _presenter;
    private TaskCompletionSource<object?>? _overlayCompletion;
    private Window? _desktopWindow;

    public string Title { get; set; } = string.Empty;
    public double Width { get; set; } = double.NaN;
    public double Height { get; set; } = double.NaN;
    public double MinWidth { get; set; }
    public double MinHeight { get; set; }
    public bool CanResize { get; set; } = true;
    public SizeToContent SizeToContent { get; set; }
    public WindowStartupLocation WindowStartupLocation { get; set; }
    public object? Content { get; set; }
    public event EventHandler? Closed;

    internal Task<object?> OverlayResultTask => _overlayCompletion?.Task
        ?? throw new InvalidOperationException("ダイアログの結果を初期化できませんでした。");

    public async Task<TResult> ShowDialog<TResult>(Control owner)
    {
        if (TopLevel.GetTopLevel(owner) is Window ownerWindow)
        {
            _desktopWindow = new Window
            {
                Title = Title,
                Width = Width,
                Height = Height,
                MinWidth = MinWidth,
                MinHeight = MinHeight,
                CanResize = CanResize,
                SizeToContent = SizeToContent,
                WindowStartupLocation = WindowStartupLocation,
                Content = Content
            };
            _desktopWindow.Closed += (_, args) => Closed?.Invoke(this, args);
            return await _desktopWindow.ShowDialog<TResult>(ownerWindow);
        }

        _presenter = owner as IEditorDialogPresenter
            ?? throw new InvalidOperationException("ダイアログホストが見つかりません。");
        _overlayCompletion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _ = _presenter.ShowDialogAsync(this);
        var result = await _overlayCompletion.Task;
        return result is TResult typedResult ? typedResult : default!;
    }

    public Task ShowDialog(Control owner) => ShowDialog<object?>(owner);

    public void Close()
    {
        if (_desktopWindow is not null)
            _desktopWindow.Close();
        else
        {
            _presenter?.CloseDialog(this, null);
            Closed?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Close<TResult>(TResult result)
    {
        if (_desktopWindow is not null)
            _desktopWindow.Close(result);
        else
        {
            _presenter?.CloseDialog(this, result);
            Closed?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Close(object? result)
    {
        if (_desktopWindow is not null)
            _desktopWindow.Close(result);
        else
        {
            _presenter?.CloseDialog(this, result);
            Closed?.Invoke(this, EventArgs.Empty);
        }
    }

    internal void CompleteOverlay(object? result) => _overlayCompletion?.TrySetResult(result);
}
