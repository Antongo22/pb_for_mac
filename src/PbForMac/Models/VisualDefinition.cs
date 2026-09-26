namespace PbForMac.Models;

public enum VisualKind
{
    Column,
    Bar,
    Line,
    Area,
    Pie,
    Scatter,
    Card,
    Table,
    Slicer,
}

/// <summary>Настройки визуального элемента дашборда (сохраняются в отчёте).</summary>
public sealed class VisualDefinition
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public VisualKind Kind { get; set; }
    public string Title { get; set; } = "";
    public string? Table { get; set; }

    /// <summary>Поле оси/категории (для точечной диаграммы — ось X).</summary>
    public string? CategoryField { get; set; }

    /// <summary>Поля значений; каждое поле — отдельная серия.</summary>
    public List<string> ValueFields { get; set; } = [];

    public Aggregation Aggregation { get; set; } = Aggregation.Sum;
    public DateGranularity DateGranularity { get; set; } = DateGranularity.Month;

    /// <summary>Сколько категорий показывать (0 — все).</summary>
    public int TopN { get; set; }

    /// <summary>Выбранные значения среза.</summary>
    public List<string> SelectedValues { get; set; } = [];

    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; } = 420;
    public double Height { get; set; } = 280;
}
