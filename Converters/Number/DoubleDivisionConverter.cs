using System.Globalization;
using System.Windows;

namespace ClaudeAccountSwitcher.Converters;

public class DoubleDivisionConverter : ValueAndMultiValueConverterMarkupExtension<DoubleDivisionConverter>
{
    public override object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is null
            ? value
            : System.Convert.ToDouble(value) / System.Convert.ToDouble(parameter);

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

        var quotient = System.Convert.ToDouble(values[0]);
        for (var i = 1; i < values.Length; i++)
        {
            var operand = System.Convert.ToDouble(values[i]);
            if (double.IsNaN(operand))
            {
                return double.NaN;
            }

            quotient /= operand;
        }

        return quotient;
    }
}
