using Avalonia.Controls;
using Avalonia.Media;

namespace SAMERIDER.Editor;

internal static class EditorToolTip
{
    internal static readonly FontFamily JapaneseFont = new("avares://SAMERIDER.Editor/Assets/Fonts#IPAGothic");

    public static void SetTip(Control? control, string? text)
    {
        if (control is null) return;
        ToolTip.SetTip(control, text is null
            ? null
            : new TextBlock
            {
                Text = text,
                FontFamily = JapaneseFont,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 480
            });
    }
}
