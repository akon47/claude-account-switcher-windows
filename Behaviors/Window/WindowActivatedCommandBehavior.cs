using System.Windows;
using System.Windows.Input;

namespace ClaudeAccountSwitcher.Behaviors;

/// <summary>
/// Window 가 활성화될 때 바인딩된 ICommand 를 실행하는 첨부 동작.
/// (예: 창이 앞으로 올 때 목록 새로고침) — 코드비하인드 Activated 핸들러를 대체한다.
/// </summary>
public static class WindowActivatedCommandBehavior
{
    public static readonly DependencyProperty CommandProperty = DependencyProperty.RegisterAttached(
        "Command", typeof(ICommand), typeof(WindowActivatedCommandBehavior),
        new PropertyMetadata(null, OnCommandChanged));

    public static ICommand? GetCommand(DependencyObject o) => (ICommand?)o.GetValue(CommandProperty);

    public static void SetCommand(DependencyObject o, ICommand? value) => o.SetValue(CommandProperty, value);

    private static void OnCommandChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Window window) return;

        window.Activated -= OnActivated;
        if (e.NewValue is ICommand) window.Activated += OnActivated;
    }

    private static void OnActivated(object? sender, EventArgs e)
    {
        var command = GetCommand((DependencyObject)sender!);
        if (command?.CanExecute(null) == true) command.Execute(null);
    }
}
