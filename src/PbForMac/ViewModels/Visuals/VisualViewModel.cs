using System.Collections.ObjectModel;
using System.Data;
using Avalonia;
using Avalonia.Layout;
using Avalonia.Media;
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
        _columnField = definition.ColumnField;
        _aggregation = Labels.Find(definition.Aggregation);
        _granularity = Labels.Find(definition.DateGranularity);
        _topN = definition.TopN;
        _showLegend = definition.ShowLegend;
        _showDataLabels = definition.ShowDataLabels;
        _text = definition.Text;
        _fill = definition.Fill;
        _stroke = definition.Stroke;
        _foreground = definition.Foreground;
        _strokeThickness = definition.StrokeThickness;
        _cornerRadius = definition.CornerRadius;
        _padding = definition.Padding;
        _fontSize = definition.FontSize <= 0 ? 14 : definition.FontSize;
        _bold = definition.Bold;
        _showTitle = definition.ShowTitle;
        _shape = Labels.Find(definition.Shape);
        _align = Labels.Find(definition.Align);
        _action = Labels.Find(definition.Action);
        _actionTarget = definition.ActionTarget;
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
    private string? _columnField;

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

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AutoTitle))]
    [NotifyPropertyChangedFor(nameof(DisplayTitle))]
    private string _text = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EffectiveFill))]
    private string? _fill;

    [ObservableProperty]
    private string? _stroke;

    [ObservableProperty]
    private string? _foreground;

    [ObservableProperty]
    private double _strokeThickness = 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EffectiveRadius))]
    private double _cornerRadius = 4;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PaddingThickness))]
    [NotifyPropertyChangedFor(nameof(ContentMargin))]
    private double _padding = 8;

    [ObservableProperty]
    private double _fontSize = 14;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TextWeight))]
    private bool _bold;

    [ObservableProperty]
    private bool _showTitle = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEllipse))]
    [NotifyPropertyChangedFor(nameof(IsTriangle))]
    [NotifyPropertyChangedFor(nameof(IsLine))]
    [NotifyPropertyChangedFor(nameof(IsFreeform))]
    [NotifyPropertyChangedFor(nameof(EffectiveFill))]
    [NotifyPropertyChangedFor(nameof(EffectiveStroke))]
    [NotifyPropertyChangedFor(nameof(EffectiveRadius))]
    private Option<ShapeKind> _shape = Labels.Shapes[1];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TextHorizontal))]
    [NotifyPropertyChangedFor(nameof(BlockAlignment))]
    private Option<DesignAlign> _align = Labels.Alignments[0];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsActionTarget))]
    private Option<DesignAction> _action = Labels.DesignActions[0];

    [ObservableProperty]
    private string? _actionTarget;

    public bool HasMessage => Message is not null;

    public bool IsDesign => Kind is VisualKind.Shape or VisualKind.Text or VisualKind.Button;
    public bool IsShape => Kind == VisualKind.Shape;
    public bool IsButton => Kind == VisualKind.Button;
    public bool IsTextBlock => Kind == VisualKind.Text;
    public bool IsEllipse => IsShape && Shape.Value == ShapeKind.Ellipse;
    public bool IsTriangle => IsShape && Shape.Value == ShapeKind.Triangle;
    public bool IsLine => IsShape && Shape.Value == ShapeKind.Line;
    public bool IsFreeform => IsEllipse || IsTriangle || IsLine;
    public bool ShowsActionTarget => IsButton && Action.Value is DesignAction.GoToPage or DesignAction.ApplyBookmark;

    public string? EffectiveFill => IsFreeform ? "transparent" : Fill;
    public double EffectiveStroke => IsFreeform ? 0 : StrokeThickness;
    public double EffectiveRadius => IsFreeform ? 0 : CornerRadius;

    public Thickness PaddingThickness => new(Math.Max(0, Padding));
    public Thickness ContentMargin => IsDesign ? PaddingThickness : new Thickness(8, 4, 8, 8);
    public FontWeight TextWeight => Bold ? FontWeight.SemiBold : FontWeight.Normal;
    public HorizontalAlignment TextHorizontal => Align.Value switch
    {
        DesignAlign.Center => HorizontalAlignment.Center,
        DesignAlign.Right => HorizontalAlignment.Right,
        _ => HorizontalAlignment.Left,
    };
    public TextAlignment BlockAlignment => Align.Value switch
    {
        DesignAlign.Center => TextAlignment.Center,
        DesignAlign.Right => TextAlignment.Right,
        _ => TextAlignment.Left,
    };

    public IReadOnlyList<Option<ShapeKind>> Shapes => Labels.Shapes;
    public IReadOnlyList<Option<DesignAlign>> Alignments => Labels.Alignments;
    public IReadOnlyList<Option<DesignAction>> DesignActions => Labels.DesignActions;

    public string DisplayTitle => string.IsNullOrWhiteSpace(Title) ? AutoTitle : Title;

    /// <summary>Заголовок по умолчанию, например «Сумма: Выручка по Регион».</summary>
    public virtual string AutoTitle
    {
        get
        {
            var values = Definition.ValueFields.Count == 0
                ? "Количество строк"
                : string.Join(", ", Definition.ValueFields.Select(f =>
                    MeasureRef.IsMeasure(f) ? MeasureRef.Display(f) : $"{Aggregation.Label}: {FieldRef.Display(f)}"));
            return CategoryField is null ? values : $"{values} по {FieldRef.Display(CategoryField)}";
        }
    }

    // Какие настройки показывать в панели полей (зависит от типа визуала).
    public virtual string CategoryCaption => "Ось";
    public virtual string ValuesCaption => "Значения";
    public virtual bool ShowsCategory => true;
    public virtual bool ShowsColumnField => false;
    public virtual bool ShowsValues => true;
    public virtual bool ShowsAggregation => true;
    public virtual bool ShowsTopN => true;
    public virtual bool ShowsFormatting => false;
    public bool ShowsDataFields => !IsDesign;

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
    public virtual void OnModelChanged()
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

        // Числовые поля идут первыми — их чаще выбирают как значения; затем меры.
        ValueOptions.Clear();
        foreach (var field in fields.OrderBy(f => TypeInference.IsNumeric(TypeInference.FromClr(f.DataType)) ? 0 : 1))
            ValueOptions.Add(new CheckItem(field.Ref, Definition.ValueFields.Contains(field.Ref), OnValueFieldsChanged));
        foreach (var measure in Owner.Model.Measures
                     .Where(m => table is null || string.Equals(m.Table, table.TableName, StringComparison.OrdinalIgnoreCase))
                     .OrderBy(m => m.Name))
        {
            var key = MeasureRef.Format(measure.Name);
            ValueOptions.Add(new CheckItem(key, Definition.ValueFields.Contains(key), OnValueFieldsChanged));
        }

        if (table is not null)
        {
            Definition.ValueFields.RemoveAll(f =>
                !MeasureRef.IsMeasure(f) && Field(table, f) is null
                || MeasureRef.TryParse(f, out var measureName)
                   && Owner.Model.Measures.All(m => !string.Equals(m.Name, measureName, StringComparison.OrdinalIgnoreCase)));
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
        ColumnField = null;
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

    partial void OnColumnFieldChanged(string? value)
    {
        Definition.ColumnField = value;
        if (!_loading)
            Refresh();
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

    partial void OnTextChanged(string value)
    {
        Definition.Text = value;
        if (!_loading) OnPropertyChanged(nameof(DisplayTitle));
    }

    partial void OnFillChanged(string? value) => Definition.Fill = value;
    partial void OnStrokeChanged(string? value) => Definition.Stroke = value;
    partial void OnForegroundChanged(string? value) => Definition.Foreground = value;
    partial void OnStrokeThicknessChanged(double value) => Definition.StrokeThickness = value;
    partial void OnCornerRadiusChanged(double value) => Definition.CornerRadius = value;
    partial void OnPaddingChanged(double value) => Definition.Padding = value;
    partial void OnFontSizeChanged(double value) => Definition.FontSize = value;
    partial void OnBoldChanged(bool value) => Definition.Bold = value;
    partial void OnShowTitleChanged(bool value) => Definition.ShowTitle = value;
    partial void OnActionTargetChanged(string? value) => Definition.ActionTarget = value;

    partial void OnShapeChanged(Option<ShapeKind> value)
    {
        Definition.Shape = value.Value;
        if (_loading || !IsShape)
            return;
        if (value.Value == ShapeKind.Line && Height > 48)
            Height = 28;
        if (value.Value == ShapeKind.Rounded && CornerRadius < 8)
            CornerRadius = 12;
    }

    partial void OnAlignChanged(Option<DesignAlign> value) => Definition.Align = value.Value;

    partial void OnActionChanged(Option<DesignAction> value) => Definition.Action = value.Value;

    [RelayCommand]
    private void SetFill(string? hex)
    {
        if (!string.IsNullOrWhiteSpace(hex))
            Fill = hex;
    }

    [RelayCommand]
    private void ApplyLook(string? look)
    {
        switch (look)
        {
            case "light":
                Fill = "#FFFFFF";
                Stroke = "#E1DFDD";
                StrokeThickness = 1;
                CornerRadius = 8;
                Foreground = "#201F1E";
                break;
            case "accent":
                Fill = "#118DFF";
                Stroke = "#0F6CBD";
                StrokeThickness = 0;
                CornerRadius = 8;
                Foreground = "#FFFFFF";
                Bold = true;
                break;
            case "dark":
                Fill = "#252423";
                Stroke = "#3B3A39";
                StrokeThickness = 1;
                CornerRadius = 8;
                Foreground = "#F3F2F1";
                break;
            case "clear":
                Fill = "transparent";
                StrokeThickness = 0;
                CornerRadius = 0;
                break;
        }
    }

    public void ClearCategory() => CategoryField = null;
    public void ClearColumnField() => ColumnField = null;

    /// <summary>Назначает поле в колодец категории (перетаскивание).</summary>
    public void AssignFieldToCategory(string tableName, string columnName)
    {
        if (!ShowsCategory)
            return;
        if (!string.Equals(Table, tableName, StringComparison.OrdinalIgnoreCase))
            Table = tableName;
        CategoryField = ResolveFieldRef(tableName, columnName);
    }

    /// <summary>Назначает поле в колодец столбцов матрицы.</summary>
    public void AssignFieldToColumn(string tableName, string columnName)
    {
        if (!ShowsColumnField)
            return;
        if (!string.Equals(Table, tableName, StringComparison.OrdinalIgnoreCase))
            Table = tableName;
        ColumnField = ResolveFieldRef(tableName, columnName);
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

    /// <summary>Назначает меру в колодец значений.</summary>
    public void AssignMeasure(string measureName)
    {
        var key = MeasureRef.Format(measureName);
        var measure = Owner.Model.Measures.FirstOrDefault(m =>
            string.Equals(m.Name, measureName, StringComparison.OrdinalIgnoreCase));
        if (measure is not null && !string.Equals(Table, measure.Table, StringComparison.OrdinalIgnoreCase))
            Table = measure.Table;

        if (!ShowsValues)
        {
            if (ShowsCategory && CategoryField is null)
                return;
        }

        var option = ValueOptions.FirstOrDefault(o => string.Equals(o.Name, key, StringComparison.OrdinalIgnoreCase));
        if (option is not null)
        {
            option.IsChecked = true;
            return;
        }
        if (!Definition.ValueFields.Contains(key))
        {
            Definition.ValueFields.Add(key);
            UpdateFieldOptions();
            Refresh();
        }
    }

    /// <summary>
    /// Назначает поле из панели «Поля»: категория, если пуста, иначе значение.
    /// При другой таблице — переключает визуал на неё.
    /// </summary>
    public void AssignField(string tableName, string columnName)
    {
        if (string.Equals(tableName, MeasureRef.PaneTable, StringComparison.OrdinalIgnoreCase))
        {
            AssignMeasure(MeasureRef.TryParse(columnName, out var fromPane) ? fromPane : columnName);
            return;
        }
        if (MeasureRef.TryParse(columnName, out var measureName))
        {
            AssignMeasure(measureName);
            return;
        }

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

        if (ShowsColumnField && ColumnField is null)
        {
            ColumnField = fieldRef;
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
