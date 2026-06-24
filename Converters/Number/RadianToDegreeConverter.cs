using System.Globalization;

namespace ClaudeAccountSwitcher.Converters;

public sealed class RadianToDegreeConverter : ValueConverterMarkupExtension<RadianToDegreeConverter>
{
    private static double AsDouble(object? value) => value switch
    {
        double d => d,
        string s when double.TryParse(s, out var parsed) => parsed,
        _ => 0d,
    };

    public override object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => AsDouble(value) * 180.0 / Math.PI;

    public override object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => AsDouble(value) * Math.PI / 180.0;
}
