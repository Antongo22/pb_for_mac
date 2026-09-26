using System.Data;
using Microsoft.Data.Sqlite;
using PbForMac.Models;

namespace PbForMac.Services.Importers;

public sealed class SqliteImporter : IDataImporter
{
    public SourceKind Kind => SourceKind.Sqlite;

    public IReadOnlyList<string> ListItems(string path)
    {
        using var connection = Open(path);
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT name FROM sqlite_master WHERE type IN ('table', 'view') AND name NOT LIKE 'sqlite_%' ORDER BY type, name";
        using var reader = command.ExecuteReader();
        var names = new List<string>();
        while (reader.Read())
            names.Add(reader.GetString(0));
        return names;
    }

    public DataTable Import(string path, string? item)
    {
        var name = item ?? ListItems(path).FirstOrDefault()
            ?? throw new InvalidOperationException("В базе данных нет таблиц.");

        using var connection = Open(path);
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT * FROM \"{name.Replace("\"", "\"\"")}\"";
        using var reader = command.ExecuteReader();

        var table = RawTable.Create(name, Enumerable.Range(0, reader.FieldCount).Select(reader.GetName));
        table.BeginLoadData();
        var values = new object?[reader.FieldCount];
        while (reader.Read())
        {
            for (var i = 0; i < values.Length; i++)
            {
                values[i] = reader.IsDBNull(i) ? null : reader.GetValue(i) switch
                {
                    byte[] blob => $"<{blob.Length} байт>",
                    var v => v,
                };
            }
            table.Rows.Add(values);
        }
        table.EndLoadData();
        return table;
    }

    private static SqliteConnection Open(string path)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
        }.ToString());
        connection.Open();
        return connection;
    }
}
