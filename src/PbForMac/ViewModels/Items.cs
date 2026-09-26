using CommunityToolkit.Mvvm.ComponentModel;
using PbForMac.Models;

namespace PbForMac.ViewModels;

/// <summary>Таблица модели в списке.</summary>
public sealed record TableItem(string Name, string Description)
{
    public override string ToString() => Name;
}

/// <summary>Столбец таблицы с типом.</summary>
public sealed class ColumnItem(string name, ColumnType type, Action<ColumnItem>? insert = null)
{
    public string Name { get; } = name;
    public ColumnType Type { get; } = type;
    public string TypeLabel => Labels.Of(Type);

    public string TypeGlyph => Type switch
    {
        ColumnType.Integer or ColumnType.Decimal => "Σ",
        ColumnType.Date => "◷",
        ColumnType.Boolean => "✓",
        _ => "A",
    };

    /// <summary>Вставляет ссылку на столбец в редактируемое выражение.</summary>
    public void Insert() => insert?.Invoke(this);

    public override string ToString() => Name;
}

/// <summary>Элемент со списком флажков.</summary>
public sealed partial class CheckItem(string name, bool isChecked = false, Action? changed = null) : ObservableObject
{
    public string Name { get; } = name;

    [ObservableProperty]
    private bool _isChecked = isChecked;

    partial void OnIsCheckedChanged(bool value) => changed?.Invoke();
}

/// <summary>Фильтр в списке с кнопкой удаления.</summary>
public sealed class FilterItemViewModel(FilterDefinition definition, Action<FilterItemViewModel> remove)
{
    public FilterDefinition Definition { get; } = definition;
    public string Text => Labels.Describe(Definition);
    public string TableText => Definition.Table;

    public void Remove() => remove(this);
}

/// <summary>Шаг преобразования в списке «Применённые шаги».</summary>
public sealed class StepItemViewModel(int index, TransformStep step, string? error, Action<StepItemViewModel> remove)
{
    public TransformStep Step { get; } = step;
    public string Number => $"{index}.";
    public string Table => Step.Table;
    public string Description => Step.Description;
    public string? Error { get; } = error;
    public bool HasError => Error is not null;

    public void Remove() => remove(this);
}

/// <summary>Агрегат для шага группировки.</summary>
public sealed class AggregationSpecItem(AggregationSpec spec, Action<AggregationSpecItem> remove)
{
    public AggregationSpec Spec { get; } = spec;
    public string Text => $"{Labels.Of(Spec.Aggregation)} «{Spec.Column}» → {Spec.OutputName}";

    public void Remove() => remove(this);
}

/// <summary>Показатель профиля столбца.</summary>
public sealed record StatItem(string Label, string Value);
