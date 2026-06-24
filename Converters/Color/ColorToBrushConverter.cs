using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace ClaudeAccountSwitcher.Converters;

/// <summary>
/// Color과 고정된 SolidColorBrush 사이를 상호 변환합니다.
/// </summary>
public class ColorToBrushConverter : ValueConverterMarkupExtension<ColorToBrushConverter>
{
    public override object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not Color color)
        {
            return DependencyProperty.UnsetValue;
        }

        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    public override object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is SolidColorBrush brush ? brush.Color : DependencyProperty.UnsetValue;
}
