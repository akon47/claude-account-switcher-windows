using System.Globalization;
using System.Windows;

namespace ClaudeAccountSwitcher.Converters;

/// <summary>
/// int 값에 Converter Parameter 를 더한 결과를 반환한다.
/// </summary>
public class IntAdditionConverter : ValueAndMultiValueConverterMarkupExtension<IntAdditionConverter>
{
    public override object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is null
            ? value
            : System.Convert.ToInt32(value) + System.Convert.ToInt32(parameter);

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
            return System.Convert.ToInt32(values[0]);
        }

        double sum = 0;
        foreach (var value in values)
        {
            var operand = System.Convert.ToInt32(value);
            if (double.IsNaN(operand))
            {
                return double.NaN;
            }

            sum += operand;
        }

        return sum;
    }
}
