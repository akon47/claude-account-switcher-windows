using System.ComponentModel;
using System.Windows;
using System.Windows.Input;

namespace ClaudeAccountSwitcher.Behaviors;

/// <summary>
/// Window 가 닫힐 때 바인딩된 ICommand 를 실행하는 첨부 동작(닫힘을 막지는 않는다).
/// 예: 진행률 창을 닫으면 다운로드를 취소. 코드비하인드 Closing 핸들러를 대체한다.
/// </summary>
public static class WindowClosingCommandBehavior
{
    public static readonly DependencyProperty CommandProperty = DependencyProperty.RegisterAttached(
        "Command", typeof(ICommand), typeof(WindowClosingCommandBehavior),
        new PropertyMetadata(null, OnCommandChanged));

    public static ICommand? GetCommand(DependencyObject o) => (ICommand?)o.GetValue(CommandProperty);

    public static void SetCommand(DependencyObject o, ICommand? value) => o.SetValue(CommandProperty, value);

    private static void OnCommandChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Window window) return;

        window.Closing -= OnClosing;
        if (e.NewValue is ICommand) window.Closing += OnClosing;
    }

    private static void OnClosing(object? sender, CancelEventArgs e)
    {
        var command = GetCommand((DependencyObject)sender!);
        if (command?.CanExecute(null) == true) command.Execute(null);
    }
}
