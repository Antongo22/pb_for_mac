using Avalonia;
using Avalonia.Headless;
using PbForMac.Tests;

[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]

namespace PbForMac.Tests;

/// <summary>Приложение для UI-тестов с [AvaloniaFact]: настоящие стили и представления без экрана.</summary>
public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
