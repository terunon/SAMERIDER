using SkiaSharp;
using SAMERIDER.Core.Models;

namespace SAMERIDER.Core.Services;

public static class CellEditService
{
    private static readonly Lazy<SKRuntimeEffect> SaturationInversionRuntimeEffect = new(() =>
    {
        const string source = """
            half4 main(half4 color)
            {
                float alpha = color.a;
                if (alpha <= 0.00001) return half4(0);
                float3 rgb = clamp(float3(color.rgb) / alpha, 0.0, 1.0);
                float maximum = max(rgb.r, max(rgb.g, rgb.b));
                float minimum = min(rgb.r, min(rgb.g, rgb.b));
                float lightness = (maximum + minimum) * 0.5;
                float chroma = maximum - minimum;
                float capacity = 1.0 - abs(2.0 * lightness - 1.0);
                float reflectedChroma = max(0.0, capacity - chroma);
                float3 reflected;
                if (chroma > 0.00001)
                    reflected = lightness + (rgb - lightness) * (reflectedChroma / chroma);
                else
                    reflected = float3(lightness + reflectedChroma * 0.5,
                                       lightness - reflectedChroma * 0.5,
                                       lightness - reflectedChroma * 0.5);
                return half4(clamp(reflected, 0.0, 1.0) * alpha, alpha);
            }
            """;
        var effect = SKRuntimeEffect.CreateColorFilter(source, out var errors);
        return effect ?? throw new InvalidOperationException($"彩度反転シェーダーを作成できません: {errors}");
    });

    public static bool CanRestoreSize(SpriteProject project, int columns, int rows)
    {
        if (!project.HasSheetResize) return true;
        var width = (long)project.OriginalCellWidth * columns;
        var height = (long)project.OriginalCellHeight * rows;
        return width <= 16_384 && height <= 16_384 && width * height <= ProjectValidator.MaximumSheetPixelCount;
    }

    public static void ResizeSheet(SpriteProject project, int width, int height)
    {
        var next = project.DeepClone();
        if (!next.HasSheetResize)
        {
            next.OriginalCellWidth = next.CellWidth;
            next.OriginalCellHeight = next.CellHeight;
        }
        next.CellWidth = width;
        next.CellHeight = height;
        ProjectValidator.Validate(next);
        project.OriginalCellWidth = next.OriginalCellWidth;
        project.OriginalCellHeight = next.OriginalCellHeight;
        project.CellWidth = width;
        project.CellHeight = height;
    }

    public static void ResetSheetSize(SpriteProject project)
    {
        if (!project.HasSheetResize) return;
        var next = project.DeepClone();
        next.CellWidth = next.OriginalCellWidth;
        next.CellHeight = next.OriginalCellHeight;
        next.OriginalCellWidth = next.OriginalCellHeight = 0;
        ProjectValidator.Validate(next);
        project.CellWidth = next.CellWidth;
        project.CellHeight = next.CellHeight;
        project.OriginalCellWidth = project.OriginalCellHeight = 0;
    }

    public static int SnapCellSize(int value, int count)
    {
        if (value <= 0 || count <= 0) throw new ArgumentOutOfRangeException(nameof(value));
        var step = count % 4 == 0 ? 1 : count % 2 == 0 ? 2 : 4;
        return Math.Max(step, checked((int)Math.Round((double)value / step, MidpointRounding.AwayFromZero) * step));
    }

    public static SKColorFilter CreateColorFilter(float hue, float saturation, float brightness, float contrast,
        ColorInversion inversions = ColorInversion.None)
    {
        ValidateInversions(inversions);
        if (inversions.HasFlag(ColorInversion.Saturation))
        {
            using var beforeSaturationInversion = CreateColorFilter(hue, saturation, 0, 0, inversions & ColorInversion.Hue);
            using var saturationInversion = SaturationInversionRuntimeEffect.Value.ToColorFilter();
            using var afterSaturationInversion = CreateColorFilter(0, 0, brightness, contrast,
                inversions & (ColorInversion.Brightness | ColorInversion.Contrast));
            using var beforeAndInversion = SKColorFilter.CreateCompose(saturationInversion, beforeSaturationInversion);
            return SKColorFilter.CreateCompose(afterSaturationInversion, beforeAndInversion);
        }
        if (inversions.HasFlag(ColorInversion.Hue)) hue += 180;
        var radians = hue * MathF.PI / 180;
        var c = MathF.Cos(radians);
        var s = MathF.Sin(radians);
        float[] rotation =
        [
            .213f + c * .787f - s * .213f, .715f - c * .715f - s * .715f, .072f - c * .072f + s * .928f,
            .213f - c * .213f + s * .143f, .715f + c * .285f + s * .140f, .072f - c * .072f - s * .283f,
            .213f - c * .213f - s * .787f, .715f - c * .715f + s * .715f, .072f + c * .928f + s * .072f
        ];
        var saturationScale = 1 + saturation / 100;
        var contrastScale = (1 + contrast / 100) * (inversions.HasFlag(ColorInversion.Contrast) ? -1 : 1);
        float[] luminance = [.213f, .715f, .072f];
        var matrix = new float[20];
        for (var row = 0; row < 3; row++)
        {
            for (var column = 0; column < 3; column++)
                for (var inner = 0; inner < 3; inner++)
                    matrix[row * 5 + column] += contrastScale *
                        ((1 - saturationScale) * luminance[inner] + (row == inner ? saturationScale : 0)) * rotation[inner * 3 + column];
            matrix[row * 5 + 4] = brightness / 100 + (1 - contrastScale) * 0.5f;
        }
        if (inversions.HasFlag(ColorInversion.Brightness))
        {
            for (var row = 0; row < 3; row++)
            {
                for (var column = 0; column < 3; column++)
                    matrix[row * 5 + column] *= -1;
                matrix[row * 5 + 4] = 1 - matrix[row * 5 + 4];
            }
        }
        matrix[18] = 1;
        return SKColorFilter.CreateColorMatrix(matrix);
    }

    public static SKBitmap AdjustColors(SKBitmap source, float hue, float saturation, float brightness, float contrast,
        ColorInversion inversions = ColorInversion.None)
    {
        ValidateInversions(inversions);
        var output = new SKBitmap(source.Width, source.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(output);
        canvas.Clear(SKColors.Transparent);
        using var filter = CreateColorFilter(hue, saturation, brightness, contrast, inversions);
        using var paint = new SKPaint { ColorFilter = filter };
        canvas.DrawBitmap(source, 0, 0, paint);
        return output;
    }

    private static void ValidateInversions(ColorInversion inversions)
    {
        if ((inversions & ~ColorInversion.All) != 0)
            throw new ArgumentOutOfRangeException(nameof(inversions), inversions, "色反転フラグに未対応の値が含まれています。");
    }

    // Decode each source once, and render directly into a bounded preview instead of exporting a full-resolution sheet.
    public static SKBitmap CreatePreview(SpriteProject project, string directory, IReadOnlySet<(int X, int Y)>? selection = null)
    {
        var cells = project.Cells.Where(cell => selection is null || selection.Contains((cell.X, cell.Y))).ToArray();
        var left = selection is null || cells.Length == 0 ? 0 : cells.Min(cell => cell.X);
        var top = selection is null || cells.Length == 0 ? 0 : cells.Min(cell => cell.Y);
        var columns = selection is null || cells.Length == 0 ? project.Columns : cells.Max(cell => cell.X) - left + 1;
        var rows = selection is null || cells.Length == 0 ? project.Rows : cells.Max(cell => cell.Y) - top + 1;
        var scale = Math.Min(1d, Math.Min(640d / ((long)columns * project.CellWidth), 400d / ((long)rows * project.CellHeight)));
        var width = Math.Max(1, (int)Math.Round(columns * (long)project.CellWidth * scale));
        var height = Math.Max(1, (int)Math.Round(rows * (long)project.CellHeight * scale));
        var output = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
        try
        {
            using var canvas = new SKCanvas(output);
            canvas.Clear(SKColors.Transparent);
            var maximumCellDimension = Math.Max(1, (int)Math.Ceiling(Math.Max(project.CellWidth, project.CellHeight) * scale));
            foreach (var group in cells.GroupBy(cell => cell.AssetPath))
            {
                var decoded = SpriteImageService.DecodeForPreview(ProjectValidator.ResolveAssetPath(directory, group.Key), 1024);
                using var source = decoded.Bitmap;
                foreach (var cell in group)
                {
                    using var thumbnail = SpriteImageService.RenderCellThumbnail(project, cell, source, maximumCellDimension, decoded.ScaleX, decoded.ScaleY);
                    var x = (float)((cell.X - left) * project.CellWidth * scale);
                    var y = (float)((cell.Y - top) * project.CellHeight * scale);
                    canvas.DrawBitmap(thumbnail, new SKRect(x, y, x + (float)(project.CellWidth * scale), y + (float)(project.CellHeight * scale)));
                }
            }
            return output;
        }
        catch { output.Dispose(); throw; }
    }
}
