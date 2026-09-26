using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using PbForMac.Models;

namespace PbForMac.ViewModels;

/// <summary>Таблица модели в списке.</summary>
public sealed record TableItem(string Name, string Description)
{
    public override string ToString() => Name;
}

/// <summary>Столбец таблицы с типом; флажок — для пакетных операций.</summary>
public sealed partial class ColumnItem(string name, ColumnType type, Action<ColumnItem>? insert = null) : ObservableObject
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

    [ObservableProperty]
    private bool _isChecked;

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

/// <summary>Узел дерева таблиц: папка (с вложенными узлами) или таблица.</summary>
public sealed partial class TableTreeNode : ObservableObject
{
    private TableTreeNode(string name, string path, TableItem? table)
    {
        Name = name;
        Path = path;
        Table = table;
    }

    public static TableTreeNode Folder(string name, string path) => new(name, path, null);

    public static TableTreeNode ForTable(TableItem table) => new(table.Name, table.Name, table);

    public string Name { get; }

    /// <summary>Путь папки («dataset/sales») или имя таблицы.</summary>
    public string Path { get; }

    public TableItem? Table { get; }
    public bool IsFolder => Table is null;
    public ObservableCollection<TableTreeNode> Children { get; } = [];

    public string Description => Table?.Description ?? CountLabel(AllTables().Count());

    [ObservableProperty]
    private bool _isExpanded = true;

    /// <summary>Все таблицы внутри узла (для папки — рекурсивно).</summary>
    public IEnumerable<TableItem> AllTables() =>
        Table is not null ? [Table] : Children.SelectMany(c => c.AllTables());

    private static string CountLabel(int count) => (count % 100, count % 10) switch
    {
        ( >= 11 and <= 14, _) => $"{count} таблиц",
        (_, 1) => $"{count} таблица",
        (_, >= 2 and <= 4) => $"{count} таблицы",
        _ => $"{count} таблиц",
    };
}

/// <summary>Связь в списке на вкладке «Связи».</summary>
public sealed class RelationshipItemViewModel(
    RelationshipDefinition relationship, double? matchRate, string? issue, Action<RelationshipItemViewModel> remove)
{
    public RelationshipDefinition Relationship { get; } = relationship;
    public string FromText => FieldRef.Format(Relationship.FromTable, Relationship.FromColumn);
    public string ToText => FieldRef.Format(Relationship.ToTable, Relationship.ToColumn);

    public string Details => matchRate is { } rate
        ? $"многие к одному · в справочнике найдено {rate:P0} ключей"
        : "многие к одному";

    public string? Issue { get; } = issue;
    public bool HasIssue => Issue is not null;

    public void Remove() => remove(this);
}
