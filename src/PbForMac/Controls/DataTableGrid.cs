using System.Data;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PbForMac.Models;
using PbForMac.Services;

namespace PbForMac.Controls;

/// <summary>Срез таблицы для отображения: таблица и отобранные строки.</summary>
public sealed record TableSlice(DataTable Table, IReadOnlyList<DataRow> Rows);

/// <summary>Строка грида: значения форматируются лениво, только для видимых строк.</summary>
public sealed class RowItem(DataRow row)
{
    private string[]? _cells;

    public DataRow Row { get; } = row;

    public string[] Cells => _cells ??= Row.ItemArray.Select(ValueFormatter.Display).ToArray();
}

/// <summary>
/// Обработчик ПКМ по заголовку столбца (удаление, тип, переименование и т. п.).
/// </summary>
public interface IColumnHeaderActions
{
    /// <summary>Можно ли менять схему таблицы (на визуале отчёта — нет).</summary>
    bool CanEdit { get; }

    /// <summary>Имя текущей таблицы модели; null — меню редактирования скрыто.</summary>
    string? TableName { get; }

    ColumnEditor? Editor { get; }

    void SelectColumn(string column);
}

/// <summary>
/// DataGrid для произвольной <see cref="DataTable"/>: столбцы генерируются по схеме,
/// сортировка выполняется по исходным (типизированным) значениям.
/// ПКМ по заголовку — меню столбца; ПКМ по строке / Delete — удаление строк.
/// </summary>
public sealed class DataTableGrid : UserControl
{
    public static readonly StyledProperty<TableSlice?> SourceProperty =
        AvaloniaProperty.Register<DataTableGrid, TableSlice?>(nameof(Source));

    public static readonly StyledProperty<IColumnHeaderActions?> ColumnActionsProperty =
        AvaloniaProperty.Register<DataTableGrid, IColumnHeaderActions?>(nameof(ColumnActions));

    private readonly DataGrid _grid = new()
    {
        IsReadOnly = true,
        CanUserReorderColumns = true,
        CanUserResizeColumns = true,
        CanUserSortColumns = true,
        GridLinesVisibility = DataGridGridLinesVisibility.All,
        HeadersVisibility = DataGridHeadersVisibility.Column,
        SelectionMode = DataGridSelectionMode.Extended,
        ClipboardCopyMode = DataGridClipboardCopyMode.IncludeHeader,
        Focusable = true,
    };

    private int _sortColumn = -1;
    private bool _sortDescending;

    public DataTableGrid()
    {
        Content = _grid;
        _grid.Sorting += OnSorting;
        _grid.AddHandler(ContextRequestedEvent, OnContextRequested, RoutingStrategies.Tunnel);
        _grid.KeyDown += OnKeyDown;
    }

    public TableSlice? Source
    {
        get => GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    public IColumnHeaderActions? ColumnActions
    {
        get => GetValue(ColumnActionsProperty);
        set => SetValue(ColumnActionsProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SourceProperty)
            Rebuild(change.GetOldValue<TableSlice?>()?.Table != change.GetNewValue<TableSlice?>()?.Table);
    }

    private void Rebuild(bool schemaChanged)
    {
        var source = Source;
        if (source is null)
        {
            _grid.ItemsSource = null;
            _grid.Columns.Clear();
            return;
        }

        var columns = source.Table.Columns.Cast<DataColumn>().ToList();
        if (schemaChanged || _grid.Columns.Count != columns.Count
            || columns.Where((c, i) => !Equals(_grid.Columns[i].Tag, c.ColumnName)).Any())
        {
            _sortColumn = -1;
            _grid.Columns.Clear();
            for (var i = 0; i < columns.Count; i++)
            {
                _grid.Columns.Add(new DataGridTextColumn
                {
                    Header = columns[i].ColumnName,
                    Tag = columns[i].ColumnName,
                    Binding = new Binding($"Cells[{i}]", BindingMode.OneTime),
                    SortMemberPath = columns[i].ColumnName,
                    MaxWidth = 360,
                });
            }
        }

        IEnumerable<DataRow> rows = source.Rows;
        if (_sortColumn >= 0 && _sortColumn < columns.Count)
        {
            var index = _sortColumn;
            var comparer = Comparer<object>.Create(QueryEngine.Compare);
            rows = _sortDescending
                ? rows.OrderByDescending(r => r[index], comparer)
                : rows.OrderBy(r => r[index], comparer);
        }
        _grid.ItemsSource = rows.Select(r => new RowItem(r)).ToList();
    }

    private void OnSorting(object? sender, DataGridColumnEventArgs e)
    {
        e.Handled = true;
        var index = _grid.Columns.IndexOf(e.Column);
        _sortDescending = index == _sortColumn && !_sortDescending;
        _sortColumn = index;

        if (e.Column.Tag is string name)
            ColumnActions?.SelectColumn(name);

        for (var i = 0; i < _grid.Columns.Count; i++)
        {
            var columnName = (string)_grid.Columns[i].Tag!;
            _grid.Columns[i].Header = i == index ? $"{columnName} {(_sortDescending ? "▼" : "▲")}" : columnName;
        }
        Rebuild(schemaChanged: false);
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Delete or Key.Back))
            return;
        if (ColumnActions is not { CanEdit: true, TableName: { } table, Editor: { } editor })
            return;
        var rows = SelectedRows();
        if (rows.Count == 0)
            return;
        e.Handled = true;
        _ = editor.RemoveRowsAsync(table, rows);
    }

    private void OnContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (e.Source is not Visual source)
            return;

        var header = source.FindAncestorOfType<DataGridColumnHeader>(includeSelf: true);
        if (header is not null)
        {
            if (ResolveColumnName(header) is not { } columnName)
                return;
            ColumnActions?.SelectColumn(columnName);
            var columnMenu = BuildColumnMenu(columnName);
            if (columnMenu.Items.Count == 0)
                return;
            e.Handled = true;
            columnMenu.Open(header);
            return;
        }

        var row = source.FindAncestorOfType<DataGridRow>(includeSelf: true);
        if (row is null)
            return;

        // ПКМ по невыделенной строке — выделить только её.
        if (row.DataContext is RowItem item && !_grid.SelectedItems.Contains(item))
        {
            _grid.SelectedItems.Clear();
            _grid.SelectedItems.Add(item);
        }

        var rows = SelectedRows();
        if (rows.Count == 0)
            return;

        var menu = BuildRowMenu(rows);
        if (menu.Items.Count == 0)
            return;
        e.Handled = true;
        menu.Open(row);
    }

    private List<DataRow> SelectedRows() =>
        _grid.SelectedItems.OfType<RowItem>().Select(r => r.Row).ToList();

    /// <summary>Имя столбца по заголовку: Content совпадает с Tag или «Tag ▲/▼».</summary>
    private string? ResolveColumnName(DataGridColumnHeader header)
    {
        var content = header.Content?.ToString() ?? "";
        foreach (var column in _grid.Columns)
        {
            if (column.Tag is not string name)
                continue;
            if (content == name || content.StartsWith(name + " ", StringComparison.Ordinal))
                return name;
        }
        return null;
    }

    private ContextMenu BuildColumnMenu(string column)
    {
        var menu = new ContextMenu();
        var actions = ColumnActions;

        menu.Items.Add(Item("Копировать имя", async () =>
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard is not null)
                await clipboard.SetTextAsync(column);
        }));

        if (actions is not { CanEdit: true, TableName: { } table, Editor: { } editor })
            return menu;

        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Удалить столбец", () => editor.RemoveAsync(table, [column])));
        menu.Items.Add(Item("Оставить только этот столбец", () => editor.KeepOnlyAsync(table, [column])));
        menu.Items.Add(Item("Переименовать…", () => editor.RenameAsync(table, column)));
        menu.Items.Add(Item("Дублировать столбец", () => editor.DuplicateAsync(table, column)));

        var typeMenu = new MenuItem { Header = "Сменить тип" };
        foreach (var option in Labels.ColumnTypes)
        {
            var type = option.Value;
            typeMenu.Items.Add(Item(option.Label, () => editor.ChangeTypeAsync(table, column, type)));
        }
        menu.Items.Add(typeMenu);

        var ordinal = editor.Ordinal(table, column);
        var count = editor.ColumnCount(table);
        if (ordinal is { } index && count > 1)
        {
            var moveMenu = new MenuItem { Header = "Переместить" };
            if (index > 0)
            {
                moveMenu.Items.Add(Item("В начало", () => editor.MoveAsync(table, column, 0)));
                moveMenu.Items.Add(Item("Влево", () => editor.MoveAsync(table, column, index - 1)));
            }
            if (index < count - 1)
            {
                moveMenu.Items.Add(Item("Вправо", () => editor.MoveAsync(table, column, index + 1)));
                moveMenu.Items.Add(Item("В конец", () => editor.MoveAsync(table, column, count - 1)));
            }
            if (moveMenu.Items.Count > 0)
            {
                menu.Items.Add(new Separator());
                menu.Items.Add(moveMenu);
            }
        }

        return menu;
    }

    private ContextMenu BuildRowMenu(IReadOnlyList<DataRow> rows)
    {
        var menu = new ContextMenu();
        var label = rows.Count == 1 ? "Удалить строку" : $"Удалить строки ({rows.Count})";
        var keepLabel = rows.Count == 1 ? "Оставить только эту строку" : $"Оставить только выбранные ({rows.Count})";

        if (ColumnActions is not { CanEdit: true, TableName: { } table, Editor: { } editor })
            return menu;

        menu.Items.Add(Item(label, () => editor.RemoveRowsAsync(table, rows)));
        menu.Items.Add(Item(keepLabel, () => editor.KeepRowsAsync(table, rows)));
        return menu;
    }

    private static MenuItem Item(string header, Func<Task> action)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => Dispatcher.UIThread.Post(() => _ = action());
        return item;
    }
}
