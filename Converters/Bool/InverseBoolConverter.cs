using System.Globalization;
using System.Windows.Data;

namespace ClaudeAccountSwitcher.Converters;

/// <summary>bool 값을 반전한다. bool이 아니면 변환은 입력 그대로, 역변환은 null.</summary>
[ValueConversion(typeof(bool), typeof(bool))]
public class InverseBoolConverter : ValueConverterMarkupExtension<InverseBoolConverter>
{
    public override object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool flag ? !flag : value;

    public override object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool flag ? !flag : null;
}
