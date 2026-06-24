using System.Globalization;
using System.Windows.Input;

namespace ClaudeAccountSwitcher.Converters;

public class StringKeyGestureConverter : ValueConverterMarkupExtension<StringKeyGestureConverter>
{
    private static readonly KeyGestureConverter Converter = new();

    public override object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string s || string.IsNullOrWhiteSpace(s))
        {
            return null;
        }

        try
        {
            return Converter.ConvertFromString(s) as KeyGesture;
        }
        catch
        {
            return null;
        }
    }

    public override object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not KeyGesture gesture)
        {
            return null;
        }

        try
        {
            return Converter.ConvertToString(gesture);
        }
        catch
        {
            return null;
        }
    }
}
