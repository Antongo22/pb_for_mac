using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using PbForMac.Models;
using PbForMac.Services;

namespace PbForMac.ViewModels.Visuals;

public sealed partial class SlicerItem(string key, string label, bool isSelected, Action changed) : ObservableObject
{
    public string Key { get; } = key;
    public string Label { get; } = label;

    [ObservableProperty]
    private bool _isSelected = isSelected;

    partial void OnIsSelectedChanged(bool value) => changed();
}

/// <summary>Срез: список значений поля, выбранные значения фильтруют остальные визуалы той же таблицы.</summary>
public sealed partial class SlicerVisualViewModel : VisualViewModel
{
    private const int MaxItems = 500;
    private bool _updating;

    public SlicerVisualViewModel(VisualDefinition definition, ReportViewModel owner) : base(definition, owner)
    {
    }

    public ObservableCollection<SlicerItem> Items { get; } = [];

    public override string CategoryCaption => "Поле";
    public override bool ShowsValues => false;
    public override bool ShowsAggregation => false;
    public override bool ShowsTopN => false;
    public override string AutoTitle => CategoryField is null ? "Срез" : FieldRef.Display(CategoryField);

    public bool HasSelection => Definition.SelectedValues.Count > 0;

    /// <summary>Фильтр, который срез накладывает на другие визуалы.</summary>
    public FilterDefinition? ActiveFilter => CategoryField is null || Table is null || Definition.SelectedValues.Count == 0
        ? null
        : new FilterDefinition { Table = Table, Column = CategoryField, Operator = FilterOperator.In, Values = [.. Definition.SelectedValues] };

    protected override void RefreshCore()
    {
        _updating = true;
        Items.Clear();
        _updating = false;

        var table = GetTable();
        if (table is null)
        {
            Message = "Выберите таблицу";
            return;
        }
        if (Field(table, CategoryField) is not { } field)
        {
            Message = "Выберите поле";
            return;
        }

        var values = GetRows(table)
            .Select(field.Get)
            .Where(v => !TypeInference.IsEmpty(v))
            .GroupBy(QueryEngine.Key)
            .Select(g => (Key: g.Key, Value: g.First()))
            .OrderBy(v => v.Value, Comparer<object?>.Create(QueryEngine.Compare))
            .Take(MaxItems);

        _updating = true;
        foreach (var (key, value) in values)
            Items.Add(new SlicerItem(key, ValueFormatter.Display(value), Definition.SelectedValues.Contains(key), OnSelectionChanged));
        _updating = false;
        OnPropertyChanged(nameof(HasSelection));
    }

    private void OnSelectionChanged()
    {
        if (_updating)
            return;
        Definition.SelectedValues.Clear();
        Definition.SelectedValues.AddRange(Items.Where(i => i.IsSelected).Select(i => i.Key));
        OnPropertyChanged(nameof(HasSelection));
        Owner.OnVisualFiltersChanged(this);
    }

    public void ClearSelection()
    {
        _updating = true;
        foreach (var item in Items)
            item.IsSelected = false;
        _updating = false;
        OnSelectionChanged();
    }
}
