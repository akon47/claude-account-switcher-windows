using System.Globalization;

namespace ClaudeAccountSwitcher.Converters;

/// <summary>
/// 단일 값은 파라미터와, 다중 값은 서로 모두 같은지 비교해 true/false를 반환한다.
/// </summary>
public class ValueEqualsConverter : ValueAndMultiValueConverterMarkupExtension<ValueEqualsConverter>
{
    public override object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Equals(value, parameter);

    public override object? Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values is not { Length: > 0 })
        {
            return false;
        }

        var first = values[0];
        return values.All(v => Equals(v, first));
    }
}
