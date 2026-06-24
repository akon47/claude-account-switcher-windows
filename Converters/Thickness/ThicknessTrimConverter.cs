using System.Globalization;
using System.Windows;

namespace ClaudeAccountSwitcher.Converters;

/// <summary>
/// 파라미터에 나열된 변만 원래 Thickness 값을 유지하고 나머지는 0으로 만든다.
/// </summary>
public class ThicknessTrimConverter : ValueConverterMarkupExtension<ThicknessTrimConverter>
{
    public override object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not Thickness thickness)
        {
            return default(Thickness);
        }

        if (parameter is null)
        {
            return thickness;
        }

        var sides = parameter.ToString()?.Split(',');
        if (sides is not { Length: > 0 })
        {
            return default(Thickness);
        }

        var trimmed = default(Thickness);
        foreach (var side in sides)
        {
            switch (side)
            {
                case nameof(Thickness.Left): trimmed.Left = thickness.Left; break;
                case nameof(Thickness.Top): trimmed.Top = thickness.Top; break;
                case nameof(Thickness.Right): trimmed.Right = thickness.Right; break;
                case nameof(Thickness.Bottom): trimmed.Bottom = thickness.Bottom; break;
                case "-" + nameof(Thickness.Left): trimmed.Left = -thickness.Left; break;
                case "-" + nameof(Thickness.Top): trimmed.Top = -thickness.Top; break;
                case "-" + nameof(Thickness.Right): trimmed.Right = -thickness.Right; break;
                case "-" + nameof(Thickness.Bottom): trimmed.Bottom = -thickness.Bottom; break;
            }
        }

        return trimmed;
    }
}
