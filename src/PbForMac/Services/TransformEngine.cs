using System.Data;
using PbForMac.Models;

namespace PbForMac.Services;

/// <summary>Применяет шаги преобразования к таблицам модели.</summary>
public static class TransformEngine
{
    /// <summary>Применяет шаг к списку таблиц (таблицы изменяются на месте, GroupBy добавляет новую).</summary>
    public static void Apply(List<DataTable> tables, TransformStep step)
    {
        var table = Find(tables, step.Table);
        switch (step)
        {
            case RenameColumnStep s:
                RequireColumn(table, s.Column);
                if (string.IsNullOrWhiteSpace(s.NewName))
                    throw new InvalidOperationException("Новое имя столбца не может быть пустым.");
                if (!string.Equals(s.Column, s.NewName, StringComparison.OrdinalIgnoreCase) && table.Columns.Contains(s.NewName))
                    throw new InvalidOperationException($"Столбец «{s.NewName}» уже существует.");
                table.Columns[s.Column]!.ColumnName = s.NewName.Trim();
                break;

            case RemoveColumnStep s:
                RequireColumn(table, s.Column);
                table.Columns.Remove(s.Column);
                break;

            case RemoveColumnsStep s:
                RemoveColumns(table, s.Columns);
                break;

            case KeepColumnsStep s:
                KeepColumns(table, s.Columns);
                break;

            case MoveColumnStep s:
                MoveColumn(table, s.Column, s.NewOrdinal);
                break;

            case ChangeTypeStep s:
                ChangeType(table, s.Column, s.TargetType);
                break;

            case CalculatedColumnStep s:
                AddCalculatedColumn(table, s.Name, s.Expression);
                break;

            case FilterRowsStep s:
                s.Filter.Table = table.TableName;
                var predicate = QueryEngine.BuildPredicate(table, s.Filter);
                foreach (var row in table.Rows.Cast<DataRow>().Where(r => !predicate(r)).ToList())
                    table.Rows.Remove(row);
                break;

            case RemoveDuplicatesStep:
                var seen = new HashSet<string>();
                foreach (var row in table.Rows.Cast<DataRow>().ToList())
                {
                    if (!seen.Add(string.Join("\u001F", row.ItemArray.Select(QueryEngine.Key))))
                        table.Rows.Remove(row);
                }
                break;

            case GroupByStep s:
                if (string.IsNullOrWhiteSpace(s.NewTable))
                    throw new InvalidOperationException("Укажите имя новой таблицы.");
                if (tables.Any(t => string.Equals(t.TableName, s.NewTable, StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidOperationException($"Таблица «{s.NewTable}» уже существует.");
                tables.Add(QueryEngine.GroupBy(table, s.GroupColumns, s.Aggregations, s.NewTable.Trim()));
                break;

            default:
                throw new NotSupportedException($"Неизвестный шаг: {step.GetType().Name}");
        }

        table.AcceptChanges();
    }

    public static void RemoveColumns(DataTable table, IReadOnlyList<string> columns)
    {
        if (columns.Count == 0)
            throw new InvalidOperationException("Выберите хотя бы один столбец для удаления.");
        foreach (var name in columns)
            RequireColumn(table, name);
        if (columns.Count >= table.Columns.Count)
            throw new InvalidOperationException("Нельзя удалить все столбцы таблицы.");
        foreach (var name in columns)
            table.Columns.Remove(name);
    }

    public static void KeepColumns(DataTable table, IReadOnlyList<string> columns)
    {
        if (columns.Count == 0)
            throw new InvalidOperationException("Выберите хотя бы один столбец, который нужно оставить.");
        foreach (var name in columns)
            RequireColumn(table, name);
        var keep = new HashSet<string>(columns, StringComparer.OrdinalIgnoreCase);
        foreach (var name in table.Columns.Cast<DataColumn>().Select(c => c.ColumnName).Where(n => !keep.Contains(n)).ToList())
            table.Columns.Remove(name);
        // Порядок как в списке «оставить».
        for (var i = 0; i < columns.Count; i++)
            table.Columns[columns[i]]!.SetOrdinal(i);
    }

    public static void MoveColumn(DataTable table, string columnName, int newOrdinal)
    {
        var column = RequireColumn(table, columnName);
        if (newOrdinal < 0 || newOrdinal >= table.Columns.Count)
            throw new InvalidOperationException($"Позиция столбца должна быть от 1 до {table.Columns.Count}.");
        column.SetOrdinal(newOrdinal);
    }

    public static void ChangeType(DataTable table, string columnName, ColumnType type)
    {
        var old = RequireColumn(table, columnName);
        var ordinal = old.Ordinal;
        var temp = table.Columns.Add(QueryEngine.Unique(table, columnName + "__new"), TypeInference.ClrType(type));
        foreach (DataRow row in table.Rows)
            row[temp] = TypeInference.Convert(row[old], type);
        table.Columns.Remove(old);
        temp.ColumnName = columnName;
        temp.SetOrdinal(ordinal);
    }

    /// <summary>
    /// Добавляет столбец, вычисленный по выражению <see cref="DataColumn.Expression"/>,
    /// и «материализует» значения, чтобы столбец не зависел от последующих шагов.
    /// </summary>
    public static void AddCalculatedColumn(DataTable table, string name, string expression)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException("Укажите имя столбца.");
        if (table.Columns.Contains(name))
            throw new InvalidOperationException($"Столбец «{name}» уже существует.");
        if (string.IsNullOrWhiteSpace(expression))
            throw new InvalidOperationException("Введите выражение.");

        DataColumn calc;
        try
        {
            calc = table.Columns.Add(QueryEngine.Unique(table, "__calc"), typeof(object), expression);
        }
        catch (Exception e) when (e is EvaluateException or SyntaxErrorException or InvalidExpressionException or ArgumentException)
        {
            throw new InvalidOperationException($"Ошибка в выражении: {e.Message}", e);
        }

        try
        {
            var values = table.Rows.Cast<DataRow>().Select(r => r[calc]).ToList();
            var type = TypeInference.Detect(values);
            // Результат арифметики над дробными числами остаётся дробным, даже если значения целые.
            if (type == ColumnType.Integer && values.Any(v => v is double or float or decimal))
                type = ColumnType.Decimal;
            var result = table.Columns.Add(name.Trim(), TypeInference.ClrType(type));
            for (var i = 0; i < values.Count; i++)
                table.Rows[i][result] = TypeInference.Convert(values[i], type);
        }
        catch (Exception e) when (e is EvaluateException or InvalidCastException or FormatException)
        {
            throw new InvalidOperationException($"Ошибка вычисления: {e.Message}", e);
        }
        finally
        {
            table.Columns.Remove(calc);
        }
    }

    public static DataTable Find(IEnumerable<DataTable> tables, string name) =>
        tables.FirstOrDefault(t => string.Equals(t.TableName, name, StringComparison.OrdinalIgnoreCase))
        ?? throw new InvalidOperationException($"Таблица «{name}» не найдена.");

    private static DataColumn RequireColumn(DataTable table, string name) =>
        table.Columns[name] ?? throw new InvalidOperationException($"Столбец «{name}» не найден в таблице «{table.TableName}».");
}
