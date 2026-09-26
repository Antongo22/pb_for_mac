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
[JsonDerivedType(typeof(RemoveRowsStep), "removeRows")]
[JsonDerivedType(typeof(KeepRowsStep), "keepRows")]
[JsonDerivedType(typeof(RemoveDuplicatesStep), "distinct")]
[JsonDerivedType(typeof(ReplaceValuesStep), "replaceValues")]
[JsonDerivedType(typeof(FillDownStep), "fillDown")]
[JsonDerivedType(typeof(FillUpStep), "fillUp")]
[JsonDerivedType(typeof(RemoveBlankRowsStep), "removeBlankRows")]
[JsonDerivedType(typeof(SplitColumnStep), "splitColumn")]
[JsonDerivedType(typeof(GroupByStep), "groupBy")]
public abstract class TransformStep
{
    /// <summary>Таблица, к которой применяется шаг.</summary>
    public string Table { get; set; } = "";

    [JsonIgnore]
    public abstract string Description { get; }

    /// <summary>Выключенный шаг пропускается при пересборке модели (как в Power Query).</summary>
    public bool Enabled { get; set; } = true;
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

/// <summary>
/// Удаляет строки по отпечатку значений (все столбцы). При обновлении данных
/// удаляются строки с теми же значениями.
/// </summary>
public sealed class RemoveRowsStep : TransformStep
{
    public List<string> RowKeys { get; set; } = [];
    public override string Description => RowKeys.Count == 1
        ? "Удалена 1 строка"
        : $"Удалены строки ({RowKeys.Count})";
}

/// <summary>Оставляет только строки с указанными отпечатками.</summary>
public sealed class KeepRowsStep : TransformStep
{
    public List<string> RowKeys { get; set; } = [];
    public override string Description => RowKeys.Count == 1
        ? "Оставлена 1 строка"
        : $"Оставлены строки ({RowKeys.Count})";
}

public sealed class RemoveDuplicatesStep : TransformStep
{
    public override string Description => "Удалены дубликаты строк";
}

/// <summary>Заменяет значения в столбце (как «Заменить значения» в Power Query).</summary>
public sealed class ReplaceValuesStep : TransformStep
{
    public string Column { get; set; } = "";

    /// <summary>Искомое значение; пустая строка — пустые ячейки.</summary>
    public string Find { get; set; } = "";

    /// <summary>На что заменить; пустая строка — очистить ячейку.</summary>
    public string Replace { get; set; } = "";

    /// <summary>true — совпадение всей ячейки; false — подстрока в тексте.</summary>
    public bool MatchEntireCell { get; set; } = true;

    public override string Description
    {
        get
        {
            var find = string.IsNullOrEmpty(Find) ? "(пусто)" : $"«{Find}»";
            var replace = string.IsNullOrEmpty(Replace) ? "(пусто)" : $"«{Replace}»";
            var mode = MatchEntireCell ? "" : " (подстрока)";
            return $"Замена в «{Column}»: {find} → {replace}{mode}";
        }
    }
}

/// <summary>Заполняет пустые ячейки значением сверху (Fill Down).</summary>
public sealed class FillDownStep : TransformStep
{
    public string Column { get; set; } = "";
    public override string Description => $"Заполнение вниз «{Column}»";
}

/// <summary>Заполняет пустые ячейки значением снизу (Fill Up).</summary>
public sealed class FillUpStep : TransformStep
{
    public string Column { get; set; } = "";
    public override string Description => $"Заполнение вверх «{Column}»";
}

/// <summary>
/// Удаляет полностью пустые строки. Если <see cref="Columns"/> пуст —
/// строка удаляется, когда пусты все столбцы; иначе — когда пусты указанные.
/// </summary>
public sealed class RemoveBlankRowsStep : TransformStep
{
    public List<string> Columns { get; set; } = [];

    public override string Description =>
        Columns.Count == 0
            ? "Удалены пустые строки"
            : $"Удалены строки с пустыми: {string.Join(", ", Columns.Select(c => $"«{c}»"))}";
}

/// <summary>Разделяет текстовый столбец по разделителю на несколько столбцов.</summary>
public sealed class SplitColumnStep : TransformStep
{
    public string Column { get; set; } = "";
    public string Delimiter { get; set; } = ",";

    /// <summary>Максимум частей; 0 или меньше — без ограничения (по фактическому максимуму).</summary>
    public int MaxParts { get; set; }

    public override string Description
    {
        get
        {
            var delim = string.IsNullOrEmpty(Delimiter) ? "(пусто)" : $"«{Delimiter}»";
            var limit = MaxParts > 0 ? $", до {MaxParts} частей" : "";
            return $"Разделён столбец «{Column}» по {delim}{limit}";
        }
    }
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
