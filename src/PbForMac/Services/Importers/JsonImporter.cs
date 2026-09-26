using System.Data;
using System.Text.Json;
using PbForMac.Models;

namespace PbForMac.Services.Importers;

/// <summary>
/// JSON: массив объектов, либо объект, внутри которого есть массив объектов.
/// Вложенные объекты разворачиваются в столбцы вида «address.city».
/// </summary>
public sealed class JsonImporter : IDataImporter
{
    public SourceKind Kind => SourceKind.Json;

    public IReadOnlyList<string> ListItems(string path) => [];

    public DataTable Import(string path, string? item)
    {
        using var doc = JsonDocument.Parse(CsvImporter.ReadText(path), new JsonDocumentOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip,
        });

        var rows = FindRows(doc.RootElement) ?? [doc.RootElement];
        var records = rows.Select(r =>
        {
            var record = new List<KeyValuePair<string, object?>>();
            Flatten(r, r.ValueKind == JsonValueKind.Object ? "" : "value", record);
            return record;
        }).ToList();

        var table = new DataTable(Path.GetFileNameWithoutExtension(path));
        foreach (var name in records.SelectMany(r => r.Select(kv => kv.Key)).Distinct())
            table.Columns.Add(name, typeof(object));

        table.BeginLoadData();
        foreach (var record in records)
        {
            var row = table.NewRow();
            foreach (var (key, value) in record)
                row[key] = value ?? DBNull.Value;
            table.Rows.Add(row);
        }
        table.EndLoadData();
        return table;
    }

    /// <summary>Ищет самый «длинный» массив объектов (корень или вложенный).</summary>
    private static List<JsonElement>? FindRows(JsonElement element)
    {
        List<JsonElement>? best = null;
        Visit(element, 0);
        return best;

        void Visit(JsonElement e, int depth)
        {
            if (depth > 4)
                return;
            if (e.ValueKind == JsonValueKind.Array)
            {
                var items = e.EnumerateArray().ToList();
                if (items.Count > 0 && items.All(i => i.ValueKind == JsonValueKind.Object) && items.Count > (best?.Count ?? 0))
                    best = items;
                else if (depth == 0 && items.Count > 0 && best is null)
                    best = items; // корневой массив примитивов — одна колонка «value»
            }
            else if (e.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in e.EnumerateObject())
                    Visit(property.Value, depth + 1);
            }
        }
    }

    private static void Flatten(JsonElement e, string prefix, List<KeyValuePair<string, object?>> record)
    {
        switch (e.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var p in e.EnumerateObject())
                    Flatten(p.Value, prefix.Length == 0 ? p.Name : $"{prefix}.{p.Name}", record);
                break;
            case JsonValueKind.Array:
                var items = e.EnumerateArray().ToList();
                var value = items.All(i => i.ValueKind is not (JsonValueKind.Object or JsonValueKind.Array))
                    ? string.Join(", ", items.Select(i => ToValue(i)?.ToString()))
                    : e.GetRawText();
                record.Add(new(prefix, value));
                break;
            default:
                record.Add(new(prefix, ToValue(e)));
                break;
        }
    }

    private static object? ToValue(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.String => e.GetString(),
        JsonValueKind.Number => e.TryGetInt64(out var l) ? l : e.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null or JsonValueKind.Undefined => null,
        _ => e.GetRawText(),
    };
}
