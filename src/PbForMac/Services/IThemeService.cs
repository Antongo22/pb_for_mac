namespace PbForMac.Services;

public enum AppTheme
{
    System,
    Light,
    Dark,
}

/// <summary>Переключение темы оформления.</summary>
public interface IThemeService
{
    /// <summary>Выбранная тема (System — как в операционной системе).</summary>
    AppTheme Theme { get; set; }

    /// <summary>Фактически применённая тема тёмная (с учётом системной).</summary>
    bool IsDark { get; }

    /// <summary>Изменилась фактическая тема (например, пользователь переключил тему в системе).</summary>
    event EventHandler? Changed;
}
