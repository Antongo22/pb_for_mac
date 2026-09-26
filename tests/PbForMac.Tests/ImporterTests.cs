using System.Text;
using ClosedXML.Excel;
using Microsoft.Data.Sqlite;
using PbForMac.Models;
using PbForMac.Services;
using PbForMac.Services.Importers;

namespace PbForMac.Tests;

public class ImporterTests : IDisposable
{
    private readonly TempFiles _files = new();

    public void Dispose() => _files.Dispose();

    [Fact]
    public void Csv_DetectsSemicolonAndQuotedFields()
    {
        var path = _files.Write("sales.csv", "Город;Сумма;Комментарий\nМосква;1 200,50;\"a;b\"\nКазань;300;\n");

        var table = TypeInference.Apply(new CsvImporter().Import(path, null));

        Assert.Equal(["Город", "Сумма", "Комментарий"], table.Columns.Cast<System.Data.DataColumn>().Select(c => c.ColumnName));
        Assert.Equal(2, table.Rows.Count);
        Assert.Equal(1200.5, table.Rows[0]["Сумма"]);
        Assert.Equal("a;b", table.Rows[0]["Комментарий"]);
    }

    [Fact]
    public void Csv_ReadsWindows1251()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var path = _files.PathOf("cp1251.csv");
        File.WriteAllBytes(path, Encoding.GetEncoding(1251).GetBytes("Имя,Возраст\nАнна,30\n"));

        var table = new CsvImporter().Import(path, null);

        Assert.Equal("Имя", table.Columns[0].ColumnName);
        Assert.Equal("Анна", table.Rows[0][0]);
    }

    [Fact]
    public void Csv_MakesDuplicateHeadersUnique()
    {
        var path = _files.Write("dup.csv", "a,a,\n1,2,3\n");

        var table = new CsvImporter().Import(path, null);

        Assert.Equal(["a", "a_2", "Столбец3"], table.Columns.Cast<System.Data.DataColumn>().Select(c => c.ColumnName));
    }

    [Fact]
    public void Json_FlattensNestedObjectsAndFindsInnerArray()
    {
        var path = _files.Write("data.json", """
            { "meta": { "count": 2 },
              "items": [
                { "id": 1, "name": "A", "supplier": { "city": "Москва" }, "tags": ["x", "y"] },
                { "id": 2, "name": "B", "price": 9.5 }
              ] }
            """);

        var table = TypeInference.Apply(new JsonImporter().Import(path, null));

        Assert.Equal(2, table.Rows.Count);
        Assert.Contains("supplier.city", table.Columns.Cast<System.Data.DataColumn>().Select(c => c.ColumnName));
        Assert.Equal("x, y", table.Rows[0]["tags"]);
        Assert.Equal(typeof(long), table.Columns["id"]!.DataType);
        Assert.Equal(DBNull.Value, table.Rows[0]["price"]);
    }

    [Fact]
    public void Xml_ReadsAttributesAndChildElements()
    {
        var path = _files.Write("regions.xml", """
            <?xml version="1.0" encoding="utf-8"?>
            <data>
              <info>test</info>
              <regions>
                <region code="77"><name>Москва</name><population>13000000</population></region>
                <region code="78"><name>Санкт-Петербург</name><population>5600000</population></region>
              </regions>
            </data>
            """);

        var table = TypeInference.Apply(new XmlImporter().Import(path, null));

        Assert.Equal(2, table.Rows.Count);
        Assert.Equal(["code", "name", "population"], table.Columns.Cast<System.Data.DataColumn>().Select(c => c.ColumnName));
        Assert.Equal(5600000L, table.Rows[1]["population"]);
    }

    [Fact]
    public void Excel_ListsSheetsAndReadsTypedCells()
    {
        var path = _files.PathOf("book.xlsx");
        using (var workbook = new XLWorkbook())
        {
            var sheet = workbook.AddWorksheet("Продажи");
            sheet.Cell(1, 1).Value = "Дата";
            sheet.Cell(1, 2).Value = "Сумма";
            sheet.Cell(2, 1).Value = new DateTime(2024, 3, 1);
            sheet.Cell(2, 2).Value = 150.5;
            workbook.AddWorksheet("Пусто");
            workbook.SaveAs(path);
        }

        var importer = new ExcelImporter();
        Assert.Equal(["Продажи", "Пусто"], importer.ListItems(path));

        var table = TypeInference.Apply(importer.Import(path, "Продажи"));
        Assert.Equal(new DateTime(2024, 3, 1), table.Rows[0]["Дата"]);
        Assert.Equal(150.5, table.Rows[0]["Сумма"]);
    }

    [Fact]
    public void Sqlite_ListsTablesAndImports()
    {
        var path = _files.PathOf("shop.db");
        using (var connection = new SqliteConnection($"Data Source={path};Pooling=False"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE orders (id INTEGER PRIMARY KEY, customer TEXT, total REAL);
                INSERT INTO orders (customer, total) VALUES ('Анна', 10.5), ('Иван', 20);
                CREATE TABLE empty (x INTEGER);
                """;
            command.ExecuteNonQuery();
        }

        var importer = new SqliteImporter();
        Assert.Equal(["empty", "orders"], importer.ListItems(path));

        var table = TypeInference.Apply(importer.Import(path, "orders"));
        Assert.Equal(2, table.Rows.Count);
        Assert.Equal(typeof(double), table.Columns["total"]!.DataType);
    }

    [Theory]
    [InlineData("a.csv", SourceKind.Csv)]
    [InlineData("a.XLSX", SourceKind.Excel)]
    [InlineData("a.json", SourceKind.Json)]
    [InlineData("a.xml", SourceKind.Xml)]
    [InlineData("a.sqlite3", SourceKind.Sqlite)]
    public void Factory_MapsExtensions(string file, SourceKind kind)
    {
        Assert.Equal(kind, ImporterFactory.KindFor(file));
    }
}
