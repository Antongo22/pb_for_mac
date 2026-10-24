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
        var expr = MeasureParser.Parse(measure.Expression);
        return Eval(expr, measure.Table, outerFilters, stack);
    }

    private double Eval(MeasureExpr expr, string homeTable, IReadOnlyList<FilterDefinition> context, HashSet<string> stack) =>
        expr switch
        {
            SumColumnExpr sum => AggregateColumn(Aggregation.Sum, sum.Table, sum.Column, homeTable, context),
            AggregateColumnExpr agg => AggregateColumn(agg.Aggregation, agg.Table, agg.Column, homeTable, context),
            MeasureRefExpr reference => Evaluate(
                Find(reference.MeasureName) ?? throw new InvalidOperationException($"Мера «{reference.MeasureName}» не найдена."),
                context, stack),
            CalculateExpr calculate => Eval(calculate.Inner, homeTable, ApplyKeepFilters(context, calculate.KeepFilters), stack),
            _ => throw new InvalidOperationException("Неизвестный тип выражения меры."),
        };

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

    private static List<FilterDefinition> ApplyKeepFilters(
        IReadOnlyList<FilterDefinition> context, IReadOnlyList<KeepFiltersClause> clauses)
    {
        var result = context.ToList();
        foreach (var clause in clauses)
        {
            // KEEPFILTERS: добавить фильтр (пересечение через AND с уже действующими).
            result.Add(new FilterDefinition
            {
                Table = clause.Table,
                Column = clause.Column,
                Operator = FilterOperator.Equals,
                Value = clause.Value,
            });
        }
        return result;
    }

    private MeasureDefinition? Find(string name) =>
        model.Measures.FirstOrDefault(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase));
}
