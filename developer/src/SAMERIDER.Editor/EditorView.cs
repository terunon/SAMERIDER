using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia.Animation;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using PathShape = Avalonia.Controls.Shapes.Path;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SkiaSharp;
using SAMERIDER.Core.Models;
using SAMERIDER.Core.Services;

namespace SAMERIDER.Editor;

public sealed class EditorView : UserControl, IEditorDialogPresenter
{
    private const string ProductName = "SAMERIDER v1.06: Same-sized Raster Image Divider, Editor and Recomposer";
    private const int MAX_UI_SOURCE_DIMENSION = 1024;
    private const int MAX_UI_SOURCE_PIXELS = 4_000_000;
    private const int MAX_CELL_PREVIEW_DIMENSION = 512;
    private const int MAX_SELECTED_PREVIEW_SOURCE_PIXELS = 64_000_000;
    private const double MIN_GRID_ZOOM = 0.05;
    private const double GRID_DETAIL_ZOOM_THRESHOLD = 0.4;
    private SpriteProject _project = CreateNewProject();
    private string _projectDirectory = string.Empty;
    private string? _projectPath;
    private Task? _titleUpdateTask;
    private bool _dirty;
    private bool _pngExportInProgress;
    private bool _imageImportInProgress;
    private bool _imagePickerInProgress;
    private (int X, int Y)? _loadingImportTarget;
    private int _projectRevision;
    private double _overlayOpacity = 0.2;
    private double _gridZoom = 1.0;
    private int _selectedX;
    private int _selectedY;
    private readonly HashSet<(int X, int Y)> _selectedCells = [(0, 0)];
    private (int X, int Y) _selectionAnchor;
    private SpriteCell[]? _copiedCells;
    private (int X, int Y) _copiedCellOrigin;
    private Key? _lastOffsetKey;
    private DateTime _lastOffsetTime;
    private (int X, int Y) _lastOffsetCell;
    private readonly Stack<SpriteProject> _undo = new();
    private readonly Stack<SpriteProject> _redo = new();
    private Button? _undoButton;
    private Button? _redoButton;
    private Button? _pngExportButton;
    private readonly Dictionary<string, CachedSourceBitmap> _sourceBitmaps = new(StringComparer.Ordinal);
    private string? _selectedPreviewAssetPath;
    private CachedSourceBitmap? _selectedPreviewSource;
    private int _selectedPreviewCellWidth;
    private int _selectedPreviewCellHeight;
    private readonly TextBox _title = new() { Width = 190 };
    private readonly TextBox _cellWidth = new() { Width = 64 };
    private readonly TextBox _cellHeight = new() { Width = 64 };
    private readonly TextBlock _cellCount = new() { Foreground = Brush.Parse("#89919D"), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
    private readonly TextBox _previewX = new() { Width = 48, Height = 36, Text = "1", TextAlignment = TextAlignment.Left, VerticalAlignment = VerticalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center };
    private readonly TextBox _previewY = new() { Width = 48, Height = 36, Text = "1", TextAlignment = TextAlignment.Left, VerticalAlignment = VerticalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center };
    private readonly ScrollViewer _gridScroll = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden };
    private readonly Canvas _gridHorizontalScrollBar = new() { Height = 8, MinHeight = 8, MaxHeight = 8, MinWidth = 32, Background = Brush.Parse("#343A44"), ClipToBounds = true, Cursor = new Cursor(StandardCursorType.Hand) };
    private readonly Canvas _gridVerticalScrollBar = new() { Width = 8, MinWidth = 8, MaxWidth = 8, MinHeight = 32, Background = Brush.Parse("#343A44"), ClipToBounds = true, Cursor = new Cursor(StandardCursorType.Hand) };
    private readonly Border _gridHorizontalThumb = new() { Height = 8, Background = Brush.Parse("#8B95A3"), CornerRadius = new CornerRadius(4), IsHitTestVisible = false };
    private readonly Border _gridVerticalThumb = new() { Width = 8, Background = Brush.Parse("#8B95A3"), CornerRadius = new CornerRadius(4), IsHitTestVisible = false };
    private readonly Canvas _grid = new() { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 0, 10, 10), Background = Brushes.Transparent };
    private readonly Canvas _gridOverlayCanvas = new() { ClipToBounds = true, IsHitTestVisible = false, ZIndex = 20, Margin = new Thickness(0, 2, 12, 12) };
    private readonly PathShape _dragArrow = new() { Stroke = Brush.Parse("#FBBF24"), StrokeThickness = 3, IsHitTestVisible = false, IsVisible = false };
    private readonly Image _dragGhost = new() { Stretch = Stretch.Uniform, Opacity = 0.72, IsHitTestVisible = false, IsVisible = false };
    private readonly Image _preview = new() { Stretch = Stretch.Uniform };
    private readonly Image _previous = new() { Stretch = Stretch.Uniform };
    private readonly Image _next = new() { Stretch = Stretch.Uniform };
    private readonly Canvas _previewCrosshairOverlay = new() { IsHitTestVisible = false, IsVisible = false, ZIndex = 20 };
    private readonly List<Avalonia.Controls.Shapes.Rectangle> _previewCropRegions = [];
    private readonly Avalonia.Controls.Shapes.Line _previewCrosshairHorizontal = new() { Stroke = Brush.Parse("#FDE68A"), StrokeThickness = 1 };
    private readonly Avalonia.Controls.Shapes.Line _previewCrosshairVertical = new() { Stroke = Brush.Parse("#FDE68A"), StrokeThickness = 1 };
    private readonly TextBlock _topCoordinateLabel = CreateCoordinateLabel();
    private readonly TextBlock _bottomCoordinateLabel = CreateCoordinateLabel();
    private readonly TextBlock _leftCoordinateLabel = CreateCoordinateLabel();
    private readonly TextBlock _rightCoordinateLabel = CreateCoordinateLabel();
    private Button? _selectAllButton;
    private Action? _updatePreviewViewport;
    private readonly Image _brandLogo = new() { Width = 112, Height = 112, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Right };
    private readonly Slider _overlayOpacitySlider = new() { Minimum = 0, Maximum = 1, Value = 0.2, Width = 220, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _noImageLabel = new() { Text = "No Image", FontSize = 20, Foreground = Brush.Parse("#A1A8B3"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, IsVisible = false };
    private readonly TextBlock _startGuide = new()
    {
        FontWeight = FontWeight.SemiBold, TextAlignment = TextAlignment.Center,
        TextWrapping = TextWrapping.Wrap, HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center, FontSize = 14, Margin = new Thickness(24),
        Foreground = Brush.Parse("#A1A8B3"), IsHitTestVisible = false, IsVisible = false, ZIndex = 5
    };
    private readonly Dictionary<(int X, int Y), (Border Frame, Border Selection, Button? Remove, Image Image, Border Loading)> _cellVisuals = [];
    private Dictionary<(int X, int Y), SpriteCell> _gridCellsByPosition = [];
    private readonly Image _backgroundImage = new() { Stretch = Stretch.UniformToFill, Opacity = 0.04, IsHitTestVisible = false };
    private readonly TranslateTransform _backgroundParallax = new();
    private readonly ScaleTransform _backgroundScale = new(1.035, 1.035);
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly ProgressBar _statusProgress = new() { Minimum = 0, Maximum = 100, Height = 5, IsVisible = false };
    private readonly Border _statusPanel = new()
    {
        BorderBrush = Brush.Parse("#3F4652"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8),
        Background = Brush.Parse("#171A20"), Padding = new Thickness(16, 12), Margin = new Thickness(0, 20, 0, 0), IsVisible = true
    };
    private readonly HashSet<(int X, int Y)> _cellsToAnimateOnRefresh = [];
    private bool _gridResizeInProgress;
    private bool _cellSizeCommitInProgress;
    private bool _previewCrosshairPinned;
    private Point _pinnedCrosshairPosition;
    private CancellationTokenSource? _gridResizeAnimation;
    private TaskCompletionSource? _gridResizeCompletion;
    private int? _panPointerId;
    private Point _panStartPosition;
    private Vector _panStartOffset;
    private bool _panDragging;
    private (int X, int Y)? _pressedCell;
    private KeyModifiers _pressedCellModifiers;
    private bool _pressedRightCell;
    private bool _cellDragDragging;
    private (int X, int Y)? _dragSourceCell;
    private (int X, int Y)? _dragTargetCell;
    private CancellationTokenSource? _scrollAnimation;
    private Vector? _scrollAnimationTarget;
    private int _zoomAnchorVersion;
    private (Point Content, Point Pointer, int Version)? _pendingZoomAnchor;
    private Orientation? _scrollbarDragAxis;
    private int? _scrollbarPointerId;
    private double _scrollbarDragPointerStart;
    private double _scrollbarDragThumbStart;
    private readonly TextBlock _offsetReadout = new()
    {
        Text = "X +0, Y +0", FontSize = 11, FontWeight = FontWeight.SemiBold,
        Foreground = Brush.Parse("#FDE68A"), HorizontalAlignment = HorizontalAlignment.Right,
        VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, -17, 0, 0),
        IsHitTestVisible = false
    };
    private readonly IEditorPlatformServices _platformServices;
    private readonly Grid _dialogOverlay = new() { IsVisible = false, ZIndex = 1000, Background = Brush.Parse("#99000000") };

    public event Action<string>? TitleChanged;

    private IStorageProvider StorageProvider => TopLevel.GetTopLevel(this)?.StorageProvider
        ?? throw new InvalidOperationException("ファイルストレージを利用できません。");

    public EditorView(IEditorPlatformServices platformServices)
    {
        _platformServices = platformServices;
        _projectDirectory = GetApplicationDirectory();
        Background = Brush.Parse("#0D0F12");
        Foreground = Brush.Parse("#F3F4F6");
        FontFamily = EditorToolTip.JapaneseFont;
        RenderOptions.SetBitmapInterpolationMode(_preview, BitmapInterpolationMode.HighQuality);
        RenderOptions.SetBitmapInterpolationMode(_previous, BitmapInterpolationMode.None);
        RenderOptions.SetBitmapInterpolationMode(_next, BitmapInterpolationMode.None);
        _brandLogo.Source = LoadBrandLogo();
        _backgroundImage.Source = LoadBackground();
        _backgroundImage.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);
        _backgroundImage.RenderTransform = new TransformGroup { Children = { _backgroundScale, _backgroundParallax } };
        var guideRuns = _startGuide.Inlines ?? throw new InvalidOperationException("スタートガイドのテキスト領域を初期化できませんでした。");
        guideRuns.Add(new Run("スプライトシート名") { FontWeight = FontWeight.Bold, Foreground = Brush.Parse("#FDE68A") });
        guideRuns.Add(new Run("と"));
        guideRuns.Add(new Run("セルサイズ") { FontWeight = FontWeight.Bold, Foreground = Brush.Parse("#FDE68A") });
        guideRuns.Add(new Run("を入力したら、"));
        guideRuns.Add(new Run("セルをクリック") { FontWeight = FontWeight.Bold, Foreground = Brush.Parse("#FDE68A") });
        guideRuns.Add(new Run("して画像を読み込んでみましょう。\n１セルのサイズが合っていれば、単体画像でも繋がったシートでも読み込めます。"));
        Content = BuildLayout();
        _gridScroll.Content = _grid;
        _gridScroll.AddHandler(InputElement.PointerWheelChangedEvent, HandleGridWheel, RoutingStrategies.Tunnel, handledEventsToo: true);
        _gridScroll.AttachedToVisualTree += (_, _) => Dispatcher.UIThread.Post(ConfigureGridScrollBars, DispatcherPriority.Loaded);
        _gridScroll.LayoutUpdated += (_, _) => ApplyPendingZoomAnchor();
        _gridScroll.ScrollChanged += (_, _) => { SyncGridScrollBars(); RefreshVisibleGridCells(); };
        _gridScroll.SizeChanged += (_, _) => RefreshVisibleGridCells();
        _gridHorizontalScrollBar.SizeChanged += (_, _) => SyncGridScrollBars();
        _gridVerticalScrollBar.SizeChanged += (_, _) => SyncGridScrollBars();
        _gridHorizontalScrollBar.PointerPressed += (_, e) => BeginScrollBarDrag(_gridHorizontalScrollBar, _gridHorizontalThumb, Orientation.Horizontal, e);
        _gridVerticalScrollBar.PointerPressed += (_, e) => BeginScrollBarDrag(_gridVerticalScrollBar, _gridVerticalThumb, Orientation.Vertical, e);
        _gridHorizontalScrollBar.PointerMoved += (_, e) => MoveScrollBarThumb(_gridHorizontalScrollBar, _gridHorizontalThumb, Orientation.Horizontal, e);
        _gridVerticalScrollBar.PointerMoved += (_, e) => MoveScrollBarThumb(_gridVerticalScrollBar, _gridVerticalThumb, Orientation.Vertical, e);
        _gridHorizontalScrollBar.PointerReleased += (_, e) => EndScrollBarDrag(_gridHorizontalScrollBar, e);
        _gridVerticalScrollBar.PointerReleased += (_, e) => EndScrollBarDrag(_gridVerticalScrollBar, e);
        _title.Text = _project.Title;
        _cellWidth.Text = _project.CellWidth.ToString(); _cellHeight.Text = _project.CellHeight.ToString();
        _cellCount.Text = $"セル数: {_project.Columns} × {_project.Rows}";
        _overlayOpacitySlider.PropertyChanged += (_, e) =>
        {
            if (e.Property == RangeBase.ValueProperty)
            {
                _overlayOpacity = _overlayOpacitySlider.Value;
                RefreshPreview();
            }
        };
        _gridScroll.AddHandler(InputElement.PointerPressedEvent, HandleGridPointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        _gridScroll.AddHandler(InputElement.PointerMovedEvent, HandleGridPointerMoved, RoutingStrategies.Tunnel, handledEventsToo: true);
        _gridScroll.AddHandler(InputElement.PointerReleasedEvent, HandleGridPointerReleased, RoutingStrategies.Tunnel, handledEventsToo: true);
        _title.LostFocus += async (_, _) => await UpdateTitleAsync();
        foreach (var input in new[] { _cellWidth, _cellHeight })
        {
            input.LostFocus += async (_, _) => await CommitCellSizeAsync();
            input.KeyDown += async (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; await CommitCellSizeAsync(); } };
        }
        _previewX.LostFocus += (_, _) => NavigateByCoordinates(); _previewY.LostFocus += (_, _) => NavigateByCoordinates();
        _previewX.KeyDown += (_, e) => { if (e.Key == Key.Enter) { NavigateByCoordinates(); e.Handled = true; } };
        _previewY.KeyDown += (_, e) => { if (e.Key == Key.Enter) { NavigateByCoordinates(); e.Handled = true; } };
        AddHandler(KeyDownEvent, HandleKeyDown, RoutingStrategies.Tunnel, handledEventsToo: true);
        Focusable = true;
        AddHandler(InputElement.PointerPressedEvent, (_, e) =>
        {
            if (e.Source is Visual source && (source is TextBox || source is Slider ||
                source.GetVisualAncestors().OfType<TextBox>().Any() || source.GetVisualAncestors().OfType<Slider>().Any())) return;
            Focus();
        }, RoutingStrategies.Tunnel, handledEventsToo: true);
        DetachedFromVisualTree += (_, _) =>
        {
            ClearSourceCache();
            DisposeGridVisuals();
            if (_brandLogo.Source is IDisposable logo) logo.Dispose();
            if (_backgroundImage.Source is IDisposable background) background.Dispose();
        };
        RefreshGrid(); RefreshPreview(); UpdateTitleBar();
    }

    private Control BuildLayout()
    {
        var surface = new Grid { Background = Brush.Parse("#0D0F12") };
        surface.Children.Add(_backgroundImage);
        var root = new DockPanel { LastChildFill = true, Margin = new Thickness(24), Background = Brushes.Transparent };
        surface.Children.Add(root);
        surface.Children.Add(_dialogOverlay);
        var header = new StackPanel { Spacing = 14, Margin = new Thickness(0, 0, 0, 22) };
        var toolbar = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("*,Auto"), RowDefinitions = RowDefinitions.Parse("Auto,Auto") };
        var fileActions = new WrapPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left };
        var newButton = Button("新規", NewProjectAsync);
        EditorToolTip.SetTip(newButton, "新しいスプライトシートを作る");
        fileActions.Children.Add(newButton);
        var openButton = Button("開く", OpenProjectAsync);
        EditorToolTip.SetTip(openButton, "既存のSAMERIDER用jsonファイルを開く");
        fileActions.Children.Add(openButton);
        var saveButton = Button("保存", SaveProjectAsAsync);
        EditorToolTip.SetTip(saveButton, "保存場所とファイル名を指定して、\nSAMERIDER用jsonファイルに保存する。\nCtrl+Sで上書き保存も可能");
        fileActions.Children.Add(saveButton);
        _pngExportButton = Button("PNGを書き出し", ExportPngAsync);
        EditorToolTip.SetTip(_pngExportButton, "作成したスプライトシートをpng画像として出力する");
        fileActions.Children.Add(_pngExportButton);
        toolbar.Children.Add(fileActions);
        var editActions = new WrapPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        _undoButton = Button("元に戻す（Undo）", Undo);
        _redoButton = Button("やり直す（Redo）", Redo);
        editActions.Children.Add(_undoButton);
        editActions.Children.Add(_redoButton);
        foreach (Control control in fileActions.Children) control.Margin = new Thickness(4, 2);
        foreach (Control control in editActions.Children) control.Margin = new Thickness(4, 2);
        UpdateUndoRedoButtons();
        Grid.SetColumn(editActions, 1); toolbar.Children.Add(editActions);
        header.Children.Add(toolbar);

        var settings = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("*,Auto"), VerticalAlignment = VerticalAlignment.Center };
        var settingInputs = new WrapPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        settingInputs.Children.Add(new TextBlock { Text = "スプライトシート名", VerticalAlignment = VerticalAlignment.Center }); settingInputs.Children.Add(_title);
        settingInputs.Children.Add(new TextBlock { Text = "セルサイズ X × Y", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) });
        settingInputs.Children.Add(_cellWidth);
        settingInputs.Children.Add(new TextBlock { Text = "×", VerticalAlignment = VerticalAlignment.Center }); settingInputs.Children.Add(_cellHeight);
        settingInputs.Children.Add(new TextBlock { Text = "px", VerticalAlignment = VerticalAlignment.Center });
        settings.Children.Add(settingInputs);
        foreach (Control control in settingInputs.Children) control.Margin = new Thickness(5, 3);
        Grid.SetColumn(_cellCount, 1); settings.Children.Add(_cellCount);
        header.Children.Add(new Border { Background = Brush.Parse("#171A20"), BorderBrush = Brush.Parse("#303640"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(16, 14), Child = settings });
        DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);

        var body = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("*,Auto,380"), RowDefinitions = RowDefinitions.Parse("*"), RowSpacing = 12 };
        var cellPanel = new Grid { RowDefinitions = RowDefinitions.Parse("Auto,*,Auto"), RowSpacing = 14 };
        var cellHeading = new StackPanel { Spacing = 4 };
        cellHeading.Children.Add(new TextBlock { Text = "セル一覧", FontSize = 20, FontWeight = FontWeight.SemiBold });
        cellPanel.Children.Add(cellHeading);
        _gridScroll.Margin = new Thickness(0, 2, 0, 0);
        var gridArea = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("*,Auto,Auto"), RowDefinitions = RowDefinitions.Parse("*,Auto,Auto"), ColumnSpacing = 12, RowSpacing = 12 };
        _gridHorizontalScrollBar.Children.Add(_gridHorizontalThumb);
        _gridVerticalScrollBar.Children.Add(_gridVerticalThumb);
        Grid.SetColumn(_gridScroll, 0); Grid.SetRow(_gridScroll, 0); gridArea.Children.Add(_gridScroll);
        _selectAllButton = new Button
        {
            Content = "全選択", HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 8, 8), Padding = new Thickness(10, 5), Focusable = false, ZIndex = 10
        };
        EditorToolTip.SetTip(_selectAllButton, "セル一覧の全選択／全解除（Ctrl+Aで全選択）");
        _selectAllButton.Click += (_, _) => ToggleSelectAllCells();
        _selectAllButton.Content = AreAllCellsSelected() ? "全解除" : "全選択";
        _selectAllButton.IsEnabled = _project.Columns != 1 || _project.Rows != 1;
        Grid.SetColumn(_selectAllButton, 2); Grid.SetRow(_selectAllButton, 2); gridArea.Children.Add(_selectAllButton);
        EditorToolTip.SetTip(_gridScroll, "クリックでセルを選択。\nCtrl+クリックで複数選択、Shift+クリックで範囲選択。\n選択範囲を左ドラッグで並べ替え、Delキーで削除");
        EditorToolTip.SetTip(_grid, "ホイールで拡縮、右／中クリックドラッグでスクロール。\nセル上でも操作可能");
        Grid.SetColumn(_gridVerticalScrollBar, 1); Grid.SetRow(_gridVerticalScrollBar, 0); gridArea.Children.Add(_gridVerticalScrollBar);
        Grid.SetColumn(_gridHorizontalScrollBar, 0); Grid.SetRow(_gridHorizontalScrollBar, 1); gridArea.Children.Add(_gridHorizontalScrollBar);
        Grid.SetColumn(_startGuide, 0); Grid.SetRow(_startGuide, 0); gridArea.Children.Add(_startGuide);
        Grid.SetColumn(_gridOverlayCanvas, 0); Grid.SetRow(_gridOverlayCanvas, 0); gridArea.Children.Add(_gridOverlayCanvas);
        _gridOverlayCanvas.Children.Add(_dragArrow);
        _gridOverlayCanvas.Children.Add(_dragGhost);
        var columnControls = new StackPanel { Spacing = 8, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        columnControls.Children.Add(GridSizeButton("＋", () => ChangeGridDimensionAsync(changeColumns: true, delta: 1)));
        columnControls.Children.Add(GridSizeButton("−", () => ChangeGridDimensionAsync(changeColumns: true, delta: -1)));
        EditorToolTip.SetTip(columnControls.Children[0], "セルの行列数を増減させる");
        EditorToolTip.SetTip(columnControls.Children[1], "セルの行列数を増減させる");
        Grid.SetColumn(columnControls, 2); Grid.SetRow(columnControls, 0); gridArea.Children.Add(columnControls);
        var rowControls = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        rowControls.Children.Add(GridSizeButton("＋", () => ChangeGridDimensionAsync(changeColumns: false, delta: 1)));
        rowControls.Children.Add(GridSizeButton("−", () => ChangeGridDimensionAsync(changeColumns: false, delta: -1)));
        EditorToolTip.SetTip(rowControls.Children[0], "セルの行列数を増減させる");
        EditorToolTip.SetTip(rowControls.Children[1], "セルの行列数を増減させる");
        Grid.SetRow(rowControls, 2); gridArea.Children.Add(rowControls);
        Grid.SetRow(gridArea, 1); cellPanel.Children.Add(gridArea);
        var statusContent = new StackPanel { Spacing = 8 };
        statusContent.Children.Add(_status);
        statusContent.Children.Add(_statusProgress);
        _statusPanel.Child = statusContent;
        Grid.SetRow(_statusPanel, 2); cellPanel.Children.Add(_statusPanel);
        body.Children.Add(cellPanel);
        var divider = new Border { Width = 1, Background = Brush.Parse("#3F4652"), Margin = new Thickness(14, 0) };
        Grid.SetColumn(divider, 1); body.Children.Add(divider);
        var previewPanel = new Grid
        {
            RowDefinitions = RowDefinitions.Parse("Auto,*,Auto,Auto"),
            RowSpacing = 12,
            Margin = new Thickness(8, 0, 8, 0)
        };
        var previewHeading = new TextBlock { Text = "プレビュー", FontSize = 20, FontWeight = FontWeight.SemiBold };
        EditorToolTip.SetTip(previewHeading, "方向キーで選択セルの表示位置を調整。\n左クリックで補助線を固定し、範囲を選んでトリミング。\n画像データは変更せず、座標情報のみ保存");
        previewPanel.Children.Add(previewHeading);
        var previewFrame = new Border { BorderBrush = Brush.Parse("#3F4652"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Background = Brush.Parse("#171A20"), Width = 340, Height = 340, MinHeight = 0, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, ClipToBounds = true, Padding = new Thickness(12) };
        Grid.SetRow(previewFrame, 1);
        EditorToolTip.SetTip(previewFrame, "方向キーで選択セルの表示位置を調整。\n左クリックで補助線を固定し、範囲を選んでトリミング。\n画像データは変更せず、座標情報のみ保存");
        var previewLayers = new Grid();
        _previous.HorizontalAlignment = HorizontalAlignment.Center; _previous.VerticalAlignment = VerticalAlignment.Center;
        _next.HorizontalAlignment = HorizontalAlignment.Center; _next.VerticalAlignment = VerticalAlignment.Center;
        _preview.HorizontalAlignment = HorizontalAlignment.Center; _preview.VerticalAlignment = VerticalAlignment.Center;
        previewLayers.Children.Add(_preview); previewLayers.Children.Add(_previous); previewLayers.Children.Add(_next); previewLayers.Children.Add(_noImageLabel); previewFrame.Child = previewLayers;
        for (var index = 0; index < 4; index++)
        {
            var cropRegion = new Avalonia.Controls.Shapes.Rectangle
            {
                Fill = Brush.Parse("#809AA0AA"), Stroke = Brush.Parse("#D9E0E8"), StrokeThickness = 1,
                IsHitTestVisible = false, IsVisible = false
            };
            _previewCropRegions.Add(cropRegion);
            _previewCrosshairOverlay.Children.Add(cropRegion);
        }
        _previewCrosshairOverlay.Children.Add(_previewCrosshairHorizontal);
        _previewCrosshairOverlay.Children.Add(_previewCrosshairVertical);
        _previewCrosshairOverlay.Children.Add(_topCoordinateLabel);
        _previewCrosshairOverlay.Children.Add(_bottomCoordinateLabel);
        _previewCrosshairOverlay.Children.Add(_leftCoordinateLabel);
        _previewCrosshairOverlay.Children.Add(_rightCoordinateLabel);
        previewLayers.Children.Add(_previewCrosshairOverlay);
        previewFrame.PointerMoved += (_, e) => HandlePreviewPointerMoved(previewLayers, e.GetPosition(previewLayers));
        previewFrame.PointerPressed += (_, e) => HandlePreviewPointerPressed(previewFrame, previewLayers, e);
        previewFrame.PointerExited += (_, _) =>
        {
            _previewCrosshairPinned = false;
            _previewCrosshairOverlay.IsVisible = false;
        };
        var previewImageSlot = new Grid { Width = 340, MinHeight = 0, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Stretch, ClipToBounds = false };
        void UpdatePreviewViewport()
        {
            if (previewImageSlot.Bounds.Width <= 0 || previewImageSlot.Bounds.Height <= 0 || _project.CellWidth <= 0 || _project.CellHeight <= 0) return;
            var aspectRatio = (double)_project.CellWidth / _project.CellHeight;
            var maxWidth = Math.Min(340, previewImageSlot.Bounds.Width);
            var maxHeight = Math.Min(340, previewImageSlot.Bounds.Height);
            var width = Math.Min(maxWidth, maxHeight * aspectRatio);
            var height = width / aspectRatio;
            previewFrame.Width = width;
            previewFrame.Height = height;
        }
        _updatePreviewViewport = UpdatePreviewViewport;
        previewImageSlot.SizeChanged += (_, _) => UpdatePreviewViewport();
        previewPanel.SizeChanged += (_, _) => UpdatePreviewViewport();
        previewImageSlot.Children.Add(previewFrame);
        previewImageSlot.Children.Add(_offsetReadout);
        Grid.SetRow(previewImageSlot, 1); previewPanel.Children.Add(previewImageSlot);
        var nav = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Spacing = 12, VerticalAlignment = VerticalAlignment.Center };
        var previousButton = Button("←", () => Navigate(-1), 42);
        EditorToolTip.SetTip(previousButton, "前のセルへ");
        nav.Children.Add(previousButton);
        nav.Children.Add(new TextBlock { Text = "X", VerticalAlignment = VerticalAlignment.Center }); nav.Children.Add(_previewX);
        nav.Children.Add(new TextBlock { Text = "Y", VerticalAlignment = VerticalAlignment.Center }); nav.Children.Add(_previewY);
        var nextButton = Button("→", () => Navigate(1), 42);
        EditorToolTip.SetTip(nextButton, "次のセルへ");
        nav.Children.Add(nextButton); Grid.SetRow(nav, 2); previewPanel.Children.Add(nav);
        var overlayControl = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Center };
        overlayControl.Children.Add(new TextBlock { Text = "前後のセルを重ねて表示", VerticalAlignment = VerticalAlignment.Center });
        overlayControl.Children.Add(_overlayOpacitySlider);
        Grid.SetRow(overlayControl, 3); previewPanel.Children.Add(overlayControl);
        EditorToolTip.SetTip(overlayControl, "前後のセルを重ねて表示");
        var brand = new StackPanel { Spacing = 2, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 4, 2) };
        brand.Children.Add(_brandLogo);
        brand.Children.Add(new TextBlock { Text = "SAMERIDER v1.06", FontSize = 13, FontWeight = FontWeight.SemiBold, HorizontalAlignment = HorizontalAlignment.Right });
        brand.Children.Add(new TextBlock { Text = "Same-sized Raster Image Divider, Editor and Recomposer", FontSize = 12, Foreground = Brush.Parse("#A1A8B3"), HorizontalAlignment = HorizontalAlignment.Right, TextWrapping = TextWrapping.Wrap, MaxWidth = 360 });
        var previewHost = new Grid { RowDefinitions = RowDefinitions.Parse("*,Auto") };
        previewHost.Children.Add(previewPanel);
        var bottomTools = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("Auto,*,Auto"), VerticalAlignment = VerticalAlignment.Bottom };
        var offsetPad = BuildOffsetPad();
        Grid.SetColumn(offsetPad, 0); bottomTools.Children.Add(offsetPad);
        Grid.SetColumn(brand, 2); bottomTools.Children.Add(brand);
        Grid.SetRow(bottomTools, 1); previewHost.Children.Add(bottomTools);
        Grid.SetColumn(previewHost, 2); body.Children.Add(previewHost);
        var compactLayout = false;
        void UpdateResponsiveLayout()
        {
            var useCompactLayout = body.Bounds.Width < 860;
            if (compactLayout == useCompactLayout) return;
            compactLayout = useCompactLayout;
            if (useCompactLayout)
            {
                toolbar.ColumnDefinitions = ColumnDefinitions.Parse("*");
                Grid.SetColumn(editActions, 0);
                Grid.SetRow(editActions, 1);
                editActions.HorizontalAlignment = HorizontalAlignment.Left;
                body.ColumnDefinitions = ColumnDefinitions.Parse("*");
                body.RowDefinitions = RowDefinitions.Parse("*,Auto,*");
                Grid.SetColumn(cellPanel, 0); Grid.SetRow(cellPanel, 0);
                Grid.SetColumn(divider, 0); Grid.SetRow(divider, 1);
                divider.Width = double.NaN; divider.Height = 1;
                divider.Margin = new Thickness(0, 4);
                Grid.SetColumn(previewHost, 0); Grid.SetRow(previewHost, 2);
                previewFrame.Width = double.NaN;
                previewFrame.Height = double.NaN;
                previewImageSlot.Width = double.NaN;
            }
            else
            {
                toolbar.ColumnDefinitions = ColumnDefinitions.Parse("*,Auto");
                Grid.SetColumn(editActions, 1);
                Grid.SetRow(editActions, 0);
                editActions.HorizontalAlignment = HorizontalAlignment.Right;
                body.ColumnDefinitions = ColumnDefinitions.Parse("*,Auto,380");
                body.RowDefinitions = RowDefinitions.Parse("*");
                Grid.SetColumn(cellPanel, 0); Grid.SetRow(cellPanel, 0);
                Grid.SetColumn(divider, 1); Grid.SetRow(divider, 0);
                divider.Width = 1; divider.Height = double.NaN;
                divider.Margin = new Thickness(14, 0);
                Grid.SetColumn(previewHost, 2); Grid.SetRow(previewHost, 0);
                previewFrame.Width = 340;
                previewFrame.Height = 340;
                previewImageSlot.Width = 340;
            }
            UpdatePreviewViewport();
        }
        body.SizeChanged += (_, _) => UpdateResponsiveLayout();
        toolbar.SizeChanged += (_, e) =>
        {
            if (e.NewSize.Width < 860)
            {
                toolbar.ColumnDefinitions = ColumnDefinitions.Parse("*");
                Grid.SetColumn(editActions, 0); Grid.SetRow(editActions, 1);
                editActions.HorizontalAlignment = HorizontalAlignment.Left;
            }
            else
            {
                toolbar.ColumnDefinitions = ColumnDefinitions.Parse("*,Auto");
                Grid.SetColumn(editActions, 1); Grid.SetRow(editActions, 0);
                editActions.HorizontalAlignment = HorizontalAlignment.Right;
            }
        };
        root.Children.Add(body);
        _brandLogo.Cursor = new Cursor(StandardCursorType.Hand);
        _brandLogo.PointerPressed += async (_, e) =>
        {
            if (!e.GetCurrentPoint(_brandLogo).Properties.IsLeftButtonPressed) return;
            e.Handled = true;
            try { await _platformServices.OpenExternalUrlAsync(TopLevel.GetTopLevel(this)!, new Uri("https://x.com/trinitroterunon")); }
            catch (Exception ex) { await Dialogs.ShowErrorAsync(this, $"リンクを開けませんでした。\n{ex.Message}"); }
        };
        return surface;
    }

    Task<object?> IEditorDialogPresenter.ShowDialogAsync(EditorDialogWindow dialog)
    {
        var content = dialog.Content as Control;
        var dialogLayout = new Grid { RowDefinitions = RowDefinitions.Parse("Auto,*"), RowSpacing = 10, Margin = new Thickness(16) };
        if (!string.IsNullOrWhiteSpace(dialog.Title))
            dialogLayout.Children.Add(new TextBlock { Text = dialog.Title, FontSize = 16, FontWeight = FontWeight.SemiBold });
        if (content is not null)
        {
            var scroll = new ScrollViewer
            {
                Content = content,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };
            Grid.SetRow(scroll, 1);
            dialogLayout.Children.Add(scroll);
        }
        var dialogCard = new Border
        {
            Child = dialogLayout,
            Background = Brush.Parse("#171A20"),
            BorderBrush = Brush.Parse("#606A78"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Width = dialog.Width,
            Height = dialog.SizeToContent == SizeToContent.Height ? double.NaN : dialog.Height,
            MaxWidth = Math.Max(320, Bounds.Width - 32),
            MaxHeight = Math.Max(240, Bounds.Height - 32),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        _dialogOverlay.Children.Add(dialogCard);
        _dialogOverlay.IsVisible = true;
        return dialog.OverlayResultTask;
    }

    void IEditorDialogPresenter.CloseDialog(EditorDialogWindow dialog, object? result)
    {
        _dialogOverlay.IsVisible = false;
        _dialogOverlay.Children.Clear();
        dialog.CompleteOverlay(result);
    }

    private static Button Button(string label, Func<Task> action, double? width = null)
    {
        var button = new Button { Content = label, MinWidth = width ?? 76, Padding = new Thickness(14, 9), HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center };
        button.Click += async (_, _) => await action(); return button;
    }

    private static Button Button(string label, Action action, double? width = null) => Button(label, () => { action(); return Task.CompletedTask; }, width);

    private static Button GridSizeButton(string label, Func<Task> action)
    {
        var button = new Button
        {
            Content = new TextBlock { Text = label, TextAlignment = TextAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            Width = 38, Height = 36, Padding = new Thickness(0), FontSize = 18,
            HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center
        };
        button.Click += async (_, _) => await action();
        return button;
    }

    private Control BuildOffsetPad()
    {
        var pad = new Grid
        {
            RowDefinitions = RowDefinitions.Parse("Auto,Auto,Auto"),
            ColumnDefinitions = ColumnDefinitions.Parse("Auto,Auto,Auto"),
            RowSpacing = 2,
            ColumnSpacing = 2,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top
        };
        AddOffsetButton(pad, "↑", 0, 1, 0, -1);
        AddOffsetButton(pad, "←", 1, 0, -1, 0);
        var resetButton = new Button
        {
            Content = "・",
            Width = 32,
            Height = 30,
            Padding = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Focusable = false
        };
        EditorToolTip.SetTip(resetButton, "座標調整をリセット");
        resetButton.Click += (_, _) => ResetSelectedCellOffset();
        Grid.SetRow(resetButton, 1);
        Grid.SetColumn(resetButton, 1);
        pad.Children.Add(resetButton);
        AddOffsetButton(pad, "→", 1, 2, 1, 0);
        AddOffsetButton(pad, "↓", 2, 1, 0, 1);
        return pad;
    }

    private void AddOffsetButton(Grid pad, string label, int row, int column, int dx, int dy)
    {
        var button = new Button
        {
            Content = label,
            Width = 32,
            Height = 30,
            Padding = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Focusable = false
        };
        EditorToolTip.SetTip(button, $"選択セルを{label}へ1px移動");
        button.Click += (_, _) => NudgeSelectedCellOffset(dx, dy);
        Grid.SetRow(button, row);
        Grid.SetColumn(button, column);
        pad.Children.Add(button);
    }

    private async Task OpenProjectAsync()
    {
        if (_imageImportInProgress) return;
        if (!await ConfirmAbandonChangesAsync()) return;
        var path = await _platformServices.PickProjectPathAsync(StorageProvider);
        if (path is null) return;
        try
        {
            var loadTimer = Stopwatch.StartNew();
            SetProgressStatus("プロジェクトと画像を読み込み中...", isIndeterminate: true);
            SpriteProject loaded;
            Dictionary<string, CachedSourceBitmap> loadedSourceBitmaps;
            try
            {
                (loaded, loadedSourceBitmaps) = await Task.Run(async () =>
                {
                    string? temporaryBundleDirectory = null;
                    try
                    {
                        var sourceDirectory = Path.GetDirectoryName(Path.GetFullPath(path))!;
                        SpriteProject sourceProject;
                        if (IsBundleExtension(Path.GetExtension(path)))
                        {
                            var bundleSize = new FileInfo(path).Length;
                            if (bundleSize > ProjectBundleService.MaximumBundleBytes)
                                throw new InvalidDataException("プロジェクトbundleが大きすぎます。");
                            var bundle = ProjectBundleService.OpenBundle(await File.ReadAllBytesAsync(path));
                            temporaryBundleDirectory = Path.Combine(Path.GetTempPath(), "SAMERIDER_Bundle", Guid.NewGuid().ToString("N"));
                            Directory.CreateDirectory(temporaryBundleDirectory);
                            foreach (var (assetPath, bytes) in bundle.Assets)
                            {
                                var extractedPath = ProjectValidator.ResolveAssetPath(temporaryBundleDirectory, assetPath);
                                Directory.CreateDirectory(Path.GetDirectoryName(extractedPath)!);
                                await File.WriteAllBytesAsync(extractedPath, bytes);
                            }
                            var temporaryProjectPath = Path.Combine(temporaryBundleDirectory, "project.json");
                            await ProjectStore.SaveAsync(bundle.Project, temporaryProjectPath);
                            sourceProject = await ProjectStore.LoadAsync(temporaryProjectPath);
                            sourceDirectory = temporaryBundleDirectory;
                        }
                        else
                        {
                            sourceProject = await ProjectStore.LoadAsync(path);
                        }

                        var copiedProject = CopyAssetsIntoImportFolder(sourceProject, sourceDirectory, GetApplicationDirectory(), renameExistingImportFolder: false);
                        var sourceBitmaps = new Dictionary<string, CachedSourceBitmap>(StringComparer.Ordinal);
                        try
                        {
                            foreach (var assetPath in copiedProject.Cells.Select(cell => cell.AssetPath).Distinct(StringComparer.Ordinal))
                            {
                                var resolvedPath = ProjectValidator.ResolveAssetPath(GetApplicationDirectory(), assetPath);
                                var maximumDimension = GetUiSourceMaximumDimension(resolvedPath, copiedProject.CellWidth, copiedProject.CellHeight);
                                var (bitmap, scaleX, scaleY) = SpriteImageService.DecodeForPreview(resolvedPath, maximumDimension);
                                sourceBitmaps.Add(assetPath, new CachedSourceBitmap(bitmap, scaleX, scaleY));
                            }
                            return (copiedProject, sourceBitmaps);
                        }
                        catch
                        {
                            foreach (var source in sourceBitmaps.Values) source.Bitmap.Dispose();
                            throw;
                        }
                    }
                    finally
                    {
                        if (temporaryBundleDirectory is not null && Directory.Exists(temporaryBundleDirectory))
                            Directory.Delete(temporaryBundleDirectory, recursive: true);
                    }
                });
            }
            finally
            {
                HideProgressStatus();
            }
            ClearSourceCache();
            foreach (var (assetPath, source) in loadedSourceBitmaps) _sourceBitmaps.Add(assetPath, source);
            _project = loaded;
            _projectPath = _platformServices.SupportsSavingExistingProject ? path : null;
            _projectDirectory = GetApplicationDirectory(); _projectRevision++;
            _copiedCells = null;
            _selectedX = Math.Min(_selectedX, _project.Columns - 1); _selectedY = Math.Min(_selectedY, _project.Rows - 1);
            ResetSelectionToCurrentCell();
            _dirty = false; _undo.Clear(); _redo.Clear(); UpdateUndoRedoButtons(); ResetOffsetBatch(); SyncFields(); RefreshAll(); UpdateTitleBar();
            loadTimer.Stop();
            SetStatus($"開きました: {_platformServices.GetDisplayName(path)}（読み込み時間: {loadTimer.Elapsed.TotalMilliseconds:F0} ms）");
        }
        catch (Exception ex) { await Dialogs.ShowErrorAsync(this, ex.Message); }
        finally
        {
            if (path is not null) await _platformServices.ReleaseTemporaryFileAsync(path);
        }
    }

    private async Task PickAndImportAsync(int x, int y)
    {
        if (_imagePickerInProgress || _imageImportInProgress || _pngExportInProgress) return;

        _imagePickerInProgress = true;
        string? path = null;
        try
        {
            path = await _platformServices.PickPngPathAsync(StorageProvider);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        finally
        {
            _imagePickerInProgress = false;
        }

        if (path is not null)
        {
            try { await ImportAsync(path, x, y); }
            finally { await _platformServices.ReleaseTemporaryFileAsync(path); }
        }
    }

    private Task UpdateTitleAsync()
    {
        if (_titleUpdateTask is { IsCompleted: false }) return _titleUpdateTask;
        _titleUpdateTask = UpdateTitleCoreAsync();
        return _titleUpdateTask;
    }

    private async Task UpdateTitleCoreAsync()
    {
        var value = _title.Text?.Trim() ?? string.Empty;
        if (value == _project.Title) return;
        if (!ProjectValidator.IsSafeFileName(value))
        {
            _title.Text = _project.Title;
            SetStatus("スプライトシート名にファイル名として使用できない文字が含まれています。");
            return;
        }

        var previousFolderName = _project.ImportFolderName;
        var currentImportFolder = Path.Combine(_projectDirectory, ProjectValidator.ImportRootDirectoryName, previousFolderName);
        var renameImportFolder = false;
        if (Directory.Exists(currentImportFolder) && !string.Equals(previousFolderName, value, StringComparison.Ordinal))
        {
            renameImportFolder = await Dialogs.ConfirmAsync(this, "SAMERIDER_Importフォルダ名の変更", $"スプライトシート名を「{value}」に変更します。SAMERIDER_Importフォルダ内の「{previousFolderName}」も同じ名前に変更しますか？", "フォルダも変更", "変更しない");
        }

        try
        {
            SpriteProject renamed;
            if (renameImportFolder)
            {
                var destination = Path.Combine(_projectDirectory, ProjectValidator.ImportRootDirectoryName, value);
                if (Directory.Exists(destination))
                    throw new IOException($"Importフォルダ「{value}」は既に存在するため、名前を変更できません。");
                renamed = CopyAssetsIntoImportFolder(_project, _projectDirectory, _projectDirectory, renameExistingImportFolder: true,
                    targetTitle: value, targetImportFolderName: value);
                RetargetUndoHistoryForFolderRename(previousFolderName, value);
            }
            else
            {
                renamed = _project.DeepClone();
                renamed.Title = value;
                if (!Directory.Exists(currentImportFolder)) renamed.ImportFolderName = value;
            }
            _project = renamed;
            _copiedCells = null;
            _title.Text = value;
            MarkDirty();
        }
        catch (Exception ex)
        {
            var renamedFolder = Path.Combine(_projectDirectory, ProjectValidator.ImportRootDirectoryName, value);
            if (renameImportFolder && Directory.Exists(renamedFolder) && !Directory.Exists(currentImportFolder))
            {
                try { Directory.Move(renamedFolder, currentImportFolder); }
                catch { /* Preserve the original error and report the rename failure below. */ }
            }
            _title.Text = _project.Title;
            SetStatus($"スプライトシート名を変更できませんでした: {ex.Message}");
        }
    }

    private async Task<bool> SaveProjectAsync()
    {
        if (_imageImportInProgress) return false;
        await UpdateTitleAsync();
        await CommitCellSizeAsync();
        if (string.IsNullOrWhiteSpace(_projectPath)) return await SaveProjectAsAsync();
        return await SaveProjectToPathAsync(_projectPath);
    }

    private async Task<bool> SaveProjectAsAsync()
    {
        if (_imageImportInProgress) return false;
        await UpdateTitleAsync();
        await CommitCellSizeAsync();
        var suggestedExtension = _projectPath is not null && IsBundleExtension(Path.GetExtension(_projectPath)) ? "samerider" : "json";
        var path = await _platformServices.PickSavePathAsync(StorageProvider,
            "SAMERIDERプロジェクトを保存", _project.Title + "." + suggestedExtension, [".json", ".samerider"]);
        if (path is null) return false;
        return await SaveProjectToPathAsync(path);
    }

    private async Task<bool> SaveProjectToPathAsync(string path)
    {
        if (_imageImportInProgress) return false;
        try
        {
            if (!ProjectValidator.IsSafeFileName(Path.GetFileName(path)))
                throw new InvalidDataException("プロジェクトファイル名に使用できない文字が含まれています。");
            var extension = Path.GetExtension(path);
            if (extension.Equals(".json", StringComparison.OrdinalIgnoreCase))
            {
                var destinationDirectory = Path.GetDirectoryName(Path.GetFullPath(path))!;
                var projectToSave = CopyAssetsIntoImportFolder(_project, _projectDirectory, destinationDirectory, renameExistingImportFolder: false);
                await ProjectStore.SaveAsync(projectToSave, path);
            }
            else if (IsBundleExtension(extension))
            {
                var assets = await Task.Run(() => _project.Assets.ToDictionary(
                    asset => asset.RelativePath,
                    asset => File.ReadAllBytes(ProjectValidator.ResolveAssetPath(_projectDirectory, asset.RelativePath)),
                    StringComparer.Ordinal));
                var bundle = ProjectBundleService.CreateBundle(_project, assets);
                await File.WriteAllBytesAsync(path, bundle);
            }
            else
            {
                throw new InvalidDataException("プロジェクトファイルの拡張子は .json または .samerider にしてください。");
            }
            var displayName = _platformServices.GetDisplayName(path);
            await _platformServices.CommitFileAsync(path);
            _projectPath = _platformServices.SupportsSavingExistingProject ? path : null; _dirty = false;
            UpdateTitleBar(); SetStatus($"保存しました: {displayName}"); return true;
        }
        catch (Exception ex) { await Dialogs.ShowErrorAsync(this, ex.Message); return false; }
    }

    private static bool IsBundleExtension(string extension) =>
        extension.Equals(".samerider", StringComparison.OrdinalIgnoreCase);

    private async Task ExportPngAsync()
    {
        if (_pngExportInProgress) return;
        var exportProject = _project.DeepClone();
        var projectDirectory = _projectDirectory;
        var totalCells = exportProject.Cells.Count;
        _pngExportInProgress = true;
        var exportTimer = Stopwatch.StartNew();
        try
        {
            SetProgressStatus("PNGセルをプレビュー用に合成中...", isIndeterminate: false, 0);
            var progress = new Progress<int>(completedCells =>
            {
                if (!_pngExportInProgress) return;
                var percent = totalCells == 0 ? 100 : completedCells * 100d / totalCells;
                SetProgressStatus($"PNGセルを合成中 ({completedCells}/{totalCells})...", isIndeterminate: false, percent);
            });
            using var bitmap = await Task.Run(() => SpriteImageService.Export(exportProject, projectDirectory, progress));
            HideProgressStatus();

            var selectedPositions = _selectedCells.ToArray();
            var settings = await Dialogs.PickPngExportSettingsAsync(
                this, bitmap, exportProject.CellWidth, exportProject.CellHeight, selectedPositions, selectedPositions.Length > 1);
            if (settings is null) return;
            var path = await _platformServices.PickSavePathAsync(StorageProvider,
                "スプライトシートPNGを書き出す", _project.Title + ".png", [".png"]);
            if (path is null) return;

            exportTimer.Restart();
            if (settings.Value.SelectedOnly) SetProgressStatus("選択セルだけを抽出中...", isIndeterminate: true);
            using var selectedImage = settings.Value.SelectedOnly
                ? await Task.Run(() => SpriteImageService.ExportSelectedCells(bitmap, exportProject.CellWidth, exportProject.CellHeight, selectedPositions))
                : null;
            var sourceImage = selectedImage ?? bitmap;
            if (settings.Value.ColorCount != 0) SetProgressStatus("PNGを減色中...", isIndeterminate: true);
            using var quantized = settings.Value.ColorCount == 0
                ? null
                : await Task.Run(() => SpriteImageService.Quantize(sourceImage, settings.Value.ColorCount));
            var imageToSave = quantized ?? sourceImage;
            SetProgressStatus("PNGを圧縮して保存中...", isIndeterminate: true);
            await Task.Run(() => SpriteImageService.SavePng(imageToSave, path));
            var displayName = _platformServices.GetDisplayName(path);
            await _platformServices.CommitFileAsync(path);
            exportTimer.Stop();
            var elapsed = exportTimer.Elapsed;
            var elapsedText = elapsed.TotalMilliseconds >= 1000
                ? $"{elapsed.TotalSeconds:F1} sec"
                : $"{elapsed.TotalMilliseconds:F0} ms";
            var qualityText = settings.Value.ColorCount == 0 ? "フルカラー" : $"{settings.Value.ColorCount}色";
            var selectedOnlyText = settings.Value.SelectedOnly ? "、選択セルのみ" : string.Empty;
            SetStatus($"書き出しました: {displayName} ({imageToSave.Width} × {imageToSave.Height}, {qualityText}{selectedOnlyText})（書き出し時間: {elapsedText}）");
        }
        catch (Exception ex)
        {
            SetStatus("PNGの書き出しに失敗しました。");
            await Dialogs.ShowErrorAsync(this, ex.Message);
        }
        finally
        {
            _pngExportInProgress = false;
            HideProgressStatus();
        }
    }

    private async Task NewProjectAsync()
    {
        if (_imageImportInProgress) return;
        if (!await ConfirmAbandonChangesAsync()) return;
        _project = CreateNewProject(); _projectPath = null; _projectDirectory = GetApplicationDirectory(); _projectRevision++;
        _copiedCells = null;
        ClearSourceCache();
        _selectedX = 0; _selectedY = 0; ResetSelectionToCurrentCell(); _dirty = false; _undo.Clear(); _redo.Clear(); UpdateUndoRedoButtons(); ResetOffsetBatch();
        SyncFields(); RefreshAll(); UpdateTitleBar(); SetStatus("新規プロジェクトを作成しました");
    }

    private async Task<bool> ConfirmAbandonChangesAsync()
    {
        if (!_dirty) return true;
        var answer = await Dialogs.UnsavedAsync(this, "未保存の変更", "続行する前に変更を保存しますか？");
        if (answer is null) return false;
        return answer.Value ? await SaveProjectAsync() : true;
    }

    public async Task<bool> ConfirmClosingAsync()
    {
        if (!_dirty) return true;
        var answer = await Dialogs.UnsavedAsync(this, "未保存の変更", "SAMERIDERを終了する前に変更を保存しますか？");
        if (answer is null) return false;
        return !answer.Value || await SaveProjectAsync();
    }

    private async Task CommitCellSizeAsync((ResizeAnchor Anchor, int Width, int Height)? suggestedResize = null)
    {
        if (_cellSizeCommitInProgress) return;
        _cellSizeCommitInProgress = true;
        try
        {
            var width = suggestedResize?.Width ?? (int.TryParse(_cellWidth.Text, out var parsedWidth) ? parsedWidth : 0);
            var height = suggestedResize?.Height ?? (int.TryParse(_cellHeight.Text, out var parsedHeight) ? parsedHeight : 0);
            if (width <= 0 || height <= 0)
            { SyncFields(); SetStatus("セルサイズには正の整数を入力してください。"); return; }
            var sheetWidth = _project.Columns * (long)width; var sheetHeight = _project.Rows * (long)height;
            if ((long)_project.Columns * _project.Rows > ProjectValidator.MaximumGridCellCount || sheetWidth > 16_384 || sheetHeight > 16_384 || sheetWidth * sheetHeight > ProjectValidator.MaximumSheetPixelCount)
            { SyncFields(); SetStatus("指定されたセルサイズではシートが大きすぎます。"); return; }

            var sizeChanged = width != _project.CellWidth || height != _project.CellHeight;
            ResizeAnchor? anchor = ResizeAnchor.TopLeft;
            if ((sizeChanged || suggestedResize is not null) && _project.Cells.Count > 0)
            {
                var sampleCell = _project.Cells.OrderBy(cell => cell.Y).ThenBy(cell => cell.X).First();
                using var sampleImage = CreateBitmap(RenderCellThumbnailForUI(sampleCell, 120));
                var projectToAnalyze = _project.DeepClone();
                var projectDirectory = _projectDirectory;
                var resizeSettings = await Dialogs.PickResizeSettingsAsync(
                    this, sampleImage, _project.CellWidth, _project.CellHeight, width, height, _project.Columns, _project.Rows,
                    suggestedResize?.Anchor ?? ResizeAnchor.Bottom,
                    cancellationToken => Task.Run(
                        () => SpriteImageService.FindCommonOpaqueBounds(projectToAnalyze, projectDirectory, cancellationToken), cancellationToken));
                if (resizeSettings is null) { SyncFields(); return; }
                width = resizeSettings.Value.CellWidth;
                height = resizeSettings.Value.CellHeight;
                anchor = resizeSettings.Value.Anchor;
            }
            if (anchor is null) { SyncFields(); return; }
            sizeChanged = width != _project.CellWidth || height != _project.CellHeight;
            if (!sizeChanged) { SyncFields(); return; }
            PushUndo();
            try
            {
                SpriteImageService.ResizeCells(_project, width, height, anchor.Value);
                MarkDirty();
                ResetOffsetBatch();
                SyncFields();
                RefreshAll();
                SetStatus("セルサイズを更新しました");
            }
            catch (Exception ex) { Undo(); await Dialogs.ShowErrorAsync(this, ex.Message); SyncFields(); }
        }
        finally
        {
            _cellSizeCommitInProgress = false;
        }
    }

    private async Task ChangeGridDimensionAsync(bool changeColumns, int delta)
    {
        while (_gridResizeInProgress)
        {
            var activeCompletion = _gridResizeCompletion?.Task;
            _gridResizeAnimation?.Cancel();
            CancelScrollAnimation(finishAtTarget: true);
            if (activeCompletion is null) return;
            await activeCompletion;
            FinishGridCellAnimations();
        }

        var animation = new CancellationTokenSource();
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _gridResizeAnimation = animation;
        _gridResizeCompletion = completion;
        _gridResizeInProgress = true;
        _gridScroll.IsEnabled = false;
        try
        {
            await ChangeGridDimensionCoreAsync(changeColumns, delta, animation.Token);
        }
        finally
        {
            if (animation.IsCancellationRequested) FinishGridCellAnimations();
            _gridScroll.IsEnabled = true;
            _gridResizeInProgress = false;
            animation.Dispose();
            if (ReferenceEquals(_gridResizeAnimation, animation)) _gridResizeAnimation = null;
            completion.TrySetResult();
            if (ReferenceEquals(_gridResizeCompletion, completion)) _gridResizeCompletion = null;
        }
    }

    private async Task ChangeGridDimensionCoreAsync(bool changeColumns, int delta, CancellationToken cancellationToken)
    {
        var previousColumns = _project.Columns;
        var previousRows = _project.Rows;
        var columns = _project.Columns + (changeColumns ? delta : 0);
        var rows = _project.Rows + (changeColumns ? 0 : delta);
        if (columns < 1 || rows < 1) return;
        var sheetWidth = columns * (long)_project.CellWidth;
        var sheetHeight = rows * (long)_project.CellHeight;
        if ((long)columns * rows > ProjectValidator.MaximumGridCellCount || sheetWidth > 16_384 || sheetHeight > 16_384 || sheetWidth * sheetHeight > ProjectValidator.MaximumSheetPixelCount)
        {
            SetStatus("これ以上、XまたはYの分割数を増やせません。シートの上限に達します。");
            return;
        }

        if (columns < previousColumns || rows < previousRows)
        {
            await EnsureGridBoundaryVisibleAsync(changeColumns, (changeColumns ? previousColumns : previousRows) - 1);
            foreach (var ((x, y), visual) in _cellVisuals.ToArray())
            {
                if (x < columns && y < rows) continue;
                visual.Frame.Transitions = CreateOpacityTransition();
                visual.Frame.Opacity = 0;
            }
            try { await Task.Delay(190, cancellationToken); }
            catch (OperationCanceledException) { }
        }

        PushUndo();
        _cellsToAnimateOnRefresh.Clear();
        if (columns > previousColumns)
            for (var y = 0; y < rows; y++) _cellsToAnimateOnRefresh.Add((previousColumns, y));
        if (rows > previousRows)
            for (var x = 0; x < columns; x++) _cellsToAnimateOnRefresh.Add((x, previousRows));
        var removedCellCount = _project.Cells.RemoveAll(cell => cell.X >= columns || cell.Y >= rows);
        _project.Columns = columns;
        _project.Rows = rows;
        _selectedX = Math.Min(_selectedX, columns - 1);
        _selectedY = Math.Min(_selectedY, rows - 1);
        MarkDirty();
        SyncFields();
        RefreshAll();
        SetStatus(removedCellCount > 0
            ? $"範囲外の{removedCellCount}セルを削除しました。Undoで復元できます。"
            : string.Empty);
        if ((columns > previousColumns || rows > previousRows) && !cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(16);
            if (!cancellationToken.IsCancellationRequested)
                await EnsureGridBoundaryVisibleAsync(changeColumns, changeColumns ? previousColumns : previousRows);
        }
    }

    private void FinishGridCellAnimations()
    {
        foreach (var border in _grid.Children.OfType<Border>())
        {
            border.Transitions = null;
            border.Opacity = 1;
        }
    }

    private void MoveCell(int sourceX, int sourceY, int targetX, int targetY)
    {
        if (sourceX == targetX && sourceY == targetY) return;
        var source = FindCell(sourceX, sourceY);
        if (source is null) return;
        var target = FindCell(targetX, targetY);
        var sourcePosition = (sourceX, sourceY);
        var targetPosition = (targetX, targetY);
        var affectedPositions = new HashSet<(int X, int Y)> { sourcePosition, targetPosition };
        affectedPositions.UnionWith(_selectedCells);
        var previousCurrent = (_selectedX, _selectedY);
        affectedPositions.Add(previousCurrent);
        var visualBitmaps = CaptureCellVisualBitmaps(affectedPositions);
        PushUndo();
        source.X = targetX;
        source.Y = targetY;
        if (target is not null)
        {
            target.X = sourceX;
            target.Y = sourceY;
        }
        _selectedX = targetX;
        _selectedY = targetY;
        _selectedCells.Clear();
        _selectedCells.Add(targetPosition);
        _selectionAnchor = targetPosition;
        MarkDirty();
        RefreshMovedCellVisuals(affectedPositions, visualBitmaps);
        UpdateSelectionVisuals(affectedPositions);
        RefreshPreview();
        SetStatus(target is null
            ? $"セル ({sourceX + 1}, {sourceY + 1}) をセル ({targetX + 1}, {targetY + 1}) へ移動しました。"
            : $"セル ({sourceX + 1}, {sourceY + 1}) とセル ({targetX + 1}, {targetY + 1}) を入れ替えました。");
    }

    private void MoveSelectedRange((int X, int Y) source, (int X, int Y) target)
    {
        var deltaX = target.X - source.X;
        var deltaY = target.Y - source.Y;
        if (deltaX == 0 && deltaY == 0) return;
        var sourcePositions = _selectedCells.ToHashSet();
        var targetPositions = sourcePositions.Select(position => (X: position.X + deltaX, Y: position.Y + deltaY)).ToHashSet();
        if (targetPositions.Any(position => position.X < 0 || position.X >= _project.Columns || position.Y < 0 || position.Y >= _project.Rows))
        {
            SetStatus("選択範囲がセル一覧の外にはみ出すため移動できません。");
            return;
        }

        var leavingPositions = sourcePositions.Except(targetPositions).OrderBy(position => position.Y).ThenBy(position => position.X).ToArray();
        var enteringPositions = targetPositions.Except(sourcePositions).OrderBy(position => position.Y).ThenBy(position => position.X).ToArray();
        var cellsByPosition = _project.Cells.ToDictionary(cell => (cell.X, cell.Y));
        var movingCells = sourcePositions
            .Where(cellsByPosition.ContainsKey)
            .Select(position => (Cell: cellsByPosition[position], Position: (X: position.X + deltaX, Y: position.Y + deltaY)))
            .ToArray();
        var displacedCells = enteringPositions
            .Select((position, index) => (Cell: cellsByPosition.GetValueOrDefault(position), Position: leavingPositions[index]))
            .Where(entry => entry.Cell is not null)
            .ToArray();
        var affectedPositions = sourcePositions.Union(targetPositions).ToHashSet();
        var visualBitmaps = CaptureCellVisualBitmaps(affectedPositions);

        PushUndo();
        _project.Cells.RemoveAll(cell => affectedPositions.Contains((cell.X, cell.Y)));
        foreach (var move in movingCells)
        {
            move.Cell.X = move.Position.X;
            move.Cell.Y = move.Position.Y;
            _project.Cells.Add(move.Cell);
        }
        foreach (var move in displacedCells)
        {
            move.Cell!.X = move.Position.X;
            move.Cell.Y = move.Position.Y;
            _project.Cells.Add(move.Cell);
        }

        _selectedCells.Clear();
        _selectedCells.UnionWith(targetPositions);
        _selectionAnchor = (_selectionAnchor.X + deltaX, _selectionAnchor.Y + deltaY);
        _selectedX = source.X + deltaX;
        _selectedY = source.Y + deltaY;
        MarkDirty();
        RefreshMovedCellVisuals(affectedPositions, visualBitmaps);
        UpdateSelectionVisuals(affectedPositions);
        RefreshPreview();
        SetStatus($"選択範囲の{sourcePositions.Count}セルを ({source.X + 1}, {source.Y + 1}) から ({target.X + 1}, {target.Y + 1}) へ移動しました。");
    }

    private Dictionary<SpriteCell, Bitmap?> CaptureCellVisualBitmaps(IEnumerable<(int X, int Y)> positions)
    {
        var result = new Dictionary<SpriteCell, Bitmap?>();
        var cellsByPosition = _project.Cells.ToDictionary(cell => (cell.X, cell.Y));
        foreach (var position in positions)
        {
            if (cellsByPosition.TryGetValue(position, out var cell) && _cellVisuals.TryGetValue(position, out var visual))
                result[cell] = visual.Image.Source as Bitmap;
        }
        return result;
    }

    private void RefreshMovedCellVisuals(IEnumerable<(int X, int Y)> positions, IReadOnlyDictionary<SpriteCell, Bitmap?> visualBitmaps)
    {
        RebuildGridCellIndex();
        var cellsByPosition = _gridCellsByPosition;
        foreach (var position in positions)
        {
            if (!_cellVisuals.TryGetValue(position, out var visual) || visual.Frame.Child is not Grid tile) continue;
            cellsByPosition.TryGetValue(position, out var cell);
            // A moved cell keeps its already-rendered bitmap. Re-encoding every tile as PNG
            // made large range moves and ordinary swaps needlessly expensive.
            visual.Image.Source = cell is not null && visualBitmaps.TryGetValue(cell, out var bitmap) ? bitmap : null;
            if (cell is null)
            {
                if (visual.Remove is not null) visual.Remove.IsVisible = false;
            }
            else
            {
                if (visual.Remove is null)
                {
                    var removeButton = CreateCellRemoveButton();
                    tile.Children.Add(removeButton);
                    visual = (visual.Frame, visual.Selection, removeButton, visual.Image, visual.Loading);
                    _cellVisuals[position] = visual;
                }
            }
            UpdateCellNamePlate(tile, visual.Selection, cell);
            if (visual.Remove is not null)
                visual.Remove.IsVisible = position == (_selectedX, _selectedY) && cell is not null && _gridZoom >= GRID_DETAIL_ZOOM_THRESHOLD;
        }
    }

    private void RefreshCellVisual((int X, int Y) position)
    {
        var cell = FindCell(position.X, position.Y);
        RefreshCellVisual(position, cell);
    }

    private void RefreshCellVisual((int X, int Y) position, SpriteCell? cell)
    {
        if (cell is null) _gridCellsByPosition.Remove(position);
        else _gridCellsByPosition[position] = cell;
        if (!_cellVisuals.TryGetValue(position, out var visual) || visual.Frame.Child is not Grid tile) return;
        if (cell is null)
        {
            SetImage(visual.Image, null);
        }
        else
        {
            SetImage(visual.Image, CreateBitmap(RenderCellThumbnailForUI(cell, 96)));
            if (visual.Remove is null)
            {
                var removeButton = CreateCellRemoveButton();
                tile.Children.Add(removeButton);
                visual = (visual.Frame, visual.Selection, removeButton, visual.Image, visual.Loading);
                _cellVisuals[position] = visual;
            }
        }
        UpdateCellNamePlate(tile, visual.Selection, cell);
        if (visual.Remove is not null)
            visual.Remove.IsVisible = position == (_selectedX, _selectedY) && cell is not null && _gridZoom >= GRID_DETAIL_ZOOM_THRESHOLD;
    }

    private Button CreateCellRemoveButton()
    {
        var button = new Button
        {
            Content = "×", Width = 22, Height = 22, Padding = new Thickness(0), FontSize = 16,
            HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(2), Background = Brush.Parse("#D91F2937"), Foreground = Brushes.White,
            BorderBrush = Brush.Parse("#8B9AAA"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(11),
            IsVisible = false
        };
        button.Click += async (_, _) => await DeleteSelectedCellAsync();
        return button;
    }

    private Border CreateCellNamePlate(SpriteCell cell)
    {
        var name = new TextBlock
        {
            Text = cell.DisplayName, FontSize = 11 * _gridZoom, Foreground = Brush.Parse("#FFFFFF"),
            TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.NoWrap,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(5 * _gridZoom, 3 * _gridZoom)
        };
        return new Border
        {
            Background = Brush.Parse("#CC090B0E"), VerticalAlignment = VerticalAlignment.Bottom,
            Padding = new Thickness(2 * _gridZoom, 1 * _gridZoom), Child = name, Tag = "cell-name-plate"
        };
    }

    private void UpdateCellNamePlate(Grid tile, Border selection, SpriteCell? cell)
    {
        var namePlate = tile.Children.OfType<Border>().FirstOrDefault(border => Equals(border.Tag, "cell-name-plate"));
        if (cell is not null && _gridZoom >= GRID_DETAIL_ZOOM_THRESHOLD)
        {
            if (namePlate is null)
            {
                namePlate = CreateCellNamePlate(cell);
                var selectionIndex = tile.Children.IndexOf(selection);
                tile.Children.Insert(Math.Max(1, selectionIndex), namePlate);
            }
            else if (namePlate.Child is TextBlock name)
            {
                name.Text = cell.DisplayName;
            }
        }
        else if (namePlate is not null)
        {
            tile.Children.Remove(namePlate);
        }
    }

    private async Task OpenCellAssetLocationAsync(int x, int y)
    {
        var cell = FindCell(x, y);
        if (cell is null) return;
        try { await _platformServices.OpenAssetLocationAsync(ProjectValidator.ResolveAssetPath(_projectDirectory, cell.AssetPath)); }
        catch (Exception ex) { await Dialogs.ShowErrorAsync(this, $"画像の保存場所を開けませんでした。\n{ex.Message}"); }
    }

    private async Task OpenSelectedCellAssetLocationsAsync(IReadOnlySet<(int X, int Y)> positions)
    {
        var cells = _project.Cells.Where(cell => positions.Contains((cell.X, cell.Y)))
            .GroupBy(cell => cell.AssetPath, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToArray();
        foreach (var cell in cells)
            await OpenCellAssetLocationAsync(cell.X, cell.Y);
    }

    private async Task ImportAsync(string sourcePath, int x, int y)
    {
        if (_imageImportInProgress || _imagePickerInProgress || _pngExportInProgress) return;

        _imageImportInProgress = true;
        var target = (X: x, Y: y);
        _loadingImportTarget = target;
        var previousStatus = _status.Text ?? string.Empty;
        var revisionAtStart = _projectRevision;
        ImportedSprite? copiedImport = null;
        string? copiedIntoDirectory = null;
        SKBitmap? pendingPreviewBitmap = null;
        var loadTimer = Stopwatch.StartNew();
        var imageProcessingTimer = Stopwatch.StartNew();
        var imageProcessingMilliseconds = 0d;
        var gridRefreshMilliseconds = 0d;
        SetCellLoading(target, true);
        try
        {
            SetProgressStatus("画像情報を確認中...", isIndeterminate: true);
            var (imageWidth, imageHeight) = await Task.Run(() => SpriteImageService.GetPngSize(sourcePath));
            EnsureImportProjectUnchanged(revisionAtStart);
            var cellWidth = _project.CellWidth;
            var cellHeight = _project.CellHeight;
            var splitCountConfirmed = false;
            var isInitialEmptyProject = _project.Columns == 1 && _project.Rows == 1 && _project.Cells.Count == 0;
            if (isInitialEmptyProject)
            {
                SetProgressStatus("画像プレビューを読み込み中...", isIndeterminate: true);
                var preview = await Task.Run(() => SpriteImageService.DecodeForPreview(sourcePath, 512));
                EnsureImportProjectUnchanged(revisionAtStart);
                using var previewBitmap = preview.Bitmap;
                using var sampleImage = CreateBitmap(previewBitmap);
                loadTimer.Stop();
                var initialCellWidth = imageWidth;
                var initialCellHeight = imageHeight;
                while (initialCellHeight != 0)
                {
                    var remainder = initialCellWidth % initialCellHeight;
                    initialCellWidth = initialCellHeight;
                    initialCellHeight = remainder;
                }
                initialCellHeight = initialCellWidth;
                if ((long)(imageWidth / initialCellWidth) * (imageHeight / initialCellWidth) > ProjectValidator.MaximumGridCellCount)
                {
                    initialCellWidth = imageWidth;
                    initialCellHeight = imageHeight;
                }
                var initialSize = await Dialogs.PickTrimmedImportCellSizeAsync(
                    this, imageWidth, imageHeight, initialCellWidth, initialCellHeight, sampleImage);
                loadTimer.Start();
                if (initialSize is null) { SetStatus(previousStatus); return; }
                EnsureImportProjectUnchanged(revisionAtStart);
                cellWidth = initialSize.Value.CellWidth;
                cellHeight = initialSize.Value.CellHeight;
                splitCountConfirmed = true;
            }
            else if (imageWidth % cellWidth != 0 || imageHeight % cellHeight != 0)
            {
                SetProgressStatus("画像プレビューを読み込み中...", isIndeterminate: true);
                var preview = await Task.Run(() => SpriteImageService.DecodeForPreview(sourcePath, 512));
                using var previewBitmap = preview.Bitmap;
                using var sampleImage = CreateBitmap(previewBitmap);
                loadTimer.Stop();
                var selectedSize = await Dialogs.PickTrimmedImportCellSizeAsync(
                    this, imageWidth, imageHeight, cellWidth, cellHeight, sampleImage, allowCellSizeChange: _project.Cells.Count == 0);
                loadTimer.Start();
                if (selectedSize is null) { SetStatus(previousStatus); return; }
                EnsureImportProjectUnchanged(revisionAtStart);
                cellWidth = selectedSize.Value.CellWidth;
                cellHeight = selectedSize.Value.CellHeight;
            }
            var tileColumns = imageWidth / cellWidth;
            var tileRows = imageHeight / cellHeight;
            var neededColumns = Math.Max(_project.Columns, x + tileColumns);
            var neededRows = Math.Max(_project.Rows, y + tileRows);
            var sheetWidth = neededColumns * (long)cellWidth; var sheetHeight = neededRows * (long)cellHeight;
            if ((long)neededColumns * neededRows > ProjectValidator.MaximumGridCellCount || sheetWidth > 16_384 || sheetHeight > 16_384 || sheetWidth * sheetHeight > ProjectValidator.MaximumSheetPixelCount)
                throw new InvalidDataException("取り込み後のスプライトシートが大きすぎます。");
            if (!splitCountConfirmed && (neededColumns != _project.Columns || neededRows != _project.Rows))
            {
                loadTimer.Stop();
                var yes = await Dialogs.ConfirmAsync(this, "X/Yの分割数を拡張しますか？", $"画像を配置するにはX/Yの分割数を {_project.Columns}×{_project.Rows} から {neededColumns}×{neededRows} に拡張する必要があります。拡張しますか？", "拡張して配置");
                loadTimer.Start();
                EnsureImportProjectUnchanged(revisionAtStart);
                if (!yes) { SetStatus(previousStatus); return; }
            }
            var occupied = _project.Cells.Where(cell => cell.X >= x && cell.X < x + tileColumns && cell.Y >= y && cell.Y < y + tileRows).ToArray();
            if (occupied.Length > 0)
            {
                loadTimer.Stop();
                var overwrite = await Dialogs.ConfirmAsync(this, "セルを置き換えますか？", "配置先にある画像は置き換えられます。続行しますか？", "置き換える");
                loadTimer.Start();
                EnsureImportProjectUnchanged(revisionAtStart);
                if (!overwrite) { SetStatus(previousStatus); return; }
            }

            EnsureImportProjectUnchanged(revisionAtStart);
            var importProject = _project.DeepClone();
            importProject.CellWidth = cellWidth;
            importProject.CellHeight = cellHeight;
            copiedIntoDirectory = _projectDirectory;
            imageProcessingTimer.Restart();
            SetProgressStatus("画像をデコードしてセルを生成中...", isIndeterminate: true);
            var preparedImport = await Task.Run(() =>
            {
                var maximumDimension = GetUiSourceMaximumDimension(sourcePath, cellWidth, cellHeight, imageWidth, imageHeight);
                var decodedPreview = SpriteImageService.DecodeForPreview(sourcePath, maximumDimension);
                try
                {
                    var importedSprite = SpriteImageService.ImportPng(sourcePath, copiedIntoDirectory, importProject, x, y, trimRightAndBottom: true);
                    return (Imported: importedSprite, Preview: decodedPreview);
                }
                catch
                {
                    decodedPreview.Bitmap.Dispose();
                    throw;
                }
            });
            imageProcessingTimer.Stop();
            imageProcessingMilliseconds = imageProcessingTimer.Elapsed.TotalMilliseconds;
            copiedImport = preparedImport.Imported;
            pendingPreviewBitmap = preparedImport.Preview.Bitmap;
            var previewSource = new CachedSourceBitmap(preparedImport.Preview.Bitmap, preparedImport.Preview.ScaleX, preparedImport.Preview.ScaleY);

            EnsureImportProjectUnchanged(revisionAtStart);
            PushUndo();
            _project.CellWidth = cellWidth;
            _project.CellHeight = cellHeight;
            _project.Assets.Add(new SpriteAsset { RelativePath = copiedImport.AssetPath, Width = copiedImport.Width, Height = copiedImport.Height });
            _project.Cells.RemoveAll(cell => copiedImport.Cells.Any(incoming => incoming.X == cell.X && incoming.Y == cell.Y));
            _project.Cells.AddRange(copiedImport.Cells);
            _sourceBitmaps[copiedImport.AssetPath] = previewSource;
            pendingPreviewBitmap = null;
            _project.Columns = copiedImport.RequiredColumns; _project.Rows = copiedImport.RequiredRows;
            var importedCellCount = copiedImport.Cells.Count;
            copiedImport = null;
            _selectedX = x; _selectedY = y; ResetSelectionToCurrentCell(); MarkDirty(); SyncFields();
            var gridTimer = Stopwatch.StartNew();
            NormalizeSelection();
            if (_selectAllButton is not null)
            {
                _selectAllButton.Content = AreAllCellsSelected() ? "全解除" : "全選択";
                _selectAllButton.IsEnabled = _project.Columns != 1 || _project.Rows != 1;
            }
            SetProgressStatus("表示中のセルを更新中...", isIndeterminate: false, 0);
            await RefreshGridForImportAsync();
            SetProgressStatus("セル一覧を更新しました", isIndeterminate: false, 100);
            RefreshPreview();
            gridTimer.Stop();
            gridRefreshMilliseconds = gridTimer.Elapsed.TotalMilliseconds;
            loadTimer.Stop();
            SetStatus($"取り込みました: {_platformServices.GetDisplayName(sourcePath)}（{importedCellCount}セル）（読み込み時間: {loadTimer.Elapsed.TotalMilliseconds:F0} ms / 画像処理: {imageProcessingMilliseconds:F0} ms / セル一覧更新: {gridRefreshMilliseconds:F0} ms）");
        }
        catch (Exception ex)
        {
            if (copiedImport is not null && copiedIntoDirectory is not null)
                TryDeleteImportedAsset(copiedIntoDirectory, copiedImport.AssetPath);
            pendingPreviewBitmap?.Dispose();
            SetStatus(previousStatus);
            await Dialogs.ShowErrorAsync(this, GetImageLoadFailureMessage(sourcePath, ex));
        }
        finally
        {
            imageProcessingTimer.Stop();
            loadTimer.Stop();
            SetCellLoading(target, false);
            _imageImportInProgress = false;
            HideProgressStatus();
        }
    }

    private void EnsureImportProjectUnchanged(int revisionAtStart)
    {
        if (_projectRevision != revisionAtStart)
            throw new InvalidOperationException("読み込み中にプロジェクトが変更されたため、画像の取り込みを中止しました。もう一度お試しください。");
    }

    private void TryDeleteImportedAsset(string projectDirectory, string relativePath)
    {
        try
        {
            var fullPath = Path.GetFullPath(Path.Combine(projectDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar)));
            var importRoot = Path.GetFullPath(Path.Combine(projectDirectory, ProjectValidator.ImportRootDirectoryName)) + Path.DirectorySeparatorChar;
            if (fullPath.StartsWith(importRoot, _platformServices.PathComparison))
                File.Delete(fullPath);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private void SetCellLoading((int X, int Y) position, bool isLoading)
    {
        _loadingImportTarget = isLoading ? position : null;
        if (_cellVisuals.TryGetValue(position, out var visual)) visual.Loading.IsVisible = isLoading;
    }

    private string GetImageLoadFailureMessage(string sourcePath, Exception exception)
    {
        var reason = exception switch
        {
            FileNotFoundException => "指定された画像ファイルが見つかりません。",
            DirectoryNotFoundException => "画像ファイルの保存先フォルダーが見つかりません。",
            UnauthorizedAccessException => "画像ファイルを読み取る権限がありません。",
            OutOfMemoryException => "画像の展開に必要なメモリが不足しています。画像サイズを小さくして再度お試しください。",
            InvalidDataException => exception.Message,
            IOException => $"画像ファイルの読み取り中に入出力エラーが発生しました。{Environment.NewLine}{exception.Message}",
            _ => $"{exception.GetType().Name}: {exception.Message}"
        };

        return $"画像を読み込めませんでした: {_platformServices.GetDisplayName(sourcePath)}{Environment.NewLine}原因: {reason}";
    }

    private void RefreshGrid()
    {
        UpdatePngExportButtonState();
        PrepareGridRefresh();
        RefreshVisibleGridCells();
        _cellsToAnimateOnRefresh.Clear();
    }

    private void UpdatePngExportButtonState()
    {
        if (_pngExportButton is null) return;
        _pngExportButton.IsEnabled = !(_project.Columns == 1 && _project.Rows == 1 && _project.Cells.Count == 0);
    }

    private Task RefreshGridForImportAsync()
    {
        RefreshGrid();
        return Task.CompletedTask;
    }

    private void PrepareGridRefresh()
    {
        DisposeGridVisuals();
        RebuildGridCellIndex();
        var stride = 106 * _gridZoom;
        _grid.Width = Math.Max(stride, _project.Columns * stride);
        _grid.Height = Math.Max(stride, _project.Rows * stride);
        _startGuide.IsVisible = _project.Columns == 1 && _project.Rows == 1 && _project.Cells.Count == 0;
    }

    private void DisposeGridVisuals()
    {
        foreach (var visual in _cellVisuals.Values)
            if (visual.Image.Source is IDisposable bitmap) bitmap.Dispose();
        _grid.Children.Clear();
        _cellVisuals.Clear();
    }

    private void RebuildGridCellIndex() => _gridCellsByPosition = _project.Cells.ToDictionary(cell => (cell.X, cell.Y));

    private void RefreshVisibleGridCells()
    {
        if (_gridScroll.Viewport.Width <= 0 || _gridScroll.Viewport.Height <= 0) return;

        var stride = 106 * _gridZoom;
        var firstX = Math.Max(0, (int)Math.Floor(_gridScroll.Offset.X / stride) - 1);
        var firstY = Math.Max(0, (int)Math.Floor(_gridScroll.Offset.Y / stride) - 1);
        var lastX = Math.Min(_project.Columns, (int)Math.Ceiling((_gridScroll.Offset.X + _gridScroll.Viewport.Width) / stride) + 1);
        var lastY = Math.Min(_project.Rows, (int)Math.Ceiling((_gridScroll.Offset.Y + _gridScroll.Viewport.Height) / stride) + 1);
        var visibleCells = new HashSet<(int X, int Y)>();
        for (var y = firstY; y < lastY; y++)
        for (var x = firstX; x < lastX; x++)
            visibleCells.Add((x, y));

        foreach (var position in _cellVisuals.Keys.Where(position => !visibleCells.Contains(position)).ToArray())
        {
            var visual = _cellVisuals[position];
            _grid.Children.Remove(visual.Frame);
            if (visual.Image.Source is IDisposable bitmap) bitmap.Dispose();
            _cellVisuals.Remove(position);
        }

        foreach (var (x, y) in visibleCells)
            if (!_cellVisuals.ContainsKey((x, y))) AddCellVisual(x, y);
    }

    private void AddCellVisual(int x, int y)
    {
        var cellX = x;
        var cellY = y;
        _gridCellsByPosition.TryGetValue((x, y), out var cell);
        var isSelected = x == _selectedX && y == _selectedY;
        var isRangeSelected = _selectedCells.Contains((x, y));
        var tileSize = 96 * _gridZoom;
        var image = new Image { Stretch = Stretch.Uniform };
        RenderOptions.SetBitmapInterpolationMode(image, BitmapInterpolationMode.None);
        if (cell is not null) SetImage(image, CreateBitmap(RenderCellThumbnailForUI(cell, 96)));
        var tile = new Grid { Width = tileSize, Height = tileSize };
        tile.Children.Add(image);
        var selectionOverlay = new Border
        {
            BorderBrush = Brush.Parse(isSelected ? "#60A5FA" : "#FBBF24"), BorderThickness = new Thickness(3),
            CornerRadius = new CornerRadius(4), Background = isSelected ? Brush.Parse("#2260A5FA") : Brush.Parse("#22FBBF24"),
            IsHitTestVisible = false, IsVisible = isRangeSelected, ZIndex = 10
        };
        tile.Children.Add(selectionOverlay);
        var loadingOverlay = new Border
        {
            Background = Brush.Parse("#D9161A20"), CornerRadius = new CornerRadius(5),
            IsHitTestVisible = false, IsVisible = _loadingImportTarget == (x, y), ZIndex = 15,
            Child = new TextBlock
            {
                Text = "Loading...", Foreground = Brush.Parse("#F3F4F6"), FontSize = 12 * _gridZoom,
                FontWeight = FontWeight.SemiBold, HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        tile.Children.Add(loadingOverlay);
        UpdateCellNamePlate(tile, selectionOverlay, cell);
        Button? removeButton = null;
        if (cell is not null)
        {
            removeButton = CreateCellRemoveButton();
            removeButton.IsVisible = isSelected && _gridZoom >= GRID_DETAIL_ZOOM_THRESHOLD;
            tile.Children.Add(removeButton);
        }
        var animateAppearance = _cellsToAnimateOnRefresh.Contains((x, y));
        var border = new Border
        {
            Child = tile, Width = tileSize, Height = tileSize, Margin = new Thickness(5 * _gridZoom),
            BorderThickness = new Thickness(isSelected ? 2 : 1), BorderBrush = Brush.Parse(isSelected ? "#60A5FA" : isRangeSelected ? "#FBBF24" : "#3F4652"),
            CornerRadius = new CornerRadius(7), Background = Brush.Parse("#20242C"), Opacity = animateAppearance ? 0 : 1,
            Transitions = animateAppearance ? CreateOpacityTransition() : null
        };
        EditorToolTip.SetTip(border, GetCellToolTip(isSelected, cell is not null));
        border.PointerPressed += (_, e) =>
        {
            if (e.Source is Button || e.Source is Visual source && source.GetVisualAncestors().OfType<Button>().Any()) return;
            var properties = e.GetCurrentPoint(border).Properties;
            if (properties.IsLeftButtonPressed) { _pressedCell = (cellX, cellY); _pressedCellModifiers = e.KeyModifiers; }
            else if (properties.IsRightButtonPressed)
            {
                _pressedCell = (cellX, cellY);
                _pressedRightCell = true;
            }
        };
        DragDrop.SetAllowDrop(border, true);
        border.AddHandler(DragDrop.DragOverEvent, (_, e) => e.DragEffects = e.DataTransfer.Formats.Contains(DataFormat.File) ? DragDropEffects.Copy : DragDropEffects.None);
        border.AddHandler(DragDrop.DropEvent, async (_, e) =>
        {
            var file = e.DataTransfer.TryGetFiles()?.FirstOrDefault();
            if (file is IStorageFile storageFile)
            {
                var stagedPath = await _platformServices.StageFileAsync(storageFile);
                try { await ImportAsync(stagedPath, cellX, cellY); }
                finally { await _platformServices.ReleaseTemporaryFileAsync(stagedPath); }
            }
            e.Handled = true;
        });
        Canvas.SetLeft(border, x * 106 * _gridZoom);
        Canvas.SetTop(border, y * 106 * _gridZoom);
        _grid.Children.Add(border);
        _cellVisuals[(x, y)] = (border, selectionOverlay, removeButton, image, loadingOverlay);
        if (animateAppearance) Dispatcher.UIThread.Post(() => border.Opacity = 1, DispatcherPriority.Loaded);
    }

    private static Transitions CreateOpacityTransition() => new()
    {
        new DoubleTransition { Property = Visual.OpacityProperty, Duration = TimeSpan.FromMilliseconds(180) }
    };

    private string GetCellToolTip(bool isCurrent, bool hasImage)
    {
        if (isCurrent && hasImage && _selectedCells.Count > 1)
            return "左クリックで画像を取り込み。\n右クリックでセル操作メニューを表示。\n左ドラッグで選択範囲を並べ替え。Delキーで削除";
        if (isCurrent && hasImage)
            return "左クリックで画像を取り込み。\n右クリックでセル操作メニューを表示。\n左ドラッグで他セルと入れ替え";
        return "クリックでセルを選択。\n右クリックでセル操作メニューを表示。\nCtrl+クリックで複数選択、Shift+クリックで範囲選択。\n選択中クリック、またはドラッグ＆ドロップで画像を取り込み";
    }

    private void UpdateSelectedCell(int x, int y)
    {
        _selectedX = x;
        _selectedY = y;
        _selectedCells.Clear();
        _selectedCells.Add((x, y));
        _selectionAnchor = (x, y);
        ResetOffsetBatch();
        UpdateSelectionVisuals();
        RefreshPreview();
        ShowSelectionStatus();
    }

    private void SelectCellWithModifiers(int x, int y, KeyModifiers modifiers)
    {
        var useControl = modifiers.HasFlag(KeyModifiers.Control);
        var useShift = modifiers.HasFlag(KeyModifiers.Shift);
        if (useShift)
        {
            if (!useControl) _selectedCells.Clear();
            var left = Math.Min(_selectionAnchor.X, x);
            var right = Math.Max(_selectionAnchor.X, x);
            var top = Math.Min(_selectionAnchor.Y, y);
            var bottom = Math.Max(_selectionAnchor.Y, y);
            for (var rangeY = top; rangeY <= bottom; rangeY++)
            for (var rangeX = left; rangeX <= right; rangeX++)
                _selectedCells.Add((rangeX, rangeY));
        }
        else if (useControl)
        {
            if (!_selectedCells.Add((x, y))) _selectedCells.Remove((x, y));
            if (_selectedCells.Count == 0) _selectedCells.Add((x, y));
            _selectionAnchor = (x, y);
        }
        else
        {
            _selectedCells.Clear();
            _selectedCells.Add((x, y));
            _selectionAnchor = (x, y);
        }
        _selectedX = x;
        _selectedY = y;
        ResetOffsetBatch();
        UpdateSelectionVisuals();
        RefreshPreview();
        ShowSelectionStatus();
    }

    private void UpdateSelectionVisuals(IEnumerable<(int X, int Y)>? positions = null)
    {
        if (_selectAllButton is not null)
            _selectAllButton.Content = AreAllCellsSelected() ? "全解除" : "全選択";
        var visuals = positions is null
            ? _cellVisuals
            : positions.Distinct().Where(_cellVisuals.ContainsKey).ToDictionary(position => position, position => _cellVisuals[position]);
        foreach (var (position, visual) in visuals)
        {
            var isCurrent = position == (_selectedX, _selectedY);
            var isRangeSelected = _selectedCells.Contains(position);
            var color = isCurrent ? "#60A5FA" : isRangeSelected ? "#FBBF24" : "#3F4652";
            visual.Frame.BorderBrush = Brush.Parse(color);
            visual.Frame.BorderThickness = new Thickness(isCurrent ? 2 : 1);
            visual.Selection.BorderBrush = Brush.Parse(color);
            visual.Selection.Background = isCurrent ? Brush.Parse("#2260A5FA") : isRangeSelected ? Brush.Parse("#22FBBF24") : Brushes.Transparent;
            visual.Selection.IsVisible = isRangeSelected;
            var hasImage = _gridCellsByPosition.ContainsKey(position);
            if (visual.Remove is not null) visual.Remove.IsVisible = isCurrent && hasImage && _gridZoom >= GRID_DETAIL_ZOOM_THRESHOLD;
            EditorToolTip.SetTip(visual.Frame, GetCellToolTip(isCurrent, hasImage));
        }
    }

    private void HandleGridPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source is Button || e.Source is Visual source &&
            (source is ScrollBar || source.GetVisualAncestors().OfType<ScrollBar>().Any() || source.GetVisualAncestors().OfType<Button>().Any())) return;
        var properties = e.GetCurrentPoint(_gridScroll).Properties;
        if (!properties.IsLeftButtonPressed && !properties.IsMiddleButtonPressed && !properties.IsRightButtonPressed) return;
        _panPointerId = e.Pointer.Id;
        _panStartPosition = e.GetPosition(_gridScroll);
        _panStartOffset = _gridScroll.Offset;
        _panDragging = false;
        _cellDragDragging = false;
        _pressedCell = null;
        _pressedCellModifiers = KeyModifiers.None;
        _pressedRightCell = false;
    }

    private void HandleGridPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_panPointerId != e.Pointer.Id) return;
        var properties = e.GetCurrentPoint(_gridScroll).Properties;
        if (!properties.IsLeftButtonPressed && !properties.IsMiddleButtonPressed && !properties.IsRightButtonPressed) return;
        var position = e.GetPosition(_gridScroll);
        var delta = position - _panStartPosition;
        if ((Math.Abs(delta.X) + Math.Abs(delta.Y)) < 5) return;
        if (properties.IsLeftButtonPressed)
        {
            if (_pressedCell is null) return;
            _cellDragDragging = true;
            e.Pointer.Capture(_gridScroll);
            if (_cellVisuals.TryGetValue(_pressedCell.Value, out var sourceCell))
            {
                _dragGhost.Source = sourceCell.Image.Source;
                _dragGhost.Width = 96 * _gridZoom;
                _dragGhost.Height = 96 * _gridZoom;
                _dragGhost.IsVisible = _dragGhost.Source is not null;
            }
            var destination = GetGridCellAt(position);
            UpdateCellDragFeedback(_pressedCell.Value, destination);
            var ghostPosition = e.GetPosition(_gridOverlayCanvas);
            Canvas.SetLeft(_dragGhost, ghostPosition.X - _dragGhost.Width / 2);
            Canvas.SetTop(_dragGhost, ghostPosition.Y - _dragGhost.Height / 2);
            e.Handled = true;
            return;
        }
        if (!properties.IsRightButtonPressed && !properties.IsMiddleButtonPressed) return;
        _panDragging = true;
        e.Pointer.Capture(_gridScroll);
        CancelScrollAnimation(finishAtTarget: false);
        _gridScroll.Offset = new Vector(
            Math.Clamp(_panStartOffset.X - delta.X, 0, Math.Max(0, _gridScroll.Extent.Width - _gridScroll.Viewport.Width)),
            Math.Clamp(_panStartOffset.Y - delta.Y, 0, Math.Max(0, _gridScroll.Extent.Height - _gridScroll.Viewport.Height)));
        e.Handled = true;
    }

    private async void HandleGridPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_panPointerId != e.Pointer.Id) return;
        var didPan = _panDragging;
        var didDragCell = _cellDragDragging;
        var cell = _pressedCell;
        var modifiers = _pressedCellModifiers;
        var rightClick = _pressedRightCell;
        var droppedOn = GetGridCellAt(e.GetPosition(_gridScroll));
        ClearCellDragFeedback();
        _dragGhost.IsVisible = false;
        _dragGhost.Source = null;
        _panPointerId = null;
        _panDragging = false;
        _cellDragDragging = false;
        _pressedCell = null;
        _pressedCellModifiers = KeyModifiers.None;
        _pressedRightCell = false;
        if (didPan)
        {
            e.Handled = true;
            return;
        }
        if (cell is not { } selectedCell) return;
        if (didDragCell)
        {
            if (droppedOn is { } target)
            {
                if (_selectedCells.Count > 1 && _selectedCells.Contains(selectedCell)) MoveSelectedRange(selectedCell, target);
                else MoveCell(selectedCell.X, selectedCell.Y, target.X, target.Y);
            }
            e.Handled = true;
            return;
        }
        if (rightClick)
        {
            ShowCellContextMenu(selectedCell.X, selectedCell.Y);
            e.Handled = true;
            return;
        }
        var alreadySelected = _selectedX == selectedCell.X && _selectedY == selectedCell.Y;
        if (modifiers.HasFlag(KeyModifiers.Control) || modifiers.HasFlag(KeyModifiers.Shift))
        {
            SelectCellWithModifiers(selectedCell.X, selectedCell.Y, modifiers);
            await EnsureCellScrolledFromViewportEdgeAsync(selectedCell.X, selectedCell.Y);
            e.Handled = true;
            return;
        }
        if (!alreadySelected) UpdateSelectedCell(selectedCell.X, selectedCell.Y);
        await EnsureCellScrolledFromViewportEdgeAsync(selectedCell.X, selectedCell.Y);
        if (alreadySelected) await PickAndImportAsync(selectedCell.X, selectedCell.Y);
        e.Handled = true;
    }

    private (int X, int Y)? GetGridCellAt(Point viewportPosition)
    {
        const double strideAtBaseZoom = 106;
        var step = strideAtBaseZoom * _gridZoom;
        var x = (int)Math.Floor((_gridScroll.Offset.X + viewportPosition.X) / step);
        var y = (int)Math.Floor((_gridScroll.Offset.Y + viewportPosition.Y) / step);
        return x >= 0 && x < _project.Columns && y >= 0 && y < _project.Rows ? (x, y) : null;
    }

    private void ShowCellContextMenu(int x, int y)
    {
        if (!_selectedCells.Contains((x, y)))
            UpdateSelectedCell(x, y);
        if (!_cellVisuals.TryGetValue((x, y), out var visual)) return;

        var selectedPositions = _selectedCells.ToHashSet();
        var selectedCells = _project.Cells.Where(cell => selectedPositions.Contains((cell.X, cell.Y))).ToArray();
        var hasCellsToShiftRight = selectedPositions.GroupBy(position => position.Y)
            .Any(group => _project.Cells.Any(cell => cell.Y == group.Key && cell.X >= group.Min(position => position.X)));
        var hasCellsToShiftDown = selectedPositions.GroupBy(position => position.X)
            .Any(group => _project.Cells.Any(cell => cell.X == group.Key && cell.Y >= group.Min(position => position.Y)));
        var canInsertRight = CanTransformSelectedLines(selectedPositions, horizontal: true, insert: true);
        var canInsertDown = CanTransformSelectedLines(selectedPositions, horizontal: false, insert: true);
        var menu = new ContextMenu
        {
            Placement = PlacementMode.Pointer,
            PlacementTarget = visual.Frame,
            FontFamily = EditorToolTip.JapaneseFont
        };
        var copy = new MenuItem { Header = "コピー (Ctrl+C)", IsEnabled = selectedCells.Length > 0 };
        copy.Click += (_, _) => CopySelectedCells();
        var paste = new MenuItem { Header = "貼り付け (Ctrl+V)", IsEnabled = _copiedCells is { Length: > 0 } };
        paste.Click += async (_, _) => await PasteCopiedCellsAsync();
        var openLocation = new MenuItem { Header = "ファイルの場所を開く", IsEnabled = selectedCells.Length > 0 };
        if (_platformServices.CanOpenAssetLocation)
            openLocation.Click += async (_, _) => await OpenSelectedCellAssetLocationsAsync(selectedPositions);
        var insertRight = new MenuItem
        {
            Header = "空白セルを挿入（右にずらす）",
            IsEnabled = hasCellsToShiftRight && canInsertRight
        };
        insertRight.Click += (_, _) => TransformSelectedLines(selectedPositions, horizontal: true, insert: true);
        var insertDown = new MenuItem
        {
            Header = "空白セルを挿入（下にずらす）",
            IsEnabled = hasCellsToShiftDown && canInsertDown
        };
        insertDown.Click += (_, _) => TransformSelectedLines(selectedPositions, horizontal: false, insert: true);
        var clearCell = new MenuItem { Header = "セルを空にする", IsEnabled = selectedCells.Length > 0 };
        clearCell.Click += async (_, _) => await DeleteCellsAtPositionsAsync(selectedPositions);
        var shiftLeft = new MenuItem { Header = "セルを削除（左に詰める）", IsEnabled = CanTransformSelectedLines(selectedPositions, horizontal: true, insert: false) };
        shiftLeft.Click += (_, _) => TransformSelectedLines(selectedPositions, horizontal: true, insert: false);
        var shiftUp = new MenuItem { Header = "セルを削除（上に詰める）", IsEnabled = CanTransformSelectedLines(selectedPositions, horizontal: false, insert: false) };
        shiftUp.Click += (_, _) => TransformSelectedLines(selectedPositions, horizontal: false, insert: false);
        var fillCell = new MenuItem { Header = "セルを塗り潰す" };
        fillCell.Click += async (_, _) => await FillCellsAsync(selectedPositions);
        menu.Items.Add(copy);
        menu.Items.Add(paste);
        menu.Items.Add(new Separator());
        if (_platformServices.CanOpenAssetLocation) menu.Items.Add(openLocation);
        menu.Items.Add(insertRight);
        menu.Items.Add(insertDown);
        menu.Items.Add(clearCell);
        menu.Items.Add(shiftLeft);
        menu.Items.Add(shiftUp);
        menu.Items.Add(fillCell);
        menu.Open(visual.Frame);
    }

    private void CopySelectedCells()
    {
        var cells = _project.Cells.Where(cell => _selectedCells.Contains((cell.X, cell.Y))).ToArray();
        if (cells.Length == 0) return;
        _copiedCellOrigin = (cells.Min(cell => cell.X), cells.Min(cell => cell.Y));
        _copiedCells = cells.Select(cell => new SpriteCell
        {
            X = cell.X - _copiedCellOrigin.X,
            Y = cell.Y - _copiedCellOrigin.Y,
            AssetPath = cell.AssetPath,
            SourceRect = cell.SourceRect,
            OffsetX = cell.OffsetX,
            OffsetY = cell.OffsetY,
            DisplayName = cell.DisplayName
        }).ToArray();
        SetStatus($"{cells.Length}セルをコピーしました。");
    }

    private async Task PasteCopiedCellsAsync()
    {
        if (_copiedCells is not { Length: > 0 } copiedCells) return;
        var targetX = _selectedX;
        var targetY = _selectedY;
        var pastePositions = copiedCells.Select(cell => (X: targetX + cell.X, Y: targetY + cell.Y)).ToHashSet();
        var pasteColumns = Math.Max(_project.Columns, pastePositions.Max(position => position.X) + 1);
        var pasteRows = Math.Max(_project.Rows, pastePositions.Max(position => position.Y) + 1);
        if (!CanExpandGrid(pasteColumns, pasteRows))
        {
            SetStatus("貼り付け後のセル一覧が大きすぎるため貼り付けできません。");
            return;
        }

        var isSelfPaste = targetX == _copiedCellOrigin.X && targetY == _copiedCellOrigin.Y;
        Dictionary<SpriteCell, (int X, int Y)> shiftedCells = [];
        HashSet<SpriteCell> removedCells = [];
        var dimensions = (Columns: _project.Columns, Rows: _project.Rows);
        if (isSelfPaste && !TryBuildLineTransform(pastePositions, horizontal: true, insert: true,
                out shiftedCells, out removedCells, out dimensions))
        {
            SetStatus("右にずらす空きがないため貼り付けできません。");
            return;
        }
        if (!CanExpandGrid(Math.Max(dimensions.Columns, pasteColumns), Math.Max(dimensions.Rows, pasteRows)))
        {
            SetStatus("貼り付け後のセル一覧が大きすぎるため貼り付けできません。");
            return;
        }

        if (!isSelfPaste && _project.Cells.Any(cell => pastePositions.Contains((cell.X, cell.Y))))
        {
            var overwrite = await Dialogs.ConfirmAsync(this, "セルを置き換えますか？", "貼り付け先にある画像は置き換えられます。続行しますか？", "置き換える");
            if (!overwrite) return;
        }

        PushUndo();
        foreach (var cell in removedCells) _project.Cells.Remove(cell);
        foreach (var (cell, position) in shiftedCells)
        {
            cell.X = position.X;
            cell.Y = position.Y;
        }
        _project.Columns = Math.Max(dimensions.Columns, pasteColumns);
        _project.Rows = Math.Max(dimensions.Rows, pasteRows);
        _project.Cells.RemoveAll(cell => pastePositions.Contains((cell.X, cell.Y)));
        foreach (var cell in copiedCells)
        {
            _project.Cells.Add(new SpriteCell
            {
                X = targetX + cell.X,
                Y = targetY + cell.Y,
                AssetPath = cell.AssetPath,
                SourceRect = cell.SourceRect,
                OffsetX = cell.OffsetX,
                OffsetY = cell.OffsetY,
                DisplayName = cell.DisplayName
            });
        }
        _selectedX = targetX;
        _selectedY = targetY;
        _selectedCells.Clear();
        _selectedCells.UnionWith(pastePositions);
        _selectionAnchor = (targetX, targetY);
        MarkDirty();
        RefreshAll();
        SetStatus($"{copiedCells.Length}セルを貼り付けました。{(isSelfPaste ? "元のセルを右にずらしました。" : string.Empty)}");
    }

    private bool CanExpandGrid(int columns, int rows)
    {
        if (columns <= 0 || rows <= 0 || (long)columns * rows > ProjectValidator.MaximumGridCellCount) return false;
        var sheetWidth = (long)_project.CellWidth * columns;
        var sheetHeight = (long)_project.CellHeight * rows;
        return sheetWidth <= 16_384 && sheetHeight <= 16_384 && sheetWidth * sheetHeight <= ProjectValidator.MaximumSheetPixelCount;
    }

    private bool CanTransformSelectedLines(IReadOnlySet<(int X, int Y)> positions, bool horizontal, bool insert)
    {
        return TryBuildLineTransform(positions, horizontal, insert, out _, out _, out _);
    }

    private bool TryBuildLineTransform(
        IReadOnlySet<(int X, int Y)> positions,
        bool horizontal,
        bool insert,
        out Dictionary<SpriteCell, (int X, int Y)> destinations,
        out HashSet<SpriteCell> removals,
        out (int Columns, int Rows) dimensions)
    {
        destinations = [];
        removals = [];
        var columns = _project.Columns;
        var rows = _project.Rows;
        var cellsByLine = _project.Cells.GroupBy(cell => horizontal ? cell.Y : cell.X)
            .ToDictionary(group => group.Key, group => group.ToArray());
        var lines = positions.GroupBy(position => horizontal ? position.Y : position.X);
        foreach (var line in lines)
        {
            var selectedIndices = line.Select(position => horizontal ? position.X : position.Y).Distinct().Order().ToArray();
            if (!cellsByLine.TryGetValue(line.Key, out var lineCells)) continue;
            foreach (var cell in lineCells)
            {
                var index = horizontal ? cell.X : cell.Y;
                var selectedIndex = Array.BinarySearch(selectedIndices, index);
                if (!insert && selectedIndex >= 0)
                {
                    removals.Add(cell);
                    continue;
                }

                var shiftCount = selectedIndex >= 0 ? selectedIndex + 1 : ~selectedIndex;
                if (shiftCount == 0) continue;
                var targetIndex = insert ? index + shiftCount : index - shiftCount;
                destinations[cell] = horizontal ? (targetIndex, cell.Y) : (cell.X, targetIndex);
                if (insert)
                {
                    if (horizontal) columns = Math.Max(columns, targetIndex + 1);
                    else rows = Math.Max(rows, targetIndex + 1);
                }
            }
        }

        dimensions = (columns, rows);
        if (insert && !CanExpandGrid(columns, rows)) return false;
        return destinations.Count > 0 || removals.Count > 0;
    }

    private void TransformSelectedLines(IReadOnlySet<(int X, int Y)> positions, bool horizontal, bool insert)
    {
        if (!TryBuildLineTransform(positions, horizontal, insert, out var destinations, out var removals, out var dimensions)) return;
        var expandsGrid = dimensions.Columns > _project.Columns || dimensions.Rows > _project.Rows;
        PushUndo();
        foreach (var cell in removals) _project.Cells.Remove(cell);
        foreach (var (cell, destination) in destinations)
        {
            cell.X = destination.X;
            cell.Y = destination.Y;
        }
        _project.Columns = dimensions.Columns;
        _project.Rows = dimensions.Rows;
        MarkDirty();
        RefreshGrid();
        RefreshPreview();
        var operation = insert ? "空白を挿入してずらしました" : "選択位置を削除して詰めました";
        var axis = horizontal ? "行" : "列";
        SetStatus($"選択された{axis}のセルを{operation}。{(expandsGrid ? "シートのサイズを拡張しました。" : string.Empty)}");
    }

    private async Task FillCellsAsync(IReadOnlySet<(int X, int Y)> positions)
    {
        var targetPositions = positions.ToArray();
        if (targetPositions.Length == 0) return;
        var color = await Dialogs.PickFillColorAsync(this);
        if (color is not { } selectedColor) return;
        var path = string.Empty;
        var committed = false;
        try
        {
            var firstPosition = targetPositions[0];
            var fileName = $"Fill_{firstPosition.X + 1}_{firstPosition.Y + 1}_{Guid.NewGuid():N}.png";
            var relativePath = $"{ProjectValidator.ImportRootDirectoryName}/{_project.ImportFolderName}/{fileName}";
            var assetsDirectory = Path.Combine(_projectDirectory, ProjectValidator.ImportRootDirectoryName, _project.ImportFolderName);
            path = Path.Combine(assetsDirectory, fileName);
            var cellWidth = _project.CellWidth;
            var cellHeight = _project.CellHeight;
            await Task.Run(() =>
            {
                Directory.CreateDirectory(assetsDirectory);
                using var bitmap = new SKBitmap(cellWidth, cellHeight, SKColorType.Bgra8888, SKAlphaType.Premul);
                bitmap.Erase(new SKColor(selectedColor.R, selectedColor.G, selectedColor.B));
                SpriteImageService.SavePng(bitmap, path);
            });

            PushUndo();
            foreach (var (x, y) in targetPositions)
            {
                var cell = FindCell(x, y);
                if (cell is null)
                {
                    cell = new SpriteCell
                    {
                        X = x,
                        Y = y,
                        AssetPath = relativePath,
                        SourceRect = new SAMERIDER.Core.Models.PixelRect(0, 0, _project.CellWidth, _project.CellHeight),
                        DisplayName = Path.GetFileNameWithoutExtension(fileName)
                    };
                    _project.Cells.Add(cell);
                }
                else
                {
                    cell.AssetPath = relativePath;
                    cell.SourceRect = new SAMERIDER.Core.Models.PixelRect(0, 0, _project.CellWidth, _project.CellHeight);
                    cell.OffsetX = 0;
                    cell.OffsetY = 0;
                }
            }
            _project.Assets.Add(new SpriteAsset { RelativePath = relativePath, Width = _project.CellWidth, Height = _project.CellHeight });
            committed = true;
            MarkDirty();
            foreach (var (x, y) in targetPositions) RefreshCellVisual((x, y));
            UpdateSelectionVisuals(targetPositions);
            RefreshPreview();
            SetStatus($"選択した{targetPositions.Length}セルを塗り潰しました。");
        }
        catch (Exception exception)
        {
            if (!committed && !string.IsNullOrEmpty(path))
            {
                try { File.Delete(path); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            await Dialogs.ShowErrorAsync(this, $"セルを塗り潰せませんでした。{Environment.NewLine}{exception.Message}");
        }
    }

    private void UpdateCellDragFeedback((int X, int Y) source, (int X, int Y)? destination)
    {
        if (_dragSourceCell != source)
        {
            ClearCellDragFeedback();
            _dragSourceCell = source;
        }
        if (_dragTargetCell is { } previousTarget && previousTarget != destination) RestoreCellSelectionOverlay(previousTarget);
        _dragTargetCell = destination == source ? null : destination;
        if (_cellVisuals.TryGetValue(source, out var sourceVisual))
        {
            sourceVisual.Selection.BorderBrush = Brush.Parse("#FBBF24");
            sourceVisual.Selection.Background = Brush.Parse("#22382200");
            sourceVisual.Selection.IsVisible = true;
        }
        if (_dragTargetCell is { } target && _cellVisuals.TryGetValue(target, out var targetVisual))
        {
            targetVisual.Selection.BorderBrush = Brush.Parse("#4ADE80");
            targetVisual.Selection.Background = Brush.Parse("#334ADE80");
            targetVisual.Selection.IsVisible = true;
            _dragArrow.Data = CreateDragArrowGeometry(CellCenterOnGrid(source), CellCenterOnGrid(target));
            _dragArrow.IsVisible = true;
        }
        else
        {
            _dragArrow.IsVisible = false;
            _dragArrow.Data = null;
        }
    }

    private void ClearCellDragFeedback()
    {
        if (_dragSourceCell is { } source) RestoreCellSelectionOverlay(source);
        if (_dragTargetCell is { } target) RestoreCellSelectionOverlay(target);
        _dragSourceCell = null;
        _dragTargetCell = null;
        _dragArrow.IsVisible = false;
        _dragArrow.Data = null;
    }

    private void RestoreCellSelectionOverlay((int X, int Y) cell)
    {
        if (!_cellVisuals.TryGetValue(cell, out var visual)) return;
        var current = cell.X == _selectedX && cell.Y == _selectedY;
        var selected = _selectedCells.Contains(cell);
        visual.Selection.BorderBrush = Brush.Parse(current ? "#60A5FA" : selected ? "#FBBF24" : "#3F4652");
        visual.Selection.Background = current ? Brush.Parse("#2260A5FA") : selected ? Brush.Parse("#22FBBF24") : Brushes.Transparent;
        visual.Selection.IsVisible = selected;
    }

    private Point CellCenterOnGrid((int X, int Y) cell)
    {
        var step = 106 * _gridZoom;
        return new Point((cell.X + 0.5) * step - _gridScroll.Offset.X, (cell.Y + 0.5) * step - _gridScroll.Offset.Y);
    }

    private static StreamGeometry CreateDragArrowGeometry(Point start, Point end)
    {
        var delta = new Vector(end.X - start.X, end.Y - start.Y);
        var length = Math.Max(1, delta.Length);
        var normal = new Vector(-delta.Y / length, delta.X / length) * Math.Min(44, Math.Max(20, length * 0.18));
        var control1 = start + delta * 0.32 + normal;
        var control2 = start + delta * 0.68 + normal;
        var direction = end - control2;
        var angle = Math.Atan2(direction.Y, direction.X);
        const double arrowHeadLength = 13;
        var head1 = new Point(end.X - arrowHeadLength * Math.Cos(angle - 0.55), end.Y - arrowHeadLength * Math.Sin(angle - 0.55));
        var head2 = new Point(end.X - arrowHeadLength * Math.Cos(angle + 0.55), end.Y - arrowHeadLength * Math.Sin(angle + 0.55));
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(start, false);
            context.CubicBezierTo(control1, control2, end);
            context.EndFigure(false);
            context.BeginFigure(end, false);
            context.LineTo(head1);
            context.EndFigure(false);
            context.BeginFigure(end, false);
            context.LineTo(head2);
            context.EndFigure(false);
        }
        return geometry;
    }

    private void ConfigureGridScrollBars()
    {
        SyncGridScrollBars();
    }

    private void SyncGridScrollBars()
    {
        UpdateScrollTrack(_gridHorizontalScrollBar, _gridHorizontalThumb, Orientation.Horizontal);
        UpdateScrollTrack(_gridVerticalScrollBar, _gridVerticalThumb, Orientation.Vertical);
        UpdateBackgroundParallax();
    }

    private void UpdateScrollTrack(Canvas track, Border thumb, Orientation orientation)
    {
        var length = orientation == Orientation.Horizontal ? track.Bounds.Width : track.Bounds.Height;
        if (length <= 0) return;
        var extent = orientation == Orientation.Horizontal ? _gridScroll.Extent.Width : _gridScroll.Extent.Height;
        var viewport = orientation == Orientation.Horizontal ? _gridScroll.Viewport.Width : _gridScroll.Viewport.Height;
        var maxOffset = Math.Max(0, extent - viewport);
        var offset = orientation == Orientation.Horizontal ? _gridScroll.Offset.X : _gridScroll.Offset.Y;
        var minimumThumb = Math.Min(28, length);
        var thumbLength = maxOffset <= 0
            ? length
            : Math.Clamp(length * viewport / Math.Max(extent, 1), minimumThumb, length);
        thumbLength = Math.Min(length, thumbLength);
        if (orientation == Orientation.Horizontal)
        {
            thumb.Width = thumbLength;
            thumb.Height = track.Bounds.Height;
        }
        else
        {
            thumb.Width = track.Bounds.Width;
            thumb.Height = thumbLength;
        }
        var travel = length - thumbLength;
        var position = maxOffset <= 0 || travel <= 0 ? 0 : offset / maxOffset * travel;
        if (orientation == Orientation.Horizontal) Canvas.SetLeft(thumb, position);
        else Canvas.SetTop(thumb, position);
    }

    private void BeginScrollBarDrag(Canvas track, Border thumb, Orientation orientation, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(track).Properties.IsLeftButtonPressed) return;
        var pointer = e.GetPosition(track);
        var coordinate = orientation == Orientation.Horizontal ? pointer.X : pointer.Y;
        var thumbStart = orientation == Orientation.Horizontal ? Canvas.GetLeft(thumb) : Canvas.GetTop(thumb);
        if (!double.IsFinite(thumbStart)) thumbStart = 0;
        var thumbLength = orientation == Orientation.Horizontal ? thumb.Bounds.Width : thumb.Bounds.Height;
        var trackLength = orientation == Orientation.Horizontal ? track.Bounds.Width : track.Bounds.Height;
        var trackPosition = Math.Clamp(coordinate - thumbLength / 2, 0, Math.Max(0, trackLength - thumbLength));
        if (coordinate < thumbStart || coordinate > thumbStart + thumbLength)
        {
            thumbStart = trackPosition;
            SetScrollOffsetFromThumb(track, thumb, orientation, thumbStart);
        }
        _scrollbarDragAxis = orientation;
        _scrollbarPointerId = e.Pointer.Id;
        _scrollbarDragPointerStart = coordinate;
        _scrollbarDragThumbStart = thumbStart;
        e.Pointer.Capture(track);
        e.Handled = true;
    }

    private void MoveScrollBarThumb(Canvas track, Border thumb, Orientation orientation, PointerEventArgs e)
    {
        if (_scrollbarDragAxis != orientation || _scrollbarPointerId != e.Pointer.Id) return;
        var pointer = e.GetPosition(track);
        var coordinate = orientation == Orientation.Horizontal ? pointer.X : pointer.Y;
        var thumbLength = orientation == Orientation.Horizontal ? thumb.Bounds.Width : thumb.Bounds.Height;
        var trackLength = orientation == Orientation.Horizontal ? track.Bounds.Width : track.Bounds.Height;
        var position = Math.Clamp(_scrollbarDragThumbStart + coordinate - _scrollbarDragPointerStart, 0, Math.Max(0, trackLength - thumbLength));
        SetScrollOffsetFromThumb(track, thumb, orientation, position);
        e.Handled = true;
    }

    private void SetScrollOffsetFromThumb(Canvas track, Border thumb, Orientation orientation, double thumbPosition)
    {
        var extent = orientation == Orientation.Horizontal ? _gridScroll.Extent.Width : _gridScroll.Extent.Height;
        var viewport = orientation == Orientation.Horizontal ? _gridScroll.Viewport.Width : _gridScroll.Viewport.Height;
        var maxOffset = Math.Max(0, extent - viewport);
        var trackLength = orientation == Orientation.Horizontal ? track.Bounds.Width : track.Bounds.Height;
        var thumbLength = orientation == Orientation.Horizontal ? thumb.Bounds.Width : thumb.Bounds.Height;
        var travel = trackLength - thumbLength;
        if (maxOffset <= 0 || travel <= 0) return;
        var offset = Math.Clamp(thumbPosition / travel * maxOffset, 0, maxOffset);
        _gridScroll.Offset = orientation == Orientation.Horizontal
            ? new Vector(offset, _gridScroll.Offset.Y)
            : new Vector(_gridScroll.Offset.X, offset);
    }

    private void EndScrollBarDrag(Canvas track, PointerReleasedEventArgs e)
    {
        if (_scrollbarPointerId != e.Pointer.Id) return;
        e.Pointer.Capture(null);
        _scrollbarDragAxis = null;
        _scrollbarPointerId = null;
        e.Handled = true;
    }

    private void UpdateBackgroundParallax()
    {
        _backgroundParallax.X = Math.Clamp(-_gridScroll.Offset.X * 0.015 + (_gridZoom - 1) * 20, -18, 18);
        _backgroundParallax.Y = Math.Clamp(-_gridScroll.Offset.Y * 0.015 + (_gridZoom - 1) * 20, -18, 18);
        var scale = 1.035 + (_gridZoom - 1) * 0.018;
        _backgroundScale.ScaleX = scale;
        _backgroundScale.ScaleY = scale;
    }

    private async Task EnsureCellScrolledFromViewportEdgeAsync(int cellX, int cellY)
    {
        const double strideAtBaseZoom = 106;
        var step = strideAtBaseZoom * _gridZoom;
        var viewport = _gridScroll.Viewport;
        if (viewport.Width <= 0 || viewport.Height <= 0) return;

        var offset = _gridScroll.Offset;
        var nextX = offset.X;
        var nextY = offset.Y;
        var cellLeft = cellX * step;
        var cellRight = cellLeft + step;
        var cellTop = cellY * step;
        var cellBottom = cellTop + step;
        if (cellLeft < offset.X - 1) nextX = Math.Max(0, offset.X - step);
        else if (cellRight > offset.X + viewport.Width + 1) nextX = offset.X + step;
        if (cellTop < offset.Y - 1) nextY = Math.Max(0, offset.Y - step);
        else if (cellBottom > offset.Y + viewport.Height + 1) nextY = offset.Y + step;

        var target = new Vector(
            Math.Clamp(nextX, 0, Math.Max(0, _gridScroll.Extent.Width - viewport.Width)),
            Math.Clamp(nextY, 0, Math.Max(0, _gridScroll.Extent.Height - viewport.Height)));
        await AnimateGridScrollToAsync(target);
    }

    private async Task EnsureGridBoundaryVisibleAsync(bool changeColumns, int boundaryIndex)
    {
        const double strideAtBaseZoom = 106;
        var step = strideAtBaseZoom * _gridZoom;
        var viewport = _gridScroll.Viewport;
        var offset = _gridScroll.Offset;
        var axisOffset = changeColumns ? offset.X : offset.Y;
        var axisViewport = changeColumns ? viewport.Width : viewport.Height;
        if (axisViewport <= 0) return;
        var start = boundaryIndex * step;
        var end = start + step;
        var next = axisOffset;
        if (start < axisOffset) next = start;
        else if (end > axisOffset + axisViewport) next = end - axisViewport;
        var max = changeColumns ? _gridScroll.Extent.Width - viewport.Width : _gridScroll.Extent.Height - viewport.Height;
        next = Math.Clamp(next, 0, Math.Max(0, max));
        var target = changeColumns ? new Vector(next, offset.Y) : new Vector(offset.X, next);
        await AnimateGridScrollToAsync(target);
    }

    private async Task AnimateGridScrollToAsync(Vector target)
    {
        var offset = _gridScroll.Offset;
        if (target == offset) return;
        CancelScrollAnimation(finishAtTarget: false);
        var cancellation = new CancellationTokenSource();
        _scrollAnimation = cancellation;
        _scrollAnimationTarget = target;
        var start = offset;
        var timer = Stopwatch.StartNew();
        const double durationMilliseconds = 180;
        try
        {
            while (timer.Elapsed.TotalMilliseconds < durationMilliseconds)
            {
                await Task.Delay(16, cancellation.Token);
                var progress = Math.Clamp(timer.Elapsed.TotalMilliseconds / durationMilliseconds, 0, 1);
                var eased = 1 - Math.Pow(1 - progress, 3);
                _gridScroll.Offset = new Vector(start.X + (target.X - start.X) * eased, start.Y + (target.Y - start.Y) * eased);
            }
            _gridScroll.Offset = target;
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (ReferenceEquals(_scrollAnimation, cancellation))
            {
                _scrollAnimation = null;
                _scrollAnimationTarget = null;
            }
            cancellation.Dispose();
        }
    }

    private void CancelScrollAnimation(bool finishAtTarget)
    {
        if (finishAtTarget && _scrollAnimationTarget is { } target) _gridScroll.Offset = target;
        _scrollAnimationTarget = null;
        var animation = _scrollAnimation;
        _scrollAnimation = null;
        animation?.Cancel();
    }

    private void HandleGridWheel(object? sender, PointerWheelEventArgs e)
    {
        if (e.Delta.Y == 0) return;

        var oldZoom = _gridZoom;
        var pointer = e.GetPosition(_gridScroll);
        var contentPoint = new Point(
            (_gridScroll.Offset.X + pointer.X) / oldZoom,
            (_gridScroll.Offset.Y + pointer.Y) / oldZoom);
        _gridZoom = Math.Clamp(_gridZoom * (e.Delta.Y > 0 ? 1.12 : 1 / 1.12), MIN_GRID_ZOOM, 2.4);
        ApplyGridZoom();
        e.Handled = true;
        var version = ++_zoomAnchorVersion;
        _pendingZoomAnchor = (contentPoint, pointer, version);
        _gridScroll.InvalidateMeasure();
        Dispatcher.UIThread.Post(ApplyPendingZoomAnchor, DispatcherPriority.Render);
    }

    private void ApplyPendingZoomAnchor()
    {
        if (_pendingZoomAnchor is not { } anchor || anchor.Version != _zoomAnchorVersion) return;
        _pendingZoomAnchor = null;
        var maxX = Math.Max(0, _gridScroll.Extent.Width - _gridScroll.Viewport.Width);
        var maxY = Math.Max(0, _gridScroll.Extent.Height - _gridScroll.Viewport.Height);
        _gridScroll.Offset = new Vector(
            Math.Clamp(anchor.Content.X * _gridZoom - anchor.Pointer.X, 0, maxX),
            Math.Clamp(anchor.Content.Y * _gridZoom - anchor.Pointer.Y, 0, maxY));
    }

    private void ApplyGridZoom()
    {
        UpdateBackgroundParallax();
        var tileSize = 96 * _gridZoom;
        foreach (var ((x, y), visual) in _cellVisuals)
        {
            Canvas.SetLeft(visual.Frame, x * 106 * _gridZoom);
            Canvas.SetTop(visual.Frame, y * 106 * _gridZoom);
            visual.Frame.Width = tileSize;
            visual.Frame.Height = tileSize;
            visual.Frame.Margin = new Thickness(5 * _gridZoom);
            visual.Frame.BorderThickness = new Thickness(x == _selectedX && y == _selectedY ? 2 * _gridZoom : _gridZoom);
            visual.Selection.BorderThickness = new Thickness(3 * _gridZoom);
            if (visual.Remove is not null)
            {
                visual.Remove.Width = 22 * _gridZoom;
                visual.Remove.Height = 22 * _gridZoom;
                visual.Remove.FontSize = 16 * _gridZoom;
                visual.Remove.CornerRadius = new CornerRadius(11 * _gridZoom);
            }
            if (visual.Frame.Child is not Grid tile) continue;
            tile.Width = tileSize;
            tile.Height = tileSize;
            foreach (var plate in tile.Children.OfType<Border>())
            {
                if (plate.Child is not TextBlock name) continue;
                name.FontSize = 11 * _gridZoom;
                name.Margin = new Thickness(5 * _gridZoom, 3 * _gridZoom);
                plate.Padding = new Thickness(2 * _gridZoom, 1 * _gridZoom);
            }
            _gridCellsByPosition.TryGetValue((x, y), out var cell);
            UpdateCellNamePlate(tile, visual.Selection, cell);
            if (visual.Remove is not null)
                visual.Remove.IsVisible = (x, y) == (_selectedX, _selectedY) && cell is not null && _gridZoom >= GRID_DETAIL_ZOOM_THRESHOLD;
        }
        var stride = 106 * _gridZoom;
        _grid.Width = Math.Max(stride, _project.Columns * stride);
        _grid.Height = Math.Max(stride, _project.Rows * stride);
        RefreshVisibleGridCells();
        _grid.InvalidateMeasure();
        _gridScroll.InvalidateMeasure();
    }

    private void RefreshPreview()
    {
        _updatePreviewViewport?.Invoke();
        UpdatePngExportButtonState();
        _previewCrosshairPinned = false;
        _previewCrosshairOverlay.IsVisible = false;
        var current = FindCell(_selectedX, _selectedY);
        _offsetReadout.Text = $"X {FormatOffset(current?.OffsetX ?? 0)}, Y {FormatOffset(current?.OffsetY ?? 0)}";
        SetImage(_preview, CreateBitmap(RenderSelectedCellForUI(current)));
        _noImageLabel.IsVisible = current is null;
        var index = _selectedY * _project.Columns + _selectedX;
        var previousCell = index > 0 ? FindCell((index - 1) % _project.Columns, (index - 1) / _project.Columns) : null;
        var nextIndex = index + 1;
        var nextCell = nextIndex < _project.Columns * _project.Rows ? FindCell(nextIndex % _project.Columns, nextIndex / _project.Columns) : null;
        if (_overlayOpacity > 0)
        {
            var alpha = (byte)Math.Clamp(Math.Round(_overlayOpacity * 255), 0, 255);
            SetImage(_previous, CreateBitmap(RenderCellForUI(previousCell, new SKColor(30, 90, 255, alpha))));
            SetImage(_next, CreateBitmap(RenderCellForUI(nextCell, new SKColor(255, 40, 40, alpha))));
        }
        else { SetImage(_previous, null); SetImage(_next, null); }
        _previewX.Text = (_selectedX + 1).ToString(); _previewY.Text = (_selectedY + 1).ToString();
    }

    private void RefreshCurrentPreview()
    {
        _previewCrosshairPinned = false;
        _previewCrosshairOverlay.IsVisible = false;
        var current = FindCell(_selectedX, _selectedY);
        _offsetReadout.Text = $"X {FormatOffset(current?.OffsetX ?? 0)}, Y {FormatOffset(current?.OffsetY ?? 0)}";
        SetImage(_preview, CreateBitmap(RenderSelectedCellForUI(current)));
        _noImageLabel.IsVisible = current is null;
    }

    private void HandlePreviewPointerPressed(Border previewFrame, Grid previewLayers, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(previewFrame).Properties.IsLeftButtonPressed) return;
        var pointer = e.GetPosition(previewLayers);
        if (_previewCrosshairPinned)
        {
            var imageBounds = GetPreviewImageBounds(previewLayers);
            if (imageBounds is { } bounds && IsInside(bounds, pointer) &&
                TryGetCrosshairCropSelection(bounds, _pinnedCrosshairPosition, pointer, out var suggestedResize, out _))
            {
                e.Handled = true;
                _ = CommitCellSizeAsync(suggestedResize);
                return;
            }
        }

        UpdatePreviewCrosshair(previewLayers, pointer);
        if (_previewCrosshairOverlay.IsVisible)
        {
            _previewCrosshairPinned = true;
            _pinnedCrosshairPosition = pointer;
        }
    }

    private bool TryGetCrosshairCropSelection(Rect bounds, Point pinnedPosition, Point selectionPosition,
        out (ResizeAnchor Anchor, int Width, int Height) suggestedResize, out Rect keptBounds)
    {
        suggestedResize = default;
        keptBounds = default;
        if (_project.CellWidth <= 0 || _project.CellHeight <= 0 || bounds.Width <= 0 || bounds.Height <= 0) return false;

        var pinnedX = Math.Clamp((int)Math.Round((pinnedPosition.X - bounds.Left) / bounds.Width * _project.CellWidth), 0, _project.CellWidth);
        var pinnedY = Math.Clamp((int)Math.Round((pinnedPosition.Y - bounds.Top) / bounds.Height * _project.CellHeight), 0, _project.CellHeight);
        var selectionX = (selectionPosition.X - bounds.Left) / bounds.Width * _project.CellWidth;
        var selectionY = (selectionPosition.Y - bounds.Top) / bounds.Height * _project.CellHeight;
        var distanceX = Math.Abs(selectionPosition.X - pinnedPosition.X);
        var distanceY = Math.Abs(selectionPosition.Y - pinnedPosition.Y);
        const double guideHitRadius = 8;

        if (distanceX <= guideHitRadius && selectionPosition.Y < pinnedPosition.Y - guideHitRadius)
        {
            keptBounds = new Rect(bounds.Left, pinnedPosition.Y, bounds.Width, bounds.Bottom - pinnedPosition.Y);
            suggestedResize = (ResizeAnchor.Bottom, _project.CellWidth, Math.Max(1, _project.CellHeight - pinnedY));
        }
        else if (distanceX <= guideHitRadius && selectionPosition.Y > pinnedPosition.Y + guideHitRadius)
        {
            keptBounds = new Rect(bounds.Left, bounds.Top, bounds.Width, pinnedPosition.Y - bounds.Top);
            suggestedResize = (ResizeAnchor.Top, _project.CellWidth, Math.Max(1, pinnedY));
        }
        else if (distanceY <= guideHitRadius && selectionPosition.X < pinnedPosition.X - guideHitRadius)
        {
            keptBounds = new Rect(pinnedPosition.X, bounds.Top, bounds.Right - pinnedPosition.X, bounds.Height);
            suggestedResize = (ResizeAnchor.Right, Math.Max(1, _project.CellWidth - pinnedX), _project.CellHeight);
        }
        else if (distanceY <= guideHitRadius && selectionPosition.X > pinnedPosition.X + guideHitRadius)
        {
            keptBounds = new Rect(bounds.Left, bounds.Top, pinnedPosition.X - bounds.Left, bounds.Height);
            suggestedResize = (ResizeAnchor.Left, Math.Max(1, pinnedX), _project.CellHeight);
        }
        else
        {
            var isLeft = selectionX < pinnedX;
            var isTop = selectionY < pinnedY;
            if (isTop && isLeft)
            {
                keptBounds = new Rect(pinnedPosition.X, pinnedPosition.Y, bounds.Right - pinnedPosition.X, bounds.Bottom - pinnedPosition.Y);
                suggestedResize = (ResizeAnchor.BottomRight, Math.Max(1, _project.CellWidth - pinnedX), Math.Max(1, _project.CellHeight - pinnedY));
            }
            else if (isTop)
            {
                keptBounds = new Rect(bounds.Left, pinnedPosition.Y, pinnedPosition.X - bounds.Left, bounds.Bottom - pinnedPosition.Y);
                suggestedResize = (ResizeAnchor.BottomLeft, Math.Max(1, pinnedX), Math.Max(1, _project.CellHeight - pinnedY));
            }
            else if (isLeft)
            {
                keptBounds = new Rect(pinnedPosition.X, bounds.Top, bounds.Right - pinnedPosition.X, pinnedPosition.Y - bounds.Top);
                suggestedResize = (ResizeAnchor.TopRight, Math.Max(1, _project.CellWidth - pinnedX), Math.Max(1, pinnedY));
            }
            else
            {
                keptBounds = new Rect(bounds.Left, bounds.Top, pinnedPosition.X - bounds.Left, pinnedPosition.Y - bounds.Top);
                suggestedResize = (ResizeAnchor.TopLeft, Math.Max(1, pinnedX), Math.Max(1, pinnedY));
            }
        }

        return true;
    }

    private void HandlePreviewPointerMoved(Grid previewLayers, Point pointer)
    {
        if (_previewCrosshairPinned)
        {
            var imageBounds = GetPreviewImageBounds(previewLayers);
            if (imageBounds is { } bounds && IsInside(bounds, pointer))
            {
                UpdatePreviewCrosshair(previewLayers, _pinnedCrosshairPosition, pointer);
                return;
            }
            _previewCrosshairPinned = false;
        }
        UpdatePreviewCrosshair(previewLayers, pointer);
    }

    private void UpdatePreviewCrosshair(Grid previewLayers, Point pointer, Point? cropHoverPosition = null)
    {
        foreach (var cropRegion in _previewCropRegions) cropRegion.IsVisible = false;
        var current = FindCell(_selectedX, _selectedY);
        var imageBounds = GetPreviewImageBounds(previewLayers);
        if (current is null || imageBounds is not { } bounds || !IsInside(bounds, pointer))
        {
            _previewCrosshairOverlay.IsVisible = false;
            return;
        }

        var availableWidth = previewLayers.Bounds.Width;
        var availableHeight = previewLayers.Bounds.Height;
        var pixelX = Math.Clamp((int)Math.Round((pointer.X - bounds.Left) / bounds.Width * _project.CellWidth), 0, _project.CellWidth);
        var pixelY = Math.Clamp((int)Math.Round((pointer.Y - bounds.Top) / bounds.Height * _project.CellHeight), 0, _project.CellHeight);
        _previewCrosshairHorizontal.StartPoint = new Point(bounds.Left, pointer.Y);
        _previewCrosshairHorizontal.EndPoint = new Point(bounds.Right, pointer.Y);
        _previewCrosshairVertical.StartPoint = new Point(pointer.X, bounds.Top);
        _previewCrosshairVertical.EndPoint = new Point(pointer.X, bounds.Bottom);
        if (_previewCrosshairPinned && cropHoverPosition is { } hoverPosition &&
            TryGetCrosshairCropSelection(bounds, pointer, hoverPosition, out _, out var keptBounds))
        {
            var croppedBounds = new[]
            {
                new Rect(bounds.Left, bounds.Top, bounds.Width, keptBounds.Top - bounds.Top),
                new Rect(bounds.Left, keptBounds.Bottom, bounds.Width, bounds.Bottom - keptBounds.Bottom),
                new Rect(bounds.Left, keptBounds.Top, keptBounds.Left - bounds.Left, keptBounds.Height),
                new Rect(keptBounds.Right, keptBounds.Top, bounds.Right - keptBounds.Right, keptBounds.Height)
            };
            for (var index = 0; index < croppedBounds.Length; index++)
            {
                var cropBounds = croppedBounds[index];
                if (cropBounds.Width <= 0 || cropBounds.Height <= 0) continue;
                var cropRegion = _previewCropRegions[index];
                cropRegion.Width = cropBounds.Width;
                cropRegion.Height = cropBounds.Height;
                Canvas.SetLeft(cropRegion, cropBounds.Left);
                Canvas.SetTop(cropRegion, cropBounds.Top);
                cropRegion.IsVisible = true;
            }
        }
        PositionCoordinateLabel(_topCoordinateLabel, pixelY.ToString(), pointer.X, bounds.Top, availableWidth, availableHeight);
        PositionCoordinateLabel(_bottomCoordinateLabel, (_project.CellHeight - pixelY).ToString(), pointer.X, bounds.Bottom, availableWidth, availableHeight);
        PositionCoordinateLabel(_leftCoordinateLabel, pixelX.ToString(), bounds.Left, pointer.Y, availableWidth, availableHeight);
        PositionCoordinateLabel(_rightCoordinateLabel, (_project.CellWidth - pixelX).ToString(), bounds.Right, pointer.Y, availableWidth, availableHeight);
        _previewCrosshairOverlay.IsVisible = true;
    }

    private Rect? GetPreviewImageBounds(Grid previewLayers)
    {
        var availableWidth = previewLayers.Bounds.Width;
        var availableHeight = previewLayers.Bounds.Height;
        if (FindCell(_selectedX, _selectedY) is null || availableWidth <= 0 || availableHeight <= 0 || _project.CellWidth <= 0 || _project.CellHeight <= 0)
            return null;
        var aspectRatio = (double)_project.CellWidth / _project.CellHeight;
        var imageWidth = Math.Min(availableWidth, availableHeight * aspectRatio);
        var imageHeight = Math.Min(availableHeight, availableWidth / aspectRatio);
        return new Rect((availableWidth - imageWidth) / 2, (availableHeight - imageHeight) / 2, imageWidth, imageHeight);
    }

    private static bool IsInside(Rect bounds, Point point) =>
        point.X >= bounds.Left && point.X < bounds.Right && point.Y >= bounds.Top && point.Y < bounds.Bottom;

    private static TextBlock CreateCoordinateLabel() => new()
    {
        FontSize = 12, FontWeight = FontWeight.Bold, Foreground = Brushes.White, IsHitTestVisible = false
    };

    private static void PositionCoordinateLabel(TextBlock label, string text, double anchorX, double anchorY, double canvasWidth, double canvasHeight)
    {
        label.Text = text;
        label.Measure(Size.Infinity);
        var left = Math.Clamp(anchorX - label.DesiredSize.Width / 2, 0, Math.Max(0, canvasWidth - label.DesiredSize.Width));
        var top = Math.Clamp(anchorY - label.DesiredSize.Height / 2, 0, Math.Max(0, canvasHeight - label.DesiredSize.Height));
        Canvas.SetLeft(label, left);
        Canvas.SetTop(label, top);
    }

    private static string FormatOffset(int value) => value >= 0 ? $"+{value}" : value.ToString();

    private SpriteCell? FindCell(int x, int y) => _gridCellsByPosition.GetValueOrDefault((x, y));

    private SKBitmap RenderCellForUI(SpriteCell? cell, SKColor? tint = null)
    {
        if (cell is null) return SpriteImageService.RenderCell(_project, null, (SKBitmap?)null, tint);
        var source = GetSourceBitmap(cell) ?? throw new InvalidOperationException($"画像の読み込みに失敗しました: {cell.AssetPath}");
        return SpriteImageService.RenderCellThumbnail(_project, cell, source.Bitmap, 512, source.ScaleX, source.ScaleY, tint);
    }

    private SKBitmap RenderCellThumbnailForUI(SpriteCell cell, int maximumSize)
    {
        var source = GetSourceBitmap(cell) ?? throw new InvalidOperationException($"画像の読み込みに失敗しました: {cell.AssetPath}");
        return SpriteImageService.RenderCellThumbnail(_project, cell, source.Bitmap, maximumSize, source.ScaleX, source.ScaleY);
    }

    private CachedSourceBitmap? GetSourceBitmap(SpriteCell? cell)
    {
        if (cell is null) return null;
        if (!_sourceBitmaps.TryGetValue(cell.AssetPath, out var source))
        {
            var path = ProjectValidator.ResolveAssetPath(_projectDirectory, cell.AssetPath);
            var maximumDimension = GetUiSourceMaximumDimension(path, _project.CellWidth, _project.CellHeight);
            var (bitmap, scaleX, scaleY) = SpriteImageService.DecodeForPreview(path, maximumDimension);
            source = new CachedSourceBitmap(bitmap, scaleX, scaleY);
            _sourceBitmaps.Add(cell.AssetPath, source);
        }
        return source;
    }

    private void ClearSourceCache()
    {
        foreach (var bitmap in _sourceBitmaps.Values) bitmap.Bitmap.Dispose();
        _sourceBitmaps.Clear();
        if (_selectedPreviewSource is { } previewSource) previewSource.Bitmap.Dispose();
        _selectedPreviewSource = null;
        _selectedPreviewAssetPath = null;
        _selectedPreviewCellWidth = 0;
        _selectedPreviewCellHeight = 0;
    }

    private SKBitmap RenderSelectedCellForUI(SpriteCell? cell)
    {
        if (cell is null)
        {
            if (_selectedPreviewSource is { } oldPreviewSource) oldPreviewSource.Bitmap.Dispose();
            _selectedPreviewSource = null;
            _selectedPreviewAssetPath = null;
            _selectedPreviewCellWidth = 0;
            _selectedPreviewCellHeight = 0;
            return SpriteImageService.RenderCell(_project, null, (SKBitmap?)null);
        }
        if (_selectedPreviewAssetPath != cell.AssetPath || _selectedPreviewSource is null ||
            _selectedPreviewCellWidth != _project.CellWidth || _selectedPreviewCellHeight != _project.CellHeight)
        {
            if (_selectedPreviewSource is { } oldPreviewSource) oldPreviewSource.Bitmap.Dispose();
            var path = ProjectValidator.ResolveAssetPath(_projectDirectory, cell.AssetPath);
            var maximumDimension = GetUiSourceMaximumDimension(path, _project.CellWidth, _project.CellHeight,
                maximumSourcePixels: MAX_SELECTED_PREVIEW_SOURCE_PIXELS);
            var (bitmap, scaleX, scaleY) = SpriteImageService.DecodeForPreview(path, maximumDimension);
            _selectedPreviewAssetPath = cell.AssetPath;
            _selectedPreviewSource = new CachedSourceBitmap(bitmap, scaleX, scaleY);
            _selectedPreviewCellWidth = _project.CellWidth;
            _selectedPreviewCellHeight = _project.CellHeight;
        }
        var source = _selectedPreviewSource.Value;
        return SpriteImageService.RenderCellThumbnail(_project, cell, source.Bitmap, MAX_CELL_PREVIEW_DIMENSION, source.ScaleX, source.ScaleY);
    }

    private static int GetUiSourceMaximumDimension(string path, int cellWidth, int cellHeight, int? knownWidth = null, int? knownHeight = null,
        int maximumSourcePixels = MAX_UI_SOURCE_PIXELS)
    {
        var (width, height) = knownWidth is { } imageWidth && knownHeight is { } imageHeight
            ? (imageWidth, imageHeight)
            : SpriteImageService.GetPngSize(path);
        var longestSide = Math.Max(width, height);
        var baselineScale = Math.Min(1d, (double)MAX_UI_SOURCE_DIMENSION / longestSide);
        var cellPreviewScale = Math.Min(1d, (double)MAX_CELL_PREVIEW_DIMENSION / Math.Max(cellWidth, cellHeight));
        var pixelBudgetScale = Math.Min(1d, Math.Sqrt((double)maximumSourcePixels / ((long)width * height)));
        var decodeScale = Math.Min(Math.Max(baselineScale, cellPreviewScale), pixelBudgetScale);
        return Math.Clamp((int)Math.Ceiling(longestSide * decodeScale), 1, longestSide);
    }

    private static Bitmap CreateBitmap(SKBitmap bitmap)
    {
        using (bitmap)
        {
            var result = new WriteableBitmap(new PixelSize(bitmap.Width, bitmap.Height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
            using var framebuffer = result.Lock();
            var rowBytes = bitmap.Width * 4;
            var row = new byte[rowBytes];
            for (var y = 0; y < bitmap.Height; y++)
            {
                Marshal.Copy(IntPtr.Add(bitmap.GetPixels(), y * bitmap.RowBytes), row, 0, rowBytes);
                Marshal.Copy(row, 0, IntPtr.Add(framebuffer.Address, y * framebuffer.RowBytes), rowBytes);
            }
            return result;
        }
    }

    private static void SetImage(Image target, Bitmap? bitmap)
    {
        if (target.Source is IDisposable old) old.Dispose();
        target.Source = bitmap;
    }

    private void RefreshAll()
    {
        NormalizeSelection();
        if (_selectAllButton is not null)
        {
            _selectAllButton.Content = AreAllCellsSelected() ? "全解除" : "全選択";
            _selectAllButton.IsEnabled = _project.Columns != 1 || _project.Rows != 1;
        }
        RefreshGrid();
        RefreshPreview();
    }

    private void ResetSelectionToCurrentCell()
    {
        _selectedX = Math.Clamp(_selectedX, 0, _project.Columns - 1);
        _selectedY = Math.Clamp(_selectedY, 0, _project.Rows - 1);
        _selectedCells.Clear();
        _selectedCells.Add((_selectedX, _selectedY));
        _selectionAnchor = (_selectedX, _selectedY);
    }

    private void NormalizeSelection()
    {
        _selectedX = Math.Clamp(_selectedX, 0, _project.Columns - 1);
        _selectedY = Math.Clamp(_selectedY, 0, _project.Rows - 1);
        _selectedCells.RemoveWhere(position => position.X < 0 || position.X >= _project.Columns || position.Y < 0 || position.Y >= _project.Rows);
        if (_selectedCells.Count == 0) _selectedCells.Add((_selectedX, _selectedY));
        _selectionAnchor = (Math.Clamp(_selectionAnchor.X, 0, _project.Columns - 1), Math.Clamp(_selectionAnchor.Y, 0, _project.Rows - 1));
    }

    private void SyncFields()
    {
        _title.Text = _project.Title; _cellWidth.Text = _project.CellWidth.ToString(); _cellHeight.Text = _project.CellHeight.ToString();
        _cellCount.Text = $"セル数: {_project.Columns} × {_project.Rows}";
    }

    private void Navigate(int delta)
    {
        ResetOffsetBatch();
        var index = _selectedY * _project.Columns + _selectedX;
        var next = Math.Clamp(index + delta, 0, _project.Columns * _project.Rows - 1);
        UpdateSelectedCell(next % _project.Columns, next / _project.Columns);
    }

    private void NavigateByCoordinates()
    {
        if (!int.TryParse(_previewX.Text, out var x) || !int.TryParse(_previewY.Text, out var y) || x < 1 || y < 1 || x > _project.Columns || y > _project.Rows)
        { _previewX.Text = (_selectedX + 1).ToString(); _previewY.Text = (_selectedY + 1).ToString(); SetStatus("プレビュー位置のX/Yはセル一覧の範囲内で指定してください。"); return; }
        UpdateSelectedCell(x - 1, y - 1);
    }

    private void HandleKeyDown(object? sender, KeyEventArgs e)
    {
        var command = _platformServices.IsCommandModifierPressed(e.KeyModifiers);
        if (command && e.Key == Key.S) { e.Handled = true; _ = SaveProjectAsync(); return; }
        if (command && e.Key == Key.Z) { e.Handled = true; if (e.KeyModifiers.HasFlag(KeyModifiers.Shift)) Redo(); else Undo(); return; }
        if (command && e.Key == Key.Y) { e.Handled = true; Redo(); return; }
        if (e.Source is TextBox) return;
        if (command && e.Key == Key.C) { e.Handled = true; CopySelectedCells(); return; }
        if (command && e.Key == Key.V) { e.Handled = true; _ = PasteCopiedCellsAsync(); return; }
        if (command && e.Key == Key.A) { e.Handled = true; SelectAllCells(); return; }
        if (e.Key is Key.Left or Key.Right or Key.Up or Key.Down)
        {
            var dx = e.Key == Key.Left ? -1 : e.Key == Key.Right ? 1 : 0;
            var dy = e.Key == Key.Up ? -1 : e.Key == Key.Down ? 1 : 0;
            NudgeSelectedCellOffset(dx, dy, e.Key);
            e.Handled = true;
        }
        else if (e.Key == Key.Delete) { e.Handled = true; _ = DeleteSelectedCellsAsync(); }
    }

    private void NudgeSelectedCellOffset(int dx, int dy, Key? key = null)
    {
        if (dx == 0 && dy == 0) return;
        var cells = _project.Cells.Where(cell => _selectedCells.Contains((cell.X, cell.Y))).ToArray();
        if (cells.Length == 0) return;
        var now = DateTime.UtcNow;
        var offsetAction = key ?? (dx, dy) switch
        {
            (-1, 0) => Key.Left,
            (1, 0) => Key.Right,
            (0, -1) => Key.Up,
            (0, 1) => Key.Down,
            _ => null
        };
        var sameBurst = _lastOffsetKey == offsetAction && _lastOffsetCell == (_selectedX, _selectedY) && now - _lastOffsetTime < TimeSpan.FromMilliseconds(450);
        if (!sameBurst) PushUndo();
        _lastOffsetKey = offsetAction;
        _lastOffsetCell = (_selectedX, _selectedY);
        _lastOffsetTime = now;
        foreach (var cell in cells)
        {
            cell.OffsetX += dx;
            cell.OffsetY += dy;
        }
        MarkDirty();
        foreach (var cell in cells) RefreshCellVisual((cell.X, cell.Y));
        RefreshCurrentPreview();
    }

    private void SelectAllCells()
    {
        _selectedCells.Clear();
        for (var y = 0; y < _project.Rows; y++)
        for (var x = 0; x < _project.Columns; x++)
            _selectedCells.Add((x, y));
        _selectionAnchor = (0, 0);
        ResetOffsetBatch();
        UpdateSelectionVisuals();
        RefreshPreview();
        ShowSelectionStatus();
    }

    private void ToggleSelectAllCells()
    {
        if (!AreAllCellsSelected())
        {
            SelectAllCells();
            return;
        }

        _selectedCells.Clear();
        _selectedCells.Add((_selectedX, _selectedY));
        _selectionAnchor = (_selectedX, _selectedY);
        ResetOffsetBatch();
        UpdateSelectionVisuals();
        RefreshPreview();
        ShowSelectionStatus();
    }

    private bool AreAllCellsSelected() =>
        _selectedCells.Count == (long)_project.Columns * _project.Rows &&
        _selectedCells.All(position => position.X >= 0 && position.X < _project.Columns && position.Y >= 0 && position.Y < _project.Rows);

    private void ResetSelectedCellOffset()
    {
        var cell = FindCell(_selectedX, _selectedY);
        if (cell is null || (cell.OffsetX == 0 && cell.OffsetY == 0)) return;
        PushUndo();
        cell.OffsetX = 0;
        cell.OffsetY = 0;
        MarkDirty();
        RefreshCellVisual((_selectedX, _selectedY));
        RefreshCurrentPreview();
        SetStatus("座標調整をリセットしました。");
    }

    private async Task DeleteSelectedCellAsync()
    {
        if (FindCell(_selectedX, _selectedY) is not { } cell) return;
        PushUndo();
        _project.Cells.Remove(cell);
        MarkDirty();
        RefreshCellVisual((_selectedX, _selectedY), null);
        UpdateSelectionVisuals([(_selectedX, _selectedY)]);
        RefreshPreview();
        SetStatus($"セル ({_selectedX + 1}, {_selectedY + 1}) を削除しました。");
        await Task.CompletedTask;
    }

    private async Task DeleteSelectedCellsAsync()
    {
        HashSet<(int X, int Y)> positions = _selectedCells.Count > 0 ? _selectedCells.ToHashSet() : [(_selectedX, _selectedY)];
        await DeleteCellsAtPositionsAsync(positions);
    }

    private async Task DeleteCellsAtPositionsAsync(IReadOnlySet<(int X, int Y)> positions)
    {
        var removedCells = _project.Cells.Where(cell => positions.Contains((cell.X, cell.Y))).ToArray();
        if (removedCells.Length == 0) return;
        PushUndo();
        _project.Cells.RemoveAll(cell => positions.Contains((cell.X, cell.Y)));
        MarkDirty();
        foreach (var position in positions) RefreshCellVisual(position, null);
        UpdateSelectionVisuals(positions);
        RefreshPreview();
        SetStatus(removedCells.Length == 1
            ? $"セル ({removedCells[0].X + 1}, {removedCells[0].Y + 1}) を削除しました。"
            : $"選択範囲の{removedCells.Length}セルを削除しました。");
        await Task.CompletedTask;
    }

    private void Undo()
    {
        ResetOffsetBatch();
        if (_undo.Count == 0) return;
        _redo.Push(_project.DeepClone()); _project = _undo.Pop(); _projectRevision++; _dirty = true; UpdateUndoRedoButtons(); SyncFields(); RefreshAll(); UpdateTitleBar(); SetStatus("操作を元に戻しました");
    }

    private void Redo()
    {
        ResetOffsetBatch();
        if (_redo.Count == 0) return;
        _undo.Push(_project.DeepClone()); _project = _redo.Pop(); _projectRevision++; _dirty = true; UpdateUndoRedoButtons(); SyncFields(); RefreshAll(); UpdateTitleBar(); SetStatus("操作をやり直しました");
    }

    private void PushUndo() { ResetOffsetBatch(); _undo.Push(_project.DeepClone()); _redo.Clear(); UpdateUndoRedoButtons(); }
    private void UpdateUndoRedoButtons()
    {
        if (_undoButton is not null) _undoButton.IsEnabled = _undo.Count > 0;
        if (_redoButton is not null) _redoButton.IsEnabled = _redo.Count > 0;
    }

    private void RetargetUndoHistoryForFolderRename(string previousFolderName, string nextFolderName)
    {
        var previousPrefix = $"{ProjectValidator.ImportRootDirectoryName}/{previousFolderName}/";
        var nextPrefix = $"{ProjectValidator.ImportRootDirectoryName}/{nextFolderName}/";
        foreach (var historyProject in _undo.Concat(_redo))
        {
            if (historyProject.ImportFolderName != previousFolderName) continue;
            historyProject.ImportFolderName = nextFolderName;
            var remap = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var asset in historyProject.Assets)
            {
                if (!asset.RelativePath.StartsWith(previousPrefix, StringComparison.Ordinal)) continue;
                var nextPath = nextPrefix + asset.RelativePath[previousPrefix.Length..];
                remap[asset.RelativePath] = nextPath;
                asset.RelativePath = nextPath;
            }
            foreach (var cell in historyProject.Cells)
                if (remap.TryGetValue(cell.AssetPath, out var nextPath)) cell.AssetPath = nextPath;
        }
    }
    private void ResetOffsetBatch() { _lastOffsetKey = null; }
    private void MarkDirty() { _projectRevision++; _dirty = true; UpdateTitleBar(); }
    private string GetApplicationDirectory() => _platformServices.WorkspaceDirectory;

    private void UpdateTitleBar()
    {
        TitleChanged?.Invoke($"{(_dirty ? "● " : string.Empty)}{_project.Title} — {ProductName}");
    }

    private static Bitmap LoadBrandLogo()
    {
        using var stream = typeof(EditorView).Assembly.GetManifestResourceStream("SAMERIDER.Editor.SAMERIDER.png");
        return new Bitmap(stream ?? throw new InvalidOperationException("ロゴ画像を読み込めませんでした。"));
    }

    private static Bitmap LoadBackground()
    {
        using var stream = typeof(EditorView).Assembly.GetManifestResourceStream("SAMERIDER.Editor.Background.png");
        return new Bitmap(stream ?? throw new InvalidOperationException("背景画像を読み込めませんでした。"));
    }

    private void SetStatus(string text)
    {
        _status.Text = text;
        _statusPanel.IsVisible = true;
        if (!_pngExportInProgress && !_imageImportInProgress) HideProgressStatus();
    }

    private void ShowSelectionStatus() => SetStatus($"（{_selectedCells.Count}）セルを選択中");

    private void SetProgressStatus(string text, bool isIndeterminate, double progress = 0)
    {
        _status.Text = text;
        _statusPanel.IsVisible = true;
        _statusProgress.IsIndeterminate = isIndeterminate;
        _statusProgress.Value = Math.Clamp(progress, _statusProgress.Minimum, _statusProgress.Maximum);
        _statusProgress.IsVisible = true;
    }

    private void HideProgressStatus()
    {
        _statusProgress.IsIndeterminate = false;
        _statusProgress.IsVisible = false;
        _statusProgress.Value = 0;
    }

    private SpriteProject CopyAssetsIntoImportFolder(SpriteProject sourceProject, string sourceDirectory, string destinationDirectory,
        bool renameExistingImportFolder, string? targetTitle = null, string? targetImportFolderName = null)
    {
        var copy = sourceProject.DeepClone();
        var originalFolderName = copy.ImportFolderName;
        copy.Title = targetTitle ?? copy.Title;
        copy.ImportFolderName = targetImportFolderName ?? copy.ImportFolderName;
        var comparison = _platformServices.PathComparison;
        var sameDirectory = string.Equals(Path.GetFullPath(destinationDirectory), Path.GetFullPath(sourceDirectory), comparison);
        var originalFolder = Path.Combine(sourceDirectory, ProjectValidator.ImportRootDirectoryName, originalFolderName);
        var assetsDirectory = Path.Combine(destinationDirectory, ProjectValidator.ImportRootDirectoryName, copy.ImportFolderName);
        var folderMoved = false;
        if (renameExistingImportFolder && sameDirectory && !string.Equals(originalFolderName, copy.ImportFolderName, StringComparison.Ordinal) &&
            Directory.Exists(originalFolder) && !Directory.Exists(assetsDirectory))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(assetsDirectory)!);
            Directory.Move(originalFolder, assetsDirectory);
            folderMoved = true;
        }
        if (copy.Assets.Count > 0) Directory.CreateDirectory(assetsDirectory);
        var remap = new Dictionary<string, string>(StringComparer.Ordinal);
        var assignedPaths = new HashSet<string>(StringComparer.Ordinal);
        foreach (var asset in copy.Assets)
        {
            var sourceRelativePath = asset.RelativePath;
            var oldPrefix = $"{ProjectValidator.ImportRootDirectoryName}/{originalFolderName}/";
            if (folderMoved && sourceRelativePath.StartsWith(oldPrefix, StringComparison.Ordinal))
                sourceRelativePath = $"{ProjectValidator.ImportRootDirectoryName}/{copy.ImportFolderName}/" + sourceRelativePath[oldPrefix.Length..];
            var source = ProjectValidator.ResolveAssetPath(sourceDirectory, sourceRelativePath);
            var name = Path.GetFileName(asset.RelativePath);
            var candidate = Path.Combine(assetsDirectory, name);
            var samePath = string.Equals(Path.GetFullPath(source), Path.GetFullPath(candidate), comparison);
            var matchingExistingFile = File.Exists(candidate) && !samePath && !assignedPaths.Contains(candidate) && FilesHaveSameContent(source, candidate);
            if (File.Exists(candidate) && !samePath && !matchingExistingFile)
            {
                var stem = Path.GetFileNameWithoutExtension(name); var ext = Path.GetExtension(name); var suffix = 2;
                do
                {
                    name = $"{stem}_{suffix++}{ext}";
                    candidate = Path.Combine(assetsDirectory, name);
                    matchingExistingFile = File.Exists(candidate) && !assignedPaths.Contains(candidate) && FilesHaveSameContent(source, candidate);
                }
                while ((File.Exists(candidate) && !matchingExistingFile) || assignedPaths.Contains(candidate));
            }
            if (!File.Exists(candidate) && !samePath) File.Copy(source, candidate);
            var relativePath = $"{ProjectValidator.ImportRootDirectoryName}/{copy.ImportFolderName}/{name}";
            assignedPaths.Add(candidate);
            remap[asset.RelativePath] = relativePath;
            asset.RelativePath = relativePath;
        }
        foreach (var cell in copy.Cells) cell.AssetPath = remap[cell.AssetPath];
        ProjectValidator.Validate(copy, destinationDirectory);
        return copy;
    }

    private static bool FilesHaveSameContent(string firstPath, string secondPath)
    {
        using var first = File.OpenRead(firstPath);
        using var second = File.OpenRead(secondPath);
        if (first.Length != second.Length) return false;
        var firstBuffer = new byte[81920];
        var secondBuffer = new byte[firstBuffer.Length];
        while (true)
        {
            var firstRead = ReadBlock(first, firstBuffer);
            var secondRead = ReadBlock(second, secondBuffer);
            if (firstRead != secondRead || !firstBuffer.AsSpan(0, firstRead).SequenceEqual(secondBuffer.AsSpan(0, secondRead))) return false;
            if (firstRead == 0) return true;
        }
    }

    private static int ReadBlock(Stream stream, byte[] buffer)
    {
        var totalRead = 0;
        while (totalRead < buffer.Length)
        {
            var read = stream.Read(buffer, totalRead, buffer.Length - totalRead);
            if (read == 0) break;
            totalRead += read;
        }
        return totalRead;
    }

    private readonly record struct CachedSourceBitmap(SKBitmap Bitmap, float ScaleX, float ScaleY);

    private static SpriteProject CreateNewProject() => new() { Title = "SAMERIDER", CellWidth = 64, CellHeight = 64, Columns = 1, Rows = 1 };
}
