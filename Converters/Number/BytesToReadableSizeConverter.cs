using System.Globalization;

namespace ClaudeAccountSwitcher.Converters;

public class BytesToReadableSizeConverter : ValueConverterMarkupExtension<BytesToReadableSizeConverter>
{
    private static readonly string[] SizeSuffixes = ["bytes", "KB", "MB", "GB", "TB", "PB"];

    public override object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not long byteCount)
        {
            return "–";
        }

        if (byteCount < 0)
        {
            return "-" + Convert(-byteCount, targetType, parameter, culture);
        }

        var unitIndex = 0;
        decimal size = byteCount;
        while (size >= 1024 && unitIndex < SizeSuffixes.Length - 1)
        {
            size /= 1024;
            unitIndex++;
        }

        var format = parameter is string s && int.TryParse(s, out var decimals)
            ? decimals <= 0 ? "0" : $"0.{new string('0', decimals)}"
            : "0.##";

        return $"{size.ToString(format, culture)} {SizeSuffixes[unitIndex]}";
    }
}
