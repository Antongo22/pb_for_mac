using System.Data;
using System.Globalization;
using System.Text;
using CsvHelper;
using CsvHelper.Configuration;

namespace PbForMac.Services;

public static class ExportService
{
    /// <summary>Сохраняет строки таблицы в CSV (UTF-8 с BOM, чтобы Excel правильно открыл кириллицу).</summary>
    public static void ToCsv(DataTable table, IEnumerable<DataRow> rows, string path)
    {
        using var writer = new StreamWriter(path, false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        using var csv = new CsvWriter(writer, new CsvConfiguration(CultureInfo.InvariantCulture));
        foreach (DataColumn column in table.Columns)
            csv.WriteField(column.ColumnName);
        csv.NextRecord();
        foreach (var row in rows)
        {
            foreach (DataColumn column in table.Columns)
                csv.WriteField(QueryEngine.Key(row[column]));
            csv.NextRecord();
        }
    }
}
