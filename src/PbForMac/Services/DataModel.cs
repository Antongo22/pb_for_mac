using System.Data;
using PbForMac.Models;
using PbForMac.Services.Importers;

namespace PbForMac.Services;

/// <summary>
/// Модель данных отчёта: загруженные источники («сырые» таблицы) и шаги преобразований.
/// Итоговые таблицы получаются применением шагов к копиям исходных таблиц.
/// </summary>
public sealed class DataModel
{
    private readonly Dictionary<string, DataTable> _raw = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<TransformStep, string> _stepErrors = [];
    private List<DataTable> _tables = [];

    public List<DataSourceDefinition> Sources { get; } = [];
    public List<TransformStep> Steps { get; } = [];
    public IReadOnlyList<DataTable> Tables => _tables;
    public IReadOnlyDictionary<TransformStep, string> StepErrors => _stepErrors;

    /// <summary>Модель изменилась (таблицы пересобраны).</summary>
    public event EventHandler? Changed;

    public DataTable? GetTable(string? name) =>
        name is null ? null : _tables.FirstOrDefault(t => string.Equals(t.TableName, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Уникальное имя таблицы на основе желаемого.</summary>
    public string UniqueTableName(string desired)
    {
        desired = string.IsNullOrWhiteSpace(desired) ? "Таблица" : desired.Trim();
        var name = desired;
        for (var i = 2; _raw.ContainsKey(name) || GetTable(name) is not null; i++)
            name = $"{desired} ({i})";
        return name;
    }

    public void AddSource(DataSourceDefinition source, DataTable table)
    {
        table.TableName = source.TableName;
        Sources.Add(source);
        _raw[source.TableName] = table;
        Rebuild();
    }

    /// <summary>Добавляет шаг, предварительно проверив его на текущих таблицах.</summary>
    public void AddStep(TransformStep step)
    {
        var copy = _tables.Select(t => t.Copy()).ToList();
        TransformEngine.Apply(copy, step); // бросает исключение с понятным текстом
        Steps.Add(step);
        _tables = copy;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void RemoveStep(TransformStep step)
    {
        Steps.Remove(step);
        Rebuild();
    }

    /// <summary>Удаляет таблицу вместе с источником и всеми зависящими от неё шагами.</summary>
    public void RemoveTable(string name)
    {
        var removed = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { name };
        Sources.RemoveAll(s => string.Equals(s.TableName, name, StringComparison.OrdinalIgnoreCase));
        _raw.Remove(name);

        foreach (var step in Steps.ToList())
        {
            if (step is GroupByStep g && removed.Contains(g.NewTable))
            {
                // Шаг создал удаляемую таблицу — сам шаг тоже удаляем.
                Steps.Remove(step);
            }
            else if (removed.Contains(step.Table))
            {
                Steps.Remove(step);
                if (step is GroupByStep created)
                    removed.Add(created.NewTable);
            }
        }
        Rebuild();
    }

    /// <summary>Перечитывает все источники с диска. Возвращает список ошибок.</summary>
    public IReadOnlyList<string> Reload()
    {
        var errors = new List<string>();
        foreach (var source in Sources)
        {
            try
            {
                _raw[source.TableName] = ImporterFactory.Load(source);
            }
            catch (Exception e)
            {
                errors.Add($"{source.TableName}: {e.Message}");
            }
        }
        Rebuild();
        return errors;
    }

    /// <summary>Заменяет содержимое модели (используется при открытии отчёта).</summary>
    public IReadOnlyList<string> Load(IEnumerable<DataSourceDefinition> sources, IEnumerable<TransformStep> steps)
    {
        Sources.Clear();
        Steps.Clear();
        _raw.Clear();
        Sources.AddRange(sources);
        Steps.AddRange(steps);
        return Reload();
    }

    public void Clear()
    {
        Sources.Clear();
        Steps.Clear();
        _raw.Clear();
        Rebuild();
    }

    /// <summary>Пересобирает таблицы: копии исходных данных + все шаги по порядку.</summary>
    public void Rebuild()
    {
        _stepErrors.Clear();
        var tables = Sources
            .Where(s => _raw.ContainsKey(s.TableName))
            .Select(s => _raw[s.TableName].Copy())
            .ToList();

        foreach (var step in Steps)
        {
            try
            {
                TransformEngine.Apply(tables, step);
            }
            catch (Exception e)
            {
                _stepErrors[step] = e.Message;
            }
        }

        _tables = tables;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
