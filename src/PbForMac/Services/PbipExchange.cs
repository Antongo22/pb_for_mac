using System.Data;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using CsvHelper;
using CsvHelper.Configuration;
using PbForMac.Models;

namespace PbForMac.Services;

/// <summary>
/// Проект Power BI (.pbip + папки .Report / .SemanticModel): этот формат открывает Power BI Desktop.
/// Рядом пишется pbformac.json — полный отчёт PbForMac, чтобы наше приложение открыло проект без потерь.
/// </summary>
public static partial class PbipExchange
{
    public const string SidecarSuffix = ".pbformac.json";

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string Save(ReportDefinition report, IReadOnlyList<DataTable> tables, string pbipPath)
    {
        pbipPath = Path.GetFullPath(pbipPath);
        if (!pbipPath.EndsWith(".pbip", StringComparison.OrdinalIgnoreCase))
            pbipPath += ".pbip";

        var directory = Path.GetDirectoryName(pbipPath)!;
        var name = Path.GetFileNameWithoutExtension(pbipPath);
        Directory.CreateDirectory(directory);

        var reportDir = Path.Combine(directory, name + ".Report");
        var modelDir = Path.Combine(directory, name + ".SemanticModel");
        var dataDir = Path.Combine(directory, name + ".Data");
        if (Directory.Exists(reportDir)) Directory.Delete(reportDir, recursive: true);
        if (Directory.Exists(modelDir)) Directory.Delete(modelDir, recursive: true);
        if (Directory.Exists(dataDir)) Directory.Delete(dataDir, recursive: true);
        Directory.CreateDirectory(dataDir);

        var csvPaths = WriteData(tables, dataDir);
        WriteSemanticModel(name, report, tables, csvPaths, modelDir);
        WriteReport(name, report, reportDir);
        File.WriteAllText(Path.Combine(directory, name + SidecarSuffix), ReportSerializer.Serialize(report));
        File.WriteAllText(pbipPath, PbipPointer(name).ToJsonString(Json));

        return "Сохранён проект Power BI. Откройте файл .pbip в Power BI Desktop.\n" +
               "Таблицы выгружены в CSV рядом с проектом, Power Query читает их по абсолютному пути этого компьютера.\n" +
               "Фигуры, кнопки и оформление в Desktop становятся текстовыми блоками. PbForMac откроет этот же .pbip без потерь.";
    }

    public static ReportDefinition Load(string pbipPath, out string? warning)
    {
        pbipPath = Path.GetFullPath(pbipPath);
        var directory = Path.GetDirectoryName(pbipPath)!;
        var name = Path.GetFileNameWithoutExtension(pbipPath);
        var sidecar = Path.Combine(directory, name + SidecarSuffix);
        if (File.Exists(sidecar))
        {
            warning = null;
            return ReportSerializer.Load(sidecar);
        }

        var modelDir = Path.Combine(directory, name + ".SemanticModel");
        var reportDir = Path.Combine(directory, name + ".Report");
        if (!Directory.Exists(modelDir) && !Directory.Exists(reportDir))
            throw new InvalidDataException("Рядом с .pbip нет папок .SemanticModel и .Report.");

        warning = "Проект сохранён в Power BI, без копии PbForMac. Загружены таблицы из CSV, связи, меры и визуалы. Шаги Power Query PbForMac в таком файле не хранятся.";
        return ReadForeignProject(directory, name, modelDir, reportDir);
    }

    private static Dictionary<string, string> WriteData(IReadOnlyList<DataTable> tables, string dataDir)
    {
        var paths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var table in tables)
        {
            var file = Unique(SafeFile(table.TableName), used) + ".csv";
            var path = Path.Combine(dataDir, file);
            WriteCsv(table, path);
            paths[table.TableName] = path;
        }
        return paths;
    }

    private static void WriteCsv(DataTable table, string path)
    {
        using var writer = new StreamWriter(path, false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        using var csv = new CsvWriter(writer, new CsvConfiguration(CultureInfo.InvariantCulture));
        foreach (DataColumn column in table.Columns)
            csv.WriteField(column.ColumnName);
        csv.NextRecord();
        foreach (DataRow row in table.Rows)
        {
            foreach (DataColumn column in table.Columns)
                csv.WriteField(FormatCell(row[column]));
            csv.NextRecord();
        }
    }

    private static string FormatCell(object? value) => value switch
    {
        null or DBNull => "",
        DateTime dt => dt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
        bool b => b ? "TRUE" : "FALSE",
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture) ?? "",
        _ => value?.ToString() ?? "",
    };

    private static void WriteSemanticModel(string name, ReportDefinition report, IReadOnlyList<DataTable> tables,
        Dictionary<string, string> csvPaths, string modelDir)
    {
        var definition = Path.Combine(modelDir, "definition");
        Directory.CreateDirectory(Path.Combine(definition, "tables"));
        WriteJson(Path.Combine(modelDir, "definition.pbism"), new JsonObject
        {
            ["$schema"] = "https://developer.microsoft.com/json-schemas/fabric/item/semanticModel/definitionProperties/1.0.0/schema.json",
            ["version"] = "4.2",
        });
        WriteJson(Path.Combine(modelDir, ".platform"), Platform("SemanticModel", name));

        var database = new StringBuilder();
        database.AppendLine("database");
        database.AppendLine("\tcompatibilityLevel: 1567");
        File.WriteAllText(Path.Combine(definition, "database.tmdl"), database.ToString());

        var model = new StringBuilder();
        model.AppendLine("model Model");
        model.AppendLine("\tculture: en-US");
        model.AppendLine("\tdefaultPowerBIDataSourceVersion: powerBI_V3");
        model.AppendLine();
        var exported = tables.Count == 0
            ? new List<DataTable> { PlaceholderTable() }
            : tables.ToList();
        if (tables.Count == 0)
        {
            var path = Path.Combine(Path.GetDirectoryName(modelDir)!, name + ".Data", "PbForMac.csv");
            WriteCsv(exported[0], path);
            csvPaths["PbForMac"] = path;
        }
        foreach (var table in exported)
        {
            model.Append("ref table ").AppendLine(Ident(table.TableName));
            File.WriteAllText(
                Path.Combine(definition, "tables", SafeFile(table.TableName) + ".tmdl"),
                TableTmdl(table, report.Measures, csvPaths.GetValueOrDefault(table.TableName)));
        }
        File.WriteAllText(Path.Combine(definition, "model.tmdl"), model.ToString());

        if (report.Relationships.Count > 0)
        {
            var relations = new StringBuilder();
            foreach (var relationship in report.Relationships)
            {
                relations.Append("relationship ").AppendLine(Guid.NewGuid().ToString("N"));
                relations.Append("\tfromColumn: ").Append(Ident(relationship.FromTable)).Append('.').AppendLine(Ident(relationship.FromColumn));
                relations.Append("\ttoColumn: ").Append(Ident(relationship.ToTable)).Append('.').AppendLine(Ident(relationship.ToColumn));
                if (relationship.Cardinality == RelationshipCardinality.OneToOne)
                {
                    relations.AppendLine("\tfromCardinality: one");
                    relations.AppendLine("\ttoCardinality: one");
                }
                if (relationship.FilterDirection == FilterDirection.Both)
                    relations.AppendLine("\tcrossFilteringBehavior: bothDirections");
                relations.AppendLine();
            }
            File.WriteAllText(Path.Combine(definition, "relationships.tmdl"), relations.ToString());
        }
    }

    private static DataTable PlaceholderTable()
    {
        var table = new DataTable("PbForMac");
        table.Columns.Add("Info", typeof(string));
        table.Rows.Add("Нет таблиц");
        return table;
    }

    private static string TableTmdl(DataTable table, IReadOnlyList<MeasureDefinition> measures, string? csvPath)
    {
        var text = new StringBuilder();
        text.Append("table ").AppendLine(Ident(table.TableName));
        text.Append("\tlineageTag: ").AppendLine(Guid.NewGuid().ToString());
        text.AppendLine();
        foreach (DataColumn column in table.Columns)
        {
            var type = TypeInference.FromClr(column.DataType);
            text.Append("\tcolumn ").AppendLine(Ident(column.ColumnName));
            text.Append("\t\tdataType: ").AppendLine(TmdlType(type));
            text.Append("\t\tlineageTag: ").AppendLine(Guid.NewGuid().ToString());
            text.Append("\t\tsummarizeBy: ").AppendLine(type is ColumnType.Integer or ColumnType.Decimal ? "sum" : "none");
            text.Append("\t\tsourceColumn: ").AppendLine(Ident(column.ColumnName));
            text.AppendLine();
        }

        foreach (var measure in measures.Where(m => string.Equals(m.Table, table.TableName, StringComparison.OrdinalIgnoreCase)))
        {
            text.Append("\tmeasure ").Append(Ident(measure.Name)).AppendLine(" = ```");
            foreach (var line in measure.Expression.Replace("\r\n", "\n").Split('\n'))
                text.Append("\t\t\t").AppendLine(line);
            text.AppendLine("\t\t\t```");
            text.Append("\t\tlineageTag: ").AppendLine(Guid.NewGuid().ToString());
            text.AppendLine();
        }

        text.Append("\tpartition ").Append(Ident(table.TableName)).AppendLine(" = m");
        text.AppendLine("\t\tmode: import");
        text.AppendLine("\t\tsource = ```");
        foreach (var line in PartitionM(table, csvPath).Split('\n'))
            text.Append("\t\t\t").AppendLine(line);
        text.AppendLine("\t\t\t```");
        text.AppendLine();
        text.AppendLine("\tannotation PBI_ResultType = Table");
        return text.ToString();
    }

    private static string PartitionM(DataTable table, string? csvPath)
    {
        var path = MString(csvPath ?? "");
        var types = string.Join(", ", table.Columns.Cast<DataColumn>().Select(c =>
            $"{{\"{MString(c.ColumnName)}\", {MType(TypeInference.FromClr(c.DataType))}}}"));
        return
            "let\n" +
            $"    Source = Csv.Document(File.Contents(\"{path}\"), [Delimiter=\",\", Encoding=65001, QuoteStyle=QuoteStyle.Csv]),\n" +
            "    Promoted = Table.PromoteHeaders(Source, [PromoteAllScalars=true]),\n" +
            $"    Typed = Table.TransformColumnTypes(Promoted, {{{types}}})\n" +
            "in\n" +
            "    Typed";
    }

    private static string MType(ColumnType type) => type switch
    {
        ColumnType.Integer => "Int64.Type",
        ColumnType.Decimal => "type number",
        ColumnType.Date => "type datetime",
        ColumnType.Boolean => "type logical",
        _ => "type text",
    };

    private static string MString(string value) => value.Replace("\\", "\\\\").Replace("\"", "\"\"");

    private static void WriteReport(string name, ReportDefinition report, string reportDir)
    {
        var definition = Path.Combine(reportDir, "definition");
        var pagesDir = Path.Combine(definition, "pages");
        Directory.CreateDirectory(pagesDir);
        WriteJson(Path.Combine(reportDir, "definition.pbir"), new JsonObject
        {
            ["$schema"] = "https://developer.microsoft.com/json-schemas/fabric/item/report/definitionProperties/2.0.0/schema.json",
            ["version"] = "4.0",
            ["datasetReference"] = new JsonObject
            {
                ["byPath"] = new JsonObject { ["path"] = "../" + name + ".SemanticModel" },
            },
        });
        WriteJson(Path.Combine(reportDir, ".platform"), Platform("Report", name));
        WriteJson(Path.Combine(definition, "version.json"), new JsonObject
        {
            ["$schema"] = "https://developer.microsoft.com/json-schemas/fabric/item/report/definition/versionMetadata/1.0.0/schema.json",
            ["version"] = "2.0.0",
        });
        WriteJson(Path.Combine(definition, "report.json"), new JsonObject
        {
            ["$schema"] = "https://developer.microsoft.com/json-schemas/fabric/item/report/definition/report/3.0.0/schema.json",
        });

        var pages = report.Pages.Count > 0 ? report.Pages : [new ReportPage { Name = "Страница 1" }];
        var order = new JsonArray();
        var pageIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var page in pages)
        {
            var pageId = Unique(Token(page.Name), pageIds);
            order.Add(pageId);
            var pageDir = Path.Combine(pagesDir, pageId);
            Directory.CreateDirectory(Path.Combine(pageDir, "visuals"));
            WriteJson(Path.Combine(pageDir, "page.json"), new JsonObject
            {
                ["$schema"] = "https://developer.microsoft.com/json-schemas/fabric/item/report/definition/page/2.0.0/schema.json",
                ["name"] = pageId,
                ["displayName"] = page.Name,
                ["displayOption"] = "FitToPage",
                ["width"] = page.CanvasWidth <= 0 ? 1280 : page.CanvasWidth,
                ["height"] = page.CanvasHeight <= 0 ? 720 : page.CanvasHeight,
            });
            var z = 0;
            foreach (var visual in page.Visuals)
                WriteJson(Path.Combine(pageDir, "visuals", Token(visual.Id), "visual.json"), VisualJson(visual, report.Measures, z++));
        }

        WriteJson(Path.Combine(pagesDir, "pages.json"), new JsonObject
        {
            ["$schema"] = "https://developer.microsoft.com/json-schemas/fabric/item/report/definition/pagesMetadata/1.0.0/schema.json",
            ["pageOrder"] = order,
            ["activePageName"] = order.Count > 0 ? order[0]!.GetValue<string>() : "Page1",
        });
    }

    private static JsonObject VisualJson(VisualDefinition visual, IReadOnlyList<MeasureDefinition> measures, int index)
    {
        var node = new JsonObject
        {
            ["$schema"] = "https://developer.microsoft.com/json-schemas/fabric/item/report/definition/visualContainer/2.4.0/schema.json",
            ["name"] = Token(visual.Id),
            ["position"] = new JsonObject
            {
                ["x"] = visual.X,
                ["y"] = visual.Y,
                ["z"] = 1000 + index,
                ["width"] = visual.Width,
                ["height"] = visual.Height,
                ["tabOrder"] = index,
            },
        };

        if (visual.Kind is VisualKind.Text or VisualKind.Shape or VisualKind.Button)
            node["visual"] = Textbox(visual);
        else
        {
            var body = new JsonObject
            {
                ["visualType"] = PbiType(visual.Kind),
                ["query"] = new JsonObject { ["queryState"] = QueryState(visual, measures) },
                ["drillFilterOtherVisuals"] = true,
            };
            var chrome = Chrome(visual);
            if (chrome.Count > 0)
                body["visualContainerObjects"] = chrome;
            node["visual"] = body;
        }
        return node;
    }

    private static JsonObject Textbox(VisualDefinition visual)
    {
        var text = string.IsNullOrWhiteSpace(visual.Text) ? visual.Title : visual.Text;
        if (string.IsNullOrWhiteSpace(text))
            text = visual.Kind switch
            {
                VisualKind.Button => "Кнопка",
                VisualKind.Shape => "",
                _ => "Текст",
            };
        var style = new JsonObject
        {
            ["fontFamily"] = "Segoe UI",
            ["fontSize"] = (visual.FontSize <= 0 ? 14 : visual.FontSize).ToString("0", CultureInfo.InvariantCulture) + "pt",
        };
        if (visual.Bold)
            style["fontWeight"] = "bold";
        if (!string.IsNullOrWhiteSpace(visual.Foreground))
            style["color"] = visual.Foreground;

        var paragraph = new JsonObject
        {
            ["textRuns"] = new JsonArray
            {
                new JsonObject { ["value"] = text, ["textStyle"] = style },
            },
        };
        if (visual.Align == DesignAlign.Center)
            paragraph["horizontalTextAlignment"] = "center";
        else if (visual.Align == DesignAlign.Right)
            paragraph["horizontalTextAlignment"] = "right";

        return new JsonObject
        {
            ["visualType"] = "textbox",
            ["objects"] = new JsonObject
            {
                ["general"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["properties"] = new JsonObject
                        {
                            ["paragraphs"] = new JsonArray { paragraph },
                        },
                    },
                },
            },
            ["visualContainerObjects"] = Chrome(visual, design: true),
            ["drillFilterOtherVisuals"] = false,
        };
    }

    private static JsonObject Chrome(VisualDefinition visual, bool design = false)
    {
        var chrome = new JsonObject();
        var showTitle = !design && visual.ShowTitle && !string.IsNullOrWhiteSpace(visual.Title);
        chrome["title"] = new JsonArray
        {
            new JsonObject
            {
                ["properties"] = new JsonObject
                {
                    ["show"] = Literal(showTitle ? "true" : "false"),
                    ["text"] = Literal(DaxString(visual.Title)),
                },
            },
        };
        if (!string.IsNullOrWhiteSpace(visual.Fill))
        {
            var transparent = visual.Fill.Equals("transparent", StringComparison.OrdinalIgnoreCase);
            var background = new JsonObject { ["show"] = Literal(transparent ? "false" : "true") };
            if (!transparent)
                background["color"] = Solid(visual.Fill);
            chrome["background"] = new JsonArray { new JsonObject { ["properties"] = background } };
        }
        return chrome;
    }

    private static JsonObject QueryState(VisualDefinition visual, IReadOnlyList<MeasureDefinition> measures)
    {
        var state = new JsonObject();
        var table = visual.Table ?? "";
        switch (visual.Kind)
        {
            case VisualKind.Matrix:
                AddRole(state, "Rows", Projections(table, One(visual.CategoryField), measures, aggregate: false));
                AddRole(state, "Columns", Projections(table, One(visual.ColumnField), measures, aggregate: false));
                AddRole(state, "Values", Projections(table, visual.ValueFields, measures, aggregate: true, visual.Aggregation));
                break;
            case VisualKind.Scatter:
                AddRole(state, "X", Projections(table, One(visual.CategoryField), measures, aggregate: false));
                AddRole(state, "Y", Projections(table, visual.ValueFields, measures, aggregate: false));
                break;
            case VisualKind.Card:
                AddRole(state, "Values", Projections(table, visual.ValueFields, measures, aggregate: true, visual.Aggregation));
                break;
            case VisualKind.Table:
                var fields = visual.ValueFields.Count > 0 ? visual.ValueFields : [];
                AddRole(state, "Values", Projections(table, fields, measures, aggregate: false));
                break;
            case VisualKind.Slicer:
                AddRole(state, "Values", Projections(table, One(visual.CategoryField), measures, aggregate: false));
                break;
            default:
                AddRole(state, "Category", Projections(table, One(visual.CategoryField), measures, aggregate: false));
                AddRole(state, "Y", Projections(table, visual.ValueFields, measures, aggregate: true, visual.Aggregation));
                break;
        }
        return state;
    }

    private static List<string> One(string? field) => string.IsNullOrWhiteSpace(field) ? [] : [field];

    private static void AddRole(JsonObject state, string role, JsonArray projections)
    {
        if (projections.Count == 0)
            return;
        state[role] = new JsonObject { ["projections"] = projections };
    }

    private static JsonArray Projections(string home, IReadOnlyList<string> fields, IReadOnlyList<MeasureDefinition> measures,
        bool aggregate, Aggregation aggregation = Aggregation.Sum)
    {
        var array = new JsonArray();
        foreach (var field in fields)
        {
            if (string.IsNullOrWhiteSpace(field))
                continue;
            JsonObject fieldNode;
            string queryRef;
            if (MeasureRef.TryParse(field, out var measureName))
            {
                var measure = measures.FirstOrDefault(m => string.Equals(m.Name, measureName, StringComparison.OrdinalIgnoreCase));
                var entity = measure?.Table ?? home;
                fieldNode = new JsonObject
                {
                    ["Measure"] = new JsonObject
                    {
                        ["Expression"] = Source(entity),
                        ["Property"] = measureName,
                    },
                };
                queryRef = entity + "." + measureName;
            }
            else
            {
                var (entity, column) = SplitField(home, field);
                if (aggregate)
                {
                    fieldNode = new JsonObject
                    {
                        ["Aggregation"] = new JsonObject
                        {
                            ["Expression"] = new JsonObject
                            {
                                ["Column"] = new JsonObject
                                {
                                    ["Expression"] = Source(entity),
                                    ["Property"] = column,
                                },
                            },
                            ["Function"] = (int)aggregation,
                        },
                    };
                    queryRef = aggregation + "(" + entity + "." + column + ")";
                }
                else
                {
                    fieldNode = new JsonObject
                    {
                        ["Column"] = new JsonObject
                        {
                            ["Expression"] = Source(entity),
                            ["Property"] = column,
                        },
                    };
                    queryRef = entity + "." + column;
                }
            }
            array.Add(new JsonObject
            {
                ["field"] = fieldNode,
                ["queryRef"] = queryRef,
                ["active"] = true,
            });
        }
        return array;
    }

    private static JsonObject Source(string entity) => new()
    {
        ["SourceRef"] = new JsonObject { ["Entity"] = entity },
    };

    private static (string Entity, string Column) SplitField(string home, string field)
    {
        if (FieldRef.TryParse(field, out var table, out var column))
            return (table, column);
        return (home, field);
    }

    private static string PbiType(VisualKind kind) => kind switch
    {
        VisualKind.Bar => "clusteredBarChart",
        VisualKind.Line => "lineChart",
        VisualKind.Area => "areaChart",
        VisualKind.Pie => "pieChart",
        VisualKind.Scatter => "scatterChart",
        VisualKind.Card => "card",
        VisualKind.Table => "tableEx",
        VisualKind.Matrix => "pivotTable",
        VisualKind.Slicer => "slicer",
        _ => "clusteredColumnChart",
    };

    private static ReportDefinition ReadForeignProject(string directory, string name, string modelDir, string reportDir)
    {
        var tables = ReadTables(modelDir, Path.Combine(directory, name + ".Data"));
        var (relationships, _) = ReadRelationships(Path.Combine(modelDir, "definition", "relationships.tmdl"));
        var pages = ReadPages(Path.Combine(reportDir, "definition", "pages"), tables.Measures);
        return new ReportDefinition
        {
            Sources = tables.Sources,
            Relationships = relationships,
            Measures = tables.Measures,
            Pages = pages,
        };
    }

    private sealed class ImportedTables
    {
        public List<DataSourceDefinition> Sources { get; } = [];
        public List<MeasureDefinition> Measures { get; } = [];
    }

    private static ImportedTables ReadTables(string modelDir, string dataDir)
    {
        var result = new ImportedTables();
        var folder = Path.Combine(modelDir, "definition", "tables");
        if (!Directory.Exists(folder))
            return result;
        foreach (var file in Directory.EnumerateFiles(folder, "*.tmdl"))
        {
            var text = File.ReadAllText(file);
            var table = ReadTableName(text);
            if (table is null || table == "PbForMac")
                continue;
            var csv = ExtractContentsPath(text);
            if (csv is null || !File.Exists(csv))
            {
                var fallback = Path.Combine(dataDir, SafeFile(table) + ".csv");
                if (File.Exists(fallback))
                    csv = fallback;
            }
            if (csv is null)
                continue;
            result.Sources.Add(new DataSourceDefinition
            {
                Kind = SourceKind.Csv,
                Path = csv,
                TableName = table,
            });
            result.Measures.AddRange(ReadMeasures(text, table));
        }
        return result;
    }

    private static string? ReadTableName(string tmdl)
    {
        var match = TableRegex().Match(tmdl);
        return match.Success ? Unquote(match.Groups[1].Value) : null;
    }

    private static IEnumerable<MeasureDefinition> ReadMeasures(string tmdl, string table)
    {
        foreach (Match match in MeasureRegex().Matches(tmdl))
        {
            var expression = match.Groups["expr"].Success
                ? match.Groups["expr"].Value.Trim()
                : StripBlock(match.Groups["block"].Value);
            if (expression.Length == 0)
                continue;
            yield return new MeasureDefinition
            {
                Name = Unquote(match.Groups["name"].Value),
                Table = table,
                Expression = expression,
            };
        }
    }

    private static string StripBlock(string block) =>
        string.Join("\n", block.Replace("\r\n", "\n").Split('\n').Select(l => l.Trim()).Where(l => l != "```"));

    private static string? ExtractContentsPath(string tmdl)
    {
        var match = ContentsRegex().Match(tmdl);
        if (!match.Success)
            return null;
        return match.Groups[1].Value.Replace("\"\"", "\"").Replace("\\\\", "\\");
    }

    private static (List<RelationshipDefinition> Items, bool Ok) ReadRelationships(string path)
    {
        var items = new List<RelationshipDefinition>();
        if (!File.Exists(path))
            return (items, true);
        RelationshipDefinition? current = null;
        var fromOne = false;
        var toOne = false;
        foreach (var raw in File.ReadLines(path))
        {
            var line = raw.Trim();
            if (line.StartsWith("relationship ", StringComparison.Ordinal))
            {
                current = new RelationshipDefinition();
                items.Add(current);
                fromOne = toOne = false;
                continue;
            }
            if (current is null || !line.Contains(':'))
                continue;
            var split = line.Split(':', 2);
            var key = split[0].Trim();
            var value = split[1].Trim();
            switch (key)
            {
                case "fromColumn" when SplitColumnRef(value, out var table, out var column):
                    current.FromTable = table;
                    current.FromColumn = column;
                    break;
                case "toColumn" when SplitColumnRef(value, out var toTable, out var toColumn):
                    current.ToTable = toTable;
                    current.ToColumn = toColumn;
                    break;
                case "fromCardinality":
                    fromOne = value == "one";
                    break;
                case "toCardinality":
                    toOne = value == "one";
                    break;
                case "crossFilteringBehavior" when value.Contains("both", StringComparison.OrdinalIgnoreCase):
                    current.FilterDirection = FilterDirection.Both;
                    break;
            }
            if (fromOne && toOne)
                current.Cardinality = RelationshipCardinality.OneToOne;
        }
        return (items, true);
    }

    private static bool SplitColumnRef(string value, out string table, out string column)
    {
        table = column = "";
        var parts = new List<string>();
        var i = 0;
        while (i < value.Length)
        {
            if (value[i] == '\'')
            {
                var end = i + 1;
                var token = new StringBuilder();
                while (end < value.Length)
                {
                    if (value[end] == '\'' && end + 1 < value.Length && value[end + 1] == '\'')
                    {
                        token.Append('\'');
                        end += 2;
                        continue;
                    }
                    if (value[end] == '\'')
                        break;
                    token.Append(value[end]);
                    end++;
                }
                parts.Add(token.ToString());
                i = end + 1;
            }
            else
            {
                var end = value.IndexOf('.', i);
                if (end < 0) end = value.Length;
                parts.Add(value[i..end]);
                i = end;
            }
            if (i < value.Length && value[i] == '.')
                i++;
        }
        if (parts.Count < 2)
            return false;
        table = parts[0];
        column = parts[^1];
        return table.Length > 0 && column.Length > 0;
    }

    private static List<ReportPage> ReadPages(string pagesDir, IReadOnlyList<MeasureDefinition> measures)
    {
        var pages = new List<ReportPage>();
        var orderPath = Path.Combine(pagesDir, "pages.json");
        if (!File.Exists(orderPath))
            return pages;
        var order = JsonNode.Parse(File.ReadAllText(orderPath))?["pageOrder"]?.AsArray()
                    ?? [];
        foreach (var node in order)
        {
            var id = node?.GetValue<string>();
            if (id is null)
                continue;
            var pageDir = Path.Combine(pagesDir, id);
            var pageJson = Path.Combine(pageDir, "page.json");
            if (!File.Exists(pageJson))
                continue;
            var pageNode = JsonNode.Parse(File.ReadAllText(pageJson))!.AsObject();
            var page = new ReportPage
            {
                Name = pageNode["displayName"]?.GetValue<string>() ?? id,
                CanvasWidth = pageNode["width"]?.GetValue<double>() ?? 1280,
                CanvasHeight = pageNode["height"]?.GetValue<double>() ?? 720,
            };
            var visualsDir = Path.Combine(pageDir, "visuals");
            if (Directory.Exists(visualsDir))
            {
                foreach (var visualFile in Directory.EnumerateFiles(visualsDir, "visual.json", SearchOption.AllDirectories))
                    page.Visuals.Add(ReadVisual(JsonNode.Parse(File.ReadAllText(visualFile))!.AsObject(), measures));
            }
            pages.Add(page);
        }
        return pages;
    }

    private static VisualDefinition ReadVisual(JsonObject node, IReadOnlyList<MeasureDefinition> measures)
    {
        var position = node["position"]?.AsObject();
        var visual = node["visual"]?.AsObject() ?? new JsonObject();
        var type = visual["visualType"]?.GetValue<string>() ?? "";
        var definition = new VisualDefinition
        {
            Id = node["name"]?.GetValue<string>() ?? Guid.NewGuid().ToString("N"),
            Kind = KindFromPbi(type),
            X = position?["x"]?.GetValue<double>() ?? 0,
            Y = position?["y"]?.GetValue<double>() ?? 0,
            Width = position?["width"]?.GetValue<double>() ?? 280,
            Height = position?["height"]?.GetValue<double>() ?? 180,
        };
        if (type == "textbox")
        {
            definition.Kind = VisualKind.Text;
            definition.Text = ReadParagraph(visual);
            definition.ShowTitle = false;
        }
        else
        {
            ReadQuery(visual, definition, measures);
        }
        ReadChrome(visual, definition);
        return definition;
    }

    private static void ReadQuery(JsonObject visual, VisualDefinition definition, IReadOnlyList<MeasureDefinition> measures)
    {
        if (visual["query"]?["queryState"] is not JsonObject state)
            return;
        foreach (var role in state)
        {
            if (role.Value?["projections"] is not JsonArray projections)
                continue;
            foreach (var projection in projections)
            {
                if (projection?["field"] is not JsonObject field)
                    continue;
                var (reference, entity, aggregation) = ReadField(field, measures);
                definition.Table ??= entity;
                switch (role.Key)
                {
                    case "Category":
                    case "Rows":
                    case "X" when definition.Kind == VisualKind.Scatter:
                        definition.CategoryField = reference;
                        break;
                    case "Columns":
                        definition.ColumnField = reference;
                        break;
                    default:
                        if (!definition.ValueFields.Contains(reference))
                            definition.ValueFields.Add(reference);
                        if (aggregation is { } agg)
                            definition.Aggregation = agg;
                        break;
                }
            }
        }
    }

    private static (string Reference, string? Entity, Aggregation? Aggregation) ReadField(JsonObject field, IReadOnlyList<MeasureDefinition> measures)
    {
        if (field["Measure"] is JsonObject measure)
        {
            var entity = measure["Expression"]?["SourceRef"]?["Entity"]?.GetValue<string>();
            var name = measure["Property"]?.GetValue<string>() ?? "";
            return (MeasureRef.Format(name), entity, null);
        }
        if (field["Aggregation"] is JsonObject aggregation)
        {
            var column = aggregation["Expression"]?["Column"] as JsonObject;
            var (reference, entity, _) = column is null
                ? ("", null, (Aggregation?)null)
                : ReadField(new JsonObject { ["Column"] = column }, measures);
            var function = aggregation["Function"]?.GetValue<int>() ?? 0;
            return (reference, entity, AggregationFromPbi(function));
        }
        if (field["Column"] is JsonObject columnNode)
        {
            var entity = columnNode["Expression"]?["SourceRef"]?["Entity"]?.GetValue<string>();
            var property = columnNode["Property"]?.GetValue<string>() ?? "";
            var reference = string.IsNullOrEmpty(entity) ? property : FieldRef.Format(entity, property);
            var known = measures.Any(m => string.Equals(m.Name, property, StringComparison.OrdinalIgnoreCase)
                                          && string.Equals(m.Table, entity, StringComparison.OrdinalIgnoreCase));
            return (known ? MeasureRef.Format(property) : reference, entity, null);
        }
        return ("", null, null);
    }

    private static void ReadChrome(JsonObject visual, VisualDefinition definition)
    {
        var title = visual["visualContainerObjects"]?["title"]?[0]?["properties"];
        var text = StripDax(title?["text"]?["expr"]?["Literal"]?["Value"]?.GetValue<string>());
        if (!string.IsNullOrWhiteSpace(text))
            definition.Title = text;
        var show = title?["show"]?["expr"]?["Literal"]?["Value"]?.GetValue<string>();
        if (show == "false")
            definition.ShowTitle = false;
        var color = visual["visualContainerObjects"]?["background"]?[0]?["properties"]?["color"]?["solid"]?["color"]?["expr"]?["Literal"]?["Value"]?.GetValue<string>();
        var fill = StripDax(color);
        if (!string.IsNullOrWhiteSpace(fill))
            definition.Fill = fill;
    }

    private static string ReadParagraph(JsonObject visual)
    {
        var run = visual["objects"]?["general"]?[0]?["properties"]?["paragraphs"]?[0]?["textRuns"]?[0];
        return run?["value"]?.GetValue<string>() ?? "";
    }

    private static Aggregation AggregationFromPbi(int function) => function switch
    {
        1 => Aggregation.Average,
        2 => Aggregation.DistinctCount,
        3 => Aggregation.Min,
        4 => Aggregation.Max,
        5 => Aggregation.Count,
        _ => Aggregation.Sum,
    };

    private static VisualKind KindFromPbi(string type) => type switch
    {
        "barChart" or "clusteredBarChart" or "stackedBarChart" => VisualKind.Bar,
        "lineChart" => VisualKind.Line,
        "areaChart" or "stackedAreaChart" => VisualKind.Area,
        "pieChart" or "donutChart" => VisualKind.Pie,
        "scatterChart" => VisualKind.Scatter,
        "card" or "cardVisual" => VisualKind.Card,
        "tableEx" or "table" => VisualKind.Table,
        "pivotTable" => VisualKind.Matrix,
        "slicer" or "advancedSlicerVisual" => VisualKind.Slicer,
        "shape" => VisualKind.Shape,
        "actionButton" => VisualKind.Button,
        "textbox" => VisualKind.Text,
        _ => VisualKind.Column,
    };

    private static JsonObject PbipPointer(string name) => new()
    {
        ["$schema"] = "https://developer.microsoft.com/json-schemas/fabric/pbip/pbipProperties/1.0.0/schema.json",
        ["version"] = "1.0",
        ["artifacts"] = new JsonArray
        {
            new JsonObject { ["report"] = new JsonObject { ["path"] = name + ".Report" } },
        },
        ["settings"] = new JsonObject { ["enableAutoRecovery"] = true },
    };

    private static JsonObject Platform(string type, string displayName) => new()
    {
        ["$schema"] = "https://developer.microsoft.com/json-schemas/fabric/gitIntegration/platformProperties/2.0.0/schema.json",
        ["metadata"] = new JsonObject { ["type"] = type, ["displayName"] = displayName },
        ["config"] = new JsonObject { ["version"] = "2.0", ["logicalId"] = Guid.NewGuid().ToString() },
    };

    private static JsonObject Literal(string value) => new()
    {
        ["expr"] = new JsonObject { ["Literal"] = new JsonObject { ["Value"] = value } },
    };

    private static JsonObject Solid(string hex) => new()
    {
        ["solid"] = new JsonObject
        {
            ["color"] = new JsonObject
            {
                ["expr"] = new JsonObject { ["Literal"] = new JsonObject { ["Value"] = DaxString(hex) } },
            },
        },
    };

    private static string? StripDax(string? literal)
    {
        if (string.IsNullOrEmpty(literal))
            return null;
        if (literal.Length >= 2 && literal[0] == '\'' && literal[^1] == '\'')
            return literal[1..^1].Replace("''", "'");
        return literal;
    }

    private static string DaxString(string text) => "'" + text.Replace("'", "''") + "'";

    private static string TmdlType(ColumnType type) => type switch
    {
        ColumnType.Integer => "int64",
        ColumnType.Decimal => "double",
        ColumnType.Date => "dateTime",
        ColumnType.Boolean => "boolean",
        _ => "string",
    };

    private static string Ident(string name) =>
        IdentRegex().IsMatch(name) ? name : "'" + name.Replace("'", "''") + "'";

    private static string Unquote(string token)
    {
        token = token.Trim();
        if (token.Length >= 2 && token[0] == '\'' && token[^1] == '\'')
            return token[1..^1].Replace("''", "'");
        return token;
    }

    private static string Token(string value)
    {
        var cleaned = new string(value.Where(char.IsLetterOrDigit).ToArray());
        if (cleaned.Length == 0)
            cleaned = "Item";
        if (char.IsDigit(cleaned[0]))
            cleaned = "P" + cleaned;
        return cleaned.Length > 40 ? cleaned[..40] : cleaned;
    }

    private static string SafeFile(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
        return cleaned.Length == 0 ? "Table" : cleaned;
    }

    private static string Unique(string name, HashSet<string> used)
    {
        var candidate = name;
        var i = 2;
        while (!used.Add(candidate))
            candidate = name + i++;
        return candidate;
    }

    private static void WriteJson(string path, JsonNode node)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
        File.WriteAllText(path, node.ToJsonString(Json));
    }

    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_]*$")]
    private static partial Regex IdentRegex();

    [GeneratedRegex(@"(?m)^table\s+('[^']+'|[A-Za-z_][\w]*)")]
    private static partial Regex TableRegex();

    [GeneratedRegex(@"measure\s+(?<name>'[^']+'|[A-Za-z_][\w]*)\s*=\s*(```(?<block>[\s\S]*?)```|(?<expr>[^\r\n]+))")]
    private static partial Regex MeasureRegex();

    [GeneratedRegex(@"File\.Contents\(\s*""([^""]*)""")]
    private static partial Regex ContentsRegex();
}
