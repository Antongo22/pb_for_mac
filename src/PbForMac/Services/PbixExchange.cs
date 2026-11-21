using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using PbForMac.Models;

namespace PbForMac.Services;

/// <summary>
/// Чтение .pbix: это zip. Макет отчёта (Report/Layout) можно разобрать,
/// модель VertiPaq внутри DataModel — нет, её пишет только Power BI.
/// </summary>
public static class PbixExchange
{
    public static ReportDefinition Load(string path, out string warning)
    {
        warning = "Файл .pbix хранит модель данных в закрытом формате VertiPaq, PbForMac её не читает. " +
                  "Импортированы страницы и визуалы. Таблицы перенесутся, если сохранить отчёт в Power BI как проект .pbip.";
        using var zip = ZipFile.OpenRead(path);
        var layout = zip.Entries.FirstOrDefault(e =>
            e.FullName.Replace('\\', '/').Equals("Report/Layout", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException("В .pbix нет Report/Layout. Сохраните отчёт в Power BI как проект .pbip.");

        using var stream = layout.Open();
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        var json = Decode(memory.ToArray()).TrimStart('\uFEFF').Trim();
        var root = JsonNode.Parse(json) as JsonObject
                   ?? throw new InvalidDataException("Макет .pbix не похож на JSON.");
        var sections = root["sections"] as JsonArray
                       ?? throw new InvalidDataException("В макете .pbix нет страниц.");

        var report = new ReportDefinition();
        foreach (var section in sections.OfType<JsonObject>())
        {
            var page = new ReportPage
            {
                Name = section["displayName"]?.GetValue<string>() ?? "Страница",
                CanvasWidth = section["width"]?.GetValue<double>() ?? 1280,
                CanvasHeight = section["height"]?.GetValue<double>() ?? 720,
            };
            if (section["visualContainers"] is JsonArray containers)
            {
                foreach (var container in containers.OfType<JsonObject>())
                    page.Visuals.Add(ReadContainer(container));
            }
            report.Pages.Add(page);
        }
        if (report.Pages.Count == 0)
            throw new InvalidDataException("В .pbix нет страниц отчёта.");
        return report;
    }

    private static VisualDefinition ReadContainer(JsonObject container)
    {
        var visual = new VisualDefinition
        {
            X = container["x"]?.GetValue<double>() ?? 0,
            Y = container["y"]?.GetValue<double>() ?? 0,
            Width = container["width"]?.GetValue<double>() ?? 280,
            Height = container["height"]?.GetValue<double>() ?? 180,
        };
        var configText = container["config"]?.GetValue<string>();
        if (configText is null)
            return visual;
        try
        {
            if (JsonNode.Parse(configText) is JsonObject config)
                ApplyConfig(config, visual);
        }
        catch (JsonException)
        {
            // Битый config одного визуала не должен ронять весь файл.
        }
        return visual;
    }

    private static void ApplyConfig(JsonObject config, VisualDefinition visual)
    {
        var single = config["singleVisual"] as JsonObject;
        var type = single?["visualType"]?.GetValue<string>() ?? "";
        visual.Kind = type switch
        {
            "barChart" or "clusteredBarChart" or "stackedBarChart" => VisualKind.Bar,
            "lineChart" => VisualKind.Line,
            "areaChart" or "stackedAreaChart" => VisualKind.Area,
            "pieChart" or "donutChart" => VisualKind.Pie,
            "scatterChart" => VisualKind.Scatter,
            "card" or "cardVisual" => VisualKind.Card,
            "tableEx" or "table" => VisualKind.Table,
            "pivotTable" => VisualKind.Matrix,
            "slicer" => VisualKind.Slicer,
            "shape" => VisualKind.Shape,
            "actionButton" => VisualKind.Button,
            "textbox" => VisualKind.Text,
            _ => VisualKind.Column,
        };
        if (single?["projections"] is not JsonObject projections)
            return;
        foreach (var role in projections)
        {
            if (role.Value is not JsonArray items)
                continue;
            foreach (var item in items.OfType<JsonObject>())
            {
                var queryRef = item["queryRef"]?.GetValue<string>();
                if (string.IsNullOrWhiteSpace(queryRef))
                    continue;
                var (reference, entity) = ParseQueryRef(queryRef);
                visual.Table ??= entity;
                switch (role.Key)
                {
                    case "Category":
                    case "Rows":
                    case "X":
                        visual.CategoryField = reference;
                        break;
                    case "Columns":
                        visual.ColumnField = reference;
                        break;
                    default:
                        if (!visual.ValueFields.Contains(reference))
                            visual.ValueFields.Add(reference);
                        break;
                }
            }
        }
    }

    private static (string Reference, string? Entity) ParseQueryRef(string queryRef)
    {
        var inner = queryRef;
        var open = queryRef.IndexOf('(');
        var close = queryRef.LastIndexOf(')');
        if (open >= 0 && close > open)
            inner = queryRef[(open + 1)..close];
        var dot = inner.IndexOf('.');
        if (dot <= 0)
            return (inner, null);
        var entity = inner[..dot];
        var property = inner[(dot + 1)..];
        return (FieldRef.Format(entity, property), entity);
    }

    private static string Decode(byte[] bytes)
    {
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            return Encoding.Unicode.GetString(bytes);
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return Encoding.UTF8.GetString(bytes);
        // Report/Layout в .pbix почти всегда UTF-16 LE без BOM.
        if (bytes.Length > 4 && bytes[1] == 0 && bytes[3] == 0)
            return Encoding.Unicode.GetString(bytes);
        return Encoding.UTF8.GetString(bytes);
    }
}
