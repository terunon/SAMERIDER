using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using SAMERIDER.Editor;

namespace SAMERIDER.App;

internal sealed class MainWindow : Window
{
    private bool _closePromptInProgress;
    private bool _allowClose;
    private readonly EditorView _editorView = new(new DesktopEditorPlatformServices());

    public MainWindow()
    {
        Title = "SAMERIDER v1.06: Same-sized Raster Image Divider, Editor and Recomposer";
        using var iconStream = typeof(MainWindow).Assembly.GetManifestResourceStream("SAMERIDER.App.SAMERIDER.ico");
        Icon = new WindowIcon(iconStream ?? throw new InvalidOperationException("アプリアイコンを読み込めませんでした。"));
        Background = Brush.Parse("#0D0F12");
        Foreground = Brush.Parse("#F3F4F6");
        Width = 1240;
        Height = 820;
        MinWidth = 900;
        MinHeight = 600;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Content = _editorView;
        _editorView.TitleChanged += title => Title = title;
        Closing += HandleClosing;
    }

    private async void HandleClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_allowClose) return;
        e.Cancel = true;
        if (_closePromptInProgress) return;
        _closePromptInProgress = true;
        try
        {
            if (!await _editorView.ConfirmClosingAsync()) return;
            _allowClose = true;
            Close();
        }
        finally
        {
            _closePromptInProgress = false;
        }
    }
}
