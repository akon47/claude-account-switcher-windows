using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace ClaudeAccountSwitcher.Converters;

// 단일 값: "truthy" 여부에 따라 Visibility / bool 로 매핑한다.

[ValueConversion(typeof(object), typeof(Visibility))]
public class TruthyToCollapsedConverter : ValueConverterMarkupExtension<TruthyToCollapsedConverter>
{
    public override object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Booleany.IsTruthy(value) ? Visibility.Collapsed : Visibility.Visible;
}

[ValueConversion(typeof(object), typeof(bool))]
public class TruthyToFalseConverter : ValueConverterMarkupExtension<TruthyToFalseConverter>
{
    public override object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => !Booleany.IsTruthy(value);
}

[ValueConversion(typeof(object), typeof(Visibility))]
public class TruthyToHiddenConverter : ValueConverterMarkupExtension<TruthyToHiddenConverter>
{
    public override object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Booleany.IsTruthy(value) ? Visibility.Hidden : Visibility.Visible;
}

[ValueConversion(typeof(object), typeof(bool))]
public class TruthyToTrueConverter : ValueConverterMarkupExtension<TruthyToTrueConverter>
{
    public override object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Booleany.IsTruthy(value);
}

[ValueConversion(typeof(object), typeof(Visibility))]
public class TruthyToVisibleConverter : ValueConverterMarkupExtension<TruthyToVisibleConverter>
{
    public override object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Booleany.IsTruthy(value) ? Visibility.Visible : Visibility.Collapsed;
}

// 다중 값(All-): 모든 값이 truthy일 때만 참으로 본다.

public class AllTruthyToCollapsedConverter : MultiValueConverterMarkupExtension<AllTruthyToCollapsedConverter>
{
    public override object? Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(values);
        return Array.TrueForAll(values, Booleany.IsTruthy) ? Visibility.Collapsed : Visibility.Visible;
    }
}

public class AllTruthyToFalseConverter : MultiValueConverterMarkupExtension<AllTruthyToFalseConverter>
{
    public override object? Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(values);
        return !Array.TrueForAll(values, Booleany.IsTruthy);
    }
}

public class AllTruthyToHiddenConverter : MultiValueConverterMarkupExtension<AllTruthyToHiddenConverter>
{
    public override object? Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(values);
        return Array.TrueForAll(values, Booleany.IsTruthy) ? Visibility.Hidden : Visibility.Visible;
    }
}

public class AllTruthyToTrueConverter : MultiValueConverterMarkupExtension<AllTruthyToTrueConverter>
{
    public override object? Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
        => Array.TrueForAll(values!, Booleany.IsTruthy);
}

public class AllTruthyToVisibleConverter : MultiValueConverterMarkupExtension<AllTruthyToVisibleConverter>
{
    public override object? Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(values);
        return Array.TrueForAll(values, Booleany.IsTruthy) ? Visibility.Visible : Visibility.Collapsed;
    }
}

// 다중 값(Any-): 하나라도 truthy면 참으로 본다.

public class AnyTruthyToCollapsedConverter : MultiValueConverterMarkupExtension<AnyTruthyToCollapsedConverter>
{
    public override object? Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(values);
        return Array.Exists(values, Booleany.IsTruthy) ? Visibility.Collapsed : Visibility.Visible;
    }
}

public class AnyTruthyToFalseConverter : MultiValueConverterMarkupExtension<AnyTruthyToFalseConverter>
{
    public override object? Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(values);
        return !Array.Exists(values, Booleany.IsTruthy);
    }
}

public class AnyTruthyToHiddenConverter : MultiValueConverterMarkupExtension<AnyTruthyToHiddenConverter>
{
    public override object? Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(values);
        return Array.Exists(values, Booleany.IsTruthy) ? Visibility.Hidden : Visibility.Visible;
    }
}

public class AnyTruthyToTrueConverter : MultiValueConverterMarkupExtension<AnyTruthyToTrueConverter>
{
    public override object? Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(values);
        return Array.Exists(values, Booleany.IsTruthy);
    }
}

public class AnyTruthyToVisibleConverter : MultiValueConverterMarkupExtension<AnyTruthyToVisibleConverter>
{
    public override object? Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(values);
        return Array.Exists(values, Booleany.IsTruthy) ? Visibility.Visible : Visibility.Collapsed;
    }
}
