using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace ClaudeAccountSwitcher.Converters;

// 단일 값: "falsy" 여부에 따라 Visibility / bool 로 매핑한다.

[ValueConversion(typeof(object), typeof(Visibility))]
public class FalsyToCollapsedConverter : ValueConverterMarkupExtension<FalsyToCollapsedConverter>
{
    public override object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Booleany.IsFalsy(value) ? Visibility.Collapsed : Visibility.Visible;
}

[ValueConversion(typeof(object), typeof(bool))]
public class FalsyToFalseConverter : ValueConverterMarkupExtension<FalsyToFalseConverter>
{
    public override object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => !Booleany.IsFalsy(value);
}

[ValueConversion(typeof(object), typeof(Visibility))]
public class FalsyToHiddenConverter : ValueConverterMarkupExtension<FalsyToHiddenConverter>
{
    public override object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Booleany.IsFalsy(value) ? Visibility.Hidden : Visibility.Visible;
}

[ValueConversion(typeof(object), typeof(bool))]
public class FalsyToTrueConverter : ValueConverterMarkupExtension<FalsyToTrueConverter>
{
    public override object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Booleany.IsFalsy(value);
}

[ValueConversion(typeof(object), typeof(Visibility))]
public class FalsyToVisibleConverter : ValueConverterMarkupExtension<FalsyToVisibleConverter>
{
    public override object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Booleany.IsFalsy(value) ? Visibility.Visible : Visibility.Collapsed;
}

// 다중 값(All-): 모든 값이 falsy일 때만 참으로 본다.

public class AllFalsyToCollapsedConverter : MultiValueConverterMarkupExtension<AllFalsyToCollapsedConverter>
{
    public override object? Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(values);
        return Array.TrueForAll(values, Booleany.IsFalsy) ? Visibility.Collapsed : Visibility.Visible;
    }
}

public class AllFalsyToFalseConverter : MultiValueConverterMarkupExtension<AllFalsyToFalseConverter>
{
    public override object? Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(values);
        return !Array.TrueForAll(values, Booleany.IsFalsy);
    }
}

public class AllFalsyToHiddenConverter : MultiValueConverterMarkupExtension<AllFalsyToHiddenConverter>
{
    public override object? Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(values);
        return Array.TrueForAll(values, Booleany.IsFalsy) ? Visibility.Hidden : Visibility.Visible;
    }
}

public class AllFalsyToTrueConverter : MultiValueConverterMarkupExtension<AllFalsyToTrueConverter>
{
    public override object? Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(values);
        return Array.TrueForAll(values, Booleany.IsFalsy);
    }
}

public class AllFalsyToVisibleConverter : MultiValueConverterMarkupExtension<AllFalsyToVisibleConverter>
{
    public override object? Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(values);
        return Array.TrueForAll(values, Booleany.IsFalsy) ? Visibility.Visible : Visibility.Collapsed;
    }
}

// 다중 값(Any-): 하나라도 falsy면 참으로 본다.

public class AnyFalsyToCollapsedConverter : MultiValueConverterMarkupExtension<AnyFalsyToCollapsedConverter>
{
    public override object? Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(values);
        return Array.Exists(values, Booleany.IsFalsy) ? Visibility.Collapsed : Visibility.Visible;
    }
}

public class AnyFalsyToFalseConverter : MultiValueConverterMarkupExtension<AnyFalsyToFalseConverter>
{
    public override object? Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(values);
        return !Array.Exists(values, Booleany.IsFalsy);
    }
}

public class AnyFalsyToHiddenConverter : MultiValueConverterMarkupExtension<AnyFalsyToHiddenConverter>
{
    public override object? Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(values);
        return Array.Exists(values, Booleany.IsFalsy) ? Visibility.Hidden : Visibility.Visible;
    }
}

public class AnyFalsyToTrueConverter : MultiValueConverterMarkupExtension<AnyFalsyToTrueConverter>
{
    public override object? Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(values);
        return Array.Exists(values, Booleany.IsFalsy);
    }
}

public class AnyFalsyToVisibleConverter : MultiValueConverterMarkupExtension<AnyFalsyToVisibleConverter>
{
    public override object? Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(values);
        return Array.Exists(values, Booleany.IsFalsy) ? Visibility.Visible : Visibility.Collapsed;
    }
}
