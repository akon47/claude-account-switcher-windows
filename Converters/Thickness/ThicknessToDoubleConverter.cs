using System.Globalization;
using System.Windows;

namespace ClaudeAccountSwitcher.Converters;

/// <summary>
/// 파라미터로 지정한 변(들)을 골라 Thickness에서 double 값을 뽑아낸다.
/// </summary>
public class ThicknessToDoubleConverter : ValueConverterMarkupExtension<ThicknessToDoubleConverter>
{
    private const string Left = nameof(Thickness.Left);
    private const string Top = nameof(Thickness.Top);
    private const string Right = nameof(Thickness.Right);
    private const string Bottom = nameof(Thickness.Bottom);

    public override object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not Thickness thickness)
        {
            return 0d;
        }

        return parameter?.ToString() switch
        {
            Left => thickness.Left,
            Top => thickness.Top,
            Right => thickness.Right,
            Bottom => thickness.Bottom,
            "-" + Left => -thickness.Left,
            "-" + Top => -thickness.Top,
            "-" + Right => -thickness.Right,
            "-" + Bottom => -thickness.Bottom,
            Left + Right => thickness.Left + thickness.Right,
            Top + Bottom => thickness.Top + thickness.Bottom,
            "-" + Left + Right => -thickness.Left - thickness.Right,
            "-" + Top + Bottom => -thickness.Top - thickness.Bottom,
            "Max" => Math.Max(Math.Max(thickness.Left, thickness.Right), Math.Max(thickness.Top, thickness.Bottom)),
            _ => 0d,
        };
    }
}
