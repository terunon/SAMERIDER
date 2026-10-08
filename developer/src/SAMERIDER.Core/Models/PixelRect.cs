namespace SAMERIDER.Core.Models;

/// <summary>Asset pixel coordinates; origin is top-left, +X right, +Y down.</summary>
public sealed record PixelRect(int X, int Y, int Width, int Height);

public enum ResizeAnchor
{
    TopLeft, Top, TopRight,
    Left, Center, Right,
    BottomLeft, Bottom, BottomRight
}
