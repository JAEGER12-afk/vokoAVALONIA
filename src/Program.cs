using Avalonia;

namespace VokoAvalonia;

/// <summary>Starts the app. You normally never need to change this file.</summary>
class Program
{
    [STAThread]
    public static void Main(string[] args) =>
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    // Also used by the XAML previewer in Visual Studio / Rider / VS Code.
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
                  .UsePlatformDetect()
                  .LogToTrace();
}
