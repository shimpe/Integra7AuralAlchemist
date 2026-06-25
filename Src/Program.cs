using System;
using Avalonia;
using ReactiveUI.Avalonia;
using Serilog;

namespace Integra7AuralAlchemist;

internal sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Console()
            .WriteTo.File("logs/I7AuralAlchemist.log",
                rollingInterval: RollingInterval.Day,
                fileSizeLimitBytes: 1024 * 1024 * 1024)
            .CreateLogger();
        Log.Information("Integra-7 Aural Alchemist is starting.");
        BuildAvaloniaApp()
            .StartWithClassicDesktopLifetime(args);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace()
            // ReactiveUI 23 (via ReactiveUI.Avalonia) replaced the parameterless
            // UseReactiveUI() with a builder-based overload. The empty action is
            // enough: UseReactiveUI internally calls WithAvalonia() and builds,
            // registering the Avalonia main-thread scheduler.
            .UseReactiveUI(_ => { });
    }
}