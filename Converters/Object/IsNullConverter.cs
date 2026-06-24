using System.Globalization;
using System.Windows;

namespace ClaudeAccountSwitcher.Converters;

/// <summary>
/// 값이 null이면 true를 반환한다.
/// </summary>
public class IsNullConverter : ValueConverterMarkupExtension<IsNullConverter>
{
    public override object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is null;
}

/// <summary>
/// 하나라도 null이 있으면 Collapsed, 모두 채워져 있으면 Visible을 반환한다.
/// </summary>
public class AnyNullToCollapsedConverter : MultiValueConverterMarkupExtension<AnyNullToCollapsedConverter>
{
    public override object? Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(values);

        return values.Any(v => v is null) ? Visibility.Collapsed : Visibility.Visible;
    }
}
