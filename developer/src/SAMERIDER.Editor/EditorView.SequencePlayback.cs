using System.Diagnostics;
using System.Threading.Channels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using SAMERIDER.Core.Models;
using SAMERIDER.Core.Services;
using SkiaSharp;

namespace SAMERIDER.Editor;

public sealed partial class EditorView
{
    private Button? _sequencePlaybackButton;
    private CancellationTokenSource? _sequencePlaybackCancellation;
    private readonly record struct SequencePreviewFrame((int X, int Y) Position, SpriteCell? Cell, SKBitmap Bitmap);

    private Grid BuildSequencePreviewHeading(TextBlock heading)
    {
        var header = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("*,Auto,Auto"), ColumnSpacing = 8 };
        heading.VerticalAlignment = VerticalAlignment.Center;
        header.Children.Add(heading);
        _sequencePlaybackButton = Button("連番再生", ToggleSequencePlaybackAsync);
        _sequencePlaybackButton.Padding = new Thickness(8, 6);
        Grid.SetColumn(_sequencePlaybackButton, 1);
        header.Children.Add(_sequencePlaybackButton);
        EditorToolTip.SetTip(_sequencePlaybackButton, "選択セルを左上から右へ15fpsで1回再生。未選択または1セル選択時は全体を再生します。");
        var blendMode = new ComboBox
        {
            ItemsSource = new[] { "通常", "加算", "乗算" }, SelectedIndex = 0,
            MinWidth = 80, VerticalAlignment = VerticalAlignment.Center
        };
        blendMode.SelectionChanged += (_, _) =>
        {
            var mode = blendMode.SelectedIndex switch
            {
                1 => BitmapBlendingMode.Plus,
                2 => BitmapBlendingMode.Multiply,
                _ => BitmapBlendingMode.SourceOver
            };
            foreach (var image in new[] { _preview, _previous, _next })
            {
                RenderOptions.SetBitmapBlendingMode(image, mode);
                // Re-record the existing bitmap draw; changing render options alone can leave a static preview cached.
                image.InvalidateVisual();
            }
        };
        EditorToolTip.SetTip(blendMode, "プレビューの合成方法。PNG書き出しや原本画像には影響しません。");
        Grid.SetColumn(blendMode, 2);
        header.Children.Add(blendMode);
        return header;
    }

    private void UpdateSequencePlaybackButton()
    {
        if (_sequencePlaybackButton is null) return;
        var busy = _cellEditInProgress || _imageImportInProgress || _pngExportInProgress;
        if (busy) StopSequencePlayback();
        var stopping = _sequencePlaybackCancellation?.IsCancellationRequested == true;
        _sequencePlaybackButton.Content = _sequencePlaybackCancellation is not null && !stopping ? "再生中止" : "連番再生";
        _sequencePlaybackButton.IsEnabled = !stopping && (_sequencePlaybackCancellation is not null || !busy && _project.Cells.Count > 0);
    }

    private void StopSequencePlayback() => _sequencePlaybackCancellation?.Cancel();

    private async Task ToggleSequencePlaybackAsync()
    {
        if (_sequencePlaybackCancellation is not null) { StopSequencePlayback(); return; }
        if (_cellEditInProgress || _imageImportInProgress || _pngExportInProgress || _project.Cells.Count == 0) return;
        var positions = SpriteSequenceService.GetPositions(_project.Columns, _project.Rows, _selectedCells);
        if (positions.Length == 0) return;
        var project = _project.DeepClone();
        var directory = _projectDirectory;
        var revision = _projectRevision;
        using var cancellation = new CancellationTokenSource();
        var token = cancellation.Token;
        _sequencePlaybackCancellation = cancellation;
        UpdateSequencePlaybackButton();
        var frames = Channel.CreateBounded<SequencePreviewFrame>(new BoundedChannelOptions(3)
        {
            SingleReader = true, SingleWriter = true, FullMode = BoundedChannelFullMode.Wait
        });
        var producer = Task.Run(() => PrepareSequenceFramesAsync(project, directory, positions, frames.Writer, token));
        Exception? failure = null;
        try
        {
            _previewCrosshairPinned = false;
            _previewCrosshairOverlay.IsVisible = false;
            SetImage(_previous, null);
            SetImage(_next, null);
            foreach (var _ in positions)
            {
                var frame = await frames.Reader.ReadAsync(token);
                if (token.IsCancellationRequested || revision != _projectRevision)
                {
                    frame.Bitmap.Dispose();
                    return;
                }
                var clock = Stopwatch.StartNew();
                // CreateBitmap transfers and disposes the SKBitmap; the displayed Avalonia bitmap is replaced/disposed each frame.
                SetImage(_preview, CreateBitmap(frame.Bitmap));
                _noImageLabel.IsVisible = false;
                _previewX.Text = (frame.Position.X + 1).ToString();
                _previewY.Text = (frame.Position.Y + 1).ToString();
                _offsetReadout.Text = $"X {FormatOffset(frame.Cell?.OffsetX ?? 0)}, Y {FormatOffset(frame.Cell?.OffsetY ?? 0)}";
                var remaining = SpriteSequenceService.FrameDuration - clock.Elapsed;
                if (remaining > TimeSpan.Zero) await Task.Delay(remaining, token);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception exception) { failure = exception; }
        finally
        {
            cancellation.Cancel();
            try
            {
                // Keep this session registered until its decoder has stopped, so rapid restarts cannot accumulate workers.
                UpdateSequencePlaybackButton();
                if (this.GetVisualRoot() is not null) RefreshPreview();
                else SetImage(_preview, null);
            }
            finally
            {
                try { await producer; }
                finally
                {
                    while (frames.Reader.TryRead(out var unused)) unused.Bitmap.Dispose();
                    _sequencePlaybackCancellation = null;
                    UpdateSequencePlaybackButton();
                }
            }
        }
        if (failure is not null) await Dialogs.ShowErrorAsync(this, $"連番再生できませんでした。\n{failure.Message}");
    }

    private static async Task PrepareSequenceFramesAsync(SpriteProject project, string directory, (int X, int Y)[] positions,
        ChannelWriter<SequencePreviewFrame> writer, CancellationToken token)
    {
        CachedSourceBitmap? source = null;
        string? sourcePath = null;
        Exception? failure = null;
        try
        {
            var cells = project.Cells.ToDictionary(cell => (cell.X, cell.Y));
            foreach (var position in positions)
            {
                token.ThrowIfCancellationRequested();
                cells.TryGetValue(position, out var cell);
                SKBitmap bitmap;
                if (cell is null)
                {
                    var scale = Math.Min(1f, (float)MAX_CELL_PREVIEW_DIMENSION / Math.Max(project.CellWidth, project.CellHeight));
                    bitmap = new SKBitmap(Math.Max(1, (int)MathF.Round(project.CellWidth * scale)),
                        Math.Max(1, (int)MathF.Round(project.CellHeight * scale)), SKColorType.Bgra8888, SKAlphaType.Premul);
                    bitmap.Erase(SKColors.Transparent);
                }
                else
                {
                    if (sourcePath != cell.AssetPath)
                    {
                        source?.Bitmap.Dispose();
                        source = null;
                        var path = ProjectValidator.ResolveAssetPath(directory, cell.AssetPath);
                        var maximumDimension = GetUiSourceMaximumDimension(path, project.CellWidth, project.CellHeight);
                        var decoded = SpriteImageService.DecodeForPreview(path, maximumDimension);
                        source = new CachedSourceBitmap(decoded.Bitmap, decoded.ScaleX, decoded.ScaleY);
                        sourcePath = cell.AssetPath;
                    }
                    token.ThrowIfCancellationRequested();
                    var currentSource = source!.Value;
                    bitmap = SpriteImageService.RenderCellThumbnail(project, cell, currentSource.Bitmap,
                        MAX_CELL_PREVIEW_DIMENSION, currentSource.ScaleX, currentSource.ScaleY);
                }
                try { await writer.WriteAsync(new SequencePreviewFrame(position, cell, bitmap), token); }
                catch { bitmap.Dispose(); throw; }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception exception) { failure = exception; }
        finally
        {
            source?.Bitmap.Dispose();
            writer.TryComplete(failure);
        }
    }
}
