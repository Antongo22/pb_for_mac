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

            case UnpivotColumnsStep s:
                UnpivotColumns(table, s.Columns, s.AttributeColumn, s.ValueColumn);
                break;

            case MergeTablesStep s:
                MergeTables(tables, s);
                break;

            case PivotColumnsStep s:
                PivotColumns(table, s.AttributeColumn, s.ValueColumn, s.Aggregation);
                break;

            case ConditionalColumnStep s:
                AddConditionalColumn(table, s);
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

    /// <summary>Unpivot: выбранные столбцы → строки с атрибутом и значением.</summary>
    public static void UnpivotColumns(DataTable table, IReadOnlyList<string> columns,
        string attributeColumn, string valueColumn)
    {
        if (columns.Count == 0)
            throw new InvalidOperationException("Выберите хотя бы один столбец для Unpivot.");
        foreach (var name in columns)
            RequireColumn(table, name);
        if (columns.Count >= table.Columns.Count)
            throw new InvalidOperationException("Для Unpivot должен остаться хотя бы один ключевой столбец.");

        var attrName = string.IsNullOrWhiteSpace(attributeColumn) ? "Атрибут" : attributeColumn.Trim();
        var valueName = string.IsNullOrWhiteSpace(valueColumn) ? "Значение" : valueColumn.Trim();
        if (string.Equals(attrName, valueName, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Имена столбцов атрибута и значения должны различаться.");

        var unpivot = new HashSet<string>(columns, StringComparer.OrdinalIgnoreCase);
        var keys = table.Columns.Cast<DataColumn>().Where(c => !unpivot.Contains(c.ColumnName)).ToList();
        if (keys.Any(c => string.Equals(c.ColumnName, attrName, StringComparison.OrdinalIgnoreCase)
                          || string.Equals(c.ColumnName, valueName, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"Столбцы «{attrName}» / «{valueName}» уже есть среди ключевых.");

        var result = new DataTable(table.TableName);
        foreach (var key in keys)
            result.Columns.Add(key.ColumnName, key.DataType);
        result.Columns.Add(attrName, typeof(string));
        // Значения разных типов → object/string; Detect при желании позже.
        result.Columns.Add(valueName, typeof(object));

        foreach (DataRow source in table.Rows)
        {
            foreach (var name in columns)
            {
                var row = result.NewRow();
                foreach (var key in keys)
                    row[key.ColumnName] = source[key];
                row[attrName] = name;
                row[valueName] = source[name];
                result.Rows.Add(row);
            }
        }

        table.Rows.Clear();
        table.Columns.Clear();
        foreach (DataColumn column in result.Columns)
            table.Columns.Add(column.ColumnName, column.DataType);
        foreach (DataRow row in result.Rows)
            table.ImportRow(row);
    }

    /// <summary>Merge/Join двух таблиц по ключу.</summary>
    public static void MergeTables(List<DataTable> tables, MergeTablesStep step)
    {
        var left = Find(tables, step.Table);
        var right = Find(tables, step.OtherTable);
        if (ReferenceEquals(left, right))
            throw new InvalidOperationException("Нельзя соединить таблицу саму с собой.");
        if (string.IsNullOrWhiteSpace(step.LeftKey) || string.IsNullOrWhiteSpace(step.RightKey))
            throw new InvalidOperationException("Укажите ключевые столбцы обеих таблиц.");
        RequireColumn(left, step.LeftKey);
        RequireColumn(right, step.RightKey);

        var result = new DataTable(string.IsNullOrWhiteSpace(step.NewTable) ? left.TableName : step.NewTable.Trim());
        foreach (DataColumn column in left.Columns)
            result.Columns.Add(column.ColumnName, column.DataType);

        // Правые столбцы: уникальные имена; ключ правой таблицы не дублируем, если имя совпадает.
        var rightMap = new List<(DataColumn Source, string Dest)>();
        foreach (DataColumn column in right.Columns)
        {
            if (string.Equals(column.ColumnName, step.RightKey, StringComparison.OrdinalIgnoreCase)
                && string.Equals(step.LeftKey, step.RightKey, StringComparison.OrdinalIgnoreCase))
                continue;
            var name = QueryEngine.Unique(result, column.ColumnName);
            result.Columns.Add(name, column.DataType);
            rightMap.Add((column, name));
        }

        var rightIndex = new Dictionary<string, List<DataRow>>(StringComparer.OrdinalIgnoreCase);
        foreach (DataRow row in right.Rows)
        {
            var key = QueryEngine.Key(row[step.RightKey]);
            if (!rightIndex.TryGetValue(key, out var list))
                rightIndex[key] = list = [];
            list.Add(row);
        }

        var matchedRight = new HashSet<DataRow>();
        foreach (DataRow leftRow in left.Rows)
        {
            var key = QueryEngine.Key(leftRow[step.LeftKey]);
            if (rightIndex.TryGetValue(key, out var matches))
            {
                foreach (var rightRow in matches)
                {
                    matchedRight.Add(rightRow);
                    AddMergedRow(result, leftRow, left, rightRow, rightMap);
                }
            }
            else if (step.JoinKind is JoinKind.Left or JoinKind.Full)
                AddMergedRow(result, leftRow, left, null, rightMap);
        }

        if (step.JoinKind == JoinKind.Full)
        {
            foreach (DataRow rightRow in right.Rows)
            {
                if (matchedRight.Contains(rightRow))
                    continue;
                AddMergedRow(result, null, left, rightRow, rightMap);
            }
        }

        if (!string.IsNullOrWhiteSpace(step.NewTable))
        {
            var name = step.NewTable.Trim();
            if (tables.Any(t => string.Equals(t.TableName, name, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException($"Таблица «{name}» уже существует.");
            result.TableName = name;
            tables.Add(result);
            return;
        }

        // Заменяем содержимое левой таблицы.
        left.Rows.Clear();
        left.Columns.Clear();
        foreach (DataColumn column in result.Columns)
            left.Columns.Add(column.ColumnName, column.DataType);
        foreach (DataRow row in result.Rows)
            left.ImportRow(row);
    }

    private static void AddMergedRow(DataTable result, DataRow? leftRow, DataTable leftSchema,
        DataRow? rightRow, List<(DataColumn Source, string Dest)> rightMap)
    {
        var row = result.NewRow();
        if (leftRow is not null)
        {
            foreach (DataColumn column in leftSchema.Columns)
                row[column.ColumnName] = leftRow[column];
        }
        if (rightRow is not null)
        {
            foreach (var (source, dest) in rightMap)
                row[dest] = rightRow[source];
        }
        result.Rows.Add(row);
    }

    /// <summary>Pivot: атрибут → столбцы, значение агрегируется по ключам.</summary>
    public static void PivotColumns(DataTable table, string attributeColumn, string valueColumn, Aggregation aggregation)
    {
        RequireColumn(table, attributeColumn);
        RequireColumn(table, valueColumn);
        if (string.Equals(attributeColumn, valueColumn, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Столбцы атрибута и значения должны различаться.");

        var keys = table.Columns.Cast<DataColumn>()
            .Where(c => !string.Equals(c.ColumnName, attributeColumn, StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(c.ColumnName, valueColumn, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (keys.Count == 0)
            throw new InvalidOperationException("Для Pivot нужен хотя бы один ключевой столбец.");

        var pivotNames = table.Rows.Cast<DataRow>()
            .Select(r => QueryEngine.Key(r[attributeColumn]))
            .Where(k => k.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(k => k, StringComparer.Create(System.Globalization.CultureInfo.GetCultureInfo("ru-RU"), ignoreCase: true))
            .ToList();
        if (pivotNames.Count == 0)
            throw new InvalidOperationException("Нет значений атрибута для Pivot.");

        var result = new DataTable(table.TableName);
        foreach (var key in keys)
            result.Columns.Add(key.ColumnName, key.DataType);
        foreach (var name in pivotNames)
            result.Columns.Add(QueryEngine.Unique(result, name), typeof(double));

        var groups = table.Rows.Cast<DataRow>().GroupBy(r =>
            string.Join("\u001F", keys.Select(k => QueryEngine.Key(r[k]))));

        foreach (var group in groups)
        {
            var first = group.First();
            var row = result.NewRow();
            foreach (var key in keys)
                row[key.ColumnName] = first[key];

            var byAttr = group.GroupBy(r => QueryEngine.Key(r[attributeColumn]), StringComparer.OrdinalIgnoreCase);
            foreach (var attrGroup in byAttr)
            {
                if (attrGroup.Key.Length == 0 || !result.Columns.Contains(attrGroup.Key))
                    continue;
                var value = QueryEngine.Aggregate(attrGroup.Select(r => r[valueColumn]), aggregation);
                row[attrGroup.Key] = double.IsNaN(value) ? DBNull.Value : value;
            }
            result.Rows.Add(row);
        }

        table.Rows.Clear();
        table.Columns.Clear();
        foreach (DataColumn column in result.Columns)
            table.Columns.Add(column.ColumnName, column.DataType);
        foreach (DataRow row in result.Rows)
            table.ImportRow(row);
    }

    public static void AddConditionalColumn(DataTable table, ConditionalColumnStep step)
    {
        var name = step.Name?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException("Укажите имя столбца.");
        if (table.Columns.Contains(name))
            throw new InvalidOperationException($"Столбец «{name}» уже существует.");
        if (step.Rules.Count == 0)
            throw new InvalidOperationException("Добавьте хотя бы одно правило.");

        var predicates = new List<(Func<DataRow, bool> Test, string Output)>();
        foreach (var rule in step.Rules)
        {
            if (string.IsNullOrWhiteSpace(rule.Column))
                throw new InvalidOperationException("В правиле не указан столбец.");
            RequireColumn(table, rule.Column);
            var filter = new FilterDefinition
            {
                Table = table.TableName,
                Column = rule.Column,
                Operator = rule.Operator,
                Value = rule.Value,
            };
            predicates.Add((QueryEngine.BuildPredicate(table, filter), rule.Output ?? ""));
        }

        var column = table.Columns.Add(name, typeof(string));
        foreach (DataRow row in table.Rows)
        {
            var matched = false;
            foreach (var (test, output) in predicates)
            {
                if (!test(row))
                    continue;
                row[column] = output;
                matched = true;
                break;
            }
            if (!matched)
                row[column] = step.ElseValue ?? "";
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
