using System.Globalization;

namespace ClaudeAccountSwitcher.Converters;

/// <summary>
/// 값이 null이 아니면 true를 반환한다.
/// </summary>
public class IsNotNullConverter : ValueConverterMarkupExtension<IsNotNullConverter>
{
    public override object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is not null;
}
