using System.Collections.ObjectModel;
using System.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PbForMac.Controls;
using PbForMac.Models;
using PbForMac.Services;

namespace PbForMac.ViewModels;

/// <summary>Вкладка «Модель»: столбцы таблиц и применённые шаги преобразований.</summary>
public sealed partial class TransformViewModel : ViewModelBase, IColumnHeaderActions
{
    private const int PreviewRows = 500;
    private readonly DataModel _model;
    private readonly IDialogService _dialogs;
    private readonly ColumnEditor _columns;

    public TransformViewModel(DataModel model, IDialogService dialogs)
    {
        _model = model;
        _dialogs = dialogs;
        _columns = new ColumnEditor(model, dialogs);
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

    public bool CanEdit => true;
    public string? TableName => SelectedTable;
    public ColumnEditor Editor => _columns;
    public IColumnHeaderActions ColumnActions => this;

    public void SelectColumn(string column) =>
        SelectedColumn = Columns.FirstOrDefault(c => c.Name == column) ?? SelectedColumn;

    public bool HasTables => Tables.Count > 0;
    public bool HasSteps => Steps.Count > 0;
    public bool HasColumn => SelectedColumn is not null;
    public bool HasCheckedColumns => Columns.Any(c => c.IsChecked);
    public int CheckedColumnCount => Columns.Count(c => c.IsChecked);

    public string StepCountText => HasSteps ? $"({Steps.Count})" : "";

    public IEnumerable<StepItemViewModel> VisibleSteps =>
        FilterStepsByTable && SelectedTable is { } table
            ? Steps.Where(s => string.Equals(s.Table, table, StringComparison.OrdinalIgnoreCase))
            : Steps;

    public bool ShowFilteredStepsEmpty =>
        HasSteps && FilterStepsByTable && !VisibleSteps.Any();

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

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VisibleSteps), nameof(ShowFilteredStepsEmpty))]
    private bool _filterStepsByTable;

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

    // Append
    [ObservableProperty]
    private string? _appendOtherTable;

    [ObservableProperty]
    private string? _appendNewTable;

    // Связи
    public ObservableCollection<RelationshipItemViewModel> Relationships { get; } = [];
    public ObservableCollection<string> RelationshipFromColumns { get; } = [];
    public ObservableCollection<string> RelationshipToColumns { get; } = [];
    public ObservableCollection<DiagramTableViewModel> DiagramTables { get; } = [];
    public ObservableCollection<DiagramLinkViewModel> DiagramLinks { get; } = [];
    public bool HasRelationships => Relationships.Count > 0;

    [ObservableProperty]
    private string? _relationshipFromTable;

    [ObservableProperty]
    private string? _relationshipFromColumn;

    [ObservableProperty]
    private string? _relationshipToTable;

    [ObservableProperty]
    private string? _relationshipToColumn;

    [ObservableProperty]
    private DiagramLinkViewModel? _selectedDiagramLink;

    [ObservableProperty]
    private string? _diagramHint;

    /// <summary>Первый клик по столбцу на диаграмме при создании связи.</summary>
    private (DiagramTableViewModel Table, DiagramColumnViewModel Column)? _linkStart;

    public Action<DiagramTableViewModel, double, double> OnDiagramPositionChanged => SaveDiagramPosition;
    public Action<DiagramTableViewModel, DiagramColumnViewModel> OnDiagramColumnClicked => DiagramColumnClicked;

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
        var total = _model.Steps.Count;
        foreach (var step in _model.Steps)
            Steps.Add(new StepItemViewModel(index++, total, step, _model.StepErrors.GetValueOrDefault(step),
                item => _ = RemoveStepAsync(item), OnStepToggled, OnStepMoved));
        OnPropertyChanged(nameof(HasSteps));
        OnPropertyChanged(nameof(StepCountText));
        OnPropertyChanged(nameof(VisibleSteps));
        OnPropertyChanged(nameof(ShowFilteredStepsEmpty));

        Relationships.Clear();
        foreach (var relationship in _model.Relationships)
        {
            Relationships.Add(new RelationshipItemViewModel(relationship, _model.MatchRate(relationship),
                _model.RelationshipIssues.GetValueOrDefault(relationship), item => _ = RemoveRelationshipAsync(item)));
        }
        OnPropertyChanged(nameof(HasRelationships));
        RebuildDiagram();

        if (RelationshipFromTable is null || !Tables.Contains(RelationshipFromTable))
            RelationshipFromTable = Tables.FirstOrDefault();
        if (RelationshipToTable is null || !Tables.Contains(RelationshipToTable))
            RelationshipToTable = Tables.FirstOrDefault(t => t != RelationshipFromTable) ?? Tables.FirstOrDefault();
        if (AppendOtherTable is null || !Tables.Contains(AppendOtherTable) || AppendOtherTable == SelectedTable)
            AppendOtherTable = Tables.FirstOrDefault(t => t != SelectedTable) ?? Tables.FirstOrDefault();
        FillColumns(RelationshipFromTable, RelationshipFromColumns);
        FillColumns(RelationshipToTable, RelationshipToColumns);
    }

    private void RebuildDiagram()
    {
        var previous = DiagramTables.ToDictionary(t => t.Name, t => (t.X, t.Y), StringComparer.OrdinalIgnoreCase);
        var selected = SelectedDiagramLink?.Relationship;
        DiagramTables.Clear();
        DiagramLinks.Clear();
        SelectedDiagramLink = null;

        var keyColumns = new HashSet<(string Table, string Column)>(
            _model.Relationships.SelectMany(r => new[]
            {
                (r.FromTable, r.FromColumn),
                (r.ToTable, r.ToColumn),
            }),
            // custom comparer via tuple - use StringComparer via custom
            EqualityComparer<(string Table, string Column)>.Create(
                (a, b) => string.Equals(a.Table, b.Table, StringComparison.OrdinalIgnoreCase)
                          && string.Equals(a.Column, b.Column, StringComparison.OrdinalIgnoreCase),
                t => HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(t.Table),
                    StringComparer.OrdinalIgnoreCase.GetHashCode(t.Column))));

        var needLayout = false;
        foreach (var table in _model.Tables)
        {
            var layout = _model.GetTableLayout(table.TableName);
            double x, y;
            if (layout is not null)
                (x, y) = (layout.X, layout.Y);
            else if (previous.TryGetValue(table.TableName, out var pos))
                (x, y) = pos;
            else
            {
                needLayout = true;
                (x, y) = (0, 0);
            }

            var columns = table.Columns.Cast<DataColumn>().Select(c =>
                new DiagramColumnViewModel(c.ColumnName, TypeInference.FromClr(c.DataType),
                    keyColumns.Contains((table.TableName, c.ColumnName))));
            DiagramTables.Add(new DiagramTableViewModel(table.TableName, columns, x, y));
        }

        if (needLayout && DiagramTables.Count > 0 && _model.TableLayouts.Count == 0)
        {
            var positions = DiagramLayout.Arrange(_model.Tables, _model.Relationships);
            foreach (var card in DiagramTables)
            {
                if (positions.TryGetValue(card.Name, out var pos))
                {
                    card.X = pos.X;
                    card.Y = pos.Y;
                    _model.SetTableLayout(card.Name, pos.X, pos.Y);
                }
            }
        }

        var byName = DiagramTables.ToDictionary(t => t.Name, StringComparer.OrdinalIgnoreCase);
        foreach (var relationship in _model.Relationships)
        {
            if (!byName.TryGetValue(relationship.FromTable, out var from) || !byName.TryGetValue(relationship.ToTable, out var to))
                continue;
            var link = new DiagramLinkViewModel(relationship, from, to, _model.MatchRate(relationship),
                _model.RelationshipIssues.GetValueOrDefault(relationship));
            DiagramLinks.Add(link);
            if (selected is not null && relationship.SameAs(selected))
                SelectedDiagramLink = link;
        }

        DiagramHint = DiagramTables.Count == 0
            ? null
            : "Перетаскивайте карточки · масштаб: сведение пальцев на тачпаде или Ctrl/⌘+скролл · Alt/средняя кнопка — панорама.";
    }

    private void SaveDiagramPosition(DiagramTableViewModel table, double x, double y) =>
        _model.SetTableLayout(table.Name, x, y);

    private void DiagramColumnClicked(DiagramTableViewModel table, DiagramColumnViewModel column)
    {
        Error = null;
        if (_linkStart is null)
        {
            ClearDiagramColumnSelection();
            column.IsSelected = true;
            table.IsHighlighted = true;
            _linkStart = (table, column);
            DiagramHint = $"Связь от «{table.Name}[{column.Name}]» — выберите столбец справочника на другой таблице.";
            RelationshipFromTable = table.Name;
            RelationshipFromColumn = column.Name;
            return;
        }

        var (fromTable, fromColumn) = _linkStart.Value;
        ClearDiagramColumnSelection();
        _linkStart = null;
        if (ReferenceEquals(fromTable, table))
        {
            DiagramHint = "Выберите столбец другой таблицы.";
            return;
        }

        RelationshipFromTable = fromTable.Name;
        RelationshipFromColumn = fromColumn.Name;
        RelationshipToTable = table.Name;
        RelationshipToColumn = column.Name;
        AddRelationship();
        DiagramHint = "Перетаскивайте карточки · Ctrl+колёсико — масштаб · клик по столбцам — новая связь.";
    }

    private void ClearDiagramColumnSelection()
    {
        foreach (var card in DiagramTables)
        {
            card.IsHighlighted = false;
            foreach (var col in card.Columns)
                col.IsSelected = false;
        }
    }

    [RelayCommand]
    private void AutoLayoutDiagram()
    {
        _model.AutoLayoutTables();
        Info = "Таблицы на диаграмме разложены автоматически.";
    }

    [RelayCommand]
    private async Task RemoveSelectedDiagramLinkAsync()
    {
        if (SelectedDiagramLink is null)
            return;
        var item = Relationships.FirstOrDefault(r => r.Relationship.SameAs(SelectedDiagramLink.Relationship));
        if (item is not null)
            await RemoveRelationshipAsync(item);
    }

    partial void OnRelationshipFromTableChanged(string? value)
    {
        FillColumns(value, RelationshipFromColumns);
        RelationshipFromColumn = RelationshipFromColumns.FirstOrDefault();
    }

    partial void OnRelationshipToTableChanged(string? value)
    {
        FillColumns(value, RelationshipToColumns);
        MatchToColumn();
    }

    partial void OnRelationshipFromColumnChanged(string? value) => MatchToColumn();

    /// <summary>В справочнике по умолчанию выбирается одноимённый столбец-ключ.</summary>
    private void MatchToColumn() =>
        RelationshipToColumn = RelationshipToColumns.FirstOrDefault(c => string.Equals(c, RelationshipFromColumn, StringComparison.OrdinalIgnoreCase))
                               ?? (RelationshipToColumns.Contains(RelationshipToColumn ?? "") ? RelationshipToColumn : RelationshipToColumns.FirstOrDefault());

    private void FillColumns(string? tableName, ObservableCollection<string> target)
    {
        var columns = _model.GetTable(tableName)?.Columns.Cast<DataColumn>().Select(c => c.ColumnName).ToList() ?? [];
        if (columns.SequenceEqual(target))
            return;
        target.Clear();
        foreach (var column in columns)
            target.Add(column);
    }

    [RelayCommand]
    private void AddRelationship()
    {
        Error = null;
        Info = null;
        if (RelationshipFromTable is null || RelationshipFromColumn is null || RelationshipToTable is null || RelationshipToColumn is null)
        {
            Error = "Выберите таблицы и столбцы связи.";
            return;
        }
        var relationship = new RelationshipDefinition
        {
            FromTable = RelationshipFromTable,
            FromColumn = RelationshipFromColumn,
            ToTable = RelationshipToTable,
            ToColumn = RelationshipToColumn,
        };
        try
        {
            _model.AddRelationship(relationship);
            Info = $"Добавлена связь {relationship}.";
        }
        catch (InvalidOperationException e)
        {
            Error = e.Message;
        }
    }

    [RelayCommand]
    private void DetectRelationships()
    {
        Error = null;
        var found = _model.DetectRelationships();
        Info = found.Count == 0
            ? "Новых связей не найдено: ищутся одноимённые столбцы с уникальными ключами в справочнике."
            : $"Найдено связей: {found.Count} — {string.Join(", ", found)}.";
    }

    /// <summary>Удаляет связь после подтверждения: визуалы потеряют поля справочника, полученные через неё.</summary>
    public async Task RemoveRelationshipAsync(RelationshipItemViewModel item)
    {
        if (!await _dialogs.ConfirmAsync("Удалить связь",
                $"Удалить связь {item.Relationship}? Визуалы таблицы «{item.Relationship.FromTable}» потеряют поля " +
                $"«{item.Relationship.ToTable}», а срезы по «{item.Relationship.ToTable}» перестанут её фильтровать."))
            return;
        Error = null;
        Info = $"Удалена связь {item.Relationship}.";
        _model.RemoveRelationship(item.Relationship);
    }

    partial void OnSelectedTableChanged(string? value)
    {
        LoadTable();
        OnPropertyChanged(nameof(VisibleSteps));
        OnPropertyChanged(nameof(ShowFilteredStepsEmpty));
    }

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
                var item = new ColumnItem(column.ColumnName, TypeInference.FromClr(column.DataType), InsertColumn);
                item.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(ColumnItem.IsChecked))
                    {
                        OnPropertyChanged(nameof(HasCheckedColumns));
                        OnPropertyChanged(nameof(CheckedColumnCount));
                    }
                };
                Columns.Add(item);
                GroupColumns.Add(new CheckItem(column.ColumnName));
            }
        }
        SelectedColumn = Columns.FirstOrDefault(c => c.Name == selectedColumn);
        OnPropertyChanged(nameof(HasCheckedColumns));
        OnPropertyChanged(nameof(CheckedColumnCount));
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
    private async Task RemoveColumnAsync()
    {
        if (SelectedTable is null || SelectedColumn is null)
            return;
        await _columns.RemoveAsync(SelectedTable, [SelectedColumn.Name]);
    }

    [RelayCommand]
    private async Task RemoveCheckedColumnsAsync()
    {
        if (SelectedTable is null)
            return;
        var names = Columns.Where(c => c.IsChecked).Select(c => c.Name).ToList();
        if (names.Count > 0)
            await _columns.RemoveAsync(SelectedTable, names);
    }

    [RelayCommand]
    private async Task KeepCheckedColumnsAsync()
    {
        if (SelectedTable is null)
            return;
        var names = Columns.Where(c => c.IsChecked).Select(c => c.Name).ToList();
        if (names.Count == 0)
        {
            Error = "Отметьте столбцы, которые нужно оставить.";
            return;
        }
        await _columns.KeepOnlyAsync(SelectedTable, names);
    }

    [RelayCommand]
    private async Task UnpivotCheckedColumnsAsync()
    {
        if (SelectedTable is null)
            return;
        var names = Columns.Where(c => c.IsChecked).Select(c => c.Name).ToList();
        if (names.Count == 0)
        {
            Error = "Отметьте столбцы для Unpivot.";
            return;
        }
        Error = null;
        await _columns.UnpivotAsync(SelectedTable, names);
    }

    [RelayCommand]
    private void SelectAllColumns()
    {
        var all = Columns.All(c => c.IsChecked);
        foreach (var column in Columns)
            column.IsChecked = !all;
    }

    [RelayCommand]
    private async Task MoveColumnLeftAsync()
    {
        if (SelectedTable is null || SelectedColumn is null)
            return;
        var ordinal = _columns.Ordinal(SelectedTable, SelectedColumn.Name);
        if (ordinal is > 0)
            await _columns.MoveAsync(SelectedTable, SelectedColumn.Name, ordinal.Value - 1);
    }

    [RelayCommand]
    private async Task MoveColumnRightAsync()
    {
        if (SelectedTable is null || SelectedColumn is null)
            return;
        var ordinal = _columns.Ordinal(SelectedTable, SelectedColumn.Name);
        var count = _columns.ColumnCount(SelectedTable);
        if (ordinal is { } index && index < count - 1)
            await _columns.MoveAsync(SelectedTable, SelectedColumn.Name, index + 1);
    }

    [RelayCommand]
    private async Task ChangeTypeAsync()
    {
        if (SelectedTable is null || SelectedColumn is null || SelectedColumn.Type == TargetType.Value)
            return;
        await _columns.ChangeTypeAsync(SelectedTable, SelectedColumn.Name, TargetType.Value);
    }

    [RelayCommand]
    private void RemoveDuplicates()
    {
        if (SelectedTable is not null)
            TryAddStep(new RemoveDuplicatesStep { Table = SelectedTable });
    }

    [RelayCommand]
    private async Task ReplaceValuesAsync()
    {
        if (SelectedTable is null || SelectedColumn is null)
            return;
        await _columns.ReplaceValuesAsync(SelectedTable, SelectedColumn.Name);
    }

    [RelayCommand]
    private async Task FillDownAsync()
    {
        if (SelectedTable is null || SelectedColumn is null)
            return;
        await _columns.FillDownAsync(SelectedTable, SelectedColumn.Name);
    }

    [RelayCommand]
    private async Task FillUpAsync()
    {
        if (SelectedTable is null || SelectedColumn is null)
            return;
        await _columns.FillUpAsync(SelectedTable, SelectedColumn.Name);
    }

    [RelayCommand]
    private async Task SplitColumnAsync()
    {
        if (SelectedTable is null || SelectedColumn is null)
            return;
        await _columns.SplitColumnAsync(SelectedTable, SelectedColumn.Name);
    }

    [RelayCommand]
    private async Task RemoveBlankRowsAsync()
    {
        if (SelectedTable is null)
            return;
        await _columns.RemoveBlankRowsAsync(SelectedTable);
    }

    [RelayCommand]
    private async Task AppendTableAsync()
    {
        if (SelectedTable is null || AppendOtherTable is null)
        {
            Error = "Выберите текущую таблицу и таблицу для добавления строк.";
            return;
        }
        if (string.Equals(SelectedTable, AppendOtherTable, StringComparison.OrdinalIgnoreCase))
        {
            Error = "Выберите другую таблицу для объединения.";
            return;
        }
        Error = null;
        if (await _columns.AppendTableAsync(SelectedTable, AppendOtherTable, AppendNewTable))
        {
            Info = string.IsNullOrWhiteSpace(AppendNewTable)
                ? $"Добавлены строки из «{AppendOtherTable}»"
                : $"Создана таблица «{AppendNewTable.Trim()}»";
            if (!string.IsNullOrWhiteSpace(AppendNewTable))
                SelectedTable = AppendNewTable.Trim();
            AppendNewTable = null;
        }
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

    private void OnStepToggled(StepItemViewModel item)
    {
        Error = null;
        Info = item.IsEnabled ? $"Включён шаг: {item.Description}" : $"Выключен шаг: {item.Description}";
        _model.Rebuild();
    }

    private void OnStepMoved(StepItemViewModel item, int delta)
    {
        Error = null;
        if (!_model.MoveStep(item.Step, delta))
            return;
        Info = delta < 0 ? $"Шаг выше: {item.Description}" : $"Шаг ниже: {item.Description}";
    }
}
