using Avalonia;
using ZapretGui.Core;

namespace ZapretGui;

internal static class Program
{
    private static Mutex? _singleInstance;

    [STAThread]
    public static int Main(string[] args)
    {
        _singleInstance = new Mutex(true, "ZapretGUI_SingleInstance_{4E1B2C83}", out var isFirst);
        if (!isFirst)
        {
            Console.Error.WriteLine("Zapret GUI уже запущен");
            return 1;
        }

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Log.Error($"Необработанная ошибка: {e.ExceptionObject}");
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Log.Error($"Ошибка фоновой задачи: {e.Exception.GetBaseException().Message}");
            e.SetObserved();
        };

        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args, Avalonia.Controls.ShutdownMode.OnExplicitShutdown);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .With(new Win32PlatformOptions { CompositionMode = [Win32CompositionMode.WinUIComposition, Win32CompositionMode.RedirectionSurface] })
            .LogToTrace();
}
