using System.Globalization;

namespace ClaudeAccountSwitcher.Converters;

public class IsDebugModeConverter : ValueConverterMarkupExtension<IsDebugModeConverter>
{
    public override object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
#if DEBUG
        true;
#else
        false;
#endif
}
