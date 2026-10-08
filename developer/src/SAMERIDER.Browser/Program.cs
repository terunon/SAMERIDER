using System.Runtime.Versioning;
using Avalonia;
using Avalonia.Browser;

[assembly: SupportedOSPlatform("browser")]

namespace SAMERIDER.Browser;

internal static class Program
{
    private static Task Main(string[] args) => BuildAvaloniaApp().StartBrowserAppAsync("out");

    private static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .WithInterFont()
        .LogToTrace();
}
