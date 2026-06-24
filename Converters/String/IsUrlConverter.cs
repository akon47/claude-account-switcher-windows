using System.Globalization;

namespace ClaudeAccountSwitcher.Converters;

/// <summary>
/// 값이 http/https 절대 URL 형태이면 true를 반환합니다.
/// </summary>
public class IsUrlConverter : ValueConverterMarkupExtension<IsUrlConverter>
{
    public override object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is string text
            && Uri.TryCreate(text, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
}
