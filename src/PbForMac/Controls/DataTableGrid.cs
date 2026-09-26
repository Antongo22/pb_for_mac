using System.Data;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
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
/// DataGrid для произвольной <see cref="DataTable"/>: столбцы генерируются по схеме,
/// сортировка выполняется по исходным (типизированным) значениям.
/// </summary>
public sealed class DataTableGrid : UserControl
{
    public static readonly StyledProperty<TableSlice?> SourceProperty =
        AvaloniaProperty.Register<DataTableGrid, TableSlice?>(nameof(Source));

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
    };

    private int _sortColumn = -1;
    private bool _sortDescending;

    public DataTableGrid()
    {
        Content = _grid;
        _grid.Sorting += OnSorting;
    }

    public TableSlice? Source
    {
        get => GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
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

        for (var i = 0; i < _grid.Columns.Count; i++)
        {
            var name = (string)_grid.Columns[i].Tag!;
            _grid.Columns[i].Header = i == index ? $"{name} {(_sortDescending ? "▼" : "▲")}" : name;
        }
        Rebuild(schemaChanged: false);
    }
}
