using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace ClaudeAccountSwitcher.Converters;

/// <summary>
/// 글꼴 이름 문자열과 FontFamily 사이를 상호 변환합니다.
/// </summary>
public class FontFamilyNameToFontFamilyConverter : ValueConverterMarkupExtension<FontFamilyNameToFontFamilyConverter>
{
    public override object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is string name ? new FontFamily(name) : DependencyProperty.UnsetValue;

    public override object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is FontFamily family ? family.Source : DependencyProperty.UnsetValue;
}
