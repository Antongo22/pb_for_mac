namespace PbForMac.Models;

public enum FilterOperator
{
    Equals,
    NotEquals,
    GreaterThan,
    GreaterOrEqual,
    LessThan,
    LessOrEqual,
    Contains,
    NotContains,
    StartsWith,
    IsEmpty,
    IsNotEmpty,
    In,
}

/// <summary>Условие фильтрации строк таблицы.</summary>
public sealed class FilterDefinition
{
    public string Table { get; set; } = "";
    public string Column { get; set; } = "";
    public FilterOperator Operator { get; set; }
    public string? Value { get; set; }

    /// <summary>Список значений для оператора <see cref="FilterOperator.In"/>.</summary>
    public List<string> Values { get; set; } = [];

    public FilterDefinition Clone() => new()
    {
        Table = Table,
        Column = Column,
        Operator = Operator,
        Value = Value,
        Values = [.. Values],
    };
}
