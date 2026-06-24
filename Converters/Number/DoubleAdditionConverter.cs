using System.Globalization;
using System.Windows;

namespace ClaudeAccountSwitcher.Converters;

public class DoubleAdditionConverter : ValueAndMultiValueConverterMarkupExtension<DoubleAdditionConverter>
{
    public override object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is null
            ? value
            : System.Convert.ToDouble(value) + System.Convert.ToDouble(parameter);

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

        if (values.Length == 1)
        {
            return System.Convert.ToDouble(values[0]);
        }

        var sum = 0d;
        foreach (var value in values)
        {
            var operand = System.Convert.ToDouble(value);
            if (double.IsNaN(operand))
            {
                return double.NaN;
            }

            sum += operand;
        }

        return sum;
    }
}
