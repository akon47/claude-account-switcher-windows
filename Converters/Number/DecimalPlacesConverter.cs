using System.Globalization;
using System.Windows.Data;

namespace ClaudeAccountSwitcher.Converters;

public class DecimalPlacesConverter : MultiValueConverterMarkupExtension<DecimalPlacesConverter>
{
    public override object? Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Length >= 2 && values[1] is int decimalPlaces)
        {
            var format = $"F{decimalPlaces}";
            switch (values[0])
            {
                case double doubleValue:
                    return doubleValue.ToString(format, culture);
                case float floatValue:
                    return floatValue.ToString(format, culture);
                case string text:
                    if (text == "-")
                    {
                        return text;
                    }

                    if ((text.EndsWith('.') || text.EndsWith('0')) && text.Count(c => c == '.') == 1)
                    {
                        return text;
                    }

                    if (decimal.TryParse(text, out var decimalValue))
                    {
                        return decimalValue.ToString(format, culture);
                    }

                    break;
            }
        }

        return values[0]?.ToString() ?? string.Empty;
    }

    public override object[]? ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture)
    {
        if (value is not string text)
        {
            return [Binding.DoNothing, Binding.DoNothing];
        }

        if (targetTypes[0] == typeof(double) && double.TryParse(text, NumberStyles.Float, culture, out var doubleValue))
        {
            return [doubleValue, Binding.DoNothing];
        }

        if (targetTypes[0] == typeof(float) && float.TryParse(text, NumberStyles.Float, culture, out var floatValue))
        {
            return [floatValue, Binding.DoNothing];
        }

        if (targetTypes[0] == typeof(decimal) && decimal.TryParse(text, NumberStyles.Float, culture, out var decimalValue))
        {
            return [decimalValue, Binding.DoNothing];
        }

        return [text, Binding.DoNothing];
    }
}
