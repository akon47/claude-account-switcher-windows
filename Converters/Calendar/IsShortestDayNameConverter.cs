using System.Globalization;

namespace ClaudeAccountSwitcher.Converters;

public class IsShortestDayNameConverter : ValueConverterMarkupExtension<IsShortestDayNameConverter>
{
    public override object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not null &&
        parameter is DayOfWeek dayOfWeek &&
        value.ToString() == culture.DateTimeFormat.GetShortestDayName(dayOfWeek);
}
