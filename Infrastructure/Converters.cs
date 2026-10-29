using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace StaffManagement.Infrastructure;

/// <summary>true → Visible; with ConverterParameter=Invert, true → Collapsed. Non-bool values: null/empty → Collapsed.</summary>
public sealed class VisibleIfConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool v = value switch
        {
            bool b => b,
            string s => !string.IsNullOrWhiteSpace(s),
            int i => i != 0,
            System.Collections.ICollection c => c.Count > 0,
            null => false,
            _ => true,
        };
        if (parameter as string == "Invert") v = !v;
        return v ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is Visibility.Visible;
}

/// <summary>Bool cell display in the grid: true → "✓".</summary>
public sealed class CheckMarkConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is true ? "✓" : "";
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not true;
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not true;
}

/// <summary>Percentage (0–100) → bar width, ConverterParameter = width at 100 %.</summary>
public sealed class PercentToWidthConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        double max = double.TryParse(parameter as string, NumberStyles.Float, CultureInfo.InvariantCulture, out var m) ? m : 200;
        return value is double d ? Math.Max(2, d / 100 * max) : 0d;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}
