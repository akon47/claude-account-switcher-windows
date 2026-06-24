using System.Globalization;

namespace ClaudeAccountSwitcher.Converters;

/// <summary>
/// TimePicker 컨트롤용 시간 변환기.
/// ConverterParameter 값에 따라 포맷을 선택한다: h→hh, H→HH, m→mm, s→ss, 그 외→tt.
/// </summary>
public class TimePickerTimeConverter : ValueConverterMarkupExtension<TimePickerTimeConverter>
{
    public override object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var time = value as DateTime? ?? DateTime.Today;

        var format = parameter?.ToString() switch
        {
            "h" => "hh",
            "H" => "HH",
            "m" => "mm",
            "s" => "ss",
            _ => "tt",
        };

        return time.ToString(format);
    }
}
