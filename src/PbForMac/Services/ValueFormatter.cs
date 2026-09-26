using System.Globalization;

namespace PbForMac.Services;

/// <summary>Форматирование значений для отображения в интерфейсе.</summary>
public static class ValueFormatter
{
    private static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("ru-RU");

    public static string Display(object? value) => value switch
    {
        null or DBNull => "",
        DateTime dt => dt.TimeOfDay == TimeSpan.Zero
            ? dt.ToString("dd.MM.yyyy", Culture)
            : dt.ToString("dd.MM.yyyy HH:mm", Culture),
        double d => Number(d),
        long l => l.ToString("#,0", Culture),
        bool b => b ? "Да" : "Нет",
        _ => value.ToString() ?? "",
    };

    public static string Number(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
            return "—";
        return Math.Abs(value) >= 1000 || value == Math.Floor(value)
            ? value.ToString("#,0.##", Culture)
            : value.ToString("#,0.####", Culture);
    }

    /// <summary>Короткая запись больших чисел: 1,2 тыс., 3,4 млн, 5,6 млрд.</summary>
    public static string Compact(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
            return "—";
        var abs = Math.Abs(value);
        return abs switch
        {
            >= 1e9 => (value / 1e9).ToString("0.##", Culture) + " млрд",
            >= 1e6 => (value / 1e6).ToString("0.##", Culture) + " млн",
            >= 1e4 => (value / 1e3).ToString("0.#", Culture) + " тыс.",
            _ => Number(Math.Round(value, 2)),
        };
    }
}
