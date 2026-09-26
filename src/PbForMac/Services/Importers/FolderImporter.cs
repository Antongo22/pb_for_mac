using System.Data;
using PbForMac.Models;

namespace PbForMac.Services.Importers;

/// <summary>
/// Папка (аналог источника «Папка» в Power BI): файлы данных объединяются в одну таблицу
/// по именам столбцов, а столбец «Файл» хранит имя исходного файла (с подпапкой, если она есть).
/// При обновлении папка сканируется заново, поэтому новые файлы подхватываются автоматически.
/// </summary>
public sealed class FolderImporter(bool includeSubfolders = false) : IDataImporter
{
    public const string FileColumn = "Файл";

    public SourceKind Kind => SourceKind.Folder;

    public IReadOnlyList<string> ListItems(string path) => [];

    /// <summary>
    /// Файлы данных папки (без скрытых файлов и папок, временных файлов Office), отсортированные по пути.
    /// </summary>
    public static IReadOnlyList<string> FindFiles(string folder, bool combinableOnly = false, bool recursive = false)
    {
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = recursive,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.Hidden | FileAttributes.System,
        };
        return Directory.EnumerateFiles(folder, "*", options)
            .Where(f =>
            {
                var relative = Path.GetRelativePath(folder, f);
                // Скрытые папки (.git и т. п.) и файлы, а также временные файлы Office.
                if (relative.Split(Path.DirectorySeparatorChar).Any(part => part.StartsWith('.')))
                    return false;
                if (Path.GetFileName(f).StartsWith("~$", StringComparison.Ordinal))
                    return false;
                if (!ImporterFactory.IsSupported(f))
                    return false;
                // Базы SQLite содержат несколько таблиц — объединять их построчно бессмысленно.
                return !combinableOnly || ImporterFactory.KindFor(f) != SourceKind.Sqlite;
            })
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Подпись файла в столбце «Файл»: путь относительно папки через «/».</summary>
    public static string RelativeName(string folder, string file) =>
        Path.GetRelativePath(folder, file).Replace(Path.DirectorySeparatorChar, '/');

    public DataTable Import(string path, string? item)
    {
        if (!Directory.Exists(path))
            throw new DirectoryNotFoundException($"Папка не найдена: {path}");

        var files = FindFiles(path, combinableOnly: true, recursive: includeSubfolders);
        if (files.Count == 0)
        {
            throw new InvalidOperationException(includeSubfolders
                ? "В папке и её подпапках нет файлов CSV, Excel, JSON или XML."
                : "В папке нет файлов CSV, Excel, JSON или XML.");
        }

        var result = new DataTable(Path.GetFileName(Path.TrimEndingDirectorySeparator(path)));
        var fileColumn = result.Columns.Add(FileColumn, typeof(object));
        result.BeginLoadData();
        foreach (var file in files)
        {
            // Для Excel берётся первый лист.
            var part = ImporterFactory.Create(ImporterFactory.KindFor(file)).Import(file, null);
            var mapping = part.Columns.Cast<DataColumn>()
                .Select(c => result.Columns[c.ColumnName] ?? result.Columns.Add(c.ColumnName, typeof(object)))
                .ToArray();
            var name = RelativeName(path, file);
            foreach (DataRow source in part.Rows)
            {
                var row = result.NewRow();
                row[fileColumn] = name;
                for (var i = 0; i < mapping.Length; i++)
                {
                    // Столбец «Файл» из самих данных не перезаписывает имя файла.
                    if (mapping[i] != fileColumn)
                        row[mapping[i]] = source[i];
                }
                result.Rows.Add(row);
            }
        }
        result.EndLoadData();
        return result;
    }
}
