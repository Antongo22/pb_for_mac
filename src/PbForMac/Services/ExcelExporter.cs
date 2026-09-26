using System.Data;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using PbForMac.Controls;

namespace PbForMac.Services;

/// <summary>Экспорт таблицы или среза визуала в .xlsx через ClosedXML.</summary>
public static partial class ExcelExporter
{
    public static void Export(DataTable table, string path) =>
        Export(table, table.Rows.Cast<DataRow>(), path);

    public static void Export(TableSlice slice, string path) =>
        Export(slice.Table, slice.Rows, path);

    public static void Export(DataTable table, IEnumerable<DataRow> rows, string path)
    {
        using var workbook = new XLWorkbook();
        var sheetName = SanitizeSheetName(string.IsNullOrWhiteSpace(table.TableName) ? "Данные" : table.TableName);
        var worksheet = workbook.Worksheets.Add(sheetName);

        for (var c = 0; c < table.Columns.Count; c++)
            worksheet.Cell(1, c + 1).Value = table.Columns[c].ColumnName;

        var rowIndex = 2;
        foreach (var row in rows)
        {
            for (var c = 0; c < table.Columns.Count; c++)
            {
                var value = row[c];
                var cell = worksheet.Cell(rowIndex, c + 1);
                if (value is DBNull or null)
                    continue;
                cell.Value = value switch
                {
                    string s => s,
                    bool b => b,
                    DateTime dt => dt,
                    byte or sbyte or short or ushort or int or uint or long or ulong => Convert.ToDouble(value),
                    float or double or decimal => Convert.ToDouble(value),
                    _ => ValueFormatter.Display(value),
                };
            }
            rowIndex++;
        }

        if (table.Columns.Count > 0)
            worksheet.SheetView.FreezeRows(1);
        workbook.SaveAs(path);
    }

    private static string SanitizeSheetName(string name)
    {
        var cleaned = InvalidSheetChars().Replace(name, "_").Trim();
        if (cleaned.Length == 0)
            cleaned = "Данные";
        return cleaned.Length <= 31 ? cleaned : cleaned[..31];
    }

    [GeneratedRegex(@"[\\/*?:\[\]]")]
    private static partial Regex InvalidSheetChars();
}
