using System.Data;
using PbForMac.Models;

namespace PbForMac.Services;

/// <summary>Автоматическая раскладка карточек таблиц на диаграмме связей (слои слева направо).</summary>
public static class DiagramLayout
{
    public const double CardWidth = 220;
    public const double HeaderHeight = 36;
    public const double RowHeight = 24;
    public const double PadX = 80;
    public const double PadY = 40;
    public const double GapX = 100;
    public const double GapY = 28;

    public static double CardHeight(int columnCount) =>
        HeaderHeight + Math.Max(1, columnCount) * RowHeight + 8;

    /// <summary>
    /// Раскладывает таблицы по слоям: факты слева, справочники правее по цепочке связей.
    /// Несвязанные таблицы — в отдельный правый столбец.
    /// </summary>
    public static Dictionary<string, (double X, double Y)> Arrange(
        IReadOnlyList<DataTable> tables, IReadOnlyList<RelationshipDefinition> relationships)
    {
        var names = tables.Select(t => t.TableName).ToList();
        var columnCounts = tables.ToDictionary(t => t.TableName, t => t.Columns.Count, StringComparer.OrdinalIgnoreCase);
        if (names.Count == 0)
            return new(StringComparer.OrdinalIgnoreCase);

        var outgoing = relationships
            .GroupBy(r => r.FromTable, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Select(r => r.ToTable).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                StringComparer.OrdinalIgnoreCase);
        var incoming = relationships
            .GroupBy(r => r.ToTable, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

        var layer = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in names)
            layer[name] = 0;

        // Итеративно: справочник правее факта.
        for (var pass = 0; pass < names.Count; pass++)
        {
            var changed = false;
            foreach (var relationship in relationships)
            {
                if (!layer.ContainsKey(relationship.FromTable) || !layer.ContainsKey(relationship.ToTable))
                    continue;
                var next = layer[relationship.FromTable] + 1;
                if (layer[relationship.ToTable] < next)
                {
                    layer[relationship.ToTable] = next;
                    changed = true;
                }
            }
            if (!changed)
                break;
        }

        // Таблицы без связей — в слой после максимума.
        var maxConnected = relationships.Count == 0
            ? 0
            : names.Where(n => incoming.ContainsKey(n) || outgoing.ContainsKey(n)).Select(n => layer[n]).DefaultIfEmpty(0).Max();
        foreach (var name in names.Where(n => !incoming.ContainsKey(n) && !outgoing.ContainsKey(n)))
            layer[name] = maxConnected + (relationships.Count == 0 ? 0 : 1);

        var byLayer = names.GroupBy(n => layer[n]).OrderBy(g => g.Key).ToList();
        var result = new Dictionary<string, (double X, double Y)>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in byLayer)
        {
            var x = PadX + group.Key * (CardWidth + GapX);
            double y = PadY;
            // Факты (много исходящих) выше; внутри слоя — по имени.
            foreach (var name in group.OrderByDescending(n => outgoing.GetValueOrDefault(n)?.Count ?? 0).ThenBy(n => n, StringComparer.OrdinalIgnoreCase))
            {
                result[name] = (x, y);
                y += CardHeight(columnCounts.GetValueOrDefault(name)) + GapY;
            }
        }
        return result;
    }

    /// <summary>Точка выхода/входа линии у столбца карточки.</summary>
    public static (double X, double Y) ColumnAnchor(double cardX, double cardY, int columnIndex, bool rightSide)
    {
        var y = cardY + HeaderHeight + columnIndex * RowHeight + RowHeight / 2;
        var x = rightSide ? cardX + CardWidth : cardX;
        return (x, y);
    }
}
