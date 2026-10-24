namespace PbForMac.Models;

/// <summary>
/// Именованная мера модели (упрощённый DAX): агрегаты, арифметика, CALCULATE-модификаторы.
/// </summary>
public sealed class MeasureDefinition
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    /// <summary>Домашняя таблица (обычно факт), в контексте которой считается мера.</summary>
    public string Table { get; set; } = "";
    /// <summary>Текст выражения, например CALCULATE([Total Revenue], KEEPFILTERS(Products[Brand] = "Confecta")).</summary>
    public string Expression { get; set; } = "";
}

/// <summary>Ссылка на меру в полях визуала: «:Имя меры».</summary>
public static class MeasureRef
{
    public const string PaneTable = "Меры";
    public const char Prefix = ':';

    public static string Format(string name) => $"{Prefix}{name}";

    public static bool TryParse(string? reference, out string name)
    {
        name = "";
        if (string.IsNullOrEmpty(reference) || reference[0] != Prefix)
            return false;
        name = reference[1..];
        return name.Length > 0;
    }

    public static string Display(string reference) =>
        TryParse(reference, out var name) ? name : reference;

    public static bool IsMeasure(string? reference) => TryParse(reference, out _);
}

/// <summary>Разобранное выражение меры (внутреннее AST).</summary>
public abstract class MeasureExpr;

public sealed class NumberExpr : MeasureExpr
{
    public double Value { get; init; }
}

public sealed class UnaryNegateExpr : MeasureExpr
{
    public MeasureExpr Inner { get; init; } = null!;
}

public enum MeasureBinaryOp
{
    Add,
    Subtract,
    Multiply,
    Divide,
}

public sealed class BinaryExpr : MeasureExpr
{
    public MeasureExpr Left { get; init; } = null!;
    public MeasureBinaryOp Op { get; init; }
    public MeasureExpr Right { get; init; } = null!;
}

public sealed class DivideFnExpr : MeasureExpr
{
    public MeasureExpr Numerator { get; init; } = null!;
    public MeasureExpr Denominator { get; init; } = null!;
    public MeasureExpr? Alternate { get; init; }
}

public sealed class CountRowsExpr : MeasureExpr
{
    /// <summary>null — домашняя таблица меры.</summary>
    public string? Table { get; init; }
}

public sealed class SumColumnExpr : MeasureExpr
{
    public string? Table { get; init; }
    public string Column { get; init; } = "";
}

public sealed class AggregateColumnExpr : MeasureExpr
{
    public Aggregation Aggregation { get; init; }
    public string? Table { get; init; }
    public string Column { get; init; } = "";
}

public sealed class MeasureRefExpr : MeasureExpr
{
    public string MeasureName { get; init; } = "";
}

public sealed class CalculateExpr : MeasureExpr
{
    public MeasureExpr Inner { get; init; } = null!;
    public List<CalculateModifier> Modifiers { get; init; } = [];
}

public abstract class CalculateModifier;

/// <summary>Фильтр Table[Col] = value; KeepExisting = KEEPFILTERS (пересечение), иначе замена.</summary>
public sealed class FilterEqualsModifier : CalculateModifier
{
    public bool KeepExisting { get; init; }
    public string Table { get; init; } = "";
    public string Column { get; init; } = "";
    public string Value { get; init; } = "";
}

/// <summary>ALL / REMOVEFILTERS — снять фильтры с таблицы или столбца.</summary>
public sealed class RemoveFiltersModifier : CalculateModifier
{
    public string Table { get; init; } = "";
    public string? Column { get; init; }
}
