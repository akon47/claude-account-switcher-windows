using System.Globalization;
using System.Windows;

namespace ClaudeAccountSwitcher.Converters;

/// <summary>
/// double 픽셀 값과 절대(Absolute) GridLength 사이를 양방향으로 변환한다.
/// </summary>
public class DoubleToGridLengthConverter : ValueConverterMarkupExtension<DoubleToGridLengthConverter>
{
    public override object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is double pixels ? new GridLength(pixels) : GridLength.Auto;

    public override object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is GridLength { IsAbsolute: true } length)
        {
            return length.Value;
        }

        throw new NotSupportedException();
    }
}
