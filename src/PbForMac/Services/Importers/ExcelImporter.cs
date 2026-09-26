using System.Data;
using ClosedXML.Excel;
using PbForMac.Models;

namespace PbForMac.Services.Importers;

public sealed class ExcelImporter : IDataImporter
{
    public SourceKind Kind => SourceKind.Excel;

    public IReadOnlyList<string> ListItems(string path)
    {
        using var workbook = Open(path);
        return workbook.Worksheets.Select(ws => ws.Name).ToList();
    }

    public DataTable Import(string path, string? item)
    {
        using var workbook = Open(path);
        var sheet = item is null ? workbook.Worksheet(1) : workbook.Worksheet(item);
        var range = sheet.RangeUsed();
        var table = new DataTable(sheet.Name);
        if (range is null)
            return table;

        var firstRow = range.FirstRow().RowNumber();
        var lastRow = range.LastRow().RowNumber();
        var firstCol = range.FirstColumn().ColumnNumber();
        var lastCol = range.LastColumn().ColumnNumber();

        table = RawTable.Create(sheet.Name,
            Enumerable.Range(firstCol, lastCol - firstCol + 1).Select(c => sheet.Cell(firstRow, c).GetFormattedString()));

        table.BeginLoadData();
        for (var r = firstRow + 1; r <= lastRow; r++)
        {
            var values = new object?[table.Columns.Count];
            var empty = true;
            for (var c = firstCol; c <= lastCol; c++)
            {
                var value = ToValue(sheet.Cell(r, c));
                values[c - firstCol] = value;
                empty &= value is null;
            }
            if (!empty)
                table.Rows.Add(values);
        }
        table.EndLoadData();
        return table;
    }

    private static XLWorkbook Open(string path)
    {
        // Открываем через поток с общим доступом, чтобы читать файл, открытый в Excel.
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        return new XLWorkbook(stream);
    }

    private static object? ToValue(IXLCell cell)
    {
        var value = cell.Value;
        return value.Type switch
        {
            XLDataType.Blank => null,
            XLDataType.Boolean => value.GetBoolean(),
            XLDataType.Number => value.GetNumber(),
            XLDataType.DateTime => value.GetDateTime(),
            XLDataType.TimeSpan => value.GetTimeSpan().ToString(),
            XLDataType.Text => value.GetText(),
            _ => cell.GetFormattedString(),
        };
    }
}
