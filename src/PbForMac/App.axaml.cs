using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using PbForMac.ViewModels;
using PbForMac.Views;

namespace PbForMac;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Avoid duplicate validations from both Avalonia and the CommunityToolkit.
            DisableAvaloniaDataAnnotationValidation();

            var window = new MainWindow();
            var viewModel = new MainWindowViewModel(new DialogService(window));
            window.DataContext = viewModel;
            desktop.MainWindow = window;

            // Отчёт или файл данных, переданный в командной строке.
            var path = desktop.Args?.FirstOrDefault(File.Exists);
            if (path is not null)
            {
                window.Opened += async (_, _) =>
                {
                    if (path.EndsWith(Services.ReportSerializer.Extension, StringComparison.OrdinalIgnoreCase))
                        await viewModel.OpenReportFileAsync(path);
                    else
                        await viewModel.ImportFileAsync(path);
                };
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static void DisableAvaloniaDataAnnotationValidation()
    {
        var dataValidationPluginsToRemove =
            BindingPlugins.DataValidators.OfType<DataAnnotationsValidationPlugin>().ToArray();
        foreach (var plugin in dataValidationPluginsToRemove)
            BindingPlugins.DataValidators.Remove(plugin);
    }
}
