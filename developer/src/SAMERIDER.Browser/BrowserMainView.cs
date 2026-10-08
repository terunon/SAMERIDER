using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using SkiaSharp;
using SAMERIDER.Core.Models;
using SAMERIDER.Core.Services;

namespace SAMERIDER.Browser;

internal sealed class BrowserMainView : UserControl
{
    private const int MaximumPngBytes = 128 * 1024 * 1024;
    private const long MaximumLegacyProjectAssetBytes = 512L * 1024 * 1024;
    private readonly SpriteProject _project = new() { Title = "SAMERIDER", CellWidth = 64, CellHeight = 64, Columns = 1, Rows = 1 };
    private readonly Dictionary<string, byte[]> _assetBytes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SKBitmap> _sourceImages = new(StringComparer.Ordinal);
    private readonly TextBox _cellWidth = new() { Width = 72, Text = "64" };
    private readonly TextBox _cellHeight = new() { Width = 72, Text = "64" };
    private readonly Button _splitButton = new() { Content = "分割", IsEnabled = false };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _selectedCellLabel = new() { FontSize = 16, FontWeight = FontWeight.SemiBold };
    private readonly Image _preview = new() { Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly Image _backgroundImage = new() { Stretch = Stretch.UniformToFill, Opacity = 0.04, IsHitTestVisible = false };
    private readonly Image _brandLogo = new() { Width = 112, Height = 112, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Right };
    private readonly WrapPanel _cells = new();
    private readonly ScrollViewer _cellScroll = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private (int X, int Y) _selectedPosition;
    private string? _selectedAssetPath;
    private bool _canSplitCurrentImage;

    public BrowserMainView()
    {
        Focusable = true;
        Background = Brush.Parse("#0D0F12");
        Foreground = Brush.Parse("#F3F4F6");
        FontFamily = new FontFamily("avares://SAMERIDER.Browser/Assets/Fonts#IPAGothic");
        _backgroundImage.Source = LoadAssetBitmap("avares://SAMERIDER.Browser/Assets/Background.png");
        _brandLogo.Source = LoadAssetBitmap("avares://SAMERIDER.Browser/Assets/SAMERIDER.png");
        Content = BuildLayout();
        AddHandler(KeyDownEvent, HandleKeyDown, RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    private Control BuildLayout()
    {
        var surface = new Grid { Background = Brush.Parse("#0D0F12") };
        surface.Children.Add(_backgroundImage);
        var root = new Grid { RowDefinitions = RowDefinitions.Parse("Auto,*,Auto"), Margin = new Avalonia.Thickness(20), RowSpacing = 12, Background = Brushes.Transparent };
        surface.Children.Add(root);
        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Spacing = 8 };
        toolbar.Children.Add(new TextBlock { Text = "SAMERIDER v1.06", FontSize = 20, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Avalonia.Thickness(0, 0, 14, 0) });
        var import = new Button { Content = "PNGを開く" };
        import.Click += async (_, _) => await PickPngAsync();
        toolbar.Children.Add(import);
        var openProject = new Button { Content = "プロジェクトを開く" };
        openProject.Click += async (_, _) => await OpenProjectAsync();
        toolbar.Children.Add(openProject);
        toolbar.Children.Add(new TextBlock { Text = "セルサイズ", VerticalAlignment = VerticalAlignment.Center, Margin = new Avalonia.Thickness(12, 0, 0, 0) });
        toolbar.Children.Add(_cellWidth);
        toolbar.Children.Add(new TextBlock { Text = "×", VerticalAlignment = VerticalAlignment.Center });
        toolbar.Children.Add(_cellHeight);
        _splitButton.Click += (_, _) => SplitCurrentImage();
        toolbar.Children.Add(_splitButton);
        var export = new Button { Content = "PNGを書き出す" };
        export.Click += async (_, _) => await ExportPngAsync();
        toolbar.Children.Add(export);
        var saveProject = new Button { Content = "プロジェクトを保存" };
        saveProject.Click += async (_, _) => await SaveProjectAsync();
        toolbar.Children.Add(saveProject);
        Grid.SetRow(toolbar, 0);
        root.Children.Add(toolbar);

        var body = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("2*,3*"), ColumnSpacing = 14 };
        _cellScroll.Content = _cells;
        var cellPanel = new Border
        {
            BorderBrush = Brush.Parse("#343A44"), BorderThickness = new Avalonia.Thickness(1), CornerRadius = new Avalonia.CornerRadius(8),
            Padding = new Avalonia.Thickness(12), Child = _cellScroll
        };
        Grid.SetColumn(cellPanel, 0);
        body.Children.Add(cellPanel);
        var previewPanel = new Grid { RowDefinitions = RowDefinitions.Parse("Auto,*,Auto"), RowSpacing = 8 };
        previewPanel.Children.Add(_selectedCellLabel);
        var imageFrame = new Border
        {
            BorderBrush = Brush.Parse("#343A44"), BorderThickness = new Avalonia.Thickness(1), CornerRadius = new Avalonia.CornerRadius(8),
            Background = Brush.Parse("#171A20"), Padding = new Avalonia.Thickness(16), Child = _preview
        };
        Grid.SetRow(imageFrame, 1);
        previewPanel.Children.Add(imageFrame);
        var brand = new StackPanel { Spacing = 2, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Avalonia.Thickness(0, 4, 4, 0) };
        brand.Children.Add(_brandLogo);
        brand.Children.Add(new TextBlock { Text = "SAMERIDER v1.06", FontSize = 13, FontWeight = FontWeight.SemiBold, HorizontalAlignment = HorizontalAlignment.Right });
        brand.Children.Add(new TextBlock { Text = "Same-sized Raster Image Divider, Editor and Recomposer", FontSize = 10, Foreground = Brush.Parse("#A1A8B3"), HorizontalAlignment = HorizontalAlignment.Right });
        Grid.SetRow(brand, 2);
        previewPanel.Children.Add(brand);
        Grid.SetColumn(previewPanel, 1);
        body.Children.Add(previewPanel);
        Grid.SetRow(body, 1);
        root.Children.Add(body);

        var footer = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("*,Auto") };
        footer.Children.Add(_status);
        var instructions = new TextBlock { Text = "セル選択後、方向キーで表示領域を調整", Foreground = Brush.Parse("#A1A8B3") };
        Grid.SetColumn(instructions, 1);
        footer.Children.Add(instructions);
        Grid.SetRow(footer, 2);
        root.Children.Add(footer);
        return surface;
    }

    private static Bitmap LoadAssetBitmap(string uri)
    {
        using var stream = AssetLoader.Open(new Uri(uri));
        return new Bitmap(stream);
    }

    private async Task PickPngAsync()
    {
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage is null) return;
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "PNG画像を開く", AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("PNG画像") { Patterns = ["*.png"] }]
        });
        var file = files.FirstOrDefault();
        if (file is null) return;

        try
        {
            await using var input = await file.OpenReadAsync();
            using var buffer = new MemoryStream();
            await CopyWithLimitAsync(input, buffer, MaximumPngBytes);
            var bytes = buffer.ToArray();
            var (width, height) = SpriteImageService.GetPngSize(bytes);
            ValidateImageSize(width, height, file.Name);

            var bitmap = SKBitmap.Decode(bytes) ?? throw new InvalidDataException("PNG画像をデコードできませんでした。");
            ClearImageData();
            var fileName = Path.GetFileNameWithoutExtension(file.Name);
            _selectedAssetPath = $"{ProjectValidator.ImportRootDirectoryName}/{_project.ImportFolderName}/{fileName}_{Guid.NewGuid():N}.png";
            _assetBytes.Add(_selectedAssetPath, bytes);
            _sourceImages.Add(_selectedAssetPath, bitmap);
            _canSplitCurrentImage = true;
            _splitButton.IsEnabled = true;
            _project.Assets.Clear();
            _project.Cells.Clear();
            _project.Columns = 1;
            _project.Rows = 1;
            _selectedPosition = (0, 0);
            _status.Text = $"画像を開きました: {file.Name} ({width} × {height} px)。セルサイズを指定して分割してください。";
            RefreshCells();
            RefreshPreview();
        }
        catch (Exception ex)
        {
            _status.Text = $"画像を開けませんでした: {ex.Message}";
        }
    }

    private void SplitCurrentImage()
    {
        if (!_canSplitCurrentImage || _selectedAssetPath is null || !_assetBytes.TryGetValue(_selectedAssetPath, out var bytes))
        {
            _status.Text = "先にPNG画像を開いてください。";
            return;
        }
        if (!int.TryParse(_cellWidth.Text, out var cellWidth) || !int.TryParse(_cellHeight.Text, out var cellHeight) || cellWidth <= 0 || cellHeight <= 0)
        {
            _status.Text = "セルサイズには正の整数を指定してください。";
            return;
        }

        try
        {
            var (imageWidth, imageHeight) = SpriteImageService.GetPngSize(bytes);
            var columns = imageWidth / cellWidth;
            var rows = imageHeight / cellHeight;
            if (columns <= 0 || rows <= 0 || (long)columns * rows > ProjectValidator.MaximumGridCellCount)
                throw new InvalidDataException("セルサイズが画像より大きいか、セル数上限を超えています。");
            var nextProject = _project.DeepClone();
            nextProject.CellWidth = cellWidth;
            nextProject.CellHeight = cellHeight;
            nextProject.Columns = columns;
            nextProject.Rows = rows;
            nextProject.Assets.Clear();
            nextProject.Cells.Clear();
            var imported = SpriteImageService.SlicePng(bytes, Path.GetFileNameWithoutExtension(_selectedAssetPath) + ".png",
                nextProject, 0, 0, trimRightAndBottom: true);
            nextProject.Assets.Add(new SpriteAsset { RelativePath = imported.AssetPath, Width = imported.Width, Height = imported.Height });
            nextProject.Cells.AddRange(imported.Cells);
            ProjectValidator.Validate(nextProject);
            CopyProject(nextProject, _project);
            var sourceImage = _sourceImages[_selectedAssetPath];
            _sourceImages.Remove(_selectedAssetPath);
            _assetBytes.Remove(_selectedAssetPath);
            _selectedAssetPath = imported.AssetPath;
            _sourceImages.Add(_selectedAssetPath, sourceImage);
            _assetBytes.Add(_selectedAssetPath, bytes);
            _selectedPosition = (0, 0);
            _status.Text = $"{columns} × {rows} セルに分割しました。右端 {imageWidth - columns * cellWidth}px・下端 {imageHeight - rows * cellHeight}px は切り捨てています。";
            RefreshCells();
            RefreshPreview();
        }
        catch (Exception ex)
        {
            _status.Text = $"分割できませんでした: {ex.Message}";
        }
    }

    private async Task ExportPngAsync()
    {
        if (_project.Cells.Count == 0)
        {
            _status.Text = "書き出す画像がありません。";
            return;
        }
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage is null) return;
        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "PNGを書き出す", SuggestedFileName = _project.Title + ".png", DefaultExtension = "png",
            FileTypeChoices = [new FilePickerFileType("PNG画像") { Patterns = ["*.png"] }]
        });
        if (file is null) return;

        try
        {
            using var bitmap = SpriteImageService.Export(_project, _sourceImages);
            using var png = SpriteImageService.EncodePng(bitmap);
            await using var output = await file.OpenWriteAsync();
            await output.WriteAsync(png.ToArray());
            _status.Text = $"PNGを書き出しました: {bitmap.Width} × {bitmap.Height}px";
        }
        catch (Exception ex)
        {
            _status.Text = $"PNGを書き出せませんでした: {ex.Message}";
        }
    }

    private async Task OpenProjectAsync()
    {
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage is null) return;
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "SAMERIDERプロジェクトを開く",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("SAMERIDERプロジェクト") { Patterns = ["*.samerider", "*.json"] }
            ]
        });
        if (files.Count == 0) return;

        try
        {
            if (files.Count == 1 && IsBundleExtension(Path.GetExtension(files[0].Name)))
            {
                await using var input = await files[0].OpenReadAsync();
                using var buffer = new MemoryStream();
                await CopyWithLimitAsync(input, buffer, (int)ProjectBundleService.MaximumBundleBytes);
                var bundle = ProjectBundleService.OpenBundle(buffer.ToArray());
                LoadProject(bundle.Project, bundle.Assets);
                _status.Text = $"プロジェクトを開きました: {bundle.Project.Title}";
                return;
            }

            var projectFile = files.FirstOrDefault(file => Path.GetExtension(file.Name).Equals(".json", StringComparison.OrdinalIgnoreCase));
            if (projectFile is null) throw new InvalidDataException(".sameriderまたは旧形式の.jsonを選択してください。");
            await using var projectStream = await projectFile.OpenReadAsync();
            using var projectBuffer = new MemoryStream();
            await CopyWithLimitAsync(projectStream, projectBuffer, 16 * 1024 * 1024);
            var project = ProjectStore.Deserialize(projectBuffer.ToArray());

            var assetBytes = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            long totalAssetBytes = 0;
            foreach (var asset in project.Assets)
            {
                var fileName = Path.GetFileName(asset.RelativePath);
                var imageFiles = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = $"参照画像を選択: {asset.RelativePath}",
                    AllowMultiple = false,
                    FileTypeFilter = [new FilePickerFileType("PNG画像") { Patterns = ["*.png"] }]
                });
                var imageFile = imageFiles.FirstOrDefault();
                if (imageFile is null)
                {
                    _status.Text = $"画像の割当をキャンセルしました: {asset.RelativePath}";
                    return;
                }
                if (!Path.GetFileName(imageFile.Name).Equals(fileName, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"「{asset.RelativePath}」には「{fileName}」を選択してください（選択されたファイル: {imageFile.Name}）。");

                var basicProperties = await imageFile.GetBasicPropertiesAsync();
                if (basicProperties.Size is { } fileSize)
                {
                    if (fileSize > MaximumPngBytes)
                        throw new InvalidDataException($"画像ファイルが大きすぎます（個別上限128MiB）: {imageFile.Name}");
                    if (fileSize > (ulong)(MaximumLegacyProjectAssetBytes - totalAssetBytes))
                        throw new InvalidDataException("旧形式プロジェクトの画像合計サイズが512MiBを超えています。");
                }
                await using var imageStream = await imageFile.OpenReadAsync();
                using var imageBuffer = new MemoryStream();
                var remainingBytes = MaximumLegacyProjectAssetBytes - totalAssetBytes;
                var maximumBytesForAsset = (int)Math.Min(MaximumPngBytes, remainingBytes);
                await CopyWithLimitAsync(imageStream, imageBuffer, maximumBytesForAsset,
                    "旧形式プロジェクト画像の個別128MiBまたは合計512MiBの上限を超えています。");
                var bytes = imageBuffer.ToArray();
                totalAssetBytes += bytes.LongLength;
                if (totalAssetBytes > MaximumLegacyProjectAssetBytes)
                    throw new InvalidDataException("旧形式プロジェクトの画像合計サイズが512MiBを超えています。");
                var (width, height) = SpriteImageService.GetPngSize(bytes);
                ValidateImageSize(width, height, imageFile.Name);
                assetBytes.Add(asset.RelativePath, bytes);
            }
            LoadProject(project, assetBytes);
            _status.Text = $"旧形式プロジェクトを開きました: {project.Title}";
        }
        catch (Exception ex)
        {
            _status.Text = $"プロジェクトを開けませんでした: {ex.Message}";
        }
    }

    private async Task SaveProjectAsync()
    {
        if (_project.Cells.Count == 0 || _project.Assets.Count == 0)
        {
            _status.Text = "保存するプロジェクト画像がありません。PNGを開いて分割してください。";
            return;
        }
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage is null) return;
        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "SAMERIDERプロジェクトを保存",
            SuggestedFileName = _project.Title + ".samerider",
            DefaultExtension = "samerider",
            FileTypeChoices = [new FilePickerFileType("SAMERIDERプロジェクトbundle") { Patterns = ["*.samerider"] }]
        });
        if (file is null) return;

        try
        {
            var archive = ProjectBundleService.CreateBundle(_project, _assetBytes);
            await using var output = await file.OpenWriteAsync();
            await output.WriteAsync(archive);
            _status.Text = $"プロジェクトを保存しました: {file.Name}";
        }
        catch (Exception ex)
        {
            _status.Text = $"プロジェクトを保存できませんでした: {ex.Message}";
        }
    }

    private static bool IsBundleExtension(string extension) =>
        extension.Equals(".samerider", StringComparison.OrdinalIgnoreCase);

    private void LoadProject(SpriteProject project, IReadOnlyDictionary<string, byte[]> assetBytes)
    {
        ProjectValidator.Validate(project);
        var decodedImages = new Dictionary<string, SKBitmap>(StringComparer.Ordinal);
        long totalImagePixels = 0;
        try
        {
            foreach (var asset in project.Assets)
            {
                if (!assetBytes.TryGetValue(asset.RelativePath, out var bytes))
                    throw new InvalidDataException($"プロジェクト画像が見つかりません: {asset.RelativePath}");
                if (bytes.Length > MaximumPngBytes)
                    throw new InvalidDataException($"画像ファイルが大きすぎます: {asset.RelativePath}");
                var (width, height) = SpriteImageService.GetPngSize(bytes);
                ValidateImageSize(width, height, asset.RelativePath);
                totalImagePixels += (long)width * height;
                if (totalImagePixels > ProjectValidator.MaximumSheetPixelCount)
                    throw new InvalidDataException("ブラウザ版で開ける画像の合計画素数は6,400万画素までです。");
                if (width != asset.Width || height != asset.Height)
                    throw new InvalidDataException($"記録された画像サイズと実際のサイズが一致しません: {asset.RelativePath}");
                decodedImages.Add(asset.RelativePath, SKBitmap.Decode(bytes) ?? throw new InvalidDataException($"画像をデコードできません: {asset.RelativePath}"));
            }
        }
        catch
        {
            foreach (var bitmap in decodedImages.Values) bitmap.Dispose();
            throw;
        }

        ClearImageData();
        CopyProject(project, _project);
        foreach (var asset in _project.Assets) _assetBytes.Add(asset.RelativePath, assetBytes[asset.RelativePath]);
        foreach (var (path, bitmap) in decodedImages) _sourceImages.Add(path, bitmap);
        _selectedAssetPath = _project.Cells.FirstOrDefault()?.AssetPath ?? _project.Assets.FirstOrDefault()?.RelativePath;
        _canSplitCurrentImage = false;
        _splitButton.IsEnabled = false;
        var selectedCell = _project.Cells.FirstOrDefault();
        _selectedPosition = selectedCell is null ? (0, 0) : (selectedCell.X, selectedCell.Y);
        _cellWidth.Text = _project.CellWidth.ToString();
        _cellHeight.Text = _project.CellHeight.ToString();
        RefreshCells();
        RefreshPreview();
    }

    private void RefreshCells()
    {
        _cells.Children.Clear();
        var cellsByPosition = _project.Cells.ToDictionary(cell => (cell.X, cell.Y));
        for (var y = 0; y < _project.Rows; y++)
        for (var x = 0; x < _project.Columns; x++)
        {
            var position = (X: x, Y: y);
            var exists = cellsByPosition.TryGetValue(position, out var cell);
            var button = new Button
            {
                Width = 104, Height = 64, Margin = new Avalonia.Thickness(3), Padding = new Avalonia.Thickness(4),
                Content = exists ? cell!.DisplayName : $"({x + 1}, {y + 1})",
                Background = _selectedPosition == position ? Brush.Parse("#765B20") : Brush.Parse("#252A32"),
                BorderBrush = _selectedPosition == position ? Brush.Parse("#FDE68A") : Brush.Parse("#444B57"),
                BorderThickness = new Avalonia.Thickness(1), Opacity = exists ? 1 : 0.55
            };
            button.Click += (_, _) =>
            {
                _selectedPosition = position;
                Focus();
                RefreshCells();
                RefreshPreview();
            };
            _cells.Children.Add(button);
        }
    }

    private void RefreshPreview()
    {
        if (_preview.Source is IDisposable previous) previous.Dispose();
        var cell = _project.Cells.FirstOrDefault(candidate => candidate.X == _selectedPosition.X && candidate.Y == _selectedPosition.Y);
        if (cell is null || !_sourceImages.TryGetValue(cell.AssetPath, out var source))
        {
            _preview.Source = null;
            _selectedCellLabel.Text = $"セル ({_selectedPosition.X + 1}, {_selectedPosition.Y + 1}) — 画像なし";
            return;
        }

        using var rendered = SpriteImageService.RenderCell(_project, cell, source);
        using var png = SpriteImageService.EncodePng(rendered);
        using var stream = new MemoryStream(png.ToArray());
        _preview.Source = new Bitmap(stream);
        _selectedCellLabel.Text = $"セル ({cell.X + 1}, {cell.Y + 1}) — {cell.DisplayName}  ({cell.OffsetX:+#;-#;0}, {cell.OffsetY:+#;-#;0}) px";
    }

    private void HandleKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Source is TextBox) return;
        var cell = _project.Cells.FirstOrDefault(candidate => candidate.X == _selectedPosition.X && candidate.Y == _selectedPosition.Y);
        if (cell is null) return;
        var (dx, dy) = e.Key switch
        {
            Key.Left => (-1, 0),
            Key.Right => (1, 0),
            Key.Up => (0, -1),
            Key.Down => (0, 1),
            _ => (0, 0)
        };
        if (dx == 0 && dy == 0) return;
        cell.OffsetX += dx;
        cell.OffsetY += dy;
        e.Handled = true;
        RefreshPreview();
    }

    private void ClearImageData()
    {
        foreach (var image in _sourceImages.Values) image.Dispose();
        _sourceImages.Clear();
        _assetBytes.Clear();
        _canSplitCurrentImage = false;
        _splitButton.IsEnabled = false;
    }

    private static async Task CopyWithLimitAsync(Stream source, Stream destination, int maximumBytes, string? limitMessage = null)
    {
        var buffer = new byte[81920];
        var totalBytes = 0;
        int read;
        while ((read = await source.ReadAsync(buffer)) > 0)
        {
            totalBytes = checked(totalBytes + read);
            if (totalBytes > maximumBytes)
                throw new InvalidDataException(limitMessage ?? "ファイルが大きすぎます。読み込み上限を超えました。");
            await destination.WriteAsync(buffer.AsMemory(0, read));
        }
    }

    private static void ValidateImageSize(int width, int height, string fileName)
    {
        if (width <= 0 || height <= 0 || width > 16_384 || height > 16_384 ||
            (long)width * height > ProjectValidator.MaximumSheetPixelCount)
            throw new InvalidDataException($"画像が大きすぎます: {fileName}。16,384px以下かつ6,400万画素以下のPNGを選択してください。");
    }

    private static void CopyProject(SpriteProject source, SpriteProject destination)
    {
        destination.Title = source.Title;
        destination.ImportFolderName = source.ImportFolderName;
        destination.CellWidth = source.CellWidth;
        destination.CellHeight = source.CellHeight;
        destination.Columns = source.Columns;
        destination.Rows = source.Rows;
        destination.Assets = source.Assets;
        destination.Cells = source.Cells;
    }
}
