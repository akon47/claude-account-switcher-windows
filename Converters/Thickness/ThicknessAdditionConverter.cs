using System.Globalization;
using System.Windows;

namespace ClaudeAccountSwitcher.Converters;

/// <summary>
/// 여러 Thickness를 변별로 더하거나, 단일 Thickness에 파라미터 오프셋을 더한다.
/// </summary>
public class ThicknessAdditionConverter : ValueAndMultiValueConverterMarkupExtension<ThicknessAdditionConverter>
{
    public override object? Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values is not { Length: > 0 })
        {
            return null;
        }

        if (Array.Exists(values, v => v == DependencyProperty.UnsetValue))
        {
            return DependencyProperty.UnsetValue;
        }

        if (values.Length == 1)
        {
            return values[0];
        }

        var sum = default(Thickness);
        foreach (var value in values)
        {
            if (value is Thickness t)
            {
                sum.Left += t.Left;
                sum.Top += t.Top;
                sum.Right += t.Right;
                sum.Bottom += t.Bottom;
            }
        }

        return sum;
    }

    public override object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not Thickness thickness)
        {
            return value;
        }

        var offset = System.Convert.ToDouble(parameter);
        return new Thickness(
            thickness.Left + offset,
            thickness.Top + offset,
            thickness.Right + offset,
            thickness.Bottom + offset);
    }
}
