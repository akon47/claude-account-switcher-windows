using System.Globalization;
using System.Windows;

namespace ClaudeAccountSwitcher.Converters;

public class DoubleSubtractionConverter : ValueAndMultiValueConverterMarkupExtension<DoubleSubtractionConverter>
{
    private static object ConvertToTargetType(double result, Type targetType)
    {
        var underlying = Nullable.GetUnderlyingType(targetType) ?? targetType;
        if (underlying == typeof(double) || underlying == typeof(object))
        {
            return result;
        }

        try
        {
            return System.Convert.ChangeType(result, underlying, CultureInfo.InvariantCulture);
        }
        catch (Exception ex) when (ex is InvalidCastException or OverflowException or FormatException)
        {
            return result;
        }
    }

    public override object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is null
            ? value
            : ConvertToTargetType(System.Convert.ToDouble(value) - System.Convert.ToDouble(parameter), targetType);

    public override object? Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values is not { Length: > 0 })
        {
            return null;
        }

        if (Array.IndexOf(values, DependencyProperty.UnsetValue) >= 0)
        {
            return DependencyProperty.UnsetValue;
        }

        var result = System.Convert.ToDouble(values[0]);
        for (var i = 1; i < values.Length; i++)
        {
            var operand = System.Convert.ToDouble(values[i]);
            if (double.IsNaN(operand))
            {
                return double.NaN;
            }

            result -= operand;
        }

        return ConvertToTargetType(result, targetType);
    }
}
