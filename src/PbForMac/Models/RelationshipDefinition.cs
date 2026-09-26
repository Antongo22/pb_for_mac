namespace PbForMac.Models;

/// <summary>
/// Связь «многие к одному»: каждая строка таблицы <see cref="FromTable"/> находит строку
/// таблицы <see cref="ToTable"/> с тем же значением ключа. Фильтры таблицы «один» распространяются
/// на таблицу «многие», а её столбцы доступны в визуалах таблицы «многие».
/// </summary>
public sealed class RelationshipDefinition
{
    /// <summary>Таблица на стороне «многие» (например, продажи).</summary>
    public string FromTable { get; set; } = "";
    public string FromColumn { get; set; } = "";

    /// <summary>Таблица на стороне «один» (справочник, например, товары).</summary>
    public string ToTable { get; set; } = "";
    public string ToColumn { get; set; } = "";

    public override string ToString() => $"{FieldRef.Format(FromTable, FromColumn)} → {FieldRef.Format(ToTable, ToColumn)}";

    public bool Connects(string table) =>
        string.Equals(FromTable, table, StringComparison.OrdinalIgnoreCase)
        || string.Equals(ToTable, table, StringComparison.OrdinalIgnoreCase);

    public bool SameAs(RelationshipDefinition other) =>
        string.Equals(FromTable, other.FromTable, StringComparison.OrdinalIgnoreCase)
        && string.Equals(FromColumn, other.FromColumn, StringComparison.OrdinalIgnoreCase)
        && string.Equals(ToTable, other.ToTable, StringComparison.OrdinalIgnoreCase)
        && string.Equals(ToColumn, other.ToColumn, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Ссылка на поле визуала или фильтра: «Столбец» — столбец основной таблицы,
/// «Таблица[Столбец]» — столбец связанной таблицы (как в DAX).
/// </summary>
public static class FieldRef
{
    public static string Format(string table, string column) => $"{table}[{column}]";

    /// <summary>Разбирает «Таблица[Столбец]»; для простого имени столбца возвращает false.</summary>
    public static bool TryParse(string reference, out string table, out string column)
    {
        table = column = "";
        if (!reference.EndsWith(']'))
            return false;
        var open = reference.IndexOf('[');
        if (open <= 0)
            return false;
        table = reference[..open];
        column = reference[(open + 1)..^1];
        return column.Length > 0;
    }

    /// <summary>Короткое имя для заголовков и легенд: имя столбца без таблицы.</summary>
    public static string Display(string reference) =>
        TryParse(reference, out _, out var column) ? column : reference;
}
