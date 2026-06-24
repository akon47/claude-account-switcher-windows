using System.Globalization;

namespace ClaudeAccountSwitcher.Converters;

public class IsDirtyToWindowTitleConverter : ValueAndMultiValueConverterMarkupExtension<IsDirtyToWindowTitleConverter>
{
    public override object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        BuildTitle(value as bool?, parameter);

    public override object? Convert(object[] values, Type targetType, object? parameter, CultureInfo culture) =>
        BuildTitle(values[0] as bool?, values[1]) ?? string.Empty;

    private static string BuildTitle(bool? isDirty, object? title) =>
        isDirty == true ? $"{title}*" : $"{title}";
}
