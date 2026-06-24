using System.Globalization;

namespace ClaudeAccountSwitcher.Converters;

/// <summary>
/// 값이 0이면 true를 반환한다.
/// null 이거나 숫자가 아니면 false를 반환한다.
/// </summary>
public class IsZeroConverter : ValueConverterMarkupExtension<IsZeroConverter>
{
    public override object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value switch
        {
            double d => d == 0,
            int i => i == 0,
            long l => l == 0,
            float f => f == 0,
            decimal m => m == 0,
            _ => false,
        };
}
