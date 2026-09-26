namespace PbForMac.Models;

/// <summary>Страница отчёта: свой набор визуалов и фильтров.</summary>
public sealed class ReportPage
{
    public string Name { get; set; } = "Страница 1";
    public List<VisualDefinition> Visuals { get; set; } = [];
    public List<FilterDefinition> Filters { get; set; } = [];
}

/// <summary>Файл отчёта (*.pbm): источники, шаги преобразований, визуалы и фильтры.</summary>
public sealed class ReportDefinition
{
    public const int CurrentVersion = 2;

    public int Version { get; set; } = CurrentVersion;
    public List<DataSourceDefinition> Sources { get; set; } = [];
    public List<TransformStep> Steps { get; set; } = [];
    public List<RelationshipDefinition> Relationships { get; set; } = [];
    /// <summary>Позиции таблиц на диаграмме связей (страница «Модель» → «Связи»).</summary>
    public List<TableLayoutDefinition> TableLayouts { get; set; } = [];

    /// <summary>Страницы отчёта (v2+).</summary>
    public List<ReportPage> Pages { get; set; } = [];

    /// <summary>Закладки: снимок срезов и активной страницы.</summary>
    public List<ReportBookmark> Bookmarks { get; set; } = [];

    /// <summary>Устаревшие плоские визуалы (v1); мигрируют в <see cref="Pages"/> при загрузке.</summary>
    public List<VisualDefinition> Visuals { get; set; } = [];

    /// <summary>Устаревшие плоские фильтры (v1).</summary>
    public List<FilterDefinition> Filters { get; set; } = [];
}

/// <summary>Закладка отчёта: выбранные значения срезов и страница.</summary>
public sealed class ReportBookmark
{
    public string Name { get; set; } = "";
    public string? PageName { get; set; }
    public List<SlicerBookmarkState> Slicers { get; set; } = [];
}

/// <summary>Состояние одного среза в закладке.</summary>
public sealed class SlicerBookmarkState
{
    public string VisualId { get; set; } = "";
    public List<string> SelectedValues { get; set; } = [];
}
