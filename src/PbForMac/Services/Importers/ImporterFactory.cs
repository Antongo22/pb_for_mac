using PbForMac.Models;

namespace PbForMac.Services.Importers;

public static class ImporterFactory
{
    private static readonly Dictionary<string, SourceKind> ExtensionMap = new(StringComparer.OrdinalIgnoreCase)
    {
        [".csv"] = SourceKind.Csv,
        [".tsv"] = SourceKind.Csv,
        [".txt"] = SourceKind.Csv,
        [".xlsx"] = SourceKind.Excel,
        [".xlsm"] = SourceKind.Excel,
        [".json"] = SourceKind.Json,
        [".xml"] = SourceKind.Xml,
        [".db"] = SourceKind.Sqlite,
        [".sqlite"] = SourceKind.Sqlite,
        [".sqlite3"] = SourceKind.Sqlite,
    };

    public static IReadOnlyCollection<string> SupportedExtensions => ExtensionMap.Keys;

    public static SourceKind KindFor(string path) =>
        ExtensionMap.TryGetValue(Path.GetExtension(path), out var kind)
            ? kind
            : throw new NotSupportedException($"Формат файла «{Path.GetExtension(path)}» не поддерживается.");

    public static IDataImporter Create(SourceKind kind) => kind switch
    {
        SourceKind.Csv => new CsvImporter(),
        SourceKind.Excel => new ExcelImporter(),
        SourceKind.Json => new JsonImporter(),
        SourceKind.Xml => new XmlImporter(),
        SourceKind.Sqlite => new SqliteImporter(),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    /// <summary>Загружает источник и приводит типы столбцов.</summary>
    public static System.Data.DataTable Load(DataSourceDefinition source)
    {
        var raw = Create(source.Kind).Import(source.Path, source.Item);
        var table = TypeInference.Apply(raw);
        table.TableName = source.TableName;
        return table;
    }
}
