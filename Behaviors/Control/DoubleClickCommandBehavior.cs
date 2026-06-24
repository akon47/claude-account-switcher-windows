using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ClaudeAccountSwitcher.Behaviors;

/// <summary>
/// 컨트롤 더블클릭 시 바인딩된 ICommand 를 실행하는 첨부 동작.
/// 코드비하인드 없이 View 에서 더블클릭을 커맨드로 연결한다.
/// </summary>
public static class DoubleClickCommandBehavior
{
    public static readonly DependencyProperty CommandProperty = DependencyProperty.RegisterAttached(
        "Command", typeof(ICommand), typeof(DoubleClickCommandBehavior),
        new PropertyMetadata(null, OnCommandChanged));

    public static readonly DependencyProperty CommandParameterProperty = DependencyProperty.RegisterAttached(
        "CommandParameter", typeof(object), typeof(DoubleClickCommandBehavior), new PropertyMetadata(null));

    public static ICommand? GetCommand(DependencyObject o) => (ICommand?)o.GetValue(CommandProperty);

    public static void SetCommand(DependencyObject o, ICommand? value) => o.SetValue(CommandProperty, value);

    public static object? GetCommandParameter(DependencyObject o) => o.GetValue(CommandParameterProperty);

    public static void SetCommandParameter(DependencyObject o, object? value) =>
        o.SetValue(CommandParameterProperty, value);

    private static void OnCommandChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Control control) return;

        control.MouseDoubleClick -= OnMouseDoubleClick;
        if (e.NewValue is ICommand) control.MouseDoubleClick += OnMouseDoubleClick;
    }

    private static void OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        var element = (DependencyObject)sender;
        var command = GetCommand(element);
        var parameter = GetCommandParameter(element);
        if (command?.CanExecute(parameter) == true) command.Execute(parameter);
    }
}
