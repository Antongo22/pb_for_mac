using System.Data;
using PbForMac.Models;
using PbForMac.Services.Importers;

namespace PbForMac.Services;

/// <summary>
/// Автопоиск связей «многие к одному» по одноимённым столбцам: на стороне «один» ключи уникальны,
/// а почти все значения стороны «многие» находятся среди этих ключей.
/// </summary>
public static class RelationshipDetector
{
    /// <summary>
    /// Минимальная доля значений стороны «многие», найденных в справочнике. В выгрузках бывают «сироты»
    /// (коды без записи в справочнике), поэтому порог мягкий; от ложных связей защищают
    /// совпадение имён столбцов и уникальность ключей на стороне «один».
    /// </summary>
    public const double MinMatchRate = 0.5;

    public static List<RelationshipDefinition> Detect(
        IReadOnlyList<DataTable> tables, IReadOnlyList<RelationshipDefinition> existing, IEnumerable<string>? onlyTables = null)
    {
        var only = onlyTables?.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var found = new List<RelationshipDefinition>();
        var keyCache = new Dictionary<DataColumn, KeyStats>();

        foreach (var many in tables)
        {
            foreach (var one in tables)
            {
                if (many == one || (only is not null && !only.Contains(many.TableName) && !only.Contains(one.TableName)))
                    continue;
                // Между парой таблиц — не больше одной связи.
                if (existing.Concat(found).Any(r => r.Connects(many.TableName) && r.Connects(one.TableName)))
                    continue;

                RelationshipDefinition? best = null;
                var bestRate = 0.0;
                foreach (DataColumn manyColumn in many.Columns)
                {
                    if (manyColumn.ColumnName == FolderImporter.FileColumn || one.Columns[manyColumn.ColumnName] is not { } oneColumn)
                        continue;
                    if (TypeInference.IsNumeric(manyColumn) != TypeInference.IsNumeric(oneColumn))
                        continue;

                    var oneStats = Stats(oneColumn, keyCache);
                    if (!oneStats.IsUnique || oneStats.Keys.Count == 0)
                        continue;
                    var manyStats = Stats(manyColumn, keyCache);
                    if (manyStats.Keys.Count == 0)
                        continue;

                    var rate = manyStats.Keys.Count(oneStats.Keys.Contains) / (double)manyStats.Keys.Count;
                    if (manyStats.IsUnique)
                    {
                        // Связь 1:1 создаём в одну сторону: туда, где находится больше ключей
                        // (при равенстве — от большей таблицы к меньшей).
                        var reverse = oneStats.Keys.Count(manyStats.Keys.Contains) / (double)oneStats.Keys.Count;
                        var preferReverse = reverse > rate
                                            || (reverse == rate && (many.Rows.Count < one.Rows.Count
                                                                    || (many.Rows.Count == one.Rows.Count
                                                                        && string.Compare(many.TableName, one.TableName, StringComparison.OrdinalIgnoreCase) > 0)));
                        if (preferReverse)
                            continue;
                    }
                    if (rate >= MinMatchRate && rate > bestRate)
                    {
                        bestRate = rate;
                        best = new RelationshipDefinition
                        {
                            FromTable = many.TableName,
                            FromColumn = manyColumn.ColumnName,
                            ToTable = one.TableName,
                            ToColumn = oneColumn.ColumnName,
                        };
                    }
                }
                if (best is not null)
                    found.Add(best);
            }
        }
        return found;
    }

    public static double MatchRate(DataTable many, DataColumn manyColumn, DataTable one, DataColumn oneColumn)
    {
        var cache = new Dictionary<DataColumn, KeyStats>();
        var manyKeys = Stats(manyColumn, cache).Keys;
        if (manyKeys.Count == 0)
            return 0;
        var oneKeys = Stats(oneColumn, cache).Keys;
        return manyKeys.Count(oneKeys.Contains) / (double)manyKeys.Count;
    }

    public static bool HasDuplicateKeys(DataTable table, string column) =>
        table.Columns[column] is { } c && !Stats(c, []).IsUnique;

    private sealed record KeyStats(HashSet<string> Keys, bool IsUnique);

    private static KeyStats Stats(DataColumn column, Dictionary<DataColumn, KeyStats> cache)
    {
        if (cache.TryGetValue(column, out var stats))
            return stats;
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var filled = 0;
        foreach (DataRow row in column.Table!.Rows)
        {
            var key = QueryEngine.Key(row[column]);
            if (key.Length == 0)
                continue;
            filled++;
            keys.Add(key);
        }
        return cache[column] = new KeyStats(keys, keys.Count == filled);
    }
}
