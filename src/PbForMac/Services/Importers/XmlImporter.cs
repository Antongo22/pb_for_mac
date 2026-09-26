using System.Data;
using System.Xml.Linq;
using PbForMac.Models;

namespace PbForMac.Services.Importers;

/// <summary>
/// XML: строками считаются повторяющиеся однотипные элементы (например, &lt;row&gt; внутри &lt;rows&gt;).
/// Атрибуты и простые дочерние элементы становятся столбцами, вложенные — столбцами вида «parent.child».
/// </summary>
public sealed class XmlImporter : IDataImporter
{
    public SourceKind Kind => SourceKind.Xml;

    public IReadOnlyList<string> ListItems(string path) => [];

    public DataTable Import(string path, string? item)
    {
        var doc = XDocument.Load(path);
        var table = new DataTable(Path.GetFileNameWithoutExtension(path));
        if (doc.Root is null)
            return table;

        var rows = FindRows(doc.Root);
        var records = rows.Select(r =>
        {
            var record = new List<KeyValuePair<string, object?>>();
            Flatten(r, "", record);
            return record;
        }).ToList();

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

    /// <summary>Находит родителя с наибольшим числом одноимённых дочерних элементов.</summary>
    private static List<XElement> FindRows(XElement root)
    {
        List<XElement> best = [];
        foreach (var parent in root.DescendantsAndSelf())
        {
            var group = parent.Elements()
                .GroupBy(e => e.Name)
                .MaxBy(g => g.Count());
            if (group is not null && group.Count() > best.Count)
                best = group.ToList();
        }
        // Единственный элемент без повторов — одна строка из корня.
        return best.Count > 1 ? best : [root];
    }

    private static void Flatten(XElement element, string prefix, List<KeyValuePair<string, object?>> record)
    {
        foreach (var attribute in element.Attributes().Where(a => !a.IsNamespaceDeclaration))
            Add(record, Join(prefix, attribute.Name.LocalName), attribute.Value);

        if (!element.HasElements)
        {
            if (!string.IsNullOrWhiteSpace(element.Value) || !element.HasAttributes)
                Add(record, prefix.Length == 0 ? "value" : prefix, element.Value);
            return;
        }

        foreach (var child in element.Elements())
        {
            var name = Join(prefix, child.Name.LocalName);
            if (child.HasElements || child.HasAttributes)
                Flatten(child, name, record);
            else
                Add(record, name, child.Value);
        }
    }

    private static string Join(string prefix, string name) => prefix.Length == 0 ? name : $"{prefix}.{name}";

    private static void Add(List<KeyValuePair<string, object?>> record, string key, string value)
    {
        // Повторяющиеся элементы с одинаковым именем склеиваем через запятую.
        var index = record.FindIndex(kv => kv.Key == key);
        if (index >= 0)
            record[index] = new(key, $"{record[index].Value}, {value}");
        else
            record.Add(new(key, value));
    }
}
