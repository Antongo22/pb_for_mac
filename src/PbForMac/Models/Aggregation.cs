namespace PbForMac.Models;

public enum Aggregation
{
    Sum,
    Average,
    Count,
    DistinctCount,
    Min,
    Max,
}

/// <summary>Шаг детализации дат при группировке по столбцу-дате.</summary>
public enum DateGranularity
{
    Day,
    Month,
    Quarter,
    Year,
}
