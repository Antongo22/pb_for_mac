using System.Text.Json;
using System.Text.Json.Serialization;

namespace PbForMac.Services;

/// <summary>Пользовательские настройки приложения (settings.json в папке данных приложения).</summary>
public sealed class AppSettings
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public AppTheme Theme { get; set; } = AppTheme.System;

    /// <summary>
    /// macOS: ~/Library/Application Support/PbForMac, Windows: %LOCALAPPDATA%\PbForMac,
    /// Linux: ~/.local/share/PbForMac.
    /// </summary>
    [JsonIgnore]
    public string FilePath { get; init; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PbForMac", "settings.json");

    public static AppSettings Load(string? path = null)
    {
        var settings = new AppSettings();
        var file = path ?? settings.FilePath;
        try
        {
            if (File.Exists(file))
                settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(file), Options) ?? settings;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            // Повреждённые или недоступные настройки не должны мешать запуску.
        }
        return new AppSettings { Theme = settings.Theme, FilePath = file };
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Options));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Настройки не критичны: без записи приложение продолжает работать.
        }
    }
}
