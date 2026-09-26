using System.Data;
using PbForMac.Models;

namespace PbForMac.Services;

/// <summary>Общие операции над столбцами таблицы (ПКМ в гриде и панель «Модель»).</summary>
public sealed class ColumnEditor(DataModel model, IDialogService dialogs)
{
    public async Task<bool> RemoveAsync(string table, IReadOnlyList<string> columns)
    {
        if (columns.Count == 0)
            return false;
        if (!await ConfirmRemoveAsync(table, columns, keepMode: false))
            return false;
        try
        {
            if (columns.Count == 1)
                model.AddStep(new RemoveColumnStep { Table = table, Column = columns[0] });
            else
                model.AddStep(new RemoveColumnsStep { Table = table, Columns = columns.ToList() });
            return true;
        }
        catch (InvalidOperationException e)
        {
            await dialogs.ShowMessageAsync("Не удалось удалить столбцы", e.Message);
            return false;
        }
    }

    public async Task<bool> KeepOnlyAsync(string table, IReadOnlyList<string> columns)
    {
        if (columns.Count == 0)
            return false;
        var data = model.GetTable(table);
        if (data is null)
            return false;
        var removed = data.Columns.Cast<DataColumn>().Select(c => c.ColumnName)
            .Where(n => !columns.Contains(n, StringComparer.OrdinalIgnoreCase)).ToList();
        if (removed.Count == 0)
            return false;
        if (!await ConfirmRemoveAsync(table, removed, keepMode: true, keep: columns))
            return false;
        try
        {
            model.AddStep(new KeepColumnsStep { Table = table, Columns = columns.ToList() });
            return true;
        }
        catch (InvalidOperationException e)
        {
            await dialogs.ShowMessageAsync("Не удалось оставить столбцы", e.Message);
            return false;
        }
    }

    public async Task<bool> RenameAsync(string table, string column)
    {
        var name = await dialogs.PromptAsync("Переименовать столбец", "Новое имя столбца", column);
        if (name is null || name.Length == 0 || string.Equals(name, column, StringComparison.Ordinal))
            return false;
        try
        {
            model.AddStep(new RenameColumnStep { Table = table, Column = column, NewName = name });
            return true;
        }
        catch (InvalidOperationException e)
        {
            await dialogs.ShowMessageAsync("Не удалось переименовать", e.Message);
            return false;
        }
    }

    public async Task<bool> ChangeTypeAsync(string table, string column, ColumnType type)
    {
        var data = model.GetTable(table);
        if (data?.Columns[column] is { } existing && TypeInference.FromClr(existing.DataType) == type)
            return false;
        try
        {
            model.AddStep(new ChangeTypeStep { Table = table, Column = column, TargetType = type });
            return true;
        }
        catch (InvalidOperationException e)
        {
            await dialogs.ShowMessageAsync("Не удалось сменить тип", e.Message);
            return false;
        }
    }

    public async Task<bool> MoveAsync(string table, string column, int newOrdinal)
    {
        try
        {
            model.AddStep(new MoveColumnStep { Table = table, Column = column, NewOrdinal = newOrdinal });
            return true;
        }
        catch (InvalidOperationException e)
        {
            await dialogs.ShowMessageAsync("Не удалось переместить столбец", e.Message);
            return false;
        }
    }

    public async Task<bool> DuplicateAsync(string table, string column)
    {
        var data = model.GetTable(table);
        if (data is null)
            return false;
        var newName = QueryEngine.Unique(data, column + " — копия");
        try
        {
            model.AddStep(new CalculatedColumnStep
            {
                Table = table,
                Name = newName,
                Expression = $"[{column}]",
            });
            return true;
        }
        catch (InvalidOperationException e)
        {
            await dialogs.ShowMessageAsync("Не удалось дублировать столбец", e.Message);
            return false;
        }
    }

    public async Task<bool> RemoveRowsAsync(string table, IReadOnlyList<DataRow> rows)
    {
        if (rows.Count == 0)
            return false;
        var keys = rows.Select(TransformEngine.RowKey).Distinct().ToList();
        var label = keys.Count == 1 ? "Удалить 1 строку?" : $"Удалить строки ({keys.Count})?";
        if (!await dialogs.ConfirmAsync("Удалить строки", label))
            return false;
        try
        {
            model.AddStep(new RemoveRowsStep { Table = table, RowKeys = keys });
            return true;
        }
        catch (InvalidOperationException e)
        {
            await dialogs.ShowMessageAsync("Не удалось удалить строки", e.Message);
            return false;
        }
    }

    public async Task<bool> KeepRowsAsync(string table, IReadOnlyList<DataRow> rows)
    {
        if (rows.Count == 0)
            return false;
        var keys = rows.Select(TransformEngine.RowKey).Distinct().ToList();
        var data = model.GetTable(table);
        if (data is null)
            return false;
        if (keys.Count >= data.Rows.Count)
            return false;
        var label = keys.Count == 1
            ? "Оставить только 1 выбранную строку? Остальные будут удалены."
            : $"Оставить только выбранные строки ({keys.Count})? Остальные будут удалены.";
        if (!await dialogs.ConfirmAsync("Оставить выбранные строки", label))
            return false;
        try
        {
            model.AddStep(new KeepRowsStep { Table = table, RowKeys = keys });
            return true;
        }
        catch (InvalidOperationException e)
        {
            await dialogs.ShowMessageAsync("Не удалось оставить строки", e.Message);
            return false;
        }
    }

    public async Task<bool> ReplaceValuesAsync(string table, string column)
    {
        var find = await dialogs.PromptAsync("Заменить значения",
            $"Столбец «{column}»: что искать? (оставьте пустым для пустых ячеек)", "");
        if (find is null)
            return false;
        var replace = await dialogs.PromptAsync("Заменить значения",
            $"Столбец «{column}»: на что заменить «{(string.IsNullOrEmpty(find) ? "(пусто)" : find)}»?", "");
        if (replace is null)
            return false;
        try
        {
            model.AddStep(new ReplaceValuesStep
            {
                Table = table,
                Column = column,
                Find = find,
                Replace = replace,
                MatchEntireCell = true,
            });
            return true;
        }
        catch (InvalidOperationException e)
        {
            await dialogs.ShowMessageAsync("Не удалось заменить значения", e.Message);
            return false;
        }
    }

    public async Task<bool> FillDownAsync(string table, string column)
    {
        try
        {
            model.AddStep(new FillDownStep { Table = table, Column = column });
            return true;
        }
        catch (InvalidOperationException e)
        {
            await dialogs.ShowMessageAsync("Не удалось заполнить вниз", e.Message);
            return false;
        }
    }

    public async Task<bool> FillUpAsync(string table, string column)
    {
        try
        {
            model.AddStep(new FillUpStep { Table = table, Column = column });
            return true;
        }
        catch (InvalidOperationException e)
        {
            await dialogs.ShowMessageAsync("Не удалось заполнить вверх", e.Message);
            return false;
        }
    }

    public async Task<bool> SplitColumnAsync(string table, string column)
    {
        var delimiter = await dialogs.PromptAsync("Разделить столбец",
            $"Разделитель для «{column}» (например , ; | или пробел)", ",");
        if (delimiter is null || delimiter.Length == 0)
            return false;
        try
        {
            model.AddStep(new SplitColumnStep
            {
                Table = table,
                Column = column,
                Delimiter = delimiter,
            });
            return true;
        }
        catch (InvalidOperationException e)
        {
            await dialogs.ShowMessageAsync("Не удалось разделить столбец", e.Message);
            return false;
        }
    }

    public async Task<bool> RemoveBlankRowsAsync(string table)
    {
        try
        {
            model.AddStep(new RemoveBlankRowsStep { Table = table });
            return true;
        }
        catch (InvalidOperationException e)
        {
            await dialogs.ShowMessageAsync("Не удалось удалить пустые строки", e.Message);
            return false;
        }
    }

    public int? Ordinal(string table, string column) =>
        model.GetTable(table)?.Columns[column]?.Ordinal;

    public int ColumnCount(string table) =>
        model.GetTable(table)?.Columns.Count ?? 0;

    private async Task<bool> ConfirmRemoveAsync(string table, IReadOnlyList<string> columns, bool keepMode,
        IReadOnlyList<string>? keep = null)
    {
        var related = model.Relationships
            .Where(r => string.Equals(r.FromTable, table, StringComparison.OrdinalIgnoreCase)
                        && columns.Contains(r.FromColumn, StringComparer.OrdinalIgnoreCase)
                        || string.Equals(r.ToTable, table, StringComparison.OrdinalIgnoreCase)
                        && columns.Contains(r.ToColumn, StringComparer.OrdinalIgnoreCase))
            .ToList();
        var title = keepMode ? "Оставить выбранные столбцы" : "Удалить столбцы";
        var action = keepMode
            ? $"Оставить только: {string.Join(", ", keep ?? [])}. Будут удалены: {string.Join(", ", columns)}."
            : columns.Count == 1
                ? $"Удалить столбец «{columns[0]}»?"
                : $"Удалить столбцы ({columns.Count}): {string.Join(", ", columns)}?";
        if (related.Count > 0)
            action += $"\n\nЗатронутые связи перестанут работать: {string.Join("; ", related)}.";
        return await dialogs.ConfirmAsync(title, action);
    }
}
