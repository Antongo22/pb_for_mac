using System.Data;
using PbForMac.Models;

namespace PbForMac.Services;

/// <summary>Вычисление мер модели в текущем контексте фильтров (как filter context в DAX).</summary>
public sealed class MeasureEngine(DataModel model, ModelQuery query)
{
    public double Evaluate(string measureName, IEnumerable<FilterDefinition> outerFilters)
    {
        var measure = Find(measureName)
            ?? throw new InvalidOperationException($"Мера «{measureName}» не найдена.");
        return Evaluate(measure, outerFilters.ToList(), []);
    }

    public double Evaluate(MeasureDefinition measure, IReadOnlyList<FilterDefinition> outerFilters) =>
        Evaluate(measure, outerFilters, []);

    private double Evaluate(MeasureDefinition measure, IReadOnlyList<FilterDefinition> outerFilters, HashSet<string> stack)
    {
        if (!stack.Add(measure.Name))
            throw new InvalidOperationException($"Циклическая ссылка на меру «{measure.Name}».");
        try
        {
            var expr = MeasureParser.Parse(measure.Expression);
            return Eval(expr, measure.Table, outerFilters, stack);
        }
        finally
        {
            stack.Remove(measure.Name);
        }
    }

    private double Eval(MeasureExpr expr, string homeTable, IReadOnlyList<FilterDefinition> context, HashSet<string> stack) =>
        expr switch
        {
            NumberExpr number => number.Value,
            UnaryNegateExpr neg => -Eval(neg.Inner, homeTable, context, stack),
            BinaryExpr binary => EvalBinary(binary, homeTable, context, stack),
            DivideFnExpr divide => EvalDivide(divide, homeTable, context, stack),
            CountRowsExpr countRows => CountRows(countRows.Table, homeTable, context),
            SumColumnExpr sum => AggregateColumn(Aggregation.Sum, sum.Table, sum.Column, homeTable, context),
            AggregateColumnExpr agg => AggregateColumn(agg.Aggregation, agg.Table, agg.Column, homeTable, context),
            MeasureRefExpr reference => Evaluate(
                Find(reference.MeasureName) ?? throw new InvalidOperationException($"Мера «{reference.MeasureName}» не найдена."),
                context, stack),
            CalculateExpr calculate => Eval(calculate.Inner, homeTable, ApplyModifiers(context, calculate.Modifiers), stack),
            _ => throw new InvalidOperationException("Неизвестный тип выражения меры."),
        };

    private double EvalBinary(BinaryExpr binary, string homeTable, IReadOnlyList<FilterDefinition> context, HashSet<string> stack)
    {
        var left = Eval(binary.Left, homeTable, context, stack);
        var right = Eval(binary.Right, homeTable, context, stack);
        return binary.Op switch
        {
            MeasureBinaryOp.Add => left + right,
            MeasureBinaryOp.Subtract => left - right,
            MeasureBinaryOp.Multiply => left * right,
            MeasureBinaryOp.Divide => right == 0 || double.IsNaN(right) ? double.NaN : left / right,
            _ => throw new InvalidOperationException("Неизвестный оператор."),
        };
    }

    private double EvalDivide(DivideFnExpr divide, string homeTable, IReadOnlyList<FilterDefinition> context, HashSet<string> stack)
    {
        var num = Eval(divide.Numerator, homeTable, context, stack);
        var den = Eval(divide.Denominator, homeTable, context, stack);
        if (den == 0 || double.IsNaN(den) || double.IsInfinity(den))
            return divide.Alternate is null ? double.NaN : Eval(divide.Alternate, homeTable, context, stack);
        return num / den;
    }

    private double CountRows(string? tableHint, string homeTable, IReadOnlyList<FilterDefinition> context)
    {
        var name = string.IsNullOrEmpty(tableHint) ? homeTable : tableHint;
        var table = model.GetTable(name)
            ?? throw new InvalidOperationException($"Таблица «{name}» не найдена.");
        return query.Filter(table, context).Count();
    }

    private double AggregateColumn(
        Aggregation aggregation, string? tableHint, string column, string homeTable, IReadOnlyList<FilterDefinition> context)
    {
        var table = model.GetTable(homeTable)
            ?? throw new InvalidOperationException($"Таблица меры «{homeTable}» не найдена.");
        var reference = string.IsNullOrEmpty(tableHint) || string.Equals(tableHint, homeTable, StringComparison.OrdinalIgnoreCase)
            ? column
            : FieldRef.Format(tableHint, column);
        var field = query.Resolve(table, reference)
            ?? throw new InvalidOperationException($"Столбец «{reference}» недоступен из «{homeTable}».");
        var rows = query.Filter(table, context);
        return QueryEngine.Aggregate(rows.Select(field.Get), aggregation);
    }

    private static List<FilterDefinition> ApplyModifiers(
        IReadOnlyList<FilterDefinition> context, IReadOnlyList<CalculateModifier> modifiers)
    {
        var result = context.ToList();
        foreach (var modifier in modifiers)
        {
            switch (modifier)
            {
                case RemoveFiltersModifier remove:
                    result.RemoveAll(f => MatchesTarget(f, remove.Table, remove.Column));
                    break;
                case FilterEqualsModifier filter when filter.KeepExisting:
                    result.Add(ToEqualsFilter(filter));
                    break;
                case FilterEqualsModifier filter:
                    result.RemoveAll(f => MatchesTarget(f, filter.Table, filter.Column));
                    result.Add(ToEqualsFilter(filter));
                    break;
            }
        }
        return result;
    }

    private static FilterDefinition ToEqualsFilter(FilterEqualsModifier filter) => new()
    {
        Table = filter.Table,
        Column = filter.Column,
        Operator = FilterOperator.Equals,
        Value = filter.Value,
    };

    private static bool MatchesTarget(FilterDefinition filter, string table, string? column)
    {
        if (!string.Equals(filter.Table, table, StringComparison.OrdinalIgnoreCase))
            return false;
        if (column is null)
            return true;
        if (string.Equals(filter.Column, column, StringComparison.OrdinalIgnoreCase))
            return true;
        return FieldRef.TryParse(filter.Column, out _, out var parsedColumn)
               && string.Equals(parsedColumn, column, StringComparison.OrdinalIgnoreCase);
    }

    private MeasureDefinition? Find(string name) =>
        model.Measures.FirstOrDefault(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase));
}
