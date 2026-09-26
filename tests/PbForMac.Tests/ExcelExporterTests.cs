using System.Data;
using ClosedXML.Excel;
using PbForMac.Services;

namespace PbForMac.Tests;

public class ExcelExporterTests
{
    [Fact]
    public void Export_WritesReadableWorkbook()
    {
        var table = new DataTable("Продажи");
        table.Columns.Add("Регион", typeof(string));
        table.Columns.Add("Сумма", typeof(double));
        table.Rows.Add("Север", 10d);
        table.Rows.Add("Юг", 5d);

        var path = Path.Combine(Path.GetTempPath(), $"pbformac-excel-{Guid.NewGuid():N}.xlsx");
        try
        {
            ExcelExporter.Export(table, path);
            Assert.True(File.Exists(path));

            using var workbook = new XLWorkbook(path);
            var sheet = workbook.Worksheet(1);
            Assert.Equal("Продажи", sheet.Name);
            Assert.Equal("Регион", sheet.Cell(1, 1).GetString());
            Assert.Equal("Север", sheet.Cell(2, 1).GetString());
            Assert.Equal(10d, sheet.Cell(2, 2).GetDouble());
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }
}
