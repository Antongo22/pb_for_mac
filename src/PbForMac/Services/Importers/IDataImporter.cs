using System.Data;
using PbForMac.Models;

namespace PbForMac.Services.Importers;

/// <summary>
/// Импортёр файла в «сырую» таблицу: столбцы типа object со строками или нативными значениями.
/// Типы столбцов затем определяет <see cref="TypeInference"/>.
/// </summary>
public interface IDataImporter
{
    SourceKind Kind { get; }

    /// <summary>Наборы данных внутри файла (листы, таблицы). Пусто — файл содержит один набор.</summary>
    IReadOnlyList<string> ListItems(string path);

    DataTable Import(string path, string? item);
}

public static class RawTable
{
    /// <summary>Создаёт таблицу со столбцами object, делая имена уникальными и непустыми.</summary>
    public static DataTable Create(string name, IEnumerable<string?> headers)
    {
        var table = new DataTable(name);
        var index = 0;
        foreach (var header in headers)
        {
            index++;
            AddColumn(table, string.IsNullOrWhiteSpace(header) ? $"Столбец{index}" : header.Trim());
        }
        return table;
    }

    /// <summary>Добавляет столбец с уникальным именем и возвращает его.</summary>
    public static DataColumn AddColumn(DataTable table, string name)
    {
        var unique = name;
        for (var i = 2; table.Columns.Contains(unique); i++)
            unique = $"{name}_{i}";
        return table.Columns.Add(unique, typeof(object));
    }
}
