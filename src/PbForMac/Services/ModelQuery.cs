using System.Data;
using PbForMac.Models;

namespace PbForMac.Services;

/// <summary>
/// Запросы к модели с учётом связей: поля связанных таблиц («Таблица[Столбец]») и фильтры,
/// которые распространяются по связям (по умолчанию от «один» к «многие»; при Both — в обе стороны).
/// </summary>
public sealed class ModelQuery(DataModel model)
{
    private readonly Dictionary<RelationshipDefinition, Dictionary<string, DataRow>> _lookups = [];
    private readonly Dictionary<RelationshipDefinition, Dictionary<string, DataRow>> _reverseLookups = [];

    public DataModel Model => model;

    /// <summary>Работающие связи: обе таблицы и оба столбца существуют.</summary>
    private IEnumerable<RelationshipDefinition> UsableRelationships => model.Relationships.Where(r =>
        model.GetTable(r.FromTable)?.Columns.Contains(r.FromColumn) == true
        && model.GetTable(r.ToTable)?.Columns.Contains(r.ToColumn) == true);

    private readonly record struct EdgeHop(RelationshipDefinition Relationship, bool Forward);

    /// <summary>
    /// Кратчайший путь по связям от from к to. Вперёд — всегда From→To;
    /// назад — если FilterDirection.Both (или для доступа к полям при 1:1).
    /// </summary>
    public IReadOnlyList<RelationshipDefinition>? FindPath(string from, string to)
    {
        if (string.Equals(from, to, StringComparison.OrdinalIgnoreCase))
            return [];

        var hops = BuildHops().ToList();
        var previous = new Dictionary<string, EdgeHop>(StringComparer.OrdinalIgnoreCase);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { from };
        var queue = new Queue<string>([from]);
        while (queue.Count > 0)
        {
            var table = queue.Dequeue();
            foreach (var hop in hops.Where(h =>
                         string.Equals(Start(h), table, StringComparison.OrdinalIgnoreCase)))
            {
                var next = End(hop);
                if (!visited.Add(next))
                    continue;
                previous[next] = hop;
                if (string.Equals(next, to, StringComparison.OrdinalIgnoreCase))
                {
                    var path = new List<RelationshipDefinition>();
                    for (var current = next; previous.TryGetValue(current, out var step); current = Start(step))
                        path.Insert(0, step.Relationship);
                    return path;
                }
                queue.Enqueue(next);
            }
        }
        return null;
    }

    /// <summary>Путь с направлением каждого шага (для навигации в обе стороны).</summary>
    private IReadOnlyList<EdgeHop>? FindHopPath(string from, string to)
    {
        if (string.Equals(from, to, StringComparison.OrdinalIgnoreCase))
            return [];

        var hops = BuildHops().ToList();
        var previous = new Dictionary<string, EdgeHop>(StringComparer.OrdinalIgnoreCase);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { from };
        var queue = new Queue<string>([from]);
        while (queue.Count > 0)
        {
            var table = queue.Dequeue();
            foreach (var hop in hops.Where(h =>
                         string.Equals(Start(h), table, StringComparison.OrdinalIgnoreCase)))
            {
                var next = End(hop);
                if (!visited.Add(next))
                    continue;
                previous[next] = hop;
                if (string.Equals(next, to, StringComparison.OrdinalIgnoreCase))
                {
                    var path = new List<EdgeHop>();
                    for (var current = next; previous.TryGetValue(current, out var step); current = Start(step))
                        path.Insert(0, step);
                    return path;
                }
                queue.Enqueue(next);
            }
        }
        return null;
    }

    private IEnumerable<EdgeHop> BuildHops()
    {
        foreach (var r in UsableRelationships)
        {
            yield return new EdgeHop(r, Forward: true);
            if (r.FilterDirection == FilterDirection.Both || r.Cardinality == RelationshipCardinality.OneToOne)
                yield return new EdgeHop(r, Forward: false);
        }
    }

    private static string Start(EdgeHop hop) => hop.Forward ? hop.Relationship.FromTable : hop.Relationship.ToTable;
    private static string End(EdgeHop hop) => hop.Forward ? hop.Relationship.ToTable : hop.Relationship.FromTable;

    /// <summary>Таблицы, до которых можно дойти по связям от указанной, в порядке близости.</summary>
    public IReadOnlyList<DataTable> RelatedTables(DataTable table) =>
        model.Tables.Where(t => t != table && FindPath(table.TableName, t.TableName) is not null)
            .OrderBy(t => FindPath(table.TableName, t.TableName)!.Count)
            .ToList();

    /// <summary>Поля для визуала таблицы: её столбцы и столбцы связанных таблиц («Таблица[Столбец]»).</summary>
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
        if (target?.Columns[columnName] is not { } targetColumn || FindHopPath(table.TableName, target.TableName) is not { } path)
            return null;
        var navigate = Navigator(path);
        var ordinal = targetColumn.Ordinal;
        return new ResolvedField(reference, targetColumn.ColumnName, targetColumn.DataType,
            r => navigate(r) is { } related ? related[ordinal] : DBNull.Value);
    }

    /// <summary>
    /// Строки таблицы, прошедшие фильтры. Фильтр своей таблицы проверяется напрямую, фильтр связанной
    /// — по строке, найденной через связи. Фильтры несвязанных таблиц не действуют.
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

            if (FindHopPath(table.TableName, filterTable.TableName) is not { } path || Resolve(filterTable, filter.Column) is not { } remoteField)
                continue;
            var navigate = Navigator(path);
            var test = QueryEngine.BuildPredicate(remoteField, filter);
            predicates.Add(r => navigate(r) is { } related && test(related));
        }

        var rows = table.Rows.Cast<DataRow>();
        return predicates.Count == 0 ? rows : rows.Where(r => predicates.All(p => p(r)));
    }

    private Func<DataRow, DataRow?> Navigator(IReadOnlyList<EdgeHop> path)
    {
        var hops = path.Select(hop =>
        {
            var r = hop.Relationship;
            if (hop.Forward)
            {
                var ordinal = model.GetTable(r.FromTable)!.Columns[r.FromColumn]!.Ordinal;
                return (Ordinal: ordinal, Lookup: Lookup(r));
            }
            var revOrdinal = model.GetTable(r.ToTable)!.Columns[r.ToColumn]!.Ordinal;
            return (Ordinal: revOrdinal, Lookup: ReverseLookup(r));
        }).ToArray();

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

    /// <summary>Обратный справочник: ключ стороны «один» → первая строка стороны «многие».</summary>
    private Dictionary<string, DataRow> ReverseLookup(RelationshipDefinition relationship)
    {
        if (_reverseLookups.TryGetValue(relationship, out var lookup))
            return lookup;
        var table = model.GetTable(relationship.FromTable)!;
        var ordinal = table.Columns[relationship.FromColumn]!.Ordinal;
        lookup = new Dictionary<string, DataRow>(StringComparer.OrdinalIgnoreCase);
        foreach (DataRow row in table.Rows)
        {
            var key = QueryEngine.Key(row[ordinal]);
            if (key.Length > 0)
                lookup.TryAdd(key, row);
        }
        return _reverseLookups[relationship] = lookup;
    }
}
