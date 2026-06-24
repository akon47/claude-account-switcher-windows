using System.Globalization;

namespace ClaudeAccountSwitcher.Converters;

/// <summary>
/// bool을 문자열로 매핑한다. parameter가 "켜짐,꺼짐" 형태(콤마 구분, 2개 이상)면
/// 각각의 텍스트를, 아니면 bool.ToString()을 돌려준다.
/// </summary>
public class BoolToStringConverter : ValueConverterMarkupExtension<BoolToStringConverter>
{
    public override object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not bool isChecked)
        {
            return null;
        }

        return TrySplit(parameter, out var onText, out var offText)
            ? (isChecked ? onText : offText)
            : isChecked.ToString();
    }

    public override object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => TrySplit(parameter, out var onText, out _) && value?.ToString() == onText;

    private static bool TrySplit(object? parameter, out string onText, out string offText)
    {
        onText = offText = string.Empty;

        if (parameter is not string text)
        {
            return false;
        }

        var parts = text.Split(',');
        if (parts.Length < 2)
        {
            return false;
        }

        onText = parts[0].Trim();
        offText = parts[1].Trim();
        return true;
    }
}
