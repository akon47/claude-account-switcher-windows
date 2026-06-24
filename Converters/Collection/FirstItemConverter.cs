using System.Collections;
using System.Globalization;

namespace ClaudeAccountSwitcher.Converters;

/// <summary>
/// 열거 가능한 값이면 첫 번째 항목을(비어 있으면 null), 그 외에는 값 자체를 반환한다.
/// </summary>
public class FirstItemConverter : ValueConverterMarkupExtension<FirstItemConverter>
{
    public override object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null)
        {
            return null;
        }

        if (value is IEnumerable enumerable)
        {
            foreach (var item in enumerable)
            {
                return item;
            }

            return null;
        }

        return value;
    }
}
