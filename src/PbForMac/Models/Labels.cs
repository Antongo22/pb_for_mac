namespace PbForMac.Models;

/// <summary>Элемент выпадающего списка: значение перечисления и его русская подпись.</summary>
public sealed record Option<T>(T Value, string Label)
{
    public override string ToString() => Label;
}

/// <summary>Русские подписи для перечислений модели.</summary>
public static class Labels
{
    public static readonly IReadOnlyList<Option<ColumnType>> ColumnTypes =
    [
        new(ColumnType.Text, "Текст"),
        new(ColumnType.Integer, "Целое число"),
        new(ColumnType.Decimal, "Десятичное число"),
        new(ColumnType.Date, "Дата"),
        new(ColumnType.Boolean, "Логический"),
    ];

    public static readonly IReadOnlyList<Option<Aggregation>> Aggregations =
    [
        new(Aggregation.Sum, "Сумма"),
        new(Aggregation.Average, "Среднее"),
        new(Aggregation.Count, "Количество"),
        new(Aggregation.DistinctCount, "Кол-во уникальных"),
        new(Aggregation.Min, "Минимум"),
        new(Aggregation.Max, "Максимум"),
    ];

    public static readonly IReadOnlyList<Option<DateGranularity>> DateGranularities =
    [
        new(DateGranularity.Day, "День"),
        new(DateGranularity.Month, "Месяц"),
        new(DateGranularity.Quarter, "Квартал"),
        new(DateGranularity.Year, "Год"),
    ];

    public static readonly IReadOnlyList<Option<FilterOperator>> FilterOperators =
    [
        new(FilterOperator.Equals, "равно"),
        new(FilterOperator.NotEquals, "не равно"),
        new(FilterOperator.GreaterThan, "больше"),
        new(FilterOperator.GreaterOrEqual, "больше или равно"),
        new(FilterOperator.LessThan, "меньше"),
        new(FilterOperator.LessOrEqual, "меньше или равно"),
        new(FilterOperator.Contains, "содержит"),
        new(FilterOperator.NotContains, "не содержит"),
        new(FilterOperator.StartsWith, "начинается с"),
        new(FilterOperator.IsEmpty, "пусто"),
        new(FilterOperator.IsNotEmpty, "не пусто"),
    ];

    public static readonly IReadOnlyList<Option<VisualKind>> VisualKinds =
    [
        new(VisualKind.Column, "Гистограмма"),
        new(VisualKind.Bar, "Линейчатая"),
        new(VisualKind.Line, "График"),
        new(VisualKind.Area, "С областями"),
        new(VisualKind.Pie, "Круговая"),
        new(VisualKind.Scatter, "Точечная"),
        new(VisualKind.Card, "Карточка"),
        new(VisualKind.Table, "Таблица"),
        new(VisualKind.Slicer, "Срез"),
    ];

    public static string Of<T>(T value) where T : struct, Enum => Find(value).Label;

    public static Option<T> Find<T>(T value) where T : struct, Enum
    {
        var list = value switch
        {
            ColumnType => (IEnumerable<Option<T>>)ColumnTypes,
            Aggregation => (IEnumerable<Option<T>>)Aggregations,
            DateGranularity => (IEnumerable<Option<T>>)DateGranularities,
            FilterOperator => (IEnumerable<Option<T>>)FilterOperators,
            VisualKind => (IEnumerable<Option<T>>)VisualKinds,
            _ => [],
        };
        return list.FirstOrDefault(o => EqualityComparer<T>.Default.Equals(o.Value, value))
               ?? new Option<T>(value, value.ToString());
    }

    public static bool OperatorNeedsValue(FilterOperator op) =>
        op is not (FilterOperator.IsEmpty or FilterOperator.IsNotEmpty);

    public static string Describe(FilterDefinition f) => f.Operator switch
    {
        FilterOperator.In => $"{f.Column} ∈ {{{string.Join(", ", f.Values)}}}",
        _ when !OperatorNeedsValue(f.Operator) => $"{f.Column} {Of(f.Operator)}",
        _ => $"{f.Column} {Of(f.Operator)} «{f.Value}»",
    };
}
