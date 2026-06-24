using System.Globalization;
using System.Windows.Data;

namespace ClaudeAccountSwitcher.Converters;

/// <summary>
/// double 값이 NaN이면 true를 반환한다.
/// </summary>
[ValueConversion(typeof(double), typeof(bool))]
public class IsNaNConverter : ValueConverterMarkupExtension<IsNaNConverter>
{
    public override object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is double casted && double.IsNaN(casted);
}
