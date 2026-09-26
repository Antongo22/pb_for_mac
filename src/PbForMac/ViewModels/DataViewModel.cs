using System.Collections.ObjectModel;
using System.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PbForMac.Controls;
using PbForMac.Models;
using PbForMac.Services;

namespace PbForMac.ViewModels;

/// <summary>Вкладка «Данные»: просмотр таблиц, поиск, фильтры и профиль столбца.</summary>
public sealed partial class DataViewModel : ViewModelBase
{
    private readonly DataModel _model;
    private readonly IDialogService _dialogs;
    private List<DataRow> _visibleRows = [];

    public DataViewModel(DataModel model, IDialogService dialogs, IRelayCommand importCommand, IRelayCommand importFolderCommand)
    {
        _model = model;
        _dialogs = dialogs;
        ImportCommand = importCommand;
        ImportFolderCommand = importFolderCommand;
        _selectedOperator = Labels.FilterOperators[0];
        _model.Changed += (_, _) => OnModelChanged();
    }

    public IRelayCommand ImportCommand { get; }
    public IRelayCommand ImportFolderCommand { get; }
    public ObservableCollection<TableItem> Tables { get; } = [];
    public ObservableCollection<string> Columns { get; } = [];
    public ObservableCollection<FilterItemViewModel> Filters { get; } = [];
    public ObservableCollection<StatItem> Profile { get; } = [];
    public IReadOnlyList<Option<FilterOperator>> FilterOperators => Labels.FilterOperators;

    public bool HasTables => Tables.Count > 0;
    public bool HasFilters => Filters.Count > 0;

    [ObservableProperty]
    private TableItem? _selectedTable;

    [ObservableProperty]
    private TableSlice? _slice;

    [ObservableProperty]
    private string _searchText = "";

    [ObservableProperty]
    private string _rowInfo = "";

    [ObservableProperty]
    private string? _filterColumn;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FilterNeedsValue))]
    private Option<FilterOperator> _selectedOperator;

    [ObservableProperty]
    private string? _filterValue;

    [ObservableProperty]
    private string? _profileColumn;

    [ObservableProperty]
    private string? _error;

    public bool FilterNeedsValue => Labels.OperatorNeedsValue(SelectedOperator.Value);

    private DataTable? CurrentTable => _model.GetTable(SelectedTable?.Name);

    public void SelectTable(string name) =>
        SelectedTable = Tables.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase)) ?? SelectedTable;

    private void OnModelChanged()
    {
        var selected = SelectedTable?.Name;
        Tables.Clear();
        foreach (var table in _model.Tables)
            Tables.Add(new TableItem(table.TableName, $"{table.Rows.Count:#,0} строк · {table.Columns.Count} столбцов"));
        OnPropertyChanged(nameof(HasTables));

        SelectedTable = Tables.FirstOrDefault(t => t.Name == selected) ?? Tables.FirstOrDefault();
        // Таблица могла пересобраться с тем же именем — обновляем данные в любом случае.
        LoadTable();
    }

    partial void OnSelectedTableChanged(TableItem? value) => LoadTable();

    partial void OnSearchTextChanged(string value) => ApplyView();

    partial void OnProfileColumnChanged(string? value) => UpdateProfile();

    private void LoadTable()
    {
        var table = CurrentTable;
        var columns = table?.Columns.Cast<DataColumn>().Select(c => c.ColumnName).ToList() ?? [];
        if (!columns.SequenceEqual(Columns))
        {
            Columns.Clear();
            foreach (var column in columns)
                Columns.Add(column);
            // Фильтры по отсутствующим столбцам больше не применимы.
            foreach (var filter in Filters.Where(f => !columns.Contains(f.Definition.Column)).ToList())
                Filters.Remove(filter);
            OnPropertyChanged(nameof(HasFilters));
        }
        if (FilterColumn is null || !Columns.Contains(FilterColumn))
            FilterColumn = Columns.FirstOrDefault();
        if (ProfileColumn is null || !Columns.Contains(ProfileColumn))
            ProfileColumn = Columns.FirstOrDefault();
        ApplyView();
    }

    private void ApplyView()
    {
        Error = null;
        var table = CurrentTable;
        if (table is null)
        {
            Slice = null;
            _visibleRows = [];
            RowInfo = "";
            UpdateProfile();
            return;
        }

        IEnumerable<DataRow> rows;
        try
        {
            rows = QueryEngine.Filter(table, Filters.Select(f => f.Definition)).ToList();
        }
        catch (InvalidOperationException e)
        {
            Error = e.Message;
            rows = table.Rows.Cast<DataRow>();
        }

        var search = SearchText.Trim();
        if (search.Length > 0)
        {
            rows = rows.Where(r => r.ItemArray.Any(v =>
                ValueFormatter.Display(v).Contains(search, StringComparison.OrdinalIgnoreCase)));
        }

        _visibleRows = rows.ToList();
        Slice = new TableSlice(table, _visibleRows);
        RowInfo = _visibleRows.Count == table.Rows.Count
            ? $"{table.Rows.Count:#,0} строк"
            : $"{_visibleRows.Count:#,0} из {table.Rows.Count:#,0} строк";
        UpdateProfile();
    }

    private void UpdateProfile()
    {
        Profile.Clear();
        var table = CurrentTable;
        var column = ProfileColumn is null ? null : table?.Columns[ProfileColumn];
        if (column is null)
            return;

        var values = _visibleRows.Select(r => r[column]).ToList();
        var type = TypeInference.FromClr(column.DataType);
        var filled = values.Where(v => !TypeInference.IsEmpty(v)).ToList();

        Profile.Add(new StatItem("Тип", Labels.Of(type)));
        Profile.Add(new StatItem("Строк", $"{values.Count:#,0}"));
        Profile.Add(new StatItem("Пустых", $"{values.Count - filled.Count:#,0}"));
        Profile.Add(new StatItem("Уникальных", $"{filled.Select(QueryEngine.Key).Distinct().Count():#,0}"));

        if (filled.Count == 0)
            return;
        if (TypeInference.IsNumeric(type))
        {
            Profile.Add(new StatItem("Сумма", ValueFormatter.Number(QueryEngine.Aggregate(filled, Aggregation.Sum))));
            Profile.Add(new StatItem("Среднее", ValueFormatter.Number(QueryEngine.Aggregate(filled, Aggregation.Average))));
        }
        if (type != ColumnType.Boolean)
        {
            var comparer = Comparer<object>.Create(QueryEngine.Compare);
            Profile.Add(new StatItem("Минимум", ValueFormatter.Display(filled.Min(comparer))));
            Profile.Add(new StatItem("Максимум", ValueFormatter.Display(filled.Max(comparer))));
        }

        var top = filled.GroupBy(QueryEngine.Key).OrderByDescending(g => g.Count()).First();
        Profile.Add(new StatItem("Частое значение", $"{ValueFormatter.Display(top.First())} ({top.Count():#,0})"));
    }

    [RelayCommand]
    private void AddFilter()
    {
        var table = CurrentTable;
        if (table is null || FilterColumn is null)
            return;
        var definition = new FilterDefinition
        {
            Table = table.TableName,
            Column = FilterColumn,
            Operator = SelectedOperator.Value,
            Value = FilterNeedsValue ? FilterValue : null,
        };
        try
        {
            QueryEngine.BuildPredicate(table, definition);
        }
        catch (InvalidOperationException e)
        {
            Error = e.Message;
            return;
        }
        Filters.Add(new FilterItemViewModel(definition, RemoveFilter));
        OnPropertyChanged(nameof(HasFilters));
        FilterValue = null;
        ApplyView();
    }

    private void RemoveFilter(FilterItemViewModel item)
    {
        Filters.Remove(item);
        OnPropertyChanged(nameof(HasFilters));
        ApplyView();
    }

    [RelayCommand]
    private void ClearFilters()
    {
        Filters.Clear();
        SearchText = "";
        OnPropertyChanged(nameof(HasFilters));
        ApplyView();
    }

    /// <summary>Превращает текущие фильтры в шаги преобразования (строки удаляются из модели).</summary>
    [RelayCommand]
    private void ApplyFiltersAsSteps()
    {
        var table = CurrentTable;
        if (table is null || Filters.Count == 0)
            return;
        try
        {
            foreach (var filter in Filters.ToList())
            {
                _model.AddStep(new FilterRowsStep { Table = table.TableName, Filter = filter.Definition.Clone() });
                Filters.Remove(filter);
            }
        }
        catch (InvalidOperationException e)
        {
            Error = e.Message;
        }
        OnPropertyChanged(nameof(HasFilters));
        ApplyView();
    }

    [RelayCommand]
    private async Task RemoveTableAsync()
    {
        var table = SelectedTable;
        if (table is null)
            return;
        if (await _dialogs.ConfirmAsync("Удалить таблицу",
                $"Удалить таблицу «{table.Name}» и все связанные с ней шаги преобразований?"))
            _model.RemoveTable(table.Name);
    }

    [RelayCommand]
    private async Task ExportCsvAsync()
    {
        var table = CurrentTable;
        if (table is null)
            return;
        var path = await _dialogs.SaveFileAsync("Экспорт в CSV", table.TableName + ".csv",
            new FileTypeFilter("CSV", [".csv"]));
        if (path is null)
            return;
        try
        {
            ExportService.ToCsv(table, _visibleRows, path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Error = e.Message;
        }
    }
}
