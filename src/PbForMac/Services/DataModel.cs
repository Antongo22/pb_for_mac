using System.Data;
using PbForMac.Models;
using PbForMac.Services.Importers;

namespace PbForMac.Services;

/// <summary>
/// Модель данных отчёта: загруженные источники («сырые» таблицы), шаги преобразований и связи.
/// Итоговые таблицы получаются применением шагов к копиям исходных таблиц.
/// </summary>
public sealed class DataModel
{
    private readonly Dictionary<string, DataTable> _raw = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<TransformStep, string> _stepErrors = [];
    private readonly Dictionary<RelationshipDefinition, string> _relationshipIssues = [];
    private List<DataTable> _tables = [];

    public List<DataSourceDefinition> Sources { get; } = [];
    public List<TransformStep> Steps { get; } = [];
    public List<RelationshipDefinition> Relationships { get; } = [];
    public IReadOnlyList<DataTable> Tables => _tables;
    public IReadOnlyDictionary<TransformStep, string> StepErrors => _stepErrors;

    /// <summary>Проблемы связей: нет таблицы/столбца (связь не работает) или неуникальные ключи (берётся первая строка).</summary>
    public IReadOnlyDictionary<RelationshipDefinition, string> RelationshipIssues => _relationshipIssues;

    /// <summary>Модель изменилась (таблицы пересобраны).</summary>
    public event EventHandler? Changed;

    /// <summary>Папка таблицы в списке (по источнику); у производных таблиц — папка исходной таблицы.</summary>
    public string? GroupOf(string tableName)
    {
        var source = Sources.FirstOrDefault(s => string.Equals(s.TableName, tableName, StringComparison.OrdinalIgnoreCase));
        if (source is not null)
            return source.Group;
        var creator = Steps.OfType<GroupByStep>()
            .FirstOrDefault(g => string.Equals(g.NewTable, tableName, StringComparison.OrdinalIgnoreCase));
        return creator is null || string.Equals(creator.Table, tableName, StringComparison.OrdinalIgnoreCase)
            ? null
            : GroupOf(creator.Table);
    }

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
        ValidateRelationships();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Добавляет связь «многие к одному», проверив таблицы и столбцы.</summary>
    public void AddRelationship(RelationshipDefinition relationship)
    {
        var from = GetTable(relationship.FromTable)
                   ?? throw new InvalidOperationException($"Таблица «{relationship.FromTable}» не найдена.");
        var to = GetTable(relationship.ToTable)
                 ?? throw new InvalidOperationException($"Таблица «{relationship.ToTable}» не найдена.");
        if (from == to)
            throw new InvalidOperationException("Связь должна соединять две разные таблицы.");
        if (!from.Columns.Contains(relationship.FromColumn))
            throw new InvalidOperationException($"Столбец «{relationship.FromColumn}» не найден в таблице «{from.TableName}».");
        if (!to.Columns.Contains(relationship.ToColumn))
            throw new InvalidOperationException($"Столбец «{relationship.ToColumn}» не найден в таблице «{to.TableName}».");
        if (Relationships.Any(r => r.SameAs(relationship)))
            throw new InvalidOperationException("Такая связь уже есть.");
        if (Relationships.Any(r => r.Connects(from.TableName) && r.Connects(to.TableName)))
            throw new InvalidOperationException($"Таблицы «{from.TableName}» и «{to.TableName}» уже связаны.");

        relationship.FromTable = from.TableName;
        relationship.ToTable = to.TableName;
        Relationships.Add(relationship);
        ValidateRelationships();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void RemoveRelationship(RelationshipDefinition relationship)
    {
        if (!Relationships.Remove(relationship))
            return;
        ValidateRelationships();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Ищет связи автоматически (одноимённые ключевые столбцы) и добавляет их.
    /// <paramref name="onlyTables"/> — искать только связи с этими таблицами (например, только что загруженными).
    /// </summary>
    public IReadOnlyList<RelationshipDefinition> DetectRelationships(IEnumerable<string>? onlyTables = null)
    {
        var found = RelationshipDetector.Detect(_tables, Relationships, onlyTables);
        if (found.Count == 0)
            return found;
        Relationships.AddRange(found);
        ValidateRelationships();
        Changed?.Invoke(this, EventArgs.Empty);
        return found;
    }

    private void ValidateRelationships()
    {
        _relationshipIssues.Clear();
        foreach (var relationship in Relationships)
        {
            var from = GetTable(relationship.FromTable);
            var to = GetTable(relationship.ToTable);
            if (from is null || to is null)
                _relationshipIssues[relationship] = $"Таблица «{(from is null ? relationship.FromTable : relationship.ToTable)}» не найдена — связь не работает.";
            else if (!from.Columns.Contains(relationship.FromColumn))
                _relationshipIssues[relationship] = $"Столбец «{relationship.FromColumn}» не найден в «{from.TableName}» — связь не работает.";
            else if (!to.Columns.Contains(relationship.ToColumn))
                _relationshipIssues[relationship] = $"Столбец «{relationship.ToColumn}» не найден в «{to.TableName}» — связь не работает.";
            else if (RelationshipDetector.HasDuplicateKeys(to, relationship.ToColumn))
                _relationshipIssues[relationship] = $"Значения «{relationship.ToColumn}» в «{to.TableName}» повторяются — используется первая строка с ключом.";
        }
    }

    /// <summary>Доля различных ключей таблицы «многие», найденных в таблице «один» (0…1), или null, если связь не работает.</summary>
    public double? MatchRate(RelationshipDefinition relationship)
    {
        var from = GetTable(relationship.FromTable);
        var to = GetTable(relationship.ToTable);
        if (from?.Columns[relationship.FromColumn] is not { } fromColumn || to?.Columns[relationship.ToColumn] is not { } toColumn)
            return null;
        return RelationshipDetector.MatchRate(from, fromColumn, to, toColumn);
    }

    public void RemoveStep(TransformStep step)
    {
        Steps.Remove(step);
        Rebuild();
    }

    /// <summary>Удаляет таблицу вместе с источником и всеми зависящими от неё шагами.</summary>
    public void RemoveTable(string name) => RemoveTables([name]);

    /// <summary>Удаляет несколько таблиц (например, целую папку) с одной пересборкой модели.</summary>
    public void RemoveTables(IEnumerable<string> names)
    {
        var removed = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
        Sources.RemoveAll(s => removed.Contains(s.TableName));
        Relationships.RemoveAll(r => removed.Any(r.Connects));
        foreach (var name in removed)
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
    public IReadOnlyList<string> Load(IEnumerable<DataSourceDefinition> sources, IEnumerable<TransformStep> steps,
        IEnumerable<RelationshipDefinition>? relationships = null)
    {
        Sources.Clear();
        Steps.Clear();
        Relationships.Clear();
        _raw.Clear();
        Sources.AddRange(sources);
        Steps.AddRange(steps);
        Relationships.AddRange(relationships ?? []);
        return Reload();
    }

    public void Clear()
    {
        Sources.Clear();
        Steps.Clear();
        Relationships.Clear();
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
        ValidateRelationships();
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
