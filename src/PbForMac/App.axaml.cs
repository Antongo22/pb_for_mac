using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using PbForMac.Services;
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

            var settings = AppSettings.Load();
            var theme = new ThemeService(this) { Theme = settings.Theme };

            var window = new MainWindow();
            var viewModel = new MainWindowViewModel(new DialogService(window), theme, settings);
            window.DataContext = viewModel;
            desktop.MainWindow = window;

            // Файлы открываются после показа окна: до этого нельзя показывать диалоги ошибок.
            var opened = false;
            var pending = new List<string>();
            window.Opened += async (_, _) =>
            {
                opened = true;
                foreach (var path in pending)
                    await viewModel.OpenPathAsync(path);
                pending.Clear();
            };

            void Open(string path)
            {
                if (opened)
                    _ = viewModel.OpenPathAsync(path);
                else
                    pending.Add(path);
            }

            // Отчёт или файл данных из командной строки (Windows, Linux, `dotnet run -- file`).
            foreach (var path in desktop.Args?.Where(File.Exists) ?? [])
                Open(path);

            // macOS передаёт файлы, открытые из Finder или «Открыть с помощью», через событие активации.
            if (TryGetFeature(typeof(IActivatableLifetime)) is IActivatableLifetime activatable)
            {
                activatable.Activated += (_, e) =>
                {
                    if (e is FileActivatedEventArgs files)
                    {
                        foreach (var path in files.Files.Select(f => f.TryGetLocalPath()).OfType<string>())
                            Open(path);
                    }
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
