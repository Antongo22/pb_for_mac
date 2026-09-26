using System.Collections.ObjectModel;
using System.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PbForMac.Controls;
using PbForMac.Models;
using PbForMac.Services;

namespace PbForMac.ViewModels;

/// <summary>Вкладка «Модель»: столбцы таблиц и применённые шаги преобразований.</summary>
public sealed partial class TransformViewModel : ViewModelBase
{
    private const int PreviewRows = 500;
    private readonly DataModel _model;

    private readonly IDialogService _dialogs;

    public TransformViewModel(DataModel model, IDialogService dialogs)
    {
        _model = model;
        _dialogs = dialogs;
        _targetType = Labels.ColumnTypes[0];
        _selectedOperator = Labels.FilterOperators[0];
        _aggregationKind = Labels.Aggregations[0];
        _model.Changed += (_, _) => OnModelChanged();
    }

    public ObservableCollection<string> Tables { get; } = [];
    public ObservableCollection<ColumnItem> Columns { get; } = [];
    public ObservableCollection<StepItemViewModel> Steps { get; } = [];
    public ObservableCollection<CheckItem> GroupColumns { get; } = [];
    public ObservableCollection<AggregationSpecItem> AggregationSpecs { get; } = [];

    public IReadOnlyList<Option<ColumnType>> ColumnTypes => Labels.ColumnTypes;
    public IReadOnlyList<Option<FilterOperator>> FilterOperators => Labels.FilterOperators;
    public IReadOnlyList<Option<Aggregation>> Aggregations => Labels.Aggregations;

    public bool HasTables => Tables.Count > 0;
    public bool HasSteps => Steps.Count > 0;
    public bool HasColumn => SelectedColumn is not null;

    public string ExpressionHelp =>
        "Синтаксис выражений DataColumn.Expression:\n" +
        "  [Цена] * [Количество]\n" +
        "  IIF([Сумма] > 1000, 'Крупный', 'Мелкий')\n" +
        "  Len([Имя]),  Substring([Код], 1, 3)\n" +
        "  Convert([Год], 'System.String') + '-' + [Месяц]\n" +
        "  IsNull([Скидка], 0)\n" +
        "Операторы: + - * / %  = <> < > <= >=  AND OR NOT  LIKE  IN";

    [ObservableProperty]
    private string? _selectedTable;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasColumn))]
    private ColumnItem? _selectedColumn;

    [ObservableProperty]
    private TableSlice? _preview;

    [ObservableProperty]
    private string? _error;

    [ObservableProperty]
    private string? _info;

    // Переименование и тип
    [ObservableProperty]
    private string? _newColumnName;

    [ObservableProperty]
    private Option<ColumnType> _targetType;

    // Вычисляемый столбец
    [ObservableProperty]
    private string? _calcName;

    [ObservableProperty]
    private string? _calcExpression;

    // Фильтр строк
    [ObservableProperty]
    private string? _filterColumn;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FilterNeedsValue))]
    private Option<FilterOperator> _selectedOperator;

    [ObservableProperty]
    private string? _filterValue;

    public bool FilterNeedsValue => Labels.OperatorNeedsValue(SelectedOperator.Value);

    // Группировка
    [ObservableProperty]
    private string? _aggregationColumn;

    [ObservableProperty]
    private Option<Aggregation> _aggregationKind;

    [ObservableProperty]
    private string? _aggregationName;

    [ObservableProperty]
    private string? _groupTableName;

    private DataTable? CurrentTable => _model.GetTable(SelectedTable);

    private void OnModelChanged()
    {
        var selected = SelectedTable;
        Tables.Clear();
        foreach (var table in _model.Tables)
            Tables.Add(table.TableName);
        OnPropertyChanged(nameof(HasTables));
        SelectedTable = Tables.FirstOrDefault(t => t == selected) ?? Tables.FirstOrDefault();
        LoadTable();

        Steps.Clear();
        var index = 1;
        foreach (var step in _model.Steps)
            Steps.Add(new StepItemViewModel(index++, step, _model.StepErrors.GetValueOrDefault(step), item => _ = RemoveStepAsync(item)));
        OnPropertyChanged(nameof(HasSteps));
    }

    partial void OnSelectedTableChanged(string? value) => LoadTable();

    partial void OnSelectedColumnChanged(ColumnItem? value)
    {
        NewColumnName = value?.Name;
        if (value is not null)
            TargetType = Labels.Find(value.Type);
    }

    private void LoadTable()
    {
        var table = CurrentTable;
        var selectedColumn = SelectedColumn?.Name;

        Columns.Clear();
        GroupColumns.Clear();
        if (table is not null)
        {
            foreach (DataColumn column in table.Columns)
            {
                Columns.Add(new ColumnItem(column.ColumnName, TypeInference.FromClr(column.DataType), InsertColumn));
                GroupColumns.Add(new CheckItem(column.ColumnName));
            }
        }
        SelectedColumn = Columns.FirstOrDefault(c => c.Name == selectedColumn);
        if (FilterColumn is null || Columns.All(c => c.Name != FilterColumn))
            FilterColumn = Columns.FirstOrDefault()?.Name;
        if (AggregationColumn is null || Columns.All(c => c.Name != AggregationColumn))
            AggregationColumn = Columns.FirstOrDefault(c => TypeInference.IsNumeric(c.Type))?.Name ?? Columns.FirstOrDefault()?.Name;
        foreach (var spec in AggregationSpecs.Where(s => Columns.All(c => c.Name != s.Spec.Column)).ToList())
            AggregationSpecs.Remove(spec);

        Preview = table is null ? null : new TableSlice(table, table.Rows.Cast<DataRow>().Take(PreviewRows).ToList());
    }

    private void InsertColumn(ColumnItem column)
    {
        var reference = $"[{column.Name}]";
        CalcExpression = string.IsNullOrWhiteSpace(CalcExpression) ? reference : $"{CalcExpression.TrimEnd()} {reference}";
    }

    /// <summary>Проверяет и добавляет шаг; ошибки показываются пользователю.</summary>
    private bool TryAddStep(TransformStep step)
    {
        Error = null;
        Info = null;
        try
        {
            _model.AddStep(step);
            Info = $"Добавлен шаг: {step.Description}";
            return true;
        }
        catch (InvalidOperationException e)
        {
            Error = e.Message;
            return false;
        }
    }

    [RelayCommand]
    private void RenameColumn()
    {
        if (SelectedTable is null || SelectedColumn is null || string.IsNullOrWhiteSpace(NewColumnName)
            || NewColumnName.Trim() == SelectedColumn.Name)
            return;
        var newName = NewColumnName.Trim();
        if (TryAddStep(new RenameColumnStep { Table = SelectedTable, Column = SelectedColumn.Name, NewName = newName }))
            SelectedColumn = Columns.FirstOrDefault(c => c.Name == newName);
    }

    [RelayCommand]
    private void RemoveColumn()
    {
        if (SelectedTable is null || SelectedColumn is null)
            return;
        TryAddStep(new RemoveColumnStep { Table = SelectedTable, Column = SelectedColumn.Name });
    }

    [RelayCommand]
    private void ChangeType()
    {
        if (SelectedTable is null || SelectedColumn is null || SelectedColumn.Type == TargetType.Value)
            return;
        var name = SelectedColumn.Name;
        if (TryAddStep(new ChangeTypeStep { Table = SelectedTable, Column = name, TargetType = TargetType.Value }))
            SelectedColumn = Columns.FirstOrDefault(c => c.Name == name);
    }

    [RelayCommand]
    private void RemoveDuplicates()
    {
        if (SelectedTable is not null)
            TryAddStep(new RemoveDuplicatesStep { Table = SelectedTable });
    }

    [RelayCommand]
    private void AddCalculatedColumn()
    {
        if (SelectedTable is null)
            return;
        if (TryAddStep(new CalculatedColumnStep
            {
                Table = SelectedTable,
                Name = CalcName?.Trim() ?? "",
                Expression = CalcExpression?.Trim() ?? "",
            }))
        {
            CalcName = null;
            CalcExpression = null;
        }
    }

    [RelayCommand]
    private void AddFilterStep()
    {
        if (SelectedTable is null || FilterColumn is null)
            return;
        if (TryAddStep(new FilterRowsStep
            {
                Table = SelectedTable,
                Filter = new FilterDefinition
                {
                    Table = SelectedTable,
                    Column = FilterColumn,
                    Operator = SelectedOperator.Value,
                    Value = FilterNeedsValue ? FilterValue : null,
                },
            }))
            FilterValue = null;
    }

    [RelayCommand]
    private void AddAggregation()
    {
        if (AggregationColumn is null)
            return;
        var name = string.IsNullOrWhiteSpace(AggregationName)
            ? $"{AggregationKind.Label} {AggregationColumn}"
            : AggregationName.Trim();
        AggregationSpecs.Add(new AggregationSpecItem(
            new AggregationSpec { Column = AggregationColumn, Aggregation = AggregationKind.Value, OutputName = name },
            item => AggregationSpecs.Remove(item)));
        AggregationName = null;
    }

    [RelayCommand]
    private void CreateGroupBy()
    {
        if (SelectedTable is null)
            return;
        var keys = GroupColumns.Where(c => c.IsChecked).Select(c => c.Name).ToList();
        var newTable = string.IsNullOrWhiteSpace(GroupTableName)
            ? _model.UniqueTableName($"{SelectedTable} по {string.Join(", ", keys.DefaultIfEmpty("всем"))}")
            : GroupTableName.Trim();
        var step = new GroupByStep
        {
            Table = SelectedTable,
            NewTable = newTable,
            GroupColumns = keys,
            Aggregations = AggregationSpecs.Select(s => s.Spec).ToList(),
        };
        if (TryAddStep(step))
        {
            AggregationSpecs.Clear();
            GroupTableName = null;
            SelectedTable = newTable;
        }
    }

    /// <summary>
    /// Удаляет шаг. Шаг группировки создаёт таблицу — её удаление требует подтверждения.
    /// </summary>
    public async Task RemoveStepAsync(StepItemViewModel item)
    {
        if (item.Step is GroupByStep groupBy && !await _dialogs.ConfirmAsync("Удалить шаг",
                $"Шаг «{item.Description}» создаёт таблицу «{groupBy.NewTable}». " +
                "Удалить шаг вместе с этой таблицей? Визуалы и шаги, которые её используют, перестанут работать."))
            return;
        Error = null;
        Info = $"Удалён шаг: {item.Description}";
        _model.RemoveStep(item.Step);
    }
}
