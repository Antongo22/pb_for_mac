using System.Data;
using PbForMac.Models;
using PbForMac.Services;
using PbForMac.Services.Importers;

namespace PbForMac.Tests;

public class FolderImporterTests : IDisposable
{
    private readonly TempFiles _files = new();

    public void Dispose() => _files.Dispose();

    [Fact]
    public void Folder_CombinesFilesByColumnNameAndAddsFileColumn()
    {
        _files.Write("sales_2024_01.csv", "Дата;Сумма\n05.01.2024;100\n");
        _files.Write("sales_2024_02.csv", "Дата;Сумма;Скидка\n03.02.2024;200;0,1\n");
        _files.Write(".hidden.csv", "x\n1\n");
        _files.Write("readme.md", "не данные");

        var table = TypeInference.Apply(new FolderImporter().Import(_files.Directory, null));

        Assert.Equal(["Файл", "Дата", "Сумма", "Скидка"], table.Columns.Cast<DataColumn>().Select(c => c.ColumnName));
        Assert.Equal(2, table.Rows.Count);
        Assert.Equal("sales_2024_02.csv", table.Rows[1]["Файл"]);
        Assert.Equal(typeof(DateTime), table.Columns["Дата"]!.DataType);
        Assert.Equal(DBNull.Value, table.Rows[0]["Скидка"]);
    }

    [Fact]
    public void Folder_SkipsSqliteWhenCombining()
    {
        _files.Write("a.csv", "x\n1\n");
        File.WriteAllBytes(_files.PathOf("shop.db"), []);

        Assert.Equal(2, FolderImporter.FindFiles(_files.Directory).Count);
        Assert.Single(FolderImporter.FindFiles(_files.Directory, combinableOnly: true));
    }

    [Fact]
    public void Folder_WithoutDataFiles_Throws()
    {
        _files.Write("notes.md", "текст");
        Assert.Throws<InvalidOperationException>(() => new FolderImporter().Import(_files.Directory, null));
    }

    [Fact]
    public void Folder_ReloadPicksUpNewFiles()
    {
        _files.Write("part1.csv", "id,value\n1,10\n");
        var source = new DataSourceDefinition { Kind = ImporterFactory.KindFor(_files.Directory), Path = _files.Directory, TableName = "Части" };
        var model = new DataModel();
        model.AddSource(source, ImporterFactory.Load(source));
        Assert.Equal(1, model.GetTable("Части")!.Rows.Count);

        _files.Write("part2.csv", "id,value\n2,20\n3,30\n");
        Assert.Empty(model.Reload());

        Assert.Equal(SourceKind.Folder, source.Kind);
        Assert.Equal(3, model.GetTable("Части")!.Rows.Count);
    }
}
