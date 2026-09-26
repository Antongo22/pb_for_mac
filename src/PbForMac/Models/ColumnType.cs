namespace PbForMac.Models;

/// <summary>Логический тип столбца в модели данных.</summary>
public enum ColumnType
{
    Text,
    Integer,
    Decimal,
    Date,
    Boolean,
}

/// <summary>Текстовое преобразование столбца (как в Power Query).</summary>
public enum TextTransformKind
{
    Trim,
    Upper,
    Lower,
    Clean, // убрать управляющие символы
}
