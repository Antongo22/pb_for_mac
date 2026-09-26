using Avalonia;
using Avalonia.Styling;
using PbForMac.Services;

namespace PbForMac.Views;

/// <summary>Тема через <see cref="Application.RequestedThemeVariant"/>; ресурсы с DynamicResource обновляются сразу.</summary>
public sealed class ThemeService : IThemeService
{
    private readonly Application _app;

    public ThemeService(Application app)
    {
        _app = app;
        _app.ActualThemeVariantChanged += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? Changed;

    public AppTheme Theme
    {
        get => _app.RequestedThemeVariant == ThemeVariant.Light ? AppTheme.Light
            : _app.RequestedThemeVariant == ThemeVariant.Dark ? AppTheme.Dark
            : AppTheme.System;
        set => _app.RequestedThemeVariant = value switch
        {
            AppTheme.Light => ThemeVariant.Light,
            AppTheme.Dark => ThemeVariant.Dark,
            _ => ThemeVariant.Default,
        };
    }

    public bool IsDark => _app.ActualThemeVariant == ThemeVariant.Dark;
}
