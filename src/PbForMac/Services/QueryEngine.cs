using System.Data;
using System.Globalization;
using PbForMac.Models;

namespace PbForMac.Services;

/// <summary>Результат агрегации: подписи категорий и по одному ряду значений на каждое поле.</summary>
public sealed record AggregatedResult(IReadOnlyList<string> Categories, IReadOnlyList<string> SeriesNames, IReadOnlyList<double[]> Series)
{
    public static AggregatedResult Empty { get; } = new([], [], []);
}

/// <summary>Фильтрация, группировка и агрегация данных таблиц.</summary>
public static class QueryEngine
{
    private static readonly CultureInfo Ru = CultureInfo.GetCultureInfo("ru-RU");

    public static IEnumerable<DataRow> Filter(DataTable table, IEnumerable<FilterDefinition> filters)
    {
        var predicates = filters
            .Where(f => table.Columns.Contains(f.Column))
            .Select(f => BuildPredicate(table, f))
            .ToList();
        var rows = table.Rows.Cast<DataRow>();
        return predicates.Count == 0 ? rows : rows.Where(r => predicates.All(p => p(r)));
    }

    public static Func<DataRow, bool> BuildPredicate(DataTable table, FilterDefinition filter)
    {
        var column = table.Columns[filter.Column]
                     ?? throw new InvalidOperationException($"Столбец «{filter.Column}» не найден в таблице «{table.TableName}».");
        var index = column.Ordinal;
        var type = TypeInference.FromClr(column.DataType);

        switch (filter.Operator)
        {
            case FilterOperator.IsEmpty:
                return r => TypeInference.IsEmpty(r[index]);
            case FilterOperator.IsNotEmpty:
                return r => !TypeInference.IsEmpty(r[index]);
            case FilterOperator.In:
                var set = filter.Values.ToHashSet(StringComparer.OrdinalIgnoreCase);
                return r => set.Contains(Key(r[index]));
            case FilterOperator.Contains or FilterOperator.NotContains or FilterOperator.StartsWith:
                var text = filter.Value ?? "";
                return filter.Operator switch
                {
                    FilterOperator.Contains => r => Text(r[index]).Contains(text, StringComparison.OrdinalIgnoreCase),
                    FilterOperator.NotContains => r => !Text(r[index]).Contains(text, StringComparison.OrdinalIgnoreCase),
                    _ => r => Text(r[index]).StartsWith(text, StringComparison.OrdinalIgnoreCase),
                };
        }

        var target = TypeInference.Convert(filter.Value, type);
        if (target is DBNull)
        {
            if (type != ColumnType.Text && !TypeInference.IsEmpty(filter.Value))
                throw new InvalidOperationException(
                    $"Значение «{filter.Value}» не подходит для столбца «{filter.Column}» ({Labels.Of(type)}).");
            target = "";
        }

        return filter.Operator switch
        {
            FilterOperator.Equals => r => Compare(r[index], target) == 0,
            FilterOperator.NotEquals => r => Compare(r[index], target) != 0,
            FilterOperator.GreaterThan => r => !TypeInference.IsEmpty(r[index]) && Compare(r[index], target) > 0,
            FilterOperator.GreaterOrEqual => r => !TypeInference.IsEmpty(r[index]) && Compare(r[index], target) >= 0,
            FilterOperator.LessThan => r => !TypeInference.IsEmpty(r[index]) && Compare(r[index], target) < 0,
            FilterOperator.LessOrEqual => r => !TypeInference.IsEmpty(r[index]) && Compare(r[index], target) <= 0,
            _ => _ => true,
        };
    }

    /// <summary>Сравнение значений одного столбца; пустые значения меньше любых других.</summary>
    public static int Compare(object? a, object? b)
    {
        var aEmpty = TypeInference.IsEmpty(a);
        var bEmpty = TypeInference.IsEmpty(b);
        if (aEmpty || bEmpty)
            return aEmpty == bEmpty ? 0 : aEmpty ? -1 : 1;
        if (a is string sa && b is string sb)
            return string.Compare(sa, sb, Ru, CompareOptions.IgnoreCase);
        if (a is IConvertible && b is IConvertible && IsNumber(a) && IsNumber(b))
            return System.Convert.ToDouble(a, CultureInfo.InvariantCulture).CompareTo(System.Convert.ToDouble(b, CultureInfo.InvariantCulture));
        if (a is IComparable ca && a.GetType() == b!.GetType())
            return ca.CompareTo(b);
        return string.Compare(Text(a), Text(b), Ru, CompareOptions.IgnoreCase);
    }

    /// <summary>Строковый ключ значения (для срезов и фильтра «в списке»).</summary>
    public static string Key(object? value) => value switch
    {
        null or DBNull => "",
        DateTime dt => TypeInference.FormatDate(dt),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? "",
    };

    public static double? ToDouble(object? value) => value switch
    {
        double d => d,
        long l => l,
        int i => i,
        float f => f,
        decimal m => (double)m,
        bool b => b ? 1 : 0,
        string s when TypeInference.TryParseDouble(s, out var d) => d,
        _ => null,
    };

    public static double Aggregate(IEnumerable<object?> values, Aggregation aggregation)
    {
        switch (aggregation)
        {
            case Aggregation.Count:
                return values.Count(v => !TypeInference.IsEmpty(v));
            case Aggregation.DistinctCount:
                return values.Where(v => !TypeInference.IsEmpty(v)).Select(Key).Distinct().Count();
        }

        var numbers = values.Select(ToDouble).Where(d => d.HasValue).Select(d => d!.Value).ToList();
        if (numbers.Count == 0)
            return aggregation == Aggregation.Sum ? 0 : double.NaN;
        return aggregation switch
        {
            Aggregation.Sum => numbers.Sum(),
            Aggregation.Average => numbers.Average(),
            Aggregation.Min => numbers.Min(),
            Aggregation.Max => numbers.Max(),
            _ => double.NaN,
        };
    }

    /// <summary>Значение категории с учётом детализации дат.</summary>
    public static object CategoryKey(object? value, DateGranularity granularity) => value switch
    {
        null or DBNull => DBNull.Value,
        DateTime dt => granularity switch
        {
            DateGranularity.Year => new DateTime(dt.Year, 1, 1),
            DateGranularity.Quarter => new DateTime(dt.Year, (dt.Month - 1) / 3 * 3 + 1, 1),
            DateGranularity.Month => new DateTime(dt.Year, dt.Month, 1),
            _ => dt.Date,
        },
        _ => value,
    };

    public static string CategoryLabel(object key, DateGranularity granularity) => key switch
    {
        DBNull => "(пусто)",
        DateTime dt => granularity switch
        {
            DateGranularity.Year => dt.Year.ToString(CultureInfo.InvariantCulture),
            DateGranularity.Quarter => $"Q{(dt.Month - 1) / 3 + 1} {dt.Year}",
            DateGranularity.Month => dt.ToString("MMM yyyy", Ru),
            _ => dt.ToString("dd.MM.yyyy", Ru),
        },
        _ => ValueFormatter.Display(key),
    };

    /// <summary>
    /// Группирует строки по полю категории и агрегирует каждое поле значений.
    /// Даты и числа сортируются по возрастанию, текст — по убыванию первой серии.
    /// </summary>
    public static AggregatedResult AggregateBy(
        DataTable table, IEnumerable<DataRow> rows, string categoryField, IReadOnlyList<string> valueFields,
        Aggregation aggregation, DateGranularity granularity = DateGranularity.Month, int topN = 0)
    {
        var category = table.Columns[categoryField];
        if (category is null)
            return AggregatedResult.Empty;
        var valueColumns = valueFields.Select(f => table.Columns[f]).OfType<DataColumn>().ToList();
        var categoryType = TypeInference.FromClr(category.DataType);

        var groups = rows
            .GroupBy(r => CategoryKey(r[category], granularity))
            .Select(g => new
            {
                Key = g.Key,
                Values = valueColumns.Count == 0
                    ? [(double)g.Count()]
                    : valueColumns.Select(c => Aggregate(g.Select(r => r[c]), aggregation)).ToArray(),
            });

        var ordered = categoryType is ColumnType.Date or ColumnType.Integer or ColumnType.Decimal
            ? groups.OrderBy(g => g.Key, Comparer<object>.Create(Compare)).ToList()
            : groups.OrderByDescending(g => double.IsNaN(g.Values[0]) ? double.MinValue : g.Values[0]).ToList();

        if (topN > 0 && ordered.Count > topN)
        {
            ordered = categoryType == ColumnType.Text
                ? ordered.Take(topN).ToList()
                : ordered.OrderByDescending(g => g.Values[0]).Take(topN).OrderBy(g => g.Key, Comparer<object>.Create(Compare)).ToList();
        }

        var names = valueColumns.Count == 0
            ? ["Количество строк"]
            : valueColumns.Select(c => $"{Labels.Of(aggregation)}: {c.ColumnName}").ToList();
        var series = Enumerable.Range(0, names.Count)
            .Select(i => ordered.Select(g => g.Values[i]).ToArray())
            .ToList();
        return new AggregatedResult(ordered.Select(g => CategoryLabel(g.Key, granularity)).ToList(), names, series);
    }

    /// <summary>Создаёт новую таблицу, сгруппированную по ключевым столбцам.</summary>
    public static DataTable GroupBy(DataTable table, IReadOnlyList<string> keys, IReadOnlyList<AggregationSpec> aggregations, string newName)
    {
        foreach (var name in keys.Concat(aggregations.Select(a => a.Column)))
        {
            if (!table.Columns.Contains(name))
                throw new InvalidOperationException($"Столбец «{name}» не найден в таблице «{table.TableName}».");
        }
        if (keys.Count == 0 && aggregations.Count == 0)
            throw new InvalidOperationException("Выберите столбцы для группировки или агрегации.");

        var result = new DataTable(newName);
        foreach (var key in keys)
            result.Columns.Add(key, table.Columns[key]!.DataType);
        foreach (var spec in aggregations)
        {
            var name = string.IsNullOrWhiteSpace(spec.OutputName) ? $"{Labels.Of(spec.Aggregation)} {spec.Column}" : spec.OutputName;
            var isInteger = spec.Aggregation is Aggregation.Count or Aggregation.DistinctCount;
            result.Columns.Add(Unique(result, name), isInteger ? typeof(long) : typeof(double));
        }

        var keyIndexes = keys.Select(k => table.Columns[k]!.Ordinal).ToArray();
        var groups = table.Rows.Cast<DataRow>()
            .GroupBy(r => string.Join("\u001F", keyIndexes.Select(i => Key(r[i]))));

        result.BeginLoadData();
        foreach (var group in groups)
        {
            var first = group.First();
            var values = new object[result.Columns.Count];
            for (var i = 0; i < keyIndexes.Length; i++)
                values[i] = first[keyIndexes[i]];
            for (var i = 0; i < aggregations.Count; i++)
            {
                var spec = aggregations[i];
                var value = Aggregate(group.Select(r => r[spec.Column]), spec.Aggregation);
                values[keyIndexes.Length + i] = double.IsNaN(value)
                    ? DBNull.Value
                    : result.Columns[keyIndexes.Length + i].DataType == typeof(long) ? (long)value : value;
            }
            result.Rows.Add(values);
        }
        result.EndLoadData();
        return result;
    }

    internal static string Unique(DataTable table, string name)
    {
        var unique = name;
        for (var i = 2; table.Columns.Contains(unique); i++)
            unique = $"{name} {i}";
        return unique;
    }

    private static bool IsNumber(object value) => value is double or long or int or float or decimal or short or byte;

    private static string Text(object? value) => value switch
    {
        null or DBNull => "",
        string s => s,
        _ => ValueFormatter.Display(value),
    };
}
