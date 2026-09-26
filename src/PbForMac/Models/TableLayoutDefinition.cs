namespace PbForMac.Models;

/// <summary>Позиция карточки таблицы на диаграмме связей (сохраняется в отчёте).</summary>
public sealed class TableLayoutDefinition
{
    public string Table { get; set; } = "";
    public double X { get; set; }
    public double Y { get; set; }
}
