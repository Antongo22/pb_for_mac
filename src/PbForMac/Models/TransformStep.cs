using System.Text.Json.Serialization;

namespace PbForMac.Models;

/// <summary>
/// Шаг преобразования данных (аналог «Применённых шагов» Power Query).
/// Шаги хранятся в отчёте и воспроизводятся по порядку после загрузки источников.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$step")]
[JsonDerivedType(typeof(RenameColumnStep), "rename")]
[JsonDerivedType(typeof(RemoveColumnStep), "remove")]
[JsonDerivedType(typeof(RemoveColumnsStep), "removeColumns")]
[JsonDerivedType(typeof(KeepColumnsStep), "keepColumns")]
[JsonDerivedType(typeof(MoveColumnStep), "moveColumn")]
[JsonDerivedType(typeof(ChangeTypeStep), "changeType")]
[JsonDerivedType(typeof(CalculatedColumnStep), "calculated")]
[JsonDerivedType(typeof(FilterRowsStep), "filter")]
[JsonDerivedType(typeof(RemoveDuplicatesStep), "distinct")]
[JsonDerivedType(typeof(GroupByStep), "groupBy")]
public abstract class TransformStep
{
    /// <summary>Таблица, к которой применяется шаг.</summary>
    public string Table { get; set; } = "";

    [JsonIgnore]
    public abstract string Description { get; }
}

public sealed class RenameColumnStep : TransformStep
{
    public string Column { get; set; } = "";
    public string NewName { get; set; } = "";
    public override string Description => $"Переименован столбец «{Column}» → «{NewName}»";
}

public sealed class RemoveColumnStep : TransformStep
{
    public string Column { get; set; } = "";
    public override string Description => $"Удалён столбец «{Column}»";
}

/// <summary>Удаляет несколько столбцов одним шагом.</summary>
public sealed class RemoveColumnsStep : TransformStep
{
    public List<string> Columns { get; set; } = [];
    public override string Description =>
        Columns.Count == 1
            ? $"Удалён столбец «{Columns[0]}»"
            : $"Удалены столбцы ({Columns.Count}): {string.Join(", ", Columns.Select(c => $"«{c}»"))}";
}

/// <summary>Оставляет только перечисленные столбцы (остальные удаляются).</summary>
public sealed class KeepColumnsStep : TransformStep
{
    public List<string> Columns { get; set; } = [];
    public override string Description =>
        $"Оставлены столбцы ({Columns.Count}): {string.Join(", ", Columns.Select(c => $"«{c}»"))}";
}

/// <summary>Перемещает столбец на новую позицию (0 — первый).</summary>
public sealed class MoveColumnStep : TransformStep
{
    public string Column { get; set; } = "";
    public int NewOrdinal { get; set; }
    public override string Description => $"Столбец «{Column}» перемещён на позицию {NewOrdinal + 1}";
}

public sealed class ChangeTypeStep : TransformStep
{
    public string Column { get; set; } = "";
    public ColumnType TargetType { get; set; }
    public override string Description => $"Тип «{Column}» → {Labels.Of(TargetType)}";
}

public sealed class CalculatedColumnStep : TransformStep
{
    public string Name { get; set; } = "";
    public string Expression { get; set; } = "";
    public override string Description => $"Вычисляемый столбец «{Name}» = {Expression}";
}

public sealed class FilterRowsStep : TransformStep
{
    public FilterDefinition Filter { get; set; } = new();
    public override string Description => $"Фильтр строк: {Labels.Describe(Filter)}";
}

public sealed class RemoveDuplicatesStep : TransformStep
{
    public override string Description => "Удалены дубликаты строк";
}

public sealed class AggregationSpec
{
    public string Column { get; set; } = "";
    public Aggregation Aggregation { get; set; }
    public string OutputName { get; set; } = "";
}

public sealed class GroupByStep : TransformStep
{
    public string NewTable { get; set; } = "";
    public List<string> GroupColumns { get; set; } = [];
    public List<AggregationSpec> Aggregations { get; set; } = [];

    public override string Description =>
        $"Группировка «{Table}» по {string.Join(", ", GroupColumns)} → таблица «{NewTable}»";
}
