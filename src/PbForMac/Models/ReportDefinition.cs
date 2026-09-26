namespace PbForMac.Models;

/// <summary>Файл отчёта (*.pbm): источники, шаги преобразований, визуалы и фильтры.</summary>
public sealed class ReportDefinition
{
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;
    public List<DataSourceDefinition> Sources { get; set; } = [];
    public List<TransformStep> Steps { get; set; } = [];
    public List<VisualDefinition> Visuals { get; set; } = [];
    public List<FilterDefinition> Filters { get; set; } = [];
}
