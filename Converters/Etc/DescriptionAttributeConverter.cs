using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Windows;

namespace ClaudeAccountSwitcher.Converters;

public class DescriptionAttributeConverter : ValueConverterMarkupExtension<DescriptionAttributeConverter>
{
    public override object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null)
        {
            return null;
        }

        if (value is Enum e &&
            e.GetType().GetField(e.ToString())?.GetCustomAttribute<DescriptionAttribute>(true) is { } description)
        {
            return description.Description;
        }

        return DependencyProperty.UnsetValue;
    }
}
