using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Data;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PbForMac.Models;
using PbForMac.Services;
using PbForMac.ViewModels.Visuals;

namespace PbForMac.ViewModels;

/// <summary>Кнопка типа визуала в панели «Визуализации».</summary>
public sealed class VisualKindItem(VisualKind kind, string label, string iconPath, Action<VisualKind> add)
{
    public VisualKind Kind { get; } = kind;
    public string Label { get; } = label;
    private Geometry? _icon;

    // Разбирается при первом обращении из разметки, чтобы view-модели создавались и без платформы Avalonia (в тестах).
    public Geometry Icon => _icon ??= StreamGeometry.Parse(iconPath);

    public void Add() => add(Kind);
}

/// <summary>Вкладка «Отчёт»: холст дашборда, визуалы, панель полей и фильтры страницы.</summary>
public sealed partial class ReportViewModel : ViewModelBase
{
    private const double Margin = 20;
    private const double Snap = 10;

    private readonly IDialogService _dialogs;

    public ReportViewModel(DataModel model, IDialogService dialogs, IRelayCommand importCommand, IRelayCommand importFolderCommand,
        IRelayCommand openSampleCommand)
    {
        Model = model;
        Query = new ModelQuery(model);
        _dialogs = dialogs;
        ImportCommand = importCommand;
        ImportFolderCommand = importFolderCommand;
        OpenSampleCommand = openSampleCommand;
        _selectedOperator = Labels.FilterOperators[0];
        Visuals.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(IsEmpty));
            UpdateBoardSize();
        };
        Model.Changed += (_, _) => OnModelChanged();

        VisualKinds =
        [
            new(VisualKind.Column, "Гистограмма", "M4,20 V12 H8 V20 Z M10,20 V5 H14 V20 Z M16,20 V9 H20 V20 Z", AddOrChangeVisual),
            new(VisualKind.Bar, "Линейчатая диаграмма", "M4,4 H15 V8 H4 Z M4,10 H20 V14 H4 Z M4,16 H10 V20 H4 Z", AddOrChangeVisual),
            new(VisualKind.Line, "График", "M3.5 18.49l6-6.01 4 4L22 6.92l-1.41-1.41-7.09 7.97-4-4L2 16.99z", AddOrChangeVisual),
            new(VisualKind.Area, "Диаграмма с областями", "M3,20 L3,14 L8,8 L13,12 L21,4 L21,20 Z", AddOrChangeVisual),
            new(VisualKind.Pie, "Круговая диаграмма",
                "M11 2v20c-5.07-.5-9-4.79-9-10s3.93-9.5 9-10zm2.03 0v8.99H22c-.47-4.74-4.24-8.52-8.97-8.99zm0 11.01V22c4.74-.47 8.5-4.25 8.97-8.99h-8.97z",
                AddOrChangeVisual),
            new(VisualKind.Scatter, "Точечная диаграмма",
                "M5,14 a2,2 0 1,0 4,0 a2,2 0 1,0 -4,0 Z M10,8 a2,2 0 1,0 4,0 a2,2 0 1,0 -4,0 Z M15,15 a2,2 0 1,0 4,0 a2,2 0 1,0 -4,0 Z M16,6 a2,2 0 1,0 4,0 a2,2 0 1,0 -4,0 Z M3,21 H21 V22 H3 Z M3,2 H4 V22 H3 Z",
                AddOrChangeVisual),
            new(VisualKind.Card, "Карточка", "M3,5 H21 V19 H3 Z M5,7 V17 H19 V7 Z M7,11 H17 V13 H7 Z", AddOrChangeVisual),
            new(VisualKind.Table, "Таблица",
                "M3,4 H21 V20 H3 Z M5,9 V12 H11 V9 Z M13,9 V12 H19 V9 Z M5,14 V18 H11 V14 Z M13,14 V18 H19 V14 Z",
                AddOrChangeVisual),
            new(VisualKind.Matrix, "Матрица",
                "M3,4 H21 V20 H3 Z M3,9 H21 M3,14 H21 M9,4 V20 M15,4 V20",
                AddOrChangeVisual),
            new(VisualKind.Slicer, "Срез",
                "M3,4 H8 V9 H3 Z M10,5.5 H21 V7.5 H10 Z M3,10 H8 V15 H3 Z M10,11.5 H21 V13.5 H10 Z M3,16 H8 V21 H3 Z M10,17.5 H21 V19.5 H10 Z",
                AddOrChangeVisual),
        ];
    }

    public DataModel Model { get; }

    /// <summary>Запросы с учётом связей; пересоздаётся при каждом изменении модели.</summary>
    public ModelQuery Query { get; private set; }
    public IRelayCommand ImportCommand { get; }
    public IRelayCommand ImportFolderCommand { get; }
    public IRelayCommand OpenSampleCommand { get; }
    public IReadOnlyList<VisualKindItem> VisualKinds { get; }
    public ObservableCollection<VisualViewModel> Visuals { get; } = [];
    public ObservableCollection<string> TableNames { get; } = [];
    public ObservableCollection<FieldTableNode> FieldTables { get; } = [];
    public ObservableCollection<FilterItemViewModel> Filters { get; } = [];
    public ObservableCollection<string> FilterColumns { get; } = [];
    public IReadOnlyList<Option<FilterOperator>> FilterOperators => Labels.FilterOperators;

    /// <summary>Рендерит холст в PNG; задаётся представлением.</summary>
    public Func<string, Task>? ExportBoard { get; set; }

    public bool IsEmpty => Visuals.Count == 0;
    public bool HasData => Model.Tables.Count > 0;
    public bool HasSelection => SelectedVisual is not null;
    public bool HasFilters => Filters.Count > 0;
    public bool FilterNeedsValue => Labels.OperatorNeedsValue(SelectedOperator.Value);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    private VisualViewModel? _selectedVisual;

    [ObservableProperty]
    private double _boardWidth = 1280;

    [ObservableProperty]
    private double _boardHeight = 760;

    /// <summary>Масштаб холста; при <see cref="FitToWidth"/> подбирается представлением.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ZoomText))]
    private double _zoom = 1;

    [ObservableProperty]
    private bool _fitToWidth = true;

    public string ZoomText => $"{Zoom:P0}";

    [ObservableProperty]
    private string? _filterTable;

    [ObservableProperty]
    private string? _filterColumn;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FilterNeedsValue))]
    private Option<FilterOperator> _selectedOperator;

    [ObservableProperty]
    private string? _filterValue;

    [ObservableProperty]
    private string? _filterError;

    partial void OnSelectedVisualChanged(VisualViewModel? oldValue, VisualViewModel? newValue)
    {
        if (oldValue is not null) oldValue.IsSelected = false;
        if (newValue is not null) newValue.IsSelected = true;
    }

    partial void OnFilterTableChanged(string? value) => UpdateFilterColumns();

    private void OnModelChanged()
    {
        Query = new ModelQuery(Model);
        var names = Model.Tables.Select(t => t.TableName).ToList();
        if (!names.SequenceEqual(TableNames))
        {
            TableNames.Clear();
            foreach (var name in names)
                TableNames.Add(name);
        }
        if (FilterTable is null || !TableNames.Contains(FilterTable))
            FilterTable = TableNames.FirstOrDefault();
        UpdateFilterColumns();
        RebuildFieldTables();
        OnPropertyChanged(nameof(HasData));

        foreach (var visual in Visuals)
            visual.OnModelChanged();
    }

    private void RebuildFieldTables()
    {
        FieldTables.Clear();
        foreach (DataTable table in Model.Tables)
        {
            var columns = table.Columns.Cast<DataColumn>()
                .Select(c => new FieldColumnItem(table.TableName, c.ColumnName, TypeInference.FromClr(c.DataType), AssignFieldFromPane));
            FieldTables.Add(new FieldTableNode(table.TableName, columns));
        }
    }

    /// <summary>Клик по полю в панели «Поля» — назначает его выбранному визуалу.</summary>
    private void AssignFieldFromPane(FieldColumnItem field)
    {
        if (SelectedVisual is null)
        {
            var definition = new VisualDefinition { Kind = VisualKind.Column, Table = field.Table };
            (definition.Width, definition.Height) = DefaultSize(VisualKind.Column);
            (definition.X, definition.Y) = FindFreeSpot(definition.Width, definition.Height);
            SelectedVisual = AddVisual(definition);
        }
        SelectedVisual.AssignField(field.Table, field.Name);
    }

    private void UpdateFilterColumns()
    {
        var selected = FilterColumn;
        FilterColumns.Clear();
        if (Model.GetTable(FilterTable) is { } table)
        {
            foreach (DataColumn column in table.Columns)
                FilterColumns.Add(column.ColumnName);
        }
        FilterColumn = FilterColumns.Contains(selected ?? "") ? selected : FilterColumns.FirstOrDefault();
    }

    /// <summary>
    /// Фильтры страницы и срезов (кроме среза <paramref name="exclude"/>). Какие из них действуют на визуал,
    /// решает <see cref="ModelQuery"/>: фильтры своей таблицы и связанных с ней справочников.
    /// </summary>
    public IEnumerable<FilterDefinition> ActiveFilters(VisualViewModel? exclude)
    {
        foreach (var filter in Filters)
            yield return filter.Definition;
        foreach (var slicer in Visuals.OfType<SlicerVisualViewModel>())
        {
            if (slicer != exclude && slicer.ActiveFilter is { } f)
                yield return f;
        }
    }

    public void RefreshAll(VisualViewModel? except = null)
    {
        foreach (var visual in Visuals)
        {
            if (visual != except)
                visual.Refresh();
        }
    }

    /// <summary>Визуал изменил набор фильтров (срез) — пересчитываем остальные.</summary>
    public void OnVisualFiltersChanged(VisualViewModel source)
    {
        if (source is SlicerVisualViewModel)
            RefreshAll(source);
    }

    public void Select(VisualViewModel? visual) => SelectedVisual = visual;

    [RelayCommand]
    private void ClearSelection() => SelectedVisual = null;

    /// <summary>Удаляет визуал с дашборда после подтверждения пользователя.</summary>
    public async Task DeleteAsync(VisualViewModel visual)
    {
        if (!Visuals.Contains(visual))
            return;
        if (!await _dialogs.ConfirmAsync("Удалить визуал",
                $"Удалить «{visual.DisplayTitle}» ({visual.KindLabel.ToLowerInvariant()}) с дашборда?"))
            return;
        Remove(visual);
    }

    private void Remove(VisualViewModel visual)
    {
        visual.PropertyChanged -= OnVisualPropertyChanged;
        Visuals.Remove(visual);
        if (SelectedVisual == visual)
            SelectedVisual = null;
        if (visual is SlicerVisualViewModel)
            RefreshAll();
    }

    [RelayCommand]
    private Task DeleteSelectedAsync() =>
        SelectedVisual is null ? Task.CompletedTask : DeleteAsync(SelectedVisual);

    public void Duplicate(VisualViewModel visual)
    {
        var source = visual.Definition;
        var copy = new VisualDefinition
        {
            Kind = source.Kind,
            Title = source.Title,
            Table = source.Table,
            CategoryField = source.CategoryField,
            ValueFields = [.. source.ValueFields],
            Aggregation = source.Aggregation,
            DateGranularity = source.DateGranularity,
            TopN = source.TopN,
            ShowLegend = source.ShowLegend,
            ShowDataLabels = source.ShowDataLabels,
            Width = source.Width,
            Height = source.Height,
        };
        (copy.X, copy.Y) = FindFreeSpot(copy.Width, copy.Height);
        SelectedVisual = AddVisual(copy);
    }

    /// <summary>Клик по типу визуала: меняет тип выбранного визуала или добавляет новый.</summary>
    private void AddOrChangeVisual(VisualKind kind)
    {
        if (SelectedVisual is { } selected)
        {
            if (selected.Kind != kind)
                ChangeKind(selected, kind);
            return;
        }

        var definition = new VisualDefinition { Kind = kind };
        (definition.Width, definition.Height) = DefaultSize(kind);
        (definition.X, definition.Y) = FindFreeSpot(definition.Width, definition.Height);
        AutoFill(definition, Model.GetTable(FilterTable) ?? Model.Tables.FirstOrDefault());
        SelectedVisual = AddVisual(definition);
    }

    private void ChangeKind(VisualViewModel visual, VisualKind kind)
    {
        var definition = visual.Definition;
        var wasSlicer = definition.Kind == VisualKind.Slicer;
        definition.Kind = kind;
        definition.SelectedValues.Clear();
        if (kind is VisualKind.Scatter or VisualKind.Card or VisualKind.Slicer)
            AutoFill(definition, Model.GetTable(definition.Table), keepTable: true);

        var index = Visuals.IndexOf(visual);
        visual.PropertyChanged -= OnVisualPropertyChanged;
        var replacement = Create(definition);
        replacement.PropertyChanged += OnVisualPropertyChanged;
        Visuals[index] = replacement;
        SelectedVisual = replacement;
        replacement.OnModelChanged();
        if (wasSlicer)
            RefreshAll(replacement);
    }

    private VisualViewModel AddVisual(VisualDefinition definition)
    {
        var visual = Create(definition);
        visual.PropertyChanged += OnVisualPropertyChanged;
        Visuals.Add(visual);
        visual.OnModelChanged();
        return visual;
    }

    private VisualViewModel Create(VisualDefinition definition) => definition.Kind switch
    {
        VisualKind.Card => new CardVisualViewModel(definition, this),
        VisualKind.Table => new TableVisualViewModel(definition, this),
        VisualKind.Matrix => new MatrixVisualViewModel(definition, this),
        VisualKind.Slicer => new SlicerVisualViewModel(definition, this),
        _ => new ChartVisualViewModel(definition, this),
    };

    private void OnVisualPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(VisualViewModel.X) or nameof(VisualViewModel.Y)
            or nameof(VisualViewModel.Width) or nameof(VisualViewModel.Height))
            UpdateBoardSize();
    }

    /// <summary>Холст растёт вслед за визуалами, но не меньше базового размера страницы.</summary>
    private void UpdateBoardSize()
    {
        BoardWidth = Math.Max(1280, Visuals.Select(v => v.X + v.Width + Margin).DefaultIfEmpty(0).Max());
        BoardHeight = Math.Max(760, Visuals.Select(v => v.Y + v.Height + Margin).DefaultIfEmpty(0).Max());
    }

    private static (double Width, double Height) DefaultSize(VisualKind kind) => kind switch
    {
        VisualKind.Card => (240, 140),
        VisualKind.Slicer => (240, 300),
        VisualKind.Table or VisualKind.Matrix => (500, 300),
        _ => (460, 300),
    };

    /// <summary>Ищет свободное место слева направо, сверху вниз.</summary>
    private (double X, double Y) FindFreeSpot(double width, double height)
    {
        for (var y = Margin; y < 10000; y += Snap)
        {
            for (var x = Margin; x + width <= Math.Max(BoardWidth, 1280) - Margin; x += Snap)
            {
                var overlaps = Visuals.Any(v =>
                    x < v.X + v.Width + Snap && x + width + Snap > v.X &&
                    y < v.Y + v.Height + Snap && y + height + Snap > v.Y);
                if (!overlaps)
                    return (x, y);
            }
        }
        return (Margin, BoardHeight);
    }

    /// <summary>Подбирает поля для нового визуала, чтобы он сразу что-то показывал.</summary>
    private static void AutoFill(VisualDefinition definition, DataTable? table, bool keepTable = false)
    {
        if (table is null)
            return;
        if (!keepTable || definition.Table is null)
            definition.Table = table.TableName;

        var columns = table.Columns.Cast<DataColumn>().ToList();
        var numeric = columns.Where(TypeInference.IsNumeric).Where(c => !LooksLikeId(c.ColumnName)).ToList();
        var dates = columns.Where(c => c.DataType == typeof(DateTime)).ToList();
        var texts = columns.Where(c => c.DataType == typeof(string))
            .OrderBy(c => table.Rows.Cast<DataRow>().Take(2000).Select(r => r[c]).Distinct().Count())
            .ToList();

        definition.ValueFields.Clear();
        switch (definition.Kind)
        {
            case VisualKind.Card:
                definition.CategoryField = null;
                if (numeric.Count > 0) definition.ValueFields.Add(numeric[^1].ColumnName);
                break;
            case VisualKind.Slicer:
                definition.CategoryField = texts.FirstOrDefault()?.ColumnName;
                break;
            case VisualKind.Scatter:
                definition.CategoryField = numeric.FirstOrDefault()?.ColumnName;
                if (numeric.Count > 1) definition.ValueFields.Add(numeric[^1].ColumnName);
                break;
            case VisualKind.Line or VisualKind.Area:
                definition.CategoryField = (dates.FirstOrDefault() ?? texts.FirstOrDefault())?.ColumnName;
                if (numeric.Count > 0) definition.ValueFields.Add(numeric[^1].ColumnName);
                break;
            case VisualKind.Table:
                definition.CategoryField = null;
                definition.ColumnField = null;
                break;
            case VisualKind.Matrix:
                definition.CategoryField = texts.ElementAtOrDefault(0)?.ColumnName;
                definition.ColumnField = texts.ElementAtOrDefault(1)?.ColumnName
                    ?? dates.FirstOrDefault()?.ColumnName
                    ?? texts.ElementAtOrDefault(0)?.ColumnName;
                if (numeric.Count > 0) definition.ValueFields.Add(numeric[^1].ColumnName);
                break;
            default:
                definition.CategoryField = (texts.FirstOrDefault() ?? dates.FirstOrDefault())?.ColumnName;
                if (numeric.Count > 0) definition.ValueFields.Add(numeric[^1].ColumnName);
                break;
        }
    }

    private static bool LooksLikeId(string name) =>
        name.Equals("id", StringComparison.OrdinalIgnoreCase) || name.EndsWith("Id", StringComparison.Ordinal)
        || name.EndsWith("_id", StringComparison.OrdinalIgnoreCase) || name.StartsWith("Код", StringComparison.OrdinalIgnoreCase);

    [RelayCommand]
    private void AddFilter()
    {
        FilterError = null;
        var table = Model.GetTable(FilterTable);
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
            FilterError = e.Message;
            return;
        }
        Filters.Add(new FilterItemViewModel(definition, RemoveFilter));
        FilterValue = null;
        OnPropertyChanged(nameof(HasFilters));
        RefreshAll();
    }

    private void RemoveFilter(FilterItemViewModel item)
    {
        Filters.Remove(item);
        OnPropertyChanged(nameof(HasFilters));
        RefreshAll();
    }

    public async Task ExportPngAsync(string path)
    {
        SelectedVisual = null;
        if (ExportBoard is not null)
            await ExportBoard(path);
    }

    public List<VisualDefinition> GetVisualDefinitions() => Visuals.Select(v => v.Definition).ToList();

    public List<FilterDefinition> GetFilterDefinitions() => Filters.Select(f => f.Definition).ToList();

    public void Clear()
    {
        SelectedVisual = null;
        foreach (var visual in Visuals)
            visual.PropertyChanged -= OnVisualPropertyChanged;
        Visuals.Clear();
        Filters.Clear();
        OnPropertyChanged(nameof(HasFilters));
    }

    public void Load(IEnumerable<VisualDefinition> visuals, IEnumerable<FilterDefinition> filters)
    {
        Clear();
        foreach (var filter in filters)
            Filters.Add(new FilterItemViewModel(filter, RemoveFilter));
        OnPropertyChanged(nameof(HasFilters));
        foreach (var definition in visuals)
        {
            var visual = Create(definition);
            visual.PropertyChanged += OnVisualPropertyChanged;
            Visuals.Add(visual);
        }
        // Обновляем после добавления всех визуалов, чтобы срезы уже действовали.
        foreach (var visual in Visuals)
            visual.OnModelChanged();
    }
}
