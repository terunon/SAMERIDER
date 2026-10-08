using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using SkiaSharp;
using SAMERIDER.Core.Models;
using SAMERIDER.Core.Services;

namespace SAMERIDER.Editor;

internal static class Dialogs
{
    public static async Task<(int ColorCount, bool SelectedOnly)?> PickPngExportSettingsAsync(
        Control owner,
        SKBitmap source,
        int cellWidth,
        int cellHeight,
        IReadOnlyCollection<(int X, int Y)> selectedPositions,
        bool initialSelectedOnly)
    {
        int[] colorOptions = [4, 8, 16, 32, 64, 128, 256, 0];
        var window = new EditorDialogWindow
        {
            Title = "PNG書き出し設定",
            Width = 720,
            Height = 620,
            MinWidth = 560,
            MinHeight = 500,
            CanResize = true,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        var qualitySlider = new Slider
        {
            Minimum = 0,
            Maximum = colorOptions.Length - 1,
            Value = colorOptions.Length - 1,
            TickFrequency = 1,
            IsSnapToTickEnabled = true,
            Margin = new Thickness(14, 8)
        };
        var qualityLabel = new TextBlock { HorizontalAlignment = HorizontalAlignment.Center, FontWeight = FontWeight.SemiBold };
        var selectedOnlyCheckBox = new CheckBox
        {
            Content = "選択中セルだけを書き出す",
            IsChecked = initialSelectedOnly,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 2)
        };
        var previewImage = new Image { Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch };
        var previewFrame = new Border
        {
            BorderBrush = Brush.Parse("#606A78"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Background = Brush.Parse("#20242B"),
            Padding = new Thickness(8),
            Child = previewImage
        };
        var fileSizeLabel = new TextBlock { Text = "PNGサイズ: 計算中…", HorizontalAlignment = HorizontalAlignment.Center };
        var previewStatus = new TextBlock { Text = "プレビューを生成しています…", Foreground = Brush.Parse("#A1A8B3"), HorizontalAlignment = HorizontalAlignment.Center };
        var ok = CenteredButton("OK", 100, isDefault: true);
        var cancel = CenteredButton("キャンセル", 110, isCancel: true);
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Spacing = 8,
            Margin = new Thickness(12)
        };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);

        var content = new Grid
        {
            RowDefinitions = RowDefinitions.Parse("Auto,Auto,*,Auto,Auto,Auto,Auto"),
            Margin = new Thickness(18),
            RowSpacing = 8
        };
        var header = new StackPanel { Spacing = 4, HorizontalAlignment = HorizontalAlignment.Center };
        header.Children.Add(new TextBlock
        {
            Text = "書き出し時の色数を指定してください。",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        });
        header.Children.Add(selectedOnlyCheckBox);
        content.Children.Add(header);
        Grid.SetRow(qualityLabel, 1);
        content.Children.Add(qualityLabel);
        Grid.SetRow(previewFrame, 2);
        content.Children.Add(previewFrame);
        Grid.SetRow(qualitySlider, 3);
        content.Children.Add(qualitySlider);
        var optionLabels = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("*,*,*,*,*,*,*,*") };
        for (var index = 0; index < colorOptions.Length; index++)
        {
            var label = new TextBlock
            {
                Text = colorOptions[index] == 0 ? "フルカラー" : colorOptions[index].ToString(),
                FontSize = 11,
                Foreground = Brush.Parse("#A1A8B3"),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            Grid.SetColumn(label, index);
            optionLabels.Children.Add(label);
        }
        Grid.SetRow(optionLabels, 4);
        content.Children.Add(optionLabels);
        var info = new StackPanel { Spacing = 4, Margin = new Thickness(0, 4) };
        info.Children.Add(fileSizeLabel);
        info.Children.Add(previewStatus);
        Grid.SetRow(info, 5);
        content.Children.Add(info);
        Grid.SetRow(buttons, 6);
        content.Children.Add(buttons);
        window.Content = content;

        (int ColorCount, bool SelectedOnly)? selectedSettings = null;
        ok.Click += (_, _) =>
        {
            selectedSettings = (colorOptions[(int)Math.Round(qualitySlider.Value)], selectedOnlyCheckBox.IsChecked == true);
            window.Close(true);
        };
        cancel.Click += (_, _) => window.Close(false);

        CancellationTokenSource? previewCancellation = null;
        window.Closed += (_, _) =>
        {
            previewCancellation?.Cancel();
            previewCancellation?.Dispose();
            (previewImage.Source as Bitmap)?.Dispose();
        };

        async Task RefreshPreviewAsync()
        {
            previewCancellation?.Cancel();
            previewCancellation?.Dispose();
            var cancellation = new CancellationTokenSource();
            previewCancellation = cancellation;
            ok.IsEnabled = false;
            var colorCount = colorOptions[(int)Math.Round(qualitySlider.Value)];
            var selectedOnly = selectedOnlyCheckBox.IsChecked == true;
            qualityLabel.Text = colorCount == 0 ? "色数: フルカラー" : $"色数: {colorCount}色";
            previewStatus.Text = "プレビューを生成しています…";
            fileSizeLabel.Text = "PNGサイズ: 計算中…";
            try
            {
                await Task.Delay(140, cancellation.Token);
                var result = await Task.Run(() =>
                {
                    using var selectedImage = selectedOnly
                        ? SpriteImageService.ExportSelectedCells(source, cellWidth, cellHeight, selectedPositions, cancellation.Token)
                        : null;
                    var sourceImage = selectedImage ?? source;
                    using var quantized = colorCount == 0 ? null : SpriteImageService.Quantize(sourceImage, colorCount, cancellation.Token);
                    var exportImage = quantized ?? sourceImage;
                    long fileSize;
                    using (var encoded = SpriteImageService.EncodePng(exportImage))
                        fileSize = encoded.Size;
                    var scale = Math.Min(1f, Math.Min(640f / exportImage.Width, 340f / exportImage.Height));
                    var width = Math.Max(1, (int)Math.Round(exportImage.Width * scale));
                    var height = Math.Max(1, (int)Math.Round(exportImage.Height * scale));
                    using var thumbnail = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
                    using (var canvas = new SKCanvas(thumbnail))
                    {
                        canvas.Clear(SKColors.Transparent);
                        canvas.DrawBitmap(exportImage, new SKRect(0, 0, width, height));
                        canvas.Flush();
                    }
                    using var thumbnailData = SpriteImageService.EncodePng(thumbnail);
                    using var stream = new MemoryStream();
                    thumbnailData.SaveTo(stream);
                    return (Bytes: stream.ToArray(), FileSize: fileSize, Width: exportImage.Width, Height: exportImage.Height);
                }, cancellation.Token);

                if (cancellation.IsCancellationRequested) return;
                using var previewStream = new MemoryStream(result.Bytes);
                var nextBitmap = new Bitmap(previewStream);
                var previousBitmap = previewImage.Source as Bitmap;
                previewImage.Source = nextBitmap;
                previousBitmap?.Dispose();
                fileSizeLabel.Text = $"PNGファイルサイズ: {result.FileSize / 1024d:N1} KB";
                previewStatus.Text = $"プレビュー: {result.Width} × {result.Height} px";
                ok.IsEnabled = true;
            }
            catch (OperationCanceledException) { }
            catch (Exception exception)
            {
                if (cancellation.IsCancellationRequested) return;
                previewStatus.Text = $"プレビューを生成できませんでした: {exception.Message}";
                ok.IsEnabled = true;
            }
        }

        void OnSettingsChanged() => _ = RefreshPreviewAsync();
        qualitySlider.PropertyChanged += (_, e) =>
        {
            if (e.Property == RangeBase.ValueProperty) OnSettingsChanged();
        };
        selectedOnlyCheckBox.PropertyChanged += (_, e) =>
        {
            if (e.Property == CheckBox.IsCheckedProperty) OnSettingsChanged();
        };
        _ = RefreshPreviewAsync();
        var accepted = await window.ShowDialog<bool>(owner);
        return accepted ? selectedSettings : null;
    }

    public static async Task<Color?> PickFillColorAsync(Control owner)
    {
        var window = new EditorDialogWindow
        {
            Title = "セルを塗り潰す",
            Width = 440,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        var sliders = new[]
        {
            new Slider { Minimum = 0, Maximum = 255, Value = 255, TickFrequency = 1, SmallChange = 1, LargeChange = 16 },
            new Slider { Minimum = 0, Maximum = 255, Value = 255, TickFrequency = 1, SmallChange = 1, LargeChange = 16 },
            new Slider { Minimum = 0, Maximum = 255, Value = 255, TickFrequency = 1, SmallChange = 1, LargeChange = 16 }
        };
        var values = new[]
        {
            new TextBox { Text = "255", Width = 68, HorizontalContentAlignment = HorizontalAlignment.Center },
            new TextBox { Text = "255", Width = 68, HorizontalContentAlignment = HorizontalAlignment.Center },
            new TextBox { Text = "255", Width = 68, HorizontalContentAlignment = HorizontalAlignment.Center }
        };
        var channelNames = new[] { "R", "G", "B" };
        var controls = new StackPanel { Spacing = 8, Margin = new Thickness(18, 12) };
        var preview = new Border
        {
            Width = 112,
            Height = 72,
            BorderBrush = Brush.Parse("#89919D"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(12, 4),
            Child = new TextBlock
            {
                Text = "#FFFFFF",
                Foreground = Brush.Parse("#111827"),
                Background = Brush.Parse("#BFFFFFFF"),
                FontWeight = FontWeight.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        var previewLabel = (TextBlock)preview.Child!;

        void UpdatePreview()
        {
            var color = Color.FromRgb(
                (byte)Math.Clamp((int)Math.Round(sliders[0].Value), 0, 255),
                (byte)Math.Clamp((int)Math.Round(sliders[1].Value), 0, 255),
                (byte)Math.Clamp((int)Math.Round(sliders[2].Value), 0, 255));
            preview.Background = new SolidColorBrush(color);
            previewLabel.Text = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
        }

        for (var index = 0; index < sliders.Length; index++)
        {
            var slider = sliders[index];
            var valueInput = values[index];
            var row = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("32,*,76"), ColumnSpacing = 8 };
            row.Children.Add(new TextBlock { Text = channelNames[index], VerticalAlignment = VerticalAlignment.Center });
            Grid.SetColumn(slider, 1);
            row.Children.Add(slider);
            Grid.SetColumn(valueInput, 2);
            row.Children.Add(valueInput);
            controls.Children.Add(row);
            slider.PropertyChanged += (_, e) =>
            {
                if (e.Property != RangeBase.ValueProperty) return;
                var value = Math.Clamp((int)Math.Round(slider.Value), 0, 255);
                if (valueInput.Text != value.ToString()) valueInput.Text = value.ToString();
                UpdatePreview();
            };
            valueInput.TextChanged += (_, _) =>
            {
                if (!int.TryParse(valueInput.Text, out var value)) return;
                slider.Value = Math.Clamp(value, 0, 255);
                UpdatePreview();
            };
        }
        UpdatePreview();

        var ok = CenteredButton("OK", 100, isDefault: true);
        var cancel = CenteredButton("キャンセル", 100, isCancel: true);
        Color? selectedColor = null;
        ok.Click += (_, _) =>
        {
            selectedColor = Color.FromRgb(
                (byte)Math.Clamp((int)Math.Round(sliders[0].Value), 0, 255),
                (byte)Math.Clamp((int)Math.Round(sliders[1].Value), 0, 255),
                (byte)Math.Clamp((int)Math.Round(sliders[2].Value), 0, 255));
            window.Close(true);
        };
        cancel.Click += (_, _) => window.Close(false);
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Spacing = 8,
            Margin = new Thickness(12)
        };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        window.Content = new StackPanel
        {
            Children =
            {
                new TextBlock { Text = "塗り潰す色をRGBで指定してください", Margin = new Thickness(18, 16, 18, 0) },
                controls,
                preview,
                buttons
            }
        };
        return await window.ShowDialog<bool>(owner) ? selectedColor : null;
    }

    public static async Task<bool> ConfirmAsync(Control owner, string title, string message, string yes = "続行", string no = "キャンセル")
    {
        var window = new EditorDialogWindow { Title = title, Width = 440, SizeToContent = SizeToContent.Height, CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var body = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(18), MaxWidth = 390 };
        var yesButton = CenteredButton(yes, 120, isDefault: true);
        var noButton = CenteredButton(no, 100, isCancel: true);
        yesButton.Click += (_, _) => window.Close(true);
        noButton.Click += (_, _) => window.Close(false);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Spacing = 8, Margin = new Thickness(12) };
        buttons.Children.Add(yesButton); buttons.Children.Add(noButton);
        window.Content = new StackPanel { Children = { body, buttons } };
        return await window.ShowDialog<bool>(owner);
    }

    public static async Task<bool?> UnsavedAsync(Control owner, string title, string message)
    {
        var window = new EditorDialogWindow { Title = title, Width = 460, SizeToContent = SizeToContent.Height, CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var body = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(18), MaxWidth = 410 };
        var save = CenteredButton("保存して終了", 120, isDefault: true);
        var discard = CenteredButton("保存しないで終了", 140);
        var cancel = CenteredButton("キャンセル", 110, isCancel: true);
        save.Click += (_, _) => window.Close(true);
        discard.Click += (_, _) => window.Close(false);
        cancel.Click += (_, _) => window.Close(null);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Spacing = 8, Margin = new Thickness(12) };
        buttons.Children.Add(save); buttons.Children.Add(discard); buttons.Children.Add(cancel);
        window.Content = new StackPanel { Children = { body, buttons } };
        return await window.ShowDialog<bool?>(owner);
    }

    public static async Task<(ResizeAnchor Anchor, int CellWidth, int CellHeight)?> PickResizeSettingsAsync(
        Control owner, Bitmap sampleImage, int currentCellWidth, int currentCellHeight, int initialCellWidth, int initialCellHeight,
        int columns, int rows, ResizeAnchor initialAnchor, Func<CancellationToken, Task<SAMERIDER.Core.Models.PixelRect?>> findCommonOpaqueBoundsAsync)
    {
        var window = new EditorDialogWindow { Title = "セルサイズの変更", Width = 560, SizeToContent = SizeToContent.Height, CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var widthInput = new TextBox { Text = initialCellWidth.ToString(), Width = 76, HorizontalContentAlignment = HorizontalAlignment.Center };
        var heightInput = new TextBox { Text = initialCellHeight.ToString(), Width = 76, HorizontalContentAlignment = HorizontalAlignment.Center };
        var trimTransparentCheckBox = new CheckBox
        {
            Content = "透明部分を切り詰める",
            IsChecked = false,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(8, 2)
        };
        var trimAmountLabel = new TextBlock
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            Foreground = Brush.Parse("#A1A8B3"),
            IsVisible = false,
            Margin = new Thickness(8, 0, 8, 6)
        };
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(16, 4, 16, 8), IsVisible = false };
        var ok = CenteredButton("OK", 100, isDefault: true);
        var previewCanvas = new Canvas { ClipToBounds = true };
        var previewFrame = new Border
        {
            BorderBrush = Brush.Parse("#606A78"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4),
            HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(16, 8), Child = previewCanvas
        };
        var anchorGrid = new Grid
        {
            RowDefinitions = RowDefinitions.Parse("*,*,*"), ColumnDefinitions = ColumnDefinitions.Parse("*,*,*"),
            IsHitTestVisible = true
        };
        var previewButtons = new List<(ResizeAnchor Anchor, Button Button)>();
        var selectedAnchor = initialAnchor;
        var previewAnchor = selectedAnchor;
        var targetOutline = new Border
        {
            BorderBrush = Brush.Parse("#AAB4C0"), BorderThickness = new Thickness(1), Background = Brushes.Transparent,
            IsHitTestVisible = false
        };
        var sampleView = new Image { Source = sampleImage, Stretch = Stretch.Fill, IsHitTestVisible = false };
        var effectCanvas = new Canvas { IsHitTestVisible = false };
        previewCanvas.Children.Add(sampleView);
        previewCanvas.Children.Add(effectCanvas);
        previewCanvas.Children.Add(targetOutline);
        previewCanvas.Children.Add(anchorGrid);
        var cropFill = Brush.Parse("#809AA0AA");
        var addedFill = Brush.Parse("#804094FF");

        void UpdateAnchorButtonStyles()
        {
            foreach (var (anchor, button) in previewButtons)
            {
                button.Background = anchor == previewAnchor ? Brush.Parse("#D93683F6") : Brush.Parse("#C91B2028");
                button.BorderBrush = anchor == selectedAnchor ? Brush.Parse("#FDE68A") : Brush.Parse("#707987");
                button.BorderThickness = new Thickness(anchor == selectedAnchor ? 2 : 1);
            }
        }

        void SetCanvasRectangle(Canvas canvas, double x, double y, double width, double height, double scale, double originX, double originY, IBrush fill)
        {
            if (width <= 0 || height <= 0) return;
            var rectangle = new Rectangle { Width = width * scale, Height = height * scale, Fill = fill, IsHitTestVisible = false };
            Canvas.SetLeft(rectangle, (x - originX) * scale);
            Canvas.SetTop(rectangle, (y - originY) * scale);
            canvas.Children.Add(rectangle);
        }

        void AddDifference(Canvas canvas, Rect source, Rect cover, double scale, double originX, double originY, IBrush fill)
        {
            var left = Math.Max(source.Left, cover.Left);
            var top = Math.Max(source.Top, cover.Top);
            var right = Math.Min(source.Right, cover.Right);
            var bottom = Math.Min(source.Bottom, cover.Bottom);
            if (right <= left || bottom <= top)
            {
                SetCanvasRectangle(canvas, source.Left, source.Top, source.Width, source.Height, scale, originX, originY, fill);
                return;
            }
            SetCanvasRectangle(canvas, source.Left, source.Top, source.Width, top - source.Top, scale, originX, originY, fill);
            SetCanvasRectangle(canvas, source.Left, bottom, source.Width, source.Bottom - bottom, scale, originX, originY, fill);
            SetCanvasRectangle(canvas, source.Left, top, left - source.Left, bottom - top, scale, originX, originY, fill);
            SetCanvasRectangle(canvas, right, top, source.Right - right, bottom - top, scale, originX, originY, fill);
        }

        bool TryReadSize(out int width, out int height)
        {
            width = 0;
            height = 0;
            var valid = int.TryParse(widthInput.Text, out width) && int.TryParse(heightInput.Text, out height) && width > 0 && height > 0;
            if (!valid) return false;
            var sheetWidth = columns * (long)width;
            var sheetHeight = rows * (long)height;
            return (long)columns * rows <= ProjectValidator.MaximumGridCellCount && sheetWidth <= 16_384 &&
                sheetHeight <= 16_384 && sheetWidth * sheetHeight <= ProjectValidator.MaximumSheetPixelCount;
        }

        int GetTrimmedDimension(int currentSize, int contentStart, int contentEnd, double anchorFactor)
        {
            if (contentEnd <= contentStart) return 1;
            var maximumTrimmedSize = currentSize;
            for (var targetSize = currentSize - 1; targetSize >= 1; targetSize--)
            {
                var totalCrop = currentSize - targetSize;
                var cropBeforeAnchor = (int)Math.Floor(totalCrop * anchorFactor);
                var cropAfterAnchor = totalCrop - cropBeforeAnchor;
                if (cropBeforeAnchor > contentStart || cropAfterAnchor > currentSize - contentEnd) break;
                maximumTrimmedSize = targetSize;
            }
            return maximumTrimmedSize;
        }

        CancellationTokenSource? transparentAnalysisCancellation = null;
        Task<SAMERIDER.Core.Models.PixelRect?>? commonOpaqueBoundsTask = null;
        var isTransparentAnalysisInProgress = false;
        var isWindowClosed = false;
        var transparentTrimRequestVersion = 0;

        void UpdatePreview()
        {
            if (!TryReadSize(out var targetWidth, out var targetHeight))
            {
                effectCanvas.Children.Clear();
                status.Text = "セルサイズは正の整数で、スプライトシートの上限以内にしてください。";
                status.Foreground = Brushes.Orange;
                status.IsVisible = true;
                ok.IsEnabled = false;
                return;
            }

            var (anchorX, anchorY) = GetAnchorFactors(previewAnchor);
            var deltaX = (int)Math.Floor((currentCellWidth - targetWidth) * anchorX);
            var deltaY = (int)Math.Floor((currentCellHeight - targetHeight) * anchorY);
            var isAddingX = targetWidth > currentCellWidth;
            var isAddingY = targetHeight > currentCellHeight;
            var oldBounds = new Rect(isAddingX ? -deltaX : 0, isAddingY ? -deltaY : 0, currentCellWidth, currentCellHeight);
            var newBounds = new Rect(isAddingX ? 0 : deltaX, isAddingY ? 0 : deltaY, targetWidth, targetHeight);
            const double originX = 0;
            const double originY = 0;
            var extentWidth = Math.Max(currentCellWidth, targetWidth);
            var extentHeight = Math.Max(currentCellHeight, targetHeight);
            var scale = Math.Min(420 / extentWidth, 250 / extentHeight);
            if (!double.IsFinite(scale) || scale <= 0) return;

            var canvasWidth = extentWidth * scale;
            var canvasHeight = extentHeight * scale;
            previewCanvas.Width = canvasWidth;
            previewCanvas.Height = canvasHeight;
            previewFrame.Width = canvasWidth + 2;
            previewFrame.Height = canvasHeight + 2;
            sampleView.Width = oldBounds.Width * scale;
            sampleView.Height = oldBounds.Height * scale;
            Canvas.SetLeft(sampleView, oldBounds.Left * scale);
            Canvas.SetTop(sampleView, oldBounds.Top * scale);

            effectCanvas.Width = canvasWidth;
            effectCanvas.Height = canvasHeight;
            effectCanvas.Children.Clear();
            AddDifference(effectCanvas, oldBounds, newBounds, scale, originX, originY, cropFill);
            AddDifference(effectCanvas, newBounds, oldBounds, scale, originX, originY, addedFill);

            targetOutline.Width = targetWidth * scale;
            targetOutline.Height = targetHeight * scale;
            Canvas.SetLeft(targetOutline, (newBounds.Left - originX) * scale);
            Canvas.SetTop(targetOutline, (newBounds.Top - originY) * scale);

            var anchorFrameLeft = isAddingX ? newBounds.Left : oldBounds.Left;
            var anchorFrameTop = isAddingY ? newBounds.Top : oldBounds.Top;
            var anchorFrameWidth = (isAddingX ? targetWidth : currentCellWidth) * scale;
            var anchorFrameHeight = (isAddingY ? targetHeight : currentCellHeight) * scale;
            anchorGrid.Width = anchorFrameWidth;
            anchorGrid.Height = anchorFrameHeight;
            Canvas.SetLeft(anchorGrid, (anchorFrameLeft - originX) * scale);
            Canvas.SetTop(anchorGrid, (anchorFrameTop - originY) * scale);
            foreach (var (_, button) in previewButtons)
            {
                button.Width = Math.Min(70, Math.Max(32, anchorGrid.Width / 3 - 4));
                button.Height = Math.Min(30, Math.Max(24, anchorGrid.Height / 3 - 4));
                button.FontSize = button.Width < 48 ? 9 : 11;
            }
            status.IsVisible = false;
            ok.IsEnabled = !isTransparentAnalysisInProgress;
            UpdateAnchorButtonStyles();
        }

        window.Closed += (_, _) =>
        {
            isWindowClosed = true;
            transparentAnalysisCancellation?.Cancel();
            transparentAnalysisCancellation?.Dispose();
        };

        async Task ApplyTransparentTrimAsync(ResizeAnchor anchor)
        {
            if (trimTransparentCheckBox.IsChecked != true) return;
            var requestVersion = ++transparentTrimRequestVersion;
            isTransparentAnalysisInProgress = true;
            ok.IsEnabled = false;
            widthInput.IsEnabled = false;
            heightInput.IsEnabled = false;
            status.Text = "全セルの透明部分を解析しています…";
            status.Foreground = Brush.Parse("#A1A8B3");
            status.IsVisible = true;
            trimAmountLabel.Text = "切り詰め量を計算しています…";
            trimAmountLabel.IsVisible = true;
            string? analysisError = null;
            try
            {
                commonOpaqueBoundsTask ??= findCommonOpaqueBoundsAsync(transparentAnalysisCancellation!.Token);
                var bounds = await commonOpaqueBoundsTask;
                if (isWindowClosed || requestVersion != transparentTrimRequestVersion || trimTransparentCheckBox.IsChecked != true) return;
                var (anchorX, anchorY) = GetAnchorFactors(anchor);
                var width = bounds is { } opaqueBounds
                    ? GetTrimmedDimension(currentCellWidth, opaqueBounds.X, opaqueBounds.X + opaqueBounds.Width, anchorX)
                    : 1;
                var height = bounds is { } opaqueHeightBounds
                    ? GetTrimmedDimension(currentCellHeight, opaqueHeightBounds.Y, opaqueHeightBounds.Y + opaqueHeightBounds.Height, anchorY)
                    : 1;
                widthInput.Text = width.ToString();
                heightInput.Text = height.ToString();
                var trimmedWidth = currentCellWidth - width;
                var trimmedHeight = currentCellHeight - height;
                trimAmountLabel.Text = trimmedWidth == 0 && trimmedHeight == 0
                    ? "切り詰められる空白がありません"
                    : $"縦: -{trimmedHeight} px　横: -{trimmedWidth}px";
            }
            catch (OperationCanceledException) { }
            catch (Exception exception)
            {
                if (isWindowClosed || requestVersion != transparentTrimRequestVersion) return;
                analysisError = $"透明部分を解析できませんでした: {exception.Message}";
                trimTransparentCheckBox.IsChecked = false;
                trimAmountLabel.IsVisible = false;
                status.Text = analysisError;
                status.Foreground = Brushes.Orange;
                status.IsVisible = true;
            }
            finally
            {
                if (requestVersion == transparentTrimRequestVersion)
                {
                    isTransparentAnalysisInProgress = false;
                    if (!isWindowClosed)
                    {
                        widthInput.IsEnabled = trimTransparentCheckBox.IsChecked != true;
                        heightInput.IsEnabled = trimTransparentCheckBox.IsChecked != true;
                        UpdatePreview();
                        if (analysisError is not null)
                        {
                            status.Text = analysisError;
                            status.Foreground = Brushes.Orange;
                            status.IsVisible = true;
                        }
                    }
                }
            }
        }

        previewCanvas.PointerMoved += (_, e) =>
        {
            if (e.Source is Button)
            {
                ToolTip.SetTip(previewCanvas, null);
                return;
            }

            var pointer = e.GetPosition(effectCanvas);
            var tip = effectCanvas.Children.OfType<Rectangle>().FirstOrDefault(rectangle =>
            {
                var left = Canvas.GetLeft(rectangle);
                var top = Canvas.GetTop(rectangle);
                return pointer.X >= left && pointer.X < left + rectangle.Width &&
                    pointer.Y >= top && pointer.Y < top + rectangle.Height;
            });
            ToolTip.SetTip(previewCanvas, tip is null ? null : ReferenceEquals(tip.Fill, cropFill) ? "削られる領域" : "追加される領域");
        };
        previewCanvas.PointerExited += (_, _) => ToolTip.SetTip(previewCanvas, null);

        string[] labels = ["左上", "上", "右上", "左", "中央", "右", "左下", "下", "右下"];
        foreach (var (anchor, index) in Enum.GetValues<ResizeAnchor>().Select((value, index) => (value, index)))
        {
            var button = CenteredButton(labels[index], 0);
            button.Width = 70; button.Height = 30; button.Margin = new Thickness(2); button.Padding = new Thickness(2, 1);
            Grid.SetRow(button, index / 3);
            Grid.SetColumn(button, index % 3);
            button.HorizontalAlignment = (index % 3) switch
            {
                0 => HorizontalAlignment.Left,
                1 => HorizontalAlignment.Center,
                _ => HorizontalAlignment.Right
            };
            button.VerticalAlignment = (index / 3) switch
            {
                0 => VerticalAlignment.Top,
                1 => VerticalAlignment.Center,
                _ => VerticalAlignment.Bottom
            };
            button.PointerEntered += (_, _) =>
            {
                if (previewAnchor == anchor) return;
                previewAnchor = anchor;
                if (trimTransparentCheckBox.IsChecked == true) _ = ApplyTransparentTrimAsync(anchor);
                else UpdatePreview();
            };
            button.Click += (_, _) =>
            {
                selectedAnchor = anchor;
                previewAnchor = anchor;
                if (trimTransparentCheckBox.IsChecked == true) _ = ApplyTransparentTrimAsync(anchor);
                else UpdatePreview();
            };
            previewButtons.Add((anchor, button));
            anchorGrid.Children.Add(button);
        }
        anchorGrid.PointerExited += (_, _) =>
        {
            if (previewAnchor == selectedAnchor) return;
            previewAnchor = selectedAnchor;
            if (trimTransparentCheckBox.IsChecked == true) _ = ApplyTransparentTrimAsync(selectedAnchor);
            else UpdatePreview();
        };

        var sizeInputs = new StackPanel
        {
            Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center, Spacing = 8, Margin = new Thickness(16, 6)
        };
        sizeInputs.Children.Add(new TextBlock { Text = "セルサイズ", VerticalAlignment = VerticalAlignment.Center });
        sizeInputs.Children.Add(widthInput);
        sizeInputs.Children.Add(new TextBlock { Text = "×", VerticalAlignment = VerticalAlignment.Center });
        sizeInputs.Children.Add(heightInput);
        sizeInputs.Children.Add(new TextBlock { Text = "px", VerticalAlignment = VerticalAlignment.Center });
        widthInput.TextChanged += (_, _) => UpdatePreview();
        heightInput.TextChanged += (_, _) => UpdatePreview();
        transparentAnalysisCancellation = new CancellationTokenSource();
        trimTransparentCheckBox.PropertyChanged += (_, e) =>
        {
            if (e.Property != CheckBox.IsCheckedProperty) return;
            if (trimTransparentCheckBox.IsChecked == true) _ = ApplyTransparentTrimAsync(selectedAnchor);
            else
            {
                transparentTrimRequestVersion++;
                isTransparentAnalysisInProgress = false;
                trimAmountLabel.IsVisible = false;
                widthInput.IsEnabled = true;
                heightInput.IsEnabled = true;
                UpdatePreview();
            }
        };

        var cancel = CenteredButton("キャンセル", 100, isCancel: true);
        (ResizeAnchor Anchor, int CellWidth, int CellHeight)? selected = null;
        ok.Click += (_, _) =>
        {
            if (!TryReadSize(out var width, out var height)) return;
            selected = (selectedAnchor, width, height);
            window.Close(true);
        };
        cancel.Click += (_, _) => window.Close(false);
        var actionButtons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Spacing = 8, Margin = new Thickness(12) };
        actionButtons.Children.Add(ok);
        actionButtons.Children.Add(cancel);
        window.Content = new StackPanel
        {
            Children =
            {
                new TextBlock { Text = "セルサイズを指定しトリミングの基準点を選んでください", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(16, 16, 16, 4) },
                trimTransparentCheckBox, trimAmountLabel, sizeInputs, previewFrame, status, actionButtons
            }
        };
        UpdatePreview();
        var accepted = await window.ShowDialog<bool>(owner);
        return accepted ? selected : null;
    }

    public static async Task<(int CellWidth, int CellHeight, int Columns, int Rows)?> PickInitialCellSizeAsync(
        Control owner, int imageWidth, int imageHeight, Bitmap sampleImage)
    {
        var window = new EditorDialogWindow { Title = "画像からセルサイズを設定", Width = 390, SizeToContent = SizeToContent.Height, CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var divisor = GreatestCommonDivisor(imageWidth, imageHeight);
        var initialColumns = Math.Max(1, imageWidth / divisor);
        var initialRows = Math.Max(1, imageHeight / divisor);
        if ((long)initialColumns * initialRows > ProjectValidator.MaximumGridCellCount)
        {
            initialColumns = 1;
            initialRows = 1;
        }
        var columns = new TextBox { Text = initialColumns.ToString(), Width = 72, HorizontalContentAlignment = HorizontalAlignment.Center };
        var rows = new TextBox { Text = initialRows.ToString(), Width = 72, HorizontalContentAlignment = HorizontalAlignment.Center };
        var calculation = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(16, 4, 16, 8) };
        const double previewMaximumWidth = 340;
        const double previewMaximumHeight = 220;
        var previewScale = Math.Min(previewMaximumWidth / imageWidth, previewMaximumHeight / imageHeight);
        var previewWidth = imageWidth * previewScale;
        var previewHeight = imageHeight * previewScale;
        var previewCanvas = new Canvas { Width = previewWidth, Height = previewHeight, ClipToBounds = true };
        previewCanvas.Children.Add(new Image { Source = sampleImage, Stretch = Stretch.Fill, Width = previewWidth, Height = previewHeight, IsHitTestVisible = false });
        var splitLineCanvas = new Canvas { Width = previewWidth, Height = previewHeight, IsHitTestVisible = false };
        previewCanvas.Children.Add(splitLineCanvas);
        var previewFrame = new Border
        {
            BorderBrush = Brush.Parse("#606A78"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4),
            HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(16, 4), Child = previewCanvas
        };
        var ok = CenteredButton("OK", 100, isDefault: true);
        var cancel = CenteredButton("キャンセル", 100, isCancel: true);
        (int CellWidth, int CellHeight, int Columns, int Rows)? selected = null;

        void UpdateCalculation()
        {
            var parsedColumns = int.TryParse(columns.Text, out var columnCount) && columnCount > 0;
            var parsedRows = int.TryParse(rows.Text, out var rowCount) && rowCount > 0;
            splitLineCanvas.Children.Clear();
            if (parsedColumns && parsedRows && (long)columnCount * rowCount <= ProjectValidator.MaximumGridCellCount)
            {
                var lineStroke = Brush.Parse("#D9FDE68A");
                for (var column = 1; column < columnCount; column++)
                {
                    var x = previewWidth * column / columnCount;
                    splitLineCanvas.Children.Add(new Line { StartPoint = new Point(x, 0), EndPoint = new Point(x, previewHeight), Stroke = lineStroke, StrokeThickness = 1 });
                }
                for (var row = 1; row < rowCount; row++)
                {
                    var y = previewHeight * row / rowCount;
                    splitLineCanvas.Children.Add(new Line { StartPoint = new Point(0, y), EndPoint = new Point(previewWidth, y), Stroke = lineStroke, StrokeThickness = 1 });
                }
            }

            if (parsedColumns && parsedRows && imageWidth % columnCount == 0 && imageHeight % rowCount == 0 &&
                (long)columnCount * rowCount <= ProjectValidator.MaximumGridCellCount && imageWidth <= 16_384 && imageHeight <= 16_384 &&
                (long)imageWidth * imageHeight <= ProjectValidator.MaximumSheetPixelCount)
            {
                var cellWidth = imageWidth / columnCount;
                var cellHeight = imageHeight / rowCount;
                calculation.Text = $"セルサイズ: {cellWidth} × {cellHeight} px";
                calculation.Foreground = Brushes.White;
                ok.IsEnabled = true;
            }
            else
            {
                calculation.Text = "分割数を画像サイズで割り切れる値にしてください。";
                calculation.Foreground = Brushes.Orange;
                ok.IsEnabled = false;
            }
        }

        columns.TextChanged += (_, _) => UpdateCalculation();
        rows.TextChanged += (_, _) => UpdateCalculation();
        ok.Click += (_, _) =>
        {
            if (!ok.IsEnabled || !int.TryParse(columns.Text, out var columnCount) || !int.TryParse(rows.Text, out var rowCount)) return;
            selected = (imageWidth / columnCount, imageHeight / rowCount, columnCount, rowCount);
            window.Close(true);
        };
        cancel.Click += (_, _) => window.Close(false);

        var splitInputs = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Spacing = 8, Margin = new Thickness(16, 4) };
        splitInputs.Children.Add(new TextBlock { Text = "横", VerticalAlignment = VerticalAlignment.Center });
        splitInputs.Children.Add(columns);
        splitInputs.Children.Add(new TextBlock { Text = "×", VerticalAlignment = VerticalAlignment.Center });
        splitInputs.Children.Add(new TextBlock { Text = "縦", VerticalAlignment = VerticalAlignment.Center });
        splitInputs.Children.Add(rows);
        splitInputs.Children.Add(new TextBlock { Text = "分割", VerticalAlignment = VerticalAlignment.Center });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Spacing = 8, Margin = new Thickness(12) };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        var header = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(16, 16, 16, 6) };
        var headerRuns = header.Inlines ?? throw new InvalidOperationException("画像サイズの表示を初期化できませんでした。");
        headerRuns.Add(new Run("画像サイズ: "));
        headerRuns.Add(new Run($"{imageWidth} × {imageHeight} px") { FontWeight = FontWeight.Bold, Foreground = Brush.Parse("#FDE68A") });
        headerRuns.Add(new Run("\n画像の分割数を指定してください。"));
        window.Content = new StackPanel { Children = { header, previewFrame, splitInputs, calculation, buttons } };
        UpdateCalculation();
        var accepted = await window.ShowDialog<bool>(owner);
        return accepted ? selected : null;
    }

    public static async Task<(int CellWidth, int CellHeight, int Columns, int Rows)?> PickTrimmedImportCellSizeAsync(
        Control owner, int imageWidth, int imageHeight, int initialCellWidth, int initialCellHeight, Bitmap sampleImage,
        bool allowCellSizeChange = true)
    {
        var window = new EditorDialogWindow { Title = "画像のセルサイズを指定", Width = 600, SizeToContent = SizeToContent.Height, CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var cellWidth = new TextBox { Text = initialCellWidth.ToString(), Width = 72, HorizontalContentAlignment = HorizontalAlignment.Center };
        var cellHeight = new TextBox { Text = initialCellHeight.ToString(), Width = 72, HorizontalContentAlignment = HorizontalAlignment.Center };
        if (!allowCellSizeChange)
        {
            cellWidth.IsReadOnly = true;
            cellHeight.IsReadOnly = true;
        }
        var calculation = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(16, 4, 16, 8) };
        const double previewMaximumWidth = 360;
        const double previewMaximumHeight = 220;
        var previewScale = Math.Min(previewMaximumWidth / imageWidth, previewMaximumHeight / imageHeight);
        var previewWidth = imageWidth * previewScale;
        var previewHeight = imageHeight * previewScale;
        var previewCanvas = new Canvas { Width = previewWidth, Height = previewHeight, ClipToBounds = true };
        previewCanvas.Children.Add(new Image { Source = sampleImage, Stretch = Stretch.Fill, Width = previewWidth, Height = previewHeight, IsHitTestVisible = false });
        var splitLineCanvas = new Canvas { Width = previewWidth, Height = previewHeight, IsHitTestVisible = false };
        previewCanvas.Children.Add(splitLineCanvas);
        var previewFrame = new Border
        {
            BorderBrush = Brush.Parse("#606A78"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4),
            HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(16, 4), Child = previewCanvas
        };
        var ok = CenteredButton("読み込む", 100, isDefault: true);
        var cancel = CenteredButton("キャンセル", 100, isCancel: true);
        (int CellWidth, int CellHeight, int Columns, int Rows)? selected = null;

        void UpdateCalculation()
        {
            var validWidth = int.TryParse(cellWidth.Text, out var width) && width > 0;
            var validHeight = int.TryParse(cellHeight.Text, out var height) && height > 0;
            splitLineCanvas.Children.Clear();
            if (validWidth && validHeight)
            {
                var columns = imageWidth / width;
                var rows = imageHeight / height;
                if (columns > 0 && rows > 0 && (long)columns * rows <= ProjectValidator.MaximumGridCellCount)
                {
                    var lineStroke = Brush.Parse("#D9FDE68A");
                    for (var column = 1; column <= columns; column++)
                    {
                        var x = previewWidth * (long)column * width / imageWidth;
                        splitLineCanvas.Children.Add(new Line { StartPoint = new Point(x, 0), EndPoint = new Point(x, previewHeight), Stroke = lineStroke, StrokeThickness = 1 });
                    }
                    for (var row = 1; row <= rows; row++)
                    {
                        var y = previewHeight * (long)row * height / imageHeight;
                        splitLineCanvas.Children.Add(new Line { StartPoint = new Point(0, y), EndPoint = new Point(previewWidth, y), Stroke = lineStroke, StrokeThickness = 1 });
                    }
                    var usedWidth = columns * (long)width;
                    var usedHeight = rows * (long)height;
                    var remainderWidth = imageWidth - usedWidth;
                    var remainderHeight = imageHeight - usedHeight;
                    calculation.Text = $"セル数: {columns} × {rows}（セルサイズ: {width} × {height} px）";
                    if (remainderWidth != 0 || remainderHeight != 0)
                        calculation.Text += $"\n端数は右端 {remainderWidth}px・下端 {remainderHeight}px を切り捨てます。読み込み後はセルを選択し、方向キーで必要に応じて表示位置を調節してください。";
                    calculation.Foreground = Brushes.White;
                    ok.IsEnabled = imageWidth <= 16_384 && imageHeight <= 16_384 &&
                        (long)imageWidth * imageHeight <= ProjectValidator.MaximumSheetPixelCount;
                    return;
                }
            }

            calculation.Text = "画像内にセルが収まる正のサイズを指定してください（セル数上限を超えています）。";
            calculation.Foreground = Brushes.Orange;
            ok.IsEnabled = false;
        }

        cellWidth.TextChanged += (_, _) => UpdateCalculation();
        cellHeight.TextChanged += (_, _) => UpdateCalculation();
        ok.Click += (_, _) =>
        {
            if (!ok.IsEnabled || !int.TryParse(cellWidth.Text, out var width) || !int.TryParse(cellHeight.Text, out var height)) return;
            selected = (width, height, imageWidth / width, imageHeight / height);
            window.Close(true);
        };
        cancel.Click += (_, _) => window.Close(false);

        var sizeInputs = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Spacing = 8, Margin = new Thickness(16, 4) };
        sizeInputs.Children.Add(new TextBlock { Text = "幅", VerticalAlignment = VerticalAlignment.Center });
        sizeInputs.Children.Add(cellWidth);
        sizeInputs.Children.Add(new TextBlock { Text = "×", VerticalAlignment = VerticalAlignment.Center });
        sizeInputs.Children.Add(new TextBlock { Text = "高さ", VerticalAlignment = VerticalAlignment.Center });
        sizeInputs.Children.Add(cellHeight);
        sizeInputs.Children.Add(new TextBlock { Text = "px", VerticalAlignment = VerticalAlignment.Center });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Spacing = 8, Margin = new Thickness(12) };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        var existingCellsNote = allowCellSizeChange ? string.Empty : "\n※既存セルがあるため、セルサイズは現在の設定に固定されています。";
        var header = new TextBlock
        {
            Text = $"画像サイズ: {imageWidth} × {imageHeight} px\nセルサイズを指定してください。{existingCellsNote}",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(16, 16, 16, 6)
        };
        window.Content = new StackPanel { Children = { header, previewFrame, sizeInputs, calculation, buttons } };
        UpdateCalculation();
        var accepted = await window.ShowDialog<bool>(owner);
        return accepted ? selected : null;
    }

    public static async Task ShowErrorAsync(Control owner, string message)
    {
        var window = new EditorDialogWindow { Title = "エラー", Width = 480, SizeToContent = SizeToContent.Height, CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var close = CenteredButton("閉じる", 90, isDefault: true, isCancel: true);
        close.HorizontalAlignment = HorizontalAlignment.Center; close.Margin = new Thickness(12);
        close.Click += (_, _) => window.Close();
        window.Content = new StackPanel { Children = { new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(18), MaxWidth = 430 }, close } };
        await window.ShowDialog(owner);
    }

    private static Button CenteredButton(string text, double minWidth, bool isDefault = false, bool isCancel = false) => new()
    {
        Content = text,
        MinWidth = minWidth,
        IsDefault = isDefault,
        IsCancel = isCancel,
        HorizontalContentAlignment = HorizontalAlignment.Center,
        VerticalContentAlignment = VerticalAlignment.Center
    };

    private static int GreatestCommonDivisor(int first, int second)
    {
        while (second != 0) (first, second) = (second, first % second);
        return Math.Max(first, 1);
    }

    private static (double X, double Y) GetAnchorFactors(ResizeAnchor anchor) => anchor switch
    {
        ResizeAnchor.TopLeft => (0, 0), ResizeAnchor.Top => (0.5, 0), ResizeAnchor.TopRight => (1, 0),
        ResizeAnchor.Left => (0, 0.5), ResizeAnchor.Center => (0.5, 0.5), ResizeAnchor.Right => (1, 0.5),
        ResizeAnchor.BottomLeft => (0, 1), ResizeAnchor.Bottom => (0.5, 1), ResizeAnchor.BottomRight => (1, 1),
        _ => throw new ArgumentOutOfRangeException(nameof(anchor))
    };
}
