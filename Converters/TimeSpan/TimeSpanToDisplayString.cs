using System.Globalization;

namespace ClaudeAccountSwitcher.Converters;

/// <summary>
/// TimeSpan을 HH:MM:SS 형식의 문자열로 표시합니다.
/// </summary>
public sealed class TimeSpanToDisplayString : ValueConverterMarkupExtension<TimeSpanToDisplayString>
{
    public override object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var (hours, minutes, seconds) = value is TimeSpan span
            ? ((int)span.TotalHours, span.Minutes, span.Seconds)
            : (0, 0, 0);

        return $"{hours:D2}:{minutes:D2}:{seconds:D2}";
    }
}
