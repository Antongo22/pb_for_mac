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

            case RemoveRowsStep s:
                RemoveRowsByKeys(table, s.RowKeys, keep: false);
                break;

            case KeepRowsStep s:
                RemoveRowsByKeys(table, s.RowKeys, keep: true);
                break;

            case RemoveDuplicatesStep:
                var seen = new HashSet<string>();
                foreach (var row in table.Rows.Cast<DataRow>().ToList())
                {
                    if (!seen.Add(RowKey(row)))
                        table.Rows.Remove(row);
                }
                break;

            case ReplaceValuesStep s:
                ReplaceValues(table, s.Column, s.Find, s.Replace, s.MatchEntireCell);
                break;

            case FillDownStep s:
                FillColumn(table, s.Column, downward: true);
                break;

            case FillUpStep s:
                FillColumn(table, s.Column, downward: false);
                break;

            case RemoveBlankRowsStep s:
                RemoveBlankRows(table, s.Columns);
                break;

            case SplitColumnStep s:
                SplitColumn(table, s.Column, s.Delimiter, s.MaxParts);
                break;

            case SortRowsStep s:
                SortRows(table, s.Column, s.Descending);
                break;

            case TextTransformStep s:
                TransformText(table, s.Column, s.Kind);
                break;

            case AppendTableStep s:
                AppendTable(tables, s);
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

    public static string RowKey(DataRow row) =>
        string.Join("\u001F", row.ItemArray.Select(QueryEngine.Key));

    public static void RemoveRowsByKeys(DataTable table, IReadOnlyList<string> keys, bool keep)
    {
        if (keys.Count == 0)
            throw new InvalidOperationException(keep ? "Не выбраны строки, которые нужно оставить." : "Не выбраны строки для удаления.");
        var set = new HashSet<string>(keys);
        foreach (var row in table.Rows.Cast<DataRow>().ToList())
        {
            var match = set.Contains(RowKey(row));
            if (keep ? !match : match)
                table.Rows.Remove(row);
        }
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

    /// <summary>Перестраивает порядок строк таблицы по столбцу (стабильная сортировка).</summary>
    public static void SortRows(DataTable table, string columnName, bool descending)
    {
        var column = RequireColumn(table, columnName);
        var comparer = Comparer<object>.Create(QueryEngine.Compare);
        var ordered = descending
            ? table.Rows.Cast<DataRow>().OrderByDescending(r => r[column], comparer).ToList()
            : table.Rows.Cast<DataRow>().OrderBy(r => r[column], comparer).ToList();
        var clone = table.Clone();
        foreach (var row in ordered)
            clone.ImportRow(row);
        table.Rows.Clear();
        foreach (DataRow row in clone.Rows)
            table.ImportRow(row);
    }

    public static void TransformText(DataTable table, string columnName, TextTransformKind kind)
    {
        var column = RequireColumn(table, columnName);
        if (column.DataType != typeof(string) && TypeInference.FromClr(column.DataType) != ColumnType.Text)
        {
            // Приводим к тексту на месте, если ещё не текст.
            ChangeType(table, columnName, ColumnType.Text);
            column = RequireColumn(table, columnName);
        }

        foreach (DataRow row in table.Rows)
        {
            if (TypeInference.IsEmpty(row[column]))
                continue;
            var text = Convert.ToString(row[column]) ?? "";
            row[column] = kind switch
            {
                TextTransformKind.Trim => text.Trim(),
                TextTransformKind.Upper => text.ToUpperInvariant(),
                TextTransformKind.Lower => text.ToLowerInvariant(),
                TextTransformKind.Clean => new string(text.Where(c => !char.IsControl(c)).ToArray()),
                _ => text,
            };
        }
    }

    /// <summary>Добавляет строки other в target; при NewTable создаёт новую таблицу.</summary>
    public static void AppendTable(List<DataTable> tables, AppendTableStep step)
    {
        var target = Find(tables, step.Table);
        var other = Find(tables, step.OtherTable);
        if (ReferenceEquals(target, other))
            throw new InvalidOperationException("Нельзя добавить таблицу саму в себя.");

        DataTable destination;
        if (!string.IsNullOrWhiteSpace(step.NewTable))
        {
            var name = step.NewTable.Trim();
            if (tables.Any(t => string.Equals(t.TableName, name, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException($"Таблица «{name}» уже существует.");
            destination = target.Clone();
            destination.TableName = name;
            foreach (DataRow row in target.Rows)
                destination.ImportRow(row);
            tables.Add(destination);
        }
        else
            destination = target;

        // Добавляем столбцы, которых нет в destination.
        foreach (DataColumn column in other.Columns)
        {
            if (!destination.Columns.Contains(column.ColumnName))
                destination.Columns.Add(column.ColumnName, column.DataType);
        }

        foreach (DataRow source in other.Rows)
        {
            var row = destination.NewRow();
            foreach (DataColumn column in other.Columns)
            {
                var value = source[column];
                row[column.ColumnName] = value == DBNull.Value ? DBNull.Value : value;
            }
            destination.Rows.Add(row);
        }
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
    public static void ReplaceValues(DataTable table, string columnName, string find, string replace, bool matchEntireCell)
    {
        var column = RequireColumn(table, columnName);
        var type = TypeInference.FromClr(column.DataType);
        var findEmpty = string.IsNullOrEmpty(find);
        object? replacement = string.IsNullOrEmpty(replace)
            ? DBNull.Value
            : TypeInference.Convert(replace, type);

        foreach (DataRow row in table.Rows)
        {
            var current = row[column];
            if (matchEntireCell)
            {
                var matches = findEmpty
                    ? TypeInference.IsEmpty(current)
                    : string.Equals(QueryEngine.Key(current), find, StringComparison.OrdinalIgnoreCase);
                if (matches)
                    row[column] = replacement ?? DBNull.Value;
            }
            else
            {
                if (TypeInference.IsEmpty(current))
                    continue;
                var text = QueryEngine.Key(current);
                if (!text.Contains(find, StringComparison.OrdinalIgnoreCase))
                    continue;
                var updated = text.Replace(find, replace ?? "", StringComparison.OrdinalIgnoreCase);
                row[column] = string.IsNullOrEmpty(updated)
                    ? DBNull.Value
                    : TypeInference.Convert(updated, type);
            }
        }
    }

    public static void FillColumn(DataTable table, string columnName, bool downward)
    {
        var column = RequireColumn(table, columnName);
        object? carry = null;
        var rows = table.Rows.Cast<DataRow>();
        foreach (var row in downward ? rows : rows.Reverse())
        {
            var value = row[column];
            if (TypeInference.IsEmpty(value))
            {
                if (carry is not null)
                    row[column] = carry;
            }
            else
            {
                carry = value is ICloneable cloneable ? cloneable.Clone() : value;
            }
        }
    }

    public static void RemoveBlankRows(DataTable table, IReadOnlyList<string> columns)
    {
        DataColumn[] targets;
        if (columns.Count == 0)
        {
            targets = table.Columns.Cast<DataColumn>().ToArray();
        }
        else
        {
            foreach (var name in columns)
                RequireColumn(table, name);
            targets = columns.Select(n => table.Columns[n]!).ToArray();
        }

        if (targets.Length == 0)
            return;

        foreach (var row in table.Rows.Cast<DataRow>().ToList())
        {
            if (targets.All(c => TypeInference.IsEmpty(row[c])))
                table.Rows.Remove(row);
        }
    }

    public static void SplitColumn(DataTable table, string columnName, string delimiter, int maxParts)
    {
        var column = RequireColumn(table, columnName);
        if (string.IsNullOrEmpty(delimiter))
            throw new InvalidOperationException("Укажите разделитель.");

        var ordinal = column.Ordinal;
        var parts = table.Rows.Cast<DataRow>()
            .Select(r => SplitText(QueryEngine.Key(r[column]), delimiter, maxParts))
            .ToList();
        var count = parts.Count == 0 ? 1 : Math.Max(1, parts.Max(p => p.Length));
        if (maxParts > 0)
            count = Math.Min(count, maxParts);

        var newColumns = new DataColumn[count];
        for (var i = 0; i < count; i++)
        {
            var name = QueryEngine.Unique(table, $"{columnName}.{i + 1}");
            newColumns[i] = table.Columns.Add(name, typeof(string));
        }

        for (var r = 0; r < table.Rows.Count; r++)
        {
            var split = parts[r];
            for (var i = 0; i < count; i++)
                table.Rows[r][newColumns[i]] = i < split.Length && split[i].Length > 0
                    ? split[i]
                    : DBNull.Value;
        }

        table.Columns.Remove(column);
        for (var i = 0; i < newColumns.Length; i++)
            newColumns[i].SetOrdinal(ordinal + i);
    }

    private static string[] SplitText(string text, string delimiter, int maxParts)
    {
        if (string.IsNullOrEmpty(text))
            return [""];
        if (maxParts > 0)
            return text.Split(delimiter, maxParts, StringSplitOptions.None);
        return text.Split(delimiter, StringSplitOptions.None);
    }

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
