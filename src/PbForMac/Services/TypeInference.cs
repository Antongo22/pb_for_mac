using System.Data;
using System.Globalization;
using PbForMac.Models;

namespace PbForMac.Services;

/// <summary>
/// Определяет типы столбцов по значениям и приводит «сырую» таблицу импортёра
/// (столбцы типа object со строками или нативными значениями) к типизированной.
/// </summary>
public static class TypeInference
{
    private static readonly CultureInfo Ru = CultureInfo.GetCultureInfo("ru-RU");

    private static readonly string[] DateFormats =
    [
        "yyyy-MM-dd", "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd HH:mm", "yyyy-MM-ddTHH:mm:ss.fff",
        "yyyy-MM-ddTHH:mm:ssZ", "yyyy-MM-ddTHH:mm:ss.fffZ", "yyyy/MM/dd",
        "dd.MM.yyyy", "dd.MM.yyyy HH:mm:ss", "dd.MM.yyyy HH:mm", "d.M.yyyy",
        "MM/dd/yyyy", "M/d/yyyy", "MM/dd/yyyy HH:mm:ss",
    ];

    public static Type ClrType(ColumnType type) => type switch
    {
        ColumnType.Integer => typeof(long),
        ColumnType.Decimal => typeof(double),
        ColumnType.Date => typeof(DateTime),
        ColumnType.Boolean => typeof(bool),
        _ => typeof(string),
    };

    public static ColumnType FromClr(Type type) => Type.GetTypeCode(type) switch
    {
        TypeCode.Byte or TypeCode.SByte or TypeCode.Int16 or TypeCode.UInt16 or TypeCode.Int32 or TypeCode.UInt32
            or TypeCode.Int64 or TypeCode.UInt64 => ColumnType.Integer,
        TypeCode.Single or TypeCode.Double or TypeCode.Decimal => ColumnType.Decimal,
        TypeCode.DateTime => ColumnType.Date,
        TypeCode.Boolean => ColumnType.Boolean,
        _ => ColumnType.Text,
    };

    public static bool IsNumeric(ColumnType type) => type is ColumnType.Integer or ColumnType.Decimal;

    public static bool IsNumeric(DataColumn column) => IsNumeric(FromClr(column.DataType));

    /// <summary>Приводит все столбцы к наиболее подходящему типу.</summary>
    public static DataTable Apply(DataTable raw)
    {
        var result = new DataTable(raw.TableName);
        var types = new ColumnType[raw.Columns.Count];
        for (var c = 0; c < raw.Columns.Count; c++)
        {
            var column = raw.Columns[c];
            types[c] = column.DataType == typeof(object) || column.DataType == typeof(string)
                ? Detect(raw.Rows.Cast<DataRow>().Select(r => r[c]))
                : FromClr(column.DataType);
            result.Columns.Add(column.ColumnName, ClrType(types[c]));
        }

        result.BeginLoadData();
        var values = new object[raw.Columns.Count];
        foreach (DataRow row in raw.Rows)
        {
            for (var c = 0; c < values.Length; c++)
                values[c] = Convert(row[c], types[c]);
            result.Rows.Add(values);
        }
        result.EndLoadData();
        return result;
    }

    /// <summary>Определяет тип по набору значений (пустые значения игнорируются).</summary>
    public static ColumnType Detect(IEnumerable<object?> values)
    {
        bool allInt = true, allNum = true, allDate = true, allBool = true, any = false;
        foreach (var value in values)
        {
            if (IsEmpty(value))
                continue;
            any = true;
            switch (value)
            {
                case long or int or short or byte or sbyte or ushort or uint or ulong:
                    allDate = allBool = false;
                    break;
                case double d:
                    allDate = allBool = false;
                    if (d != Math.Floor(d) || Math.Abs(d) > long.MaxValue) allInt = false;
                    break;
                case float or decimal:
                    allInt = allDate = allBool = false;
                    break;
                case DateTime or DateTimeOffset or DateOnly:
                    allInt = allNum = allBool = false;
                    break;
                case bool:
                    allInt = allNum = allDate = false;
                    break;
                default:
                    var s = value!.ToString()!.Trim();
                    if (allInt && !TryParseLong(s, out _)) allInt = false;
                    if (allNum && !TryParseDouble(s, out _)) allNum = false;
                    if (allBool && !TryParseBool(s, out _)) allBool = false;
                    if (allDate && (allNum || !TryParseDate(s, out _))) allDate = false;
                    break;
            }

            if (!allInt && !allNum && !allDate && !allBool)
                return ColumnType.Text;
        }

        if (!any) return ColumnType.Text;
        if (allBool) return ColumnType.Boolean;
        if (allInt) return ColumnType.Integer;
        if (allNum) return ColumnType.Decimal;
        return allDate ? ColumnType.Date : ColumnType.Text;
    }

    /// <summary>Преобразует значение к типу; при неудаче возвращает <see cref="DBNull"/>.</summary>
    public static object Convert(object? value, ColumnType type)
    {
        if (IsEmpty(value))
            return DBNull.Value;

        try
        {
            switch (type)
            {
                case ColumnType.Text:
                    return value switch
                    {
                        DateTime dt => FormatDate(dt),
                        double d => d.ToString(CultureInfo.InvariantCulture),
                        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
                        _ => value!.ToString()!,
                    };
                case ColumnType.Integer:
                    return value switch
                    {
                        long l => l,
                        double d => (long)Math.Round(d),
                        bool b => b ? 1L : 0L,
                        IConvertible c and not string => c.ToInt64(CultureInfo.InvariantCulture),
                        _ => TryParseLong(value!.ToString()!, out var l) ? l
                            : TryParseDouble(value.ToString()!, out var d) ? (long)Math.Round(d) : DBNull.Value,
                    };
                case ColumnType.Decimal:
                    return value switch
                    {
                        double d => d,
                        bool b => b ? 1d : 0d,
                        IConvertible c and not string and not DateTime => c.ToDouble(CultureInfo.InvariantCulture),
                        _ => TryParseDouble(value!.ToString()!, out var d) ? d : DBNull.Value,
                    };
                case ColumnType.Date:
                    return value switch
                    {
                        DateTime dt => dt,
                        DateTimeOffset dto => dto.DateTime,
                        DateOnly d => d.ToDateTime(TimeOnly.MinValue),
                        double oa => DateTime.FromOADate(oa),
                        _ => TryParseDate(value!.ToString()!, out var dt) ? dt : DBNull.Value,
                    };
                case ColumnType.Boolean:
                    return value switch
                    {
                        bool b => b,
                        long l => l != 0,
                        double d => d != 0,
                        _ => TryParseBool(value!.ToString()!, out var b) ? b : DBNull.Value,
                    };
            }
        }
        catch (Exception e) when (e is FormatException or InvalidCastException or OverflowException or ArgumentException)
        {
            return DBNull.Value;
        }

        return DBNull.Value;
    }

    public static bool IsEmpty(object? value) =>
        value is null || value is DBNull || (value is string s && string.IsNullOrWhiteSpace(s));

    public static bool TryParseLong(string s, out long result)
    {
        s = s.Trim();
        // Числа с ведущими нулями («007») обычно коды, а не количества.
        if (s.Length > 1 && s[0] == '0')
        {
            result = 0;
            return false;
        }
        return long.TryParse(s, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out result);
    }

    public static bool TryParseDouble(string s, out double result)
    {
        s = s.Trim();
        if (s.Length > 1 && s[0] == '0' && char.IsDigit(s[1]))
        {
            result = 0;
            return false;
        }
        if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out result))
            return true;
        // «1 234,56» и «1234,56» — русский формат.
        var compact = s.Replace(" ", "").Replace(" ", "").Replace(" ", "");
        return double.TryParse(compact, NumberStyles.Float, Ru, out result);
    }

    public static bool TryParseDate(string s, out DateTime result)
    {
        s = s.Trim();
        if (DateTime.TryParseExact(s, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out result))
            return true;
        if (s.Length >= 8 && (s.Contains('-') || s.Contains('.') || s.Contains('/')))
            return DateTime.TryParse(s, Ru, DateTimeStyles.AllowWhiteSpaces, out result)
                   || DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out result);
        return false;
    }

    public static bool TryParseBool(string s, out bool result)
    {
        switch (s.Trim().ToLowerInvariant())
        {
            case "true" or "да" or "yes":
                result = true;
                return true;
            case "false" or "нет" or "no":
                result = false;
                return true;
            default:
                result = false;
                return false;
        }
    }

    public static string FormatDate(DateTime dt) =>
        dt.TimeOfDay == TimeSpan.Zero
            ? dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : dt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
}
