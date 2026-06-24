using System.Globalization;
using System.Windows.Data;

namespace ClaudeAccountSwitcher.Converters;

/// <summary>
/// 값의 런타임 타입이 파라미터로 지정한 타입과 정확히 일치하면 true를 반환한다.
/// </summary>
[ValueConversion(typeof(object), typeof(bool))]
public class IsTypeConverter : ValueConverterMarkupExtension<IsTypeConverter>
{
    public override object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is not null && parameter is Type expected && value.GetType() == expected;
}
