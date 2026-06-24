using System.Windows;

namespace ClaudeAccountSwitcher.Behaviors;

/// <summary>
/// VM의 bool? 값을 모달 창의 DialogResult로 밀어넣는 첨부 동작.
/// DialogResult를 설정하면 ShowDialog로 띄운 창이 자동으로 닫힌다.
/// 코드비하인드 Ok_Click(= DialogResult 설정)을 대체한다. (창에 b:DialogResultBehavior.Result="{Binding ...}")
/// </summary>
public static class DialogResultBehavior
{
    public static readonly DependencyProperty ResultProperty = DependencyProperty.RegisterAttached(
        "Result", typeof(bool?), typeof(DialogResultBehavior), new PropertyMetadata(null, OnResultChanged));

    public static bool? GetResult(DependencyObject o) => (bool?)o.GetValue(ResultProperty);

    public static void SetResult(DependencyObject o, bool? value) => o.SetValue(ResultProperty, value);

    private static void OnResultChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Window window || e.NewValue is not bool result) return;
        try { window.DialogResult = result; }
        catch (InvalidOperationException) { /* ShowDialog로 띄운 모달이 아니면 DialogResult 설정이 막힌다 — 무시 */ }
    }
}
