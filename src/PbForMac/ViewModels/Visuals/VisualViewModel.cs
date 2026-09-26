using System.Collections.ObjectModel;
using System.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using PbForMac.Models;
using PbForMac.Services;

namespace PbForMac.ViewModels.Visuals;

/// <summary>
/// Базовый класс визуала дашборда: положение на холсте, выбранные поля и пересчёт данных.
/// Все изменения сразу записываются в <see cref="Definition"/>, который сохраняется в отчёт.
/// </summary>
public abstract partial class VisualViewModel : ViewModelBase
{
    private bool _loading;

    protected VisualViewModel(VisualDefinition definition, ReportViewModel owner)
    {
        Definition = definition;
        Owner = owner;
        _loading = true;
        _title = definition.Title;
        _x = definition.X;
        _y = definition.Y;
        _width = definition.Width;
        _height = definition.Height;
        _table = definition.Table;
        _categoryField = definition.CategoryField;
        _aggregation = Labels.Find(definition.Aggregation);
        _granularity = Labels.Find(definition.DateGranularity);
        _topN = definition.TopN;
        UpdateFieldOptions();
        _loading = false;
    }

    public VisualDefinition Definition { get; }
    public ReportViewModel Owner { get; }
    public VisualKind Kind => Definition.Kind;
    public string KindLabel => Labels.Of(Kind);

    public IReadOnlyList<Option<Aggregation>> Aggregations => Labels.Aggregations;
    public IReadOnlyList<Option<DateGranularity>> Granularities => Labels.DateGranularities;
    public ObservableCollection<string> TableNames => Owner.TableNames;
    public ObservableCollection<string> CategoryOptions { get; } = [];
    public ObservableCollection<CheckItem> ValueOptions { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayTitle))]
    private string _title;

    [ObservableProperty]
    private double _x;

    [ObservableProperty]
    private double _y;

    [ObservableProperty]
    private double _width;

    [ObservableProperty]
    private double _height;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMessage))]
    private string? _message;

    [ObservableProperty]
    private string? _table;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsGranularity))]
    private string? _categoryField;

    [ObservableProperty]
    private Option<Aggregation> _aggregation;

    [ObservableProperty]
    private Option<DateGranularity> _granularity;

    [ObservableProperty]
    private decimal? _topN;

    public bool HasMessage => Message is not null;

    public string DisplayTitle => string.IsNullOrWhiteSpace(Title) ? AutoTitle : Title;

    /// <summary>Заголовок по умолчанию, например «Сумма: Выручка по Регион».</summary>
    public virtual string AutoTitle
    {
        get
        {
            var values = Definition.ValueFields.Count == 0
                ? "Количество строк"
                : $"{Aggregation.Label}: {string.Join(", ", Definition.ValueFields)}";
            return CategoryField is null ? values : $"{values} по {CategoryField}";
        }
    }

    // Какие настройки показывать в панели полей (зависит от типа визуала).
    public virtual string CategoryCaption => "Ось";
    public virtual string ValuesCaption => "Значения";
    public virtual bool ShowsCategory => true;
    public virtual bool ShowsValues => true;
    public virtual bool ShowsAggregation => true;
    public virtual bool ShowsTopN => true;

    public bool ShowsGranularity =>
        ShowsCategory && Kind != VisualKind.Scatter && Owner.Model.GetTable(Table)?.Columns[CategoryField ?? ""] is { } c
        && c.DataType == typeof(DateTime);

    /// <summary>Пересчитывает данные визуала с учётом фильтров отчёта.</summary>
    public void Refresh()
    {
        try
        {
            Message = null;
            RefreshCore();
        }
        catch (Exception e) when (e is InvalidOperationException or ArgumentException or InvalidCastException)
        {
            Message = e.Message;
        }
        OnPropertyChanged(nameof(DisplayTitle));
    }

    protected abstract void RefreshCore();

    /// <summary>Таблица визуала или null, если она не выбрана/удалена.</summary>
    protected DataTable? GetTable() => Owner.Model.GetTable(Table);

    /// <summary>Строки таблицы после фильтров страницы и срезов.</summary>
    protected IEnumerable<DataRow> GetRows(DataTable table) =>
        QueryEngine.Filter(table, Owner.ActiveFilters(table.TableName, this));

    /// <summary>Вызывается при изменении модели данных: обновляет списки полей и данные.</summary>
    public void OnModelChanged()
    {
        _loading = true;
        if (Table is not null && Owner.Model.GetTable(Table) is null)
            Table = null; // таблицу удалили
        Table ??= Owner.TableNames.FirstOrDefault();
        UpdateFieldOptions();
        _loading = false;
        OnPropertyChanged(nameof(ShowsGranularity));
        Refresh();
    }

    private void UpdateFieldOptions()
    {
        var table = Owner.Model.GetTable(Table);
        var columns = table?.Columns.Cast<DataColumn>().ToList() ?? [];

        CategoryOptions.Clear();
        foreach (var column in columns)
            CategoryOptions.Add(column.ColumnName);

        // Числовые столбцы идут первыми — их чаще выбирают как значения.
        ValueOptions.Clear();
        foreach (var column in columns.OrderBy(c => TypeInference.IsNumeric(c) ? 0 : 1))
            ValueOptions.Add(new CheckItem(column.ColumnName, Definition.ValueFields.Contains(column.ColumnName), OnValueFieldsChanged));

        if (table is not null)
        {
            Definition.ValueFields.RemoveAll(f => !table.Columns.Contains(f));
            if (CategoryField is not null && !table.Columns.Contains(CategoryField))
                CategoryField = null;
        }
    }

    private void OnValueFieldsChanged()
    {
        if (_loading)
            return;
        // Сохраняем порядок выбора: новые поля добавляются в конец.
        var selected = ValueOptions.Where(o => o.IsChecked).Select(o => o.Name).ToList();
        Definition.ValueFields.RemoveAll(f => !selected.Contains(f));
        Definition.ValueFields.AddRange(selected.Where(f => !Definition.ValueFields.Contains(f)));
        Refresh();
    }

    partial void OnTitleChanged(string value) => Definition.Title = value;
    partial void OnXChanged(double value) => Definition.X = value;
    partial void OnYChanged(double value) => Definition.Y = value;
    partial void OnWidthChanged(double value) => Definition.Width = value;
    partial void OnHeightChanged(double value) => Definition.Height = value;

    partial void OnTableChanged(string? value)
    {
        Definition.Table = value;
        if (_loading)
            return;
        _loading = true;
        Definition.ValueFields.Clear();
        Definition.SelectedValues.Clear();
        CategoryField = null;
        UpdateFieldOptions();
        _loading = false;
        Refresh();
        Owner.OnVisualFiltersChanged(this);
    }

    partial void OnCategoryFieldChanged(string? value)
    {
        Definition.CategoryField = value;
        if (_loading)
            return;
        Definition.SelectedValues.Clear();
        Refresh();
        Owner.OnVisualFiltersChanged(this);
    }

    partial void OnAggregationChanged(Option<Aggregation> value)
    {
        Definition.Aggregation = value.Value;
        if (!_loading) Refresh();
    }

    partial void OnGranularityChanged(Option<DateGranularity> value)
    {
        Definition.DateGranularity = value.Value;
        if (!_loading) Refresh();
    }

    partial void OnTopNChanged(decimal? value)
    {
        Definition.TopN = (int)(value ?? 0);
        if (!_loading) Refresh();
    }

    public void ClearCategory() => CategoryField = null;

    public void Select() => Owner.Select(this);

    public void Delete() => Owner.Delete(this);

    public void Duplicate() => Owner.Duplicate(this);
}
