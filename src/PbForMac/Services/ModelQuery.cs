using System.Data;
using PbForMac.Models;

namespace PbForMac.Services;

/// <summary>
/// Запросы к модели с учётом связей: поля связанных таблиц («Таблица[Столбец]») и фильтры,
/// которые распространяются по связям от таблицы «один» к таблице «многие» (как в Power BI).
/// Создаётся заново при каждом изменении модели: справочники для поиска по ключу кэшируются.
/// </summary>
public sealed class ModelQuery(DataModel model)
{
    private readonly Dictionary<RelationshipDefinition, Dictionary<string, DataRow>> _lookups = [];

    public DataModel Model => model;

    /// <summary>Работающие связи: обе таблицы и оба столбца существуют.</summary>
    private IEnumerable<RelationshipDefinition> UsableRelationships => model.Relationships.Where(r =>
        model.GetTable(r.FromTable)?.Columns.Contains(r.FromColumn) == true
        && model.GetTable(r.ToTable)?.Columns.Contains(r.ToColumn) == true);

    /// <summary>
    /// Кратчайший путь по связям от таблицы «многие» к таблице «один» (возможно, через промежуточные таблицы).
    /// Пустой путь — та же таблица; null — таблицы не связаны в этом направлении.
    /// </summary>
    public IReadOnlyList<RelationshipDefinition>? FindPath(string from, string to)
    {
        if (string.Equals(from, to, StringComparison.OrdinalIgnoreCase))
            return [];
        var edges = UsableRelationships.ToList();
        var previous = new Dictionary<string, RelationshipDefinition>(StringComparer.OrdinalIgnoreCase);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { from };
        var queue = new Queue<string>([from]);
        while (queue.Count > 0)
        {
            var table = queue.Dequeue();
            foreach (var edge in edges.Where(e => string.Equals(e.FromTable, table, StringComparison.OrdinalIgnoreCase)))
            {
                if (!visited.Add(edge.ToTable))
                    continue;
                previous[edge.ToTable] = edge;
                if (string.Equals(edge.ToTable, to, StringComparison.OrdinalIgnoreCase))
                {
                    var path = new List<RelationshipDefinition>();
                    for (var current = edge.ToTable; previous.TryGetValue(current, out var step); current = step.FromTable)
                        path.Insert(0, step);
                    return path;
                }
                queue.Enqueue(edge.ToTable);
            }
        }
        return null;
    }

    /// <summary>Таблицы, до которых можно дойти по связям от указанной (её справочники), в порядке близости.</summary>
    public IReadOnlyList<DataTable> RelatedTables(DataTable table) =>
        model.Tables.Where(t => t != table && FindPath(table.TableName, t.TableName) is not null)
            .OrderBy(t => FindPath(table.TableName, t.TableName)!.Count)
            .ToList();

    /// <summary>Поля для визуала таблицы: её столбцы и столбцы связанных справочников («Таблица[Столбец]»).</summary>
    public IReadOnlyList<string> AvailableFields(DataTable table) =>
        table.Columns.Cast<DataColumn>().Select(c => c.ColumnName)
            .Concat(RelatedTables(table).SelectMany(t => t.Columns.Cast<DataColumn>().Select(c => FieldRef.Format(t.TableName, c.ColumnName))))
            .ToList();

    /// <summary>Находит поле: столбец самой таблицы или столбец связанной таблицы. null — поле недоступно.</summary>
    public ResolvedField? Resolve(DataTable table, string reference)
    {
        if (table.Columns[reference] is { } own)
            return ResolvedField.FromColumn(own);
        if (!FieldRef.TryParse(reference, out var tableName, out var columnName))
            return null;
        if (string.Equals(tableName, table.TableName, StringComparison.OrdinalIgnoreCase))
            return table.Columns[columnName] is { } column ? ResolvedField.FromColumn(column) with { Ref = reference } : null;

        var target = model.GetTable(tableName);
        if (target?.Columns[columnName] is not { } targetColumn || FindPath(table.TableName, target.TableName) is not { } path)
            return null;
        var navigate = Navigator(path);
        var ordinal = targetColumn.Ordinal;
        return new ResolvedField(reference, targetColumn.ColumnName, targetColumn.DataType,
            r => navigate(r) is { } related ? related[ordinal] : DBNull.Value);
    }

    /// <summary>
    /// Строки таблицы, прошедшие фильтры. Фильтр своей таблицы проверяется напрямую, фильтр связанного
    /// справочника — по строке справочника, найденной через связи. Фильтры несвязанных таблиц не действуют.
    /// </summary>
    public IEnumerable<DataRow> Filter(DataTable table, IEnumerable<FilterDefinition> filters)
    {
        var predicates = new List<Func<DataRow, bool>>();
        foreach (var filter in filters)
        {
            var filterTable = string.IsNullOrEmpty(filter.Table) ? table : model.GetTable(filter.Table);
            if (filterTable is null)
                continue;
            if (filterTable == table)
            {
                if (Resolve(table, filter.Column) is { } field)
                    predicates.Add(QueryEngine.BuildPredicate(field, filter));
                continue;
            }

            if (FindPath(table.TableName, filterTable.TableName) is not { } path || Resolve(filterTable, filter.Column) is not { } remoteField)
                continue;
            var navigate = Navigator(path);
            var test = QueryEngine.BuildPredicate(remoteField, filter);
            predicates.Add(r => navigate(r) is { } related && test(related));
        }

        var rows = table.Rows.Cast<DataRow>();
        return predicates.Count == 0 ? rows : rows.Where(r => predicates.All(p => p(r)));
    }

    /// <summary>Переход от строки таблицы «многие» по цепочке связей к строке последнего справочника.</summary>
    private Func<DataRow, DataRow?> Navigator(IReadOnlyList<RelationshipDefinition> path)
    {
        var hops = path.Select(r => (Ordinal: model.GetTable(r.FromTable)!.Columns[r.FromColumn]!.Ordinal, Lookup: Lookup(r))).ToArray();
        return row =>
        {
            DataRow? current = row;
            foreach (var (ordinal, lookup) in hops)
            {
                var key = QueryEngine.Key(current[ordinal]);
                if (key.Length == 0 || !lookup.TryGetValue(key, out current))
                    return null;
            }
            return current;
        };
    }

    /// <summary>Справочник «ключ → строка» стороны «один»; при повторах ключа берётся первая строка.</summary>
    private Dictionary<string, DataRow> Lookup(RelationshipDefinition relationship)
    {
        if (_lookups.TryGetValue(relationship, out var lookup))
            return lookup;
        var table = model.GetTable(relationship.ToTable)!;
        var ordinal = table.Columns[relationship.ToColumn]!.Ordinal;
        lookup = new Dictionary<string, DataRow>(StringComparer.OrdinalIgnoreCase);
        foreach (DataRow row in table.Rows)
        {
            var key = QueryEngine.Key(row[ordinal]);
            if (key.Length > 0)
                lookup.TryAdd(key, row);
        }
        return _lookups[relationship] = lookup;
    }
}
