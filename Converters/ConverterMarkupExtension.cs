using System.Globalization;
using System.Windows.Data;
using System.Windows.Markup;

namespace ClaudeAccountSwitcher.Converters;

public abstract class ConverterMarkupExtension<T> : MarkupExtension
    where T : ConverterMarkupExtension<T>
{
    private static readonly Lazy<T> SharedInstance =
        new(() => (T)Activator.CreateInstance(typeof(T), nonPublic: true)!);

#pragma warning disable CA1000 // Do not declare static members on generic types
    public static T Instance => SharedInstance.Value;
#pragma warning restore CA1000 // Do not declare static members on generic types

    public override object ProvideValue(IServiceProvider serviceProvider) => SharedInstance.Value;
}

public abstract class ValueConverterMarkupExtension<T> : ConverterMarkupExtension<T>, IValueConverter
    where T : ConverterMarkupExtension<T>
{
    public virtual object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotImplementedException();

    public virtual object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotImplementedException();
}

public abstract class MultiValueConverterMarkupExtension<T> : ConverterMarkupExtension<T>, IMultiValueConverter
    where T : ConverterMarkupExtension<T>
{
    public virtual object? Convert(object[] values, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotImplementedException();

    public virtual object[]? ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotImplementedException();
}

public abstract class ValueAndMultiValueConverterMarkupExtension<T> :
    ConverterMarkupExtension<T>,
    IValueAndMultiValueConverter
    where T : ConverterMarkupExtension<T>
{
    public virtual object? Convert(object[] values, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotImplementedException();

    public virtual object[]? ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotImplementedException();

    public virtual object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotImplementedException();

    public virtual object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotImplementedException();
}
