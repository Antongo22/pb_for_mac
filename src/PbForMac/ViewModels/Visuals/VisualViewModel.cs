using System.Collections.ObjectModel;
using System.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
        _showLegend = definition.ShowLegend;
        _showDataLabels = definition.ShowDataLabels;
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

    [ObservableProperty]
    private bool _showLegend;

    [ObservableProperty]
    private bool _showDataLabels;

    public bool HasMessage => Message is not null;

    public string DisplayTitle => string.IsNullOrWhiteSpace(Title) ? AutoTitle : Title;

    /// <summary>Заголовок по умолчанию, например «Сумма: Выручка по Регион».</summary>
    public virtual string AutoTitle
    {
        get
        {
            var values = Definition.ValueFields.Count == 0
                ? "Количество строк"
                : $"{Aggregation.Label}: {string.Join(", ", Definition.ValueFields.Select(FieldRef.Display))}";
            return CategoryField is null ? values : $"{values} по {FieldRef.Display(CategoryField)}";
        }
    }

    // Какие настройки показывать в панели полей (зависит от типа визуала).
    public virtual string CategoryCaption => "Ось";
    public virtual string ValuesCaption => "Значения";
    public virtual bool ShowsCategory => true;
    public virtual bool ShowsValues => true;
    public virtual bool ShowsAggregation => true;
    public virtual bool ShowsTopN => true;
    public virtual bool ShowsFormatting => false;

    public bool ShowsGranularity =>
        ShowsCategory && Kind != VisualKind.Scatter && GetTable() is { } table
        && Field(table, CategoryField)?.DataType == typeof(DateTime);

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

    /// <summary>Строки таблицы после фильтров страницы и срезов (в том числе фильтров связанных справочников).</summary>
    protected IEnumerable<DataRow> GetRows(DataTable table) =>
        Owner.Query.Filter(table, Owner.ActiveFilters(this));

    /// <summary>Поле визуала: столбец таблицы или «Таблица[Столбец]» связанного справочника.</summary>
    protected ResolvedField? Field(DataTable table, string? reference) =>
        reference is null ? null : Owner.Query.Resolve(table, reference);

    /// <summary>Поле, которое обязано существовать; иначе визуал покажет понятное сообщение.</summary>
    protected ResolvedField RequireField(DataTable table, string reference) =>
        Field(table, reference) ?? throw new InvalidOperationException(
            $"Поле «{reference}» недоступно: нет такого столбца или связи с таблицей «{table.TableName}».");

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
        var table = GetTable();
        // Столбцы самой таблицы, затем столбцы связанных справочников («Таблица[Столбец]»).
        var fields = table is null
            ? []
            : Owner.Query.AvailableFields(table).Select(f => Field(table, f)).OfType<ResolvedField>().ToList();

        CategoryOptions.Clear();
        foreach (var field in fields)
            CategoryOptions.Add(field.Ref);

        // Числовые поля идут первыми — их чаще выбирают как значения.
        ValueOptions.Clear();
        foreach (var field in fields.OrderBy(f => TypeInference.IsNumeric(TypeInference.FromClr(f.DataType)) ? 0 : 1))
            ValueOptions.Add(new CheckItem(field.Ref, Definition.ValueFields.Contains(field.Ref), OnValueFieldsChanged));

        if (table is not null)
        {
            Definition.ValueFields.RemoveAll(f => Field(table, f) is null);
            if (CategoryField is not null && Field(table, CategoryField) is null)
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

    partial void OnShowLegendChanged(bool value)
    {
        Definition.ShowLegend = value;
        if (!_loading) Refresh();
    }

    partial void OnShowDataLabelsChanged(bool value)
    {
        Definition.ShowDataLabels = value;
        if (!_loading) Refresh();
    }

    public void ClearCategory() => CategoryField = null;

    /// <summary>Назначает поле в колодец категории (перетаскивание).</summary>
    public void AssignFieldToCategory(string tableName, string columnName)
    {
        if (!ShowsCategory)
            return;
        if (!string.Equals(Table, tableName, StringComparison.OrdinalIgnoreCase))
            Table = tableName;
        CategoryField = ResolveFieldRef(tableName, columnName);
    }

    /// <summary>Добавляет/включает поле в колодец значений (перетаскивание).</summary>
    public void AssignFieldToValues(string tableName, string columnName)
    {
        if (!ShowsValues)
        {
            AssignField(tableName, columnName);
            return;
        }
        if (!string.Equals(Table, tableName, StringComparison.OrdinalIgnoreCase))
            Table = tableName;
        var fieldRef = ResolveFieldRef(tableName, columnName);
        if (fieldRef is null)
            return;
        var option = ValueOptions.FirstOrDefault(o =>
            string.Equals(o.Name, fieldRef, StringComparison.OrdinalIgnoreCase));
        if (option is not null)
        {
            option.IsChecked = true;
            return;
        }
        if (!Definition.ValueFields.Contains(fieldRef))
        {
            Definition.ValueFields.Add(fieldRef);
            UpdateFieldOptions();
            Refresh();
        }
    }

    private string? ResolveFieldRef(string tableName, string columnName)
    {
        var table = GetTable();
        if (table is null)
            return null;
        if (!string.Equals(table.TableName, tableName, StringComparison.OrdinalIgnoreCase))
            return FieldRef.Format(tableName, columnName);
        return Field(table, columnName) is not null ? columnName : FieldRef.Format(tableName, columnName);
    }

    /// <summary>
    /// Назначает поле из панели «Поля»: категория, если пуста, иначе значение.
    /// При другой таблице — переключает визуал на неё.
    /// </summary>
    public void AssignField(string tableName, string columnName)
    {
        if (string.IsNullOrWhiteSpace(tableName) || string.IsNullOrWhiteSpace(columnName))
            return;

        if (!string.Equals(Table, tableName, StringComparison.OrdinalIgnoreCase))
            Table = tableName;

        var table = GetTable();
        if (table is null)
            return;

        // Для связанного поля — полный ref; для своей таблицы — имя столбца.
        var fieldRef = columnName;
        if (!string.Equals(table.TableName, tableName, StringComparison.OrdinalIgnoreCase))
            fieldRef = FieldRef.Format(tableName, columnName);
        else if (Field(table, columnName) is null && Field(table, FieldRef.Format(tableName, columnName)) is { } linked)
            fieldRef = linked.Ref;

        if (ShowsCategory && CategoryField is null)
        {
            CategoryField = fieldRef;
            return;
        }

        if (ShowsValues)
        {
            var option = ValueOptions.FirstOrDefault(o =>
                string.Equals(o.Name, fieldRef, StringComparison.OrdinalIgnoreCase)
                || string.Equals(FieldRef.Display(o.Name), columnName, StringComparison.OrdinalIgnoreCase));
            if (option is not null)
            {
                option.IsChecked = !option.IsChecked;
                return;
            }
            if (!Definition.ValueFields.Contains(fieldRef))
            {
                Definition.ValueFields.Add(fieldRef);
                UpdateFieldOptions();
                Refresh();
            }
            return;
        }

        if (ShowsCategory)
            CategoryField = fieldRef;
    }

    public void Select() => Owner.Select(this);

    /// <summary>Удаление с подтверждением (кнопка на плитке и в панели «Поля»).</summary>
    [RelayCommand]
    private Task DeleteAsync() => Owner.DeleteAsync(this);

    public void Duplicate() => Owner.Duplicate(this);
}
