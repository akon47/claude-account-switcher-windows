using System.Globalization;
using System.IO;

namespace ClaudeAccountSwitcher.Converters;

/// <summary>
/// 경로 문자열에서 파일 이름만 추출합니다.
/// </summary>
public class FileNameConverter : ValueConverterMarkupExtension<FileNameConverter>
{
    public override object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is string path && !string.IsNullOrWhiteSpace(path)
            ? Path.GetFileName(path)
            : null;
}
