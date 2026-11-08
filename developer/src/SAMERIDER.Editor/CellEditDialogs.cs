using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using SAMERIDER.Core.Services;
using SAMERIDER.Core.Models;
using SkiaSharp;

namespace SAMERIDER.Editor;

internal static class CellEditDialogs
{
    private const int MaximumSheetSide = 16384;
    private const long MaximumSheetPixels = 64_000_000;

    public static async Task<(int Width, int Height)?> PickSheetSizeAsync(
        Control owner, SKBitmap preview, int cellWidth, int cellHeight, int columns, int rows)
    {
        var widthInput = new TextBox { Width = 88, Text = cellWidth.ToString(), HorizontalContentAlignment = HorizontalAlignment.Center };
        var heightInput = new TextBox { Width = 88, Text = cellHeight.ToString(), HorizontalContentAlignment = HorizontalAlignment.Center };
        var image = new Image { Stretch = Stretch.Uniform, Width = 480, Height = 280 };
        using var previewData = EncodePng(preview);
        using var previewStream = new MemoryStream(previewData.ToArray());
        var previewBitmap = new Bitmap(previewStream);
        image.Source = previewBitmap;
        RenderOptions.SetBitmapInterpolationMode(image, BitmapInterpolationMode.None);
        var sizeLabel = new TextBlock { HorizontalAlignment = HorizontalAlignment.Center, TextWrapping = TextWrapping.Wrap };
        var validation = new TextBlock { HorizontalAlignment = HorizontalAlignment.Center, Foreground = Brushes.Orange };
        var ok = Button("OK", true);
        var cancel = Button("キャンセル", false, true);
        var snap = Button("スナップ");
        var dimensions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center };
        dimensions.Children.Add(new TextBlock { Text = $"セルサイズ：{cellWidth} × {cellHeight} px →", VerticalAlignment = VerticalAlignment.Center });
        dimensions.Children.Add(widthInput);
        dimensions.Children.Add(new TextBlock { Text = "×", VerticalAlignment = VerticalAlignment.Center });
        dimensions.Children.Add(heightInput);
        dimensions.Children.Add(snap);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center };
        buttons.Children.Add(ok); buttons.Children.Add(cancel);
        var content = new StackPanel { Spacing = 8, Margin = new Thickness(14) };
        content.Children.Add(dimensions);
        content.Children.Add(sizeLabel);
        content.Children.Add(validation);
        content.Children.Add(new Border { BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1), HorizontalAlignment = HorizontalAlignment.Center, Child = image });
        content.Children.Add(buttons);
        var window = new EditorDialogWindow { Title = "全体サイズ変更", Width = 620, SizeToContent = SizeToContent.Height, CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = content };
        (int Width, int Height)? selected = null;

        void Validate()
        {
            var validWidth = int.TryParse(widthInput.Text, out var width) && width > 0;
            var validHeight = int.TryParse(heightInput.Text, out var height) && height > 0;
            snap.IsEnabled = validWidth && validHeight && width <= MaximumSheetSide && height <= MaximumSheetSide;
            if (validWidth && validHeight)
            {
                var sheetWidth = (long)width * columns;
                var sheetHeight = (long)height * rows;
                sizeLabel.Text = $"全体サイズ：{(long)cellWidth * columns} × {(long)cellHeight * rows} px → {sheetWidth} × {sheetHeight} px";
                var withinLimits = columns > 0 && rows > 0 && sheetWidth <= MaximumSheetSide && sheetHeight <= MaximumSheetSide && sheetWidth * sheetHeight <= MaximumSheetPixels;
                ok.IsEnabled = withinLimits;
                validation.Text = withinLimits ? string.Empty : "シートは各辺16384px以下、総画素数64M以下にしてください。";
                if (withinLimits)
                {
                    var scale = Math.Min(1d, Math.Min(480d / sheetWidth, 280d / sheetHeight));
                    using var scaled = ScalePreview(preview, Math.Max(1, (int)Math.Round(sheetWidth * scale)), Math.Max(1, (int)Math.Round(sheetHeight * scale)));
                    using var data = EncodePng(scaled);
                    using var stream = new MemoryStream(data.ToArray());
                    var next = new Bitmap(stream);
                    var previous = image.Source as Bitmap;
                    image.Source = next;
                    previous?.Dispose();
                    previewBitmap = next;
                }
            }
            else
            {
                sizeLabel.Text = $"全体サイズ：{(long)cellWidth * columns} × {(long)cellHeight * rows} px → —";
                validation.Text = "幅と高さは正の整数で指定してください。";
                ok.IsEnabled = false;
            }
            validation.IsVisible = !string.IsNullOrEmpty(validation.Text);
        }

        widthInput.TextChanged += (_, _) => Validate();
        heightInput.TextChanged += (_, _) => Validate();
        snap.Click += (_, _) =>
        {
            if (!int.TryParse(widthInput.Text, out var width) || !int.TryParse(heightInput.Text, out var height) || columns <= 0 || rows <= 0) return;
            widthInput.Text = CellEditService.SnapCellSize(width, columns).ToString();
            heightInput.Text = CellEditService.SnapCellSize(height, rows).ToString();
        };
        ok.Click += (_, _) =>
        {
            Validate();
            if (!ok.IsEnabled || !int.TryParse(widthInput.Text, out var width) || !int.TryParse(heightInput.Text, out var height)) return;
            selected = (width, height);
            window.Close();
        };
        cancel.Click += (_, _) => window.Close();
        Validate();
        try { await window.ShowDialog(owner); return selected; }
        finally { previewBitmap.Dispose(); }
    }

    public static async Task<(float Hue, float Saturation, float Brightness, float Contrast, ColorInversion Inversions)?> PickColorAsync(Control owner, SKBitmap preview)
    {
        var window = new EditorDialogWindow { Title = "色調整", Width = 700, SizeToContent = SizeToContent.Height, CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var image = new Image { Width = 520, Height = 280, Stretch = Stretch.Uniform };
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(60) };
        var values = new[] { 0f, 0f, 0f, 0f };
        var sliders = new Slider[4];
        var valueLabels = new TextBlock[4];
        var inversionChecks = new CheckBox[4];
        ColorInversion[] inversionFlags = [ColorInversion.Hue, ColorInversion.Saturation, ColorInversion.Brightness, ColorInversion.Contrast];
        string[] inversionTips =
        [
            "色相の反転\n色相の調整後、色相をさらに180°回転して補色側の色にします。\n鮮やかさや明暗ではなく、色の種類を反転する機能です。透明度は変わりません。",
            "彩度の反転\n彩度の調整後、鮮やかさを逆転します。鮮やかな色は無彩色寄りに、くすんだ色は鮮やかになります。\n色相を持たない中間のグレーには赤系の色が付きます。黒・白・透明度は変わりません。",
            "明るさの反転（ネガポジ）\nすべての色調整後、RGBを「255 − RGB」に反転します。明るさを＋にした効果も反転され、暗くなる方向に働きます。\n明るさの調整値が0なら、コントラスト反転と同じ結果です。透明度は変わりません。",
            "コントラストの反転\n明暗の関係を反転してから、明るさの調整値を加えます。明るさが＋なら、その分は明るくなります。\n明るさ反転は「明るさの調整後に反転」する点が異なります。明るさの調整値が0なら同じ結果です。透明度は変わりません。"
        ];
        ColorInversion GetInversions()
        {
            var result = ColorInversion.None;
            for (var index = 0; index < inversionChecks.Length; index++)
                if (inversionChecks[index].IsChecked == true) result |= inversionFlags[index];
            return result;
        }
        var names = new[] { "色相", "彩度", "明るさ", "コントラスト" };
        var ranges = new[] { (-180d, 180d), (-100d, 100d), (-100d, 100d), (-100d, 100d) };
        for (var index = 0; index < sliders.Length; index++)
        {
            sliders[index] = new Slider { Minimum = ranges[index].Item1, Maximum = ranges[index].Item2, Value = 0, TickFrequency = 1, Width = 320 };
            valueLabels[index] = new TextBlock { Text = "0", Width = 42, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            inversionChecks[index] = new CheckBox { Content = "反転", IsChecked = false, VerticalAlignment = VerticalAlignment.Center };
            EditorToolTip.SetTip(inversionChecks[index], inversionTips[index]);
            inversionChecks[index].PropertyChanged += (_, args) =>
            {
                if (args.Property == ToggleButton.IsCheckedProperty && !timer.IsEnabled) timer.Start();
            };
        }
        var rowsPanel = new StackPanel { Spacing = 4, Margin = new Thickness(6, 0) };
        for (var index = 0; index < sliders.Length; index++)
        {
            var captured = index;
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
            row.Children.Add(new TextBlock { Text = names[index], Width = 100, VerticalAlignment = VerticalAlignment.Center });
            row.Children.Add(sliders[index]);
            row.Children.Add(valueLabels[index]);
            row.Children.Add(inversionChecks[index]);
            rowsPanel.Children.Add(row);
            sliders[index].PropertyChanged += (_, args) =>
            {
                if (args.Property != RangeBase.ValueProperty) return;
                values[captured] = (float)sliders[captured].Value;
                valueLabels[captured].Text = values[captured].ToString("0");
                if (!timer.IsEnabled) timer.Start();
            };
        }
        var ok = Button("OK", true);
        var cancel = Button("キャンセル", false, true);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center };
        buttons.Children.Add(ok); buttons.Children.Add(cancel);
        var content = new StackPanel { Spacing = 8, Margin = new Thickness(14) };
        content.Children.Add(new Border { BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1), Child = image });
        content.Children.Add(rowsPanel);
        content.Children.Add(new TextBlock
        {
            Text = "明るさの反転でネガポジ反転できます。透明度は変わりません。",
            FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center, TextWrapping = TextWrapping.Wrap
        });
        content.Children.Add(buttons);
        window.Content = content;
        (float Hue, float Saturation, float Brightness, float Contrast, ColorInversion Inversions)? selected = null;
        var sample = CreateBoundedPreview(preview, 512, 320);
        var initialData = EncodePng(sample);
        using (initialData)
        using (var stream = new MemoryStream(initialData.ToArray())) image.Source = new Bitmap(stream);
        RenderOptions.SetBitmapInterpolationMode(image, BitmapInterpolationMode.None);
        var currentImage = image.Source as Bitmap;
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            var adjusted = CellEditService.AdjustColors(sample, values[0], values[1], values[2], values[3], GetInversions());
            try
            {
                using var data = EncodePng(adjusted);
                using var stream = new MemoryStream(data.ToArray());
                var next = new Bitmap(stream);
                image.Source = next;
                currentImage?.Dispose();
                currentImage = next;
            }
            finally { adjusted.Dispose(); }
        };
        ok.Click += (_, _) => { selected = (values[0], values[1], values[2], values[3], GetInversions()); window.Close(); };
        cancel.Click += (_, _) => window.Close();
        try
        {
            await window.ShowDialog(owner);
            return selected;
        }
        finally
        {
            timer.Stop();
            currentImage?.Dispose();
            sample.Dispose();
        }
    }

    private static SKBitmap CreateBoundedPreview(SKBitmap source, int maxWidth, int maxHeight)
    {
        var scale = Math.Min(1d, Math.Min((double)maxWidth / source.Width, (double)maxHeight / source.Height));
        return ScalePreview(source, Math.Max(1, (int)Math.Round(source.Width * scale)), Math.Max(1, (int)Math.Round(source.Height * scale)));
    }

    private static SKBitmap ScalePreview(SKBitmap source, int width, int height)
    {
        var result = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(result);
        canvas.Clear(SKColors.Transparent);
        using var sourceImage = SKImage.FromBitmap(source);
        canvas.DrawImage(sourceImage, new SKRect(0, 0, width, height), new SKSamplingOptions(SKFilterMode.Nearest));
        canvas.Flush();
        return result;
    }

    private static SKData EncodePng(SKBitmap bitmap) => bitmap.Encode(SKEncodedImageFormat.Png, 100);

    private static Button Button(string text, bool isDefault = false, bool isCancel = false) => new()
    {
        Content = text, MinWidth = 96, HorizontalContentAlignment = HorizontalAlignment.Center,
        IsDefault = isDefault, IsCancel = isCancel
    };
}
