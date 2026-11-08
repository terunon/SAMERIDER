namespace SAMERIDER.Core.Models;

[Flags]
public enum ColorInversion
{
    None = 0,
    Hue = 1,
    Saturation = 2,
    Brightness = 4,
    Contrast = 8,
    All = Hue | Saturation | Brightness | Contrast
}
