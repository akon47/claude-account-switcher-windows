using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace ClaudeAccountSwitcher.Behaviors;

/// <summary>
/// 로드될 때 포커스를 주고, 대상이 TextBox면 전체 선택까지 하는 첨부 동작.
/// 코드비하인드 Loaded 핸들러(InputBox.Focus()/SelectAll())를 대체한다.
/// </summary>
public static class FocusOnLoadBehavior
{
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(FocusOnLoadBehavior), new PropertyMetadata(false, OnIsEnabledChanged));

    public static bool GetIsEnabled(DependencyObject o) => (bool)o.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(DependencyObject o, bool value) => o.SetValue(IsEnabledProperty, value);

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement fe) return;

        fe.Loaded -= OnLoaded;
        if (e.NewValue is true) fe.Loaded += OnLoaded;
    }

    private static void OnLoaded(object sender, RoutedEventArgs e)
    {
        var fe = (FrameworkElement)sender;
        // 템플릿/레이아웃이 자리잡은 뒤에 포커스를 준다(템플릿 트리거로 보이게 되는 컨트롤 대응).
        fe.Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            fe.Focus();
            if (fe is TextBox tb) tb.SelectAll();
        });
    }
}
