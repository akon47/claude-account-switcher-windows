using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Markup;

namespace ClaudeAccountSwitcher.Converters;

/// <summary>여러 <see cref="IValueConverter"/>를 순서대로 이어 실행하는 컨버터 체인.</summary>
[ContentProperty("Converters")]
public class ValueConverterChain : IValueConverter
{
    public ObservableCollection<IValueConverter> Converters { get; } = [];

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        RunChain(value, targetType, parameter, culture, reversed: false);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        RunChain(value, targetType, parameter, culture, reversed: true);

    private object? RunChain(
        object? value,
        Type finalTargetType,
        object? parameter,
        CultureInfo culture,
        bool reversed)
    {
        var pipeline = (reversed ? Converters.Reverse() : Converters).ToList();
        if (pipeline.Count == 0)
        {
            return value;
        }

        var lastConverter = pipeline[^1];
        var output = value;

        foreach (var converter in pipeline)
        {
            var stepTargetType = converter == lastConverter
                ? finalTargetType
                : IntermediateTargetType(converter, reversed);

            output = reversed
                ? converter.ConvertBack(output, stepTargetType, parameter, culture)
                : converter.Convert(output, stepTargetType, parameter, culture);

            if (output == DependencyProperty.UnsetValue || output == Binding.DoNothing)
            {
                break;
            }
        }

        return output;
    }

    private static Type? IntermediateTargetType(IValueConverter converter, bool reversed)
    {
        var conversion = converter.GetType().GetCustomAttributes(true)
            .OfType<ValueConversionAttribute>()
            .SingleOrDefault();

        return reversed ? conversion?.SourceType : conversion?.TargetType;
    }
}
