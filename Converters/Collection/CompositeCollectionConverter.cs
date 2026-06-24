using System.Collections;
using System.Globalization;
using System.Windows.Data;

namespace ClaudeAccountSwitcher.Converters;

/// <summary>
/// 여러 입력을 하나의 CompositeCollection으로 합친다.
/// 컬렉션은 CollectionContainer로 펼치고, 단일 항목은 그대로 추가한다(null은 건너뜀).
/// </summary>
public class CompositeCollectionConverter : MultiValueConverterMarkupExtension<CompositeCollectionConverter>
{
    public override object? Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        var composite = new CompositeCollection();
        foreach (var value in values)
        {
            switch (value)
            {
                case null:
                    continue;
                case IEnumerable enumerable:
                    composite.Add(new CollectionContainer { Collection = enumerable });
                    break;
                default:
                    composite.Add(value);
                    break;
            }
        }

        return composite;
    }
}
