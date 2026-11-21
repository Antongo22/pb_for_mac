using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace PbForMac.Views;

/// <summary>#RRGGBB или transparent → кисть. Пустое значение — ресурс темы из ConverterParameter.</summary>
public sealed class HexBrushConverter : IValueConverter
{
    public static readonly HexBrushConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string text && !string.IsNullOrWhiteSpace(text))
        {
            if (text.Equals("transparent", StringComparison.OrdinalIgnoreCase))
                return Brushes.Transparent;
            if (Color.TryParse(text, out var color))
                return new SolidColorBrush(color);
        }

        if (parameter is string key
            && Application.Current?.TryGetResource(key, Application.Current.ActualThemeVariant, out var resource) == true
            && resource is IBrush brush)
            return brush;

        return AvaloniaProperty.UnsetValue;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class DoubleThicknessConverter : IValueConverter
{
    public static readonly DoubleThicknessConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is double d ? new Thickness(Math.Max(0, d)) : new Thickness(0);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is Thickness t ? t.Left : 0d;
}

public sealed class DoubleCornerConverter : IValueConverter
{
    public static readonly DoubleCornerConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is double d ? new CornerRadius(Math.Max(0, d)) : new CornerRadius(0);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is CornerRadius r ? r.TopLeft : 0d;
}

public sealed class DoubleDecimalConverter : IValueConverter
{
    public static readonly DoubleDecimalConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is double d ? (decimal)d : 0m;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is decimal d ? (double)d : 0d;
}
