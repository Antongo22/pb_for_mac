using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using PbForMac.Models;

namespace PbForMac.Services;

/// <summary>Сохранение и загрузка отчёта (*.pbm — JSON).</summary>
public static class ReportSerializer
{
    public const string Extension = ".pbm";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        AllowOutOfOrderMetadataProperties = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static void Save(ReportDefinition report, string path)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        foreach (var source in report.Sources)
            source.RelativePath = Path.GetRelativePath(directory, source.Path).Replace('\\', '/');
        File.WriteAllText(path, Serialize(report));
    }

    public static ReportDefinition Load(string path)
    {
        var report = Deserialize(File.ReadAllText(path));
        var directory = Path.GetDirectoryName(Path.GetFullPath(path))!;

        // Относительный путь имеет приоритет: отчёт можно переносить вместе с данными.
        foreach (var source in report.Sources)
        {
            if (source.RelativePath is null)
                continue;
            var relative = Path.GetFullPath(Path.Combine(directory, source.RelativePath));
            if (File.Exists(relative) || !File.Exists(source.Path))
                source.Path = relative;
        }
        return report;
    }

    public static string Serialize(ReportDefinition report) => JsonSerializer.Serialize(report, Options);

    public static ReportDefinition Deserialize(string json)
    {
        var report = JsonSerializer.Deserialize<ReportDefinition>(json, Options)
                     ?? throw new InvalidDataException("Файл отчёта пуст.");
        if (report.Version > ReportDefinition.CurrentVersion)
            throw new InvalidDataException("Отчёт создан в более новой версии приложения.");
        return report;
    }
}
