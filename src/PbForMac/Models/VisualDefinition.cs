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
    Matrix,
    Slicer,
    Shape,
    Text,
    Button,
}

public enum ShapeKind
{
    Rectangle,
    Rounded,
    Ellipse,
    Triangle,
    Line,
}

public enum DesignAlign
{
    Left,
    Center,
    Right,
}

public enum DesignAction
{
    None,
    GoToPage,
    ApplyBookmark,
    ResetSlicers,
}

/// <summary>Настройки визуального элемента дашборда (сохраняются в отчёте).</summary>
public sealed class VisualDefinition
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public VisualKind Kind { get; set; }
    public string Title { get; set; } = "";
    public string? Table { get; set; }

    /// <summary>Поле оси/категории (для точечной диаграммы — ось X; для матрицы — строки).</summary>
    public string? CategoryField { get; set; }

    /// <summary>Поле столбцов матрицы (заголовки колонок сводной таблицы).</summary>
    public string? ColumnField { get; set; }

    /// <summary>Поля значений; каждое поле — отдельная серия.</summary>
    public List<string> ValueFields { get; set; } = [];

    public Aggregation Aggregation { get; set; } = Aggregation.Sum;
    public DateGranularity DateGranularity { get; set; } = DateGranularity.Month;

    /// <summary>Сколько категорий показывать (0 — все).</summary>
    public int TopN { get; set; }

    /// <summary>Показывать легенду на диаграмме.</summary>
    public bool ShowLegend { get; set; } = true;

    /// <summary>Подписи данных на диаграмме.</summary>
    public bool ShowDataLabels { get; set; }

    /// <summary>Выбранные значения среза.</summary>
    public List<string> SelectedValues { get; set; } = [];

    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; } = 420;
    public double Height { get; set; } = 280;

    /// <summary>Текст фигуры, кнопки или текстового блока.</summary>
    public string Text { get; set; } = "";

    public ShapeKind Shape { get; set; } = ShapeKind.Rounded;
    public DesignAlign Align { get; set; } = DesignAlign.Left;
    public DesignAction Action { get; set; } = DesignAction.None;
    /// <summary>Имя страницы или закладки для действия кнопки.</summary>
    public string? ActionTarget { get; set; }

    /// <summary>Заливка #RRGGBB или transparent. Пусто — цвет страницы.</summary>
    public string? Fill { get; set; }
    public string? Stroke { get; set; }
    public string? Foreground { get; set; }
    public double StrokeThickness { get; set; } = 1;
    public double CornerRadius { get; set; } = 4;
    public double Padding { get; set; } = 8;
    public double FontSize { get; set; } = 14;
    public bool Bold { get; set; }
    public bool ShowTitle { get; set; } = true;

    public VisualDefinition Clone() => new()
    {
        Id = Id,
        Kind = Kind,
        Title = Title,
        Table = Table,
        CategoryField = CategoryField,
        ColumnField = ColumnField,
        ValueFields = [..ValueFields],
        Aggregation = Aggregation,
        DateGranularity = DateGranularity,
        TopN = TopN,
        ShowLegend = ShowLegend,
        ShowDataLabels = ShowDataLabels,
        SelectedValues = [..SelectedValues],
        X = X,
        Y = Y,
        Width = Width,
        Height = Height,
        Text = Text,
        Shape = Shape,
        Align = Align,
        Action = Action,
        ActionTarget = ActionTarget,
        Fill = Fill,
        Stroke = Stroke,
        Foreground = Foreground,
        StrokeThickness = StrokeThickness,
        CornerRadius = CornerRadius,
        Padding = Padding,
        FontSize = FontSize,
        Bold = Bold,
        ShowTitle = ShowTitle,
    };
}
