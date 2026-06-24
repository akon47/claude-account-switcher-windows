using System.Globalization;

namespace ClaudeAccountSwitcher.Converters;

public class NumberGreaterEqualConverter : ValueAndMultiValueConverterMarkupExtension<NumberGreaterEqualConverter>
{
    public override object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not IComparable comparable)
        {
            return null;
        }

        var normalizedParameter = System.Convert.ChangeType(parameter, value.GetType(), culture);
        return comparable.CompareTo(normalizedParameter) >= 0;
    }

    public override object? Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
        => values is { Length: 2 } && values[0] is IComparable comparable && comparable.CompareTo(values[1]) >= 0;
}
