using System.Collections;
using System.Windows;

namespace ClaudeAccountSwitcher.Converters;

/// <summary>Truthy/Falsy 판정을 위한 유틸리티.</summary>
internal static class Booleany
{
    /// <summary>지정한 값이 truthy 값인지 여부를 반환한다.</summary>
    /// <remarks>
    /// truthy/falsy 개념은 JavaScript에서 비롯되었다.
    /// falsy로 간주되는 값: null, 0, false, NaN, DBNull, 빈 문자열, 빈 컬렉션.
    /// </remarks>
    /// <param name="value">평가할 값.</param>
    /// <returns>truthy이면 true, 아니면 false.</returns>
    public static bool IsTruthy(object? value) => value switch
    {
        null => false,
        string s => s.Length != 0,
        bool b => b,
        int i => i != 0,
        double d => d != 0d && !double.IsNaN(d),
        long l => l != 0L,
        ICollection collection => collection.Count != 0,
        IEnumerable sequence => sequence.Cast<object>().Any(),
        DBNull => false,
        sbyte sb => sb != 0,
        byte by => by != 0,
        short sh => sh != 0,
        ushort us => us != 0,
        uint ui => ui != 0,
        float f => f != 0f,
        decimal m => m != 0m,
        Visibility visibility => visibility == Visibility.Visible,
        TimeSpan span => span != TimeSpan.Zero,
        _ => true,
    };

    /// <summary>지정한 값이 falsy 값인지 여부를 반환한다.</summary>
    /// <seealso cref="IsTruthy"/>
    /// <param name="value">평가할 값.</param>
    /// <returns>falsy이면 true, 아니면 false.</returns>
    public static bool IsFalsy(object? value) => !IsTruthy(value);
}
