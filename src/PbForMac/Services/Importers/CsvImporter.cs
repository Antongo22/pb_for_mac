using System.Data;
using System.Globalization;
using System.Text;
using CsvHelper;
using CsvHelper.Configuration;
using PbForMac.Models;

namespace PbForMac.Services.Importers;

public sealed class CsvImporter : IDataImporter
{
    private static readonly char[] Delimiters = [',', ';', '\t', '|'];

    public SourceKind Kind => SourceKind.Csv;

    public IReadOnlyList<string> ListItems(string path) => [];

    public DataTable Import(string path, string? item)
    {
        var text = ReadText(path);
        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            Delimiter = DetectDelimiter(text).ToString(),
            BadDataFound = null,
            MissingFieldFound = null,
            HeaderValidated = null,
            TrimOptions = TrimOptions.Trim,
            IgnoreBlankLines = true,
        };

        using var reader = new StringReader(text);
        using var csv = new CsvReader(reader, config);
        var table = new DataTable(Path.GetFileNameWithoutExtension(path));
        if (!csv.Read())
            return table;
        csv.ReadHeader();
        table = RawTable.Create(table.TableName, csv.HeaderRecord ?? []);

        table.BeginLoadData();
        while (csv.Read())
        {
            var values = new object?[table.Columns.Count];
            var count = Math.Min(csv.Parser.Count, values.Length);
            for (var i = 0; i < count; i++)
                values[i] = csv.GetField(i);
            // Лишние поля в строке добавляют новые столбцы.
            for (var i = values.Length; i < csv.Parser.Count; i++)
            {
                RawTable.AddColumn(table, $"Столбец{i + 1}");
                Array.Resize(ref values, table.Columns.Count);
                values[i] = csv.GetField(i);
            }
            table.Rows.Add(values);
        }
        table.EndLoadData();
        return table;
    }

    /// <summary>Читает файл как UTF-8, а если он не является корректным UTF-8 — как Windows-1251.</summary>
    internal static string ReadText(string path)
    {
        var bytes = File.ReadAllBytes(path);
        try
        {
            var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
            var text = utf8.GetString(bytes);
            return text.Length > 0 && text[0] == '﻿' ? text[1..] : text;
        }
        catch (DecoderFallbackException)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(1251).GetString(bytes);
        }
    }

    /// <summary>Выбирает разделитель, который встречается одинаково часто в первых строках.</summary>
    internal static char DetectDelimiter(string text)
    {
        var lines = text.Split('\n', 11).Take(10).Where(l => l.Trim().Length > 0).ToList();
        if (lines.Count == 0)
            return ',';

        var best = ',';
        var bestScore = -1;
        foreach (var d in Delimiters)
        {
            var counts = lines.Select(l => CountOutsideQuotes(l, d)).ToList();
            var min = counts.Min();
            if (min == 0)
                continue;
            // Предпочитаем разделитель с одинаковым числом вхождений в каждой строке.
            var score = counts.All(c => c == counts[0]) ? min * 10 : min;
            if (score > bestScore)
            {
                bestScore = score;
                best = d;
            }
        }
        return best;
    }

    private static int CountOutsideQuotes(string line, char delimiter)
    {
        var count = 0;
        var inQuotes = false;
        foreach (var ch in line)
        {
            if (ch == '"') inQuotes = !inQuotes;
            else if (ch == delimiter && !inQuotes) count++;
        }
        return count;
    }
}
